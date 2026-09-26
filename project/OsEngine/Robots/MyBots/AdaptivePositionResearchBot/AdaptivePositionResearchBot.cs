using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using OsEngine.Entity;
using OsEngine.Logging;
using OsEngine.Market.Connectors;
using OsEngine.Market.Servers.Tester;
using OsEngine.Market.Servers.Optimizer;
using OsEngine.OsOptimizer;
using OsEngine.OsTrader.AdaptivePositionManager;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.OsTrader.Panels.Tab;

namespace OsEngine.Robots.MyBots
{
    /// <summary>
    /// Registered ResearchOnly shell for saved TradeOnly campaigns on one dedicated native tab.
    /// Entry signals and risk locks come exclusively from the schedule; the APM core owns all order decisions.
    /// </summary>
    /// <remarks>
    /// Supports actual Tester/Optimizer tick servers only. Each replay reset creates isolated campaign state.
    /// Off pauses increases; it does not pretend to flatten. Native manual protection is disabled only on this
    /// robot's dedicated tab so it cannot compete with the APM arbiter. Contract: APM-INTEGRATION-001.
    /// </remarks>
    [Bot("AdaptivePositionResearchBot")]
    public sealed class AdaptivePositionResearchBot : BotPanel, IOptimizerResearchRun
    {
        private readonly object _lifecycle = new object();
        private readonly BotTabSimple _tab;
        private readonly ConnectorCandles _connector;
        private readonly StrategyParameterString _regime;
        private readonly StrategyParameterString _scheduleFile;
        private readonly StrategyParameterString _datasetFile;
        private readonly StrategyParameterString _outputRoot;
        private readonly StrategyParameterDecimal _addScale;
        private readonly StrategyParameterDecimal _reduceScale;
        private readonly StrategyParameterDecimal _penalty;
        private readonly StrategyParameterBool _adaptive;
        private readonly StrategyParameterBool _fast;
        private readonly StrategyParameterBool _constant;
        private readonly StrategyParameterDecimal _rearm;
        private readonly StrategyParameterButton _diagnostics;
        private readonly StrategyParameterButton _studyReport;
        private readonly StrategyParameterBool _researchExecution;
        private readonly StrategyParameterString _researchSettings;
        private readonly List<ApmSnapshot> _results = new List<ApmSnapshot>();
        private readonly List<ApmCampaignMetrics> _metrics = new List<ApmCampaignMetrics>();
        private ApmSchedule _schedule;
        private ApmPolicy _policy;
        private ApmOsEngineAdapter _adapter;
        private ApmArtifacts _artifacts;
        private ApmRunManifest _manifest;
        private ApmDiagnosticsWindow _window;
        private int _index;
        private bool _archived;
        private bool _failed;
        private bool _deleted;
        private string _runId;
        private string _runDirectory;
        private bool _finalized;
        private OptimizerServer _optimizer;
        private DateTime _phaseStart;
        private DateTime _phaseEnd;
        private string _sourceScheduleHash;
        private ApmOptimizerStudy _optimizerStudy;
        private string _studyDirectory;
        private StrategyParameterString _studyOutputParameter;
        private string _studyOriginalOutput;

        /// <summary>
        /// Native UI/headless Optimizer entry: validate and save the immutable experiment before enumeration,
        /// then redirect pass artifacts to a new study directory. No server or pass is started here.
        /// </summary>
        public void PrepareResearchRun(List<IIStrategyParameter> parameters, List<bool> selected,
            IReadOnlyList<OptimizerFaze> phases, string nativeFilters)
        {
            lock (_lifecycle)
            {
                string Text(string name) => ((StrategyParameterString)parameters.Single(p => p.Name == name)).ValueString;
                if (Text("Regime") != "On") throw new ArgumentException("APM native optimization requires explicit Regime On.");
                ApmSchedule schedule = ApmSchedule.Load(Text("Schedule file"));
                schedule.VerifyDataset(Text("Dataset file"));
                // Native UI stores its fixed column in Defolt; its preview count may already reset current to Start.
                for (int i = 0; i < parameters.Count; i++)
                    if (!selected[i] && parameters[i] is StrategyParameterDecimal fixedValue)
                        fixedValue.ValueDecimal = fixedValue.ValueDecimalDefolt;
                ApmStudyPhase[] studyPhases = phases.Select((phase, index) => new ApmStudyPhase(index + "-" + phase.TypeFaze,
                    phase.TimeStart, phase.TimeEnd, phase.TypeFaze == OptimizerFazeType.OutOfSample)).ToArray();
                ApmOptimizerStudy study = new ApmOptimizerStudy(schedule, studyPhases, parameters, selected);
                string directory = Path.GetFullPath(Path.Combine(Text("Artifacts root"), "study-" + Guid.NewGuid().ToString("N")));
                Directory.CreateDirectory(directory);
                study.SavePlan(Path.Combine(directory, "experiment.json"), "Owner-selected data; historical economic qualification NOT_VERIFIED", nativeFilters);
                _studyOutputParameter = (StrategyParameterString)parameters.Single(p => p.Name == "Artifacts root");
                _studyOriginalOutput = _studyOutputParameter.ValueString;
                _studyOutputParameter.ValueString = Path.Combine(directory, "passes");
                _optimizerStudy = study; _studyDirectory = directory;
            }
        }

