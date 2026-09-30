/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using OsEngine.Entity;
using OsEngine.Logging;
using OsEngine.Market.Servers.Optimizer;
using OsEngine.Market.Servers.Tester;
using OsEngine.OsTrader.Grids.Futures2;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.OsTrader.Panels.Tab;

namespace OsEngine.Robots.MyBots
{
    /// <summary>Manual exact-step Futures2 grid with compact settings and optional per-zone lots/markup.</summary>
    /// <remarks>Uses the same native ownership, cancel-confirm reconfiguration and recovery as Futures2Grid.
    /// Only the first tab trades; the second is an unconfigured adapter reserve. Decimal bounds/step/markup
    /// use price units, lots use native contracts. Decisions follow native quotes on the common adapter clock,
    /// not finished candles. Off pauses entries; exits remain managed and bounds are not automatic stops.
    /// Commands/callbacks serialize on adapter Sync. Live checkpoints use a separate directory and require
    /// explicit reconciliation after restart; Tester/Optimizer reset on TestingStart and start on the first
    /// quote when On. Disposal releases subscriptions/timer and dispatches window closure. Shared engine
    /// reuse does not establish native replay or live qualification of this host. THG-SIMPLE-017.</remarks>
    [Bot("Futures2GridSimple")]
    public sealed class Futures2GridSimple : BotPanel
    {
        private readonly Dictionary<string, IIStrategyParameter> _settings = new Dictionary<string, IIStrategyParameter>();
        private readonly Dictionary<StrategyParameterButton, Action> _buttons = new Dictionary<StrategyParameterButton, Action>();
        private readonly Futures2NativeAdapter _adapter;
        private readonly BotTabSimple _tab;
        private readonly bool _live;
        private readonly System.Threading.Timer _timer;
        private TesterServer _tester;
        private OptimizerServer _optimizer;
        private string _configuration = "";
        private string _error = "";
        private bool _simulationStarted;
        private bool _disposed;
        private Window _window;
        private DispatcherTimer _windowTimer;

        /// <summary>Registers native parameters and callbacks without starting a connector or placing orders.</summary>
        public Futures2GridSimple(string name, StartProgram startProgram) : base(name, startProgram)
        {
            _live = startProgram == StartProgram.IsOsTrader;
            TabCreate(BotTabType.Simple); TabCreate(BotTabType.Simple);
            _tab = TabsSimple[0];
            _settings.Add("Regime", CreateParameter("Regime", "Off", new[] { "Off", "On" }, "Grid"));
            _settings.Add("Direction", CreateParameter("Direction", "Long", new[] { "Long", "Short" }, "Grid"));
            Number("Lower bound", 0, "Grid"); Number("Upper bound", 10, "Grid");
            Number("Grid step", 1, "Grid"); Number("Markup per zone", 1, "Grid"); Number("Lots per zone", 1, "Grid");
            Text("Zone markup overrides", "", "Zones"); Text("Zone lot overrides", "", "Zones");
            Number("Funds limit", 100000, "Safety"); Number("Collateral per lot", 100, "Safety");
            Flag("Signed order capability selected", false, "Safety");
            Flag("Owner verified account and orders", false, "Recovery");
            Number("External net", 0, "Recovery"); Text("Resolve intent id", "", "Recovery");
            Number("Verified executed quantity", 0, "Recovery");
            Futures2Store store = _live ? new Futures2Store(Path.Combine("Engine", "Futures2GridSimple", Futures2Store.Name(name))) : null;
            _adapter = new Futures2NativeAdapter(_tab, TabsSimple[1], store, _live, WriteLog);
            _adapter.BeforeDecision = BeforeDecision;
            Button("Preview and status", ShowIndividualSettingsDialog, "Grid");
            Button("Apply configuration", ApplyConfiguration, "Grid");
            Button("Start or resume", Start, "Grid");
            Button("Pause entries", Pause, "Grid");
            Button("Emergency flatten", () => { SetOff(); _adapter.Engine.Flatten("Operator emergency flatten"); }, "Safety");
            Button("Reconcile native and account", () => _adapter.Reconcile(B("Owner verified account and orders")), "Recovery");
            Button("Resolve verified absent order", () => _adapter.ResolveAbsent(S("Resolve intent id"), D("Verified executed quantity"), B("Owner verified account and orders")), "Recovery");
            Button("Rearm after confirmed flat", Rearm, "Recovery");
            ParametrsChangeByUser += SettingsChanged;
            DeletingEvent += Deleted; DeleteEvent += Deleted;
            _tab.SecuritySubscribeEvent += SecurityChanged;
            AttachReplay();
            if (_live) _timer = new System.Threading.Timer(_ => _adapter.Pump(), null, 250, 250);
            Description = "Exact-step Futures2 grid. Configure the first tab only. Zone 1 is the lowest price. "
                + "Markup/lots overrides: 1=2;3=4. Bounds define levels, not automatic stops. Off pauses entries and keeps exits.";
        }

