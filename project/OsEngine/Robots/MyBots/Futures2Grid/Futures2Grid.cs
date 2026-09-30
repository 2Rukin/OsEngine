/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using OsEngine.Entity;
using OsEngine.Logging;
using OsEngine.Market.Servers.Tester;
using OsEngine.Market.Servers.Optimizer;
using OsEngine.OsTrader.Grids.Futures2;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.OsTrader.Panels.Tab;

namespace OsEngine.Robots.MyBots
{
    /// <summary>Manual native range-grid robot with versioned plans and explicit signed-price execution.</summary>
    /// <remarks>Two native tabs preserve old/new instrument ownership during contract cutover.
    /// Off pauses increases; emergency close is a separate latched command. Runtime settings use
    /// cancel-confirm publication. Live restart requires operator reconciliation. THG-INTEGRATION-001.</remarks>
    [Bot("Futures2Grid")]
    public sealed class Futures2Grid : BotPanel
    {
        private readonly Dictionary<string, IIStrategyParameter> _settings = new Dictionary<string, IIStrategyParameter>();
        private readonly List<StrategyParameterButton> _buttons = new List<StrategyParameterButton>();
        private readonly Futures2NativeAdapter _adapter;
        private readonly BotTabSimple[] _tabs;
        private readonly System.Threading.Timer _timer;
        private readonly bool _live;
        private readonly string _identity;
        private string _configuration = "";
        private string _gridConfiguration = "";
        private decimal _manualCapital;
        private string _peerKey = "";
        private TesterServer _tester;
        private OptimizerServer _optimizer;
        private Window _window;
        private DispatcherTimer _windowTimer;
        private bool _disposed;
        private string _emptyRemovalStatus = "RemoveEmptyRobot disabled";

        /// <summary>Registers manual parameters and native tabs; does not start trading or change connections.</summary>
        public Futures2Grid(string name, StartProgram startProgram) : base(name, startProgram)
        {
            _identity = name; _live = startProgram == StartProgram.IsOsTrader;
            TabCreate(BotTabType.Simple); TabCreate(BotTabType.Simple);
            _tabs = new[] { TabsSimple[0], TabsSimple[1] };
            _settings["Regime"] = CreateParameter("Regime", "Off", new[] { "Off", "On" }, "Control");
            Add("Capital", 1000m, "Control"); Add("Signed order capability selected", false, "Control");
            Add("Use portfolio available funds", false, "Control"); Add("Manual available funds", 1000m, "Control");
            Add("Owner verified account and orders", false, "Recovery");
            Add("External net endpoint 0", 0m, "Recovery"); Add("External net endpoint 1", 0m, "Recovery");
            Add("Resolve intent id", "", "Recovery"); Add("Verified executed quantity", 0m, "Recovery");
            AddObject("Grid", new Futures2PlanInput(), new[] { "Instrument", "Tick" });
            AddObject("Policy", new Futures2Policy(), Array.Empty<string>());
            Add("Selected plan id", "", "Commands");
            Add("Selected levels", "", "Commands"); Add("Selected level", 0, "Commands"); Add("Selected markup", 0.0001m, "Commands");
            Add("Selected entry enabled", true, "Commands"); Add("Selected exit enabled", true, "Commands");
            Add("Inventory operation id", "", "Inventory"); Add("Inventory endpoint", 0, "Inventory");
            Add("Inventory levels", "", "Inventory"); Add("Inventory use level prices", false, "Inventory");
            Add("Inventory target or execution quantity", 0m, "Inventory");
            Add("Inventory price known", false, "Inventory"); Add("Inventory price", 0m, "Inventory");
            Add("External execution reference", "", "Inventory"); Add("External execution fee", 0m, "Inventory");
            Add("External execution time", "", "Inventory");
            Add("Range shift", 0m, "Commands");
            Add("Accept coordination", false, "Portfolio"); Add("Portfolio participants", "", "Portfolio");
            Add("Portfolio leader", false, "Portfolio"); Add("Portfolio retain fraction", 0m, "Portfolio");
            Add("Transfer destination", "", "Portfolio"); Add("Transfer collateral", 0m, "Portfolio");
            Add("Replacement endpoint", 1, "Rollover");
            Add("Replacement shift by spread", true, "Rollover");
            AddObject("Replacement", new Futures2PlanInput(), new[] { "Instrument", "Tick" });
            Futures2Store store = _live ? new Futures2Store(Path.Combine("Engine", "Futures2Grid", Futures2Store.Name(name))) : null;
            _adapter = new Futures2NativeAdapter(_tabs[0], _tabs[1], store, _live, WriteLog);
            _gridConfiguration = GridFingerprint(); _manualCapital = D("Capital");
            _configuration = Fingerprint();
            _adapter.BeforeDecision = BeforeDecision;
            _adapter.PortfolioScope = PortfolioScope;
            ParametrsChangeByUser += SettingsChanged;
            DeletingEvent += Deleted;
            DeleteEvent += Deleted;
            CreateParameterButton("Preview and status", "Control").UserClickOnButtonEvent += ShowIndividualSettingsDialog;
            Button("Apply configuration", ApplyConfiguration, "Control");
            Button("Reconcile native and account", () => _adapter.Reconcile(B("Owner verified account and orders")), "Recovery");
            Button("Start or resume", Start, "Control");
            Button("Pause entries", () => { ((StrategyParameterString)_settings["Regime"]).ValueString = "Off"; _adapter.Engine.Pause(); }, "Control");
            Button("Cancel working orders", () => { _adapter.Engine.Pause(); _adapter.Engine.Data.CancelAll = true; }, "Control");
            Button("Emergency flatten", () => _adapter.Engine.Flatten("Operator emergency flatten"), "Control");
            Button("Reduce to retained collateral", () => _adapter.Engine.Reduce(D("Policy.RetainCollateral"), "Operator voluntary reduction"), "Commands");
            Button("Cancel voluntary reduction", () =>
            {
                if (_adapter.Engine.Data.Emergency) throw new InvalidOperationException("Emergency cannot be revoked.");
                _adapter.Engine.Data.RecoverReduction = true; _adapter.Engine.Data.Trail.Disabled = true;
            }, "Commands");
            Button("Reset daily counter", () => _adapter.Engine.Data.Book.DaySpent = 0, "Commands");
            Button("Reset HJ", () => _adapter.Engine.Data.Hj = new Futures2Hj(), "Commands");
            Button("Rearm trailing helper", () => { _adapter.Engine.Data.Trail.Reset(_adapter.Now); _adapter.Engine.Data.GroupReductionId = ""; }, "Commands");
            Button("Apply selected level settings", () => EditLevel(true, true, true), "Commands");
            Button("Set selected markup", () => EditLevel(true, false, false), "Commands");
            Button("Set selected entry enabled", () => EditLevel(false, true, false), "Commands");
            Button("Set selected exit enabled", () => EditLevel(false, false, true), "Commands");
            Button("Cancel selected level orders", () => { Futures2Plan selected = SelectedPlan();
                Futures2Commands.CancelLevels(_adapter.Engine.Data, selected.Id, Selection(selected)); }, "Commands");
            Button("Register selected levels to capacity", () => AdjustInventory(Futures2InventoryKind.RegisterToCapacity), "Inventory");
            Button("Set selected inventory quantity", () => AdjustInventory(Futures2InventoryKind.SetQuantity), "Inventory");
            Button("Set selected accounting basis", () => AdjustInventory(Futures2InventoryKind.SetBasis), "Inventory");
            Button("Attribute external inventory increase", () => AdjustInventory(Futures2InventoryKind.ExternalIncrease), "Inventory");
            Button("Attribute external inventory decrease", () => AdjustInventory(Futures2InventoryKind.ExternalDecrease), "Inventory");
            Button("Cancel pending selected entries", () => Futures2Commands.CancelEntryBatch(_adapter.Engine.Data), "Commands");
            Button("Enter selected levels", () => { Futures2Plan selected = SelectedPlan();
                Futures2Commands.EnterLevels(_adapter.Engine.Data, selected.Id, Selection(selected)); }, "Commands");
            Button("Exit selected levels", () => { Futures2Plan selected = SelectedPlan();
                Futures2Commands.ExitLevels(_adapter.Engine.Data, selected.Id, Selection(selected)); }, "Commands");
            Button("Shift range", () => Configure(Futures2Commands.Shift(Active(), D("Range shift"))), "Commands");
            Button("Widen range to quote", Widen, "Commands");
            Button("Prepare replacement", PrepareRollover, "Rollover");
            Button("Start replacement", StartRollover, "Rollover");
            Button("Cancel replacement", CancelRollover, "Rollover");
            Button("Start collateral transfer", StartTransfer, "Portfolio");
            Button("Cancel collateral transfer", CancelTransfer, "Portfolio");
            Button("Reduce selected portfolio", ReducePortfolio, "Portfolio");
            Button("Resolve verified absent order", () => _adapter.ResolveAbsent(S("Resolve intent id"), D("Verified executed quantity"), B("Owner verified account and orders")), "Recovery");
            Button("New campaign after confirmed flat", NewCampaign, "Recovery");
            foreach (BotTabSimple tab in _tabs) tab.SecuritySubscribeEvent += SecurityChanged;
            AttachReplay();
            if (_live) _timer = new System.Threading.Timer(_ => _adapter.Pump(), null, 250, 250);
        }

