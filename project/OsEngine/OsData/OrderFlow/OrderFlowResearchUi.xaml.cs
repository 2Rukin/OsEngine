/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using OsEngine.Entity;
using OsEngine.Language;
using OsEngine.Logging;
using OsEngine.Market;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OsEngine.OsData.OrderFlow
{
    /// <summary>
    /// Offline paired-QSH workbench for inspecting causal Order Flow research
    /// features, candidates and separately computed future market-path labels.
    /// </summary>
    public partial class OrderFlowResearchUi
    {
        private OrderFlowResearchChart _chart;
        private Thread _analysisThread;
        private CancellationTokenSource _cancellation;
        private OrderFlowResearchRequest _pendingRequest;
        private OrderFlowResearchResult _completedResult;
        private Exception _workerError;
        private bool _workerCancelled;
        private volatile bool _isClosing;
        private bool _updatingChartRange;

        /// <summary>
        /// Creates the research-only workbench. The window does not start a
        /// replay or access external systems until the user selects local files.
        /// </summary>
        public OrderFlowResearchUi()
        {
            InitializeComponent();
            Layout.StickyBorders.Listen(this);
            Layout.StartupLocation.Start_FitHeightToWorkArea(this);

            _chart = new OrderFlowResearchChart();
            ContentControlChart.Content = _chart;
            ComboBoxTimeFrame.ItemsSource = Enum.GetValues(typeof(OrderFlowDisplayTimeFrame));
            ComboBoxTimeFrame.SelectedItem = OrderFlowDisplayTimeFrame.Min1;

            ButtonBrowseDeals.Click += ButtonBrowseDeals_Click;
            ButtonBrowseQuotes.Click += ButtonBrowseQuotes_Click;
            ButtonBrowseOutput.Click += ButtonBrowseOutput_Click;
            ButtonRun.Click += ButtonRun_Click;
            ButtonCancel.Click += ButtonCancel_Click;
            ButtonOpenArtifacts.Click += ButtonOpenArtifacts_Click;
            ComboBoxTimeFrame.SelectionChanged += ComboBoxTimeFrame_SelectionChanged;
            DataGridCandidates.SelectionChanged += DataGridCandidates_SelectionChanged;
            DataGridCandidates.MouseDoubleClick += DataGridCandidates_MouseDoubleClick;
            Closing += OrderFlowResearchUi_Closing;

            _chart.ViewChanged += Chart_ViewChanged;
            ScrollBarChart.ValueChanged += ScrollBarChart_ValueChanged;
            ButtonChartFirst.Click += ButtonChartNavigation_Click;
            ButtonChartLast.Click += ButtonChartNavigation_Click;
            ButtonChartSelected.Click += ButtonChartNavigation_Click;
            ButtonChartZoomIn.Click += ButtonChartNavigation_Click;
            ButtonChartZoomOut.Click += ButtonChartNavigation_Click;
            ButtonChartAll.Click += ButtonChartNavigation_Click;
            ApplyLocalization();
            Chart_ViewChanged(this, EventArgs.Empty);
        }

        private void ApplyLocalization()
        {
            Title = OsLocalization.ConvertToLocString("Eng:Order Flow Research_Ru:Исследование Order Flow_");
            LabelDeals.Content = OsLocalization.ConvertToLocString("Eng:Deals QSH_Ru:Сделки QSH_");
            LabelQuotes.Content = OsLocalization.ConvertToLocString("Eng:Quotes QSH_Ru:Стакан QSH_");
            LabelOutput.Content = OsLocalization.ConvertToLocString("Eng:Output folder_Ru:Папка результатов_");
            ButtonBrowseDeals.Content = OsLocalization.ConvertToLocString("Eng:Browse_Ru:Выбрать_");
            ButtonBrowseQuotes.Content = OsLocalization.ConvertToLocString("Eng:Browse_Ru:Выбрать_");
            ButtonBrowseOutput.Content = OsLocalization.ConvertToLocString("Eng:Browse_Ru:Выбрать_");
            LabelWindow.Content = OsLocalization.ConvertToLocString("Eng:Window sec_Ru:Окно сек_");
            LabelDelta.Content = OsLocalization.ConvertToLocString("Eng:Min delta_Ru:Мин дельта_");
            LabelPriceTicks.Content = OsLocalization.ConvertToLocString("Eng:Price ticks_Ru:Тики цены_");
            LabelBookLevels.Content = OsLocalization.ConvertToLocString("Eng:Book levels_Ru:Уровни стакана_");
            LabelBookAge.Content = OsLocalization.ConvertToLocString("Eng:Book age ms_Ru:Возраст стакана мс_");
            LabelCooldown.Content = OsLocalization.ConvertToLocString("Eng:Cooldown ms_Ru:Пауза мс_");
            LabelBackground.Content = OsLocalization.ConvertToLocString("Eng:Background sec_Ru:Фон сек_");
            LabelHorizons.Content = OsLocalization.ConvertToLocString("Eng:Horizons sec_Ru:Горизонты сек_");
            LabelTarget.Content = OsLocalization.ConvertToLocString("Eng:Target ticks_Ru:Цель тики_");
            LabelInvalidation.Content = OsLocalization.ConvertToLocString("Eng:Adverse ticks_Ru:Против, тики_");
            LabelPriceStep.Content = OsLocalization.ConvertToLocString("Eng:Price step override_Ru:Шаг цены вручную_");
            LabelVolumeStep.Content = OsLocalization.ConvertToLocString("Eng:Volume step override_Ru:Шаг объема вручную_");
            ButtonRun.Content = OsLocalization.ConvertToLocString("Eng:Run research_Ru:Запустить_");
            ButtonCancel.Content = OsLocalization.ConvertToLocString("Eng:Cancel_Ru:Отмена_");
            ButtonOpenArtifacts.Content = OsLocalization.ConvertToLocString("Eng:Open artifacts_Ru:Открыть файлы_");
            TabItemSummary.Header = OsLocalization.ConvertToLocString("Eng:Summary_Ru:Сводка_");
            TabItemCandidates.Header = OsLocalization.ConvertToLocString("Eng:Candidates_Ru:Кандидаты_");
            TabItemChart.Header = OsLocalization.ConvertToLocString("Eng:Chart_Ru:График_");
            TabItemJournal.Header = OsLocalization.ConvertToLocString("Eng:Event journal_Ru:Журнал событий_");
            LabelTimeFrame.Content = OsLocalization.ConvertToLocString("Eng:Display timeframe_Ru:Таймфрейм отображения_");
            TextBlockChartBoundary.Text = OsLocalization.ConvertToLocString(
                "Eng:Visualization only. Signals are not recalculated._Ru:Только визуализация. Сигналы не пересчитываются._");
            ButtonChartFirst.Content = L("Start", "Начало");
            ButtonChartLast.Content = L("End", "Конец");
            ButtonChartSelected.Content = L("To candidate", "К кандидату");
            ButtonChartAll.Content = L("All history", "Весь период");
            ButtonChartZoomIn.ToolTip = L("Zoom in: fewer bars", "Приблизить: меньше свечей");
            ButtonChartZoomOut.ToolTip = L("Zoom out: more bars", "Отдалить: больше свечей");
            TextBlockChartBoundary.Text = L("Wheel / scrollbar: move through the file. Timeframe changes display only. Hover for bar values.",
                "Колесо / полоса прокрутки: перемещение по файлу. Таймфрейм меняет только отображение. Наведите мышь для значений свечи.");
            SetParameterHelp();
            TextBlockStatus.Text = OsLocalization.ConvertToLocString("Eng:Ready_Ru:Готово_");
            TextBlockEvidence.Text = OsLocalization.ConvertToLocString(
                "Eng:Research only. No trades or PnL._Ru:Только исследование. Без сделок и PnL._");
        }

        private static string L(string english, string russian)
        {
            return OsLocalization.CurLocalization == OsLocalization.OsLocalType.Ru ? russian : english;
        }

        private void SetParameterHelp()
        {
            TextBoxWindowSeconds.ToolTip = LabelWindow.ToolTip = L("Trailing trade window in seconds. 180 / 540 / 1080 = 3 / 9 / 18 minutes; independent of chart timeframe.",
                "Сколько секунд сделок брать назад для признаков. 180 / 540 / 1080 = 3 / 9 / 18 минут; не размер свечи.");
            TextBoxMinimumDelta.ToolTip = LabelDelta.ToolTip = L("Minimum absolute buy minus sell volume. Candidate also requires price resilience against the dominant flow.",
                "Минимальный перевес объёма покупок или продаж. Дельта = покупки − продажи. Для кандидата дополнительно проверяется цена против потока.");
            TextBoxMinimumPriceTicks.ToolTip = LabelPriceTicks.ToolTip = L("Minimum price move against the dominant flow in ticks. Long: negative delta and rising/holding price; Short is mirrored. Zero allows unchanged price.",
                "Минимальное движение цены против потока в тиках. Long: отрицательная дельта и рост/удержание цены; Short зеркально. Ноль допускает неизменную цену. Сравниваются VWAP начального и текущего timestamp окна.");
            TextBoxTopLevels.ToolTip = LabelBookLevels.ToolTip = L("Number of nearest price levels on EACH side used for book volume imbalance.",
                "Сколько ближайших ценовых уровней с КАЖДОЙ стороны брать для дисбаланса объёмов стакана.");
            TextBoxBookAge.ToolTip = LabelBookAge.ToolTip = L("Maximum age of the previous valid book in milliseconds. 1000 = 1 second. Older books are flagged; diagnostic candidates remain.",
                "Предельный возраст предыдущего валидного стакана, мс. 1000 = 1 секунда. Более старый помечается как устаревший; диагностический кандидат сохраняется.");
            TextBoxCooldown.ToolTip = LabelCooldown.ToolTip = L("Minimum interval between candidates of the same direction, in milliseconds. Long and Short use separate timers. 5000 = 5 seconds.",
                "Минимальный интервал между кандидатами одного направления, мс. Для Long и Short отсчёт отдельный. 5000 = 5 секунд.");
            TextBoxBackground.ToolTip = LabelBackground.ToolTip = L("Interval for background observations in seconds, at available trade times. A candidate at that time replaces the background row.",
                "Интервал обычных фоновых наблюдений, сек, при наличии сделок. Если в этот момент создан кандидат, отдельная фоновая запись не добавляется.");
            TextBoxHorizons.ToolTip = LabelHorizons.ToolTip = L("Future intervals after each candidate, separated by semicolons. 60;300;900 = 1, 5, 15 minutes. Separate from the trailing feature window.",
                "На сколько секунд смотреть вперёд от кандидата. 60;300;900 = 1, 5, 15 минут. Каждому горизонту соответствует отдельная оценка; это не окно признаков.");
            TextBoxTargetTicks.ToolTip = LabelTarget.ToolTip = L("Favorable distance from candidate reference price in ticks: up for Long, down for Short. Historical label only, no order.",
                "Расстояние от опорной цены в сторону кандидата, в тиках: вверх для Long, вниз для Short. Только оценка истории, без заявки.");
            TextBoxInvalidationTicks.ToolTip = LabelInvalidation.ToolTip = L("Adverse distance from candidate reference price in ticks: down for Long, up for Short. Not an order cancellation.",
                "Расстояние от опорной цены против кандидата, в тиках: вниз для Long, вверх для Short. Это граница неблагоприятного движения, а не отмена заявки.");
            TextBoxPriceStep.ToolTip = LabelPriceStep.ToolTip = L("Price distance for one tick. Leave blank to use QSH header metadata.",
                "Размер одного тика в единицах цены. Пусто — взять шаг из QSH header.");
            TextBoxVolumeStep.ToolTip = LabelVolumeStep.ToolTip = L("Confirmed volume unit override when QSH metadata is absent. Leave blank to use metadata.",
                "Подтверждённый шаг объёма, если его нет в QSH metadata. Пусто — взять из metadata; не подбирается автоматически.");
        }

        private void Chart_ViewChanged(object sender, EventArgs e)
        {
            try
            {
                if (_chart == null) { return; }
                _updatingChartRange = true;
                ScrollBarChart.Maximum = Math.Max(0, _chart.TotalBars - _chart.VisibleCount);
                ScrollBarChart.ViewportSize = _chart.VisibleCount;
                ScrollBarChart.SmallChange = 1;
                ScrollBarChart.LargeChange = Math.Max(1, _chart.VisibleCount * 0.8);
                ScrollBarChart.Value = _chart.StartIndex;
                TextBlockChartRange.Text = _chart.RangeText;
            }
            catch (Exception error) { ShowError(error); }
            finally { _updatingChartRange = false; }
        }

        private void ScrollBarChart_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                if (_updatingChartRange == false && _chart != null && _chart.StartIndex != (int)e.NewValue)
                {
                    _chart.ScrollTo((int)e.NewValue);
                }
            }
            catch (Exception error) { ShowError(error); }
        }

        private void ButtonChartNavigation_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_chart == null) { return; }
                if (sender == ButtonChartFirst) { _chart.ScrollTo(0); }
                else if (sender == ButtonChartLast) { _chart.ScrollTo(_chart.TotalBars); }
                else if (sender == ButtonChartZoomIn) { _chart.Zoom(_chart.VisibleCount / 2); }
                else if (sender == ButtonChartZoomOut) { _chart.Zoom(_chart.VisibleCount * 2); }
                else if (sender == ButtonChartAll) { _chart.Zoom(_chart.TotalBars); }
                else
                {
                    OrderFlowCandidateView selected = DataGridCandidates.SelectedItem as OrderFlowCandidateView;
                    if (selected != null) { _chart.SelectCandidate(selected.CandidateId); }
                }
            }
            catch (Exception error) { ShowError(error); }
        }

        private void ButtonBrowseDeals_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog();
                dialog.Filter = "Deals QSH (*.Deals.qsh)|*.Deals.qsh|QSH files (*.qsh)|*.qsh|All files (*.*)|*.*";
                dialog.CheckFileExists = true;

                if (dialog.ShowDialog() == true)
                {
                    TextBoxDealsPath.Text = dialog.FileName;
                    TryFillPairedPath(dialog.FileName, ".Deals.qsh", ".Quotes.qsh", TextBoxQuotesPath);
                    SetDefaultOutputPath(dialog.FileName);
                }
            }
            catch (Exception error)
            {
                ShowError(error);
            }
        }

        private void ButtonBrowseQuotes_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog();
                dialog.Filter = "Quotes QSH (*.Quotes.qsh)|*.Quotes.qsh|QSH files (*.qsh)|*.qsh|All files (*.*)|*.*";
                dialog.CheckFileExists = true;

                if (dialog.ShowDialog() == true)
                {
                    TextBoxQuotesPath.Text = dialog.FileName;
                    TryFillPairedPath(dialog.FileName, ".Quotes.qsh", ".Deals.qsh", TextBoxDealsPath);
                    SetDefaultOutputPath(dialog.FileName);
                }
            }
            catch (Exception error)
            {
                ShowError(error);
            }
        }

        private void ButtonBrowseOutput_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using (System.Windows.Forms.FolderBrowserDialog dialog = new System.Windows.Forms.FolderBrowserDialog())
                {
                    dialog.SelectedPath = Directory.Exists(TextBoxOutputPath.Text)
                        ? TextBoxOutputPath.Text
                        : string.Empty;

                    if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        TextBoxOutputPath.Text = dialog.SelectedPath;
                    }
                }
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
                if (_analysisThread != null && _analysisThread.IsAlive)
                {
                    return;
                }

                _pendingRequest = BuildRequest();
                _completedResult = null;
                _workerError = null;
                _workerCancelled = false;
                _cancellation = new CancellationTokenSource();

                SetRunningState(true);
                TextBlockStatus.Text = OsLocalization.ConvertToLocString(
                    "Eng:Reading and replaying the QSH pair_Ru:Чтение и воспроизведение пары QSH_");

                _analysisThread = new Thread(ResearchThreadArea);
                _analysisThread.IsBackground = true;
                _analysisThread.Name = "OrderFlowResearch";
                _analysisThread.Start();
            }
            catch (Exception error)
            {
                SetRunningState(false);
                ShowError(error);
            }
        }

        private void ResearchThreadArea()
        {
            try
            {
                OrderFlowResearchRunner runner = new OrderFlowResearchRunner();
                _completedResult = runner.RunAndExport(_pendingRequest, _cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                _workerCancelled = true;
            }
            catch (Exception error)
            {
                _workerError = error;
                ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error);
            }

            if (_isClosing == false && Dispatcher.HasShutdownStarted == false)
            {
                try
                {
                    Dispatcher.BeginInvoke(new Action(ResearchCompletedOnUiThread));
                }
                catch (InvalidOperationException error)
                {
                    ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error);
                    DisposeCancellationAfterClose();
                }
            }
            else
            {
                DisposeCancellationAfterClose();
            }
        }

        private void DisposeCancellationAfterClose()
        {
            if (_cancellation != null)
            {
                _cancellation.Dispose();
                _cancellation = null;
            }

            _analysisThread = null;
        }

        private void ResearchCompletedOnUiThread()
        {
            if (_isClosing)
            {
                DisposeCancellationAfterClose();
                return;
            }

            try
            {
                SetRunningState(false);

                if (_workerCancelled)
                {
                    TextBlockStatus.Text = OsLocalization.ConvertToLocString(
                        "Eng:Research cancelled_Ru:Исследование отменено_");
                    return;
                }

                if (_workerError != null)
                {
                    ShowError(_workerError);
                    return;
                }

                ApplyResult(_completedResult);
            }
            catch (Exception error)
            {
                ShowError(error);
            }
            finally
            {
                if (_cancellation != null)
                {
                    _cancellation.Dispose();
                    _cancellation = null;
                }

                _analysisThread = null;
            }
        }

        private void ApplyResult(OrderFlowResearchResult result)
        {
            if (result == null)
            {
                return;
            }

            TextBoxSummary.Text = BuildSummary(result, OsLocalization.CurLocalization == OsLocalization.OsLocalType.Ru);
            TextBoxSummary.ScrollToHome();
            List<OrderFlowCandidateView> candidateViews = OrderFlowCandidateView.Create(result);
            DataGridCandidates.ItemsSource = candidateViews;
            DataGridJournal.ItemsSource = result.Journal;
            _chart.SetResult(result);
            string horizon = result.Labels.Count == 0 ? "—" : result.Labels.Min(label => label.HorizonSeconds).ToString(CultureInfo.InvariantCulture);
            TextBlockChartLegend.Text = L(
                "▲ Long / ▼ Short. Marker color = future outcome at the shortest horizon ",
                "▲ Long / ▼ Short. Цвет метки = будущий исход на коротком горизонте ") + horizon + L(" s. ", " сек. ") + L(
                "Green: target first; red: adverse barrier first; yellow: same timestamp; blue: neither; gray: incomplete / no trades. Gold outline: selected. Markers can overlap within a bar. Red book shading: missing or stale book. Bars with no trades are omitted; time is from QSH without conversion.",
                "Зелёный: цель раньше; красный: против раньше; жёлтый: один timestamp; синий: ни одна граница; серый: неполный горизонт / нет сделок. Золотой контур: выбранный кандидат. Метки в одной свече могут перекрываться. Красный фон стакана: нет данных или стакан устарел. Свечи без сделок пропущены; время из QSH без пересчёта.");
            ButtonOpenArtifacts.IsEnabled = Directory.Exists(result.ArtifactDirectory);
            TabControlResults.SelectedItem = TabItemSummary;

            if (candidateViews.Count > 0)
            {
                DataGridCandidates.SelectedIndex = 0;
            }

            TextBlockStatus.Text = result.Quality.ResearchAccepted
                ? OsLocalization.ConvertToLocString("Eng:Research replay accepted_Ru:Исследовательский replay принят_")
                : OsLocalization.ConvertToLocString("Eng:Research replay rejected. Inspect quality reasons_Ru:Replay отклонен. Проверьте причины качества_");
        }

        private void ButtonCancel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_cancellation != null)
                {
                    _cancellation.Cancel();
                    TextBlockStatus.Text = OsLocalization.ConvertToLocString(
                        "Eng:Cancelling_Ru:Отмена выполняется_");
                }
            }
            catch (Exception error)
            {
                ShowError(error);
            }
        }

        private void ButtonOpenArtifacts_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_completedResult == null || Directory.Exists(_completedResult.ArtifactDirectory) == false)
                {
                    return;
                }

                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = _completedResult.ArtifactDirectory;
                startInfo.UseShellExecute = true;
                Process.Start(startInfo);
            }
            catch (Exception error)
            {
                ShowError(error);
            }
        }

        private void ComboBoxTimeFrame_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (_chart != null && ComboBoxTimeFrame.SelectedItem is OrderFlowDisplayTimeFrame)
                {
                    _chart.SetTimeFrame((OrderFlowDisplayTimeFrame)ComboBoxTimeFrame.SelectedItem);
                }
            }
            catch (Exception error)
            {
                ShowError(error);
            }
        }

        private void DataGridCandidates_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                OrderFlowCandidateView selected = DataGridCandidates.SelectedItem as OrderFlowCandidateView;
                if (selected == null)
                {
                    return;
                }

                _chart.SelectCandidate(selected.CandidateId);
                TextBlockCandidateDetails.Text = selected.Details;
            }
            catch (Exception error)
            {
                ShowError(error);
            }
        }

        private void DataGridCandidates_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            try
            {
                OrderFlowCandidateView selected = DataGridCandidates.SelectedItem as OrderFlowCandidateView;
                if (selected != null)
                {
                    _chart.SelectCandidate(selected.CandidateId);
                    TabControlResults.SelectedItem = TabItemChart;
                }
            }
            catch (Exception error)
            {
                ShowError(error);
            }
        }

        private OrderFlowResearchRequest BuildRequest()
        {
            OrderFlowResearchRequest request = new OrderFlowResearchRequest();
            request.DealsFilePath = TextBoxDealsPath.Text.Trim();
            request.QuotesFilePath = TextBoxQuotesPath.Text.Trim();
            request.OutputRootPath = TextBoxOutputPath.Text.Trim();
            request.FeatureWindowSeconds = ParseInt(TextBoxWindowSeconds.Text, "Feature window");
            request.MinimumAbsoluteDelta = TextBoxMinimumDelta.Text.ToDecimal();
            request.MinimumPriceChangeTicks = ParseNonNegativeInt(TextBoxMinimumPriceTicks.Text, "Price ticks");
            request.TopBookLevels = ParseInt(TextBoxTopLevels.Text, "Book levels");
            request.MaximumBookAgeMilliseconds = ParseInt(TextBoxBookAge.Text, "Book age");
            request.CandidateCooldownMilliseconds = ParseNonNegativeInt(TextBoxCooldown.Text, "Cooldown");
            request.BackgroundSampleSeconds = ParseInt(TextBoxBackground.Text, "Background interval");
            request.LabelHorizonsSeconds = ParseHorizons(TextBoxHorizons.Text);
            request.TargetTicks = ParseInt(TextBoxTargetTicks.Text, "Target ticks");
            request.InvalidationTicks = ParseInt(TextBoxInvalidationTicks.Text, "Invalidation ticks");
            request.PriceStepOverride = string.IsNullOrWhiteSpace(TextBoxPriceStep.Text)
                ? 0
                : TextBoxPriceStep.Text.ToDecimal();
            request.VolumeStepOverride = string.IsNullOrWhiteSpace(TextBoxVolumeStep.Text)
                ? 0
                : TextBoxVolumeStep.Text.ToDecimal();
            request.Validate();
            return request;
        }

        private static int ParseInt(string value, string fieldName)
        {
            int parsed;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) == false || parsed <= 0)
            {
                throw new ArgumentException(fieldName + " must be a positive integer.");
            }

            return parsed;
        }

        private static int ParseNonNegativeInt(string value, string fieldName)
        {
            int parsed;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) == false || parsed < 0)
            {
                throw new ArgumentException(fieldName + " must be a non-negative integer.");
            }

            return parsed;
        }

        private static List<int> ParseHorizons(string value)
        {
            string[] parts = value.Replace(',', ';').Split(';', StringSplitOptions.RemoveEmptyEntries);
            List<int> horizons = new List<int>();

            for (int i = 0; i < parts.Length; i++)
            {
                horizons.Add(ParseInt(parts[i].Trim(), "Label horizon"));
            }

            return horizons;
        }

        private static void TryFillPairedPath(string selectedPath, string selectedSuffix,
            string pairedSuffix, TextBox target)
        {
            if (selectedPath.EndsWith(selectedSuffix, StringComparison.OrdinalIgnoreCase) == false)
            {
                return;
            }

            string pairedPath = selectedPath.Substring(0, selectedPath.Length - selectedSuffix.Length) + pairedSuffix;
            if (File.Exists(pairedPath))
            {
                target.Text = pairedPath;
            }
        }

        private void SetDefaultOutputPath(string selectedPath)
        {
            if (string.IsNullOrWhiteSpace(TextBoxOutputPath.Text))
            {
                string directory = Path.GetDirectoryName(selectedPath);
                if (string.IsNullOrWhiteSpace(directory) == false)
                {
                    TextBoxOutputPath.Text = Path.Combine(directory, "OrderFlowResearchResults");
                }
            }
        }

        internal static string BuildSummary(OrderFlowResearchResult result, bool russian)
        {
            Func<string, string, string> text = (english, translated) => russian ? translated : english;
            StringBuilder builder = new StringBuilder();
            builder.AppendLine(result.Quality.ResearchAccepted ? text("RESEARCH ACCEPTED", "ИССЛЕДОВАНИЕ ПРИНЯТО") : text("RESEARCH REJECTED", "ИССЛЕДОВАНИЕ ОТКЛОНЕНО"));
            builder.AppendLine(text("This result contains no order, fill, execution PnL or profitability claim.", "Оценка исторических данных. Заявки и торговая прибыль не рассчитываются."));
            builder.AppendLine();
            builder.AppendLine(text("Deals: ", "Сделки QSH: ") + result.Quality.DealCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine(text("Quotes: ", "Снимки стакана QSH: ") + result.Quality.QuoteCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine(text("Valid books: ", "Валидные стаканы: ") + result.Quality.ValidBookCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine(text("Deals range: ", "Период сделок: ") + FormatRange(result.Quality.FirstDealTime, result.Quality.LastDealTime));
            builder.AppendLine(text("Quotes range: ", "Период стакана: ") + FormatRange(result.Quality.FirstQuoteTime, result.Quality.LastQuoteTime));
            builder.AppendLine(text("Closed buckets: ", "Обработанные группы событий с одинаковым временем: ") + result.Quality.BucketCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine(text("Observations: ", "Наблюдения (кандидаты + фон): ") + result.Observations.Count.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine(text("Broad candidates: ", "Кандидаты Long/Short: ") + result.Candidates.Count.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine(text("Market path labels: ", "Оценки будущего движения (все горизонты): ") + result.Labels.Count.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine(text("Invalid deals: ", "Некорректные сделки: ") + result.Quality.InvalidDealCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine(text("Invalid books: ", "Некорректные стаканы: ") + result.Quality.InvalidBookCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine(text("Missing book features: ", "Расчёты признаков без доступного стакана: ") + result.Quality.MissingBookFeatureCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine(text("Stale book features: ", "Расчёты признаков с устаревшим стаканом: ") + result.Quality.StaleBookFeatureCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine();
            builder.AppendLine(text("Input hash: ", "Хеш входных данных: ") + result.InputHash);
            builder.AppendLine(text("ResearchSpec hash: ", "Хеш параметров: ") + result.ResearchSpecHash);
            builder.AppendLine(text("Normalized event hash: ", "Хеш событий: ") + result.NormalizedEventHash);
            builder.AppendLine(text("Feature hash: ", "Хеш признаков: ") + result.FeatureHash);
            builder.AppendLine(text("Candidate hash: ", "Хеш кандидатов: ") + result.CandidateHash);
            builder.AppendLine(text("Artifacts: ", "Папка файлов результата: ") + result.ArtifactDirectory);
            builder.AppendLine();
            builder.AppendLine(text("Quality reasons", "Причины качества данных (коды и исходные сообщения)"));

            for (int i = 0; i < result.Quality.Issues.Count; i++)
            {
                OrderFlowQualityIssue issue = result.Quality.Issues[i];
                builder.AppendLine((issue.IsRejection ? text("REJECT", "ОТКАЗ") : text("WARN", "ПРЕДУПРЕЖДЕНИЕ")) + " · " +
                    issue.ReasonCode + " · " + issue.Message);
            }

            return builder.ToString();
        }

        private static string FormatRange(DateTime? first, DateTime? last)
        {
            if (first.HasValue == false || last.HasValue == false)
            {
                return "n/a";
            }

            return first.Value.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) +
                " .. " + last.Value.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        }

        private void SetRunningState(bool isRunning)
        {
            ButtonRun.IsEnabled = isRunning == false;
            ButtonCancel.IsEnabled = isRunning;
            ButtonBrowseDeals.IsEnabled = isRunning == false;
            ButtonBrowseQuotes.IsEnabled = isRunning == false;
            ButtonBrowseOutput.IsEnabled = isRunning == false;
            TextBoxDealsPath.IsEnabled = isRunning == false;
            TextBoxQuotesPath.IsEnabled = isRunning == false;
            TextBoxOutputPath.IsEnabled = isRunning == false;
            TextBoxWindowSeconds.IsEnabled = isRunning == false;
            TextBoxMinimumDelta.IsEnabled = isRunning == false;
            TextBoxMinimumPriceTicks.IsEnabled = isRunning == false;
            TextBoxTopLevels.IsEnabled = isRunning == false;
            TextBoxBookAge.IsEnabled = isRunning == false;
            TextBoxCooldown.IsEnabled = isRunning == false;
            TextBoxBackground.IsEnabled = isRunning == false;
            TextBoxHorizons.IsEnabled = isRunning == false;
            TextBoxTargetTicks.IsEnabled = isRunning == false;
            TextBoxInvalidationTicks.IsEnabled = isRunning == false;
            TextBoxPriceStep.IsEnabled = isRunning == false;
            TextBoxVolumeStep.IsEnabled = isRunning == false;
        }

        private void ShowError(Exception error)
        {
            ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error);
            TextBlockStatus.Text = error.Message;
            CustomMessageBoxUi message = new CustomMessageBoxUi(OsLocalization.ConvertToLocString(
                "Eng:Order Flow research failed. See the log and status line._Ru:Ошибка исследования Order Flow. Проверьте лог и строку состояния._"));
            message.ShowDialog();
        }

        private void OrderFlowResearchUi_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                _isClosing = true;

                if (_cancellation != null)
                {
                    _cancellation.Cancel();
                }

                ButtonBrowseDeals.Click -= ButtonBrowseDeals_Click;
                ButtonBrowseQuotes.Click -= ButtonBrowseQuotes_Click;
                ButtonBrowseOutput.Click -= ButtonBrowseOutput_Click;
                ButtonRun.Click -= ButtonRun_Click;
                ButtonCancel.Click -= ButtonCancel_Click;
                ButtonOpenArtifacts.Click -= ButtonOpenArtifacts_Click;
                ComboBoxTimeFrame.SelectionChanged -= ComboBoxTimeFrame_SelectionChanged;
                DataGridCandidates.SelectionChanged -= DataGridCandidates_SelectionChanged;
                DataGridCandidates.MouseDoubleClick -= DataGridCandidates_MouseDoubleClick;
                Closing -= OrderFlowResearchUi_Closing;

                _chart.ViewChanged -= Chart_ViewChanged;
                ScrollBarChart.ValueChanged -= ScrollBarChart_ValueChanged;
                ButtonChartFirst.Click -= ButtonChartNavigation_Click;
                ButtonChartLast.Click -= ButtonChartNavigation_Click;
                ButtonChartSelected.Click -= ButtonChartNavigation_Click;
                ButtonChartZoomIn.Click -= ButtonChartNavigation_Click;
                ButtonChartZoomOut.Click -= ButtonChartNavigation_Click;
                ButtonChartAll.Click -= ButtonChartNavigation_Click;
                ContentControlChart.Content = null;
                _chart = null;
            }
            catch (Exception error)
            {
                ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error);
            }
        }
    }
}