        private void Number(string name, decimal value, string group)
            => _settings.Add(name, CreateParameter(name, value, -1000000m, 1000000m, 0.00001m, group));
        private void Text(string name, string value, string group) => _settings.Add(name, CreateParameter(name, value, group));
        private void Flag(string name, bool value, string group) => _settings.Add(name, CreateParameter(name, value, group));
        private string S(string name) => ((StrategyParameterString)_settings[name]).ValueString;
        private decimal D(string name) => ((StrategyParameterDecimal)_settings[name]).ValueDecimal;
        private bool B(string name) => ((StrategyParameterBool)_settings[name]).ValueBool;
        private void SetOff() => ((StrategyParameterString)_settings["Regime"]).ValueString = "Off";
        private void Pause() { SetOff(); _adapter.Engine.Pause(); }
        private void WriteLog(string message) => SendNewLogMessage(message, LogMessageType.System);
        private void Button(string name, Action command, string group)
        {
            StrategyParameterButton button = CreateParameterButton(name, group);
            Action callback = () => Run(command);
            _buttons.Add(button, callback); button.UserClickOnButtonEvent += callback;
        }
        private void Run(Action command)
        {
            lock (_adapter.Sync)
            {
                if (_disposed) return;
                try
                {
                    UpdateAdapterSettings(); command(); _error = "";
                    _adapter.Save(); _adapter.Pump();
                }
                catch (Exception error)
                {
                    _error = error.Message;
                    SendNewLogMessage(error.ToString(), LogMessageType.Error);
                }
            }
        }
        private Futures2SimpleSettings Settings() => new Futures2SimpleSettings
        {
            Low = D("Lower bound"), High = D("Upper bound"), Step = D("Grid step"),
            Markup = D("Markup per zone"), Lots = D("Lots per zone"),
            Direction = Enum.Parse<Futures2Direction>(S("Direction")),
            ZoneMarkups = S("Zone markup overrides"), ZoneLots = S("Zone lot overrides"),
            Funds = D("Funds limit"), Collateral = D("Collateral per lot")
        };
        private Futures2Plan Plan()
        {
            Futures2Plan plan = Settings().Build(_tab.Security);
            plan.EndpointIdentity = _adapter.EndpointIdentity(0); plan.PaperExecution = _adapter.IsPaper(0);
            return plan;
        }
        private void ApplyConfiguration()
        {
            Futures2Plan plan = Plan();
            string configuration = JsonSerializer.Serialize(Settings());
            Futures2Checkpoint data = _adapter.Engine.Data;
            // Do not create another grid version for repeated Apply or a recovered identical snapshot.
            Futures2Plan previous = data.PendingPlan ?? (data.ActivePlan.Length > 0 ? data.Plans[data.ActivePlan] : null);
            if (previous != null && SamePlan(previous, plan)) { _configuration = configuration; return; }
            _adapter.Engine.Configure(plan, new Futures2Policy(), plan.Reserved, 0, true);
            _configuration = configuration;
        }
        private static bool SamePlan(Futures2Plan left, Futures2Plan right)
            => left.EndpointIdentity == right.EndpointIdentity && left.PaperExecution == right.PaperExecution
                && JsonSerializer.Serialize(left.Input) == JsonSerializer.Serialize(right.Input)
                && JsonSerializer.Serialize(left.Levels) == JsonSerializer.Serialize(right.Levels);
        private void Start()
        {
            UpdateAdapterSettings();
            if (!_adapter.SignedOrdersEnabled) throw new InvalidOperationException("Select signed order capability for the live instrument and connector.");
            ApplyConfiguration();
            if (_adapter.Engine.Data.PendingPlan != null)
            {
                if (!_adapter.CanConfirmNativeState) throw new InvalidOperationException("Reconcile native/account state before resuming.");
                _adapter.Engine.Data.ResumeAfterConfigure = true;
            }
            else _adapter.Engine.Start(_adapter.CanConfirmNativeState, _adapter.Now);
            ((StrategyParameterString)_settings["Regime"]).ValueString = "On";
            if (!_live) _simulationStarted = true;
        }
        private void SettingsChanged()
        {
            Run(() =>
            {
                try
                {
                    if (_adapter.Engine.Data.ActivePlan.Length > 0 && JsonSerializer.Serialize(Settings()) != _configuration)
                        ApplyConfiguration();
                    if (S("Regime") == "Off") { if (_adapter.Engine.Data.ActivePlan.Length > 0) _adapter.Engine.Pause(); }
                    else if (_live && _adapter.Engine.Data.ActivePlan.Length > 0) Start();
                    else if (!_live && _adapter.Quotes.ContainsKey(0)) { _adapter.Reconcile(false); Start(); }
                }
                catch
                {
                    Pause(); _adapter.Save(); _adapter.Pump();
                    throw;
                }
            });
        }
        private void UpdateAdapterSettings()
        {
            _adapter.SignedOrdersEnabled = !_live || B("Signed order capability selected");
            _adapter.UsePortfolioFunds = false;
            // Risk fields become effective only with the accepted plan, including runtime drain.
            _adapter.ManualFreeFunds = _adapter.Engine.Data.Capital > 0 ? _adapter.Engine.Data.Capital : Math.Max(0, D("Funds limit"));
            _adapter.ExternalNet[0] = D("External net");
        }
        private void BeforeDecision()
        {
            if (_disposed) return;
            AttachReplay(); UpdateAdapterSettings();
            // Shared publication returns Ready when resume is disabled. Simple Off still owns exits:
            // restore the paused-entry state on the next quote/timer pass, keeping reconciliation gates.
            if (S("Regime") == "Off" && _adapter.Engine.Data.State == Futures2State.Ready
                && _adapter.Engine.Data.ActivePlan.Length > 0 && _adapter.Engine.Data.Book.Lots.Any(l => l.Quantity > 0))
                _adapter.Engine.Pause();
            if (!_live && S("Regime") == "On" && !_simulationStarted && _adapter.Quotes.ContainsKey(0))
            {
                ApplyConfiguration(); _adapter.Reconcile(false); Start();
            }
        }
        private void Rearm()
        {
            _adapter.Reconcile(B("Owner verified account and orders"));
            if (!_adapter.Engine.Data.Book.LocallyEmpty) throw new InvalidOperationException("Confirmed flat and no unresolved orders required.");
            SetOff();
            Futures2Checkpoint data = _adapter.Engine.Data;
            data.Emergency = false; data.HadInventory = false; data.Reducing = false; data.RecoverReduction = false;
            data.ResumeAfterConfigure = false; data.Trail.Reset(_adapter.Now);
            data.State = Futures2State.Ready; data.Reason = "Simple campaign explicitly rearmed after confirmed flat";
        }
        private void SecurityChanged(Security security) { lock (_adapter.Sync) { if (!_disposed) AttachReplay(); } }
        private void AttachReplay()
        {
            if (_live) return;
            TesterServer tester = _tab.Connector?.MyServer as TesterServer;
            OptimizerServer optimizer = _tab.Connector?.MyServer as OptimizerServer;
            if (ReferenceEquals(tester, _tester) && ReferenceEquals(optimizer, _optimizer)) return;
            DetachReplay(); _tester = tester; _optimizer = optimizer;
            if (_tester != null) { _tester.TestingStartEvent += ReplayStarted; _tester.ReplayTimeAdvancedEvent += ReplayTime; }
            if (_optimizer != null) { _optimizer.TestingStartEvent += ReplayStarted; _optimizer.ReplayTimeAdvancedEvent += ReplayTime; }
        }
        private void DetachReplay()
        {
            if (_tester != null) { _tester.TestingStartEvent -= ReplayStarted; _tester.ReplayTimeAdvancedEvent -= ReplayTime; }
            if (_optimizer != null) { _optimizer.TestingStartEvent -= ReplayStarted; _optimizer.ReplayTimeAdvancedEvent -= ReplayTime; }
            _tester = null; _optimizer = null;
        }
        private void ReplayStarted()
        {
            lock (_adapter.Sync)
            {
                if (_disposed) return;
                _adapter.ResetSimulation(); _configuration = ""; _error = ""; _simulationStarted = false;
            }
        }
        private void ReplayTime(DateTime time) { if (!_disposed) _adapter.AdvanceReplay(time); }

