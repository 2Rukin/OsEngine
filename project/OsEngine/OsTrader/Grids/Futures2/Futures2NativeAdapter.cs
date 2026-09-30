/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text;
using OsEngine.Entity;
using OsEngine.Market.Connectors;
using OsEngine.Market.Servers;
using OsEngine.OsTrader.Panels.Tab;

namespace OsEngine.OsTrader.Grids.Futures2
{
    /// <summary>Serial native-journal adapter for two explicitly configured grid endpoints.</summary>
    /// <remarks>Does not create servers, change connection settings or infer live reconciliation from a
    /// locally empty journal. Restart/reconnect requires operator reconciliation. Before-send callback
    /// binds durable native IDs before connector reentrancy. All public methods serialize on Sync.</remarks>
    public sealed partial class Futures2NativeAdapter : IDisposable
    {
        /// <summary>Shared owner lock for commands, callbacks and timer passes.</summary>
        public object Sync { get; } = new object();
        /// <summary>Current deterministic engine, accessed under Sync.</summary>
        public Futures2Controller Engine { get; private set; }
        /// <summary>Last quote per native endpoint, accessed under Sync.</summary>
        public Dictionary<int, Futures2Quote> Quotes { get; } = new Dictionary<int, Futures2Quote>();
        /// <summary>Last accepted reconciliation; current account agreement must also pass CanConfirmNativeState.</summary>
        public bool Reconciled { get; private set; }
        /// <summary>Current native/account agreement for an automatic barrier; missing receipts wait without faulting a rollover.</summary>
        public bool CanConfirmNativeState { get { lock (Sync) return !_disposed && !_persistenceFailed && Reconciled && AccountMatches(); } }
        /// <summary>Owner's explicit instrument capability selection; not a venue certification.</summary>
        public bool SignedOrdersEnabled { get; set; }
        /// <summary>Fixed account-external net quantities acknowledged by the operator, per endpoint.</summary>
        public decimal[] ExternalNet { get; } = new decimal[2];
        /// <summary>Callback for simulation initialization and completed input processing, inside Sync.</summary>
        public Action BeforeDecision { get; set; }
        /// <summary>Optional coordinated portfolio return provider, inside Sync.</summary>
        public Func<decimal?> PortfolioReturn { get; set; }
        /// <summary>Fresh selected-portfolio inventory and timer scope; null suppresses the group helper.</summary>
        public Func<Futures2HelperScope> PortfolioScope { get; set; }
        /// <summary>Selected portfolio scope must have a fresh aggregate mark before its helper is evaluated.</summary>
        public bool RequirePortfolioMark { get; set; }
        /// <summary>Owner-approved interpretation of Portfolio.ValueCurrent minus ValueBlocked as available funds.</summary>
        public bool UsePortfolioFunds { get; set; }
        /// <summary>Alternative manually declared available-funds envelope, still bounded by campaign capital.</summary>
        public decimal? ManualFreeFunds { get; set; }

        private readonly BotTabSimple[] _tabs;
        private readonly ConnectorCandles[] _connectors;
        private readonly IExplicitQuoteSource[] _sources = new IExplicitQuoteSource[2];
        private readonly IServer[] _servers = new IServer[2];
        private readonly Action<string>[] _statusHandlers = new Action<string>[2];
        private readonly string[] _serverStatus = new string[2];
        private readonly Action<ExplicitAccount>[] _explicitAccountHandlers = new Action<ExplicitAccount>[2];
        private readonly Action<ExplicitQuote>[] _quoteHandlers = new Action<ExplicitQuote>[2];
        private readonly Action<bool>[] _paperHandlers = new Action<bool>[2];
        private readonly Action<Order>[] _orderHandlers = new Action<Order>[2];
        private readonly Action<MyTrade>[] _fillHandlers = new Action<MyTrade>[2];
        private readonly Action<Portfolio>[] _accountHandlers = new Action<Portfolio>[2];
        private readonly Action<string, TimeFrame, TimeSpan, string, string>[] _reconnectHandlers = new Action<string, TimeFrame, TimeSpan, string, string>[2];
        private readonly DateTime[] _accountAt = new DateTime[2];
        private readonly DateTime[] _fundsAt = new DateTime[2];
        private readonly DateTime[] _fillAt = new DateTime[2];
        private readonly long[] _accountSequence = new long[2];
        private readonly long[] _fillSequence = new long[2];
        private readonly decimal?[] _accountNet = new decimal?[2];
        private readonly bool[] _accountZeroAttested = new bool[2];
        private readonly decimal?[] _accountFunds = new decimal?[2];
        private long _sequence;
        private readonly string[] _sourceSession = new string[2];
        private readonly long[] _sourceAccountSequence = new long[2];
        private readonly long[] _sourceFundsSequence = new long[2];
        private readonly long[] _sourceQuoteSequence = new long[2];
        private readonly long[] _sourceFillSequence = new long[2];
        private readonly Action<string> _log;
        private readonly Futures2Store _store;
        private readonly bool _live;
        private bool _disposed;
        private bool _processing;
        private bool _persistenceFailed;
        private bool _awaitingFillDetails;
        private Futures2State _stateBeforeDetails;
        private DateTime _now;
        private string _lastReason = "";
        private string _lastAccountReason = "";
        private string _savedPayload;

        /// <summary>Loads projection and subscribes existing native tabs; never starts a server.</summary>
        public Futures2NativeAdapter(BotTabSimple first, BotTabSimple second, Futures2Store store, bool live, Action<string> log)
        {
            _tabs = new[] { first, second }; _connectors = _tabs.Select(t => t.Connector).ToArray();
            _store = store; _live = live; _log = log ?? throw new ArgumentNullException(nameof(log));
            Futures2Checkpoint recovered = store?.Load();
            Engine = new Futures2Controller(recovered ?? new Futures2Checkpoint { CoordinationUsed = false });
            if (recovered != null) Engine.Reconcile("Restart: reconcile native orders, fills and account");
            RecoverInventory(); // Fail before subscribing when the local ownership journal cannot be recovered.
            for (int endpoint = 0; endpoint < 2; endpoint++)
            {
                int index = endpoint;
                _quoteHandlers[index] = quote => OnQuote(index, quote);
                _paperHandlers[index] = _ => OnReconnect(index);
                _tabs[index].EmulatorIsOnChangeStateEvent += _paperHandlers[index];
                _orderHandlers[index] = order => OnOrder(index, order);
                _fillHandlers[index] = trade => OnFill(index, trade);
                _accountHandlers[index] = portfolio => OnAccount(index, portfolio);
                _statusHandlers[index] = status => { lock (Sync) { if (_disposed || _serverStatus[index] == status) return; _serverStatus[index] = status; OnReconnect(index); } };
                _explicitAccountHandlers[index] = account => OnExplicitAccount(index, account);
                _reconnectHandlers[index] = (security, frame, span, portfolio, server) => OnReconnect(index);
                _tabs[index].OrderUpdateEvent += _orderHandlers[index];
                _tabs[index].MyTradeEvent += _fillHandlers[index];
                _tabs[index].PortfolioOnExchangeChangedEvent += _accountHandlers[index];
                _connectors[index].ConnectorStartedReconnectEvent += _reconnectHandlers[index];
                _tabs[index].CandleUpdateEvent += OnCandles;
                _tabs[index].CandleFinishedEvent += OnCandles;
                _tabs[index].NewTickEvent += OnTick;
                _tabs[index].SecuritySubscribeEvent += OnSecurity;
            }
            AttachSources();
        }

