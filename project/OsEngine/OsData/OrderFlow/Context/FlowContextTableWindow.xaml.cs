using OsEngine.Logging;
using OsEngine.Market;
using System;
using System.Collections;
using System.Windows;
using System.Windows.Input;

namespace OsEngine.OsData.OrderFlow.Context
{
    /// <summary>Displays a fixed, read-only snapshot and invokes the owner's selection callback on the UI dispatcher.</summary>
    /// <remarks>Rows do not follow replay automatically. Closing detaches selection handlers and releases the callback and item source.</remarks>
    public partial class FlowContextTableWindow : Window
    {
        private Action<object> _select;
        internal FlowContextTableWindow(string title, string context, IEnumerable rows, Action<object> select)
        {
            InitializeComponent(); Title = title; TextBlockContext.Text = context; DataGridRows.ItemsSource = rows; _select = select;
            ButtonSelect.IsEnabled = select != null; ButtonSelect.Click += SelectClick; DataGridRows.MouseDoubleClick += DoubleClick; Closed += WindowClosed;
        }
        private void SelectClick(object sender, RoutedEventArgs e) { Select(); }
        private void DoubleClick(object sender, MouseButtonEventArgs e) { Select(); }
        private void Select() { try { if (DataGridRows.SelectedItem != null) { _select?.Invoke(DataGridRows.SelectedItem); } } catch (Exception error) { ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error); } }
        private void WindowClosed(object sender, EventArgs e) { ButtonSelect.Click -= SelectClick; DataGridRows.MouseDoubleClick -= DoubleClick; Closed -= WindowClosed; DataGridRows.ItemsSource = null; _select = null; }
    }
}