        /// <summary>Shows accepted and preview levels, state, recovery diagnostics and native intent identities.</summary>
        public override void ShowIndividualSettingsDialog()
        {
            if (Application.Current == null || _disposed) return;
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_disposed) return;
                if (_window != null) { _window.Activate(); return; }
                TextBox text = new TextBox { IsReadOnly = true, FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
                _window = new Window { Title = "Futures2GridSimple", Width = 950, Height = 650, Content = text };
                _windowTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _windowTimer.Tick += (_, _) => text.Text = Status();
                _window.Closed += (_, _) => { _windowTimer?.Stop(); _windowTimer = null; _window = null; };
                text.Text = Status(); _windowTimer.Start(); _window.Show();
            }));
        }
        private string Status()
        {
            lock (_adapter.Sync)
            {
                Futures2Checkpoint data = _adapter.Engine.Data;
                StringBuilder text = new StringBuilder();
                text.AppendLine(data.State + " | " + data.Reason + " | emergency=" + data.Emergency);
                if (_error.Length > 0) text.AppendLine("Last command rejected: " + _error);
                text.AppendLine("Configure the first tab only. Bounds are levels, not automatic stops. Zone 1 = lowest price.");
                text.Append(_adapter.DescribeNativeState());
                text.AppendLine("Active=" + data.ActivePlan + " | pending=" + data.PendingPlan?.Id + " | capital=" + data.Capital);
                if (data.ActivePlan.Length > 0) AppendPlan(text, "Accepted", data.Plans[data.ActivePlan]);
                try { AppendPlan(text, "Preview (not yet accepted)", Plan()); }
                catch (Exception error) { text.AppendLine("Preview rejected: " + error.Message); }
                foreach (Futures2Intent intent in data.Book.Intents.TakeLast(50))
                    text.AppendLine(intent.Id + " " + (intent.Entry ? "ENTRY" : "EXIT") + " " + intent.State + " "
                        + intent.Filled + "/" + intent.Quantity + " price=" + intent.Price.ToString("0.#####", CultureInfo.InvariantCulture)
                        + " position=" + intent.PositionNumber + " order=" + intent.OrderNumber + " market=" + intent.MarketNumber);
                foreach (Futures2Lot lot in data.Book.Lots.Where(l => l.Quantity > 0))
                    text.AppendLine("Held plan=" + lot.PlanId + " zone=" + Futures2SimpleSettings.Zone(data.Plans[lot.PlanId], data.Plans[lot.PlanId].Levels[lot.LevelId])
                        + " qty=" + lot.Quantity + " average=" + lot.Average);
                return text.ToString();
            }
        }
        private static void AppendPlan(StringBuilder text, string title, Futures2Plan plan)
        {
            text.AppendLine(title + ": " + plan.Levels.Count + " zones | exact step=" + plan.IdealStep + " | reserve=" + plan.Reserved);
            foreach (Futures2Level level in plan.Levels.OrderBy(l => l.Price).Take(200))
                text.AppendLine(string.Format(CultureInfo.InvariantCulture, "Zone {0}: price={1:0.#####} lots={2} markup={3:0.#####}",
                    Futures2SimpleSettings.Zone(plan, level), level.Price, level.Volume, level.Markup));
            if (plan.Levels.Count > 200) text.AppendLine("Only the first 200 zones are displayed.");
        }
        private void Deleted()
        {
            lock (_adapter.Sync)
            {
                if (_disposed) return;
                _disposed = true; _timer?.Dispose(); DetachReplay();
                _tab.SecuritySubscribeEvent -= SecurityChanged;
                ParametrsChangeByUser -= SettingsChanged; DeletingEvent -= Deleted; DeleteEvent -= Deleted;
                foreach (KeyValuePair<StrategyParameterButton, Action> button in _buttons) button.Key.UserClickOnButtonEvent -= button.Value;
                _adapter.BeforeDecision = null; _adapter.Dispose();
                if (_window != null) _window.Dispatcher.BeginInvoke(new Action(() => _window?.Close()));
            }
        }
    }
}