        /// <summary>Decision clock: local receipt time live, source event time in simulations.</summary>
        public DateTime Now => _live ? DateTime.Now : _now;

        /// <summary>Explains why native owner removal is refused; empty means currently eligible, not deleted.</summary>
        /// <remarks>Reads under Sync. Requires a durable live, never-owned and never-coordinated campaign,
        /// an elapsed EmptyStop latch and current native/account evidence. Both native journals and local
        /// stop-openers must be empty. Recheck immediately before quiescence. THG-EMPTY-012.</remarks>
        public string EmptyRemovalBlockReason
        {
            get
            {
                lock (Sync)
                {
                    Futures2Checkpoint data = Engine.Data;
                    if (!_live) return "Tester/Optimizer retain robot results";
                    if (!data.Policy.RemoveEmptyRobot) return "RemoveEmptyRobot disabled";
                    if (_store == null) return "Durable checkpoint required";
                    if (_disposed || _persistenceFailed || _awaitingFillDetails) return "Adapter unavailable or unresolved persistence/fills";
                    if (data.ActivePlan.Length == 0 || !data.Plans.TryGetValue(data.ActivePlan, out Futures2Plan plan)) return "Accepted plan required";
                    if (!data.Policy.Trailing || data.Policy.EmptyStopMinutes <= 0 || !data.Trail.Disabled
                        || data.Trail.Cause != "Empty stop" || data.Trail.Closing || data.Trail.WasActivity
                        || !(data.Trail.Created ?? data.Trail.Started).HasValue
                        || Now - (data.Trail.Created ?? data.Trail.Started).Value <= TimeSpan.FromMinutes(data.Policy.EmptyStopMinutes))
                        return "Elapsed never-active EmptyStop required";
                    if (data.State != Futures2State.PausingEntries && data.State != Futures2State.PausedEntries) return "EmptyStop pause required";
                    if (data.CoordinationUsed != false) return "Coordination history used or unknown";
                    if (data.HadInventory || data.Book.Lots.Count != 0 || data.Book.Intents.Count != 0
                        || data.InventoryOperations.Count != 0 || data.Book.InventoryRevision != 0)
                        return "Campaign has ownership or order history";
                    if (data.PendingPlan != null || data.PendingPolicy != null || data.Emergency || data.Reducing || data.RecoverReduction
                        || data.ManualEntry.HasValue || data.ManualExit.HasValue || data.EntryBatch != null || data.LevelEdit != null
                        || data.ManualExitLots.Count > 0 || data.CancelAll || data.SelectedCancels.Count > 0 || data.Rollover != null)
                        return "Pending campaign command or recovery";
                    if (data.Transfers.Count > 0 || data.Receipts.Count > 0 || data.GroupReceipts.Count > 0
                        || data.GroupCommands.Count > 0 || data.GroupVersions.Count > 0 || data.GroupSequence != 0
                        || data.GroupClosing || data.GroupReductionId.Length > 0 || data.TransferId.Length > 0 || data.FundingDelta != 0)
                        return "Portfolio history or obligations";
                    for (int endpoint = 0; endpoint < 2; endpoint++)
                    {
                        if (_tabs[endpoint].PositionsAll == null || _tabs[endpoint].PositionsAll.Count != 0
                            || _tabs[endpoint].PositionOpenerToStopsAll == null || _tabs[endpoint].PositionOpenerToStopsAll.Count != 0)
                            return "Native journal or local stop-openers not confirmed empty";
                    }
                    string reason = NativeStateBlockReason;
                    if (reason.Length > 0) return reason;
                    if (plan.EndpointIdentity != EndpointIdentity(data.Endpoint) || plan.PaperExecution != IsPaper(data.Endpoint)
                        || _tabs[data.Endpoint].Security == null || _tabs[data.Endpoint].Security.Name != plan.Input.Instrument
                        || _tabs[data.Endpoint].Security.PriceStep != plan.Input.Tick)
                        return "Native execution profile changed";
                    if (!Quotes.TryGetValue(data.Endpoint, out Futures2Quote quote) || !quote.Valid(Now, data.Policy.FreshnessSeconds))
                        return "Fresh ready quote required";
                    return "";
                }
            }
        }

        /// <summary>Persists an eligible empty campaign as Stopped, then detaches adapter callbacks.</summary>
        /// <returns>False when any current guard refuses; true after durable stop and disposal.</returns>
        /// <remarks>The native owner still owns robot/tab deletion. Caller serializes its own settings,
        /// timer and command quiescence on Sync. Write failure throws before disposal and blocks removal;
        /// the retained checkpoint must be reconciled after restart. No order/cancel is sent. THG-EMPTY-012.</remarks>
        public bool TryPrepareEmptyRemoval()
        {
            lock (Sync)
            {
                if (EmptyRemovalBlockReason.Length != 0) return false;
                Engine.Data.State = Futures2State.Stopped;
                Engine.Data.Reason = "EmptyStop: prepared for native owner removal";
                Save();
                Dispose();
                return true;
            }
        }

        /// <summary>Saves authoritative intent knowledge; failure permanently blocks sends in this adapter.</summary>
        public void Save()
        {
            lock (Sync)
            {
                if (_persistenceFailed) throw new InvalidOperationException("Checkpoint storage is faulted; restart and reconcile.");
                try
                {
                    if (_store != null)
                    {
                        string payload = System.Text.Json.JsonSerializer.Serialize(Engine.Data);
                        if (payload != _savedPayload) { _store.Save(Engine.Data); _savedPayload = payload; }
                    }
                }
                catch (Exception error) { _persistenceFailed = true; Reconciled = false; Engine.Fault("Checkpoint write failed"); _log(error.ToString()); throw; }
            }
        }

        private void AttachSources()
        {
            for (int index = 0; index < 2; index++)
            {
                IServer server = _connectors[index].MyServer;
                if (!ReferenceEquals(server, _servers[index]))
                {
                    if (_servers[index] != null)
                    {
                        _servers[index].ConnectStatusChangeEvent -= _statusHandlers[index];
                        if (_servers[index] is IExplicitAccountSource priorAccount) priorAccount.ExplicitAccountEvent -= _explicitAccountHandlers[index];
                        OnReconnect(index);
                    }
                    _servers[index] = server;
                    if (server != null)
                    {
                        _serverStatus[index] = server.ServerStatus.ToString(); server.ConnectStatusChangeEvent += _statusHandlers[index];
                        if (server is IExplicitAccountSource accountSource) accountSource.ExplicitAccountEvent += _explicitAccountHandlers[index];
                    }
                }
                if (RequiresSignedTransport(index) && server is ISignedOrderSource signedSource)
                {
                    string session = signedSource.SignedOrderSession;
                    if (_sourceSession[index] != session)
                    {
                        _sourceSession[index] = session; OnReconnect(index);
                        _sourceAccountSequence[index] = _sourceFundsSequence[index] = _sourceQuoteSequence[index] = _sourceFillSequence[index] = 0;
                    }
                    foreach (Futures2Intent intent in Engine.Data.Book.Intents.Where(i => i.Endpoint == index && i.ClientKey.Length > 0))
                    {
                        Order native = OrderFor(intent);
                        if (native == null) continue;
                        if (native.SignedIdentity == null) native.SignedIdentity = new SignedOrderIdentity { ClientKey = intent.ClientKey };
                        if (native.SignedIdentity.ClientKey != intent.ClientKey) throw new InvalidOperationException("Persisted broker correlation mismatch.");
                        signedSource.RegisterSignedOrder(native);
                    }
                }
                IExplicitQuoteSource source = _connectors[index].MyServer as IExplicitQuoteSource;
                if (ReferenceEquals(source, _sources[index])) continue;
                if (_sources[index] != null) _sources[index].ExplicitQuoteEvent -= _quoteHandlers[index];
                _sources[index] = source;
                if (source != null) source.ExplicitQuoteEvent += _quoteHandlers[index];
            }
        }