        private void Add(string name, object value, string group)
        {
            IIStrategyParameter parameter;
            if (value is bool boolean) parameter = CreateParameter(name, boolean, group);
            else if (value is int integer) parameter = CreateParameter(name, integer, -10000, 10000, 1, group);
            else if (value is decimal number) parameter = CreateParameter(name, number, -1000000m, 1000000m, 0.00001m, group);
            else if (value.GetType().IsEnum) parameter = CreateParameter(name, value.ToString(), Enum.GetNames(value.GetType()), group);
            else parameter = CreateParameter(name, (string)value, group);
            _settings.Add(name, parameter);
        }
        private void AddObject(string prefix, object value, string[] exclude)
        {
            foreach (PropertyInfo property in value.GetType().GetProperties().Where(p => p.CanWrite && !exclude.Contains(p.Name)))
            {
                object item = property.GetValue(value);
                if (item is Futures2Region) AddObject(prefix + "." + property.Name, item, Array.Empty<string>());
                else Add(prefix + "." + property.Name, item, prefix);
            }
        }
        private T Read<T>(string prefix) where T : new()
        {
            T value = new T(); ReadInto(prefix, value); return value;
        }
        private void ReadInto(string prefix, object value)
        {
            foreach (PropertyInfo property in value.GetType().GetProperties().Where(p => p.CanWrite))
            {
                string key = prefix + "." + property.Name;
                if (property.PropertyType == typeof(Futures2Region)) { ReadInto(key, property.GetValue(value)); continue; }
                if (!_settings.TryGetValue(key, out IIStrategyParameter parameter)) continue;
                object parsed = parameter switch
                {
                    StrategyParameterBool boolean => boolean.ValueBool,
                    StrategyParameterInt integer => integer.ValueInt,
                    StrategyParameterDecimal number => number.ValueDecimal,
                    StrategyParameterString text => property.PropertyType.IsEnum ? Enum.Parse(property.PropertyType, text.ValueString) : text.ValueString.Replace("|", "\n"),
                    _ => throw new InvalidOperationException("Unsupported parameter type.")
                };
                property.SetValue(value, parsed);
            }
        }
        private bool B(string name) => ((StrategyParameterBool)_settings[name]).ValueBool;
        private int I(string name) => ((StrategyParameterInt)_settings[name]).ValueInt;
        private decimal D(string name) => ((StrategyParameterDecimal)_settings[name]).ValueDecimal;
        private string S(string name) => ((StrategyParameterString)_settings[name]).ValueString;
        private void Button(string label, Action command, string group)
        {
            StrategyParameterButton button = CreateParameterButton(label, group);
            button.UserClickOnButtonEvent += () => Run(command);
            _buttons.Add(button);
        }
        private void Run(Action command)
        {
            if (_disposed) return;
            lock (_adapter.Sync)
            {
                if (_disposed) return;
                try { command(); _adapter.Save(); _adapter.Pump(); }
                catch (Exception error) { WriteLog(error.ToString()); }
            }
        }
        private void WriteLog(string message) => SendNewLogMessage(message, LogMessageType.System);
        private void AdjustInventory(Futures2InventoryKind kind)
        {
            UpdateAdapterSettings();
            Futures2Plan plan = SelectedPlan();
            int selected = I("Selected level");
            string levels = S("Inventory levels").Trim();
            string executedAt = S("External execution time").Trim();
            _adapter.AdjustInventory(new Futures2InventoryRequest { Id = S("Inventory operation id").Trim(),
                PlanId = plan.Id, Endpoint = I("Inventory endpoint"), Kind = kind,
                Levels = Futures2Commands.SelectLevels(plan, levels, selected),
                Quantity = D("Inventory target or execution quantity"), Price = B("Inventory price known") ? D("Inventory price") : null,
                UseLevelPrices = B("Inventory use level prices"),
                ExecutedAt = executedAt.Length == 0 ? null : DateTime.ParseExact(executedAt, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                ExecutionReference = S("External execution reference").Trim(), Fee = D("External execution fee") });
            ((StrategyParameterString)_settings["Regime"]).ValueString = "Off";
        }
        private Futures2Plan Active() => _adapter.Engine.Data.ActivePlan.Length == 0 ? throw new InvalidOperationException("Apply a plan first.") : _adapter.Engine.Data.Plans[_adapter.Engine.Data.ActivePlan];
        private Futures2Plan SelectedPlan() => S("Selected plan id").Trim().Length == 0 ? Active()
            : _adapter.Engine.Data.Plans.TryGetValue(S("Selected plan id").Trim(), out Futures2Plan plan) ? plan
            : throw new ArgumentException("Unknown selected plan id.");
        private Futures2Plan Plan(string prefix, int endpoint)
        {
            if (endpoint < 0 || endpoint > 1 || _tabs[endpoint].Security == null) throw new InvalidOperationException("Configure the selected native tab and instrument first.");
            Futures2PlanInput input = Read<Futures2PlanInput>(prefix);
            input.Instrument = _tabs[endpoint].Security.Name; input.Tick = _tabs[endpoint].Security.PriceStep;
            Futures2Plan plan = Futures2Plan.Build(input);
            Security security = _tabs[endpoint].Security;
            if (security.DecimalsVolume < 0 || security.DecimalsVolume > 28
                || decimal.Round(input.VolumeStep, security.DecimalsVolume) != input.VolumeStep
                || security.VolumeStep > 0 && input.VolumeStep % security.VolumeStep != 0
                || plan.Levels.Any(l => !SignedPriceMath.IsNativeVolume(l.Volume, security)))
                throw new ArgumentException("Plan quantity profile does not match native contract metadata.");
            plan.EndpointIdentity = _adapter.EndpointIdentity(endpoint); plan.PaperExecution = _adapter.IsPaper(endpoint);
            return plan;
        }
        private void Configure(Futures2Plan plan) => _adapter.Engine.Configure(plan, Read<Futures2Policy>("Policy"), D("Capital") + _adapter.Engine.Data.FundingDelta, _adapter.Engine.Data.Endpoint);
        private void ApplyConfiguration()
        {
            if (ReplacementRunning()) throw new InvalidOperationException("Finish or cancel replacement before changing its grid.");
            Futures2Plan plan = Plan("Grid", _adapter.Engine.Data.Endpoint);
            Configure(plan); _configuration = Fingerprint(); _gridConfiguration = GridFingerprint(); _manualCapital = D("Capital");
        }
        private string GridFingerprint() => System.Text.Json.JsonSerializer.Serialize(Read<Futures2PlanInput>("Grid"));
        private string Fingerprint() => System.Text.Json.JsonSerializer.Serialize(new { Input = Read<Futures2PlanInput>("Grid"), Policy = Read<Futures2Policy>("Policy"), Capital = D("Capital") });
        private void Start()
        {
            if (!B("Signed order capability selected")) throw new InvalidOperationException("Select the instrument order capability after verifying the account/venue profile.");
            if (_adapter.Engine.Data.ActivePlan.Length == 0) ApplyConfiguration();
            _adapter.Engine.Start(_adapter.CanConfirmNativeState, _adapter.Now);
            ((StrategyParameterString)_settings["Regime"]).ValueString = "On";
        }
        private void SettingsChanged()
        {
            Run(() =>
            {
                UpdateAdapterSettings();
                if (_adapter.Engine.Data.ActivePlan.Length > 0 && Fingerprint() != _configuration)
                {
                    if (GridFingerprint() != _gridConfiguration) ApplyConfiguration();
                    else
                    {
                        decimal capital = D("Capital") == _manualCapital ? _adapter.Engine.Data.Capital : D("Capital") + _adapter.Engine.Data.FundingDelta;
                        _adapter.Engine.Configure(_adapter.Engine.Data.PendingPlan ?? Active(), Read<Futures2Policy>("Policy"), capital,
                            _adapter.Engine.Data.PendingPlan != null ? _adapter.Engine.Data.PendingEndpoint : _adapter.Engine.Data.Endpoint);
                        _manualCapital = D("Capital"); _configuration = Fingerprint();
                    }
                }
                if (S("Regime") == "Off") _adapter.Engine.Pause();
                else if (_adapter.CanConfirmNativeState && B("Signed order capability selected"))
                {
                    if (_adapter.Engine.Data.PendingPlan != null) _adapter.Engine.Data.ResumeAfterConfigure = true;
                    else _adapter.Engine.Start(true, _adapter.Now);
                }
                else if (_adapter.Engine.Data.PendingPlan != null)
                {
                    _adapter.Engine.Data.ResumeAfterConfigure = false;
                }
            });
        }
        private void UpdateAdapterSettings()
        {
            RecordCoordinationUse();
            _adapter.SignedOrdersEnabled = B("Signed order capability selected");
            _adapter.UsePortfolioFunds = B("Use portfolio available funds");
            _adapter.ManualFreeFunds = _adapter.UsePortfolioFunds ? null : Math.Max(0, D("Manual available funds"));
            _adapter.ExternalNet[0] = D("External net endpoint 0"); _adapter.ExternalNet[1] = D("External net endpoint 1");
            _adapter.RequirePortfolioMark = S("Portfolio participants").Trim().Length > 0;
        }
        private void RecordCoordinationUse()
        {
            if (_adapter.Engine.Data.CoordinationUsed != true && (B("Accept coordination")
                || B("Portfolio leader") || S("Portfolio participants").Trim().Length > 0))
            {
                _adapter.Engine.Data.CoordinationUsed = true;
                _adapter.Save(); // History must be durable before any consenting peer publication.
            }
        }
        private void RequestEmptyRemoval()
        {
            _emptyRemovalStatus = _adapter.EmptyRemovalBlockReason;
            if (_emptyRemovalStatus.Length == 0)
                _emptyRemovalStatus = RequestAutomaticDeletion() ? "Native owner request queued; final recheck pending" : "Native owner unavailable";
        }

        /// <summary>Revalidates the live empty-stop request and durably stops before native owner deletion.</summary>
        /// <returns>True only after adapter, timer and robot callbacks are quiesced; otherwise retains the robot.</returns>
        /// <remarks>Called on the owner UI dispatcher. Sync serializes with commands and callbacks; current
        /// settings must still match the accepted configuration. Save failures propagate to the owner and
        /// forbid destruction. Tester/Optimizer and unknown participation history are refused. THG-EMPTY-012.</remarks>
        public override bool TryPrepareForAutomaticDeletion()
        {
            lock (_adapter.Sync)
            {
                if (_disposed || !_live) return false;
                UpdateAdapterSettings();
                if (Fingerprint() != _configuration) { _emptyRemovalStatus = "Unapplied settings changed"; return false; }
                _emptyRemovalStatus = _adapter.EmptyRemovalBlockReason;
                if (!_adapter.TryPrepareEmptyRemoval()) return false;
                Deleted();
                return true;
            }
        }
        private void BeforeDecision()
        {
            if (_disposed) return;
            AttachReplay(); UpdateAdapterSettings();
            if (!_live && S("Regime") == "On" && _adapter.Engine.Data.State == Futures2State.Draft && _adapter.Quotes.Count > 0)
            { ApplyConfiguration(); _adapter.Reconcile(false); Start(); }
            if (_adapter.Engine.Data.ActivePlan.Length == 0) return;
            Coordinate();
            RequestEmptyRemoval();
            Futures2Rollover rollover = _adapter.Engine.Data.Rollover;
            if (rollover?.State == "Draining" && _adapter.Engine.Data.Book.Intents.All(i => !i.CanFill) && _adapter.CanConfirmNativeState)
            {
                _adapter.Reconcile(B("Owner verified account and orders"), false);
                rollover.Capture(_adapter.Engine.Data);
                _adapter.Engine.Reduce(0, "Replacement: drain old contract"); rollover.State = "Reducing"; _adapter.Save();
            }
            if (rollover?.State == "Reducing" && _adapter.Engine.Data.Book.LocallyEmpty && _adapter.CanConfirmNativeState)
            {
                _adapter.Reconcile(B("Owner verified account and orders"), false);
                rollover.CarryBasis(_adapter.Engine.Data);
                _adapter.Engine.Data.Reducing = false; _adapter.Engine.Data.RecoverReduction = false;
                _adapter.Engine.Data.TransferId = "";
                _adapter.Engine.Configure(rollover.Plan, Read<Futures2Policy>("Policy"), _adapter.Engine.Data.Capital, rollover.Endpoint);
                _adapter.Engine.Data.ResumeAfterConfigure = true; rollover.State = "Entering"; _adapter.Save();
            }
        }
        private List<int> Selection(Futures2Plan plan) => Futures2Commands.SelectLevels(plan, S("Selected levels"), I("Selected level"));
        private void EditLevel(bool markup, bool entry, bool exit)
        {
            Futures2Plan selected = SelectedPlan();
            Futures2Commands.EditLevels(_adapter.Engine.Data, selected.Id, Selection(selected),
                markup ? D("Selected markup") : null, entry ? B("Selected entry enabled") : null, exit ? B("Selected exit enabled") : null);
        }
        private void Widen()
        {
            if (!_adapter.Quotes.TryGetValue(_adapter.Engine.Data.Endpoint, out Futures2Quote quote) || !quote.Valid(_adapter.Now, _adapter.Engine.Data.Policy.FreshnessSeconds)) throw new InvalidOperationException("Fresh quote required.");
            Configure(Futures2Commands.Widen(Active(), quote.Bid, quote.Ask));
        }
        private void PrepareRollover()
        {
            if (ReplacementRunning()) throw new InvalidOperationException("Cancel running replacement before preparing another.");
            int endpoint = I("Replacement endpoint");
            if (endpoint == _adapter.Engine.Data.Endpoint) throw new ArgumentException("Replacement must use the other native tab.");
            Futures2Plan plan = Plan("Replacement", endpoint);
            if (B("Replacement shift by spread"))
            {
                if (!_adapter.Quotes.TryGetValue(_adapter.Engine.Data.Endpoint, out Futures2Quote oldQuote) || !oldQuote.Valid(_adapter.Now, _adapter.Engine.Data.Policy.FreshnessSeconds)
                    || !_adapter.Quotes.TryGetValue(endpoint, out Futures2Quote newQuote) || !newQuote.Valid(_adapter.Now, _adapter.Engine.Data.Policy.FreshnessSeconds))
                    throw new InvalidOperationException("Fresh quotes on both contracts are required for the spread shift.");
                decimal spread = checked((newQuote.Bid - oldQuote.Bid + newQuote.Ask - oldQuote.Ask) / 2);
                Futures2PlanInput input = Futures2Commands.Copy(plan.Input);
                input.Low = SignedPriceMath.Quantize(Active().Input.Low + spread, input.Tick, false);
                input.High = SignedPriceMath.Quantize(Active().Input.High + spread, input.Tick, true);
                if (input.LowerStopEnabled) input.LowerStop = SignedPriceMath.Quantize(input.Low - (plan.Input.Low - plan.Input.LowerStop), input.Tick, false);
                if (input.UpperStopEnabled) input.UpperStop = SignedPriceMath.Quantize(input.High + (plan.Input.UpperStop - plan.Input.High), input.Tick, true);
                Futures2Plan shifted = Futures2Plan.Build(input); shifted.EndpointIdentity = plan.EndpointIdentity; shifted.PaperExecution = plan.PaperExecution; plan = shifted;
            }
            if (plan.Input.Currency != Active().Input.Currency || !plan.SameOrientation(Active())) throw new ArgumentException("Replacement currency/logical direction/hedge mode must match.");
            if (plan.Reserved > _adapter.Engine.Data.Capital) throw new ArgumentException("Replacement exceeds campaign capital.");
            _adapter.ValidateEmptyEndpoint(endpoint, plan, false);
            _adapter.Engine.Data.Rollover = new Futures2Rollover { Plan = plan, Endpoint = endpoint };
        }
        private void StartRollover()
        {
            if (_adapter.Engine.Data.Rollover?.State != "Prepared") throw new InvalidOperationException("Prepare replacement first.");
            if (_adapter.Engine.Data.Transfers.Any(t => !t.Completed && !t.Canceled)) throw new InvalidOperationException("Finish/cancel collateral transfer first.");
            _adapter.ValidateEmptyEndpoint(_adapter.Engine.Data.Rollover.Endpoint, _adapter.Engine.Data.Rollover.Plan, true);
            _adapter.Engine.Data.Rollover.State = "Draining"; _adapter.Engine.Pause(); _adapter.Engine.Data.CancelAll = true;
        }
        private bool ReplacementRunning() => _adapter.Engine.Data.Rollover?.State is "Draining" or "Reducing" or "Entering";
        private void CancelRollover()
        {
            if (_adapter.Engine.Data.Rollover == null) return;
            _adapter.Engine.Data.Rollover.State = "Canceled"; _adapter.Engine.Data.Reducing = false;
            _adapter.Engine.Data.CancelAll = true; _adapter.Engine.Pause();
        }
        private void NewCampaign()
        {
            _adapter.Reconcile(B("Owner verified account and orders"));
            if (!_adapter.Engine.Data.Book.LocallyEmpty || _adapter.Engine.Data.Transfers.Any(t => t.Debited > 0 && !t.Completed))
                throw new InvalidOperationException("Confirmed flat and settled transfers required.");
            // Preserve historical IDs/fill dedup; clearing an emergency rearms only explicitly confirmed empty ownership.
            _adapter.Engine.Data.Emergency = false; _adapter.Engine.Data.HadInventory = false;
            _adapter.Engine.Data.ManualEntry = _adapter.Engine.Data.ManualExit = null;
            _adapter.Engine.Data.EntryBatch = null; _adapter.Engine.Data.SelectedCancels.Clear();
            _adapter.Engine.Data.ManualExitLots.Clear(); _adapter.Engine.Data.Reducing = false; _adapter.Engine.Data.RecoverReduction = false;
            _adapter.Engine.Data.Trail.Reset(_adapter.Now); _adapter.Engine.Data.Rollover = null;
            _adapter.Engine.Data.State = Futures2State.Ready; _adapter.Engine.Data.Reason = "Explicitly rearmed after confirmed flat";
        }

        private string Scope => _live ? "Live/" : _optimizer != null ? "Optimizer" + _optimizer.NumberServer + "/" : "Tester/";
        private string[] Participants() => S("Portfolio participants").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(n => Scope + n).Distinct().ToArray();
        private Futures2HelperScope PortfolioScope() => !_adapter.RequirePortfolioMark || _adapter.Engine.Data.ActivePlan.Length == 0 ? null
            : Futures2Coordination.Helper(Participants(), Active().Input.Currency, _adapter.Now, _adapter.Engine.Data.Policy.FreshnessSeconds);
        private void Coordinate()
        {
            RecordCoordinationUse();
            Futures2Checkpoint data = _adapter.Engine.Data;
            string key = Scope + _identity;
            if (_peerKey != key) { if (_peerKey.Length > 0) Futures2Coordination.Remove(_peerKey); _peerKey = key; }
            decimal? profit = _adapter.Quotes.TryGetValue(data.Endpoint, out Futures2Quote activeQuote) && activeQuote.Valid(_adapter.Now, data.Policy.FreshnessSeconds)
                && data.Book.Lots.Where(l => l.Quantity > 0).All(l => _adapter.Quotes.TryGetValue(l.Endpoint, out Futures2Quote quote) && quote.Valid(_adapter.Now, data.Policy.FreshnessSeconds))
                ? _adapter.Engine.ProfitPercent(_adapter.Quotes) * data.ReturnBase / 100 : null;
            Futures2Coordination.Publish(new Futures2Peer { Name = key, Currency = Active().Input.Currency, Capital = data.Capital, ReturnBase = data.ReturnBase,
                Profit = profit, Held = data.Book.Lots.Sum(l => l.Quantity * data.Plans[l.PlanId].Input.Collateral), Time = _adapter.Now,
                AcceptCoordination = B("Accept coordination"), Receipts = data.Receipts, GroupReceipts = data.GroupReceipts,
                CanEnter = data.State == Futures2State.Active && _adapter.CanConfirmNativeState && !data.Policy.ForbidEntries,
                FirstEntry = data.Book.Lots.Where(l => l.Quantity > 0).Select(l => (DateTime?)l.Opened).DefaultIfEmpty(null).Min(),
                Created = data.Trail.Started ?? _adapter.Now, WasActivity = data.Book.Lots.Count > 0, HasPending = data.Book.Intents.Any(i => i.CanFill),
                Spent = data.Receipts.ToDictionary(r => r.Key, r => data.Book.Intents.Where(i => i.Entry && i.TransferBudgets.ContainsKey(r.Key))
                    .Sum(i => i.TransferBudgets[r.Key] * i.Filled / i.Quantity)) });
            for (int count = 0; count < 16 && Futures2Coordination.Receive(key, out Futures2PeerCommand command); count++)
            {
                if (!B("Accept coordination") || command.Currency != Active().Input.Currency || data.Emergency) continue;
                if (command.Reduce)
                {
                    if (data.GroupReceipts.Add(command.Id))
                    {
                        string source = command.Source + "/" + command.SourceCampaign;
                        long previous = data.GroupVersions.TryGetValue(source, out long version) ? version : 0;
                        if (command.Sequence > previous)
                        {
                            data.GroupVersions[source] = command.Sequence;
                            if (command.Revoke) { data.RecoverReduction = true; data.Policy.ForbidEntries = true; }
                            else _adapter.Engine.Reduce(command.Amount, "Portfolio reduction from " + command.Source);
                        }
                        _adapter.Save();
                    }
                }
                else
                {
                    decimal accepted = data.Receipts.TryGetValue(command.Id, out decimal value) ? value : 0;
                    if (command.Amount <= accepted) continue;
                    decimal delta = command.Amount - accepted;
                    Futures2Plan fundingPlan = data.PendingPlan ?? Active();
                    Futures2PlanInput input = Futures2Commands.Copy(fundingPlan.Input); input.Budget = checked(input.Budget + delta);
                    input.FixedVolume = 0;
                    Futures2Plan replacement = Futures2Plan.Build(input);
                    replacement.EndpointIdentity = fundingPlan.EndpointIdentity; replacement.PaperExecution = fundingPlan.PaperExecution;
                    decimal capital = checked(data.Capital + delta);
                    // The durable monetary credit is independent of pending grid publication.
                    _adapter.Engine.Configure(replacement, data.PendingPolicy ?? data.Policy, capital, data.PendingPlan != null ? data.PendingEndpoint : data.Endpoint);
                    data.Capital = capital; data.FundingDelta = checked(data.FundingDelta + delta);
                    data.Receipts[command.Id] = command.Amount; _adapter.Save();
                }
            }
            foreach (Futures2Transfer transfer in data.Transfers.Where(t => !t.Completed))
            {
                decimal released = Math.Min(transfer.Canceled ? transfer.Debited : transfer.Requested, data.Book.Intents.Where(i => !i.Entry && i.Protective && i.TransferId == transfer.Id)
                    .Sum(i => i.Filled * data.Plans[i.PlanId].Input.Collateral));
                if (released > transfer.Debited)
                {
                    decimal debit = released - transfer.Debited;
                    if (data.Capital - debit < 0 || data.Capital - debit < data.Book.Reserved(data.Plans)) throw new InvalidOperationException("Transfer debit would violate remaining source reserve.");
                    data.Capital -= debit; data.FundingDelta -= debit; transfer.Debited = released;
                    if (data.PendingPlan != null) data.PendingCapital -= debit;
                    if (data.Capital == 0) { data.State = Futures2State.Stopped; data.Reason = "All source collateral transferred"; }
                    _adapter.Save();
                }
                Futures2Peer destination = Futures2Coordination.Get(transfer.Destination);
                decimal received = destination != null && destination.Receipts.TryGetValue(transfer.Id, out decimal receipt) ? receipt : 0;
                decimal spent = destination != null && destination.Spent.TryGetValue(transfer.Id, out decimal entered) ? entered : 0;
                if (received >= transfer.Requested && spent >= transfer.Requested || transfer.Canceled && received >= transfer.Debited) { transfer.Completed = true; _adapter.Save(); continue; }
                if (transfer.Debited > received) Futures2Coordination.Send(transfer.Destination, new Futures2PeerCommand
                    { Id = transfer.Id, Source = key, Currency = transfer.Currency, Amount = transfer.Debited });
            }
            if (B("Portfolio leader") && data.Trail.HasStopped && data.Trail.Closing != data.GroupClosing)
            {
                data.GroupClosing = data.Trail.Closing;
                if (data.GroupClosing) ReducePortfolio();
                else
                {
                    long sequence = checked(++data.GroupSequence);
                    foreach (string participant in Participants()) data.GroupCommands[participant] = new Futures2PeerCommand
                        { Id = Guid.NewGuid().ToString("N"), Source = key, SourceCampaign = data.Campaign, Sequence = sequence,
                            Currency = Active().Input.Currency, Reduce = true, Revoke = true };
                    _adapter.Save();
                }
            }
            foreach (KeyValuePair<string, Futures2PeerCommand> request in data.GroupCommands.ToArray())
            {
                Futures2Peer peer = Futures2Coordination.Get(request.Key);
                if (peer != null && peer.GroupReceipts.Contains(request.Value.Id)) { data.GroupCommands.Remove(request.Key); _adapter.Save(); }
                else Futures2Coordination.Send(request.Key, request.Value);
            }
        }
        private void StartTransfer()
        {
            Futures2Checkpoint data = _adapter.Engine.Data;
            string destination = Scope + S("Transfer destination").Trim();
            Futures2Peer peer = Futures2Coordination.Get(destination);
            decimal amount = D("Transfer collateral");
            if (peer == null || !peer.Profit.HasValue || peer.Time > _adapter.Now || _adapter.Now - peer.Time > TimeSpan.FromSeconds(data.Policy.FreshnessSeconds)
                || !peer.AcceptCoordination || !peer.CanEnter || peer.Currency != Active().Input.Currency || destination == _peerKey || amount <= 0
                || amount > data.Capital || data.Transfers.Any(t => !t.Completed && !t.Canceled) || ReplacementRunning())
                throw new InvalidOperationException("Invalid or unavailable transfer participant/amount.");
            decimal held = data.Book.Lots.Sum(l => l.Quantity * data.Plans[l.PlanId].Input.Collateral);
            if (amount > held) throw new ArgumentException("Transfer must be funded by actual source inventory reductions.");
            decimal remainder = amount;
            foreach (Futures2Lot lot in data.Book.Lots.Where(l => l.Quantity > 0))
            {
                Futures2Plan plan = data.Plans[lot.PlanId];
                decimal volume = Math.Min(lot.Quantity, decimal.Floor(remainder / plan.Input.Collateral / plan.Input.VolumeStep) * plan.Input.VolumeStep);
                if (volume >= plan.Input.MinimumVolume) remainder -= volume * plan.Input.Collateral;
            }
            if (remainder != 0) throw new ArgumentException("Transfer collateral must map exactly to admissible source quantities.");
            Futures2Transfer transfer = new Futures2Transfer { Destination = destination, Currency = Active().Input.Currency, Requested = amount };
            data.Transfers.Add(transfer); data.TransferId = transfer.Id;
            _adapter.Engine.Reduce(held - amount, "Collateral transfer to " + destination, transfer.Id);
        }
        private void CancelTransfer()
        {
            foreach (Futures2Transfer transfer in _adapter.Engine.Data.Transfers.Where(t => !t.Completed)) transfer.Canceled = true;
            _adapter.Engine.Data.Reducing = false; _adapter.Engine.Data.CancelAll = true; _adapter.Engine.Pause();
        }
        private void ReducePortfolio()
        {
            decimal fraction = D("Portfolio retain fraction");
            if (fraction < 0 || fraction > 1) throw new ArgumentException("Retain fraction must be between zero and one.");
            string id = _adapter.Engine.Data.GroupReductionId = Guid.NewGuid().ToString("N");
            Futures2Peer[] selected = Participants().Select(Futures2Coordination.Get).ToArray();
            if (selected.Length == 0 || selected.Any(p => p == null || !p.AcceptCoordination || p.Currency != Active().Input.Currency))
                throw new InvalidOperationException("Select available consenting portfolio participants.");
            decimal high = selected.Max(p => p.Held);
            decimal retained = Math.Max(0, high - Math.Max(high - selected.Min(p => p.Held), _adapter.Engine.Data.Policy.PieVolume));
            long sequence = checked(++_adapter.Engine.Data.GroupSequence);
            foreach (string participant in Participants())
            {
                Futures2Peer peer = Futures2Coordination.Get(participant);
                if (peer == null || !peer.AcceptCoordination || peer.Currency != Active().Input.Currency) throw new InvalidOperationException("Portfolio participant unavailable: " + participant);
                _adapter.Engine.Data.GroupCommands[participant] = new Futures2PeerCommand { Id = id, Source = _peerKey, Currency = peer.Currency,
                    SourceCampaign = _adapter.Engine.Data.Campaign, Sequence = sequence,
                    Reduce = true, Amount = _adapter.Engine.Data.Policy.EqualizeVolumes ? retained : peer.Held * fraction };
            }
        }

        private void SecurityChanged(Security security) { lock (_adapter.Sync) AttachReplay(); }
        private void AttachReplay()
        {
            if (_live) return;
            TesterServer tester = _tabs[0].Connector?.MyServer as TesterServer;
            OptimizerServer optimizer = _tabs[0].Connector?.MyServer as OptimizerServer;
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
        private void ReplayStarted() { if (!_disposed) { _adapter.ResetSimulation(); _configuration = ""; } }
        private void ReplayTime(DateTime time) { if (!_disposed) _adapter.AdvanceReplay(time); }

        /// <summary>Shows read-only plan/order/ownership diagnostics through the native robot settings entry.</summary>
        public override void ShowIndividualSettingsDialog()
        {
            if (Application.Current == null) return;
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_window != null) { _window.Activate(); return; }
                TextBox text = new TextBox { IsReadOnly = true, FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
                _window = new Window { Title = "Futures2Grid — " + _identity, Width = 1050, Height = 700, Content = text };
                _windowTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
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
                text.Append(_adapter.DescribeNativeState());
                text.AppendLine("Empty removal: " + _emptyRemovalStatus + " | coordination history=" + (data.CoordinationUsed?.ToString() ?? "unknown"));
                if (data.ActivePlan.Length > 0)
                {
                    Futures2Plan accepted = data.Plans[data.ActivePlan];
                    text.AppendLine("Accepted logical grid=" + accepted.Input.Direction + " | hedge=" + accepted.Input.IsHedge
                        + " | actual inventory=" + (accepted.IsLongInventory ? "Long/Buy" : "Short/Sell"));
                    if (accepted.Input.IsHedge) text.AppendLine("Hedge keeps logical targets: ordinary exit can realize a loss.");
                }
                text.AppendLine("Campaign " + data.Campaign + " | active plan " + data.ActivePlan + " | pending " + data.PendingPlan?.Id);
                text.AppendLine("Accepted execution: ascending level priority=" + data.Policy.AscendingLevelPriority
                    + " | ordinary quote limits=" + data.Policy.QuoteOrdinaryLimits);
                text.AppendLine("Capital " + data.Capital + " | daily spent " + data.Book.DaySpent + " | realized " + data.Book.Realized);
                foreach (Futures2InventoryOperation operation in data.InventoryOperations.TakeLast(10))
                    text.AppendLine("Inventory " + operation.Request.Id + " " + operation.Request.Kind + " "
                        + (operation.Committed ? "Committed" : "Prepared") + " revision " + operation.Revision);
                text.AppendLine("HJ " + (data.Hj.Active ? data.Hj.From + " … " + data.Hj.To : "inactive") + " | helper " + data.Trail.Cause);
                text.AppendLine("Commands use Selected levels CSV or Selected level fallback (-1=all). Inventory has its own CSV.");
                if (data.EntryBatch != null) text.AppendLine("Pending selected entries: plan=" + data.EntryBatch.Plan + " levels=" + string.Join(",", data.EntryBatch.Levels));
                text.AppendLine("Pending selected cancels: " + string.Join(",", data.SelectedCancels));
                Futures2Plan plan;
                try { plan = Plan("Grid", data.Endpoint); text.AppendLine("Preview: " + plan.Levels.Count + " levels; step " + plan.IdealStep + "; reserve " + plan.Reserved); }
                catch (Exception error) { text.AppendLine("Preview rejected: " + error.Message); plan = data.ActivePlan.Length > 0 ? Active() : null; }
                if (plan != null) foreach (Futures2Level level in plan.Levels)
                    text.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,4} price {1,16:0.00000} qty {2,10} markup {3:0.00000}", level.Id, level.Price, level.Volume, level.Markup));
                text.AppendLine("Native intents (last 100):");
                foreach (Futures2Intent intent in data.Book.Intents.TakeLast(100))
                    text.AppendLine(intent.Id + " ep=" + intent.Endpoint + " " + (intent.Entry ? "ENTRY" : "EXIT") + " " + intent.State
                        + " " + intent.Filled + "/" + intent.Quantity + " price=" + intent.Price.ToString("0.00000", CultureInfo.InvariantCulture)
                        + " levels=" + string.Join(",", intent.Allocations.Select(a => a.LevelId)) + " remaining=" + intent.Remaining + " reserved collateral=" + (intent.Entry ? intent.Remaining * data.Plans[intent.PlanId].Input.Collateral : 0)
                        + " pos=" + intent.PositionNumber + " order=" + intent.OrderNumber + " market=" + intent.MarketNumber);
                foreach (Futures2Lot lot in data.Book.Lots.Where(l => l.Quantity > 0)) text.AppendLine("Held plan=" + lot.PlanId + " level=" + lot.LevelId + " qty=" + lot.Quantity + " average=" + lot.Average);
                foreach (Futures2Transfer transfer in data.Transfers) text.AppendLine("Transfer " + transfer.Id + " " + transfer.Debited + "/" + transfer.Requested + " completed=" + transfer.Completed + " canceled=" + transfer.Canceled);
                if (data.Rollover != null) text.AppendLine("Replacement " + data.Rollover.State + " endpoint=" + data.Rollover.Endpoint);
                return text.ToString();
            }
        }
        private void Deleted()
        {
            if (_disposed) return; _disposed = true; _timer?.Dispose(); DetachReplay();
            foreach (BotTabSimple tab in _tabs) tab.SecuritySubscribeEvent -= SecurityChanged;
            ParametrsChangeByUser -= SettingsChanged; DeleteEvent -= Deleted; DeletingEvent -= Deleted;
            _adapter.Dispose(); Futures2Coordination.Remove(_peerKey);
            if (_window != null) _window.Dispatcher.BeginInvoke(new Action(() => _window?.Close()));
        }
    }
}
