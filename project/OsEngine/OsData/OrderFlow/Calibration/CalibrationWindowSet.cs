/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.Logging;
using OsEngine.Market;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace OsEngine.OsData.OrderFlow.Calibration
{
    /// <summary>Dispatcher-owned nonmodal windows, one per exact context. Closing the screen closes views and releases event links.</summary>
    internal sealed class CalibrationWindowSet : IDisposable
    {
        private readonly Dictionary<string, Window> _windows = new Dictionary<string, Window>();
        private bool _disposed;
        internal Window Open(string key, Func<Window> create, Action<Window> show = null)
        {
            if (_disposed) { throw new ObjectDisposedException(nameof(CalibrationWindowSet)); }
            if (_windows.TryGetValue(key, out Window existing))
            { if (existing.WindowState == WindowState.Minimized) { existing.WindowState = WindowState.Normal; } existing.Activate(); return existing; }
            Window window = create(); _windows.Add(key, window); window.Closed += WindowClosed;
            try { if (show == null) { window.Show(); } else { show(window); } return window; }
            catch { window.Closed -= WindowClosed; _windows.Remove(key); window.Close(); throw; }
        }
        internal void CloseAll()
        {
            foreach (Window window in _windows.Values.ToArray()) { window.Close(); }
        }
        private void WindowClosed(object sender, EventArgs e)
        {
            try
            {
                Window window = (Window)sender; window.Closed -= WindowClosed;
                string key = _windows.FirstOrDefault(p => ReferenceEquals(p.Value, window)).Key;
                if (key != null) { _windows.Remove(key); }
            }
            catch (Exception error) { ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error); }
        }
        public void Dispose() { if (_disposed) { return; } CloseAll(); _disposed = true; }
    }
}
