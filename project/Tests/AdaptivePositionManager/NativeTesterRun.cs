using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using OsEngine.Entity;
using OsEngine.Logging;
using OsEngine.Market;
using OsEngine.Market.Servers;
using OsEngine.Market.Servers.Tester;
using OsEngine.OsTrader.AdaptivePositionManager;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Tab;
using OsEngine.OsTrader;
using OsEngine.OsTrader.Gui;
using OsEngine.Robots;
using OsEngine.Robots.MyBots;

namespace OsEngine.AdaptivePositionManager.Tests
{
    /// <summary>
    /// Explicit synthetic native Tester qualification using the registered robot and native replay APIs.
    /// Settings are created in a new temporary directory. Full-ui instantiates the real OsEngine.App for its
    /// resources and suppresses WPF StartupUri in the host; MainWindow, MCP and live servers are not started.
    /// Run each instance in a separate process: TesterServer has no public worker shutdown implementation.
    /// </summary>
    internal sealed class NativeTesterRun
    {
        private readonly List<string> _errors = new List<string>();
        private TesterServer _tester;
        private AdaptivePositionResearchBot _robot;
        private volatile bool _ended;
        private volatile bool _started;
        private string _output;
        private string _variant;
        private int _clockEvents;
        private int _tickEvents;
        private int _uiRequested;
        private bool _uiOpened;
        private decimal _fee;
        private int _slippage;
        private BotPanel _legacy;
        private string _legacySettingsHash;

        internal static int Run(string scenario, string output, string variant)
        {
            NativeTesterRun run = new NativeTesterRun { _output = Path.GetFullPath(output), _variant = variant };
            int result = run.Execute(scenario);
            Console.Out.Flush(); Console.Error.Flush();
            // The native Tester worker loops forever; finish only this dedicated qualification process.
            Environment.Exit(result);
            return result;
        }