        /// <summary>Flush whole-pass metrics before the native executor removes this bot from the active pass set.</summary>
        public void FinalizeResearchPass() { lock (_lifecycle) FinalizeRun(); }

        /// <summary>Complete native UI/headless experiment accounting independently of filtered native position reports.</summary>
        public void CompleteResearchRun(IReadOnlyList<OptimizerFazeReport> reports)
        {
            lock (_lifecycle)
            {
                if (_optimizerStudy == null) return;
                try
                {
                    _optimizerStudy.WriteResults(Path.Combine(_studyDirectory, "passes"), Path.Combine(_studyDirectory, "all-trials.json"));
                    using FileStream stream = new FileStream(Path.Combine(_studyDirectory, "native-selection.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    JsonSerializer.Serialize(stream, reports.Select(r => new { r.Faze.TypeFaze, r.Faze.TimeStart, r.Faze.TimeEnd,
                        SurvivingBotNames = r.Reports.Select(p => p.BotName).ToArray() }));
                    stream.Flush(true);
                }
                finally
                {
                    _studyOutputParameter.ValueString = _studyOriginalOutput;
                    _studyOutputParameter = null; _optimizerStudy = null;
                }
            }
        }

        /// <summary>Create the native tab and parameters once. No server is connected or order sent by this constructor.</summary>
        public AdaptivePositionResearchBot(string name, StartProgram startProgram) : base(name, startProgram)
        {
            if (startProgram != StartProgram.IsTester && startProgram != StartProgram.IsOsOptimizer)
                throw new NotSupportedException("AdaptivePositionResearchBot supports actual Tester/Optimizer only.");
            TabCreate(BotTabType.Simple);
            _tab = TabsSimple[0];
            _connector = _tab.Connector;
            _tab.ManualPositionSupport.DisableManualSupport();
            _regime = CreateParameter("Regime", "Off", new[] { "Off", "On" }, "Run");
            _scheduleFile = CreateParameter("Schedule file", "", "Run");
            _datasetFile = CreateParameter("Dataset file", "", "Run");
            _outputRoot = CreateParameter("Artifacts root", "Engine/APM", "Run");
            _constant = CreateParameter("B0 constant inventory", false, "Behavior");
            _adaptive = CreateParameter("Volatility scales", false, "Behavior");
            _addScale = CreateParameter("Add scale price", 5m, 1m, 9m, 2m, "Behavior");
            _reduceScale = CreateParameter("Reduce scale price", 10m, 6m, 14m, 2m, "Behavior");
            _penalty = CreateParameter("Inventory gamma", 0m, 0m, 1m, 0.25m, "Behavior");
            _fast = CreateParameter("FAST enabled", false, "Behavior");
            _rearm = CreateParameter("Rearm volatility factor", 0m, 0m, 1m, 0.25m, "Behavior");
            _researchExecution = CreateParameter("Research AC enabled", false, "ResearchOnly");
            _researchSettings = CreateParameter("Research AC settings file", "", "ResearchOnly");
            _diagnostics = CreateParameterButton("APM diagnostics", "Run");
            _diagnostics.UserClickOnButtonEvent += ShowIndividualSettingsDialog;
            _studyReport = CreateParameterButton("APM study report", "Run");
            _studyReport.UserClickOnButtonEvent += ShowStudyReport;
            _regime.ValueChange += Regime_Changed;
            _tab.NewTickEvent += Tab_NewTickEvent;
            _connector.TestStartEvent += Connector_TestStartEvent;
            _connector.TestOverEvent += Connector_TestOverEvent;
            ParametrsChangeByUser += Parameters_Changed;
            DeleteEvent += Robot_DeleteEvent;
            Description = "ResearchOnly: saved TradeOnly schedule, reversible position sizing and immutable risk locks. "
                + "Set schedule/dataset files, native tick data and Regime On. Off pauses increases; use APM Close for actual exit.";
        }

