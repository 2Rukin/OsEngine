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
    /// Offline tick-text workbench for inspecting causal Order Flow research
    /// features, candidates and separately computed future market-path labels.
    /// </summary>
    public partial class OrderFlowResearchUi
    {
        #region Setup and chart interaction

        private OrderFlowResearchChart _chart;
        private Thread _analysisThread;
        private CancellationTokenSource _cancellation;
        private OrderFlowResearchRequest _pendingRequest;
        private OrderFlowResearchResult _completedResult;
        private Exception _workerError;
        private bool _workerCancelled;
        private volatile bool _isClosing;
        private bool _updatingChartRange;
        private readonly OrderFlowDateInput _fromDateInput;
        private readonly OrderFlowDateInput _toDateInput;

        /// <summary>
        /// Creates the research-only workbench. The window does not start a
        /// replay, network access or file download.
        /// </summary>
        public OrderFlowResearchUi()
        {
            InitializeComponent();
            _fromDateInput = new OrderFlowDateInput(DateFrom);
            _toDateInput = new OrderFlowDateInput(DateTo);
            Layout.StickyBorders.Listen(this);
            Layout.StartupLocation.Start_FitHeightToWorkArea(this);

            _chart = new OrderFlowResearchChart();
            ContentControlChart.Content = _chart;
            ComboBoxTimeFrame.ItemsSource = OrderFlowChartTimeFrames.GetMenuValues()
                .Select(frame => new KeyValuePair<OrderFlowDisplayTimeFrame, string>(frame,
                    OrderFlowChartTimeFrames.GetDisplayName(frame, OsLocalization.CurLocalization == OsLocalization.OsLocalType.Ru)))
                .ToList();
            ComboBoxTimeFrame.SelectedValue = OrderFlowDisplayTimeFrame.Min1;


            ButtonBrowseTicks.Click += ButtonBrowseTicks_Click;
            ButtonAllDates.Click += ButtonAllDates_Click;
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
            CheckBoxCalculateDelta.Click += CalculationMode_Click;
            CheckBoxCalculateCloud.Click += CalculationMode_Click;
            CheckBoxShowDelta.Click += ChartLayers_Click;
            CheckBoxShowCloud.Click += ChartLayers_Click;
            SliderCloudScale.ValueChanged += SliderCloudScale_ValueChanged;
            DataGridClouds.MouseDoubleClick += DataGridClouds_MouseDoubleClick;
            InitializeCloud2Controls();
            InitializeCloudImbalanceControls();
            UpdateCalculationControls();
            ApplyLocalization();
            InitializeChartTools();
            InitializeReplay();
            InitializeCloudNavigation();
            InitializeStatistics();
            InitializeFieldHelp();
            Chart_ViewChanged(this, EventArgs.Empty);
        }

        private void ApplyLocalization()
        {
            Title = OsLocalization.ConvertToLocString("Eng:Order Flow Research_Ru:Исследование Order Flow_");
            TabItemDeltaSettings.Header = L("Delta", "Дельта");
            TabItemChartSettings.Header = L("Chart", "График");
            CheckBoxCalculateDelta.Content = L("Calculate delta", "Рассчитать дельту");
            CheckBoxCalculateCloud.Content = L("Calculate Cloud 1", "Рассчитать Cloud 1");
            CheckBoxShowDelta.Content = L("Show delta", "Показать дельту");
            CheckBoxShowCloud.Content = L("Show Cloud 1", "Показать Cloud 1");
            LabelCloudMinTick.Content = L("Min tick volume", "Мин. объём тика");
            LabelCloudSum.Content = L("Min sum", "Мин. сумма");
            LabelCloudGap.Content = L("Gap ms", "Пауза, мс");
            LabelCloudRange.Content = L("Range ticks", "Диапазон, тики");
            TextBoxCloudGap.ToolTip = L("Maximum time between eligible ticks, inclusive. 0 means equal timestamps.", "Максимальная пауза между отобранными тиками, включительно. 0 — одинаковый timestamp.");
            TextBoxCloudRange.ToolTip = L("Maximum high-low of the entire chain in manual price steps. Zero requires one price.", "Максимум цены − минимум цены всей цепочки в шагах цены. Ноль — одна цена.");
            TextBlockCloudHelp.Text = L("Historical tick chains. Double-click a Clouds table row to locate it. Spread modes and Smart are unavailable.",
                "Исторические цепочки тиков. Двойной клик строки Clouds — переход на график. Режимы спреда и Smart недоступны.");
            LabelPeriod.Content = L("Period", "Период");
            ButtonAllDates.Content = L("Full file", "Весь файл");
            DateFrom.ToolTip = L("Start date, inclusive", "Начальная дата, включительно");
            DateTo.ToolTip = L("End date, inclusive", "Конечная дата, включительно");
            TextBlockPeriodHelp.Text = L("Inclusive dates; leave both empty for the full file.", "Даты включительно; оба поля пустые — весь файл.");
            LabelTicks.Content = OsLocalization.ConvertToLocString("Eng:Tick file_Ru:Файл тиков_");
            LabelOutput.Content = OsLocalization.ConvertToLocString("Eng:Output folder_Ru:Папка результатов_");
            ButtonBrowseTicks.Content = OsLocalization.ConvertToLocString("Eng:Browse_Ru:Выбрать_");
            ButtonBrowseOutput.Content = OsLocalization.ConvertToLocString("Eng:Browse_Ru:Выбрать_");
            LabelWindow.Content = OsLocalization.ConvertToLocString("Eng:Window sec_Ru:Окно сек_");
            LabelDelta.Content = OsLocalization.ConvertToLocString("Eng:Min delta_Ru:Мин дельта_");
            LabelPriceTicks.Content = OsLocalization.ConvertToLocString("Eng:Price ticks_Ru:Тики цены_");
            LabelCooldown.Content = OsLocalization.ConvertToLocString("Eng:Cooldown ms_Ru:Пауза мс_");
            LabelBackground.Content = OsLocalization.ConvertToLocString("Eng:Background sec_Ru:Фон сек_");
            LabelHorizons.Content = OsLocalization.ConvertToLocString("Eng:Horizons sec_Ru:Горизонты сек_");
            LabelTarget.Content = OsLocalization.ConvertToLocString("Eng:Target ticks_Ru:Цель тики_");
            LabelInvalidation.Content = OsLocalization.ConvertToLocString("Eng:Adverse ticks_Ru:Против, тики_");
            LabelPriceStep.Content = OsLocalization.ConvertToLocString("Eng:Price step_Ru:Шаг цены_");
            ButtonRun.Content = OsLocalization.ConvertToLocString("Eng:Run research_Ru:Запустить_");
            ButtonCancel.Content = OsLocalization.ConvertToLocString("Eng:Cancel_Ru:Отмена_");
            ButtonOpenArtifacts.Content = OsLocalization.ConvertToLocString("Eng:Open artifacts_Ru:Открыть файлы_");
            TabItemSummary.Header = OsLocalization.ConvertToLocString("Eng:Summary_Ru:Сводка_");
            TabItemCandidates.Header = OsLocalization.ConvertToLocString("Eng:Candidates_Ru:Кандидаты_");
            TabItemChart.Header = OsLocalization.ConvertToLocString("Eng:Chart_Ru:График_");
            TabItemJournal.Header = OsLocalization.ConvertToLocString("Eng:Event journal_Ru:Журнал событий_");
            LabelChartTimeFrame.Content = L("Timeframe", "Таймфрейм");
            LabelCloudScale.Content = LabelChartCloudScale.Content = L("Cloud size", "Размер Cloud");
            SliderCloudScale.ToolTip = SliderChartCloudScale.ToolTip = L(
                "Radius coefficient 0.1–3. Changes only the circles, without recalculation.",
                "Коэффициент радиуса 0,1–3. Меняет только кружки, без пересчёта.");
            LabelTimeFrame.Content = OsLocalization.ConvertToLocString("Eng:Display timeframe_Ru:Таймфрейм отображения_");
            TextBlockChartBoundary.Text = OsLocalization.ConvertToLocString(
                "Eng:Visualization only. Signals are not recalculated._Ru:Только визуализация. Сигналы не пересчитываются._");
            ButtonChartFirst.Content = L("Start", "Начало");
            ButtonChartLast.Content = L("End", "Конец");
            ButtonChartSelected.Content = L("To candidate", "К кандидату");
            ButtonChartAll.Content = L("All history", "Весь период");
            ButtonChartZoomIn.ToolTip = L("Zoom in: fewer bars", "Приблизить: меньше свечей");
            ButtonChartZoomOut.ToolTip = L("Zoom out: more bars", "Отдалить: больше свечей");
            TextBlockChartBoundary.Text = L("Wheel: zoom at pointer. Shift + wheel / drag plot / scrollbar: scroll. Drag the time axis: horizontal scale. Hover for values.",
                "Колесо: масштаб у курсора. Shift + колесо / перетаскивание графика / полоса: прокрутка. Потяните шкалу времени для масштаба. Наведение: значения.");
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
            TextBoxPriceStep.ToolTip = LabelPriceStep.ToolTip = L("Required manual price distance per tick, e.g. 5 or 0.00001. Volume is read as stored.",
                "Обязательный ручной шаг цены, например 5 или 0,00001. Объём читается как записан в файле.");
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
                SliderCloudContrast.ToolTip = SliderChartCloudContrast.ToolTip = (_chart.IsReplaying
                    ? L("Replay reference = minimum Cloud sum: ", "Опорный объём реплея = минимальная сумма Cloud: ") : L(
                    "Emphasizes differences in volume, independently of overall size. Full-result median volume = ",
                    "Усиливает разницу объёмов независимо от общего размера. Медианный объём всего результата = "))
                    + _chart.CloudReferenceVolume.ToString("0.############################", CultureInfo.InvariantCulture);
                SliderCloud2Contrast.ToolTip = SliderChartCloud2Contrast.ToolTip = (_chart.IsReplaying
                    ? L("Cloud 2 replay reference = effective volume threshold: ", "Опорный объём реплея Cloud 2 = действующий порог объёма: ")
                    : L("Cloud 2 full-result median volume: ", "Медианный объём всего результата Cloud 2: "))
                    + _chart.Cloud2ReferenceVolume.ToString("0.############################", CultureInfo.InvariantCulture);
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

        #endregion

        #region Replay and input

        private void ButtonBrowseTicks_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog();
                dialog.Filter = "Tick text (*.txt;*.csv)|*.txt;*.csv|All files (*.*)|*.*";
                dialog.CheckFileExists = true;

                if (dialog.ShowDialog() == true)
                {
                    TextBoxTicksPath.Text = dialog.FileName;
                    TextBoxPriceStep.Clear();
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
                if (_replay != null || (_analysisThread != null && _analysisThread.IsAlive))
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
                    "Eng:Validating tick file and calculating the selected period_Ru:Проверка файла тиков и расчёт выбранного периода_");

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

            ClearStatistics();
            _displayedResult = result;
            _displayedRequest = _pendingRequest;
            ButtonReplayPlay.IsEnabled = result.Quality.ResearchAccepted;
            UpdateStatisticsControls(false);
            TextBoxSummary.Text = BuildSummary(result, OsLocalization.CurLocalization == OsLocalization.OsLocalType.Ru);
            TextBoxSummary.ScrollToHome();
            List<OrderFlowCandidateView> candidateViews = OrderFlowCandidateView.Create(result);
            DataGridCandidates.ItemsSource = candidateViews;
            DataGridJournal.ItemsSource = result.Journal;
            DataGridClouds.ItemsSource = result.Clouds;
            DataGridClouds2.ItemsSource = result.Clouds2;
            TextBlockCandidateDetails.Text = string.Empty;
            _chart.SetResult(result);
            _chart.SetCloudFilters(null, null);
            try { ApplyCloudViewFilters(); }
            catch (Exception error) { RefreshCloudViewRows(); ShowError(error); }
            _chart.SetLayers(CheckBoxShowDelta.IsChecked == true, CheckBoxShowCloud.IsChecked == true, CheckBoxShowCloud2.IsChecked == true);
            string horizon = result.Labels.Count == 0 ? "—" : result.Labels.Min(label => label.HorizonSeconds).ToString(CultureInfo.InvariantCulture);
            TextBlockChartLegend.Text = L(
                "▲ Long / ▼ Short. Marker color = future outcome at the shortest horizon ",
                "▲ Long / ▼ Short. Цвет метки = будущий исход на коротком горизонте ") + horizon + L(" s. ", " сек. ") + L(
                "Green: target first; red: adverse barrier first; yellow: same timestamp; blue: neither; gray: incomplete / no trades. Gold outline: selected. Markers can overlap within a bar. Bars with no trades are omitted; time is from the tick file without conversion.",
                "Зелёный: цель раньше; красный: против раньше; жёлтый: один timestamp; синий: ни одна граница; серый: неполный горизонт / нет сделок. Золотой контур: выбранный кандидат. Метки в одной свече могут перекрываться. Свечи без сделок пропущены; время из файла тиков без пересчёта.");
            TextBlockChartLegend.Text = (result.DeltaCalculated ? TextBlockChartLegend.Text : string.Empty) + L(
                " Clouds: squares for single trades, circles for chains. Green = more Buy ticks, red = more Sell ticks, blue = equal counts. Size reflects volume. Anchor is the last tick; completion may be later. OpenAtEnd is unfinished.",
                " Cloud: квадраты — одиночные сделки, круги — цепочки. Зелёный — больше тиков Buy, красный — Sell, синий — поровну. Размер отражает объём. Метка стоит на последнем тике; завершение может быть позже. OpenAtEnd — незавершённая цепочка.");
            TextBlockChartLegend.Text += L(" Cloud 2: thick outlined markers; its Cloud path is purple. Layers have independent size/contrast and visibility.",
                " Cloud 2: метки с толстым контуром; линия по Cloud — фиолетовая. Размер, контраст и видимость слоёв независимы.");
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
                if (_statisticsJob != null) { _statisticsJob.Cancel(); return; }
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
                if (_chart != null && ComboBoxTimeFrame.SelectedValue is OrderFlowDisplayTimeFrame)
                {
                    _chart.SetTimeFrame((OrderFlowDisplayTimeFrame)ComboBoxTimeFrame.SelectedValue);
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
                    ShowChartView();
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
            request.TicksFilePath = TextBoxTicksPath.Text.Trim();
            request.OutputRootPath = TextBoxOutputPath.Text.Trim();
            request.CalculateDelta = CheckBoxCalculateDelta.IsChecked == true;
            request.CalculateCloud = CheckBoxCalculateCloud.IsChecked == true;
            if (request.CalculateCloud)
            {
                request.Cloud = new OrderFlowCloudSettings
                {
                    MinimumTickVolume = TextBoxCloudMinTick.Text.ToDecimal(),
                    MinimumSumVolume = TextBoxCloudSum.Text.ToDecimal(),
                    MaximumGapMilliseconds = ParseNonNegativeInt(TextBoxCloudGap.Text, "Cloud gap"),
                    MaximumRangeTicks = ParseNonNegativeInt(TextBoxCloudRange.Text, "Cloud range")
                };
            }
            if (request.CalculateDelta)
            {
                request.FeatureWindowSeconds = ParseInt(TextBoxWindowSeconds.Text, "Feature window");
                request.MinimumAbsoluteDelta = TextBoxMinimumDelta.Text.ToDecimal();
                request.MinimumPriceChangeTicks = ParseNonNegativeInt(TextBoxMinimumPriceTicks.Text, "Price ticks");
                request.CandidateCooldownMilliseconds = ParseNonNegativeInt(TextBoxCooldown.Text, "Cooldown");
                request.BackgroundSampleSeconds = ParseInt(TextBoxBackground.Text, "Background interval");
                request.LabelHorizonsSeconds = ParseHorizons(TextBoxHorizons.Text);
                request.TargetTicks = ParseInt(TextBoxTargetTicks.Text, "Target ticks");
                request.InvalidationTicks = ParseInt(TextBoxInvalidationTicks.Text, "Invalidation ticks");
            }
            BuildCloud2Request(request);
            if (request.CalculateCloud) { request.Cloud.Imbalance = ReadImbalanceSettings("Cloud"); }
            if (request.CalculateCloud2) { request.Cloud2.Imbalance = ReadImbalanceSettings("Cloud2"); }
            request.PriceStep = ParsePriceStep(TextBoxPriceStep.Text);
            request.FromDate = _fromDateInput.ReadDate();
            request.ToDate = _toDateInput.ReadDate();
            request.Validate();
            return request;
        }

        private void ButtonAllDates_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _fromDateInput.Clear();
                _toDateInput.Clear();
            }
            catch (Exception error) { ShowError(error); }
        }

        /// <summary>Parses a required positive decimal tick size, accepting either comma or dot without grouping.</summary>
        internal static decimal ParsePriceStep(string text)
        {
            if (!decimal.TryParse((text ?? string.Empty).Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out decimal step) || step <= 0)
            {
                throw new ArgumentException(L("Enter a positive price step, for example 5 or 0.00001.",
                    "Укажите положительный шаг цены, например 5 или 0,00001."));
            }
            return step;
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

        #endregion

        #region Presentation and lifecycle

        internal static string BuildSummary(OrderFlowResearchResult result, bool russian)
        {
            Func<string, string, string> text = (english, translated) => russian ? translated : english;
            StringBuilder builder = new StringBuilder();
            builder.AppendLine(result.Quality.ResearchAccepted ? text("RESEARCH ACCEPTED", "ИССЛЕДОВАНИЕ ПРИНЯТО") : text("RESEARCH REJECTED", "ИССЛЕДОВАНИЕ ОТКЛОНЕНО"));
            builder.AppendLine(text("This result contains no order, fill, execution PnL or profitability claim.", "Оценка исторических данных. Заявки и торговая прибыль не рассчитываются."));
            builder.AppendLine();
            builder.AppendLine(text("Calculated: ", "Рассчитано: ") + (result.DeltaCalculated ? "Delta " : "") + (result.CloudCalculated ? "Cloud 1 " : "") + (result.Cloud2Calculated ? "Cloud 2" : ""));
            builder.AppendLine("Clouds: " + result.Clouds.Count + text("; open at end: ", "; незавершённых: ") + result.Clouds.Count(cloud => cloud.CompletedAt == null));
            builder.AppendLine("Cloud hash: " + result.CloudHash);
            builder.AppendLine("Cloud 2: " + result.Clouds2.Count + text("; single ticks: ", "; одиночных тиков: ") + result.Clouds2.Count(cloud => cloud.CompletionReason == "SingleTick"));
            builder.AppendLine("Cloud 2 hash: " + result.Cloud2Hash);
            builder.AppendLine(text("Cloud filter PASS / total: ", "Cloud прошли фильтр / всего: ")
                + result.Clouds.Count(cloud => cloud.ImbalancePassed) + " / " + result.Clouds.Count);
            builder.AppendLine(text("Cloud 2 filter PASS / total: ", "Cloud 2 прошли фильтр / всего: ")
                + result.Clouds2.Count(cloud => cloud.ImbalancePassed) + " / " + result.Clouds2.Count);
            builder.AppendLine(text("Selected ticks: ", "Тики выбранного периода: ") + result.Quality.DealCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine(text("Deals range: ", "Период сделок: ") + FormatRange(result.Quality.FirstDealTime, result.Quality.LastDealTime));
            if (result.Input != null)
            {
                builder.AppendLine(text("Instrument (filename): ", "Инструмент (имя файла): ") + result.Input.Instrument);
                builder.AppendLine(text("Validated source ticks: ", "Проверено тиков во всём файле: ") + result.Input.RecordCount);
                builder.AppendLine(text("Full source range: ", "Весь файл: ") + FormatRange(result.Input.FirstTime, result.Input.LastTime));
            }
            builder.AppendLine(text("Closed buckets: ", "Обработанные группы событий с одинаковым временем: ") + result.Quality.BucketCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine(text("Observations: ", "Наблюдения (кандидаты + фон): ") + result.Observations.Count.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine(text("Broad candidates: ", "Кандидаты Long/Short: ") + result.Candidates.Count.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine(text("Market path labels: ", "Оценки будущего движения (все горизонты): ") + result.Labels.Count.ToString(CultureInfo.InvariantCulture));
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

            return first.Value.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture) +
                " .. " + last.Value.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture);
        }

        private void SetRunningState(bool isRunning)
        {
            UpdateStatisticsControls(isRunning);
            ButtonReplayPlay.IsEnabled = !isRunning && _displayedResult?.Quality.ResearchAccepted == true;
            CheckBoxCalculateDelta.IsEnabled = CheckBoxCalculateCloud.IsEnabled = CheckBoxCalculateCloud2.IsEnabled = !isRunning;
            UpdateCalculationControls();
            DateFrom.IsEnabled = DateTo.IsEnabled = isRunning == false;
            ButtonAllDates.IsEnabled = isRunning == false;
            ButtonRun.IsEnabled = isRunning == false;
            ButtonCancel.IsEnabled = isRunning;
            ButtonBrowseTicks.IsEnabled = isRunning == false;
            ButtonBrowseOutput.IsEnabled = isRunning == false;
            TextBoxTicksPath.IsEnabled = isRunning == false;
            TextBoxOutputPath.IsEnabled = isRunning == false;
            TextBoxWindowSeconds.IsEnabled = isRunning == false;
            TextBoxMinimumDelta.IsEnabled = isRunning == false;
            TextBoxMinimumPriceTicks.IsEnabled = isRunning == false;
            TextBoxCooldown.IsEnabled = isRunning == false;
            TextBoxBackground.IsEnabled = isRunning == false;
            TextBoxHorizons.IsEnabled = isRunning == false;
            TextBoxTargetTicks.IsEnabled = isRunning == false;
            TextBoxInvalidationTicks.IsEnabled = isRunning == false;
            TextBoxPriceStep.IsEnabled = isRunning == false;
        }

        private void UpdateCalculationControls()
        {
            UpdateCloud2Controls();
            GridDeltaSettings.IsEnabled = CheckBoxCalculateDelta.IsEnabled && CheckBoxCalculateDelta.IsChecked == true;
            GridCloudSettings.IsEnabled = CheckBoxCalculateCloud.IsEnabled && CheckBoxCalculateCloud.IsChecked == true;
        }

        private void CalculationMode_Click(object sender, RoutedEventArgs e)
        {
            try { UpdateCalculationControls(); }
            catch (Exception error) { ShowError(error); }
        }

        private void ChartLayers_Click(object sender, RoutedEventArgs e)
        {
            try { _chart?.SetLayers(CheckBoxShowDelta.IsChecked == true, CheckBoxShowCloud.IsChecked == true, CheckBoxShowCloud2.IsChecked == true); }
            catch (Exception error) { ShowError(error); }
        }

        private void SliderCloudScale_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try { _chart?.SetCloudScale(e.NewValue); }
            catch (Exception error) { ShowError(error); }
        }

        private void DataGridClouds_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (sender is DataGrid grid && grid.SelectedItem is OrderFlowCloud cloud)
                {
                    ShowCloudOnChart(cloud);
                }
            }
            catch (Exception error) { ShowError(error); }
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
                DisposeCloudImbalanceControls();
                DisposeCloudNavigation();
                DisposeStatistics();
                DisposeCloud2Controls();
                DisposeReplay();
                DisposeChartTools();

                if (_cancellation != null)
                {
                    _cancellation.Cancel();
                }

                CheckBoxCalculateDelta.Click -= CalculationMode_Click;
                CheckBoxCalculateCloud.Click -= CalculationMode_Click;
                CheckBoxShowDelta.Click -= ChartLayers_Click;
                CheckBoxShowCloud.Click -= ChartLayers_Click;
                SliderCloudScale.ValueChanged -= SliderCloudScale_ValueChanged;
                DataGridClouds.MouseDoubleClick -= DataGridClouds_MouseDoubleClick;
                ButtonBrowseTicks.Click -= ButtonBrowseTicks_Click;
                ButtonAllDates.Click -= ButtonAllDates_Click;
                _fromDateInput.Dispose();
                _toDateInput.Dispose();
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
        #endregion
    }
}