        private int Execute(string scenario)
        {
            if (Directory.Exists(_output)) throw new InvalidOperationException("Native evidence directory must be new.");
            Directory.CreateDirectory(_output);
            string sandbox = Path.Combine(_output, "workspace");
            Directory.CreateDirectory(sandbox);
            Environment.CurrentDirectory = sandbox;
            Directory.CreateDirectory("Engine"); Directory.CreateDirectory("Data"); Directory.CreateDirectory("Log");
            Application application;
            if (_variant == "full-ui")
            {
                OsEngine.App nativeApplication = new OsEngine.App();
                nativeApplication.InitializeComponent();
                // WPF's public StartupUri setter rejects null. This test-host-only suppression prevents
                // production MainWindow/MCP startup while preserving real App resources and TesterUi.
                typeof(Application).GetField("_startupUri", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(nativeApplication, null);
                application = nativeApplication;
            }
            else
            {
                application = new Application();
                application.Resources.MergedDictionaries.Add(new ResourceDictionary
                    { Source = new Uri("/OsEngine;component/Themes/ThemeDarkOrange.xaml", UriKind.Relative) });
            }
            application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            OsEngine.MainWindow.ProccesIsWorked = true;
            try
            {
                if (_variant == "full-ui") new TesterUi().Show();
                string dataset = PrepareFixture(scenario);
                ServerMaster.LogMessageEvent += Log;
                ServerMaster.CreateServer(ServerType.Tester, false);
                _tester = (TesterServer)ServerMaster.GetServers().Single(s => s.ServerType == ServerType.Tester);
                _tester.LogMessageEvent += Log;
                _tester.TestingStartEvent += Started;
                _tester.TestingEndEvent += Ended;
                _tester.ReplayTimeAdvancedEvent += Clock;
                _tester.TypeTesterData = TesterDataType.TickOnlyReadyCandle;
                _tester.SourceDataType = TesterSourceDataType.Folder;
                _tester.SetFolderPath(Path.GetDirectoryName(dataset));
                Wait(Loaded, 45000, "native data/security/portfolio");
                Security security = _tester.Securities.Single(s => s.Name == "APM_SYNTH.txt");
                security.PriceStep = 1; security.PriceStepCost = 1; security.Lot = 1;
                security.VolumeStep = 1; security.DecimalsVolume = 0; security.MinTradeAmount = 1;
                _tester.SaveSecurityDopSettings(security);
                _tester.TimeStart = new DateTime(2026, 9, 25, 0, 0, 1);
                _tester.TimeEnd = new DateTime(2026, 9, 25, 0, 2, 30);
                _tester.StartPortfolio = 1000000;
                _tester.SlippageToSimpleOrder = _slippage;
                _tester.SlippageToStopOrder = 0;
                Program.Check(BotFactory.GetIncludeNamesStrategy().Contains("AdaptivePositionResearchBot"), "actual BotFactory registration");
                _robot = (AdaptivePositionResearchBot)BotFactory.GetStrategyForName("AdaptivePositionResearchBot", "APM_native", StartProgram.IsTester, false);
                _robot.LogMessageEvent += Log;
                if (_variant == "full-ui") OsTraderMaster.Master.CreateNewBot(_robot);
                string pastedSuffix = _variant == "path-crlf" ? "\r\n" : "";
                SetString("Schedule file", Path.Combine(_output, "schedule.json") + pastedSuffix);
                SetString("Dataset file", dataset + pastedSuffix);
                SetString("Artifacts root", Path.Combine(_output, "artifacts") + pastedSuffix);
                SetString("Regime", "On");
                if (_variant == "legacy")
                {
                    SetString("Regime", "Off");
                    _legacy = BotFactory.GetStrategyForName("UnsafeLimitsClosingSample", "APM_legacy", StartProgram.IsTester, false);
                    ((StrategyParameterDecimal)_legacy.Parameters.Single(p => p.Name == "Volume")).ValueDecimal = 7;
                    string[] saved = _legacy.Parameters.Select(p => p.GetStringToSave()).ToArray();
                    _legacy.Delete();
                    File.WriteAllLines(Path.Combine("Engine", "APM_legacyParametrs.txt"), saved);
                    _legacySettingsHash = ApmTickReader.HashFile(Path.Combine("Engine", "APM_legacyParametrs.txt"));
                    _legacy = BotFactory.GetStrategyForName("UnsafeLimitsClosingSample", "APM_legacy", StartProgram.IsTester, false);
                    _legacy.LogMessageEvent += Log;
                    Connect(_legacy.TabsSimple[0], security.Name);
                }
                if (scenario == "S08" || scenario == "S09")
                    ((StrategyParameterBool)_robot.Parameters.Single(p => p.Name == "FAST enabled")).ValueBool = true;
                BotTabSimple tab = _robot.TabsSimple[0];
                tab.NewTickEvent += Tick;
                Connect(tab, security.Name);
                Wait(Connected, 45000, "native tab subscription");
                _tester.SynchSecurities(Bots());
                UiSmoke.Pump(5500);
                if (_variant == "full-ui")
                {
                    _robot.SaveParameters(); tab.Connector.Save(); _tester.Save();
                }
                Thread starter = new Thread(StartReplay) { IsBackground = true, Name = "APM native qualification start" };
                starter.Start();
                Wait(Finished, 90000, "native replay terminal event");
                UiSmoke.Pump(300);
                return Verify(scenario);
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                File.WriteAllText(Path.Combine(_output, "failure.txt"), error.ToString());
                return 1;
            }
            finally
            {
                if (_tester != null)
                {
                    _tester.TesterRegime = TesterRegime.Pause;
                    _tester.TestingStartEvent -= Started; _tester.TestingEndEvent -= Ended;
                    _tester.ReplayTimeAdvancedEvent -= Clock; _tester.LogMessageEvent -= Log;
                }
                if (_robot != null)
                {
                    if (_variant == "full-ui") OsTraderMaster.Master.StopPaint();
                    _robot.TabsSimple[0].NewTickEvent -= Tick;
                    _robot.LogMessageEvent -= Log;
                    // Preserve the full-UI demo's saved native settings for owner replay.
                    if (_variant != "full-ui") _robot.Delete();
                }
                if (_legacy != null) { _legacy.LogMessageEvent -= Log; _legacy.Delete(); }
                ServerMaster.LogMessageEvent -= Log;
                OsEngine.MainWindow.ProccesIsWorked = false;
                lock (_errors) File.WriteAllLines(Path.Combine(_output, "errors.txt"), _errors);
                if (_variant != "full-ui") application.Shutdown();
            }
        }

        private string PrepareFixture(string scenario)
        {
            if (!new[] { "S01", "S02", "S05", "S06", "S07", "S08", "S09", "S26" }.Contains(scenario))
                throw new ArgumentException("Unsupported native scenario.");
            _fee = _variant == "cost1" ? 0.1m : _variant == "cost1.5" ? 0.15m : _variant == "cost2" ? 0.2m : 0;
            _slippage = _variant == "cost1" ? 2 : _variant == "cost1.5" ? 3 : _variant == "cost2" ? 4 : 0;
            string folder = Path.Combine(_output, "ticks"); Directory.CreateDirectory(folder);
            string dataset = Path.Combine(folder, "APM_SYNTH.txt");
            decimal[] path = scenario == "S02" ? new decimal[] { 100, 98, 96, 98, 96, 98 }
                : scenario == "S05" ? new decimal[] { 100, 92 }
                : scenario == "S06" ? new decimal[] { 100, 90, 88 }
                : scenario == "S07" ? new decimal[] { 100, 110, 112 }
                : scenario == "S08" ? new decimal[] { 100, 104 }
                : scenario == "S09" ? new decimal[] { 100, 96 }
                : scenario == "S26" ? new decimal[] { 100 }
                : new decimal[] { 100, 102, 104, 102, 100, 104, 102 };
            DateTime day = new DateTime(2026, 9, 25);
            using (StreamWriter writer = new StreamWriter(dataset, false, new UTF8Encoding(false)))
                for (int second = 1; second <= 150; second++)
                {
                    if (scenario == "S26" && second >= 115 && second < 130) continue;
                    int index = Math.Clamp((second - 35) / 10, 0, path.Length - 1);
                    if ((scenario == "S06" || scenario == "S07") && second >= 46) index = 2;
                    decimal price = (scenario == "S08" || scenario == "S09") && second >= 50
                        ? (scenario == "S08" ? 106m : 94m) : path[index];
                    writer.WriteLine(day.AddSeconds(second).ToString("yyyyMMdd,HHmmss", CultureInfo.InvariantCulture)
                        + "," + price.ToString(CultureInfo.InvariantCulture) + ",100,Buy,0," + second);
                }
            ApmCampaignSpec spec = new ApmCampaignSpec(scenario, "signal-" + scenario, "APM_SYNTH.txt", "GodMode", ApmDirection.Long,
                day.AddSeconds(35), day.AddSeconds(120), 100, 10, 20, 90, 110, 1000, 1, 1, 1, "SYN", "SYN", "UTC",
                StopSlippageReserveTicks: _slippage, FeePerContract: _fee, EntrySlippageReserveTicks: _slippage);
            ApmSchedule schedule = new ApmSchedule(new ApmScheduleDocument("APM-Schedule-v1", "SyntheticNative-" + scenario,
                ApmTickReader.HashFile(dataset), ApmDataProfile.TradeOnly, new[] { spec }));
            schedule.Save(Path.Combine(_output, "schedule.json"));
            return dataset;
        }

        private int Verify(string scenario)
        {
            if (_variant == "legacy")
            {
                Program.Check(_started && _tickEvents > 0, "legacy/APM-Off actual native replay");
                Program.Equal(0, _tester.MyTrades?.Count ?? 0, "Off robots send no orders");
                Program.Check(_robot.ActiveController == null, "APM Off never creates execution owner");
                Program.Equal(7m, ((StrategyParameterDecimal)_legacy.Parameters.Single(p => p.Name == "Volume")).ValueDecimal,
                    "old native parameter serialization reloads non-default value");
                Program.Equal(_legacySettingsHash, ApmTickReader.HashFile(Path.Combine("Engine", "APM_legacyParametrs.txt")),
                    "APM Off leaves legacy settings unchanged");
                lock (_errors) Program.Equal(0, _errors.Count, "legacy replay no logged errors");
                File.WriteAllText(Path.Combine(_output, "result.json"), JsonSerializer.Serialize(new { status = "PASS", scenario,
                    variant = _variant, legacy = "UnsafeLimitsClosingSample", _tickEvents, savedVolume = 7, nativeFills = 0,
                    limitation = "Legacy Off/settings compatibility only; no claim of legacy strategy profitability or all-robot parity" }));
                Console.WriteLine("NATIVE_LEGACY_PASS " + _output);
                return 0;
            }
            ApmExecutionController controller = _robot.ActiveController;
            Program.Check(_started && _clockEvents > 0 && _tickEvents > 0 && controller != null, "actual native replay events");
            ApmSnapshot final = controller.Snapshot;
            if (_uiOpened)
                foreach (Window window in Application.Current.Windows)
                    if (window is ApmDiagnosticsWindow) UiSmoke.Capture(window, Path.Combine(_output, "native-diagnostics.png"));
            if (_variant == "full-ui")
            {
                Program.Check(TesterUi.Instance.IsVisible && OsTraderMaster.Master.PanelsArray.Contains(_robot), "actual visible TesterUi hosts registered robot");
                UiSmoke.Capture(TesterUi.Instance, Path.Combine(_output, "native-tester.png"));
            }
            ApmAuditRow[] rows = controller.Recent;
            ApmAuditRow[] fills = rows.Where(r => r.Kind == "Fill").ToArray();
            File.WriteAllText(Path.Combine(_output, "native-rows.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
            Program.Check(fills.Length > 0, "actual native fills received by registered robot");
            Program.Equal(ApmState.Completed, final.State, "native campaign completed");
            Program.Equal(0m, final.FilledVolume, "native campaign flat");
            Program.Equal(0m, final.PendingIncrease + final.PendingReduce, "native reservations terminal");
            Program.Equal(1, _robot.CampaignResults.Length, "native replay archives campaign exactly once");
            using (JsonDocument summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(_robot.ArtifactDirectory), "run-summary.json"))))
                Program.Equal("CompletedResearchOnly", summary.RootElement.GetProperty("CompletionStatus").GetString(), "whole schedule terminal evidence");
            Program.Equal(0m, _robot.TabsSimple[0].PositionsAll.Sum(p => p.OpenVolume), "actual native journal flat");
            Program.Equal(fills.Sum(r => r.Volume), _tester.MyTrades.Sum(t => t.Volume), "native fills match APM turnover");
            string quantities = string.Join(",", fills.Select(r => r.Snapshot.FilledVolume.ToString(CultureInfo.InvariantCulture)));
            if (_fee == 0 && _variant != "controls" && (scenario == "S01" || scenario == "S02"))
                Program.Equal(scenario == "S01" ? "10,8,6,8,10,6,8,0" : "10,14,18,14,18,14,0", quantities, "actual native S01/S02 quantity trace");
            if (_variant == "controls")
                Program.Check(!fills.Any(r => r.Intent.Increases && r.Time >= new DateTime(2026, 9, 25, 0, 0, 40)
                    && r.Time < new DateTime(2026, 9, 25, 0, 1, 15)), "native tick/timer respects independent pause/Off until both resume");
            if (scenario == "S05" || scenario == "S06" || scenario == "S07" || scenario == "S26")
                Program.Equal("10,0", quantities, "no unrequested native increase around protection");
            if (scenario == "S06" || scenario == "S07")
            {
                Program.Equal(scenario == "S06" ? "HARD_STOP" : "FINAL_TARGET", final.ExitReason, "native terminal trigger");
                Program.Equal(scenario == "S06" ? 90m : 110m, fills.Last().Price, "native Tester permits same-time market execution");
            }
            if (scenario == "S08" || scenario == "S09")
            {
                DateTime impulse = new DateTime(2026, 9, 25, 0, 0, 50);
                ApmRegime regime = scenario == "S08" ? ApmRegime.FastFavorable : ApmRegime.FastAdverse;
                ApmAction forbidden = scenario == "S08" ? ApmAction.Reduce : ApmAction.Add;
                string reason = scenario == "S08" ? "FAST_FAVORABLE_DEFER" : "FAST_ADVERSE_NO_ADD";
                ApmAuditRow[] suppressed = rows.Where(r => r.Kind == "Decision" && r.Time >= impulse && r.Time < impulse.AddSeconds(5)).ToArray();
                Program.Check(suppressed.Length >= 5 && suppressed.All(r => r.Snapshot.Regime == regime && r.Action == "Wait"
                    && r.Reasons.Contains(reason) && (scenario == "S08" ? r.Allowed < r.Filled : r.Allowed > r.Filled)),
                    "native FAST suppresses an actual nonzero target gap with explicit reason");
                Program.Check(!rows.Any(r => (r.Kind == "Intent" || r.Kind == "Fill") && r.Time >= impulse
                    && r.Time < impulse.AddSeconds(5) && r.Intent?.Action == forbidden), "no forbidden native intent/fill during FAST interval");
            }
            if (scenario == "S26")
                Program.Equal(new DateTime(2026, 9, 25, 0, 2, 0), rows.First(r => r.Action == "Exit").Time,
                    "silent native market starts exit on cutoff heartbeat");
            decimal cash = _tester.MyTrades.Sum(t => (t.Side == Side.Sell ? 1m : -1m) * t.Price * t.Volume - _fee * t.Volume);
            Program.Check(Math.Abs(cash - final.RealizedNet) < 0.01m, "native cashflow reconciles within declared SYN monetary unit 0.01");
            Program.Equal(_tester.MyTrades.Sum(t => t.Volume) * _fee, final.Fees, "actual contract fees reconciled");
            string canonical = JsonSerializer.Serialize(rows.Select(r => new
            { r.Kind, r.Time, r.Sequence, r.Action, r.Volume, r.Price, r.Reasons, r.Snapshot, r.Side, r.Slippage }));
            File.WriteAllText(Path.Combine(_output, "canonical.json"), canonical);
            ApmArtifacts.ExportCsv(Path.Combine(_output, "trace.csv"), rows);
            lock (_errors) Program.Equal(0, _errors.Count, "native execution has no logged errors");
            File.WriteAllText(Path.Combine(_output, "result.json"), JsonSerializer.Serialize(new
            {
                status = "PASS", scenario, variant = _variant, engine = "ActualNativeTester", _clockEvents, _tickEvents,
                quantities, nativeFills = _tester.MyTrades.Count, final, feePerContract = _fee, nativeSlippageTicks = _slippage,
                runtime = Environment.Version.ToString(), operatingSystem = Environment.OSVersion.ToString(),
                moneyUnit = 0.01m, cashflowDifference = cash - final.RealizedNet,
                uiConcurrent = _uiOpened, canonicalHash = ApmTickReader.HashFile(Path.Combine(_output, "canonical.json")),
                artifactDirectory = _robot.ArtifactDirectory, limitations = "Synthetic TradeOnly; full fills; no queue/impact; no live parity"
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("NATIVE_TESTER_PASS " + scenario + " q=" + quantities + " " + _output);
            return 0;
        }

        private void SetString(string name, string value) => ((StrategyParameterString)_robot.Parameters.Single(p => p.Name == name)).ValueString = value;
        private List<BotPanel> Bots() => _legacy == null ? new List<BotPanel> { _robot } : new List<BotPanel> { _robot, _legacy };
        private void Connect(BotTabSimple tab, string security)
        {
            tab.Connector.ServerType = ServerType.Tester;
            tab.Connector.ServerFullName = _tester.ServerNameAndPrefix;
            tab.Connector.PortfolioName = "GodMode";
            tab.Connector.TimeFrame = TimeFrame.Sec1;
            tab.Connector.SecurityClass = "TestClass";
            tab.Connector.SecurityName = security;
        }
        private bool Loaded() => _tester.DataIsReady && _tester.Securities?.Any(s => s.Name == "APM_SYNTH.txt") == true
            && _tester.ServerStatus == ServerConnectStatus.Connect && _tester.Portfolios?.Any(p => p.Number == "GodMode") == true;
        private bool Connected() => Bots().All(b => b.TabsSimple[0].IsConnected && b.TabsSimple[0].IsReadyToTrade);
        private bool Finished() => _ended;
        private void Wait(Func<bool> condition, int milliseconds, string stage)
        {
            DateTime limit = DateTime.UtcNow.AddMilliseconds(milliseconds);
            while (!condition())
            {
                if (DateTime.UtcNow >= limit) throw new TimeoutException(stage + "; clocks=" + _clockEvents + "; ticks=" + _tickEvents);
                if (Interlocked.CompareExchange(ref _uiRequested, 2, 1) == 1)
                {
                    _robot.ShowIndividualSettingsDialog();
                    ApmDiagnosticsWindow window = Application.Current.Windows.OfType<ApmDiagnosticsWindow>().Single();
                    foreach (string name in new[] { "ButtonDecisions", "ButtonOrders", "ButtonCampaigns", "ButtonQuality", "ButtonReport" })
                        ((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    _uiOpened = true;
                    _tester.TesterRegime = TesterRegime.Play;
                }
                UiSmoke.Pump(50);
            }
            Console.WriteLine("NATIVE_STAGE " + stage);
        }
        private void StartReplay() => _tester.TestingStart();
        private void Started()
        {
            if (_variant != "full-ui")
            {
                _robot.Clear();
                _legacy?.Clear();
                _tester.SynchSecurities(Bots());
            }
            _started = true;
        }
        private void Ended() => _ended = true;
        private void Clock(DateTime time) => Interlocked.Increment(ref _clockEvents);
        private void Tick(Trade trade)
        {
            Interlocked.Increment(ref _tickEvents);
            if (_variant == "ui" || _variant == "slow" || _variant == "full-ui") Thread.Sleep(8);
            if ((_variant == "ui" || _variant == "full-ui") && _robot.ActiveController != null)
            {
                _robot.ActiveController.DetailedDiagnostics = true;
                if (trade.Time.Second == 40 && trade.Time.Minute == 0 && Interlocked.CompareExchange(ref _uiRequested, 1, 0) == 0)
                    _tester.TesterRegime = TesterRegime.Pause;
            }
            if (_variant == "controls" && _robot.ActiveController != null)
            {
                int second = (int)trade.Time.TimeOfDay.TotalSeconds;
                if (second == 40) _robot.ActiveController.Pause(true);
                if (second == 50) SetString("Regime", "Off");
                if (second == 60) _robot.ActiveController.Pause(false);
                if (second == 75) SetString("Regime", "On");
            }
        }
        private void Log(string message, LogMessageType type)
        {
            if (type != LogMessageType.Error) return;
            lock (_errors) _errors.Add(message);
            Console.Error.WriteLine("NATIVE_ERROR " + message);
        }
    }
}
