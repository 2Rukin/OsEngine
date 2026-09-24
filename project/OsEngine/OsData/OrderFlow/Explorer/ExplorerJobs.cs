/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Threading;

namespace OsEngine.OsData.OrderFlow.Explorer
{
    /// <summary>One cancellable offline job. The owner polls completion and handles the captured error on its dispatcher.</summary>
    /// <remarks>Dispose cancels without joining the UI thread. Only the worker disposes its token; completion state is lock-protected.</remarks>
    internal sealed class ExplorerJob : IDisposable
    {
        private readonly object _sync = new object();
        private readonly CancellationTokenSource _cancel = new CancellationTokenSource();
        private bool _finished, _disposed;
        private object _result;
        private Exception _error;
        internal ExplorerJob(Func<CancellationToken, object> action)
        {
            new Thread(() =>
            {
                object result = null; Exception error = null;
                try { result = action(_cancel.Token); }
                catch (Exception failure) { error = failure; }
                finally
                {
                    bool log;
                    lock (_sync) { log = _disposed && error != null && error is not OperationCanceledException; _result = _disposed ? null : result; _error = error; _finished = true; _cancel.Dispose(); }
                    if (log) { OsEngine.Market.ServerMaster.SendNewLogMessage(error.ToString(), OsEngine.Logging.LogMessageType.Error); }
                }
            }) { IsBackground = true, Name = "Cloud Explorer offline job" }.Start();
        }
        internal bool TryResult(out object result, out Exception error)
        { lock (_sync) { result = _disposed ? null : _result; error = _disposed ? null : _error; return _finished; } }
        internal void Cancel() { lock (_sync) { if (!_finished) { _cancel.Cancel(); } } }
        public void Dispose() { lock (_sync) { _disposed = true; if (!_finished) { _cancel.Cancel(); } _result = null; } }
    }

    /// <summary>One persistent replay worker with a bounded one-frame mailbox; each consumed step request advances one raw row.</summary>
    /// <remarks>Pause stops the batch after its in-flight row. Dispatcher polling owns publication; disposal cancels without joining the UI.</remarks>
    internal sealed class ExplorerPlayback : IDisposable
    {
        private readonly object _sync = new object();
        private readonly CancellationTokenSource _cancel = new CancellationTokenSource();
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private bool _playing, _step, _finished, _disposed;
        private int _speed = 1;
        private ExplorerFrame _frame;
        private Exception _error;
        internal ExplorerPlayback(ExplorerRun run, ExplorerAnchor anchor)
        {
            new Thread(() => Run(run, anchor)) { IsBackground = true, Name = "Cloud Explorer replay" }.Start();
        }
        #region Dispatcher mailbox

        internal void Play(int speed) { lock (_sync) { if (_finished || _disposed) { return; } _speed = Math.Clamp(speed, 1, 10000); _playing = true; _wake.Set(); } }
        internal void Pause() { lock (_sync) { _playing = false; } }
        internal void Step() { lock (_sync) { if (_finished || _disposed) { return; } _playing = false; _step = true; _wake.Set(); } }
        internal bool Take(out ExplorerFrame frame, out Exception error)
        { lock (_sync) { frame = _frame; error = _error; _frame = null; _error = null; return frame != null || error != null; } }
        #endregion

        #region Worker lifetime

        private void Run(ExplorerRun run, ExplorerAnchor anchor)
        {
            try
            {
                using ExplorerReplayCursor cursor = new ExplorerReplayCursor(run, anchor, _cancel.Token);
                while (true)
                {
                    _cancel.Token.ThrowIfCancellationRequested(); bool work; int speed;
                    lock (_sync) { work = _playing || _step; speed = _step ? 1 : _speed; _step = false; }
                    if (!work) { _wake.WaitOne(50); continue; }
                    ExplorerFrame frame = null;
                    for (int i = 0; i < speed; i++)
                    {
                        frame = cursor.Step(); if (frame.Complete) { break; }
                        lock (_sync) { if (!_playing) { break; } }
                    }
                    lock (_sync) { if (!_disposed) { _frame = frame; } }
                    if (frame.Complete) { break; }
                    _wake.WaitOne(40);
                }
            }
            catch (OperationCanceledException) { lock (_sync) { _playing = false; _step = false; } }
            catch (Exception error) { lock (_sync) { if (!_disposed) { _error = error; } } }
            finally { lock (_sync) { _finished = true; _cancel.Dispose(); _wake.Dispose(); } }
        }
        public void Dispose()
        { lock (_sync) { _disposed = true; _frame = null; if (!_finished) { _cancel.Cancel(); _wake.Set(); } } }
        #endregion
    }
}
