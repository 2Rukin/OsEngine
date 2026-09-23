/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using OsEngine.Logging;
using OsEngine.Market;

namespace OsEngine.OsData.OrderFlow
{
    /// <summary>Synchronous worker callbacks; input is hashed first, each selected tick is paced before it changes accumulators.</summary>
    internal interface IOrderFlowReplayObserver
    {
        void InputReady(OrderFlowTickInput input);
        void BeforeTick(DateTime time, CancellationToken cancellationToken);
        void TickProcessed(OrderFlowRunContext context, OrderFlowBucket pending, OrderFlowDeal tick);
    }

    /// <summary>Read-only by convention prefix payload, owned by the UI after publication.</summary>
    internal sealed class OrderFlowReplayFrame
    {
        public OrderFlowResearchResult Result { get; set; }
        public DateTime Time { get; set; }
        public long TickCount { get; set; }
        public long SourceSequence { get; set; }
        public bool Complete { get; set; }

        /// <summary>Copies collections and mutable accumulators; finalized DTOs are shared read-only and contain no future suffix.</summary>
        internal static OrderFlowReplayFrame Capture(OrderFlowRunContext context, OrderFlowBucket pending, OrderFlowDeal tick)
        {
            OrderFlowResearchResult result = new OrderFlowResearchResult
            {
                DeltaCalculated = context.Result.DeltaCalculated,
                CloudCalculated = context.Result.CloudCalculated,
                Cloud2Calculated = context.Result.Cloud2Calculated,
                Bars = context.BarAggregator.Snapshot(pending),
                Clouds = context.CloudAccumulator?.Snapshot() ?? new List<OrderFlowCloud>(),
                Clouds2 = context.CloudAccumulator2?.Snapshot() ?? new List<OrderFlowCloud>(),
                Candidates = new List<OrderFlowCandidate>(context.Result.Candidates),
                Labels = new List<OrderFlowMarketPathLabel>(context.Result.Labels)
            };
            return new OrderFlowReplayFrame { Result = result, Time = tick.Time,
                TickCount = context.Result.Quality.DealCount, SourceSequence = tick.SourceSequence };
        }
    }

    /// <summary>One background tick playback with a replaceable single-frame mailbox and cancellation-aware pacing.</summary>
    /// <remarks>
    /// Owner supplies the immutable completed request and expected raw SHA. Re-reads the pinned file through the same engine,
    /// without exporting. UI polls frames; no worker uses the dispatcher. Dispose cancels without joining or blocking the UI.
    /// Prefix frames are provisional until the full file is validated. Clock controls are thread-safe.
    /// </remarks>
    internal sealed class OrderFlowReplaySession : IOrderFlowReplayObserver, IDisposable
    {
        #region Lifetime and mailbox

        private readonly object _gate = new object();
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private readonly OrderFlowResearchRequest _request;
        private readonly string _expectedHash;
        private readonly Stopwatch _publication = Stopwatch.StartNew();
        private OrderFlowReplayFrame _latest;
        private bool _finished;
        private Exception _error;
        private bool _published;
        private long _lastSequence;
        private OrderFlowRunContext _lastContext;
        private OrderFlowBucket _lastPending;
        private OrderFlowDeal _lastTick;
        private bool _needsFrame;
        private bool _pauseAcknowledged;

        public OrderFlowReplayClock Clock { get; private set; }
        /// <summary>Known-before-playback sizing reference; one when Cloud calculation is disabled.</summary>
        public decimal CloudReferenceVolume { get; private set; }
        /// <summary>Known-before-playback reference for the second independent Cloud layer, or one when disabled.</summary>
        public decimal Cloud2ReferenceVolume { get; private set; }
        internal bool PausedAtBoundary { get { lock (_gate) { return _pauseAcknowledged; } } }
        public bool Finished { get { lock (_gate) { return _finished; } } }
        public Exception Error { get { lock (_gate) { return _error; } } }

        public OrderFlowReplaySession(OrderFlowResearchRequest request, string expectedHash, double speed, bool skipGaps)
        {
            _request = request ?? throw new ArgumentNullException(nameof(request));
            _expectedHash = expectedHash ?? throw new ArgumentNullException(nameof(expectedHash));
            CloudReferenceVolume = request.Cloud?.MinimumSumVolume ?? 1;
            Cloud2ReferenceVolume = request.Cloud2?.MinimumSumVolume ?? 1;
            Clock = new OrderFlowReplayClock(speed, skipGaps);
        }

