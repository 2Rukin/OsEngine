/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace OsEngine.OsData.OrderFlow.DetachedTables
{
    /// <summary>
    /// Replaces one explicitly registered table surface with a launcher and moves the same surface on the UI thread.
    /// Source controls keep ownership of data/edit validation; this object never starts calculations or clones rows.
    /// </summary>
    /// <remarks>
    /// UI-DETACHED-TABLES-001. Call Dispose before disposing the source screen. A source Closed event is a fallback,
    /// not Closing (which can be cancelled). Source enabled state and data context are inherited through the launcher;
    /// the nonmodal Order Flow workbenches use Show without a WPF Owner. Actual desktop/DPI qualification
    /// is separate from component transfer tests. This adapter is limited to their WPF tables.
    /// </remarks>
    internal sealed class DetachedTableRegistration : IDisposable
    {
        #region Source and presentation ownership

        private readonly FrameworkElement _surface;
        private readonly FrameworkElement _source;
        private readonly Func<string> _context;
        private readonly Action<Exception> _error;
        private readonly Action _focus;
        private readonly Func<FrameworkElement> _tools;
        private readonly DataGrid _wpf;
        private readonly DispatcherTimer _statusTimer;
        private readonly ContentControl _parking = new ContentControl();
        private ContentControl _current;
        private Window _sourceWindow;
        private DetachedTableWindow _window;
        private bool _disposed, _forceClose;
        private string _validationError;
        private readonly string _title;
        private readonly TextBlock _status;
        internal FrameworkElement Launcher { get; }
        internal Button OpenButton { get; }

        internal DetachedTableRegistration(FrameworkElement source, FrameworkElement surface, string title,
            Action<Exception> error, Func<string> context = null, Action focus = null, Func<FrameworkElement> tools = null)
        {
            source.VerifyAccess();
            _source = source; _surface = surface; _title = title; _error = error ?? throw new ArgumentNullException(nameof(error));
            _context = context; _focus = focus; _tools = tools; _wpf = surface as DataGrid;
            if (_wpf == null) { throw new ArgumentException("Expected an Order Flow DataGrid.", nameof(surface)); }
            OpenButton = new Button { Content = "Открыть таблицу: " + title, Height = double.NaN, MinHeight = 30,
                Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(4), HorizontalAlignment = HorizontalAlignment.Left,
                ToolTip = "Открыть эту таблицу в отдельном окне. Повторное нажатие активирует уже открытое окно." };
            _status = new TextBlock { Margin = new Thickness(8, 2, 4, 6), TextWrapping = TextWrapping.Wrap };
            _status.SetResourceReference(TextBlock.ForegroundProperty, "ControlForegroundWhite");
            StackPanel launcher = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
            launcher.Children.Add(OpenButton); launcher.Children.Add(_status); Launcher = launcher;
            CopyPlacement(surface, launcher);
            // An unselected TabItem has no presented visual subtree. Bind its own gate explicitly,
            // not only a child's effective IsEnabled, which can be coerced differently off-screen.
            MultiBinding enabled = new MultiBinding { Converter = SourceGate.Instance };
            for (FrameworkElement parent = surface.Parent as FrameworkElement; parent != null; parent = parent.Parent as FrameworkElement)
            { enabled.Bindings.Add(new Binding(nameof(UIElement.IsEnabled)) { Source = parent }); }
            enabled.Bindings.Add(new Binding(nameof(UIElement.IsEnabled)) { Source = source });
            BindingOperations.SetBinding(launcher, UIElement.IsEnabledProperty, enabled);
            Replace(surface, launcher);
            _parking.Content = surface; _current = _parking;
            BindContext(_parking);
            OpenButton.Click += OpenClick;
            source.Loaded += SourceLoaded;
            _statusTimer = new DispatcherTimer(DispatcherPriority.Background, source.Dispatcher) { Interval = TimeSpan.FromMilliseconds(500) };
            _statusTimer.Tick += RefreshStatus;
            UpdateStatus();
            if (source.IsLoaded) { AttachSourceWindow(); }
        }

        private static void CopyPlacement(FrameworkElement source, FrameworkElement target)
        {
            Grid.SetRow(target, Grid.GetRow(source)); Grid.SetRowSpan(target, Grid.GetRowSpan(source));
            Grid.SetColumn(target, Grid.GetColumn(source)); Grid.SetColumnSpan(target, Grid.GetColumnSpan(source));
            DockPanel.SetDock(target, DockPanel.GetDock(source));
            target.Margin = source.Margin;
            BindingOperations.SetBinding(target, UIElement.VisibilityProperty, new Binding(nameof(UIElement.Visibility)) { Source = source });
        }

        private static void Replace(FrameworkElement source, FrameworkElement target)
        {
            if (source.Parent is Panel panel)
            { int index = panel.Children.IndexOf(source); panel.Children.RemoveAt(index); panel.Children.Insert(index, target); }
            else if (source.Parent is Decorator decorator) { decorator.Child = target; }
            else if (source.Parent is ContentControl content && ReferenceEquals(content.Content, source)) { content.Content = target; }
            else { throw new InvalidOperationException("Table parent needs an explicit detach adapter."); }
        }

        private void BindContext(ContentControl target)
        {
            BindingOperations.SetBinding(target, FrameworkElement.DataContextProperty, new Binding(nameof(FrameworkElement.DataContext)) { Source = Launcher });
            // Bind the wrapper, not the table: local IsEnabled changes on the original view cannot bypass a disabled source ancestor.
            BindingOperations.SetBinding(target, UIElement.IsEnabledProperty, new Binding(nameof(UIElement.IsEnabled)) { Source = Launcher });
            // Original disabled result tabs could not reveal their future rows during causal replay.
            BindVisibility(target);
            target.Resources.MergedDictionaries.Add(_source.Resources);
        }

        private void BindVisibility(UIElement target)
        {
            MultiBinding visibility = new MultiBinding { Converter = SourceGate.Instance };
            visibility.Bindings.Add(new Binding(nameof(UIElement.IsEnabled)) { Source = Launcher });
            visibility.Bindings.Add(new Binding(nameof(UIElement.Visibility)) { Source = Launcher });
            BindingOperations.SetBinding(target, UIElement.VisibilityProperty, visibility);
        }

        private void SourceLoaded(object sender, RoutedEventArgs e)
        { try { AttachSourceWindow(); } catch (Exception error) { Report(error); } }

        private void AttachSourceWindow()
        {
            Window window = Window.GetWindow(_source);
            if (window == null || ReferenceEquals(window, _sourceWindow)) { return; }
            if (_sourceWindow != null) { throw new InvalidOperationException("A registered table cannot silently change its source Window."); }
            _sourceWindow = window; window.Closed += SourceClosed;
            _statusTimer.Start();
        }

        private void SourceClosed(object sender, EventArgs e)
        { try { Dispose(); } catch (Exception error) { Report(error); } }

        #endregion

        #region Transfer, editing and close

        private void OpenClick(object sender, RoutedEventArgs e)
        { try { Open(); } catch (Exception error) { Report(error); } }

        internal void Open()
        {
            _source.VerifyAccess();
            if (_disposed) { throw new ObjectDisposedException(nameof(DetachedTableRegistration)); }
            if (!Launcher.IsEnabled || Launcher.Visibility != Visibility.Visible) { return; }
            AttachSourceWindow();
            if (_sourceWindow == null) { throw new InvalidOperationException("Таблица ещё не присоединена к рабочему окну."); }
            if (_window != null)
            { if (_window.WindowState == WindowState.Minimized) { _window.WindowState = WindowState.Normal; } _window.Activate(); return; }
            DetachedTableWindow window = new DetachedTableWindow(this); _window = window;
            try
            {
                window.Title = _title + " — " + _sourceWindow.Title;
                BindContext(window.ContentControlTable);
                BindContext(window.ContentControlTools);
                window.ContentControlTools.Content = _tools?.Invoke();
                BindVisibility(window.TextBlockStatus);
                MoveTo(window.ContentControlTable); UpdateStatus();
                window.ButtonSource.SetBinding(UIElement.IsEnabledProperty, new Binding(nameof(UIElement.IsEnabled)) { Source = Launcher });
                window.Show();
            }
            catch
            {
                _forceClose = true;
                try { window.Close(); if (ReferenceEquals(_window, window)) { WindowClosed(window); } }
                finally { _forceClose = false; }
                throw;
            }
        }

        /// <summary>Transfers into an empty host; failure restores the preceding host, without changing the model or triggering calculation.</summary>
        internal void MoveTo(ContentControl target)
        {
            _source.VerifyAccess();
            if (_disposed) { throw new ObjectDisposedException(nameof(DetachedTableRegistration)); }
            if (ReferenceEquals(_current, target)) { return; }
            if (target.Content != null) { throw new InvalidOperationException("Table target already has content."); }
            ContentControl previous = _current; previous.Content = null;
            try { target.Content = _surface; _current = target; }
            catch { target.Content = null; previous.Content = _surface; throw; }
        }

        internal bool CanClose()
        {
            if (_forceClose || _disposed) { return true; }
            bool valid = _wpf.CommitEdit(DataGridEditingUnit.Cell, true) && _wpf.CommitEdit(DataGridEditingUnit.Row, true);
            _validationError = valid ? null : "Исправьте значение или отмените правку клавишей Esc. Таблица не закрыта.";
            UpdateStatus();
            return valid;
        }

        internal void WindowClosed(DetachedTableWindow window)
        {
            if (!ReferenceEquals(_window, window)) { return; }
            if (!_disposed) { MoveTo(_parking); }
            window.ContentControlTools.Content = null;
            BindingOperations.ClearAllBindings(window.ButtonSource);
            BindingOperations.ClearAllBindings(window.TextBlockStatus);
            BindingOperations.ClearAllBindings(window.ContentControlTools); BindingOperations.ClearAllBindings(window.ContentControlTable);
            window.ContentControlTools.Resources.MergedDictionaries.Clear(); window.ContentControlTable.Resources.MergedDictionaries.Clear();
            _window = null; _validationError = null; UpdateStatus();
        }

        internal void FocusSource()
        {
            if (_disposed || !Launcher.IsEnabled || _sourceWindow == null || !_sourceWindow.IsEnabled) { return; }
            _focus?.Invoke();
            if (_sourceWindow.WindowState == WindowState.Minimized) { _sourceWindow.WindowState = WindowState.Normal; }
            _sourceWindow.Activate();
        }

        #endregion

        #region Status and disposal

        private void RefreshStatus(object sender, EventArgs e)
        { try { UpdateStatus(); } catch (Exception error) { Report(error); } }

        private void UpdateStatus()
        {
            if (_disposed) { return; }
            int count = _wpf.Items.Count;
            string context = _context?.Invoke() ?? _sourceWindow?.Title ?? "";
            string text = !Launcher.IsEnabled || Launcher.Visibility != Visibility.Visible ? "Таблица временно недоступна в исходном разделе. Завершите реплей или текущую операцию." :
                count == 0 ? "Строк пока нет. Откройте таблицу и проверьте источник, фильтр или состояние расчёта." : "Строк в текущем представлении: " + count;
            _status.Text = text;
            if (_window != null) { _window.TextBlockContext.Text = _title + " · " + context; _window.TextBlockStatus.Text = _validationError ?? text; }
        }

        internal void Report(Exception error) { _error(error); }

        /// <summary>Closes presentation and releases only registration resources; never disposes the source table or its model.</summary>
        public void Dispose()
        {
            _source.VerifyAccess(); if (_disposed) { return; }
            _forceClose = true;
            if (_window != null) { _window.Close(); }
            _disposed = true; _statusTimer.Stop(); _statusTimer.Tick -= RefreshStatus;
            OpenButton.Click -= OpenClick; _source.Loaded -= SourceLoaded;
            if (_sourceWindow != null) { _sourceWindow.Closed -= SourceClosed; _sourceWindow = null; }
            _current.Content = null; BindingOperations.ClearAllBindings(_parking); _parking.Resources.MergedDictionaries.Clear();
            BindingOperations.ClearAllBindings(Launcher);
        }

        #endregion

        private sealed class SourceGate : IMultiValueConverter
        {
            internal static readonly SourceGate Instance = new SourceGate();
            public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
            {
                bool allowed = true;
                foreach (object value in values)
                {
                    if (value is bool enabled ? !enabled : value is Visibility visibility ? visibility != Visibility.Visible : true)
                    { allowed = false; break; }
                }
                if (targetType == typeof(Visibility)) { return allowed ? Visibility.Visible : Visibility.Collapsed; }
                return allowed;
            }
            public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
        }
    }
}
