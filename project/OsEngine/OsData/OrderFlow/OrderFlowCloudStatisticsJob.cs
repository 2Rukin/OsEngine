/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using OsEngine.Logging;
using OsEngine.Market;
using System.Threading;

namespace OsEngine.OsData.OrderFlow
{
    /// <summary>Owns a cancellable statistics worker; the UI polls terminal state and never joins the worker while closing.</summary>
    /// <remarks>Inputs belong to the completed run and must remain immutable. Disposal cancels without publishing a late result; the worker disposes its CTS after use. Failures remain observable to the UI, or go to the standard log after closure. ORDER-FLOW-MVP-RUNBOOK-001 defines the offline boundary.</remarks>
    internal sealed class OrderFlowCloudStatisticsJob : IDisposable
    {
        private readonly object _sync = new object();
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private readonly OrderFlowResearchRequest _request;
        private readonly OrderFlowCloudStatisticsSettings _settings;
        private readonly bool _export;
        private bool _started;
        private bool _disposed;
        private bool _finished;
        private bool _cancelled;
        private OrderFlowCloudStatisticsResult _result;
        private Exception _error;
        public OrderFlowResearchResult Source { get; }
        public bool Finished { get { lock (_sync) { return _finished; } } }
        public bool Cancelled { get { lock (_sync) { return _cancelled; } } }
        public OrderFlowCloudStatisticsResult Result { get { lock (_sync) { return _result; } } }
        public Exception Error { get { lock (_sync) { return _error; } } }

        public OrderFlowCloudStatisticsJob(OrderFlowResearchRequest request, OrderFlowResearchResult source, OrderFlowCloudStatisticsSettings settings, bool export = true)
        { _request = request; Source = source; _settings = settings.CopyValidated(); _export = export; }

        /// <summary>Starts this job once; rejects restart or use after disposal. No Application, connector or order flow is started.</summary>
        public void Start()
        {
            lock (_sync)
            {
                if (_disposed || _started) { throw new InvalidOperationException("Statistics worker cannot be started twice or after disposal."); }
                _started = true;
                try { new Thread(Run) { IsBackground = true, Name = "OrderFlowCloudStatistics" }.Start(); }
                catch { _started = false; throw; }
            }
        }

        private void Run()
        {
            try
            {
                CancellationToken token = _cancellation.Token;
                OrderFlowCloudStatisticsResult result = new OrderFlowCloudStatisticsEngine().Run(_request, Source, _settings, token);
                if (_export) { result.ArtifactDirectory = OrderFlowCloudStatisticsArtifacts.Write(_request.OutputRootPath, result, token); }
                token.ThrowIfCancellationRequested();
                lock (_sync) { if (!_disposed && !_cancellation.IsCancellationRequested) { _result = result; } else { _cancelled = true; } }
            }
            catch (OperationCanceledException) { lock (_sync) { _cancelled = true; } }
            catch (Exception error)
            {
                bool closed; lock (_sync) { closed = _disposed; if (!closed) { _error = error; } }
                if (closed) { ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error); }
            }
            finally { lock (_sync) { _finished = true; if (_disposed) { _cancellation.Dispose(); } } }
        }

        /// <summary>Requests cooperative cancellation without blocking the calling UI thread; terminal or disposed jobs are unchanged.</summary>
        public void Cancel() { lock (_sync) { if (!_disposed && !_finished) { _cancellation.Cancel(); } } }

        /// <summary>Cancels and prevents subsequent publication. An active worker releases its own cancellation source on completion; the UI does not join it.</summary>
        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) { return; }
                _disposed = true; _cancellation.Cancel();
                if (!_started || _finished) { _cancellation.Dispose(); }
            }
        }
    }
}