        /// <summary>Actual current controller for diagnostics and research evidence; access does not adopt native positions.</summary>
        public ApmExecutionController ActiveController { get { lock (_lifecycle) return _adapter?.Controller; } }
        /// <summary>Detached completed campaign results from this replay only.</summary>
        public ApmSnapshot[] CampaignResults { get { lock (_lifecycle) return _results.ToArray(); } }
        /// <summary>Stable directory of the most recently created campaign's artifacts, or null before preflight.</summary>
        public string ArtifactDirectory { get; private set; }

        /// <summary>Native BotFactory identity.</summary>
        public override string GetNameStrategyType() => "AdaptivePositionResearchBot";

        /// <summary>Open or activate independent diagnostics; before the first replay tick, show native parameter settings.</summary>
        public override void ShowIndividualSettingsDialog()
        {
            try
            {
                if (StartProgram == StartProgram.IsOsOptimizer || Application.Current == null) return;
                if (!Application.Current.Dispatcher.CheckAccess())
                { Application.Current.Dispatcher.BeginInvoke(new Action(ShowIndividualSettingsDialog)); return; }
                lock (_lifecycle)
                {
                    if (_deleted) return;
                    if (_adapter == null) { ShowParameterDialog(); return; }
                    if (_window != null)
                    {
                        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
                        _window.Activate(); return;
                    }
                    _window = new ApmDiagnosticsWindow(_adapter.Controller, ShowParameterDialog);
                    _window.Closed += Diagnostics_Closed;
                    _window.Show();
                }
            }
            catch (Exception error) { SendNewLogMessage(error.ToString(), LogMessageType.Error); }
        }

        private void Tab_NewTickEvent(Trade tick)
        {
            try
            {
                lock (_lifecycle)
                {
                    if (_deleted || _failed) return;
                    if (StartProgram != StartProgram.IsTester && StartProgram != StartProgram.IsOsOptimizer)
                        throw new NotSupportedException("APM ResearchOnly rejects live startup.");
                    if (_adapter != null)
                    {
                        if (_adapter.Controller.Snapshot.State != ApmState.Completed) return;
                        ArchiveCompleted();
                    }
                    if (_regime.ValueString != "On") return;
                    if (_schedule == null) LoadRun();
                    if (_index >= _schedule.Campaigns.Count) return;
                    ApmCampaignSpec spec = _schedule.Campaigns[_index];
                    if (tick.Time >= spec.SessionExitTime) throw new InvalidOperationException("Saved campaign interval was missed; replay is Partial.");
                    if (tick.SecurityNameCode != spec.Instrument) throw new InvalidOperationException("Selected native security differs from saved schedule.");
                    ReleaseCampaign();
                    ArtifactDirectory = Path.Combine(_runDirectory, _index.ToString("D4"));
                    _artifacts = new ApmArtifacts(ArtifactDirectory);
                    Assembly assembly = typeof(AdaptivePositionResearchBot).Assembly;
                    string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
                    int separator = version.IndexOf('+');
                    string revision = separator < 0 ? "UNVERIFIED" : version.Substring(separator + 1);
                    _manifest = new ApmRunManifest("APM-Schema-v1", _policy.ModelVersion, revision,
                        _runId, _schedule.DatasetHash, _schedule.Hash, spec.Timezone,
                        StartProgram == StartProgram.IsTester ? "NativeTester-full-volume-v1" : "NativeOptimizer-full-volume-v1",
                        "FixedFeePerContract:" + spec.FeePerContract.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        "NativeActualFills; reserved entry/stop slippage from schedule", "Stable source order; whole-second event time",
                        ApmDataProfile.TradeOnly, _policy, new[] { spec }, 67123, "Running", BuildHash: ApmTickReader.HashFile(assembly.Location),
                        NativeLimits: new ApmOperationalLimits());
                    _artifacts.WriteManifest(_manifest);
                    _adapter = new ApmOsEngineAdapter(_tab, StartProgram, spec, _policy, _artifacts);
                    _adapter.Controller.SetEnabled(_regime.ValueString == "On");
                    _adapter.LogMessageEvent += Adapter_LogMessageEvent;
                    _archived = false;
                }
            }
            catch (Exception error)
            {
                lock (_lifecycle) _failed = true;
                SendNewLogMessage(error.ToString(), LogMessageType.Error);
            }
        }