        /// <summary>Starts exactly once, after the owner has installed UI state.</summary>
        public void Start()
        {
            lock (_gate)
            {
                if (_started || _finished) { throw new InvalidOperationException("Replay session already started or closed."); }
                Thread worker = new Thread(Run) { IsBackground = true, Name = "OrderFlowVisualReplay" };
                worker.Start();
                _started = true;
            }
        }

        private bool _started;

        public OrderFlowReplayFrame TakeFrame() { return TakeFrame(out bool ignored); }

        /// <summary>Atomically takes the latest frame and its worker pause acknowledgment; the UI paints before enabling Step.</summary>
        public OrderFlowReplayFrame TakeFrame(out bool pausedAtBoundary)
        {
            lock (_gate)
            {
                pausedAtBoundary = _pauseAcknowledged;
                OrderFlowReplayFrame frame = _latest;
                _latest = null;
                return frame;
            }
        }

        /// <summary>Requests pause/resume. An already admitted tick finishes before the worker acknowledges its pause.</summary>
        public void SetPaused(bool paused)
        {
            lock (_gate) { _pauseAcknowledged = false; Clock.SetPaused(paused); }
        }

        /// <summary>Admits one physical tick only after the worker has published the last prefix and acknowledged pause.</summary>
        /// <returns>False while running, preparing, processing an admitted tick or finished; no permit is added in those states.</returns>
        public bool TryStep()
        {
            lock (_gate)
            {
                if (!_pauseAcknowledged || !Clock.Paused || _finished) { return false; }
                _pauseAcknowledged = false;
                Clock.Step();
                return true;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_finished) { return; }
                _cancellation.Cancel();
                if (!_started) { _finished = true; _cancellation.Dispose(); }
                _latest = null;
            }
        }

        #endregion

        #region Worker callbacks

        public void InputReady(OrderFlowTickInput input)
        {
            if (!string.Equals(_expectedHash, input.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Tick file changed. Run research again before replay.");
            }
        }

        public void BeforeTick(DateTime time, CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lock (_gate)
                {
                    if (Clock.TryAdvanceTo(time)) { _pauseAcknowledged = false; break; }
                }
                PublishIfDue();
                lock (_gate) { _pauseAcknowledged = Clock.WaitingForStep && !_needsFrame; }
                if (cancellationToken.WaitHandle.WaitOne(10)) { cancellationToken.ThrowIfCancellationRequested(); }
            }
            cancellationToken.ThrowIfCancellationRequested();
        }

        public void TickProcessed(OrderFlowRunContext context, OrderFlowBucket pending, OrderFlowDeal tick)
        {
            _lastSequence = tick.SourceSequence;
            _lastContext = context;
            _lastPending = pending;
            _lastTick = tick;
            _needsFrame = true;
            PublishIfDue();
        }

        private void PublishIfDue()
        {
            if (!_needsFrame || (_published && !Clock.Paused && _publication.ElapsedMilliseconds < 200)) { return; }
            OrderFlowReplayFrame frame = OrderFlowReplayFrame.Capture(_lastContext, _lastPending, _lastTick);
            lock (_gate) { _latest = frame; }
            _publication.Restart();
            _published = true;
            _needsFrame = false;
        }

        private void Run()
        {
            try
            {
                OrderFlowResearchResult result = new OrderFlowResearchEngine().Run(_request, _cancellation.Token, this);
                if (!result.Quality.ResearchAccepted) { throw new InvalidDataException("Replay input rejected; run research again and inspect quality reasons."); }
                lock (_gate)
                {
                    if (!_cancellation.IsCancellationRequested)
                    {
                        _latest = new OrderFlowReplayFrame { Result = result, Time = result.Quality.LastDealTime.Value,
                            TickCount = result.Quality.DealCount, SourceSequence = _lastSequence, Complete = true };
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                lock (_gate) { _error = error; _latest = null; }
                ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error);
            }
            finally
            {
                _lastContext = null;
                _lastPending = null;
                _lastTick = null;
                lock (_gate) { _finished = true; _cancellation.Dispose(); }
            }
        }

        #endregion
    }
}
