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

namespace OsEngine.OsData.Statistics
{
    public partial class StatisticsPairsUi
    {
        private readonly List<StatisticsInstrument> _instruments;
        private readonly string _folder;
        private readonly PairStatisticsOptions _options;
        private readonly string _preferredTimeFrame;
        private readonly StatisticsSortableGrid _table;
        private List<StatisticsPair> _pairs = new List<StatisticsPair>();
        private CancellationTokenSource _cancellation;
        private bool _closed;
        private bool _busy;

        public StatisticsPairsUi(List<StatisticsInstrument> instruments, string folder, PairStatisticsOptions options, string preferredTimeFrame)
        {
            InitializeComponent();
            _instruments = instruments;
            _folder = folder;
            _options = new PairStatisticsOptions
            {
                From = options.From, To = options.To,
                MinVolumeA = options.MinVolumeA, MinVolumeB = options.MinVolumeB,
                MinActiveBarsA = options.MinActiveBarsA, MinActiveBarsB = options.MinActiveBarsB,
                MinCoveragePercent = options.MinCoveragePercent
            };
            _preferredTimeFrame = preferredTimeFrame;
            Localize();
            _table = new StatisticsSortableGrid(HostPairs, Headers(), GetCell);
            _table.Grid.SelectionChanged += Grid_SelectionChanged;
            _table.Grid.Columns[0].Width = 240;
            _table.Grid.Columns[4].Width = 240;
            ButtonBuild.Click += ButtonBuild_Click;
            ButtonGraph.Click += ButtonGraph_Click;
            ButtonStatistics.Click += ButtonStatistics_Click;
            ButtonCancel.Click += ButtonCancel_Click;
            TextBoxPrefixA.TextChanged += Prefix_TextChanged;
            TextBoxPrefixB.TextChanged += Prefix_TextChanged;
            OsLocalization.LocalizationTypeChangeEvent += Localization_Changed;
            Themes.ThemeManager.ThemeChangedEvent += Theme_Changed;
            Closed += Window_Closed;
            Layout.StickyBorders.Listen(this);
            Layout.StartupLocation.Start_FitHeightToWorkArea(this);
            ButtonBuild_Click(null, null);
        }

        #region Catalog

        private static string L(string english, string russian)
        {
            return OsLocalization.ConvertToLocString("Eng:" + english + "_Ru:" + russian + "_");
        }

        private string[] Headers()
        {
            return new string[] { L("Pair A − B", "Пара A − B"), L("Expiry code", "Код экспирации"), L("Leg A", "Нога A"), L("Leg B", "Нога B"), L("Common timeframes", "Общие таймфреймы") };
        }

        private void Localize()
        {
            Title = L("Pairs by expiry", "Пары по экспирации");
            LabelPrefixA.Content = L("Prefix A", "Префикс A");
            LabelPrefixB.Content = L("Prefix B", "Префикс B");
            ButtonBuild.Content = L("Find pairs", "Найти пары");
            LabelTimeFrame.Content = L("Timeframe", "Таймфрейм");
            LabelSma.Content = L("SMA observations", "SMA наблюдений");
            ButtonGraph.Content = L("Chart", "График");
            ButtonStatistics.Content = L("Statistics", "Статистика");
            ButtonCancel.Content = L("Cancel", "Отмена");
            TextBlockSnapshot.Text = L("Dataset snapshot", "Снимок параметров датасета") + " — " + _folder
                + "\n" + (_options.From.HasValue ? _options.From.Value.ToString("yyyy-MM-dd") : L("Beginning", "Начало истории")) + " — " + (_options.To.HasValue ? _options.To.Value.ToString("yyyy-MM-dd") : L("End", "Конец истории"))
                + "   " + L("Min volumes A/B", "Мин. объёмы A/B") + " " + _options.MinVolumeA + "/" + _options.MinVolumeB
                + "   " + L("Active bars A/B", "Активные свечи A/B") + " " + _options.MinActiveBarsA + "/" + _options.MinActiveBarsB
                + "   " + L("Coverage", "Покрытие") + " " + _options.MinCoveragePercent + "%"
                + "\n" + L("Two-letter prefixes, matching expiry codes only. Verify actual expiry dates and quote units. Settings are fixed for this window. To change daily filters, reopen from the main statistics window.", "Двухбуквенные префиксы, только совпадающий код экспирации. Проверьте фактические даты экспирации и единицы котирования. Настройки этого окна зафиксированы. Для изменения дневных фильтров откройте его заново из основного окна статистики.");
        }

