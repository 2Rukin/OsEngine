using OsEngine.Entity;
using OsEngine.Language;
using OsEngine.Logging;
using OsEngine.Market;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Forms = System.Windows.Forms;

namespace OsEngine.OsData.Statistics
{
    public partial class StatisticsUi
    {
        private Forms.DataGridView _daysGrid;
        private Forms.DataGridView _intervalsGrid;
        private Forms.DataGridView _pointsGrid;
        private StatisticsSortableGrid _daysTable;
        private StatisticsSortableGrid _intervalsTable;
        private StatisticsSortableGrid _pointsTable;
        private List<StatisticsInstrument> _catalog;
        private PairStatisticsResult _result;
        private CancellationTokenSource _cancellation;
        private string _catalogFolder;
        private bool _closed;
        private bool _busy;

        public StatisticsUi()
        {
            InitializeComponent();
            Layout.StickyBorders.Listen(this);
            Layout.StartupLocation.Start_FitHeightToWorkArea(this);
            Localize();
            _daysTable = new StatisticsSortableGrid(HostDays, new string[] { L("Date", "Дата"), L("Volume A", "Объём A"), L("Volume B", "Объём B"), L("Active A", "Активных A"), L("Active B", "Активных B"), L("Matched", "Совпало"), L("Coverage %", "Покрытие %"), L("Accepted", "Принят"), L("Reason", "Причина"), L("Min", "Мин"), L("Max", "Макс"), L("Range", "Размах") }, GetDayCell);
            _intervalsTable = new StatisticsSortableGrid(HostIntervals, new string[] { L("Start", "Начало"), L("End", "Конец"), L("Days", "Дней"), L("Points", "Точек"), L("Min", "Мин"), L("Max", "Макс"), L("Range", "Размах"), L("Mean", "Среднее"), L("Stddev N", "Станд. откл. N") }, GetIntervalCell);
            _pointsTable = new StatisticsSortableGrid(HostPoints, new string[] { L("Time", "Время"), "Close A − Close B" }, GetPointCell);
            _daysGrid = _daysTable.Grid;
            _intervalsGrid = _intervalsTable.Grid;
            _pointsGrid = _pointsTable.Grid;
            _daysGrid.Columns[0].DefaultCellStyle.Format = "yyyy-MM-dd";
            ButtonPairs.Click += ButtonPairs_Click;
            ButtonBrowse.Click += ButtonBrowse_Click;
            ButtonLoad.Click += ButtonLoad_Click;
            ButtonRun.Click += ButtonRun_Click;
            ButtonCancel.Click += ButtonCancel_Click;
            TextBoxFolder.TextChanged += TextBoxFolder_TextChanged;
            ComboBoxFirst.SelectionChanged += Instrument_SelectionChanged;
            ComboBoxSecond.SelectionChanged += Instrument_SelectionChanged;
            ComboBoxTimeFrame.SelectionChanged += Input_SelectionChanged;
            DatePickerFrom.SelectedDateChanged += Input_SelectionChanged;
            DatePickerTo.SelectedDateChanged += Input_SelectionChanged;
            DatePickerFrom.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(Input_TextChanged));
            DatePickerTo.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(Input_TextChanged));
            DatePickerFrom.DateValidationError += DatePicker_DateValidationError;
            DatePickerTo.DateValidationError += DatePicker_DateValidationError;
            TextBoxVolumeA.TextChanged += Input_TextChanged;
            TextBoxVolumeB.TextChanged += Input_TextChanged;
            TextBoxBarsA.TextChanged += Input_TextChanged;
            TextBoxBarsB.TextChanged += Input_TextChanged;
            TextBoxCoverage.TextChanged += Input_TextChanged;
            OsLocalization.LocalizationTypeChangeEvent += Localization_Changed;
            Themes.ThemeManager.ThemeChangedEvent += Theme_Changed;
            Closed += StatisticsUi_Closed;
        }

        #region Presentation

        private static string L(string english, string russian)
        {
            return OsLocalization.ConvertToLocString("Eng:" + english + "_Ru:" + russian + "_");
        }

        private void Localize()
        {
            Title = L("Statistics — historical pairs", "Статистика — исторические пары");
            LabelFolder.Content = L("Dataset folder", "Папка датасета");
            ButtonBrowse.Content = L("Browse", "Обзор");
            ButtonLoad.Content = L("Load catalog", "Загрузить каталог");
            ButtonPairs.Content = L("Pairs by expiry", "Пары по экспирации");
            LabelFirst.Content = L("Leg A — subtract B from A", "Нога A — из A вычитаем B");
            LabelSecond.Content = L("Leg B", "Нога B");
            LabelTimeFrame.Content = L("Common timeframe", "Общий таймфрейм");
            LabelFrom.Content = L("From (optional)", "С даты (необязательно)");
            LabelTo.Content = L("To (optional)", "По дату (необязательно)");
            DatePickerFrom.Language = System.Windows.Markup.XmlLanguage.GetLanguage(OsLocalization.CurCulture.IetfLanguageTag);
            DatePickerTo.Language = DatePickerFrom.Language;
            LabelVolumeA.Content = L("Min daily volume A", "Мин. объём за день A");
            LabelVolumeB.Content = L("Min daily volume B", "Мин. объём за день B");
            LabelBarsA.Content = L("Min active bars A", "Мин. активных свечей A");
            LabelBarsB.Content = L("Min active bars B", "Мин. активных свечей B");
            LabelCoverage.Content = L("Min coverage %", "Мин. покрытие %");
            ButtonRun.Content = L("Analyze A − B", "Рассчитать A − B");
            ButtonCancel.Content = L("Cancel", "Отмена");
            TabItemDays.Header = L("Daily diagnostics", "Диагностика по дням");
            TabItemIntervals.Header = L("Accepted intervals", "Принятые участки");
            TabItemPoints.Header = L("All spread points", "Все точки спреда");
            TextBlockWarning.Text = L("Close A − Close B only, coefficients 1 and 1. Check quote units, expiry and timezone manually. Defaults 1 / 1 / 1 / 1 / 80% are demonstration settings, not universal liquidity criteria. Stop dataset downloads before analysis.", "Только Close A − Close B, коэффициенты 1 и 1. Проверьте единицы котирования, экспирации и часовой пояс вручную. Начальные 1 / 1 / 1 / 1 / 80% — демонстрационные настройки, а не универсальные критерии ликвидности. Остановите загрузку датасета перед анализом.");
            TextBlockFooter.Text = L("Read-only analysis, no trading. Range uses matched active Close values, not intrabar extrema or PnL. Coverage measures overlap of observed active bars, not completeness of a trading session. Daily filtering is retrospective. All result rows are available in the tables.", "Анализ только для чтения, без торговли. Размах по совпавшим активным Close, не внутрисвечные экстремумы и не PnL. Покрытие — совпадение имеющихся активных свечей, не полнота сессии. Дневной фильтр ретроспективный. В таблицах доступны все строки результата.");
        }

        private object GetPointCell(int row, int column)
        {
            PairStatisticsPoint point = _result.Points[row];
            return column == 0 ? (object)point.Time : point.Value;
        }

        private object GetDayCell(int row, int column)
        {
            PairStatisticsDay day = _result.Days[row];
            switch (column)
            {
                case 0: return day.Date;
                case 1: return day.VolumeA;
                case 2: return day.VolumeB;
                case 3: return day.ActiveBarsA;
                case 4: return day.ActiveBarsB;
                case 5: return day.MatchedBars;
                case 6: return day.CoveragePercent;
                case 7: return day.Accepted;
                case 8: return DayReason(day);
                case 9: return day.Summary == null ? null : (object)day.Summary.Minimum;
                case 10: return day.Summary == null ? null : (object)day.Summary.Maximum;
                default: return day.Summary == null ? null : (object)day.Summary.Range;
            }
        }

        private object GetIntervalCell(int row, int column)
        {
            PairStatisticsInterval interval = _result.Intervals[row];
            switch (column)
            {
                case 0: return interval.Start;
                case 1: return interval.End;
                case 2: return interval.Days;
                case 3: return interval.Summary.Count;
                case 4: return interval.Summary.Minimum;
                case 5: return interval.Summary.Maximum;
                case 6: return interval.Summary.Range;
                case 7: return interval.Summary.Mean;
                default: return interval.Summary.StandardDeviation;
            }
        }

        private string DayReason(PairStatisticsDay day)
        {
            switch (day.Reason)
            {
                case "Accepted": return L("Passed", "Прошёл фильтр");
                case "NoMatchedBars": return L("No matched active bars", "Нет совпавших активных свечей");
                case "LowVolumeA": return L("Low daily volume A", "Низкий дневной объём A");
                case "LowVolumeB": return L("Low daily volume B", "Низкий дневной объём B");
                case "LowActiveBarsA": return L("Few active bars A", "Мало активных свечей A");
                case "LowActiveBarsB": return L("Few active bars B", "Мало активных свечей B");
                case "LowCoverage": return L("Low coverage", "Низкое покрытие");
                default: return day.Reason;
            }
        }

        private void ClearResult()
        {
            _daysTable.SetCount(0);
            _intervalsTable.SetCount(0);
            _pointsTable.SetCount(0);
            _result = null;
            TextBlockSummary.Text = "";
            TextBlockStatus.Text = "";
        }

        private void ShowResult(PairStatisticsResult result)
        {
            _result = result;
            _daysTable.SetCount(result.Days.Count);
            _intervalsTable.SetCount(result.Intervals.Count);
            _pointsTable.SetCount(result.Points.Count);
            int acceptedDays = 0;
            foreach (PairStatisticsDay day in result.Days)
            {
                if (day.Accepted)
                {
                    acceptedDays++;
                }
            }
            TextBlockStatus.Text = L("Completed. Accepted / rejected days", "Готово. Принято / отклонено дней") + " — " + acceptedDays + " / " + (result.Days.Count - acceptedDays);
            PairStatisticsSummary summary = result.Summary;
            if (summary == null)
            {
                TextBlockSummary.Text = L("No points passed the filter. Review the daily diagnostics and settings.", "Ни одна точка не прошла фильтр. Проверьте дневную диагностику и настройки.");
                return;
            }
            TextBlockSummary.Text = L("Points", "Точек") + " " + summary.Count + "   " + summary.Start.ToString("yyyy-MM-dd HH:mm:ss") + " — " + summary.End.ToString("yyyy-MM-dd HH:mm:ss")
                + "\n" + L("Min", "Мин") + " " + summary.Minimum + "   " + L("Max", "Макс") + " " + summary.Maximum + "   " + L("Range", "Размах") + " " + summary.Range
                + "   " + L("Mean", "Среднее") + " " + summary.Mean + "   " + L("Stddev (N)", "Станд. откл. (N)") + " " + summary.StandardDeviation.ToString("G8", CultureInfo.CurrentCulture);
        }

        #endregion

        #region Inputs

        private void ButtonBrowse_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using (Forms.FolderBrowserDialog dialog = new Forms.FolderBrowserDialog())
                {
                    dialog.Description = L("Select dataset folder containing Settings.txt", "Выберите папку датасета с Settings.txt");
                    dialog.UseDescriptionForTitle = true;
                    if (dialog.ShowDialog() == Forms.DialogResult.OK)
                    {
                        TextBoxFolder.Text = dialog.SelectedPath;
                    }
                }
            }
            catch (Exception error)
            {
                ShowError(error);
            }
        }

        private void TextBoxFolder_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                ClearResult();
                _catalogFolder = null;
                _catalog = null;
                ComboBoxFirst.ItemsSource = null;
                ComboBoxSecond.ItemsSource = null;
                ComboBoxTimeFrame.ItemsSource = null;
            }
            catch (Exception error)
            {
                ShowError(error);
            }
        }

        private void Instrument_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                ClearResult();
                ComboBoxTimeFrame.ItemsSource = null;
                StatisticsInstrument first = ComboBoxFirst.SelectedItem as StatisticsInstrument;
                StatisticsInstrument second = ComboBoxSecond.SelectedItem as StatisticsInstrument;
                if (first == null || second == null)
                {
                    return;
                }
                List<string> common = new List<string>();
                foreach (string timeframe in first.CandleFiles.Keys)
                {
                    if (second.CandleFiles.ContainsKey(timeframe))
                    {
                        common.Add(timeframe);
                    }
                }
                common.Sort(StringComparer.Ordinal);
                ComboBoxTimeFrame.ItemsSource = common;
                if (common.Count == 0)
                {
                    TextBlockStatus.Text = L("No common candle timeframe.", "Нет общего свечного таймфрейма.");
                }
            }
            catch (Exception error)
            {
                ShowError(error);
            }
        }

        private void Input_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                ClearResult();
            }
            catch (Exception error)
            {
                ShowError(error);
            }
        }

        private void Input_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                ClearResult();
            }
            catch (Exception error)
            {
                ShowError(error);
            }
        }

        private PairStatisticsOptions ReadOptions()
        {
            int barsA;
            int barsB;
            if (!int.TryParse(TextBoxBarsA.Text, out barsA) || !int.TryParse(TextBoxBarsB.Text, out barsB))
            {
                throw new ArgumentException(L("Active bar thresholds must be positive integers.", "Пороги активных свечей должны быть положительными целыми числами."));
            }
            PairStatisticsOptions options = new PairStatisticsOptions
            {
                From = ReadDate(DatePickerFrom),
                To = ReadDate(DatePickerTo),
                MinVolumeA = ReadDecimal(TextBoxVolumeA.Text),
                MinVolumeB = ReadDecimal(TextBoxVolumeB.Text),
                MinActiveBarsA = barsA,
                MinActiveBarsB = barsB,
                MinCoveragePercent = ReadDecimal(TextBoxCoverage.Text)
            };
            if (string.IsNullOrWhiteSpace(TextBoxVolumeA.Text) || string.IsNullOrWhiteSpace(TextBoxVolumeB.Text) || string.IsNullOrWhiteSpace(TextBoxCoverage.Text)
                || options.MinVolumeA < 0 || options.MinVolumeB < 0 || barsA < 1 || barsB < 1 || options.MinCoveragePercent < 0 || options.MinCoveragePercent > 100)
            {
                throw new ArgumentException(L("Volumes must be nonnegative, active bars positive, coverage between 0 and 100.", "Объёмы должны быть неотрицательными, число свечей положительным, покрытие от 0 до 100."));
            }
            if (options.From.HasValue && options.To.HasValue && options.From.Value > options.To.Value)
            {
                throw new ArgumentException(L("Start date must not exceed end date.", "Начальная дата не должна быть позже конечной."));
            }
            return options;
        }

        private decimal ReadDecimal(string text)
        {
            string number = text.Trim();
            if (!Regex.IsMatch(number, @"^[+-]?[0-9]+([.,][0-9]+)?$"))
            {
                throw new ArgumentException(L("Enter a valid number using a dot or comma decimal separator.", "Введите корректное число с точкой или запятой в качестве разделителя."));
            }
            decimal validated;
            if (!decimal.TryParse(number, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out validated)
                && !decimal.TryParse(number, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.GetCultureInfo("ru-RU"), out validated))
            {
                throw new ArgumentException(L("Number is outside the decimal range.", "Число выходит за допустимый диапазон decimal."));
            }
            return number.ToDecimal();
        }

        private DateTime? ReadDate(DatePicker picker)
        {
            if (string.IsNullOrWhiteSpace(picker.Text))
            {
                return null;
            }
            DateTime date;
            if (!DateTime.TryParse(picker.Text, OsLocalization.CurCulture, DateTimeStyles.AllowWhiteSpaces, out date))
            {
                throw new ArgumentException(L("Invalid date. Select it using the calendar.", "Некорректная дата. Выберите её в календаре."));
            }
            return date.Date;
        }

        private void DatePicker_DateValidationError(object sender, DatePickerDateValidationErrorEventArgs e)
        {
            try
            {
                e.ThrowException = false;
                ClearResult();
                ShowError(new ArgumentException(L("Invalid date. Select it using the calendar.", "Некорректная дата. Выберите её в календаре."), e.Exception));
            }
            catch (Exception error)
            {
                ShowError(error);
            }
        }

        #endregion

        #region Background operations

        private void ButtonPairs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_catalog == null || _catalogFolder == null)
                {
                    throw new ArgumentException(L("Load the dataset catalog first.", "Сначала загрузите каталог датасета."));
                }
                StatisticsPairsUi window = new StatisticsPairsUi(new List<StatisticsInstrument>(_catalog), _catalogFolder, ReadOptions(), ComboBoxTimeFrame.SelectedItem as string);
                window.Owner = this;
                window.Show();
            }
            catch (Exception error)
            {
                ShowError(error);
            }
        }

        private void ButtonLoad_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string folder = Path.GetFullPath(TextBoxFolder.Text.Trim());
                ClearResult();
                _catalogFolder = null;
                _catalog = null;
                ComboBoxFirst.ItemsSource = null;
                ComboBoxSecond.ItemsSource = null;
                ComboBoxTimeFrame.ItemsSource = null;
                StartWork(delegate(CancellationToken token)
                {
                    List<StatisticsInstrument> catalog = new StatisticsDatasetReader().ReadCatalog(folder, token);
                    return delegate
                    {
                        _catalogFolder = folder;
                        _catalog = catalog;
                        ComboBoxFirst.ItemsSource = catalog;
                        ComboBoxSecond.ItemsSource = new List<StatisticsInstrument>(catalog);
                        ComboBoxFirst.SelectedIndex = -1;
                        ComboBoxSecond.SelectedIndex = -1;
                        TextBlockStatus.Text = L("Select both legs and a common timeframe. Instruments", "Выберите обе ноги и общий таймфрейм. Инструментов") + " — " + catalog.Count;
                    };
                });
            }
            catch (Exception error)
            {
                ShowError(error);
            }
        }

        private void ButtonRun_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StatisticsInstrument first = ComboBoxFirst.SelectedItem as StatisticsInstrument;
                StatisticsInstrument second = ComboBoxSecond.SelectedItem as StatisticsInstrument;
                string timeframe = ComboBoxTimeFrame.SelectedItem as string;
                if (_catalogFolder == null || first == null || second == null || timeframe == null)
                {
                    throw new ArgumentException(L("Load a dataset, select A, B and a common timeframe.", "Загрузите датасет, выберите A, B и общий таймфрейм."));
                }
                string pathA = first.CandleFiles[timeframe];
                string pathB = second.CandleFiles[timeframe];
                if (string.Equals(Path.GetFullPath(pathA), Path.GetFullPath(pathB), StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException(L("Select two different instruments.", "Выберите два разных инструмента."));
                }
                PairStatisticsOptions options = ReadOptions();
                ClearResult();
                StartWork(delegate(CancellationToken token)
                {
                    PairStatisticsResult result;
                    using (FileStream guardA = new FileStream(pathA, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (FileStream guardB = new FileStream(pathB, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        StatisticsDatasetReader reader = new StatisticsDatasetReader();
                        List<StatisticsCandle> candlesA = reader.ReadCandles(pathA, options.From, options.To, token);
                        List<StatisticsCandle> candlesB = reader.ReadCandles(pathB, options.From, options.To, token);
                        result = new PairStatisticsAnalyzer().Analyze(candlesA, candlesB, options, token);
                    }
                    return delegate { ShowResult(result); };
                });
            }
            catch (Exception error)
            {
                ShowError(error);
            }
        }

        private void StartWork(Func<CancellationToken, Action> work)
        {
            if (_busy || _closed)
            {
                return;
            }
            _cancellation = new CancellationTokenSource();
            CancellationTokenSource cancellation = _cancellation;
            SetBusy(true);
            TextBlockStatus.Text = L("Reading and calculating…", "Чтение и расчёт…");
            Task.Run(delegate
            {
                try
                {
                    Action publish = work(cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    DispatchCompletion(cancellation, publish, null);
                }
                catch (OperationCanceledException)
                {
                    DispatchCompletion(cancellation, null, null);
                }
                catch (Exception error)
                {
                    ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error);
                    DispatchCompletion(cancellation, null, error);
                }
                finally
                {
                    cancellation.Dispose();
                }
            });
        }

        private void DispatchCompletion(CancellationTokenSource cancellation, Action publish, Exception error)
        {
            try
            {
                if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                {
                    return;
                }
                Dispatcher.Invoke(new Action(delegate
                {
                    try
                    {
                        if (_closed || _cancellation != cancellation)
                        {
                            return;
                        }
                        bool cancelled = cancellation.IsCancellationRequested;
                        _cancellation = null;
                        SetBusy(false);
                        if (cancelled || (publish == null && error == null))
                        {
                            TextBlockStatus.Text = L("Cancelled. No result published.", "Отменено. Результат не опубликован.");
                        }
                        else if (error != null)
                        {
                            TextBlockStatus.Text = L("Error", "Ошибка") + " — " + error.Message;
                        }
                        else
                        {
                            publish();
                        }
                    }
                    catch (Exception callbackError)
                    {
                        ShowError(callbackError);
                    }
                }));
            }
            catch (Exception dispatchError)
            {
                ServerMaster.SendNewLogMessage(dispatchError.ToString(), LogMessageType.Error);
            }
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            GridInputs.IsEnabled = !busy;
            ButtonRun.IsEnabled = !busy;
            ButtonCancel.IsEnabled = busy;
        }

        private void ButtonCancel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _cancellation?.Cancel();
                ButtonCancel.IsEnabled = false;
                TextBlockStatus.Text = L("Cancelling…", "Отмена…");
            }
            catch (Exception error)
            {
                ShowError(error);
            }
        }

        private void ShowError(Exception error)
        {
            ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error);
            if (!_closed)
            {
                TextBlockStatus.Text = L("Error", "Ошибка") + " — " + error.Message;
            }
        }

        #endregion

        #region Cleanup

        private void Theme_Changed()
        {
            try
            {
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.Invoke(new Action(Theme_Changed));
                    return;
                }
                if (_closed)
                {
                    return;
                }
                DataGridFactory.ApplyTheme(_daysGrid);
                DataGridFactory.ApplyTheme(_intervalsGrid);
                DataGridFactory.ApplyTheme(_pointsGrid);
            }
            catch (Exception error)
            {
                ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error);
            }
        }

        private void Localization_Changed()
        {
            try
            {
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.Invoke(new Action(Localization_Changed));
                    return;
                }
                if (_closed)
                {
                    return;
                }
                PairStatisticsResult previousResult = _result;
                Localize();
                string[][] headers = new string[][]
                {
                    new string[] { L("Date", "Дата"), L("Volume A", "Объём A"), L("Volume B", "Объём B"), L("Active A", "Активных A"), L("Active B", "Активных B"), L("Matched", "Совпало"), L("Coverage %", "Покрытие %"), L("Accepted", "Принят"), L("Reason", "Причина"), L("Min", "Мин"), L("Max", "Макс"), L("Range", "Размах") },
                    new string[] { L("Start", "Начало"), L("End", "Конец"), L("Days", "Дней"), L("Points", "Точек"), L("Min", "Мин"), L("Max", "Макс"), L("Range", "Размах"), L("Mean", "Среднее"), L("Stddev N", "Станд. откл. N") },
                    new string[] { L("Time", "Время"), "Close A − Close B" }
                };
                Forms.DataGridView[] grids = new Forms.DataGridView[] { _daysGrid, _intervalsGrid, _pointsGrid };
                for (int gridIndex = 0; gridIndex < grids.Length; gridIndex++)
                {
                    for (int column = 0; column < headers[gridIndex].Length; column++)
                    {
                        grids[gridIndex].Columns[column].HeaderText = headers[gridIndex][column];
                    }
                    grids[gridIndex].Invalidate();
                }
                if (previousResult != null)
                {
                    ShowResult(previousResult);
                }
            }
            catch (Exception error)
            {
                ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error);
            }
        }

        private void StatisticsUi_Closed(object sender, EventArgs e)
        {
            try
            {
                _closed = true;
                _cancellation?.Cancel();
                _cancellation = null;
                OsLocalization.LocalizationTypeChangeEvent -= Localization_Changed;
                Themes.ThemeManager.ThemeChangedEvent -= Theme_Changed;
                ButtonBrowse.Click -= ButtonBrowse_Click;
                ButtonPairs.Click -= ButtonPairs_Click;
                ButtonLoad.Click -= ButtonLoad_Click;
                ButtonRun.Click -= ButtonRun_Click;
                ButtonCancel.Click -= ButtonCancel_Click;
                TextBoxFolder.TextChanged -= TextBoxFolder_TextChanged;
                ComboBoxFirst.SelectionChanged -= Instrument_SelectionChanged;
                ComboBoxSecond.SelectionChanged -= Instrument_SelectionChanged;
                ComboBoxTimeFrame.SelectionChanged -= Input_SelectionChanged;
                DatePickerFrom.SelectedDateChanged -= Input_SelectionChanged;
                DatePickerTo.SelectedDateChanged -= Input_SelectionChanged;
                DatePickerFrom.RemoveHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(Input_TextChanged));
                DatePickerTo.RemoveHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(Input_TextChanged));
                DatePickerFrom.DateValidationError -= DatePicker_DateValidationError;
                DatePickerTo.DateValidationError -= DatePicker_DateValidationError;
                TextBoxVolumeA.TextChanged -= Input_TextChanged;
                TextBoxVolumeB.TextChanged -= Input_TextChanged;
                TextBoxBarsA.TextChanged -= Input_TextChanged;
                TextBoxBarsB.TextChanged -= Input_TextChanged;
                TextBoxCoverage.TextChanged -= Input_TextChanged;
                Closed -= StatisticsUi_Closed;
                HostDays.Child = null;
                HostIntervals.Child = null;
                HostPoints.Child = null;
                _daysTable.Dispose();
                _intervalsTable.Dispose();
                _pointsTable.Dispose();
                _daysGrid = null;
                _intervalsGrid = null;
                _pointsGrid = null;
                HostDays.Dispose();
                HostIntervals.Dispose();
                HostPoints.Dispose();
                ComboBoxFirst.ItemsSource = null;
                ComboBoxSecond.ItemsSource = null;
                ComboBoxTimeFrame.ItemsSource = null;
                _result = null;
                _catalog = null;
            }
            catch (Exception error)
            {
                ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error);
            }
        }

        #endregion
    }
}
