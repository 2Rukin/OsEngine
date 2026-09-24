/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.OsData.OrderFlow.Explorer;
using System;
using System.Windows.Controls;

namespace OsEngine.OsData.OrderFlow
{
    public partial class OrderFlowResearchUi
    {
        private CloudExplorerTabLauncher _cloudExplorerLauncher;

        private void InitializeCloudExplorer()
        {
            TabItemCloudExplorer.Header = L("Cloud research", "Исследование Cloud");
            _cloudExplorerLauncher = new CloudExplorerTabLauncher(TabControlResults, TabItemCloudExplorer, TabItemSummary,
                CreateCloudExplorerWindow, ShowCloudExplorerWindow, ShowError);
        }

        private CloudExplorerWindow CreateCloudExplorerWindow()
        {
            return new CloudExplorerWindow(CreateExplorerInput, ExplorerInputFingerprint)
            { Owner = this, Title = L("Cloud research", "Исследование Cloud") };
        }

        private static void ShowCloudExplorerWindow(CloudExplorerWindow window)
        {
            window.Show();
        }

        private ExplorerRunSpec CreateExplorerInput()
        {
            return new ExplorerRunSpec { InputPath = TextBoxTicksPath.Text.Trim(), OutputRootPath = TextBoxOutputPath.Text.Trim(),
                PriceStep = ParsePriceStep(TextBoxPriceStep.Text), FromDate = _fromDateInput.ReadDate(), ToDate = _toDateInput.ReadDate() };
        }

        private string ExplorerInputFingerprint() => string.Join("|", TextBoxTicksPath.Text, TextBoxOutputPath.Text, TextBoxPriceStep.Text, DateFrom.Text, DateTo.Text);

        private void DisposeCloudExplorer()
        {
            _cloudExplorerLauncher?.Dispose();
            _cloudExplorerLauncher = null;
        }
    }

    /// <summary>
    /// Turns the Cloud Explorer result tab into a single modeless Window launcher while retaining the prior tab selection.
    /// Disposal detaches the routed selection handler and closes the Window owned by this launcher.
    /// </summary>
    internal sealed class CloudExplorerTabLauncher : IDisposable
    {
        private readonly TabControl _tabControl;
        private readonly TabItem _launcherTab;
        private readonly object _fallbackSelection;
        private readonly Func<CloudExplorerWindow> _windowFactory;
        private readonly Action<CloudExplorerWindow> _showWindow;
        private readonly Action<Exception> _showError;
        private object _previousSelection;
        private CloudExplorerWindow _window;
        private bool _disposed;

        internal CloudExplorerTabLauncher(TabControl tabControl, TabItem launcherTab, object fallbackSelection,
            Func<CloudExplorerWindow> windowFactory, Action<CloudExplorerWindow> showWindow, Action<Exception> showError)
        {
            _tabControl = tabControl ?? throw new ArgumentNullException(nameof(tabControl));
            _launcherTab = launcherTab ?? throw new ArgumentNullException(nameof(launcherTab));
            _fallbackSelection = fallbackSelection ?? throw new ArgumentNullException(nameof(fallbackSelection));
            _windowFactory = windowFactory ?? throw new ArgumentNullException(nameof(windowFactory));
            _showWindow = showWindow ?? throw new ArgumentNullException(nameof(showWindow));
            _showError = showError ?? throw new ArgumentNullException(nameof(showError));
            _previousSelection = ReferenceEquals(_tabControl.SelectedItem, _launcherTab) ? _fallbackSelection : _tabControl.SelectedItem ?? _fallbackSelection;
            _tabControl.SelectionChanged += SelectionChanged;
        }

        private void SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (_disposed) { return; }
                if (!_launcherTab.IsSelected)
                {
                    _previousSelection = _tabControl.SelectedItem ?? _fallbackSelection;
                    return;
                }
                object returnSelection = _previousSelection ?? _fallbackSelection;
                foreach (object removed in e.RemovedItems)
                {
                    if (!ReferenceEquals(removed, _launcherTab)) { returnSelection = removed; break; }
                }
                _tabControl.SelectedItem = returnSelection;
                OpenWindow();
            }
            catch (Exception error) { _showError(error); }
        }

        private void OpenWindow()
        {
            if (_window != null)
            {
                if (_window.IsVisible) { _window.Activate(); }
                return;
            }
            CloudExplorerWindow window = _windowFactory();
            _window = window;
            window.Disposed += WindowDisposed;
            try { _showWindow(window); }
            catch
            {
                window.Disposed -= WindowDisposed;
                _window = null;
                window.Dispose();
                throw;
            }
        }

        private void WindowDisposed(CloudExplorerWindow window)
        {
            try
            {
                window.Disposed -= WindowDisposed;
                if (ReferenceEquals(_window, window)) { _window = null; }
            }
            catch (Exception error) { _showError(error); }
        }

        public void Dispose()
        {
            if (_disposed) { return; }
            _disposed = true;
            _tabControl.SelectionChanged -= SelectionChanged;
            CloudExplorerWindow window = _window;
            _window = null;
            if (window == null) { return; }
            window.Disposed -= WindowDisposed;
            if (window.IsLoaded) { window.Close(); }
            window.Dispose();
        }
    }
}