        private bool RequiresSignedTransport(int endpoint) => _live && !IsPaper(endpoint)
            && _connectors[endpoint].ServerType == OsEngine.Market.ServerType.Transaq;

        private bool CurrentSource(int endpoint, string session) => !RequiresSignedTransport(endpoint)
            || _servers[endpoint] is ISignedOrderSource source && source.HasSignedOrderTransport
            && !string.IsNullOrEmpty(session) && session == source.SignedOrderSession && session == _sourceSession[endpoint];

        private void OnSecurity(Security security) { Pump(); }

        private void OnAccount(int endpoint, Portfolio portfolio)
        {
            lock (Sync)
            {
                if (_live || _disposed || portfolio == null || portfolio.Number != _connectors[endpoint].PortfolioName) return;
                PositionOnBoard[] rows = portfolio.GetPositionOnBoard()?.Where(p => p.SecurityNameCode == _connectors[endpoint].SecurityName).ToArray();
                _accountZeroAttested[endpoint] = false;
                _accountNet[endpoint] = rows == null || rows.Length > 1 ? null : rows.Length == 0 ? 0 : rows[0].ValueCurrent;
                _accountFunds[endpoint] = Math.Max(0, portfolio.ValueCurrent - portfolio.ValueBlocked);
                _accountAt[endpoint] = _fundsAt[endpoint] = Now; _accountSequence[endpoint] = checked(++_sequence);
            }
        }

        private void OnExplicitAccount(int endpoint, ExplicitAccount account)
        {
            lock (Sync)
            {
                if (_disposed || account.Number != _connectors[endpoint].PortfolioName || !CurrentSource(endpoint, account.Session)) return;
                bool sequenced = RequiresSignedTransport(endpoint);
                if (account.FundsUpdated && (sequenced ? account.Sequence > _sourceFundsSequence[endpoint] : account.ReceivedAt > _fundsAt[endpoint]))
                {
                    _fundsAt[endpoint] = account.ReceivedAt; _sourceFundsSequence[endpoint] = account.Sequence;
                    _accountFunds[endpoint] = Math.Max(0, account.FundsAreFree ? account.Current : account.Current - account.Blocked);
                }
                if (sequenced && account.PositionsComplete && account.Sequence > _sourceAccountSequence[endpoint]
                    && !account.Positions.ContainsKey(_connectors[endpoint].SecurityName))
                {
                    _accountZeroAttested[endpoint] = false;
                    _accountNet[endpoint] = null; _accountAt[endpoint] = DateTime.MinValue;
                    _sourceAccountSequence[endpoint] = account.Sequence;
                }
                if ((sequenced ? account.Sequence > _sourceAccountSequence[endpoint] : account.ReceivedAt > _accountAt[endpoint]) && account.Positions.TryGetValue(_connectors[endpoint].SecurityName, out decimal net))
                {
                    _accountZeroAttested[endpoint] = false;
                    _accountNet[endpoint] = net; _accountAt[endpoint] = account.ReceivedAt; _sourceAccountSequence[endpoint] = account.Sequence;
                    _accountSequence[endpoint] = checked(++_sequence);
                }
            }
        }
        private void OnCandles(List<Candle> candles) { Pump(); }
        private void OnTick(Trade trade) { Pump(); }

        private void OnReconnect(int endpoint)
        {
            lock (Sync)
            {
                if (_disposed) return;
                _accountZeroAttested[endpoint] = false;
                Quotes.Remove(endpoint); _accountAt[endpoint] = DateTime.MinValue; _awaitingFillDetails = false;
                _accountNet[endpoint] = null; _accountFunds[endpoint] = null; _accountSequence[endpoint] = 0;
                _fundsAt[endpoint] = DateTime.MinValue;
                Reconciled = false; Engine.Reconcile("Endpoint reconnect: reconcile before resume");
                _connectors[endpoint].ProcessSignedQuote(new ExplicitQuote(_connectors[endpoint].SecurityName, false, 0, false, 0, Now));
                TrySave();
            }
        }

        private void OnQuote(int endpoint, ExplicitQuote quote)
        {
            lock (Sync)
            {
                if (_disposed || quote.Instrument != _connectors[endpoint].SecurityName || !CurrentSource(endpoint, quote.Session)) return;
                if (RequiresSignedTransport(endpoint))
                {
                    if (quote.Sequence <= _sourceQuoteSequence[endpoint]) return;
                    _sourceQuoteSequence[endpoint] = quote.Sequence;
                }
                if (!_live && _now != DateTime.MinValue && quote.Time < _now)
                { Engine.Reconcile("Simulation time moved backwards: begin a new campaign"); Reconciled = false; }
                _now = _live ? DateTime.Now : quote.Time;
                Security security = _tabs[endpoint].Security;
                Portfolio portfolio = _tabs[endpoint].Portfolio;
                bool ready = _tabs[endpoint].IsReadyToTrade && !_tabs[endpoint].IsNonTradePeriodInConnector;
                decimal margin = Engine.Data.ActivePlan.Length == 0 || Engine.Data.Plans[Engine.Data.ActivePlan].IsLongInventory
                    ? security?.MarginBuy ?? 0 : security?.MarginSell ?? 0;
                bool accountFresh = !_live || _fundsAt[endpoint] != DateTime.MinValue
                    && Now - _fundsAt[endpoint] <= TimeSpan.FromSeconds(Engine.Data.Policy.FreshnessSeconds);
                Quotes[endpoint] = new Futures2Quote { HasBid = quote.HasBid, Bid = quote.Bid, HasAsk = quote.HasAsk, Ask = quote.Ask,
                    Ready = ready, Time = _live ? quote.ReceivedAt : quote.Time, Margin = margin > 0 ? margin : null,
                    FreeMargin = ManualFreeFunds ?? (UsePortfolioFunds && accountFresh ? _accountFunds[endpoint] : null) };
                bool fresh = Quotes[endpoint].Valid(Now, Engine.Data.Policy.FreshnessSeconds);
                _connectors[endpoint].ProcessSignedQuote(fresh ? quote : new ExplicitQuote(quote.Instrument, false, 0, false, 0, quote.Time));
                foreach (Position position in _tabs[endpoint].PositionsAll ?? new List<Position>())
                    if (Own(position) && fresh) position.SetSignedBidAsk(quote.HasBid, quote.Bid, quote.HasAsk, quote.Ask);
                Pump();
            }
        }

