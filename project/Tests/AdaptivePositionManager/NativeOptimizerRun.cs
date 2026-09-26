using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using OsEngine.Entity;
using OsEngine.Logging;
using OsEngine.Market;
using OsEngine.Market.Servers;
using OsEngine.Market.Servers.Tester;
using OsEngine.OsOptimizer;
using OsEngine.OsTrader.AdaptivePositionManager;
using OsEngine.OsTrader.Panels.Tab;

namespace OsEngine.AdaptivePositionManager.Tests
{
    /// <summary>Explicit, isolated synthetic qualification through the actual native OptimizerMaster/Executor.</summary>
    /// <remarks>
    /// Invoke --native-optimizer NEW_OUTPUT_DIRECTORY THREADS VARIANT only for an authorized synthetic
    /// scenario. Creates fresh settings/data in a new temporary working directory; starts actual native
    /// workers, observes terminal TestReady and enforces a 180-second stage timeout. The dedicated process
    /// exits after evidence because native factory workers have no public shutdown join. Does not launch
    /// MainWindow/MCP or live connectors. Whole-second synthetic IS/OOS proves selected engineering paths,
    /// not a historical untouched holdout, economic GO, order-book liquidity or full Tester fill-timing parity.
    /// Contracts: APM-RESEARCH-001, APM-OPTIMIZER-EVIDENCE-001.
    /// </remarks>
    internal sealed class NativeOptimizerRun
    {
        private OptimizerMaster _master;
        private readonly List<string> _errors = new List<string>();
        private volatile bool _ended;
        private volatile bool _loaded;
        private List<OptimizerFazeReport> _reports;
        private int _terminalCount;
        private bool _terminalEvidenceReady;
        private string _expectedRoot;
        private StrategyParameterString _outputParameter;
        private int _stopAfterPasses;
        private readonly HashSet<int> _finishedServers = new HashSet<int>();

        internal static int Run(string output, int threads, string variant)
        {
            int result = new NativeOptimizerRun().Execute(Path.GetFullPath(output), threads, variant);
            Console.Out.Flush(); Console.Error.Flush();
            Environment.Exit(result); // Native factory foreground workers have no public shutdown join.
            return result;
        }

