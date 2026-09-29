using Microsoft.Win32;
using OsEngine.Logging;
using OsEngine.Market;
using OsEngine.OsData.OrderFlow.Calibration;
using OsEngine.OsData.OrderFlow.Explorer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace OsEngine.OsData.OrderFlow.Context
{
    /// <summary>Independent offline workspace with one grey-price chart. Dispatcher owns views; cancellable workers own calculations.</summary>
    /// <remarks>Closing cancels jobs and replay without blocking the dispatcher. Result and profile changes are explicit; no Cloud workspace mutation.</remarks>
    public partial class FlowContextWindow : Window
    {
        private FlowContextSettings _settings = new FlowContextSettings();
        private readonly FlowContextView _view = new FlowContextView();
        private readonly OrderFlowResearchChart _chart = new OrderFlowResearchChart();
        private readonly CalibrationWindowSet _tables = new CalibrationWindowSet();
        private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        private readonly object _progressLock = new object();
        private string _progress;
        private ExplorerJob _job;
        private FlowContextRun _run;
        private OrderFlowReplaySession _replay;
        private bool _closed, _updating;
        private string _outputRoot;
        private Action<object> _completion;

        internal FlowContextWindow(ExplorerRunSpec input)
        {
            InitializeComponent(); TextBoxInput.Text = input.InputPath; DatePickerFrom.SelectedDate = input.FromDate; DatePickerTo.SelectedDate = input.ToDate;
            _settings.PriceStep = input.PriceStep > 0 ? input.PriceStep : 1; _settings.Instrument = Path.GetFileNameWithoutExtension(input.InputPath ?? "instrument");
            _outputRoot = input.OutputRootPath; ContentControlChart.Content = _chart; _chart.SetLayers(false, false, false); _chart.SetPriceDisplay(OrderFlowPriceDisplay.MutedHighLow);
            _updating = true; RefreshPeriods();
            ComboBoxPricePeriod.ItemsSource = Enum.GetValues<OrderFlowDisplayTimeFrame>().Select(frame => new KeyValuePair<OrderFlowDisplayTimeFrame, string>(frame, OrderFlowChartTimeFrames.GetDisplayName(frame, true))).ToList();
            ComboBoxPricePeriod.SelectedValue = OrderFlowDisplayTimeFrame.Min1;
            ComboBoxScale.ItemsSource = new[] { "Все масштабы", "Локальный", "Рабочий", "Старший" }; ComboBoxScale.SelectedIndex = 0;
            ComboBoxSigma.ItemsSource = new decimal[] { .5m, 1, 1.5m, 2, 3 }; ComboBoxSigma.SelectedItem = 1m;
            ComboBoxKind.ItemsSource = new[] { "Все" }; ComboBoxKind.SelectedIndex = 0;
            ComboBoxSpeed.ItemsSource = new double[] { 1, 10, 100, 1000 }; ComboBoxSpeed.SelectedItem = 100d;
            _updating = false; Wire(true); _timer.Start();
            TextBlockStatus.Text = "Выберите один день, проверьте шаг цены и параметры инструмента, затем нажмите «Рассчитать систему». Начальные пороги — пример, не торговая рекомендация.";
        }

        #region Lifecycle and jobs
        private void Wire(bool attach)
        {
            if (attach)
            {
                ButtonRun.Click += RunClick; ButtonCancel.Click += CancelClick; ButtonBrowse.Click += BrowseClick; ButtonSettings.Click += SettingsClick;
                ButtonSaveProfile.Click += SaveProfileClick; ButtonLoadProfile.Click += LoadProfileClick; ButtonSave.Click += SaveClick; ButtonOpen.Click += OpenClick; ButtonGuide.Click += GuideClick;
                ButtonRegions.Click += RegionsClick; ButtonEvents.Click += EventsClick; ButtonStatistics.Click += StatisticsClick;
                ButtonReplay.Click += ReplayClick; ButtonStep.Click += StepClick; ButtonHistory.Click += HistoryClick;
                ButtonClearSelection.Click += ClearSelectionClick; ButtonPrevious.Click += PreviousClick; ButtonNext.Click += NextClick; ButtonZoomIn.Click += ZoomInClick; ButtonZoomOut.Click += ZoomOutClick;
                ButtonDraw.Click += DrawClick; ButtonAnchor.Click += AnchorClick; ButtonClearAnchors.Click += ClearAnchorsClick;
                ComboBoxLocal.SelectionChanged += FormationChanged; ComboBoxWork.SelectionChanged += FormationChanged; ComboBoxSenior.SelectionChanged += FormationChanged; ComboBoxPricePeriod.SelectionChanged += PricePeriodChanged; ComboBoxSpeed.SelectionChanged += SpeedChanged;
                ComboBoxScale.SelectionChanged += ViewSelectionChanged; ComboBoxKind.SelectionChanged += ViewSelectionChanged; ComboBoxSigma.SelectionChanged += ViewSelectionChanged;
                foreach (CheckBox check in ViewChecks()) { check.Click += ViewClick; }
                SliderPosition.ValueChanged += PositionChanged; _chart.ViewChanged += ChartChanged; _chart.ContextSelected += ContextSelected; _timer.Tick += Poll; Closed += WindowClosed;
            }
            else
            {
                ButtonRun.Click -= RunClick; ButtonCancel.Click -= CancelClick; ButtonBrowse.Click -= BrowseClick; ButtonSettings.Click -= SettingsClick;
                ButtonSaveProfile.Click -= SaveProfileClick; ButtonLoadProfile.Click -= LoadProfileClick; ButtonSave.Click -= SaveClick; ButtonOpen.Click -= OpenClick; ButtonGuide.Click -= GuideClick;
                ButtonRegions.Click -= RegionsClick; ButtonEvents.Click -= EventsClick; ButtonStatistics.Click -= StatisticsClick;
                ButtonReplay.Click -= ReplayClick; ButtonStep.Click -= StepClick; ButtonHistory.Click -= HistoryClick;
                ButtonClearSelection.Click -= ClearSelectionClick; ButtonPrevious.Click -= PreviousClick; ButtonNext.Click -= NextClick; ButtonZoomIn.Click -= ZoomInClick; ButtonZoomOut.Click -= ZoomOutClick;
                ButtonDraw.Click -= DrawClick; ButtonAnchor.Click -= AnchorClick; ButtonClearAnchors.Click -= ClearAnchorsClick;
                ComboBoxLocal.SelectionChanged -= FormationChanged; ComboBoxWork.SelectionChanged -= FormationChanged; ComboBoxSenior.SelectionChanged -= FormationChanged; ComboBoxPricePeriod.SelectionChanged -= PricePeriodChanged; ComboBoxSpeed.SelectionChanged -= SpeedChanged;
                ComboBoxScale.SelectionChanged -= ViewSelectionChanged; ComboBoxKind.SelectionChanged -= ViewSelectionChanged; ComboBoxSigma.SelectionChanged -= ViewSelectionChanged;
                foreach (CheckBox check in ViewChecks()) { check.Click -= ViewClick; }
                SliderPosition.ValueChanged -= PositionChanged; _chart.ViewChanged -= ChartChanged; _chart.ContextSelected -= ContextSelected; _timer.Tick -= Poll; Closed -= WindowClosed;
            }
        }
        private IEnumerable<CheckBox> ViewChecks() => new[] { CheckBoxAllRegions, CheckBoxRegions, CheckBoxVwap, CheckBoxTwap, CheckBoxBands, CheckBoxEvents, CheckBoxSwings, CheckBoxWarmup };
        private void WindowClosed(object sender, EventArgs e)
        {
            try { _closed = true; Wire(false); _timer.Stop(); _job?.Dispose(); _job = null; _completion = null; _replay?.Dispose(); _replay = null; _tables.Dispose(); ContentControlChart.Content = null; _run = null; }
            catch (Exception error) { ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error); }
        }
        private void StartJob(Func<CancellationToken, object> action, Action<object> completion)
        {
            if (_job != null) { throw new InvalidOperationException("Дождитесь окончания текущего расчёта."); }
            ReturnHistory(); _completion = completion; _job = new ExplorerJob(action); SetBusy(true);
            TextBlockStatus.Text = "Выполняется расчёт. До его завершения график показывает предыдущее исследование.";
        }
        private void Progress(string message) { lock (_progressLock) { _progress = message; } }
        private void SetBusy(bool busy)
        {
            ButtonRun.IsEnabled = ButtonOpen.IsEnabled = ButtonSettings.IsEnabled = ButtonSave.IsEnabled = ButtonLoadProfile.IsEnabled = ButtonSaveProfile.IsEnabled = ButtonReplay.IsEnabled = !busy;
            ComboBoxLocal.IsEnabled = ComboBoxWork.IsEnabled = ComboBoxSenior.IsEnabled = !busy; ButtonCancel.IsEnabled = busy;
        }
        private void Poll(object sender, EventArgs e)
        {
            try
            {
                if (_closed) { return; }
                string progress; lock (_progressLock) { progress = _progress; _progress = null; }
                if (progress != null) { TextBlockStatus.Text = progress; }
                if (_job != null && _job.TryResult(out object value, out Exception error))
                {
                    _job.Dispose(); _job = null; SetBusy(false); Action<object> completion = _completion; _completion = null;
                    if (error is OperationCanceledException) { TextBlockStatus.Text = "Расчёт отменён. Предыдущее исследование сохранено."; }
                    else if (error != null) { throw error; }
                    else { completion?.Invoke(value); }
                }
                if (_replay != null)
                {
                    if (_replay.Error != null) { Exception failure = _replay.Error; ReturnHistory(); throw failure; }
                    OrderFlowReplayFrame frame = _replay.TakeFrame(out bool paused);
                    if (frame != null)
                    {
                        _chart.ApplyReplayFrame(frame.Result); _chart.SetContextReplay(frame.SourceSequence);
                        TextBlockStatus.Text = "Реплей " + frame.Time.ToString("dd.MM.yyyy HH:mm:ss.ffffff") + " · прочитано " + frame.TickCount.ToString("N0") + " тиков";
                        if (frame.Complete) { TextBlockStatus.Text += " · завершён; будущие исходы доступны после «Вся история»."; ButtonReplay.Content = "Реплей заново"; }
                    }
                    ButtonStep.IsEnabled = paused && !_replay.Finished;
                }
            }
            catch (Exception error) { Report(error); }
        }
        private void Report(Exception error) { TextBlockStatus.Text = "Ошибка: " + error.Message; ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error); }
        #endregion

        #region Calculate and profiles
        private void RunClick(object sender, RoutedEventArgs e)
        {
            try
            {
                ReadPeriods(); FlowContextSettings settings = _settings.Copy(); settings.Validate(); string path = TextBoxInput.Text.Trim(); DateTime? from = DatePickerFrom.SelectedDate; DateTime? to = DatePickerTo.SelectedDate;
                StartJob(token => FlowContextRunner.Run(path, from, to, settings, token, Progress), result => Install((FlowContextRun)result));
            }
            catch (Exception error) { Report(error); }
        }
        private void Install(FlowContextRun run)
        {
            _tables.CloseAll(); _run = run; _view.SelectedEventId = null; _view.SelectedRegionId = null;
            _chart.SetResult(run.Prices); _chart.SetLayers(false, false, false); _chart.SetPriceDisplay(OrderFlowPriceDisplay.MutedHighLow); _chart.SetContext(run.Data, _view);
            _updating = true; ComboBoxKind.ItemsSource = new[] { "Все" }.Concat(run.Data.Events.Select(item => item.Kind).Distinct().OrderBy(kind => kind)).ToArray(); ComboBoxKind.SelectedItem = "Все"; _updating = false;
            _view.EventKind = "Все"; ExpanderInput.IsExpanded = false; TextBoxDetails.Text = string.Empty;
            TextBlockStatus.Text = run.Data.Settings.Instrument + " · " + run.Data.TickCount.ToString("N0") + " тиков · " + run.Data.Regions.Count + " областей · " + run.Data.Events.Count + " событий · пауз данных " + run.Data.GapCount + " · профиль " + run.Data.SettingsHash.Substring(0, 12);
            _chart.ScrollTo(0);
        }
        private void CancelClick(object sender, RoutedEventArgs e) { try { _job?.Cancel(); } catch (Exception error) { Report(error); } }
        private void BrowseClick(object sender, RoutedEventArgs e) { try { OpenFileDialog dialog = new OpenFileDialog { Filter = "Тики TXT|*.txt|Все файлы|*.*" }; if (dialog.ShowDialog() == true) { TextBoxInput.Text = dialog.FileName; } } catch (Exception error) { Report(error); } }
        private void SettingsClick(object sender, RoutedEventArgs e)
        {
            try { ReadPeriods(); FlowContextSettingsWindow window = new FlowContextSettingsWindow(_settings) { Owner = this }; if (window.ShowDialog() == true) { _settings = window.Settings; RefreshPeriods(); TextBlockStatus.Text = "Новый профиль подготовлен. Нажмите «Рассчитать систему» для его применения."; } }
            catch (Exception error) { Report(error); }
        }
        private void RefreshPeriods()
        {
            ComboBox[] controls = { ComboBoxLocal, ComboBoxWork, ComboBoxSenior };
            for (int scale = 0; scale < 3; scale++)
            {
                int current = _settings.Scales[scale].FormationSeconds;
                controls[scale].ItemsSource = new[] { 60, 120, 300, 600, 900, 1800, 3600, 14400, 28800, 86400, current }.Distinct().OrderBy(value => value)
                    .Select(value => new KeyValuePair<int, string>(value, value < 3600 ? (value / 60m).ToString("0.#") + " мин" : (value / 3600m).ToString("0.#") + " ч")).ToList();
                controls[scale].SelectedValue = current;
            }
        }
        private void FormationChanged(object sender, SelectionChangedEventArgs e)
        { if (!_updating) { TextBlockStatus.Text = "Периоды изменены для следующего расчёта. Нажмите «Рассчитать систему»; на графике прежний результат."; } }
        private void ReadPeriods()
        { _settings.Scales[0].FormationSeconds = (int)ComboBoxLocal.SelectedValue; _settings.Scales[1].FormationSeconds = (int)ComboBoxWork.SelectedValue; _settings.Scales[2].FormationSeconds = (int)ComboBoxSenior.SelectedValue; }
        private void SaveProfileClick(object sender, RoutedEventArgs e)
        {
            try { ReadPeriods(); _settings.Validate(); SaveFileDialog dialog = new SaveFileDialog { Filter = "Профиль контекста|*.json", FileName = "flow-context-profile.json" }; if (dialog.ShowDialog() == true) { FlowContextRunner.Save(dialog.FileName, _settings); TextBlockStatus.Text = "Профиль сохранён: " + dialog.FileName; } }
            catch (Exception error) { Report(error); }
        }
        private void LoadProfileClick(object sender, RoutedEventArgs e)
        {
            try { OpenFileDialog dialog = new OpenFileDialog { Filter = "Профиль контекста|*.json" }; if (dialog.ShowDialog() == true) { _settings = FlowContextRunner.LoadSettings(dialog.FileName); RefreshPeriods(); TextBlockStatus.Text = "Профиль загружен для следующего расчёта. Текущее исследование не изменено."; } }
            catch (Exception error) { Report(error); }
        }
        private void SaveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                RequireResult(); if (_replay != null) { throw new InvalidOperationException("Вернитесь из реплея для сохранения полного исследования."); }
                SaveFileDialog dialog = new SaveFileDialog { Filter = "Исследование контекста|*.json", FileName = "flow-context-study.json" };
                if (dialog.ShowDialog() != true) { return; } FlowContextRun run = _run; string path = dialog.FileName;
                StartJob(token => { token.ThrowIfCancellationRequested(); FlowContextRunner.Save(path, run, token); return path; }, result => TextBlockStatus.Text = "Исследование сохранено: " + result);
            }
            catch (Exception error) { Report(error); }
        }
        private void OpenClick(object sender, RoutedEventArgs e)
        {
            try
            {
                OpenFileDialog dialog = new OpenFileDialog { Filter = "Исследование контекста|*.json" }; if (dialog.ShowDialog() != true) { return; } string path = dialog.FileName;
                StartJob(token =>
                {
                    token.ThrowIfCancellationRequested(); if (new FileInfo(path).Length > 512L * 1024 * 1024) { throw new InvalidDataException("Исследование превышает лимит 512 МБ."); }
                    using FileStream input = File.OpenRead(path); FlowContextRun run = JsonSerializer.Deserialize<FlowContextRun>(input) ?? throw new InvalidDataException("Пустое исследование.");
                    FlowContextRunner.ValidateSaved(run); token.ThrowIfCancellationRequested(); return run;
                }, result => { FlowContextRun run = (FlowContextRun)result; _settings = run.Data.Settings.Copy(); RefreshPeriods(); TextBoxInput.Text = run.Data.SourcePath; DatePickerFrom.SelectedDate = run.Data.FromDate; DatePickerTo.SelectedDate = run.Data.ToDate; Install(run); });
            }
            catch (Exception error) { Report(error); }
        }
        private void GuideClick(object sender, RoutedEventArgs e)
        {
            try
            {
                DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory); string path = null;
                for (int level = 0; directory != null && level < 6; level++, directory = directory.Parent)
                {
                    string candidate = Path.Combine(directory.FullName, "Documentation", "OrderFlow", "MULTISCALE_CONTEXT_RUNBOOK.md");
                    if (File.Exists(candidate)) { path = candidate; break; }
                }
                Process.Start(new ProcessStartInfo(path ?? "https://github.com/2Rukin/OsEngine/blob/feature/order-flow-multiscale-context/project/Documentation/OrderFlow/MULTISCALE_CONTEXT_RUNBOOK.md") { UseShellExecute = true });
            }
            catch (Exception error) { Report(error); }
        }
        #endregion

        #region Display, selection and tables
        private void RequireResult() { if (_run == null) { throw new InvalidOperationException("Сначала рассчитайте или откройте исследование."); } }
        private void ViewSelectionChanged(object sender, SelectionChangedEventArgs e) { ApplyView(); }
        private void ViewClick(object sender, RoutedEventArgs e) { ApplyView(); }
        private void ApplyView()
        {
            if (_updating) { return; }
            try
            {
                _view.AllRegions = CheckBoxAllRegions.IsChecked == true; _view.Regions = CheckBoxRegions.IsChecked == true; _view.Vwap = CheckBoxVwap.IsChecked == true; _view.Twap = CheckBoxTwap.IsChecked == true;
                _view.Bands = CheckBoxBands.IsChecked == true; _view.Swings = CheckBoxSwings.IsChecked == true; _view.Events = CheckBoxEvents.IsChecked == true; _view.WarmedUpOnly = CheckBoxWarmup.IsChecked == true;
                _view.Scale = ComboBoxScale.SelectedIndex - 1; _view.EventKind = ComboBoxKind.SelectedItem as string ?? "Все"; _view.SigmaMultiplier = ComboBoxSigma.SelectedItem is decimal sigma ? sigma : 1;
                _tables.CloseAll(); if (_run != null) { _chart.SetContext(_run.Data, _view); }
            }
            catch (Exception error) { Report(error); }
        }
        private void PricePeriodChanged(object sender, SelectionChangedEventArgs e) { try { if (!_updating && ComboBoxPricePeriod.SelectedValue is OrderFlowDisplayTimeFrame frame) { _chart.SetTimeFrame(frame); } } catch (Exception error) { Report(error); } }
        private void ChartChanged(object sender, EventArgs e)
        { try { _updating = true; SliderPosition.Maximum = Math.Max(0, _chart.TotalBars - _chart.VisibleCount); SliderPosition.Value = _chart.StartIndex; _updating = false; } catch (Exception error) { _updating = false; Report(error); } }
        private void PositionChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { try { if (!_updating) { _chart.ScrollTo((int)e.NewValue); } } catch (Exception error) { Report(error); } }
        private void ContextSelected(FlowContextEvent item) { try { TextBoxDetails.Text = FlowContextPresentation.Describe(item, _run.Data, _replay != null); } catch (Exception error) { Report(error); } }
        private IEnumerable<FlowContextEvent> VisibleEvents() => _run.Data.Events.Where(item => item.Sequence <= _chart.ContextSequence && _view.Matches(item) && (_view.Scale < 0 || item.Coordinates.Any(coordinate => coordinate.Scale == _view.Scale)));
        private void Navigate(int direction)
        {
            RequireResult(); List<FlowContextEvent> events = VisibleEvents().ToList(); if (events.Count == 0) { return; }
            int index = events.FindIndex(item => item.Id == _view.SelectedEventId); index = index < 0 ? direction > 0 ? 0 : events.Count - 1 : Math.Clamp(index + direction, 0, events.Count - 1); _chart.SelectContextEvent(events[index]);
        }
        private void ClearSelectionClick(object sender, RoutedEventArgs e)
        { try { _chart.ClearContextSelection(); TextBoxDetails.Text = string.Empty; } catch (Exception error) { Report(error); } }
        private void PreviousClick(object sender, RoutedEventArgs e) { try { Navigate(-1); } catch (Exception error) { Report(error); } }
        private void NextClick(object sender, RoutedEventArgs e) { try { Navigate(1); } catch (Exception error) { Report(error); } }
        private void ZoomInClick(object sender, RoutedEventArgs e) { try { _chart.Zoom(Math.Max(10, _chart.VisibleCount / 2)); } catch (Exception error) { Report(error); } }
        private void ZoomOutClick(object sender, RoutedEventArgs e) { try { _chart.Zoom(Math.Max(20, _chart.VisibleCount * 2)); } catch (Exception error) { Report(error); } }
        private string TableContext() => _run.Data.Settings.Instrument + " · " + (_replay == null ? "вся история" : "срез до строки " + _chart.ContextSequence) + " · таблица фиксирует текущий срез; открыть заново для обновления";
        private void RegionsClick(object sender, RoutedEventArgs e)
        {
            try
            {
                RequireResult(); List<RegionRow> rows = _run.Data.Regions.Where(region => region.KnownSequence <= _chart.ContextSequence && (_view.Scale < 0 || region.Scale == _view.Scale)).Select(region => new RegionRow(region, _run.Data.Settings, _chart.ContextSequence)).ToList();
                _tables.Open("regions", () => new FlowContextTableWindow("Области контекста", TableContext(), rows, selected => { RegionRow row = (RegionRow)selected; _chart.SelectContextRegion(row.Source); TextBoxDetails.Text = "Область " + row.Код + " · родитель " + row.Родитель + " · известна " + row.Подтверждена + " · исходный объём " + row.Объём + " · предшествующее движение " + row.Source.PriorChangeTicks + " шагов"; Activate(); }));
            }
            catch (Exception error) { Report(error); }
        }
        private void EventsClick(object sender, RoutedEventArgs e)
        {
            try { RequireResult(); _tables.Open("events", () => new FlowContextTableWindow("События контекста", TableContext(), VisibleEvents().Select(item => new EventRow(item)).ToList(), selected => { _chart.SelectContextEvent(((EventRow)selected).Source); Activate(); })); }
            catch (Exception error) { Report(error); }
        }
        private void StatisticsClick(object sender, RoutedEventArgs e)
        {
            try { RequireResult(); if (_replay != null) { throw new InvalidOperationException("Статистика будущих исходов доступна в режиме «Вся история»."); } _tables.Open("statistics", () => new FlowContextTableWindow("Исходы — без комиссий и исполнения", "Наблюдения одного дня/семейства зависимы. Complete — полный горизонт; прочие строки не входят в средние.", FlowContextPresentation.Statistics(_run.Data, _view), null)); }
            catch (Exception error) { Report(error); }
        }
        private sealed class RegionRow
        {
            internal FlowContextRegion Source;
            public string Код { get; }
            public string Масштаб { get; }
            public DateTime Начало { get; }
            public DateTime Подтверждена { get; }
            public decimal Низ { get; }
            public decimal Верх { get; }
            public decimal Объём { get; }
            public decimal Средняя { get; }
            public int Цепочек { get; }
            public string Родитель { get; }
            public string Состояние { get; }
            internal RegionRow(FlowContextRegion region, FlowContextSettings settings, long cursor)
            { Source = region; Код = region.Id; Масштаб = settings.Scales[region.Scale].Name; Начало = region.Start; Подтверждена = region.KnownAt; Низ = region.Low; Верх = region.High; Объём = region.SourceVolume; Средняя = region.InitialMean; Цепочек = region.CloudCount; Родитель = region.ParentId; Состояние = region.EndSequence <= cursor ? region.EndReason : "Активна / конец данных"; }
        }
        private sealed class EventRow
        {
            internal FlowContextEvent Source;
            public string Код { get; }
            public string Событие { get; }
            public DateTime Известно { get; }
            public DateTime Наблюдалось { get; }
            public decimal Цена { get; }
            public int Направление { get; }
            public decimal Объём { get; }
            public decimal Дельта { get; }
            public int Сделок { get; }
            public decimal ДоляРазмеров { get; }
            public decimal УскорениеTPS { get; }
            public bool ПослеПрогрева { get; }
            internal EventRow(FlowContextEvent item) { Source = item; Код = item.Id; Событие = item.Kind; Известно = item.Time; Наблюдалось = item.ObservedAt; Цена = item.Price; Направление = item.Direction; Объём = item.Volume; Дельта = item.Delta; Сделок = item.Trades; ДоляРазмеров = item.SameSizeShare; УскорениеTPS = item.TpsRatio; ПослеПрогрева = item.WarmedUp; }
        }
        #endregion

        #region Replay and manual areas
        private void ReplayClick(object sender, RoutedEventArgs e)
        {
            try
            {
                RequireResult(); if (_replay?.Finished == true) { ReturnHistory(); }
                if (_replay != null) { _replay.SetPaused(!_replay.Clock.Paused); ButtonReplay.Content = _replay.Clock.Paused ? "Продолжить" : "Пауза"; return; }
                FlowContextResult data = _run.Data;
                OrderFlowResearchRequest request = new OrderFlowResearchRequest { TicksFilePath = string.IsNullOrWhiteSpace(TextBoxInput.Text) ? data.SourcePath : TextBoxInput.Text.Trim(), FromDate = data.FromDate, ToDate = data.ToDate, PriceStep = data.Settings.PriceStep,
                    OutputRootPath = string.IsNullOrWhiteSpace(_outputRoot) ? Path.GetTempPath() : _outputRoot, CalculateDelta = false, CalculateCloud = true,
                    Cloud = new OrderFlowCloudSettings { MinimumTickVolume = decimal.MaxValue, MinimumSumVolume = decimal.MaxValue } };
                request.Validate(); _tables.CloseAll(); TextBoxDetails.Text = string.Empty;
                _chart.ClearDrawings(); _chart.SetDrawingTool(OrderFlowDrawingTool.Select);
                _chart.BeginReplay(1); _chart.SetContextReplay(0);
                _replay = new OrderFlowReplaySession(request, data.SourceHash, (double)ComboBoxSpeed.SelectedItem, true); _replay.Start(); ButtonReplay.Content = "Пауза";
                ButtonDraw.IsEnabled = ButtonAnchor.IsEnabled = ButtonRun.IsEnabled = ButtonOpen.IsEnabled = false;
            }
            catch (Exception error) { ReturnHistory(); Report(error); }
        }
        private void SpeedChanged(object sender, SelectionChangedEventArgs e) { try { if (!_updating && ComboBoxSpeed.SelectedItem is double speed) { _replay?.Clock.SetSpeed(speed); } } catch (Exception error) { Report(error); } }
        private void StepClick(object sender, RoutedEventArgs e) { try { if (_replay?.TryStep() == true) { ButtonStep.IsEnabled = false; } } catch (Exception error) { Report(error); } }
        private void HistoryClick(object sender, RoutedEventArgs e) { try { ReturnHistory(); } catch (Exception error) { Report(error); } }
        private void ReturnHistory()
        {
            if (_replay == null) { return; }
            _replay.Dispose(); _replay = null; _tables.CloseAll(); _chart.EndReplay(); TextBoxDetails.Text = string.Empty; ButtonReplay.Content = "Реплей"; ButtonStep.IsEnabled = false;
            ButtonDraw.IsEnabled = ButtonAnchor.IsEnabled = ButtonRun.IsEnabled = ButtonOpen.IsEnabled = true;
        }
        private void DrawClick(object sender, RoutedEventArgs e)
        {
            try { RequireResult(); _chart.SetDrawingStyle(Colors.DeepSkyBlue, 2, false, false); _chart.SetDrawingTool(OrderFlowDrawingTool.Trend); TextBlockStatus.Text = "Два клика: начало и конец исходного прямоугольника. Затем «Добавить выделение как якорь». Escape — отмена."; }
            catch (Exception error) { Report(error); }
        }
        private void AnchorClick(object sender, RoutedEventArgs e)
        {
            try
            {
                RequireResult(); OrderFlowChartLine line = _chart.SelectedDrawing ?? throw new InvalidOperationException("Сначала выделите область двумя кликами на графике.");
                FlowManualAnchor anchor = new FlowManualAnchor { Scale = _view.Scale < 0 ? 1 : _view.Scale, Start = line.First.Time < line.Second.Time ? line.First.Time : line.Second.Time,
                    KnownAt = line.First.Time > line.Second.Time ? line.First.Time : line.Second.Time, Low = Math.Min(line.First.Price, line.Second.Price), High = Math.Max(line.First.Price, line.Second.Price) };
                FlowContextSettings candidate = _settings.Copy(); candidate.ManualAnchors.Add(anchor); candidate.Validate(); _settings = candidate; _chart.SetDrawingTool(OrderFlowDrawingTool.Select);
                TextBlockStatus.Text = "Ручная область добавлена в профиль (всего " + _settings.ManualAnchors.Count + "). Нажмите «Рассчитать систему». Ручная разметка требует отдельной проверки на новых данных.";
            }
            catch (Exception error) { Report(error); }
        }
        private void ClearAnchorsClick(object sender, RoutedEventArgs e) { try { _settings.ManualAnchors.Clear(); _chart.ClearDrawings(); TextBlockStatus.Text = "Ручные якоря убраны из следующего расчёта. Текущее исследование сохранено."; } catch (Exception error) { Report(error); } }
        #endregion
    }
}