        private bool Own(Position position) => position.SignalTypeOpen != null && position.SignalTypeOpen.StartsWith("F2:" + Engine.Data.Campaign + ":", StringComparison.Ordinal);
        private string Tag(Futures2Intent intent) => "F2:" + Engine.Data.Campaign + ":" + intent.Id;
        private Position PositionFor(Futures2Intent intent) => (_tabs[intent.Endpoint].PositionsAll ?? new List<Position>()).SingleOrDefault(p => p.Number == intent.PositionNumber && Own(p));
        private Order OrderFor(Futures2Intent intent)
        {
            Position position = PositionFor(intent);
            return (intent.Entry ? position?.OpenOrders : position?.CloseOrders)?.SingleOrDefault(o => o.NumberUser == intent.OrderNumber);
        }

        private void OnOrder(int endpoint, Order order)
        {
            lock (Sync)
            {
                if (_disposed) return;
                try
                {
                    Futures2Intent intent = Engine.Data.Book.Intents.SingleOrDefault(i => i.Endpoint == endpoint && i.OrderNumber == order.NumberUser);
                    if (intent == null) return;
                    ProjectOrder(intent, order); TrySave();
                }
                catch (Exception error) { Fail(error); }
            }
        }

        private void ProjectOrder(Futures2Intent intent, Order order)
        {
            if ((order.SignedIdentity?.ClientKey ?? "") != intent.ClientKey)
                throw new InvalidOperationException("Native broker reference does not match the durable intent.");
            if (!string.IsNullOrEmpty(order.NumberMarket))
            {
                if (intent.MarketNumber.Length > 0 && intent.MarketNumber != order.NumberMarket) throw new InvalidOperationException("Native venue identity changed: reconcile replacement mapping.");
                intent.MarketNumber = order.NumberMarket;
            }
            foreach (MyTrade trade in order.MyTrades ?? new List<MyTrade>()) ProjectFill(intent, trade);
            Futures2IntentState status = order.State switch
            {
                OrderStateType.Active => Futures2IntentState.Working,
                OrderStateType.Partial => Futures2IntentState.Partial,
                OrderStateType.Done => Futures2IntentState.Filled,
                OrderStateType.Cancel => Futures2IntentState.Canceled,
                OrderStateType.Fail => Futures2IntentState.Rejected,
                OrderStateType.LostAfterActive => Futures2IntentState.Unknown,
                _ => intent.State
            };
            Engine.Data.Book.Observe(intent, status, Math.Max(order.VolumeExecute, order.SignedIdentity?.Executed ?? 0));
            if (intent.State == Futures2IntentState.Unknown)
            {
                bool detailsOnly = status != Futures2IntentState.Unknown && intent.ReportedExecuted > intent.Filled;
                if (detailsOnly && Reconciled) { _awaitingFillDetails = true; _stateBeforeDetails = Engine.Data.State; }
                if (!detailsOnly) _awaitingFillDetails = false;
                Reconciled = false; Engine.Reconcile(detailsOnly ? "Awaiting identified native fills" : "Native order/fill outcome unknown");
            }
        }

        private void OnFill(int endpoint, MyTrade trade)
        {
            lock (Sync)
            {
                if (_disposed) return;
                try
                {
                    Futures2Intent[] matching = Engine.Data.Book.Intents.Where(i => i.Endpoint == endpoint && i.MarketNumber == trade.NumberOrderParent && i.ClientKey == (trade.SignedIdentity?.ClientKey ?? "")
                        && FillInstrumentMatches(i, trade.SecurityNameCode)).ToArray();
                    if (matching.Length == 0)
                    {
                        // Journal may have bound venue ID before the order callback reaches the strategy.
                        foreach (Futures2Intent intent in Engine.Data.Book.Intents.Where(i => i.Endpoint == endpoint))
                        {
                            Order native = OrderFor(intent);
                            if (native != null && native.MatchesTrade(trade)) { ProjectOrder(intent, native); TrySave(); return; }
                        }
                        Engine.Reconcile("Unmatched native fill: reconcile ownership"); Reconciled = false;
                    }
                    else if (matching.Length != 1) throw new InvalidOperationException("Ambiguous fill identity.");
                    else ProjectFill(matching[0], trade);
                    TrySave();
                }
                catch (Exception error) { Fail(error); }
            }
        }

        private bool FillInstrumentMatches(Futures2Intent intent, string name)
        {
            string instrument = Engine.Data.Plans[intent.PlanId].Input.Instrument;
            return name == instrument || (Engine.Data.Plans[intent.PlanId].PaperExecution && name == instrument + " TestPaper");
        }

        private void ProjectFill(Futures2Intent intent, MyTrade trade)
        {
            Futures2Plan plan = Engine.Data.Plans[intent.PlanId];
            bool buy = (plan.IsLongInventory) == intent.Entry;
            if (string.IsNullOrEmpty(trade.NumberTrade) || !FillInstrumentMatches(intent, trade.SecurityNameCode)
                || trade.Side != (buy ? Side.Buy : Side.Sell) || trade.NumberOrderParent != intent.MarketNumber
                || intent.ClientKey != (trade.SignedIdentity?.ClientKey ?? ""))
                throw new InvalidOperationException("Native fill identity/side mismatch.");
            string key = intent.Endpoint + ":" + intent.OrderNumber + ":" + trade.NumberTrade;
            if (trade.SignedIdentity != null) key += ":" + intent.ClientKey + ":" + trade.Time.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
            if (Engine.Data.Book.Fill(intent, plan, key, trade.Volume, trade.Price, intent.FeePerUnit,
                Engine.Data.Policy.CreditDayExits, Now == DateTime.MinValue ? trade.Time : Now))
            {
                SignedOrderIdentity identity = trade.SignedIdentity;
                bool current = identity != null && CurrentSource(intent.Endpoint, identity.Session);
                _fillAt[intent.Endpoint] = current ? identity.ReceivedAt : Now;
                if (current) _sourceFillSequence[intent.Endpoint] = Math.Max(_sourceFillSequence[intent.Endpoint], identity.Sequence);
                _fillSequence[intent.Endpoint] = checked(++_sequence);
            }
        }

