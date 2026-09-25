using OsEngine.Entity;
using OsEngine.Language;
using OsEngine.Logging;
using OsEngine.Market;
using OsEngine.OsData.OrderFlow.Explorer;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace OsEngine.OsData.OrderFlow.Calibration
{
    /// <summary>Nonmodal page view. Closing cancels only presentation work and releases callbacks; calculations and rule identity are untouched.</summary>
    public partial class CalibrationTableWindow : Window
    {
        private readonly CalibrationTableSource _source;
        private Action<object> _select;
        private readonly DispatcherTimer _timer;
        private ExplorerJob _job;
        private TableQuery _query = new TableQuery();
        private TablePage _page;
        private readonly List<TableCursor> _history = new List<TableCursor>();
        private bool _closed;

        internal CalibrationTableWindow(CalibrationTableSource source, string context, Action<object> select = null)
        {
            InitializeComponent(); _source = source; _select = select;
            Title = source.Title; TextBlockContext.Text = source.Title + " · " + context;
            TextBoxSearch.ToolTip = L("Text / exact ID", "Текст / точный ID");
            TextBoxTimeRange.ToolTip = "TimeRangeId"; TextBoxMinimum.ToolTip = L("Minimum inclusive; empty disables", "Минимум включительно; пусто — выключен");
            TextBoxMaximum.ToolTip = L("Maximum inclusive; empty disables", "Максимум включительно; пусто — выключен");
            ComboBoxDirection.ItemsSource = new[] { "Any", "Buy", "Sell" }; ComboBoxDirection.SelectedIndex = 0;
            ComboBoxNumeric.ItemsSource = source.Columns; ComboBoxNumeric.SelectedIndex = 0;
            CalibrationText.LocalizeItems(ComboBoxDirection); CalibrationText.LocalizeItems(ComboBoxNumeric);
            ComboBoxDirection.IsEnabled = source.Columns.Contains("Direction") || source.Columns.Contains("Side");
            TextBoxTimeRange.IsEnabled = source.Columns.Contains("TimeRangeId");
            ButtonApply.Content = L("Apply view filters", "Применить фильтры таблицы"); ButtonReset.Content = L("Reset filters", "Сбросить фильтры");
            ButtonSelect.Content = L("Select / chart / anatomy", "Выбрать / график / anatomy"); ButtonSelect.IsEnabled = select != null;
            ButtonPrevious.Content = L("Previous", "Назад"); ButtonNext.Content = L("Next", "Вперёд");
            foreach (string column in source.Columns)
            {
                Style header = new Style(typeof(DataGridColumnHeader), DataGridRows.ColumnHeaderStyle); header.Setters.Add(new Setter(ToolTipProperty, column));
                DataGridRows.Columns.Add(new DataGridTextColumn { Header = CalibrationText.Label(column), SortMemberPath = column,
                    Binding = new Binding("Values[" + column + "]") { Converter = ScalarDisplay.Instance }, Width = DataGridLength.SizeToHeader, HeaderStyle = header });
            }
            ButtonApply.Click += ApplyClick; ButtonReset.Click += ResetClick; ButtonSelect.Click += SelectClick;
            ButtonPrevious.Click += PreviousClick; ButtonNext.Click += NextClick;
            DataGridRows.Sorting += Sorting; DataGridRows.MouseDoubleClick += DoubleClick;
            Closed += WindowClosed; PreviewKeyDown += WindowKeyDown;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) }; _timer.Tick += Poll;
            LoadPage();
        }

        private static string L(string en, string ru) => OsLocalization.ConvertToLocString("Eng:" + en + "_Ru:" + ru + "_");
        private void LoadPage()
        {
            _job?.Dispose(); TableQuery query = _query;
            _job = new ExplorerJob(token => _source.Page(query, token)); _timer.Start();
            TextBlockStatus.Text = L("Reading saved data…", "Чтение сохранённых данных…");
            ButtonNext.IsEnabled = ButtonPrevious.IsEnabled = false;
        }
        private void Poll(object sender, EventArgs e)
        {
            try
            {
                if (_closed || _job == null || !_job.TryResult(out object result, out Exception error)) { return; }
                _timer.Stop(); _job.Dispose(); _job = null;
                if (error != null) { throw error; }
                _page = (TablePage)result; DataGridRows.ItemsSource = _page.Rows;
                TextBlockStatus.Text = L("Visible / total", "Видимых / всего") + " " + _page.Visible + " / " + _page.Total +
                    " · " + L("page", "страница") + " " + (_history.Count + 1) + " · " + L("rows", "строк") + " " + _page.Rows.Count;
                ButtonNext.IsEnabled = _page.Next != null; ButtonPrevious.IsEnabled = _history.Count > 0;
            }
            catch (Exception error) { Report(error); }
        }
        private void ApplyClick(object sender, RoutedEventArgs e)
        {
            try
            {
                _query = _query with { Text = TextBoxSearch.Text.Trim(), TimeRange = TextBoxTimeRange.Text.Trim(), Direction = (string)ComboBoxDirection.SelectedItem,
                    NumericColumn = (string)ComboBoxNumeric.SelectedItem ?? "", Minimum = Number(TextBoxMinimum.Text), Maximum = Number(TextBoxMaximum.Text), After = null };
                _history.Clear(); LoadPage();
            }
            catch (Exception error) { Report(error); }
        }
        private static decimal? Number(string text) => string.IsNullOrWhiteSpace(text) ? null : text.ToDecimal();
        private void ResetClick(object sender, RoutedEventArgs e)
        {
            try
            {
                TextBoxSearch.Clear(); TextBoxTimeRange.Clear(); TextBoxMinimum.Clear(); TextBoxMaximum.Clear(); ComboBoxDirection.SelectedIndex = 0;
                _query = new TableQuery(); _history.Clear(); foreach (DataGridColumn column in DataGridRows.Columns) { column.SortDirection = null; } LoadPage();
            }
            catch (Exception error) { Report(error); }
        }
        private void Sorting(object sender, DataGridSortingEventArgs e)
        {
            try
            {
                e.Handled = true; string key = e.Column.SortMemberPath;
                int direction = _query.SortColumn != key || _query.SortDirection == 0 ? 1 : _query.SortDirection == 1 ? -1 : 0;
                foreach (DataGridColumn column in DataGridRows.Columns) { column.SortDirection = null; }
                e.Column.SortDirection = direction == 0 ? null : direction == 1 ? ListSortDirection.Ascending : ListSortDirection.Descending;
                _query = _query with { SortColumn = key, SortDirection = direction, After = null }; _history.Clear(); LoadPage();
            }
            catch (Exception error) { Report(error); }
        }
        private void NextClick(object sender, RoutedEventArgs e)
        {
            try { if (_page?.Next == null) { return; } _history.Add(_query.After); _query = _query with { After = _page.Next }; LoadPage(); }
            catch (Exception error) { Report(error); }
        }
        private void PreviousClick(object sender, RoutedEventArgs e)
        {
            try { if (_history.Count == 0) { return; } _query = _query with { After = _history[_history.Count - 1] }; _history.RemoveAt(_history.Count - 1); LoadPage(); }
            catch (Exception error) { Report(error); }
        }
        private void SelectClick(object sender, RoutedEventArgs e)
        { try { if (DataGridRows.SelectedItem is CalibrationTableRow row) { _select?.Invoke(row.Payload); } } catch (Exception error) { Report(error); } }
        private void DoubleClick(object sender, MouseButtonEventArgs e) { SelectClick(sender, e); }
        private void WindowKeyDown(object sender, KeyEventArgs e) { try { if (e.Key == Key.Escape) { Close(); } } catch (Exception error) { Report(error); } }
        private void Report(Exception error)
        { TextBlockStatus.Text = error.Message; ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error); }
        private void WindowClosed(object sender, EventArgs e)
        {
            try
            {
                _closed = true; _timer.Stop(); _timer.Tick -= Poll; _job?.Dispose(); _job = null; _select = null;
                ButtonApply.Click -= ApplyClick; ButtonReset.Click -= ResetClick; ButtonSelect.Click -= SelectClick;
                ButtonPrevious.Click -= PreviousClick; ButtonNext.Click -= NextClick;
                DataGridRows.Sorting -= Sorting; DataGridRows.MouseDoubleClick -= DoubleClick; DataGridRows.ItemsSource = null;
                Closed -= WindowClosed; PreviewKeyDown -= WindowKeyDown;
            }
            catch (Exception error) { ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error); }
        }
        private sealed class ScalarDisplay : IValueConverter
        {
            internal static readonly ScalarDisplay Instance = new ScalarDisplay();
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
            {
                DateTime time => time.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture),
                decimal number => number.ToString("G29", CultureInfo.InvariantCulture),
                double number => number.ToString("G17", CultureInfo.InvariantCulture),
                _ => value?.ToString() ?? ""
            };
            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
        }
    }
}
