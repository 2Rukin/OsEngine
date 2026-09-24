/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.Entity;
using OsEngine.Logging;
using OsEngine.Market;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace OsEngine.OsData.OrderFlow.Explorer
{
    /// <summary>Opt-in offline Explorer workbench. Workers own file handles; this control owns dispatcher, cancellation and chart transfer.</summary>
    public partial class CloudExplorerControl : IDisposable
    {
        private readonly Dictionary<string, List<ExplorerOption>> _profiles = new Dictionary<string, List<ExplorerOption>>();
        private readonly Dictionary<string, List<ExplorerOption>> _views = new Dictionary<string, List<ExplorerOption>>();
        private readonly List<ExplorerOption> _episodeOptions;
        private readonly List<ExplorerOption> _studyOptions;
        private readonly ExplorerChart _chart;
        private readonly DispatcherTimer _timer;
        private readonly object _progressLock = new object();
        private ExplorerProgress _progress;
        private ExplorerJob _job;
        private Action<object> _finish;
        private ExplorerPlayback _playback;
        private ExplorerRun _run;
        private ExplorerView _view = new ExplorerView();
        private ExplorerPage _page;
        private ExplorerAnchor _anchor;
        private ExplorerAnchor _selectedAnchor;
        private ImmutableArray<ExplorerVwapSample> _vwap = ImmutableArray<ExplorerVwapSample>.Empty;
        private IReadOnlyList<ExplorerObservation> _observations = Array.Empty<ExplorerObservation>();
        private IReadOnlyList<ExplorerPivot> _pivots = Array.Empty<ExplorerPivot>();
        private IReadOnlyList<ExplorerEpisode> _episodes = Array.Empty<ExplorerEpisode>();
        private IReadOnlyList<ExplorerBar> _bars = Array.Empty<ExplorerBar>();
        private OrderFlowDisplayTimeFrame _timeFrame = OrderFlowDisplayTimeFrame.Min1;
        private string _appliedOptions;
        private CloudExplorerChartWindow _window;
        private bool _disposed;
        private long _pageStart;
        private ExplorerFrame _frame;
        private string _attemptId = Guid.NewGuid().ToString("N");
        internal Func<ExplorerRunSpec> RequestProvider { get; set; }
        internal Func<string> InputFingerprint { get; set; }
        internal Action<string> InputFocus { get; set; }

        /// <summary>Creates only the editable workbench; no file is opened until an explicit calculation, reopen or replay action.</summary>
        public CloudExplorerControl()
        {
            InitializeComponent();
            foreach (string layer in new[] { "Cloud1", "Cloud2" })
            {
                foreach ((string Name, int Range, decimal Factor) scale in new[] { ("Narrow", 2, .1m), ("Base", 5, .2m), ("Wide", 10, .4m) })
                {
                    ExplorerProfile profile = new ExplorerProfile { Layer = layer, Scale = scale.Name, SingleTicks = layer == "Cloud2", MaximumRangeTicks = scale.Range, AtrFactor = scale.Factor };
                    _profiles[profile.Key] = ExplorerOptions.Create(profile, "Layer", "Scale");
                    _views[profile.Key] = ViewOptions(new ExplorerView());
                }
            }
            _episodeOptions = ExplorerOptions.Create(new ExplorerEpisodeSpec());
            _studyOptions = ExplorerOptions.Create(new ExplorerStudySpec());
            DataGridModules.ItemsSource = _episodeOptions.Concat(_studyOptions.Where(o => o.Property.Name.Contains("Swing", StringComparison.Ordinal))).ToArray();
            DataGridStudy.ItemsSource = _studyOptions.Where(o => !o.Property.Name.Contains("Swing", StringComparison.Ordinal)).ToArray();
            ComboBoxProfile.ItemsSource = _profiles.Keys;
            ComboBoxProfile.SelectionChanged += ProfileChanged;
            ComboBoxProfile.SelectedItem = "Cloud1/Base";
            _chart = new ExplorerChart(); ContentControlPlot.Content = _chart; _chart.Selected += ChartSelected;
            _chart.ObservationSelected += ChartObservationSelected;
            _chart.Failed += Error;
            ComboBoxTimeFrame.ItemsSource = OrderFlowChartTimeFrames.GetMenuValues(); ComboBoxTimeFrame.SelectedItem = _timeFrame;
            ComboBoxTimeFrame.SelectionChanged += TimeFrameChanged;
            ComboBoxPriceStyle.ItemsSource = new[] { "Свечи", "Бары", "High-Low" }; ComboBoxPriceStyle.SelectedIndex = 0;
            ComboBoxPriceStyle.SelectionChanged += PriceStyleChanged; CheckBoxDraw.Click += DrawChanged; ButtonClearLines.Click += ClearLines;
            ButtonReopen.Click += Reopen;
            ButtonFitPrice.Click += FitPrice; ButtonResetAxes.Click += ResetAxes;
            ButtonCalculate.Click += Calculate; ButtonCancel.Click += Cancel; ButtonFilter.Click += ApplyFilter;
            ButtonFirst.Click += FirstPage; ButtonNext.Click += NextPage; ButtonFind.Click += Find;
            ButtonArtifacts.Click += OpenArtifacts; ButtonAnchor.Click += Anchor; ButtonSeparate.Click += Separate;
            ButtonReplay.Click += Replay; ButtonPause.Click += Pause; ButtonStep.Click += Step; ButtonHistory.Click += History;
            CheckBoxBands.Click += Bands; DataGridCatalog.SelectionChanged += CatalogSelected; DataGridCatalog.MouseDoubleClick += CatalogDoubleClick;
            DataGridObservations.SelectionChanged += ObservationSelected; DataGridEpisodes.SelectionChanged += EpisodeSelected;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) }; _timer.Tick += Poll; _timer.Start();
            InitializePatterns();
            foreach (DataGrid grid in ResultGrids()) { grid.AutoGeneratingColumn += TranslateColumn; }
        }

        #region Settings and worker ownership

        private static List<ExplorerOption> ViewOptions(ExplorerView view) => ExplorerOptions.Create(view, "Schema", "Profile", "IdContains", "Layers", "RelatedCloudIds");
        private void ProfileChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (ComboBoxProfile.SelectedItem is not string profile) { return; }
                string[] formation = { "SingleTicks", "AllTicks", "MinimumTickVolume", "MaximumGapMilliseconds", "MaximumRangeTicks", "ContextSeconds" };
                DataGridFormation.ItemsSource = _profiles[profile].Where(o => formation.Contains(o.Property.Name)).ToArray();
                DataGridAdaptation.ItemsSource = _profiles[profile].Where(o => !formation.Contains(o.Property.Name)).Concat(_studyOptions.Where(o => o.Property.Name.StartsWith("Activity", StringComparison.Ordinal))).ToArray();
                DataGridView.ItemsSource = _views[profile];
            }
            catch (Exception error) { Error(error); }
        }
        private ExplorerRunSpec ReadSpec()
        {
            CommitEdits(); ExplorerRunSpec input = RequestProvider?.Invoke() ?? throw new InvalidOperationException("Explorer input owner is missing.");
            List<ExplorerProfile> profiles = new List<ExplorerProfile>();
            foreach (KeyValuePair<string, List<ExplorerOption>> pair in _profiles)
            {
                string[] key = pair.Key.Split('/');
                if ((key[0] == "Cloud1" && CheckBoxLayer1.IsChecked != true) || (key[0] == "Cloud2" && CheckBoxLayer2.IsChecked != true) ||
                    (key[1] != "Base" && CheckBoxScales.IsChecked != true)) { continue; }
                try { profiles.Add(ExplorerOptions.Read(new ExplorerProfile { Layer = key[0], Scale = key[1] }, pair.Value)); }
                catch (ExplorerInputException error) { throw new ExplorerInputException(error.Field, ExplorerValidation.UserMessage(error, ""), pair.Key, error); }
            }
            ExplorerRunSpec spec = input with { Profiles = profiles.ToImmutableArray(), Episodes = ExplorerOptions.ReadScoped(new ExplorerEpisodeSpec(), _episodeOptions, "Episodes"), Study = ExplorerOptions.ReadScoped(new ExplorerStudySpec(), _studyOptions, "Study"),
                MaximumBufferItems = ExplorerValidation.Integer(TextBoxBufferLimit.Text, "MaximumBufferItems"), MaximumMemoryMegabytes = ExplorerValidation.Integer(TextBoxMemoryLimit.Text, "MaximumMemoryMegabytes") };
            spec.Validate(); return spec;
        }
        private ExplorerView ReadView()
        {
            CommitEdits(); ImmutableDictionary<string, ExplorerView>.Builder layers = ImmutableDictionary.CreateBuilder<string, ExplorerView>();
            foreach (KeyValuePair<string, List<ExplorerOption>> pair in _views)
            {
                try { ExplorerView view = ExplorerOptions.Read(new ExplorerView(), pair.Value); ExplorerValidation.View(view); layers.Add(pair.Key, view); }
                catch (ExplorerInputException error) { throw new ExplorerInputException("View." + error.Field, ExplorerValidation.UserMessage(error, ""), pair.Key, error); }
            }
            return new ExplorerView { Layers = layers.ToImmutable(), ShowControl = layers.Values.Any(v => v.ShowControl) };
        }
        private void CommitEdits()
        {
            foreach (DataGrid grid in new[] { DataGridFormation, DataGridAdaptation, DataGridView, DataGridModules, DataGridStudy })
            { grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true); }
        }
        private void StartJob(Func<CancellationToken, object> action, Action<object> finish)
        {
            if (_job != null) { throw new InvalidOperationException("Дождитесь завершения операции или нажмите Отмена."); }
            _attemptId = Guid.NewGuid().ToString("N"); _finish = finish; _job = new ExplorerJob(action); TextBlockStatus.Text = "Выполняется…";
        }
        private void Progress(ExplorerProgress progress) { lock (_progressLock) { _progress = progress; } }
        private void Poll(object sender, EventArgs e)
        {
            try
            {
                if (_disposed) { return; }
                lock (_progressLock)
                {
                    if (_progress != null)
                    {
                        TextBlockStatus.Text = $"{ExplorerValidation.Status(_progress.Phase)}: {_progress.Rows:N0} строк; {_progress.Clouds:N0} событий; {_progress.Date:yyyy-MM-dd}; {_progress.MemoryBytes / 1048576} МБ; {_progress.Seconds:F1} с";
                        _progress = null;
                    }
                }
                if (_job != null && _job.TryResult(out object result, out Exception error))
                {
                    ExplorerJob job = _job; Action<object> finish = _finish; _job = null; _finish = null;
                    job.Dispose();
                    if (error is OperationCanceledException) { TextBlockStatus.Text = "Отменено. Незавершённый результат не опубликован."; }
                    else if (error != null) { Error(error); }
                    else { finish(result); }
                }
                if (_job == null && _pendingObservation != null) { ExplorerObservation observation = _pendingObservation; _pendingObservation = null; ShowObservation(observation); }
                if (_job == null && _pendingPatternExample) { _pendingPatternExample = false; LoadPatternExample(); }
                if (_job == null && _pendingInterval.HasValue) { (DateTime from, DateTime to) = _pendingInterval.Value; _pendingInterval = null; ShowInterval(from, to); }
                if (_playback != null && _playback.Take(out ExplorerFrame frame, out Exception replayError))
                {
                    if (replayError != null) { Error(replayError); StopPlayback(); }
                    else if (frame != null)
                    {
                        _frame = frame; PaintFrame(frame);
                        TextBlockStatus.Text = $"Реплей: {frame.Time:yyyy-MM-dd HH:mm:ss.fffffff}; ordinal {frame.Sequence}; {(frame.Complete ? "EOF" : "причинный префикс")}";
                        DataGridLabels.ItemsSource = frame.Labels; DataGridObservations.ItemsSource = frame.Observations;
                        DataGridTriggers.ItemsSource = frame.Triggers; DataGridPivots.ItemsSource = frame.Pivots;
                        DataGridDiagnostics.ItemsSource = frame.Watches.Cast<object>().Concat(frame.Diagnostics).ToArray();
                        DataGridEpisodes.ItemsSource = frame.Episodes;
                        TextBlockEpisodeState.Text = $"Причинный кадр: показано {frame.Episodes.Length} последних эпизодов, {frame.Episodes.Sum(p => p.ChildCount)} дочерних Cloud. Будущие итоги скрыты.";
                        PaintPatternFrame(frame);
                        ExplorerView frameView = FrameView(frame);
                        DataGridCatalog.ItemsSource = frame.Clouds.Select(c => new CatalogRow(c, frameView.Passes(c, _run.Spec.PriceStep),
                            frame.Triggers.FirstOrDefault(t => t.VolumeId == c.Id), null)).ToArray();
                        TextBoxSummary.Text = $"Причинный кадр #{frame.Sequence}: на экране {frame.Clouds.Length} Cloud; прошло {frame.Clouds.Count(c => frameView.Passes(c, _run.Spec.PriceStep))}. Экран хранит не более 250 последних объектов каждого типа.";
                    }
                }
                if (_job == null && _run != null)
                { TextBlockIdentity.Text = Identity() + (OptionsIdentity() != _appliedOptions ? " · Изменённые вычислительные параметры не применены" : ""); }
            }
            catch (Exception error) { Error(error); }
        }
        private void Error(Exception error)
        {
            TextBlockStatus.Text = ExplorerValidation.UserMessage(error, _attemptId); FocusInput(error as ExplorerInputException);
            string diagnostic = "Cloud Explorer attempt=" + _attemptId + " " + error;
            // ServerMaster.Error opens a raw-stack MessageBox when no log subscriber exists (notably in OsData).
            // Persist diagnostics independently, then use the existing non-modal System logging route.
            if (ExplorerValidation.NeedsDiagnosticFile(error))
            {
                try
                {
                    string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OsEngine", "CloudExplorer", "Diagnostics");
                    Directory.CreateDirectory(directory); File.AppendAllText(Path.Combine(directory, _attemptId + ".log"), diagnostic + Environment.NewLine);
                }
                catch (Exception logError) { ServerMaster.SendNewLogMessage("Cloud Explorer diagnostic write failed: " + logError, LogMessageType.System); }
            }
            ServerMaster.SendNewLogMessage(diagnostic, LogMessageType.System);
        }

        #endregion

        #region Catalog, filters and selection

        private void Calculate(object sender, RoutedEventArgs e)
        {
            try
            {
                ExplorerRunSpec spec = ReadSpec(); ExplorerView view = ReadView(); ExplorerValidation.Preflight(spec); string options = OptionsIdentity(); StopPlayback();
                StartJob(token =>
                {
                    return ExplorerAttempt.Run(spec, token, Progress);
                }, result =>
                {
                    InstallRun((ExplorerRun)result); _view = view; _appliedOptions = options;
                    string saved = Path.Combine(_run.Spec.OutputRootPath, "cloud-explorer-view", _run.Spec.CatalogSpecHash + ".json");
                    if (File.Exists(saved) && _views.Values.SelectMany(v => v).All(o => o.Value == o.Initial)) { _view = ExplorerStorage.LoadView(_run); RestoreViews(); }
                    foreach (ExplorerOption option in _profiles.Values.SelectMany(v => v).Concat(_episodeOptions).Concat(_studyOptions)) { option.Initial = option.Value; }
                    TextBlockIdentity.Text = Identity(); _pageStart = 0; LoadPage(true);
                });
            }
            catch (Exception error) { Error(error); }
        }
        private string Identity() => $"{ExplorerRunSpec.CatalogVersion} · {_run.Spec.CatalogSpecHash} · {(_run.Spec.FromDate ?? _run.Catalog.Dates.FirstOrDefault()):yyyy-MM-dd}…{(_run.Spec.ToDate ?? _run.Catalog.Dates.LastOrDefault()):yyyy-MM-dd} · " +
            (_run.Catalog.SecondsOnly ? "точность источника 1 с; порядок внутри секунды — ordinal" : "дробные секунды источника") + $" · всего {_run.Catalog.Clouds:N0}" +
            $"\n{string.Join(", ", _run.Spec.Profiles.Select(p => p.Key))} · {ExplorerRunSpec.EpisodeVersion} {_run.Spec.EpisodeSpecHash ?? "off"} · {ExplorerRunSpec.StudyVersion} {_run.Spec.StudyHash ?? "off"}";
        private void LoadPage(bool summary)
        {
            if (_run == null) { return; }
            if (_playback != null) { throw new InvalidOperationException("Для просмотра исторических страниц нажмите «История»."); }
            ExplorerRun run = _run; ExplorerView view = _view; long start = _pageStart; string search = TextBoxFind.Text.Trim();
            Dictionary<string, long> offsets = new Dictionary<string, long>(_tableOffsets);
            long PageOffset(string name) => offsets.TryGetValue(name, out long value) ? value : 0;
            DateTime? chartFrom = _chartFrom, chartTo = _chartTo;
            ExplorerChartContext context = null;
            Dictionary<string, (long Unknown, long Eligible)> profileQuality = null;
            OrderFlowDisplayTimeFrame timeFrame = _timeFrame;
            StartJob(token =>
            {
                view = ExplorerStorage.ResolveRelations(run, view, token); ExplorerStorage.SaveView(run, view);
                ExplorerPage page = ExplorerStorage.Page(run.CatalogPath, view with { IdContains = search }, run.Spec.PriceStep, start, 250, token);
                IReadOnlyList<ExplorerSummary> totals = summary ? ExplorerStorage.Summarize(run.CatalogPath, run.Spec, view, token) : null;
                if (summary)
                {
                    profileQuality = run.Spec.Profiles.ToDictionary(p => p.Key, p => (0L, 0L));
                    foreach (ExplorerCloud cloud in ExplorerStorage.ReadRows<ExplorerCloud>(run.CatalogPath, "catalog"))
                    {
                        token.ThrowIfCancellationRequested(); (long unknown, long eligible) = profileQuality[cloud.Profile];
                        profileQuality[cloud.Profile] = (unknown + (cloud.RelativeStatus == "Unknown" ? 1 : 0), eligible + (cloud.Reason != "OpenAtEnd" ? 1 : 0));
                    }
                }
                ExplorerEpisode[] episodes = AuxPage<ExplorerEpisode>(run.EpisodePath, "episodes", PageOffset("episodes"));
                ExplorerObservation[] observations = AuxPage<ExplorerObservation>(run.StudyPath, "observations", PageOffset("observations"));
                ExplorerLabel[] labels = AuxPage<ExplorerLabel>(run.StudyPath, "future-labels", PageOffset("future-labels"));
                ExplorerPivot[] pivots = AuxPage<ExplorerPivot>(run.StudyPath, "pivots", PageOffset("pivots"));
                ExplorerTrigger[] triggers = AuxPage<ExplorerTrigger>(run.StudyPath, "triggers", PageOffset("triggers"));
                ExplorerDiagnostic[] diagnostics = AuxPage<ExplorerDiagnostic>(run.StudyPath, "diagnostics", PageOffset("diagnostics"));
                chartFrom ??= page.Rows.Count == 0 ? null : page.Rows.Min(c => c.StartTime);
                chartTo ??= page.Rows.Count == 0 ? null : page.Rows.Max(c => c.Time).AddSeconds(1);
                IReadOnlyList<ExplorerBar> bars = !chartFrom.HasValue ? Array.Empty<ExplorerBar>() : ExplorerBars.ReadRange(run.CatalogPath, chartFrom.Value, chartTo.Value, timeFrame, token);
                if (chartFrom.HasValue) { context = ExplorerChartContext.Load(run, chartFrom.Value, chartTo.Value, timeFrame, token); }
                Dictionary<string, (ExplorerTrigger Trigger, ExplorerMembership Membership)> evidence = new Dictionary<string, (ExplorerTrigger, ExplorerMembership)>();
                foreach (ExplorerCloud cloud in page.Rows)
                {
                    token.ThrowIfCancellationRequested();
                    ExplorerTrigger trigger = run.StudyPath == null || cloud.Profile != run.Spec.Study.Profile || run.Spec.Study.EpisodeTrigger ? null :
                        ExplorerStorage.FindOrdered<ExplorerTrigger>(run.StudyPath, "triggers", cloud.FirstSequence, t => t.FirstSequence);
                    ExplorerMembership membership = run.EpisodePath == null || cloud.Profile != run.Spec.Episodes.Profile ? null :
                        ExplorerStorage.FindOrdered<ExplorerMembership>(run.EpisodePath, "membership", cloud.FirstSequence, m => m.FirstSequence);
                    evidence[cloud.Id] = (trigger, membership);
                }
                string comparison = run.StudyPath == null ? "Включите исследование и выполните расчёт." : File.ReadAllText(Path.Combine(run.StudyPath, "study-summary.json")) + "\n\n" + File.ReadAllText(Path.Combine(run.StudyPath, "comparison.csv"));
                return (page, totals, episodes, observations, labels, pivots, triggers, diagnostics, bars, comparison, evidence);
            }, result =>
            {
                (ExplorerPage page, IReadOnlyList<ExplorerSummary> totals, ExplorerEpisode[] episodes, ExplorerObservation[] observations, ExplorerLabel[] labels, ExplorerPivot[] pivots,
                    ExplorerTrigger[] triggers, ExplorerDiagnostic[] diagnostics, IReadOnlyList<ExplorerBar> bars, string comparison, Dictionary<string, (ExplorerTrigger Trigger, ExplorerMembership Membership)> evidence) =
                    ((ExplorerPage, IReadOnlyList<ExplorerSummary>, ExplorerEpisode[], ExplorerObservation[], ExplorerLabel[], ExplorerPivot[], ExplorerTrigger[], ExplorerDiagnostic[], IReadOnlyList<ExplorerBar>, string, Dictionary<string, (ExplorerTrigger, ExplorerMembership)>))result;
                _page = page; _view = view; _observations = observations; _pivots = pivots; _episodes = episodes; _bars = bars;
                _chartContext = context;
                if (profileQuality != null) { _profileQuality = profileQuality; }
                _chart.SetInterval(chartFrom, chartTo); TextBlockEpisodeState.Text = EpisodeState(run, episodes.Length);
                DataGridCatalog.ItemsSource = page.Rows.Select(c => new CatalogRow(c, _view.Passes(c, run.Spec.PriceStep), evidence[c.Id].Trigger, evidence[c.Id].Membership)).ToArray();
                DataGridEpisodes.ItemsSource = episodes; DataGridObservations.ItemsSource = observations; DataGridLabels.ItemsSource = labels;
                DataGridTriggers.ItemsSource = triggers; DataGridPivots.ItemsSource = pivots; DataGridDiagnostics.ItemsSource = diagnostics; TextBoxComparison.Text = comparison;
                if (totals != null) { TextBoxSummary.Text = string.Join(Environment.NewLine + Environment.NewLine, totals.Select(t => $"{t.Profile}: всего {t.Total:N0} / прошло {t.Passed:N0}; одиночных {t.Single:N0}; EOF {t.OpenAtEnd:N0}\np25 {t.P25}; median {t.Median}; p75 {t.P75}; p90 {t.P90}; p95 {t.P95}; p99 {t.P99}")) + "\n\nОписательная сводка всего периода. Не является причинным признаком. Разбивка по датам/часам в quality.json. Сравнение групп и все исходы — в отдельной папке исследования."; }
                if (totals != null) { TextBoxSummary.AppendText("\n\n" + string.Join("\n", totals.Select(t => $"{t.Profile}: накопленных {t.Accumulated}; доля ≥ порогу объёма {t.VolumeFraction:P2}; в часовом срезе {t.InTimeSlice}; {(t.Total == 0 ? "нет данных" : "")}"))); }
                if (totals != null)
                {
                    TextBoxSummary.AppendText("\n\n" + string.Join("\n", totals.Select(t => t.Profile + $": фон неизвестен {_profileQuality[t.Profile].Unknown:N0}; пригодных завершённых Cloud {_profileQuality[t.Profile].Eligible:N0}.")));
                    TextBoxSummary.Text = "СВОДКА ОТКРЫТОГО РЕЗУЛЬТАТА\nОписательные показатели всего периода, не признаки для поиска.\n\n" + TextBoxSummary.Text + "\n\n" + TextBlockEpisodeState.Text +
                        $"\nНаблюдений: {TableCount(run.StudyPath, "observations")}; триггеров: {TableCount(run.StudyPath, "triggers")}; экстремумов: {TableCount(run.StudyPath, "pivots")}. Подробности — на одноимённых вкладках.";
                    TextBoxSummary.ScrollToHome();
                }
                Paint(); TextBlockStatus.Text = $"Страница: {page.Rows.Count} строк, прошло {page.Passed}, фон неизвестен {page.Unknown}. Отсечённые строки сохранены." +
                    (context?.Limited == true ? " На графике показаны первые 4000 меток каждого слоя; сузьте интервал для остальных." : "");
            });
        }
        private void ApplyFilter(object sender, RoutedEventArgs e)
        {
            try
            {
                _view = ReadView(); _pageStart = 0;
                if (_playback != null)
                {
                    ExplorerStorage.SaveView(_run, _view); Paint();
                    if (_frame != null)
                    {
                        ExplorerView frameView = FrameView(_frame);
                        DataGridCatalog.ItemsSource = _frame.Clouds.Select(c => new CatalogRow(c, frameView.Passes(c, _run.Spec.PriceStep),
                            _frame.Triggers.FirstOrDefault(t => t.VolumeId == c.Id), null)).ToArray();
                    }
                }
                else { LoadPage(true); }
            }
            catch (Exception error) { Error(error); }
        }
        private void FirstPage(object sender, RoutedEventArgs e) { try { RequireIdle(); string table = SelectedTable(); if (table == null) { _pageStart = 0; _chartFrom = _chartTo = null; } else { _tableOffsets[table] = 0; } LoadPage(false); } catch (Exception error) { Error(error); } }
        private void NextPage(object sender, RoutedEventArgs e) { try { RequireIdle(); if (_page != null) { string table = SelectedTable(); if (table == null) { _pageStart = _page.NextOffset; _chartFrom = _chartTo = null; } else { _tableOffsets[table] = Offset(table) + 250; } LoadPage(false); } } catch (Exception error) { Error(error); } }
        private void Find(object sender, RoutedEventArgs e) { try { _pageStart = 0; LoadPage(false); } catch (Exception error) { Error(error); } }
        private void Cancel(object sender, RoutedEventArgs e) { try { _pendingObservation = null; _pendingPatternExample = false; _pendingInterval = null; _job?.Cancel(); StopPlayback(); } catch (Exception error) { Error(error); } }
        private void OpenArtifacts(object sender, RoutedEventArgs e) { try { if (_run != null) { Process.Start(new ProcessStartInfo(_patternRun?.Directory ?? _run.StudyPath ?? _run.CatalogPath) { UseShellExecute = true }); } } catch (Exception error) { Error(error); } }
        private void CatalogSelected(object sender, SelectionChangedEventArgs e)
        {
            if (DataGridCatalog.SelectedItem is CatalogRow selected)
            { ClearObservationContext(); _selectedAnchor = new ExplorerAnchor(selected.Id, selected.Cloud.FirstSequence, selected.Cloud.KnownSequence ?? long.MaxValue, selected.Cloud.StartTime.Date); }
            else if (e.RemovedItems.OfType<CatalogRow>().Any(r => r.Id == _selectedAnchor?.Id)) { _selectedAnchor = null; }
            try { if (DataGridCatalog.SelectedItem is CatalogRow row) { _chart.Select(row.Id); TextBoxDetails.Text = $"{row.Id}\nНачало {row.Cloud.StartTime:O}; ObservedAt {row.Cloud.Time:O}; KnownAt {row.Cloud.KnownAt:O}; ordinal {row.Cloud.LastSequence}; {row.Cloud.Reason}\nВключено {row.Cloud.Count} сделок; Buy {row.Cloud.Buy}; Sell {row.Cloud.Sell}; VWAP включённых {row.Cloud.Vwap}; пауза {row.Cloud.Effective.GapMilliseconds} мс; диапазон {row.Cloud.Effective.RangeTicks}; {row.Cloud.Effective.Status}"; LoadCloudDetails(row.Cloud); } }
            catch (Exception error) { Error(error); }
        }
        private void ChartSelected(ExplorerCloud cloud)
        {
            try
            {
                CatalogRow row = DataGridCatalog.Items.Cast<CatalogRow>().FirstOrDefault(r => r.Id == cloud.Id);
                if (row == null) { row = new CatalogRow(cloud, _view.Passes(cloud, _run.Spec.PriceStep), null, null); DataGridCatalog.ItemsSource = new[] { row }; }
                DataGridCatalog.SelectedItem = row; DataGridCatalog.ScrollIntoView(row);
                if (_patternRun != null) { TabControlResult.SelectedIndex = 0; }
            }
            catch (Exception error) { Error(error); }
        }
        private void CatalogDoubleClick(object sender, MouseButtonEventArgs e) { try { if (DataGridCatalog.SelectedItem is CatalogRow row) { ShowInterval(row.Cloud.StartTime.AddMinutes(-2), row.Cloud.Time.AddMinutes(2)); } TabControlResult.SelectedItem = TabItemChart; _window?.Activate(); } catch (Exception error) { Error(error); } }
        private void EpisodeSelected(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (DataGridEpisodes.SelectedItem is not ExplorerEpisode episode)
                { if (e.RemovedItems.OfType<ExplorerEpisode>().Any(r => r.Id == _selectedAnchor?.Id)) { _selectedAnchor = null; } return; }
                ClearObservationContext(); _selectedAnchor = new ExplorerAnchor(episode.Id, episode.FirstSequence, episode.KnownSequence, episode.StartTime.Date);
                TextBoxDetails.Text = episode.Id + "\nДети: " + string.Join("\n", episode.ChildIds) + "\nОбъём Cloud " + episode.Volume + "; весь сырой объём интервала " + episode.RawVolume;
                TextBlockStatus.Text = $"Эпизод {episode.Id} · {episode.ChildCount} Cloud · {episode.Volume}; сырой объём {episode.RawVolume}; KnownAt {episode.KnownAt:O}";
                _chart.SelectEpisode(episode);
                ShowInterval(episode.StartTime.AddMinutes(-2), episode.Time.AddMinutes(2));
            }
            catch (Exception error) { Error(error); }
        }
        private void ObservationSelected(object sender, SelectionChangedEventArgs e)
        { try { if (DataGridObservations.SelectedItem is ExplorerObservation observation) { ShowObservation(observation); } } catch (Exception error) { Error(error); } }

        #endregion

        #region Selected anchor, playback and chart lifetime

        private void Anchor(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_run == null) { return; }
                if (_playback != null) { throw new InvalidOperationException("Выберите якорь в режиме «История» перед запуском реплея."); }
                ExplorerAnchor anchor = _selectedAnchor ?? throw new InvalidOperationException("Сначала выберите строку Cloud или эпизода.");
                anchor.Validate(_run.Spec);
                bool triggerAnchor = CheckBoxTriggerAnchor.IsChecked == true; ExplorerRun run = _run;
                RequireIdle(); ClearObservationContext();
                StartJob(token =>
                {
                    ExplorerTrigger trigger = run.StudyPath == null ? null : ExplorerStorage.ReadRows<ExplorerTrigger>(run.StudyPath, "triggers").FirstOrDefault(t => t.VolumeId == anchor.Id);
                    if (triggerAnchor && trigger == null) { throw new InvalidOperationException("У выбранного события нет причинного trigger в этом исследовании."); }
                    ExplorerAnchor selected = anchor with { FirstSequence = triggerAnchor ? trigger.Sequence : anchor.FirstSequence, KnownSequence = trigger?.Sequence ?? anchor.KnownSequence };
                    return (selected, ExplorerAnchorRunner.Run(run, selected, token));
                }, result => { (_anchor, _vwap) = ((ExplorerAnchor, ImmutableArray<ExplorerVwapSample>))result; Paint(); TextBlockStatus.Text = "VWAP и σ рассчитаны по всем raw сделкам даты. В реплее линия появится только в KnownAt."; });
            }
            catch (Exception error) { Error(error); }
        }
        private void Paint()
        {
            if (_frame != null) { PaintFrame(_frame); }
            else if (_chartContext != null && _run != null)
            {
                _chart.SetInterval(_chartContext.From, _chartContext.To);
                _chart.Set(_chartContext.Clouds, _view, _run.Spec.PriceStep, _observationVwap ?? (IReadOnlyList<ExplorerVwapSample>)_vwap, _chartContext.Pivots, _chartContext.Observations, CheckBoxBands.IsChecked == true);
                _chart.SetContext(_chartContext.Bars, _chartContext.Episodes);
            }
            else if (_page != null && _run != null) { _chart.Set(_page.Rows, _view, _run.Spec.PriceStep, _observationVwap ?? (IReadOnlyList<ExplorerVwapSample>)_vwap, _pivots, _observations, CheckBoxBands.IsChecked == true); _chart.SetContext(_bars, _episodes); }
        }
        private void PaintFrame(ExplorerFrame frame)
        {
            _chart.SetInterval(null, null);
            _chart.Set(frame.Clouds, FrameView(frame), _run.Spec.PriceStep, frame.Vwap, frame.Pivots.AddRange(frame.Provisional == null ? Array.Empty<ExplorerPivot>() : new[] { frame.Provisional }), frame.Observations, CheckBoxBands.IsChecked == true);
            _chart.SetContext(ExplorerBars.Aggregate(frame.Bars, _timeFrame, CancellationToken.None), frame.Episodes);
        }
        private void Bands(object sender, RoutedEventArgs e) { try { Paint(); } catch (Exception error) { Error(error); } }
        private void Replay(object sender, RoutedEventArgs e)
        { try { if (_run == null) { return; } int speed = ExplorerValidation.Integer(TextBoxSpeed.Text, "ReplaySpeed"); ExplorerValidation.Require(speed >= 1 && speed <= 10000, "ReplaySpeed", "Скорость реплея: целое число от 1 до 10000 тиков за кадр."); BeginPlayback(); _playback.Play(speed); } catch (Exception error) { Error(error); } }
        private void Pause(object sender, RoutedEventArgs e) { try { _playback?.Pause(); } catch (Exception error) { Error(error); } }
        private void Step(object sender, RoutedEventArgs e) { try { if (_run != null) { BeginPlayback(); _playback.Step(); } } catch (Exception error) { Error(error); } }
        private void History(object sender, RoutedEventArgs e) { try { StopPlayback(); LoadPage(true); } catch (Exception error) { Error(error); } }
        private void StopPlayback() { _playback?.Dispose(); _playback = null; _frame = null; PatternReplayMode(false); }
        private void Separate(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_window != null)
                {
                    if (_window.WindowState == WindowState.Minimized) { _window.WindowState = WindowState.Normal; }
                    _window.Activate(); return;
                }
                ContentControlChart.Content = null;
                _window = new CloudExplorerChartWindow(); _window.ContentControlSurface.Content = GridChartSurface;
                _window.Closing += SeparateClosing; _window.Show();
            }
            catch (Exception error) { Error(error); }
        }
        private void SeparateClosing(object sender, CancelEventArgs e)
        { try { _window.Closing -= SeparateClosing; _window.ContentControlSurface.Content = null; _window = null; if (!_disposed) { ContentControlChart.Content = GridChartSurface; } } catch (Exception error) { Error(error); } }

        /// <summary>Cancels owned offline work and detaches UI events; workers release file handles without blocking the dispatcher.</summary>
        public void Dispose()
        {
            try
            {
                if (_disposed) { return; } _disposed = true; _timer.Stop(); _timer.Tick -= Poll; _job?.Dispose(); _job = null; _finish = null; DisposePatterns();
                StopPlayback(); _window?.Close(); _chart.Selected -= ChartSelected; ContentControlPlot.Content = null; RequestProvider = null; InputFingerprint = null; InputFocus = null;
                foreach (DataGrid grid in ResultGrids()) { grid.AutoGeneratingColumn -= TranslateColumn; }
                _chart.ObservationSelected -= ChartObservationSelected; ButtonReopen.Click -= Reopen; ComboBoxTimeFrame.SelectionChanged -= TimeFrameChanged;
                _chart.Failed -= Error;
                ButtonFitPrice.Click -= FitPrice; ButtonResetAxes.Click -= ResetAxes;
                ComboBoxPriceStyle.SelectionChanged -= PriceStyleChanged; CheckBoxDraw.Click -= DrawChanged; ButtonClearLines.Click -= ClearLines;
                ComboBoxProfile.SelectionChanged -= ProfileChanged; ButtonCalculate.Click -= Calculate; ButtonCancel.Click -= Cancel; ButtonFilter.Click -= ApplyFilter;
                ButtonFirst.Click -= FirstPage; ButtonNext.Click -= NextPage; ButtonFind.Click -= Find; ButtonArtifacts.Click -= OpenArtifacts; ButtonAnchor.Click -= Anchor;
                ButtonSeparate.Click -= Separate; ButtonReplay.Click -= Replay; ButtonPause.Click -= Pause; ButtonStep.Click -= Step; ButtonHistory.Click -= History;
                CheckBoxBands.Click -= Bands; DataGridCatalog.SelectionChanged -= CatalogSelected; DataGridCatalog.MouseDoubleClick -= CatalogDoubleClick;
                DataGridObservations.SelectionChanged -= ObservationSelected; DataGridEpisodes.SelectionChanged -= EpisodeSelected;
                foreach (DataGrid grid in new[] { DataGridCatalog, DataGridEpisodes, DataGridObservations, DataGridLabels, DataGridTriggers, DataGridPivots, DataGridDiagnostics, DataGridFormation, DataGridAdaptation, DataGridView, DataGridModules, DataGridStudy }) { grid.ItemsSource = null; }
            }
            catch (Exception error) { ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error); }
        }

        #endregion

        private sealed record CatalogRow(ExplorerCloud Cloud, bool Passed, ExplorerTrigger Trigger, ExplorerMembership Membership)
        {
            public string Id => Cloud.Id;
            public string Profile => Cloud.Profile;
            public DateTime StartTime => Cloud.StartTime;
            public long FirstOrdinal => Cloud.FirstSequence;
            public long LastOrdinal => Cloud.LastSequence;
            public DateTime ObservedAt => Cloud.Time;
            public DateTime? KnownAt => Cloud.KnownAt;
            public decimal Volume => Cloud.Volume;
            public int Count => Cloud.Count;
            public decimal Delta => Cloud.Delta;
            public decimal? Percentile => Cloud.RelativePercentile;
            public string RelativeStatus => Cloud.RelativeStatus;
            public string Reason => Cloud.Reason;
            public decimal TickThreshold => Cloud.Effective.TickVolume;
            public int GapMilliseconds => Cloud.Effective.GapMilliseconds;
            public int RangeTicks => Cloud.Effective.RangeTicks;
            public decimal? RollingThreshold => Cloud.Effective.RollingThreshold;
            public decimal? TimeOfDayThreshold => Cloud.Effective.TimeOfDayThreshold;
            public DateTime? FirstTrigger => Trigger?.Time;
            public long? TriggerOrdinal => Trigger?.Sequence;
            public string TriggerStatus => Trigger == null ? "Unavailable" : "Known";
            public string EpisodeId => Membership?.EpisodeId;
            public int? EpisodeChildren => Membership?.ChildCount;
        }
    }
}