        /// <summary>Replays native journal facts and checks owned quantities plus live account net exposure.</summary>
        /// <param name="ownerVerifiedOrders">Operator has checked unresolved and external orders against the account.</param>
        /// <param name="acceptAbsentZero">Explicit operator command may attest a missing initial zero row; automatic rollover checks must pass false.</param>
        /// <remarks>Does not resolve an absent Unknown order. Native records must provide its terminal outcome;
        /// a dedicated absent-order resolution command requires explicit operator evidence.</remarks>
        public void Reconcile(bool ownerVerifiedOrders, bool acceptAbsentZero = true)
        {
            lock (Sync)
            {
                Reconciled = false;
                RecoverInventory();
                AttachSources();
                if (_live && !ownerVerifiedOrders) throw new InvalidOperationException("Owner account/order verification is required.");
                foreach (Futures2Intent intent in Engine.Data.Book.Intents)
                {
                    if (RequiresSignedTransport(intent.Endpoint) && intent.ClientKey.Length == 0 && (intent.CanFill || intent.Filled > 0))
                        throw new InvalidOperationException("Legacy TRANSAQ ownership has no durable broker reference; reconcile and finish that campaign before enabling signed transport.");
                    Order order = OrderFor(intent);
                    if (order == null)
                    {
                        if (intent.CanFill || intent.Filled > 0) throw new InvalidOperationException("Native journal identity missing; reconcile before resume.");
                    }
                    else ProjectOrder(intent, order);
                }
                for (int endpoint = 0; endpoint < 2; endpoint++)
                {
                    if (!HasEndpointInventory(endpoint) && endpoint != Engine.Data.Endpoint && !Quotes.ContainsKey(endpoint)) continue;
                    List<Position> native = _tabs[endpoint].PositionsAll ?? new List<Position>();
                    if (native.Any(p => !Own(p) && (p.OpenVolume != 0 || HasWorking(p))))
                        throw new InvalidOperationException("Dedicated tab contains foreign positions/orders.");
                    foreach (Position position in native.Where(Own))
                    {
                        decimal projected = Engine.Data.Book.Lots.Where(l => l.Endpoint == endpoint && l.PositionNumber == position.Number).Sum(l => l.Quantity);
                        if (position.OpenVolume != projected) throw new InvalidOperationException("Native/projected inventory mismatch.");
                        foreach (Order order in (position.OpenOrders ?? new List<Order>()).Concat(position.CloseOrders ?? new List<Order>()))
                            if (!Engine.Data.Book.Intents.Any(i => i.Endpoint == endpoint && i.OrderNumber == order.NumberUser))
                                throw new InvalidOperationException("Unowned native order in campaign position.");
                        if (position.StopOrderIsActive || position.ProfitOrderIsActive) throw new InvalidOperationException("Foreign local protection is active.");
                    }
                    if (_live && !IsPaper(endpoint))
                    {
                        if (_servers[endpoint] is not IExplicitAccountSource accountSource || !accountSource.HasExplicitAccountUpdates)
                            throw new InvalidOperationException("This realization does not provide actual account update observations; live signed execution is unavailable.");
                        if (RequiresSignedTransport(endpoint) && (!CurrentSource(endpoint, _sourceSession[endpoint])
                            || !ManualFreeFunds.HasValue))
                            throw new InvalidOperationException("TRANSAQ requires its enabled signed profile and an explicit manual funds envelope; broker-free reservation coverage is unavailable.");
                        decimal ownNet = native.Where(Own).Sum(p => p.OpenVolume * (p.Direction == Side.Buy ? 1 : -1));
                        if (acceptAbsentZero && ownerVerifiedOrders && !_accountNet[endpoint].HasValue && ownNet + ExternalNet[endpoint] == 0
                            && Now - _fundsAt[endpoint] <= TimeSpan.FromSeconds(Engine.Data.Policy.FreshnessSeconds))
                        {
                            _accountZeroAttested[endpoint] = true;
                            _accountNet[endpoint] = 0; _accountAt[endpoint] = Now; _accountSequence[endpoint] = checked(++_sequence);
                            if (RequiresSignedTransport(endpoint)) _sourceAccountSequence[endpoint] = Math.Max(_sourceAccountSequence[endpoint], _sourceFundsSequence[endpoint]);
                        }
                        if (!Quotes.TryGetValue(endpoint, out Futures2Quote quote) || !quote.Valid(Now, Engine.Data.Policy.FreshnessSeconds)
                            || RequiresSignedTransport(endpoint) && _sourceAccountSequence[endpoint] <= _sourceFillSequence[endpoint]
                            || _accountSequence[endpoint] <= _fillSequence[endpoint]
                            || _accountAt[endpoint] < _fillAt[endpoint]
                            || Now - _accountAt[endpoint] > TimeSpan.FromSeconds(Engine.Data.Policy.FreshnessSeconds))
                            throw new InvalidOperationException("Fresh market/account evidence is required.");
                        if (_accountNet[endpoint] != ownNet + ExternalNet[endpoint])
                            throw new InvalidOperationException("Account position does not reconcile with owned plus declared external net.");
                    }
                }
                if (Engine.Data.Book.Intents.Any(i => i.State == Futures2IntentState.Unknown || i.State == Futures2IntentState.SubmitPending))
                    throw new InvalidOperationException("Unresolved native outcomes remain reserved.");
                string mismatch = AccountMismatchReason();
                if (mismatch.Length > 0) throw new InvalidOperationException(mismatch);
                Reconciled = true;
                if (Engine.Data.State == Futures2State.Reconciling || Engine.Data.State == Futures2State.Faulted) Engine.Pause();
                Engine.Data.Reason = "Native/account reconciliation accepted"; Save();
            }
        }

        private static bool HasWorking(Position position) => (position.OpenOrders ?? new List<Order>()).Concat(position.CloseOrders ?? new List<Order>())
            .Any(o => o.State != OrderStateType.Done && o.State != OrderStateType.Cancel && o.State != OrderStateType.Fail);

        /// <summary>Runs one bounded pass; timer calls are permitted only by the owning robot's lifecycle.</summary>
        public void Pump()
        {
            lock (Sync)
            {
                if (_disposed || _processing || _persistenceFailed) return;
                _processing = true;
                try
                {
                    AttachSources(); BeforeDecision?.Invoke();
                    if (_disposed) return;
                    if (RequiresSignedTransport(Engine.Data.Endpoint) && UsePortfolioFunds && !ManualFreeFunds.HasValue)
                    {
                        Engine.Pause(); Engine.Data.Reason = "TRANSAQ broker-free reservation coverage is unavailable; select a manual funds envelope.";
                    }
                    foreach (Futures2Plan plan in Engine.Data.Plans.Values)
                    {
                        IEnumerable<int> endpoints = Engine.Data.Book.Intents.Where(i => i.PlanId == plan.Id && i.CanFill).Select(i => i.Endpoint)
                            .Concat(Engine.Data.Book.Lots.Where(l => l.PlanId == plan.Id && l.Quantity > 0).Select(l => l.Endpoint))
                            .Concat(Engine.Data.InventoryOperations.Where(o => !o.Committed && o.Request.PlanId == plan.Id).Select(o => o.Request.Endpoint));
                        foreach (int endpoint in endpoints.Distinct())
                            if (plan.EndpointIdentity.Length > 0 && plan.EndpointIdentity != EndpointIdentity(endpoint))
                            { Reconciled = false; Engine.Reconcile("Native execution profile changed with owned obligations"); }
                    }
                    if (_awaitingFillDetails && Engine.Data.Book.Intents.All(i => i.State != Futures2IntentState.Unknown && i.State != Futures2IntentState.SubmitPending) && AccountMatches())
                    {
                        _awaitingFillDetails = false; Reconciled = true;
                        if (Engine.Data.State == Futures2State.Reconciling && Engine.Data.Reason == "Awaiting identified native fills")
                        { Engine.Data.State = _stateBeforeDetails; Engine.Data.Reason = "Native fill details reconciled"; }
                    }
                    foreach (KeyValuePair<int, Futures2Quote> pair in Quotes)
                    {
                        pair.Value.Ready = _tabs[pair.Key].IsReadyToTrade && !_tabs[pair.Key].IsNonTradePeriodInConnector;
                        if (ManualFreeFunds.HasValue)
                            pair.Value.FreeMargin = Math.Max(0, ManualFreeFunds.Value - Engine.Data.Book.Lots.Sum(l => l.Quantity * Engine.Data.Plans[l.PlanId].Input.Collateral));
                        else pair.Value.FreeMargin = !RequiresSignedTransport(pair.Key) && UsePortfolioFunds && (!_live || Now - _fundsAt[pair.Key] <= TimeSpan.FromSeconds(Engine.Data.Policy.FreshnessSeconds))
                            ? _accountFunds[pair.Key] : null;
                        if (!pair.Value.Valid(Now, Engine.Data.Policy.FreshnessSeconds))
                            _connectors[pair.Key].ProcessSignedQuote(new ExplicitQuote(_connectors[pair.Key].SecurityName, false, 0, false, 0, Now));
                    }
                    Futures2HelperScope helper = PortfolioScope?.Invoke();
                    List<Futures2Action> actions = Engine.Decide(Quotes, Now, Reconciled && AccountMatches(), helper?.Percent ?? PortfolioReturn?.Invoke(), RequirePortfolioMark, helper);
                    Save(); // Quote-only risk watermarks are as durable as order-producing decisions.
                    foreach (Futures2Action action in actions) Execute(action);
                    string accountReason = NativeStateBlockReason;
                    if (_lastAccountReason != accountReason)
                    {
                        _lastAccountReason = accountReason;
                        _log(accountReason.Length == 0 ? "Native/account gate accepted" : "Native/account gate blocked: " + accountReason);
                    }
                    if (_lastReason != Engine.Data.Reason) { _lastReason = Engine.Data.Reason; _log(Engine.Data.State + ": " + _lastReason); }
                }
                catch (Exception error) { Fail(error); }
                finally { _processing = false; }
            }
        }

