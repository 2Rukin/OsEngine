/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.Logging;
using OsEngine.Market;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace OsEngine.OsData.OrderFlow.Calibration
{
    /// <summary>Forward-only saved-evidence cursor. Every catalog row is consumed once as completion becomes known, including equal-time source ordering.</summary>
    /// <remarks>No raw ticks, future labels or historical-final lookahead enter the visible prefix; memory is bounded per layer.</remarks>
    internal sealed class CalibrationReplayCursor : IDisposable
    {
        private sealed class Layer : IDisposable
        {
            internal CloudRule Rule;
            internal CalibrationRun Run;
            internal IEnumerator<CalibrationEvent> Reader;
            internal bool HasNext;
            internal long Passed;
            internal readonly Queue<CalibrationMarker> Visible = new Queue<CalibrationMarker>();
            public void Dispose() { Reader?.Dispose(); }
        }
        private readonly List<Layer> _layers = new List<Layer>();
        private readonly CancellationToken _cancellation;
        private long _sequence;
        internal CalibrationReplayCursor(IEnumerable<CloudRule> rules, CancellationToken cancellation)
        {
            _cancellation = cancellation;
            try
            {
                Dictionary<string, CalibrationRun> runs = new Dictionary<string, CalibrationRun>(StringComparer.OrdinalIgnoreCase);
                foreach (CloudRule rule in rules.Where(r => r.Enabled))
                {
                    if (_layers.Count >= 32) { throw new InvalidOperationException("Calibration replay layer limit exceeded."); }
                    if (!runs.TryGetValue(rule.BundlePath, out CalibrationRun run)) { run = CalibrationStorage.Open(rule.BundlePath, cancellation); runs.Add(rule.BundlePath, run); }
                    if (run.Spec.Hash != rule.Provenance.Hash) { throw new System.IO.InvalidDataException("Replay rule provenance differs."); }
                    Layer layer = new Layer { Rule = rule, Run = run, Reader = CalibrationStorage.Events(run, rule.Formation, cancellation).GetEnumerator() };
                    _layers.Add(layer); layer.HasNext = layer.Reader.MoveNext();
                }
            }
            catch { Dispose(); throw; }
        }
        internal ImmutableArray<CalibrationLayer> Advance(long sequence, bool complete)
        {
            if (sequence < _sequence) { throw new ArgumentException("Replay sequence cannot move backwards."); } _sequence = sequence;
            foreach (Layer layer in _layers)
            {
                while (layer.HasNext)
                {
                    _cancellation.ThrowIfCancellationRequested(); CalibrationEvent item = layer.Reader.Current;
                    if (item.Evidence.KnownSequence.HasValue ? item.Evidence.KnownSequence > sequence : !complete) { break; }
                    if (CalibrationFilter.Passes(item, layer.Run.Spec.PriceStep, layer.Rule.Kind, layer.Rule.Filters, out _))
                    {
                        layer.Passed++; layer.Visible.Enqueue(new CalibrationMarker(layer.Run, layer.Rule, item));
                        if (layer.Visible.Count > CalibrationPresentation.MarkersPerLayer) { layer.Visible.Dequeue(); }
                    }
                    layer.HasNext = layer.Reader.MoveNext();
                }
            }
            return _layers.Select(l => new CalibrationLayer(l.Rule, l.Passed, l.Visible.ToImmutableArray())).ToImmutableArray();
        }
        public void Dispose() { foreach (Layer layer in _layers) { layer.Dispose(); } _layers.Clear(); }
    }

    internal sealed record CalibrationReplayFrame(long Sequence, bool Complete, ImmutableArray<CalibrationLayer> Layers);

    /// <summary>One bounded mailbox follows the existing Order Flow replay clock; disposal cancels without blocking the dispatcher.</summary>
    internal sealed class CalibrationLayerPlayback : IDisposable
    {
        private readonly object _sync = new object();
        private readonly CancellationTokenSource _cancel = new CancellationTokenSource();
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private long _requested;
        private bool _complete, _pending, _disposed, _finished;
        private CalibrationReplayFrame _frame;
        private Exception _error;
        internal CalibrationLayerPlayback(ImmutableArray<CloudRule> rules)
        { new Thread(() => Run(rules)) { IsBackground = true, Name = "Calibration saved-layer replay" }.Start(); }
        internal void Request(long sequence, bool complete)
        { lock (_sync) { if (_disposed || _finished) { return; } _requested = sequence; _complete = complete; _pending = true; _wake.Set(); } }
        internal bool Take(out CalibrationReplayFrame frame, out Exception error)
        { lock (_sync) { frame = _frame; error = _error; _frame = null; _error = null; return frame != null || error != null; } }
        private void Run(ImmutableArray<CloudRule> rules)
        {
            try
            {
                using CalibrationReplayCursor cursor = new CalibrationReplayCursor(rules, _cancel.Token);
                while (true)
                {
                    _cancel.Token.ThrowIfCancellationRequested(); long sequence; bool complete, pending;
                    lock (_sync) { sequence = _requested; complete = _complete; pending = _pending; _pending = false; }
                    if (!pending) { _wake.WaitOne(50); continue; }
                    CalibrationReplayFrame frame = new CalibrationReplayFrame(sequence, complete, cursor.Advance(sequence, complete));
                    lock (_sync) { if (!_disposed) { _frame = frame; } }
                    if (complete) { break; }
                }
            }
            catch (OperationCanceledException) { lock (_sync) { _pending = false; } }
            catch (Exception error)
            {
                bool log; lock (_sync) { log = _disposed; if (!_disposed) { _error = error; } }
                if (log) { ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error); }
            }
            finally { lock (_sync) { _finished = true; _cancel.Dispose(); _wake.Dispose(); } }
        }
        public void Dispose()
        { lock (_sync) { _disposed = true; _frame = null; if (!_finished) { _cancel.Cancel(); _wake.Set(); } } }
    }
}