        private object GetCell(int row, int column)
        {
            StatisticsPair pair = _pairs[row];
            switch (column)
            {
                case 0: return pair.Name;
                case 1: return pair.Expiry;
                case 2: return string.IsNullOrEmpty(pair.First.FullName) ? pair.First.Name : pair.First.Name + " — " + pair.First.FullName;
                case 3: return string.IsNullOrEmpty(pair.Second.FullName) ? pair.Second.Name : pair.Second.Name + " — " + pair.Second.FullName;
                default: return string.Join(", ", pair.TimeFrames);
            }
        }

        private void Prefix_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                _table.SetCount(0);
                _pairs = new List<StatisticsPair>();
                ComboBoxTimeFrame.ItemsSource = null;
                TextBlockStatus.Text = L("Click Find pairs after changing prefixes.", "После изменения префиксов нажмите Найти пары.");
            }
            catch (Exception error) { ShowError(error); }
        }

        private void Grid_SelectionChanged(object sender, EventArgs e)
        {
            try
            {
                string previousTimeFrame = ComboBoxTimeFrame.SelectedItem as string;
                ComboBoxTimeFrame.ItemsSource = null;
                int selected = _table.SelectedSourceIndex;
                if (selected < 0 || selected >= _pairs.Count) return;
                List<string> frames = _pairs[selected].TimeFrames;
                ComboBoxTimeFrame.ItemsSource = frames;
                if (previousTimeFrame != null && frames.Contains(previousTimeFrame)) ComboBoxTimeFrame.SelectedItem = previousTimeFrame;
                else if (_preferredTimeFrame != null && frames.Contains(_preferredTimeFrame)) ComboBoxTimeFrame.SelectedItem = _preferredTimeFrame;
            }
            catch (Exception error) { ShowError(error); }
        }

        private void ButtonBuild_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string prefixA = TextBoxPrefixA.Text.Trim().ToUpperInvariant();
                string prefixB = TextBoxPrefixB.Text.Trim().ToUpperInvariant();
                if (!Regex.IsMatch(prefixA, "^[A-Z]{2}$") || !Regex.IsMatch(prefixB, "^[A-Z]{2}$") || prefixA == prefixB)
                {
                    throw new ArgumentException(L("Enter two different two-letter Latin prefixes.", "Введите два разных префикса из двух латинских букв."));
                }
                _table.SetCount(0);
                _pairs = new List<StatisticsPair>();
                ComboBoxTimeFrame.ItemsSource = null;
                StartWork(delegate(CancellationToken token)
                {
                    List<StatisticsPair> pairs = new StatisticsPairCatalog().BuildPairs(_instruments, prefixA, prefixB, token);
                    return delegate
                    {
                        _pairs = pairs;
                        _table.SetCount(pairs.Count);
                        TextBlockStatus.Text = pairs.Count == 0
                            ? L("No same-expiry pairs with common candle files were found.", "Пар с одинаковой экспирацией и общими свечными файлами не найдено.")
                            : L("Select a pair and timeframe. Pairs", "Выберите пару и таймфрейм. Пар") + " — " + pairs.Count;
                    };
                });
            }
            catch (Exception error) { ShowError(error); }
        }

        #endregion

        #region Research

        private void ButtonGraph_Click(object sender, RoutedEventArgs e)
        {
            try { StartResearch(false); }
            catch (Exception error) { ShowError(error); }
        }

        private void ButtonStatistics_Click(object sender, RoutedEventArgs e)
        {
            try { StartResearch(true); }
            catch (Exception error) { ShowError(error); }
        }

        private void StartResearch(bool showStatistics)
        {
            int selected = _table.SelectedSourceIndex;
            string timeframe = ComboBoxTimeFrame.SelectedItem as string;
            int period;
            if (selected < 0 || selected >= _pairs.Count || timeframe == null)
            {
                throw new ArgumentException(L("Select a pair and its common timeframe.", "Выберите пару и её общий таймфрейм."));
            }
            if (!int.TryParse(TextBoxSma.Text, NumberStyles.None, CultureInfo.InvariantCulture, out period) || period < 2 || period > StatisticsDatasetReader.MaximumCandles)
            {
                throw new ArgumentException(L("SMA period must be an integer from 2 to 2000000.", "Период SMA должен быть целым числом от 2 до 2000000."));
            }
            StatisticsPair pair = _pairs[selected];
            string pathA = pair.First.CandleFiles[timeframe];
            string pathB = pair.Second.CandleFiles[timeframe];
            PairResearchOptions researchOptions = new PairResearchOptions { SmaPeriod = period, TimeFrameDuration = StatisticsPairCatalog.GetTimeFrameDuration(timeframe) };
            StartWork(delegate(CancellationToken token)
            {
                PairResearchResult research;
                using (FileStream guardA = new FileStream(pathA, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (FileStream guardB = new FileStream(pathB, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    StatisticsDatasetReader reader = new StatisticsDatasetReader();
                    List<StatisticsCandle> first = reader.ReadCandles(pathA, _options.From, _options.To, token);
                    List<StatisticsCandle> second = reader.ReadCandles(pathB, _options.From, _options.To, token);
                    PairStatisticsResult accepted = new PairStatisticsAnalyzer().Analyze(first, second, _options, token);
                    research = new PairResearchAnalyzer().Analyze(accepted, first, second, researchOptions, token);
                }
                return delegate
                {
                    StatisticsResearchUi window = new StatisticsResearchUi(research, pair.Name, timeframe, period, showStatistics);
                    window.Owner = this;
                    window.Show();
                    TextBlockStatus.Text = L("Research opened", "Исследование открыто") + " — " + pair.Name;
                };
            });
        }

        private void StartWork(Func<CancellationToken, Action> work)
        {
            if (_busy || _closed) return;
            CancellationTokenSource cancellation = new CancellationTokenSource();
            _cancellation = cancellation;
            SetBusy(true);
            TextBlockStatus.Text = L("Reading and calculating…", "Чтение и расчёт…");
            Task.Run(delegate
            {
                try
                {
                    Action publish = work(cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    Complete(cancellation, publish, null);
                }
                catch (OperationCanceledException) { Complete(cancellation, null, null); }
                catch (Exception error)
                {
                    ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error);
                    Complete(cancellation, null, error);
                }
                finally { cancellation.Dispose(); }
            });
        }

        private void Complete(CancellationTokenSource cancellation, Action publish, Exception error)
        {
            try
            {
                if (Dispatcher.HasShutdownStarted) return;
                Dispatcher.Invoke(new Action(delegate
                {
                    try
                    {
                        if (_closed || _cancellation != cancellation) return;
                        _cancellation = null;
                        SetBusy(false);
                        if (cancellation.IsCancellationRequested || (publish == null && error == null)) TextBlockStatus.Text = L("Cancelled", "Отменено");
                        else if (error != null) ShowError(error);
                        else publish();
                    }
                    catch (Exception callbackError) { ShowError(callbackError); }
                }));
            }
            catch (Exception dispatchError) { ServerMaster.SendNewLogMessage(dispatchError.ToString(), LogMessageType.Error); }
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            GridPrefixes.IsEnabled = !busy;
            GridActions.IsEnabled = !busy;
            _table.Grid.Enabled = !busy;
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
            catch (Exception error) { ShowError(error); }
        }

        private void ShowError(Exception error)
        {
            ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error);
            if (!_closed) TextBlockStatus.Text = L("Error", "Ошибка") + " — " + error.Message;
        }

        #endregion

        #region Appearance and cleanup

        private void Localization_Changed()
        {
            try
            {
                if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(new Action(Localization_Changed)); return; }
                if (_closed) return;
                Localize();
                _table.RefreshHeaders(Headers());
            }
            catch (Exception error) { ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error); }
        }

        private void Theme_Changed()
        {
            try
            {
                if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(new Action(Theme_Changed)); return; }
                if (!_closed) _table.ApplyTheme();
            }
            catch (Exception error) { ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error); }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            try
            {
                _closed = true;
                _cancellation?.Cancel();
                _cancellation = null;
                OsLocalization.LocalizationTypeChangeEvent -= Localization_Changed;
                Themes.ThemeManager.ThemeChangedEvent -= Theme_Changed;
                ButtonBuild.Click -= ButtonBuild_Click;
                ButtonGraph.Click -= ButtonGraph_Click;
                ButtonStatistics.Click -= ButtonStatistics_Click;
                ButtonCancel.Click -= ButtonCancel_Click;
                TextBoxPrefixA.TextChanged -= Prefix_TextChanged;
                TextBoxPrefixB.TextChanged -= Prefix_TextChanged;
                _table.Grid.SelectionChanged -= Grid_SelectionChanged;
                _table.Dispose();
                HostPairs.Dispose();
                ComboBoxTimeFrame.ItemsSource = null;
                Closed -= Window_Closed;
            }
            catch (Exception error) { ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error); }
        }

        #endregion
    }
}
