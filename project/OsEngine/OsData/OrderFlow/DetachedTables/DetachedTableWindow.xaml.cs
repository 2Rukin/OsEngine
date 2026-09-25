using System;
using System.ComponentModel;
using System.Windows;

namespace OsEngine.OsData.OrderFlow.DetachedTables
{
    /// <summary>
    /// Transient presentation of an existing table; never owns its data, subscriptions or trading commands.
    /// The registration validates normal close and explicitly closes this window when its source ends.
    /// </summary>
    public partial class DetachedTableWindow : Window
    {
        private readonly DetachedTableRegistration _registration;

        internal DetachedTableWindow(DetachedTableRegistration registration)
        {
            InitializeComponent();
            _registration = registration;
            ButtonSource.Click += SourceClick;
            Closing += WindowClosing;
            Closed += WindowClosed;
        }

        private void SourceClick(object sender, RoutedEventArgs e)
        {
            try { _registration.FocusSource(); }
            catch (Exception error) { _registration.Report(error); }
        }

        private void WindowClosing(object sender, CancelEventArgs e)
        {
            try { e.Cancel = !_registration.CanClose(); }
            catch (Exception error) { e.Cancel = true; _registration.Report(error); }
        }

        private void WindowClosed(object sender, EventArgs e)
        {
            try
            {
                ButtonSource.Click -= SourceClick; Closing -= WindowClosing; Closed -= WindowClosed;
                _registration.WindowClosed(this);
            }
            catch (Exception error) { _registration.Report(error); }
        }
    }
}
