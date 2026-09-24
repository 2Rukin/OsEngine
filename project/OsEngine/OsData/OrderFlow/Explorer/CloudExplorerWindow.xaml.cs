/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.Logging;
using OsEngine.Market;
using System;

namespace OsEngine.OsData.OrderFlow.Explorer
{
    /// <summary>
    /// Hosts one complete Cloud Explorer workbench without starting a calculation on construction.
    /// Closing or disposing the Window cancels and releases the hosted workbench.
    /// </summary>
    public partial class CloudExplorerWindow : IDisposable
    {
        private bool _disposed;

        internal event Action<CloudExplorerWindow> Disposed;

        /// <summary>Creates an unstarted workbench bound to the current owner inputs; no file is read until an explicit operator action.</summary>
        internal CloudExplorerWindow(Func<ExplorerRunSpec> requestProvider, Func<string> inputFingerprint)
        {
            InitializeComponent();
            Explorer = new CloudExplorerControl { RequestProvider = requestProvider, InputFingerprint = inputFingerprint };
            ContentControlExplorer.Content = Explorer;
            Closed += WindowClosed;
        }

        internal CloudExplorerControl Explorer { get; private set; }

        private void WindowClosed(object sender, EventArgs e)
        {
            try { Dispose(); }
            catch (Exception error) { ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error); }
        }

        /// <summary>
        /// Cancels the hosted workbench and detaches it from the Window.
        /// Repeated calls have no effect, and no published Explorer artifact is removed.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) { return; }
            _disposed = true;
            Closed -= WindowClosed;
            CloudExplorerControl explorer = Explorer;
            Explorer = null;
            ContentControlExplorer.Content = null;
            explorer?.Dispose();
            Disposed?.Invoke(this);
            Disposed = null;
        }
    }
}