        private void Execute(Futures2Action action)
        {
            Futures2Intent intent = action.Intent;
            if (action.Cancel)
            {
                Order native = OrderFor(intent);
                if (native == null) { intent.State = Futures2IntentState.Unknown; Reconciled = false; Engine.Reconcile("Cannot locate order to cancel"); Save(); return; }
                _tabs[intent.Endpoint].CloseOrder(native); return;
            }
            if (!Reconciled || !AccountMatches() || Engine.Data.State == Futures2State.Faulted || Engine.Data.State == Futures2State.Reconciling
                || intent.Entry && (Engine.Data.State != Futures2State.Active || Engine.Data.Emergency))
            { intent.State = Futures2IntentState.Rejected; Save(); return; }
            Futures2Plan plan = Engine.Data.Plans[intent.PlanId];
            BotTabSimple tab = _tabs[intent.Endpoint];
            if (!SignedOrdersEnabled || (plan.EndpointIdentity.Length > 0 && plan.EndpointIdentity != EndpointIdentity(intent.Endpoint)) || tab.Security == null || tab.Security.Name != plan.Input.Instrument || tab.Security.PriceStep != plan.Input.Tick
                || !SignedPriceMath.IsNativeVolume(intent.Quantity, tab.Security)
                || !Quotes.TryGetValue(intent.Endpoint, out Futures2Quote quote) || !quote.Valid(Now, Engine.Data.Policy.FreshnessSeconds))
            { intent.State = Futures2IntentState.Rejected; Engine.Pause(); Engine.Data.Reason = "Submission capability/metadata/quote check failed"; Save(); return; }
            Position position = intent.Entry ? null : PositionFor(intent);
            if (!intent.Entry && position == null) throw new InvalidOperationException("Owned close position missing.");
            bool buy = plan.IsLongInventory == intent.Entry;
            // A flat mode change can retain a fresh quote carrying the previous side's margin.
            // Recheck current metadata before native position/order creation; reductions remain allowed.
            decimal entryMargin = buy ? tab.Security.MarginBuy : tab.Security.MarginSell;
            if (intent.Entry && entryMargin > plan.Input.Collateral)
            {
                intent.State = Futures2IntentState.Rejected; Engine.Pause();
                Engine.Data.Reason = "Broker margin increased for actual entry side: revalidate reserve";
                Save(); return;
            }
            if (RequiresSignedTransport(intent.Endpoint))
            {
                if (_servers[intent.Endpoint] is not ISignedOrderSource source || !source.HasSignedOrderTransport
                    || !CurrentSource(intent.Endpoint, source.SignedOrderSession))
                    throw new InvalidOperationException("Signed TRANSAQ transport profile is unavailable.");
                intent.ClientKey = "F2" + intent.Id;
            }
            intent.State = Futures2IntentState.SubmitPending;
            Save();
            try
            {
                tab.SubmitSignedOrder(position, buy ? Side.Buy : Side.Sell, intent.Quantity, intent.Price,
                    intent.Market, plan.Input.PercentBase, Tag(intent), (boundPosition, order) =>
                    {
                        intent.PositionNumber = boundPosition.Number; intent.OrderNumber = order.NumberUser;
                        // Plan tick value is explicitly per native quantity unit; normalize native reporting accordingly.
                        boundPosition.PriceStep = plan.Input.Tick; boundPosition.PriceStepCost = plan.Input.TickValue; boundPosition.Lots = 1;
                        if (intent.ClientKey.Length > 0)
                        {
                            order.SignedIdentity = new SignedOrderIdentity { ClientKey = intent.ClientKey };
                            string session = _sourceSession[intent.Endpoint];
                            string endpointIdentity = EndpointIdentity(intent.Endpoint);
                            Security metadata = tab.Security;
                            decimal lotSize = metadata.Lot, volumeStep = metadata.VolumeStep, minimumVolume = metadata.MinTradeAmount;
                            int volumeDecimals = metadata.DecimalsVolume;
                            order.SignedDispatch = new SignedOrderDispatch(Sync, () =>
                                !_disposed && !_persistenceFailed && SignedOrdersEnabled && Reconciled && AccountMatches()
                                && CurrentSource(intent.Endpoint, session) && endpointIdentity == EndpointIdentity(intent.Endpoint)
                                && !intent.CancelRequested.HasValue && intent.State == Futures2IntentState.SubmitPending
                                && order.NumberUser == intent.OrderNumber && order.Volume == intent.Quantity && order.Price == intent.Price
                                && tab.Security != null && tab.Security.PriceStep == plan.Input.Tick && tab.Security.Lot == lotSize
                                && tab.Security.VolumeStep == volumeStep && tab.Security.MinTradeAmount == minimumVolume
                                && tab.Security.DecimalsVolume == volumeDecimals && SignedPriceMath.IsNativeVolume(intent.Quantity, tab.Security)
                                && (!intent.Entry || ManualFreeFunds.HasValue && Engine.Data.Book.Reserved(Engine.Data.Plans) <= ManualFreeFunds.Value
                                    && Engine.Data.Book.Reserved(Engine.Data.Plans) <= Engine.Data.Capital
                                    && (buy ? tab.Security.MarginBuy : tab.Security.MarginSell) <= plan.Input.Collateral)
                                && Quotes.TryGetValue(intent.Endpoint, out Futures2Quote currentQuote) && currentQuote.Valid(Now, Engine.Data.Policy.FreshnessSeconds)
                                && (intent.Entry ? Engine.Data.State == Futures2State.Active && !Engine.Data.Emergency
                                    : Engine.Data.State != Futures2State.Faulted && Engine.Data.State != Futures2State.Reconciling), () =>
                            {
                                order.State = OrderStateType.Fail; _tabs[intent.Endpoint].GetJournal().SetNewOrder(order, false);
                                intent.State = Futures2IntentState.Rejected; intent.TerminalReport = Futures2IntentState.Rejected;
                                Engine.Data.Reason = "Queued signed submission suppressed before transport"; Save();
                            });
                        }
                        Save();
                        if (intent.ClientKey.Length > 0) ((ISignedOrderSource)_servers[intent.Endpoint]).RegisterSignedOrder(order);
                    });
                Save();
            }
            catch
            {
                intent.State = Futures2IntentState.Unknown; Reconciled = false;
                Engine.Reconcile("Submit outcome uncertain; native identity remains reserved"); TrySave(); throw;
            }
        }

