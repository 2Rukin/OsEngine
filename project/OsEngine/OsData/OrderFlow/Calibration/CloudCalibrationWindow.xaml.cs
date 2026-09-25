using Microsoft.Win32;
using OsEngine.Entity;
using OsEngine.Language;
using OsEngine.Logging;
using OsEngine.Market;
using OsEngine.Themes;
using OsEngine.OsData.OrderFlow.Explorer;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace OsEngine.OsData.OrderFlow.Calibration
{
    /// <summary>Opt-in OsData calibration workspace. Workers own IO/calculation; the dispatcher owns controls and nonmodal views.</summary>
    /// <remarks>ORDER-FLOW-CLOUD-CALIBRATION-001. Closed cancels workers without joining the UI; no legacy settings or trading state are mutated.</remarks>
    public partial class CloudCalibrationWindow : Window
    {
        private readonly Func<ExplorerRunSpec> _input;
        private Action<CalibrationChartData, CalibrationMarker> _showMain;
        private readonly CalibrationWindowSet _windows = new CalibrationWindowSet();
        private readonly DispatcherTimer _poll, _debounce;
        private ExplorerJob _job;
        private Action<object> _completion;
        private string _jobKind;
        private CalibrationProgress _progress;
        private readonly object _progressLock = new object();
        private bool _closed, _restoring;
        private CalibrationRun _run;
        private ParameterCell _cell;
        private CloudRule _preview;
        private CalibrationChartData _chartData;
        private CalibrationMarker _selected;
        private ImmutableArray<CloudRule> _rules = ImmutableArray<CloudRule>.Empty;
        private readonly List<PinnedCandidate> _pins = new List<PinnedCandidate>();
        private readonly List<decimal> _tickPins = new List<decimal>();
        private readonly List<TimeRangeProfile> _profiles = TimeRangeProfile.Presets().ToList();
        private readonly Dictionary<string, (TextBox Minimum, TextBox Maximum)> _filters = new Dictionary<string, (TextBox, TextBox)>();
        private readonly Dictionary<string, CalibrationPlot> _plots = new Dictionary<string, CalibrationPlot>();
        private readonly CalibrationPlot _ticks = new CalibrationPlot(), _heatmap = new CalibrationPlot { Heatmap = true },
            _timeHistogram = new CalibrationPlot(), _timeMap = new CalibrationPlot { Heatmap = true };
        private readonly OrderFlowResearchChart _chart = new OrderFlowResearchChart { CalibrationTheme = true };
        private string _outputRoot;
        private static readonly string[] HeatMetrics = { "Chains / active day", "Singles / active day", "Volume P50", "Volume P95", "Volume P99",
            "AbsoluteDelta P50", "AbsoluteDelta P95", "TradeCount P50", "TradeCount P95", "Duration P50", "Duration P95", "RangeTicks P50", "RangeTicks P95",
            "AbsoluteDiagonalDelta P95", "StackLength P95", "NeighborSensitivity" };

        internal CloudCalibrationWindow(Func<ExplorerRunSpec> input, Action<CalibrationChartData, CalibrationMarker> showMain)
        {
            InitializeComponent(); _input = input; _showMain = showMain; Title = L("Cloud calibration", "Подбор Cloud");
            _restoring = true; Localize(this);
            _poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) }; _poll.Tick += Poll;
            _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) }; _debounce.Tick += Debounced;
            ContentControlTicks.Content = _ticks; ContentControlHeatmap.Content = _heatmap;
            ContentControlTimeHistogram.Content = _timeHistogram; ContentControlTimeMap.Content = _timeMap; ContentControlChart.Content = _chart;
            ComboBoxTimezone.ItemsSource = new[] { "" }.Concat(TimeZoneInfo.GetSystemTimeZones().Select(z => z.Id)).ToArray();
            ComboBoxSide.ItemsSource = new[] { "All", "Buy", "Sell" }; ComboBoxSide.SelectedIndex = 0;
            ComboBoxFormation.ItemsSource = Enum.GetValues<FormationMode>(); ComboBoxFormation.SelectedItem = FormationMode.Chain;
            ComboBoxRuleKind.ItemsSource = Enum.GetValues<RuleKind>(); ComboBoxRuleKind.SelectedIndex = 0;
            ComboBoxDeltaDirection.ItemsSource = Enum.GetValues<FlowDirection>(); ComboBoxDeltaDirection.SelectedIndex = 0;
            ComboBoxDiagonalDirection.ItemsSource = Enum.GetValues<FlowDirection>(); ComboBoxDiagonalDirection.SelectedIndex = 0;
            ComboBoxDiagonalSource.ItemsSource = Enum.GetValues<DiagonalSource>(); ComboBoxDiagonalSource.SelectedIndex = 0;
            ComboBoxBucket.ItemsSource = new[] { 5, 15, 30, 60 }; ComboBoxBucket.SelectedItem = 15;
            ComboBoxTimeMode.ItemsSource = new[] { "Single", "Chain", "Diagonal", "Passed rule" }; ComboBoxTimeMode.SelectedItem = "Passed rule";
            ComboBoxTimeMetric.ItemsSource = new[] { "Count", "Volume", "abs(Delta)", "abs(DiagonalDelta)" }; ComboBoxTimeMetric.SelectedIndex = 0;
            ComboBoxHeatMetric.ItemsSource = HeatMetrics; ComboBoxHeatMetric.SelectedIndex = 0;
            ComboBoxChartTimeFrame.ItemsSource = Enum.GetValues<OrderFlowDisplayTimeFrame>(); ComboBoxChartTimeFrame.SelectedItem = OrderFlowDisplayTimeFrame.Min1;
            foreach (ComboBox combo in new[] { ComboBoxSide, ComboBoxFormation, ComboBoxRuleKind, ComboBoxDeltaDirection, ComboBoxDiagonalDirection,
                ComboBoxDiagonalSource, ComboBoxTimeMode, ComboBoxTimeMetric, ComboBoxHeatMetric }) { CalibrationText.LocalizeItems(combo); }
            foreach (PropertyInfo property in typeof(CloudFilters).GetProperties().Where(p => p.PropertyType == typeof(NumericFilter)))
            {
                WrapPanel group = new WrapPanel { Margin = new Thickness(4) };
                TextBlock label = new TextBlock { Text = CalibrationText.Label(property.Name), ToolTip = property.Name, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Width = 155 };
                label.SetResourceReference(TextBlock.ForegroundProperty, "ControlForegroundWhite"); group.Children.Add(label);
                TextBox minimum = new TextBox { Width = 80, Margin = new Thickness(2), ToolTip = L("Inclusive minimum; empty disables", "Минимум включительно; пусто — выключен") };
                TextBox maximum = new TextBox { Width = 80, Margin = new Thickness(2), ToolTip = L("Inclusive maximum; empty disables", "Максимум включительно; пусто — выключен") };
                group.Children.Add(minimum); group.Children.Add(maximum); WrapPanelFilters.Children.Add(group); _filters.Add(property.Name, (minimum, maximum));
                minimum.TextChanged += FilterChanged; maximum.TextChanged += FilterChanged;
            }
            foreach (string metric in EventStatistics.Metrics)
            {
                CalibrationPlot plot = new CalibrationPlot { Width = 390, Height = 230, Margin = new Thickness(4) };
                _plots.Add(metric, plot); WrapPanelDistributions.Children.Add(plot);
            }
            _outputRoot = _input().OutputRootPath;
            LoadWorkspace(); RefreshProfiles(); Wire(true);
            TextBlockClockHelp.Text = L("Source clock. US pre-open requires an explicit source timezone; DST-aware overlay, no US holiday calendar. Evening ends at 23.51 exclusive.",
                "Время файла. US pre-open требует явной timezone файла; учитывается DST, календарь праздников США не применяется. Evening заканчивается в 23.51 исключительно.");
            TextBlockClockHelp.Text += L(" Analyze ticks refreshes the input snapshot; Form grid reuses its displayed SHA when input/dates/step match.",
                " Анализ тиков обновляет snapshot файла; сетка использует показанный SHA при совпадении input/dates/step.");
            TextBlockStatus.Text = L("Choose a profile, analyze ticks, select a threshold, then form a grid. Only you select the parameters.",
                "Выберите профиль, выполните анализ тиков, выберите порог, затем сформируйте сетку. Параметры выбираете только вы.");
            _restoring = false;
        }

        #region Dispatcher worker ownership

        private void StartJob(string kind, Func<CancellationToken, object> action, Action<object> completion)
        {
            _job?.Dispose(); _jobKind = kind; _completion = completion; _job = new ExplorerJob(action); _poll.Start();
            ButtonAnalyzeTicks.IsEnabled = ButtonRun.IsEnabled = ButtonOpen.IsEnabled = false; ButtonCancel.IsEnabled = true; ButtonSaveRule.IsEnabled = false;
            TextBlockStatus.Text = kind;
        }
        private void Progress(CalibrationProgress progress) { lock (_progressLock) { _progress = progress; } }
        private void Poll(object sender, EventArgs e)
        {
            try
            {
                if (_closed || _job == null) { return; }
                CalibrationProgress progress; lock (_progressLock) { progress = _progress; _progress = null; }
                if (progress != null) { TextBlockStatus.Text = progress.Stage + " · rows " + progress.Rows + " · " + progress.Date?.ToString("yyyy-MM-dd") + " · cell " + progress.Cell + "/" + progress.Cells; }
                if (!_job.TryResult(out object result, out Exception error)) { return; }
                _poll.Stop(); _job.Dispose(); _job = null; _jobKind = null;
                ButtonAnalyzeTicks.IsEnabled = ButtonRun.IsEnabled = ButtonOpen.IsEnabled = true; ButtonCancel.IsEnabled = false; UpdateProfileGate();
                Action<object> completion = _completion; _completion = null;
                if (error is OperationCanceledException) { TextBlockStatus.Text = L("Cancelled; no partial study was published", "Отменено; частичное исследование не опубликовано"); return; }
                if (error != null) { throw error; } completion?.Invoke(result);
            }
            catch (Exception error) { Report(error); }
        }
        private void RunClick(object sender, RoutedEventArgs e)
        {
            try
            {
                CalibrationSpec spec = ReadSpec(); CalibrationRun prepared = CalibrationEngine.CanReuse(_run, spec) ? _run : null;
                StartJob("Formation", token => CalibrationEngine.Run(spec, token, Progress, prepared), result => AcceptRun((CalibrationRun)result));
            }
            catch (Exception error) { Report(error); }
        }
        private void AnalyzeTicksClick(object sender, RoutedEventArgs e)
        {
            try { CalibrationSpec spec = ReadSpec(true); StartJob("Tick preparation", token => CalibrationEngine.Run(spec, token, Progress), result => AcceptRun((CalibrationRun)result)); }
            catch (Exception error) { Report(error); }
        }
        private void CancelClick(object sender, RoutedEventArgs e) { try { _job?.Cancel(); } catch (Exception error) { Report(error); } }
        private void OpenClick(object sender, RoutedEventArgs e)
        {
            try
            {
                OpenFileDialog dialog = new OpenFileDialog { Filter = "Calibration manifest|manifest.json", InitialDirectory = _outputRoot };
                if (dialog.ShowDialog() != true) { return; } string path = Path.GetDirectoryName(dialog.FileName);
                StartJob("Open", token => CalibrationStorage.Open(path, token), result => AcceptRun((CalibrationRun)result));
            }
            catch (Exception error) { Report(error); }
        }
        private void AcceptRun(CalibrationRun run, bool refreshTicks = true)
        {
            _windows.CloseAll(); _run = run; _cell = null; _chainCell = null; _preview = null; _editingRule = null; _selected = null; _chartData = null;
            _chart.SetCalibrationLayers(ImmutableArray<CalibrationLayer>.Empty); _chart.SetResult(null);
            _restoring = true;
            if (!_profiles.Any(p => p.Identity == run.Spec.Range.Identity)) { _profiles.Add(run.Spec.Range); }
            RefreshProfiles(); ComboBoxProfile.SelectedItem = _profiles.First(p => p.Identity == run.Spec.Range.Identity);
            ComboBoxTimezone.Text = run.Spec.Range.SourceTimeZone ?? ""; TextBoxTickVolume.Text = run.Spec.MinimumTickVolume.ToString("G29", CultureInfo.InvariantCulture);
            if (!run.Spec.TickDistributionOnly)
            { TextBoxGaps.Text = string.Join(";", run.Spec.Gaps); TextBoxRanges.Text = string.Join(";", run.Spec.Ranges); TextBoxContext.Text = run.Spec.ContextSeconds.ToString(); }
            ComboBoxGap.ItemsSource = run.Spec.Gaps; ComboBoxRange.ItemsSource = run.Spec.Ranges; ComboBoxGap.SelectedIndex = ComboBoxRange.SelectedIndex = -1;
            _restoring = false;
            CalibrationQuality q = run.Manifest.Quality;
            TextBlockQuality.Text = "SHA-256 " + q.InputSha256 + "\n" + L("Accepted", "Принято") + " " + q.Accepted + " · " + q.First?.ToString("O") + " … " + q.Last?.ToString("O") +
                "\n" + L("Source dates ", "Даты файла ") + q.SourceDates + L(" · Duplicate timestamps ", " · Повторные timestamp ") + q.DuplicateTimestamps + " · MicroSeconds=0 " + q.ZeroMicroseconds + " (" + N(q.ZeroMicrosecondsPercent) + "%)" +
                " · Buy/Sell " + q.BuyRows + "/" + q.SellRows;
            TextBlockIdentity.Text = run.Spec.Range.Name + " · " + run.Manifest.Hash;
            TextBlockCandidate.Text = run.Spec.TickDistributionOnly ? L("Tick distribution is ready without a grid. Select the threshold, then explicitly form the grid.",
                "Распределение тиков готово без сетки. Выберите порог, затем явно сформируйте сетку.") :
                L("Select a heatmap cell or use Gap/Range controls. No winner is selected.", "Выберите ячейку heatmap либо Gap/Range. Победитель не выбирается.");
            TextBlockFormation.Text = L("Select Single or a Chain candidate", "Выберите Single или вариант Chain");
            RenderHeatmap(); if (refreshTicks) { RefreshTicks(); }
        }
        private void RefreshTicks()
        {
            if (_run == null) { return; }
            CalibrationRun run = _run; decimal threshold = Number(TextBoxTickVolume.Text); Side? side = ComboBoxSide.SelectedIndex == 1 ? Side.Buy : ComboBoxSide.SelectedIndex == 2 ? Side.Sell : null;
            StartJob("Tick distribution", token => CalibrationEngine.Ticks(run.Directory, run.Spec, side, threshold, token), result => RenderTicks((TickStatistics)result));
        }
        private void Report(Exception error)
        { TextBlockStatus.Text = L("Error — ", "Ошибка — ") + (OsLocalization.CurLocalization == OsLocalization.OsLocalType.Ru ? ExplorerValidation.UserMessage(error, _run?.Spec.Hash ?? "calibration") : error.Message);
            ButtonSaveRule.IsEnabled = false; ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error); }

        #endregion

        #region Profiles and reproducible controls

        private CalibrationSpec ReadSpec(bool ticksOnly = false)
        {
            ExplorerRunSpec input = _input(); _outputRoot = input.OutputRootPath;
            TimeRangeProfile range = SelectedProfile();
            CalibrationSpec spec = new CalibrationSpec { InputPath = input.InputPath, OutputRootPath = input.OutputRootPath, PriceStep = input.PriceStep,
                FromDate = input.FromDate, ToDate = input.ToDate, Range = range, MinimumTickVolume = Number(TextBoxTickVolume.Text), TickDistributionOnly = ticksOnly,
                Gaps = ticksOnly ? ImmutableArray<int>.Empty : GridValues(TextBoxGaps.Text), Ranges = ticksOnly ? ImmutableArray<int>.Empty : GridValues(TextBoxRanges.Text), ContextSeconds = ticksOnly ? 30 : Integer(TextBoxContext.Text),
                MaximumCells = ticksOnly ? 128 : Integer(TextBoxCellBudget.Text), MaximumBufferItems = Integer(TextBoxBufferBudget.Text),
                MaximumMemoryMegabytes = Integer(TextBoxMemoryBudget.Text), MaximumCacheBytes = checked((long)Integer(TextBoxDiskBudget.Text) * 1024 * 1024),
                Diagonal = ticksOnly ? new DiagonalSettings() : ReadDiagonal() with { Enabled = false } };
            spec.Validate(); return spec;
        }
        private TimeRangeProfile SelectedProfile()
        {
            if (ComboBoxProfile.SelectedItem is not TimeRangeProfile profile) { throw new ArgumentException(L("Select a time profile", "Выберите временной профиль")); }
            return profile with { SourceTimeZone = string.IsNullOrWhiteSpace(ComboBoxTimezone.Text) ? null : ComboBoxTimezone.Text.Trim() };
        }
        private static ImmutableArray<int> GridValues(string text) => text.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(Integer).OrderBy(v => v).ToImmutableArray();
        private static int Integer(string text) => int.Parse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture);
        private static decimal Number(string text) => ExplorerValidation.Number(text, "Value");
        private static decimal? Optional(string text) => string.IsNullOrWhiteSpace(text) ? null : Number(text);
        private static string N(decimal value) => value.ToString("G6", CultureInfo.InvariantCulture);
        private static string L(string en, string ru) => OsLocalization.ConvertToLocString("Eng:" + en + "_Ru:" + ru + "_");
        private void CustomClick(object sender, RoutedEventArgs e)
        {
            try
            {
                TimeRangeProfile profile = new TimeRangeProfile { Id = Guid.NewGuid().ToString("N"), Name = TextBoxProfileName.Text.Trim(), DayMask = Integer(TextBoxDays.Text),
                    StartTime = Clock(TextBoxStart.Text), EndTime = Clock(TextBoxEnd.Text), Tag = TextBoxTag.Text.Trim(), SourceTimeZone = string.IsNullOrWhiteSpace(ComboBoxTimezone.Text) ? null : ComboBoxTimezone.Text.Trim() };
                profile.Validate(); _profiles.Add(profile); RefreshProfiles(); ComboBoxProfile.SelectedItem = profile; SaveWorkspace();
            }
            catch (Exception error) { Report(error); }
        }
        private static TimeSpan Clock(string text) => text.Trim() == "24:00" ? TimeSpan.FromDays(1) : TimeSpan.Parse(text, CultureInfo.InvariantCulture);
        private void RefreshProfiles()
        {
            TimeRangeProfile selected = ComboBoxProfile.SelectedItem as TimeRangeProfile;
            ComboBoxProfile.ItemsSource = _profiles.ToArray(); ComboBoxCopyRange.ItemsSource = _profiles.ToArray();
            ComboBoxProfile.SelectedItem = selected ?? _profiles[1];
        }
        private void LoadWorkspace()
        {
            string path = Path.Combine(_outputRoot, "cloud-calibration-workspace.json"); if (!File.Exists(path)) { return; }
            CalibrationWorkspace state = JsonSerializer.Deserialize<CalibrationWorkspace>(File.ReadAllText(path), ExplorerStorage.Json);
            if (state == null || state.CustomProfiles.IsDefault || state.Pins.IsDefault || state.TickThresholds.IsDefault) { throw new InvalidDataException("Invalid calibration workspace."); }
            foreach (TimeRangeProfile profile in state.CustomProfiles) { profile.Validate(); _profiles.Add(profile); }
            _pins.AddRange(state.Pins); _tickPins.AddRange(state.TickThresholds);
        }
        private void SaveWorkspace()
        {
            Directory.CreateDirectory(_outputRoot);
            CalibrationStorage.AtomicJson(Path.Combine(_outputRoot, "cloud-calibration-workspace.json"),
                new CalibrationWorkspace(_profiles.Where(p => p.Kind == "Custom").ToImmutableArray(), _pins.ToImmutableArray(), _tickPins.ToImmutableArray()), CancellationToken.None);
        }
        private void FormationChanged(object sender, EventArgs e)
        {
            try
            {
                if (_restoring) { return; }
                UpdateProfileGate();
                TextBlockFormation.Text = L("Formation fields changed — a new formation is required. Post-filters still use the explicitly selected saved candidate.",
                    "Поля формирования изменены — требуется новое формирование. Post-фильтры используют явно выбранный сохранённый вариант.");
                if (sender == TextBoxTickVolume || sender == ComboBoxSide) { if (_job == null || _jobKind == "Tick distribution") { RefreshTicks(); } }
            }
            catch (Exception error) { Report(error); }
        }
        private void UpdateProfileGate()
        {
            bool enabled = ComboBoxProfile.SelectedItem is TimeRangeProfile profile && (profile.ClockMode != "USPreOpen" || !string.IsNullOrWhiteSpace(ComboBoxTimezone.Text));
            ButtonAnalyzeTicks.IsEnabled = ButtonRun.IsEnabled = _job == null && enabled;
            if (!enabled) { TextBlockStatus.Text = L("US pre-open is disabled until you explicitly choose the source timezone", "US pre-open отключён до явного выбора timezone файла"); }
        }
        private void ThemeChanged()
        {
            try { if (_closed) { return; } if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(ThemeChanged)); return; } _chart.InvalidateVisual(); }
            catch (Exception error) { Report(error); }
        }

        #endregion

        #region Lifecycle and localization

        private static void Localize(DependencyObject root)
        {
            if (root is FrameworkElement element && element.Tag is string tag && tag.Contains('|'))
            {
                string[] parts = tag.Split('|'); string value = L(parts[0], parts[1]);
                if (element is TabItem tab) { tab.Header = value; }
                else if (element is ContentControl content) { content.Content = value; }
            }
            foreach (object child in LogicalTreeHelper.GetChildren(root)) { if (child is DependencyObject dependency) { Localize(dependency); } }
        }
        private void Wire(bool add)
        {
            Button[] buttons = { ButtonAnalyzeTicks, ButtonRun, ButtonCancel, ButtonOpen, ButtonCustom, ButtonTicks, ButtonPinTick, ButtonSelectCell, ButtonPin, ButtonCompare,
                ButtonCreateRule, ButtonChains, ButtonDiagonal, ButtonSaveRule, ButtonCopyRule, ButtonPassed, ButtonRules, ButtonRefreshLayers,
                ButtonAnatomy, ButtonMainChart, ButtonChartAll, ButtonChartPlus, ButtonChartMinus };
            RoutedEventHandler[] handlers = { AnalyzeTicksClick, RunClick, CancelClick, OpenClick, CustomClick, TicksClick, PinTickClick, SelectCellClick, PinClick, CompareClick,
                CreateRuleClick, ChainsClick, DiagonalClick, SaveRuleClick, CopyRuleClick, PassedClick, RulesClick, RefreshLayersClick,
                AnatomyClick, MainChartClick, ChartAllClick, ChartPlusClick, ChartMinusClick };
            for (int i = 0; i < buttons.Length; i++) { if (add) { buttons[i].Click += handlers[i]; } else { buttons[i].Click -= handlers[i]; } }
            TextBox[] formation = { TextBoxTickVolume, TextBoxGaps, TextBoxRanges, TextBoxContext, TextBoxCellBudget, TextBoxBufferBudget, TextBoxMemoryBudget, TextBoxDiskBudget };
            foreach (TextBox text in formation) { if (add) { text.TextChanged += FormationChanged; } else { text.TextChanged -= FormationChanged; } }
            TextBox[] diagonal = { TextBoxRatio, TextBoxDominant, TextBoxDifference, TextBoxStack };
            foreach (TextBox text in diagonal) { if (add) { text.TextChanged += FilterChanged; } else { text.TextChanged -= FilterChanged; } }
            ComboBox[] filters = { ComboBoxFormation, ComboBoxRuleKind, ComboBoxDeltaDirection, ComboBoxDiagonalDirection, ComboBoxDiagonalSource, ComboBoxBucket, ComboBoxTimeMode, ComboBoxTimeMetric };
            foreach (ComboBox combo in filters) { if (add) { combo.SelectionChanged += FilterChanged; } else { combo.SelectionChanged -= FilterChanged; } }
            if (add)
            {
                ComboBoxProfile.SelectionChanged += FormationChanged; ComboBoxTimezone.SelectionChanged += FormationChanged; ComboBoxSide.SelectionChanged += FormationChanged;
                ComboBoxHeatMetric.SelectionChanged += HeatMetricChanged; ComboBoxChartTimeFrame.SelectionChanged += ChartFrameChanged;
                CheckBoxDiagonalFilter.Click += FilterChanged; _ticks.Selected += TickSelected; _heatmap.Selected += CellSelected;
                _chart.CalibrationSelected += ChartSelected; Closed += WindowClosed;
                ThemeManager.ThemeChangedEvent += ThemeChanged;
                ComboBoxTimezone.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler(FormationChanged));
            }
            else
            {
                ComboBoxProfile.SelectionChanged -= FormationChanged; ComboBoxTimezone.SelectionChanged -= FormationChanged; ComboBoxSide.SelectionChanged -= FormationChanged;
                ComboBoxHeatMetric.SelectionChanged -= HeatMetricChanged; ComboBoxChartTimeFrame.SelectionChanged -= ChartFrameChanged;
                CheckBoxDiagonalFilter.Click -= FilterChanged; _ticks.Selected -= TickSelected; _heatmap.Selected -= CellSelected;
                _chart.CalibrationSelected -= ChartSelected; Closed -= WindowClosed;
                ThemeManager.ThemeChangedEvent -= ThemeChanged;
                ComboBoxTimezone.RemoveHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler(FormationChanged));
            }
        }
        private void WindowClosed(object sender, EventArgs e)
        {
            try
            {
                _closed = true; Wire(false); _poll.Stop(); _debounce.Stop(); _poll.Tick -= Poll; _debounce.Tick -= Debounced;
                _job?.Dispose(); _job = null; _completion = null; _showMain = null; _windows.Dispose();
                foreach ((TextBox minimum, TextBox maximum) in _filters.Values) { minimum.TextChanged -= FilterChanged; maximum.TextChanged -= FilterChanged; }
                foreach (Button button in WrapPanelQuantiles.Children.OfType<Button>()) { button.Click -= QuantileClick; }
                _chart.SetCalibrationLayers(ImmutableArray<CalibrationLayer>.Empty); _chart.SetResult(null);
            }
            catch (Exception error) { ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error); }
        }

        #endregion
    }
}