        private void LoadRun()
        {
            _schedule = ApmSchedule.Load(_scheduleFile.ValueString);
            _schedule.VerifyDataset(_datasetFile.ValueString);
            List<SecurityTester> sources = _connector.MyServer is TesterServer tester ? tester.SecuritiesTester
                : _connector.MyServer is OptimizerServer optimizer ? optimizer.SecuritiesTester : null;
            SecurityTester[] matching = sources?.Where(s => s.Security.Name == _schedule.Campaigns[0].Instrument
                && s.DataType == SecurityTesterDataType.Tick).ToArray();
            if (matching == null || matching.Length != 1 || string.IsNullOrWhiteSpace(matching[0].FileAddress)
                || !string.Equals(Path.GetFullPath(matching[0].FileAddress), Path.GetFullPath(_datasetFile.ValueString), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Native tick source must be the exact file declared by the saved schedule.");
            ApmDatasetReport report = ApmDatasetValidation.Inspect(_datasetFile.ValueString, "NativeResearch", _schedule.Campaigns[0].Instrument, false);
            if (report.Status != "ValidatedTradeOnly") throw new InvalidDataException("Selected native research input failed source-order validation.");
            _policy = new ApmPolicy { ConstantInventory = _constant.ValueBool, FixedScales = !_adaptive.ValueBool,
                AddScale = _addScale.ValueDecimal, ReduceScale = _reduceScale.ValueDecimal,
                InventoryPenalty = _penalty.ValueDecimal, FastEnabled = _fast.ValueBool,
                RearmVolatilityFactor = _rearm.ValueDecimal,
                ResearchExecution = _researchExecution.ValueBool ? ApmAcSettings.Load(_researchSettings.ValueString) : null };
            _sourceScheduleHash = _schedule.Hash;
            if (_connector.MyServer is OptimizerServer phaseServer)
            {
                if (!phaseServer.TryGetReplayInterval(_schedule.Campaigns[0].Instrument, out _phaseStart, out _phaseEnd))
                    throw new InvalidOperationException("Native Optimizer phase bounds are unavailable or inconsistent.");
                _schedule = _schedule.SelectPhase(_phaseStart, _phaseEnd, _policy.WarmupSeconds);
            }
            foreach (ApmCampaignSpec spec in _schedule.Campaigns) _policy.Validate(spec);
            _runId = "run-" + Guid.NewGuid().ToString("N");
            _runDirectory = Path.GetFullPath(Path.Combine(_outputRoot.ValueString, _runId));
            _optimizer = _connector.MyServer as OptimizerServer;
            if (_optimizer != null) _optimizer.TestingEndEvent += Optimizer_TestingEndEvent;
            WriteRunSummary("Running");
        }

        private void ArchiveCompleted()
        {
            if (_archived) return;
            _artifacts.WriteManifest(_manifest with { CompletionStatus = "CompletedResearchOnly" });
            _results.Add(_adapter.Controller.Snapshot);
            _metrics.Add(_artifacts.Metrics);
            _archived = true; _index++;
        }

        private void Connector_TestStartEvent()
        {
            try
            {
                lock (_lifecycle)
                {
                    try { FinalizeRun(); }
                    finally
                    {
                        ReleaseCampaign();
                        if (_optimizer != null) _optimizer.TestingEndEvent -= Optimizer_TestingEndEvent;
                        _optimizer = null;
                    }
                    _schedule = null; _policy = null; _index = 0; _archived = false; _failed = false;
                    _finalized = false; _runId = null; _runDirectory = null;
                    _results.Clear(); ArtifactDirectory = null;
                    _metrics.Clear();
                }
            }
            catch (Exception error) { _failed = true; SendNewLogMessage(error.ToString(), LogMessageType.Error); }
        }

        private void Connector_TestOverEvent()
        {
            try
            {
                lock (_lifecycle)
                {
                    FinalizeRun();
                }
            }
            catch (Exception error) { SendNewLogMessage(error.ToString(), LogMessageType.Error); }
        }

        private void Optimizer_TestingEndEvent(int serverNumber, TimeSpan elapsed) => Connector_TestOverEvent();

        private void FinalizeRun()
        {
            if (_finalized || _runDirectory == null) return;
            if (_adapter?.Controller.Snapshot.State == ApmState.Completed) ArchiveCompleted();
            else if (_artifacts != null) _artifacts.WriteManifest(_manifest with { CompletionStatus = "Partial" });
            WriteRunSummary(!_failed && _index == _schedule.Campaigns.Count ? "CompletedResearchOnly" : "Partial");
            _finalized = true;
        }

        private void WriteRunSummary(string status)
        {
            Directory.CreateDirectory(_runDirectory);
            string target = Path.Combine(_runDirectory, "run-summary.json");
            string temporary = target + ".tmp";
            using (FileStream stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new { SchemaVersion = "APM-RunSummary-v1", RunId = _runId,
                    CompletionStatus = status, _schedule.DatasetHash, ScheduleHash = _schedule.Hash,
                    SourceScheduleHash = _sourceScheduleHash, PhaseStart = _phaseStart, PhaseEnd = _phaseEnd,
                    BotName = NameStrategyUniq, Parameters = _policy,
                    Planned = _schedule.Campaigns.Count, Completed = _results.Count,
                    Unstarted = _schedule.Campaigns.Count - _index - (_adapter != null && !_archived ? 1 : 0),
                    Campaigns = _results.ToArray(), Metrics = _metrics.ToArray(), Current = _adapter?.Controller.Snapshot,
                    CurrentMetrics = _adapter != null && !_archived ? _artifacts.Metrics : null });
                stream.Flush(true);
            }
            File.Move(temporary, target, true);
        }

        private void Regime_Changed()
        {
            try
            {
                lock (_lifecycle) _adapter?.Controller.SetEnabled(_regime.ValueString == "On");
            }
            catch (Exception error) { SendNewLogMessage(error.ToString(), LogMessageType.Error); }
        }

        private void Parameters_Changed()
        {
            lock (_lifecycle)
                if (_adapter != null) SendNewLogMessage("APM behavior/schedule changes apply on the next replay; current locks remain frozen.", LogMessageType.System);
        }

        private void Adapter_LogMessageEvent(string message, LogMessageType type) => SendNewLogMessage(message, type);

        private void ShowStudyReport()
        {
            try
            {
                if (Application.Current == null) return;
                if (!Application.Current.Dispatcher.CheckAccess())
                { Application.Current.Dispatcher.BeginInvoke(new Action(ShowStudyReport)); return; }
                Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog
                    { Filter = "APM all-trials (*.json)|*.json", Title = "Сохранённый отчёт эксперимента APM" };
                if (dialog.ShowDialog() == true) new ApmStudyReportWindow(dialog.FileName).Show();
            }
            catch (Exception error) { SendNewLogMessage(error.ToString(), LogMessageType.Error); }
        }

        private void Diagnostics_Closed(object sender, EventArgs args)
        {
            lock (_lifecycle)
            {
                ApmDiagnosticsWindow closed = (ApmDiagnosticsWindow)sender;
                closed.Closed -= Diagnostics_Closed;
                if (ReferenceEquals(closed, _window)) _window = null;
            }
        }

        private void ReleaseCampaign()
        {
            ApmDiagnosticsWindow window = _window;
            _window = null;
            if (window != null) window.Dispatcher.BeginInvoke(new Action(window.Close));
            if (_adapter != null)
            {
                _adapter.LogMessageEvent -= Adapter_LogMessageEvent;
                _adapter.Dispose(); _adapter = null; _artifacts = null;
            }
            else { _artifacts?.Dispose(); _artifacts = null; }
        }

        private void Robot_DeleteEvent()
        {
            lock (_lifecycle)
            {
                try { FinalizeRun(); }
                finally
                {
                    _deleted = true;
                    _tab.NewTickEvent -= Tab_NewTickEvent;
                    _connector.TestStartEvent -= Connector_TestStartEvent;
                    _connector.TestOverEvent -= Connector_TestOverEvent;
                    _diagnostics.UserClickOnButtonEvent -= ShowIndividualSettingsDialog;
                    _studyReport.UserClickOnButtonEvent -= ShowStudyReport;
                    _regime.ValueChange -= Regime_Changed;
                    if (_optimizer != null) _optimizer.TestingEndEvent -= Optimizer_TestingEndEvent;
                    _optimizer = null;
                    ParametrsChangeByUser -= Parameters_Changed;
                    DeleteEvent -= Robot_DeleteEvent;
                    ReleaseCampaign();
                }
            }
        }
    }
}