        /// <summary>True only for native paper execution routes, independent of live market-data source.</summary>
        public bool IsPaper(int endpoint) => _connectors[endpoint].EmulatorIsOn || _connectors[endpoint].ServerType == OsEngine.Market.ServerType.Finam;

        /// <summary>Hashes stable endpoint identity without storing account identifiers in the strategy checkpoint.</summary>
        public string EndpointIdentity(int endpoint) => Futures2Store.Name(_connectors[endpoint].ServerFullName + "|"
            + _connectors[endpoint].PortfolioName + "|" + _connectors[endpoint].SecurityName + "|"
            + _connectors[endpoint].SecurityClass + "|" + IsPaper(endpoint));

        /// <summary>Checks a manually configured replacement endpoint before old-contract liquidation starts.</summary>
        public void ValidateEmptyEndpoint(int endpoint, Futures2Plan plan, bool requireQuote)
        {
            lock (Sync)
            {
                if (endpoint < 0 || endpoint > 1 || _tabs[endpoint].Security == null || _tabs[endpoint].Security.Name != plan.Input.Instrument
                    || _tabs[endpoint].Security.PriceStep != plan.Input.Tick || plan.EndpointIdentity != EndpointIdentity(endpoint)
                    || plan.Levels.Any(l => !SignedPriceMath.IsNativeVolume(l.Volume, _tabs[endpoint].Security))
                    || (_tabs[endpoint].PositionsAll ?? new List<Position>()).Any(p => p.OpenVolume != 0 || HasWorking(p)))
                    throw new InvalidOperationException("Replacement endpoint identity/metadata/inventory is not valid.");
                if (requireQuote && (!Quotes.TryGetValue(endpoint, out Futures2Quote quote) || !quote.Valid(Now, Engine.Data.Policy.FreshnessSeconds)))
                    throw new InvalidOperationException("Fresh ready replacement quote required before cutover starts.");
                if (_live && !IsPaper(endpoint) && requireQuote && (_accountNet[endpoint] != ExternalNet[endpoint]
                    || _accountSequence[endpoint] <= _fillSequence[endpoint] || _accountAt[endpoint] < _fillAt[endpoint]
                    || Now - _accountAt[endpoint] > TimeSpan.FromSeconds(Engine.Data.Policy.FreshnessSeconds)))
                    throw new InvalidOperationException("Reconcile the replacement account before closing the old contract.");
            }
        }

        private bool RequiresAccount(int endpoint) => _live && !IsPaper(endpoint)
            && (endpoint == Engine.Data.Endpoint || HasEndpointInventory(endpoint));

        private bool HasEndpointInventory(int endpoint) => Engine.Data.Book.Intents.Any(i => i.Endpoint == endpoint)
            || Engine.Data.Book.Lots.Any(l => l.Endpoint == endpoint && l.Quantity > 0)
            || Engine.Data.InventoryOperations.Any(o => !o.Committed && o.Request.Endpoint == endpoint);

        private bool AccountMatches() => AccountMismatchReason().Length == 0;

        private string AccountMismatchReason()
        {
            if (InventoryPending) return "Prepared inventory recovery remains incomplete";
            for (int endpoint = 0; endpoint < 2; endpoint++)
            {
                string prefix = "Endpoint " + endpoint + ": ";
                Futures2Lot[] lots = Engine.Data.Book.Lots.Where(l => l.Endpoint == endpoint && l.Quantity > 0).ToArray();
                foreach (Position position in _tabs[endpoint].PositionsAll ?? new List<Position>())
                {
                    if (!Own(position))
                    {
                        if (position.OpenVolume != 0 || HasWorking(position)) return prefix + "Foreign position/order in dedicated tab";
                        continue;
                    }
                    if (position.Inventory != null && position.Inventory.Fault.Length > 0) return prefix + position.Inventory.Fault;
                    if (position.OpenVolume != lots.Where(l => l.PositionNumber == position.Number).Sum(l => l.Quantity))
                        return prefix + "Native/projected quantity mismatch";
                    if (position.StopOrderIsActive || position.ProfitOrderIsActive) return prefix + "Foreign native protection";
                    foreach (Order order in (position.OpenOrders ?? new List<Order>()).Concat(position.CloseOrders ?? new List<Order>()))
                        if (!Engine.Data.Book.Intents.Any(i => i.Endpoint == endpoint && i.OrderNumber == order.NumberUser))
                            return prefix + "Unowned order in campaign position";
                }
                foreach (IGrouping<int, Futures2Lot> group in lots.GroupBy(l => l.PositionNumber))
                {
                    Position native = (_tabs[endpoint].PositionsAll ?? new List<Position>()).SingleOrDefault(p => Own(p) && p.Number == group.Key);
                    if (native == null || native.OpenVolume != group.Sum(l => l.Quantity)) return prefix + "Owned journal position missing or inconsistent";
                }
                if (!RequiresAccount(endpoint)) continue;
                if (_servers[endpoint] is not IExplicitAccountSource accountSource || !accountSource.HasExplicitAccountUpdates)
                    return prefix + "Explicit account source unavailable";
                if (RequiresSignedTransport(endpoint) && !CurrentSource(endpoint, _sourceSession[endpoint]))
                    return prefix + "Signed transport session unavailable";
                if (!_accountNet[endpoint].HasValue) return prefix + "Account position unknown";
                if (RequiresSignedTransport(endpoint) && _sourceAccountSequence[endpoint] <= _sourceFillSequence[endpoint]
                    || _accountSequence[endpoint] <= _fillSequence[endpoint] || _accountAt[endpoint] < _fillAt[endpoint])
                    return prefix + "Account observation does not follow owned fills";
                if (Now - _accountAt[endpoint] > TimeSpan.FromSeconds(Engine.Data.Policy.FreshnessSeconds))
                    return prefix + "Account observation stale";
                decimal net = lots.Sum(l => l.Quantity * (Engine.Data.Plans[l.PlanId].IsLongInventory ? 1 : -1));
                if (_accountNet[endpoint] != net + ExternalNet[endpoint]) return prefix + "Account net differs from owned plus declared external net";
            }
            return "";
        }