        private int Execute(string output, int threads, string variant)
        {
            if (threads < 1 || threads > 16 || !new[] { "B0", "B1", "A1", "A2", "A3", "A5", "fixed", "grid", "filtered", "cost1", "cost1.5", "cost2", "report-ui", "wfo", "stop-is", "stop-oos" }.Contains(variant))
                throw new ArgumentException("Unsupported explicit native qualification variant or thread count.");
            if (variant.StartsWith("stop-", StringComparison.Ordinal) && threads != 1)
                throw new ArgumentException("Deterministic stop qualification requires one native worker.");
            if (Directory.Exists(output)) throw new ArgumentException("Evidence directory must be new.");
            Directory.CreateDirectory(output);
            Directory.CreateDirectory(Path.Combine(output, "workspace"));
            Environment.CurrentDirectory = Path.Combine(output, "workspace");
            Directory.CreateDirectory("Engine"); Directory.CreateDirectory("Data"); Directory.CreateDirectory("Log");
            Application application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            application.Resources.MergedDictionaries.Add(new ResourceDictionary
                { Source = new Uri("/OsEngine;component/Themes/ThemeDarkOrange.xaml", UriKind.Relative) });
            OsEngine.MainWindow.ProccesIsWorked = true;
            try
            {
                string dataset = Prepare(output, variant);
                ApmSchedule schedule = ApmSchedule.Load(Path.Combine(output, "schedule.json"));
                _master = new OptimizerMaster();
                _master.LogMessageEvent += Log;
                _master.Storage.TimeChangeEvent += StorageLoaded;
                _master.Storage.PathToFolder = Path.GetDirectoryName(dataset);
                _master.Storage.TypeTesterData = TesterDataType.TickOnlyReadyCandle;
                _master.Storage.SourceDataType = TesterSourceDataType.Folder;
                _master.Storage.ReloadSecurities(false);
                Wait(Loaded, 45000, "optimizer storage");
                Security security = _master.Storage.Securities.Single(s => s.Name == "APM_SYNTH.txt");
                security.PriceStep = 1; security.PriceStepCost = 1; security.Lot = 1;
                security.VolumeStep = 1; security.DecimalsVolume = 0; security.MinTradeAmount = 1;
                _master.Storage.SaveSecurityDopSettings(security);
                _loaded = false;
                _master.Storage.ReloadSecurities(false);
                Wait(Loaded, 45000, "optimizer metadata reload");
                security = _master.Storage.Securities.Single(s => s.Name == "APM_SYNTH.txt");
                Program.Equal(1m, security.VolumeStep, "explicit volume step persists through native reload");
                _master.StrategyName = "AdaptivePositionResearchBot";
                _master.IsScript = false;
                _master.CreateBot();
                _master.ManualControl.DisableManualSupport();
                BotTabSimple tab = _master.BotToTest.TabsSimple[0];
                tab.Connector.ServerType = ServerType.Optimizer;
                tab.Connector.TimeFrame = TimeFrame.Sec1;
                tab.Connector.SecurityClass = "TestClass";
                tab.Connector.SecurityName = security.Name;
                tab.Connector.PortfolioName = "GodMode";
                List<IIStrategyParameter> parameters = _master.Parameters;
                List<bool> flags = _master.ParametersOn;
                for (int i = 0; i < flags.Count; i++) flags[i] = false;
                Set(parameters, "Regime", "On");
                Set(parameters, "Schedule file", Path.Combine(output, "schedule.json"));
                Set(parameters, "Dataset file", dataset);
                Set(parameters, "Artifacts root", Path.Combine(output, "artifacts"));
                _expectedRoot = Path.Combine(output, "artifacts");
                _outputParameter = (StrategyParameterString)parameters.Single(p => p.Name == "Artifacts root");
                if (variant == "A5")
                {
                    string settings = Path.Combine(output, "research-ac.json");
                    File.WriteAllText(settings, JsonSerializer.Serialize(new ApmAcSettings(3, 6, 0, 1, 1, 0,
                        "SYNTHETIC-UNFITTED-AC-v1", new DateTime(2026, 9, 23))));
                    ((StrategyParameterBool)parameters.Single(p => p.Name == "Research AC enabled")).ValueBool = true;
                    Set(parameters, "Research AC settings file", settings);
                }
                bool baseline = variant == "B0";
                ((StrategyParameterBool)parameters.Single(p => p.Name == "B0 constant inventory")).ValueBool = baseline;
                ((StrategyParameterBool)parameters.Single(p => p.Name == "Volatility scales")).ValueBool = variant == "A1" || variant == "A2" || variant == "A3";
                ((StrategyParameterBool)parameters.Single(p => p.Name == "FAST enabled")).ValueBool = variant == "A2" || variant == "A3";
                if (variant == "A3") ((StrategyParameterDecimal)parameters.Single(p => p.Name == "Inventory gamma")).ValueDecimal = 0.5m;
                if (variant == "fixed") ((StrategyParameterDecimal)parameters.Single(p => p.Name == "Reduce scale price")).ValueDecimal = 8;
                if (variant == "grid" || variant == "filtered" || variant.StartsWith("stop-", StringComparison.Ordinal))
                {
                    int index = parameters.FindIndex(p => p.Name == "Add scale price");
                    parameters[index] = new StrategyParameterDecimal("Add scale price", 5, 3, 7, 2);
                    flags[index] = true;
                }
                DateTime first = new DateTime(2026, 9, 24);
                ApmStudyPhase[] phases = {
                    new ApmStudyPhase("SyntheticIS", first.AddSeconds(1), first.AddSeconds(150), false),
                    new ApmStudyPhase("SyntheticOOS", first.AddDays(1).AddSeconds(1), first.AddDays(1).AddSeconds(150), true) };
                if (variant == "wfo") phases = phases.Concat(new[] {
                    new ApmStudyPhase("RollingIS2", first.AddSeconds(1), first.AddDays(2).AddSeconds(150), false),
                    new ApmStudyPhase("FutureOOS2", first.AddDays(3).AddSeconds(1), first.AddDays(3).AddSeconds(150), true) }).ToArray();
                ApmOptimizerStudy study = new ApmOptimizerStudy(schedule, phases, parameters, flags);
                study.SavePlan(Path.Combine(output, "experiment.json"), "Synthetic whole-second ticks; no historical GO",
                    variant == "filtered" ? "DealsCount >= 10000; deliberately rejects all IS candidates" : "none");
                _master.Fazes = phases.Select(p => new OptimizerFaze { TypeFaze = p.OutOfSample ? OptimizerFazeType.OutOfSample : OptimizerFazeType.InSample,
                    TimeStart = p.Start, TimeEnd = p.End }).ToList();
                _master.ThreadsCount = threads; _master.StartDeposit = 1000000;
                _master.SlippageToSimpleOrder = variant == "cost1" ? 2 : variant == "cost1.5" ? 3 : variant == "cost2" ? 4 : 0;
                _master.FilterProfitIsOn = false; _master.FilterMaxDrawDownIsOn = false;
                _master.FilterMiddleProfitIsOn = false; _master.FilterProfitFactorIsOn = false; _master.FilterDealsCountIsOn = false;
                if (variant == "filtered") { _master.FilterDealsCountIsOn = true; _master.FilterDealsCountValue = 10000; }
                _master.TestReadyEvent += Finished;
                _stopAfterPasses = variant == "stop-is" ? 1 : variant == "stop-oos" ? 4 : 0;
                _master._optimizerExecutor.TestingProgressChangeEvent += StopAfterPass;
                if (variant == "fixed") Program.Equal(1, _master._optimizerExecutor.BotCountOneFaze(parameters, flags),
                    "native UI preview count resets current without losing declared fixed column");
                Program.Check(_master._optimizerExecutor.Start(flags, parameters), "native optimizer start");
                Wait(Ended, 180000, "native optimizer terminal event");
                UiSmoke.Pump(500);
                Program.Equal(1, _terminalCount, "one public terminal event per research study including explicit stop");
                Program.Check(_terminalEvidenceReady, "first public terminal event observes finalized all-trials and restored root");
                Program.Equal(Path.Combine(output, "artifacts"), ((StrategyParameterString)parameters.Single(p => p.Name == "Artifacts root")).ValueString,
                    "native study lifecycle restores selected output root");
                string[] nativeStudies = Directory.GetFiles(Path.Combine(output, "artifacts"), "all-trials.json", SearchOption.AllDirectories);
                Program.Equal(1, nativeStudies.Length, "same production hook used by native UI automatically retains all trials");
                ApmStudyReportRow[] nativeRows = ApmStudyReportWindow.Read(nativeStudies[0]);
                Program.Equal(study.Trials.Count, nativeRows.Length, "native UI hook records full planned grid before filter");
                if (variant == "report-ui")
                {
                    ApmStudyReportWindow window = new ApmStudyReportWindow(nativeStudies[0]);
                    window.Show(); UiSmoke.Pump(500);
                    Program.Check(window.IsVisible, "actual ownerless experiment report window");
                    UiSmoke.Capture(window, Path.Combine(output, "study-report.png"));
                    window.Close(); UiSmoke.Pump(100);
                }
                study.WriteResults(Path.Combine(output, "artifacts"), Path.Combine(output, "all-trials.json"));
                string[] summaries = Directory.GetFiles(Path.Combine(output, "artifacts"), "run-summary.json", SearchOption.AllDirectories);
                int expected = study.Trials.Count;
                if (variant == "filtered") expected /= 2;
                if (_stopAfterPasses > 0) expected = _stopAfterPasses;
                Program.Equal(expected, summaries.Length, "native pass output isolation/count");
                List<object> passes = new List<object>();
                foreach (string summaryPath in summaries)
                {
                    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(summaryPath));
                    JsonElement summary = document.RootElement;
                    Program.Equal("CompletedResearchOnly", summary.GetProperty("CompletionStatus").GetString(), "native optimizer completed entire phase");
                    Program.Equal(schedule.SelectPhase(summary.GetProperty("PhaseStart").GetDateTime(),
                        summary.GetProperty("PhaseEnd").GetDateTime(), 30).Campaigns.Count,
                        summary.GetProperty("Completed").GetInt32(), "all whole campaigns in declared phase complete");
                    if (variant == "fixed") Program.Equal(8m, summary.GetProperty("Parameters").GetProperty("ReduceScale").GetDecimal(), "fixed non-default retained");
                    string[] journals = Directory.GetFiles(Path.GetDirectoryName(summaryPath), "events.jsonl", SearchOption.AllDirectories)
                        .OrderBy(path => path, StringComparer.Ordinal).ToArray();
                    ApmAuditRow[] rows = journals.SelectMany(File.ReadLines).Select(line => JsonSerializer.Deserialize<ApmAuditRow>(line)).ToArray();
                    ApmAuditRow[] fills = rows.Where(r => r.Kind == "Fill").ToArray();
                    Program.Check(fills.Length >= 2, "native optimizer actual entry and exit fills");
                    Program.Equal(0m, fills[fills.Length - 1].Snapshot.FilledVolume, "native optimizer ends flat");
                    string quantities = string.Join(",", fills.Select(f => f.Snapshot.FilledVolume.ToString(CultureInfo.InvariantCulture)));
                    if (variant == "B1") Program.Equal(summary.GetProperty("PhaseStart").GetDateTime().Day == 24
                        ? "10,8,6,8,10,6,8,0" : "10,14,18,14,18,14,0", quantities, "native optimizer matches independent S01/S02 oracle");
                    passes.Add(new { Summary = summary.Clone(), Quantities = quantities, Fills = fills.Select(f => new { f.Time, f.Price, f.Volume, f.Side }),
                        DecisionHash = ApmTickReader.HashJson(rows.Select(r => new { r.Kind, r.Time, r.Action, r.Price, r.Volume, r.Filled, r.Raw, r.Allowed, r.Reasons })) });
                }
                lock (_errors) Program.Equal(0, _errors.Count, "optimizer no logged errors");
                File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { Status = "PASS", Threads = threads,
                    Variant = variant, Planned = study.Trials.Count, Completed = summaries.Length, Passes = passes,
                    TerminalEvents = _terminalCount, TerminalEvidenceReady = _terminalEvidenceReady,
                    NativeReports = _reports.Select(r => new { r.Faze.TypeFaze, Count = r.Reports.Count }),
                    Limitation = "Native synthetic engineering evidence; fill timing differs from Tester; no economic GO" }, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine("NATIVE_OPTIMIZER_PASS " + output);
                return 0;
            }
            catch (Exception error)
            {
                File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString());
                Console.Error.WriteLine(error); return 1;
            }
            finally
            {
                if (_master != null) { _master.TestReadyEvent -= Finished; _master.LogMessageEvent -= Log;
                    _master._optimizerExecutor.TestingProgressChangeEvent -= StopAfterPass;
                    _master.Storage.TimeChangeEvent -= StorageLoaded; _master.Storage.ClearDelete(); }
                lock (_errors) File.WriteAllLines(Path.Combine(output, "errors.txt"), _errors);
                OsEngine.MainWindow.ProccesIsWorked = false;
                application.Shutdown();
            }
        }

        private static string Prepare(string output, string variant)
        {
            Directory.CreateDirectory(Path.Combine(output, "ticks"));
            string dataset = Path.Combine(output, "ticks", "APM_SYNTH.txt");
            List<ApmCampaignSpec> campaigns = new List<ApmCampaignSpec>();
            using (StreamWriter writer = new StreamWriter(dataset))
                for (int day = 0; day < (variant == "wfo" ? 4 : 2); day++)
                {
                    DateTime date = new DateTime(2026, 9, 24).AddDays(day);
                    decimal[] prices = day % 2 == 0 ? new decimal[] { 100, 102, 104, 102, 100, 104, 102 } : new decimal[] { 100, 98, 96, 98, 96, 98 };
                    for (int second = 1; second <= 150; second++)
                    {
                        decimal price = prices[Math.Clamp((second - 35) / 10, 0, prices.Length - 1)];
                        writer.WriteLine(date.AddSeconds(second).ToString("yyyyMMdd,HHmmss", CultureInfo.InvariantCulture)
                            + "," + price.ToString(CultureInfo.InvariantCulture) + ",100,Buy,0," + (day * 150 + second));
                    }
                    campaigns.Add(new ApmCampaignSpec("S0" + (day + 1), "signal" + day, "APM_SYNTH.txt", "GodMode", ApmDirection.Long,
                        date.AddSeconds(35), date.AddSeconds(120), 100, 10, 20, 90, 110, 1000, 1, 1, 1, "SYN", "SYN", "UTC",
                        StopSlippageReserveTicks: variant == "cost1" ? 2 : variant == "cost1.5" ? 3 : variant == "cost2" ? 4 : 0,
                        FeePerContract: variant == "cost1" ? 0.1m : variant == "cost1.5" ? 0.15m : variant == "cost2" ? 0.2m : 0,
                        EntrySlippageReserveTicks: variant == "cost1" ? 2 : variant == "cost1.5" ? 3 : variant == "cost2" ? 4 : 0));
                }
            new ApmSchedule(new ApmScheduleDocument("APM-Schedule-v1", "SyntheticNativeOptimizer", ApmTickReader.HashFile(dataset),
                ApmDataProfile.TradeOnly, campaigns.ToArray())).Save(Path.Combine(output, "schedule.json"));
            return dataset;
        }

        private static void Set(List<IIStrategyParameter> parameters, string name, string value) =>
            ((StrategyParameterString)parameters.Single(p => p.Name == name)).ValueString = value;
        private void StorageLoaded(DateTime start, DateTime end) { _loaded = true; }
        private bool Loaded() => _loaded && _master.Storage.Securities?.Any(s => s.Name == "APM_SYNTH.txt") == true;
        private bool Ended() => _ended && !_master._optimizerExecutor.IsRunning;
        private void Finished(List<OptimizerFazeReport> reports)
        {
            _terminalCount++;
            _terminalEvidenceReady = _terminalCount == 1
                && _outputParameter.ValueString == _expectedRoot
                && Directory.GetFiles(_expectedRoot, "all-trials.json", SearchOption.AllDirectories).Length == 1;
            _reports = reports; _ended = true;
        }
        private void StopAfterPass(int current, int maximum, int server)
        {
            if (_stopAfterPasses == 0 || current != maximum) return;
            lock (_finishedServers)
                if (_finishedServers.Add(server) && _finishedServers.Count == _stopAfterPasses) _master._optimizerExecutor.Stop();
        }
        private void Log(string message, LogMessageType type) { if (type == LogMessageType.Error) lock (_errors) _errors.Add(message); }
        private static void Wait(Func<bool> condition, int milliseconds, string stage)
        {
            DateTime limit = DateTime.UtcNow.AddMilliseconds(milliseconds);
            while (!condition()) { if (DateTime.UtcNow >= limit) throw new TimeoutException(stage); UiSmoke.Pump(50); }
            Console.WriteLine("OPTIMIZER_STAGE " + stage);
        }
    }
}