        /// <summary>Current reason that native/account reconciliation blocks decisions; empty means this gate passes.</summary>
        /// <remarks>Calculated under Sync without changing ownership or requesting broker data. Quote, policy,
        /// capability and budget checks still apply separately; passing this gate is not permission to trade.</remarks>
        public string NativeStateBlockReason
        {
            get
            {
                lock (Sync)
                {
                    if (_disposed) return "Adapter disposed";
                    if (_persistenceFailed) return "Checkpoint storage faulted";
                    string mismatch = AccountMismatchReason();
                    if (mismatch.Length > 0) return mismatch;
                    return Reconciled ? "" : "Explicit native/account reconciliation required";
                }
            }
        }

        /// <summary>Returns a detached operator view of current account evidence and actual campaign reservations.</summary>
        /// <remarks>Reads under Sync; does not reconcile, infer external ownership or change reservations.
        /// Expected net uses this campaign plus the fixed operator declaration, not an automatic peer total.
        /// Owner-attested zero is distinguished from a reported position. Account identifiers are omitted.
        /// This is the native/account gate only; execution has additional quote, policy and funds checks.</remarks>
        public string DescribeNativeState()
        {
            lock (Sync)
            {
                StringBuilder text = new StringBuilder();
                string reason = NativeStateBlockReason;
                text.AppendLine("Native/account gate=" + (reason.Length == 0 ? "PASS" : "BLOCKED: " + reason)
                    + " | last reconciliation accepted=" + Reconciled);
                decimal held = Engine.Data.Book.Lots.Sum(l => l.Quantity * Engine.Data.Plans[l.PlanId].Input.Collateral);
                decimal reserved = Engine.Data.Book.Reserved(Engine.Data.Plans);
                text.AppendLine(FormattableString.Invariant($"Actual collateral: held={held}; pending entries={reserved - held}; total={reserved}; capital={Engine.Data.Capital}"));
                for (int endpoint = 0; endpoint < 2; endpoint++)
                {
                    decimal own = Engine.Data.Book.Lots.Where(l => l.Endpoint == endpoint)
                        .Sum(l => l.Quantity * (Engine.Data.Plans[l.PlanId].IsLongInventory ? 1 : -1));
                    decimal expected = own + ExternalNet[endpoint];
                    string observed = _accountNet[endpoint]?.ToString(CultureInfo.InvariantCulture) ?? "unknown";
                    string difference = _accountNet[endpoint].HasValue ? (_accountNet[endpoint].Value - expected).ToString(CultureInfo.InvariantCulture) : "unknown";
                    string age = _accountAt[endpoint] == DateTime.MinValue ? "unknown" : (Now - _accountAt[endpoint]).TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture);
                    text.AppendLine(FormattableString.Invariant($"Endpoint {endpoint}: own={own}; declared external={ExternalNet[endpoint]}; expected={expected}; observed={observed}; difference={difference}; age seconds={age}; source={(_accountZeroAttested[endpoint] ? "owner-attested zero" : _accountNet[endpoint].HasValue ? "account observation" : "unknown")}; account check={(RequiresAccount(endpoint) ? "required" : "not required for this route")}"));
                }
                return text.ToString();
            }
        }

        /// <summary>Resolves one absent unknown order only after the operator verified its terminal canceled remainder and all fills.</summary>
        /// <remarks>Does not infer absence from a cache. Exact owner evidence may terminalize an unresolved
        /// native record, but cannot reduce observed execution or fabricate fills. Save and full reconciliation
        /// still precede resume. All changes serialize on Sync. THG-TRANSAQ-IMPLEMENTATION-006.</remarks>
        public void ResolveAbsent(string intentId, decimal verifiedExecuted, bool ownerVerified)
        {
            lock (Sync)
            {
                Futures2Intent intent = Engine.Data.Book.Intents.Single(i => i.Id == intentId);
                Order native = OrderFor(intent);
                if (!ownerVerified || verifiedExecuted != intent.Filled || verifiedExecuted < intent.ReportedExecuted
                    || (native != null && native.State != OrderStateType.Cancel && native.State != OrderStateType.Fail
                        && intent.State != Futures2IntentState.Unknown && intent.State != Futures2IntentState.SubmitPending))
                    throw new InvalidOperationException("Exact terminal/fill evidence required for absent-order resolution.");
                if (native != null)
                {
                    if (native.SignedDispatch?.CancelBeforeSend() != true)
                    {
                        native.State = OrderStateType.Cancel;
                        _tabs[intent.Endpoint].GetJournal().SetNewOrder(native, false);
                    }
                }
                Engine.Data.Book.Observe(intent, Futures2IntentState.Canceled, verifiedExecuted);
                Reconciled = false; Engine.Reconcile("Resolved one absent order; reconcile full campaign"); Save();
            }
        }

        /// <summary>Advances simulation timers after native sources were processed; does not refresh old quotes.</summary>
        public void AdvanceReplay(DateTime time)
        {
            lock (Sync)
            {
                if (_live || _disposed) return;
                _now = time; Pump();
            }
        }

        /// <summary>Begins a new isolated simulation pass after the native server's explicit reset event.</summary>
        public void ResetSimulation()
        {
            lock (Sync)
            {
                if (_live) throw new InvalidOperationException("Live ownership cannot be reset as simulation.");
                Engine = new Futures2Controller(new Futures2Checkpoint { CoordinationUsed = false }); Quotes.Clear(); Reconciled = false; _now = DateTime.MinValue;
                Array.Clear(_accountAt); Array.Clear(_fillAt);
                Array.Clear(_fundsAt);
                Array.Clear(_accountZeroAttested);
                Array.Clear(_accountSequence); Array.Clear(_fillSequence); Array.Clear(_accountNet); Array.Clear(_accountFunds); _sequence = 0;
            }
        }

        private void TrySave() { if (!_persistenceFailed) { try { Save(); } catch (Exception error) { _log(error.ToString()); } } }
        private void Fail(Exception error) { _awaitingFillDetails = false; Reconciled = false; Engine.Fault(error.Message); _log(error.ToString()); TrySave(); }

        /// <summary>Unsubscribes and saves ownership; does not send cancellation after native deletion.</summary>
        public void Dispose()
        {
            lock (Sync)
            {
                if (_disposed) return; _disposed = true;
                for (int index = 0; index < 2; index++)
                {
                    if (_sources[index] != null) _sources[index].ExplicitQuoteEvent -= _quoteHandlers[index];
                    if (_servers[index] != null) _servers[index].ConnectStatusChangeEvent -= _statusHandlers[index];
                    if (_servers[index] is IExplicitAccountSource accountSource) accountSource.ExplicitAccountEvent -= _explicitAccountHandlers[index];
                    _tabs[index].EmulatorIsOnChangeStateEvent -= _paperHandlers[index];
                    _tabs[index].OrderUpdateEvent -= _orderHandlers[index]; _tabs[index].MyTradeEvent -= _fillHandlers[index];
                    _tabs[index].PortfolioOnExchangeChangedEvent -= _accountHandlers[index];
                    _connectors[index].ConnectorStartedReconnectEvent -= _reconnectHandlers[index];
                    _tabs[index].CandleUpdateEvent -= OnCandles; _tabs[index].CandleFinishedEvent -= OnCandles;
                    _tabs[index].NewTickEvent -= OnTick; _tabs[index].SecuritySubscribeEvent -= OnSecurity;
                }
                TrySave();
            }
        }
    }
}
