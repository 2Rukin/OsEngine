using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Xml.Linq;
using OsEngine.Charts.CandleChart;
using OsEngine.Entity;
using OsEngine.Market;
using OsEngine.Market.Connectors;
using OsEngine.Market.Servers;
using OsEngine.Market.Servers.Entity;
using OsEngine.Market.Servers.Transaq;
using OsEngine.OsTrader.Grids.Futures2;
using OsEngine.OsTrader.Panels.Tab;
using OsEngine.OsTrader.Panels.Tab.Internal;

namespace OsEngine.TradeHelpGrid.Tests
{
    /// <summary>Synthetic override of the physical command boundary. Instances bypass every native constructor.</summary>
    internal sealed class SyntheticTransaq : TransaqServerRealization
    {
        internal Func<string, string> Command;
        protected override string SendSignedCommand(string command) => Command(command);
    }

    /// <summary>Real managed parser, native queue and journal paths; no DLL, workers, sockets or broker settings.</summary>
    /// <remarks>Run with dotnet run --project Tests/TradeHelpGrid/OsEngine.TradeHelpGrid.Tests.csproj.
    /// Fixtures use uninitialized native objects and a synthetic physical-command override. Owned temporary
    /// checkpoint files are deleted explicitly; no real orders or account settings are changed. This does not
    /// qualify DLL/server compatibility, GUI, a native replay session, liquidity, slippage or profitability.
    /// THG-TRANSAQ-IMPLEMENTATION-006; THG-QUALIFICATION-001.</remarks>
    internal static class TransaqCases
    {
        private static readonly Type ProtocolType = typeof(TransaqServer).Assembly.GetType("OsEngine.Market.Servers.Transaq.TransaqSignedProtocol", true);
        private static Security Security() => new Security { Name = "TEST", NameClass = "FUT", NameId = "1", Lot = 10,
            PriceStep = 0.00001m, PriceStepCost = 0.00001m, VolumeStep = 1, MinTradeAmount = 1 };
        private static Order Order(string key = "F2fixture", int number = 101) => new Order { NumberUser = number,
            NumberMarket = "", SecurityNameCode = "TEST", SecurityClassCode = "FUT", PortfolioNumber = "United_fixture",
            Volume = 10, Side = Side.Buy, TypeOrder = OrderPriceType.Limit, Price = 0, UsesSignedPrice = true,
            SignedPercentBase = 1, SignedIdentity = new SignedOrderIdentity { ClientKey = key }, TimeCreate = Program.T0 };

        internal static object Invoke(object target, string name, params object[] args)
        {
            Type type = target as Type ?? target.GetType(); MethodInfo method = null;
            while (type != null && method == null)
            {
                method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
                type = type.BaseType;
            }
            if (method == null) throw new MissingMethodException(name);
            return method.Invoke(target is Type ? null : target, args);
        }
        private static object Protocol(bool union = true)
        {
            object protocol = Activator.CreateInstance(ProtocolType, true); Invoke(protocol, "Begin", union); return protocol;
        }
        private static readonly Func<string, string, string, Security> Resolve = (name, board, id) =>
            (name.Length == 0 || name == "TEST") && (board.Length == 0 || board == "FUT") && (id.Length == 0 || id == "1") ? Security() : null;
        private static object Capture(object protocol, string xml, DateTime? at = null) => Invoke(protocol, "Capture", xml, at ?? Program.T0);
        private static List<object> Parse(object protocol, string xml, DateTime? at = null) =>
            (List<object>)Invoke(protocol, "Parse", Capture(protocol, xml, at), Resolve);
        private static void Register(object protocol, Order order) => Invoke(protocol, "Register", order, Resolve);
        private static string FullOrder(string key, string status = "active", string balance = "10", string venue = "700", string transaction = "901", string extra = "") =>
            "<orders><order transactionid=\"" + transaction + "\"><orderno>" + venue + "</orderno><board>FUT</board><seccode>TEST</seccode>"
            + "<union>fixture</union><client>underlying</client><buysell>B</buysell><brokerref>" + key + "</brokerref><quantity>10</quantity><balance>"
            + balance + "</balance><price>0</price><status>" + status + "</status>" + extra + "</order></orders>";
        private static string Fill(string key, string quantity = "2", string trade = "800", string venue = "700", string time = "29.09.2026 12:00:00") =>
            "<trades><trade><orderno>" + venue + "</orderno><tradeno>" + trade + "</tradeno><board>FUT</board><seccode>TEST</seccode>"
            + "<union>fixture</union><client>underlying</client><buysell>B</buysell><brokerref>" + key + "</brokerref><quantity>" + quantity
            + "</quantity><price>-0.00001</price><time>" + time + "</time></trade></trades>";

        internal static void Run()
        {
            Serialization(); PricesAndAccounts(); OrdersAndDeltas(); Dispatch(); NativePath(); LiveProfilePath();
        }

        private static void Serialization()
        {
            Order original = Order(); original.NumberMarket = "700"; original.SignedIdentity.Transaction = "901";
            original.SignedIdentity.Session = "before-crash"; original.SignedIdentity.Sequence = 21;
            MyTrade trade = new MyTrade { NumberOrderParent = "700", NumberTrade = "800", SecurityNameCode = "TEST", Side = Side.Buy,
                Volume = 10, Price = 0, Time = Program.T0, SignedIdentity = new SignedOrderIdentity { ClientKey = "F2fixture", Session = "before-crash", Sequence = 22 } };
            original.SetTrade(trade); string first = original.GetStringForSave().ToString();
            original.SignedIdentity.Transaction = "902";
            Order restored = new Order(); restored.SetOrderFromString(original.GetStringForSave().ToString());
            Program.Equal(101, restored.NumberUser, "TRANSAQ N survives native serialization");
            Program.Equal("700", restored.NumberMarket, "TRANSAQ V stays raw venue identity");
            Program.Equal("902", restored.SignedIdentity.Transaction, "terminal save cache cannot hide new transport evidence");
            Program.Equal("FUT", restored.SecurityClassCode, "native route class persists");
            Program.Equal("F2fixture", restored.MyTrades.Single().SignedIdentity.ClientKey, "fill correlation persists with journal");
            restored.SetTrade(trade); Program.Equal(10m, restored.VolumeExecute, "replayed persisted fill is idempotent");
            Order old = new Order(); old.SetOrderFromString(string.Join("@", first.Split('@').Take(28)));
            Program.Check(old.SignedIdentity == null && old.UsesSignedPrice, "previous signed record has explicit no-correlation default");
            Program.Throws(() => SignedOrderIdentity.Load("2:future"), "unknown identity version fails closed");
            Order foreign = Order("F2another", 102); foreign.NumberMarket = "700"; foreign.SetTrade(trade);
            Program.Equal(0m, foreign.VolumeExecute, "reused V does not attribute foreign signed fill");
        }

        private static void PricesAndAccounts()
        {
            object protocol = Protocol();
            ExplicitQuote quote = Parse(protocol, "<quotes><quote secid=\"1\"><price>-0.00001</price><buy>3</buy></quote><quote secid=\"1\"><price>0</price><sell>4</sell></quote></quotes>").OfType<ExplicitQuote>().Single();
            Program.Check(quote.HasBid && quote.HasAsk && quote.Bid == -0.00001m && quote.Ask == 0, "TRANSAQ exact signed zero book");
            Program.Equal(Program.T0, quote.ReceivedAt, "source receipt retained before downstream dispatch");
            ExplicitQuote equalTime = Parse(protocol, "<quotes><quote secid=\"1\"><price>0</price><sell>-1</sell></quote></quotes>").OfType<ExplicitQuote>().Single();
            Program.Check(!equalTime.HasAsk && equalTime.HasBid && equalTime.Sequence > quote.Sequence, "depth delete and equal timestamp order distinguished");
            quote = Parse(protocol, "<quotes><quote secid=\"1\"><price>-0.00001</price><buy>0</buy></quote></quotes>").OfType<ExplicitQuote>().Single();
            Program.Check(!quote.HasBid && !quote.HasAsk, "zero depth quantity cannot retain stale positive liquidity");
            foreach (string malformed in new[] { "bad", "", "-0.5", "-2" })
            {
                object invalidProtocol = Protocol(); SyntheticTransaq invalidRealization = Realization(invalidProtocol);
                int quotes = 0; invalidRealization.ExplicitQuoteEvent += _ => quotes++;
                Invoke(invalidRealization, "DispatchSignedFrame", Capture(invalidProtocol,
                    "<quotes><quote secid=\"1\"><price>0</price><buy>3</buy><sell>4</sell></quote></quotes>"));
                Invoke(invalidRealization, "DispatchSignedFrame", Capture(invalidProtocol,
                    "<quotes><quote secid=\"1\"><price>0</price><buy>" + malformed + "</buy></quote></quotes>"));
                Program.Equal(1, quotes, "malformed present depth quantity never refreshes retained quotes: " + malformed);
                Program.Equal("", invalidRealization.SignedOrderSession, "malformed depth closes source readiness: " + malformed);
            }
            ExplicitAccount account = Parse(protocol, "<positions><united_limits union=\"fixture\"><free>500</free><requirements>200</requirements></united_limits></positions>").OfType<ExplicitAccount>().Single();
            Program.Check(account.FundsAreFree && account.FundsUpdated && account.Current == 500 && account.Blocked == 0, "union free is already free, no double reserve deduction");
            Program.Equal(0, account.Positions.Count, "funds-only update does not refresh inventory");
            account = Parse(protocol, "<mc_portfolio union=\"fixture\"><security secid=\"1\"><seccode>TEST</seccode><balance>-20</balance></security></mc_portfolio>").OfType<ExplicitAccount>().Single();
            Program.Equal(-2m, account.Positions["TEST"], "union pieces normalized by instrument lot");
            Program.Check(!account.FundsUpdated, "mc portfolio valuation is not invented free collateral");
            account = Parse(protocol, "<mc_portfolio union=\"fixture\"/>").OfType<ExplicitAccount>().Single();
            Program.Check(!account.Positions.ContainsKey("TEST"), "missing union row is not inferred zero");
            account = Parse(protocol, "<mc_portfolio union=\"fixture\"><security secid=\"1\"><seccode>TEST</seccode><balance>0</balance></security></mc_portfolio>").OfType<ExplicitAccount>().Single();
            Program.Equal(0m, account.Positions["TEST"], "explicit zero position retained");
            object oldFrame = Capture(protocol, "<positions><united_limits union=\"fixture\"><free>9999</free></united_limits></positions>");
            Invoke(protocol, "Begin", true);
            Program.Equal(0, ((List<object>)Invoke(protocol, "Parse", oldFrame, Resolve)).Count, "old account frame cannot refresh new session");
            object forts = Protocol(false);
            account = Parse(forts, "<positions><forts_position><client>fixture</client><secid>1</secid><seccode>TEST</seccode><totalnet>-2</totalnet></forts_position></positions>").OfType<ExplicitAccount>().Single();
            Program.Equal(-2m, account.Positions["TEST"], "separate FORTS totalnet already contracts");
            account = Parse(forts, "<clientlimits client=\"fixture\"><money_free>500</money_free><money_reserve>200</money_reserve></clientlimits>").OfType<ExplicitAccount>().Single();
            Program.Equal(500m, account.Current, "separate money_free does not subtract reserve again");
            Order order = Order(); CultureInfo prior = CultureInfo.CurrentCulture; CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
            try
            {
                foreach (decimal price in new[] { -0.00001m, 0m, 0.00001m })
                {
                    order.Price = price; XElement command = XElement.Parse((string)Invoke(ProtocolType, "NewOrderCommand", order, Security(), true));
                    Program.Equal(price.ToString(CultureInfo.InvariantCulture), command.Element("price").Value, "culture-independent signed limit " + price);
                    Program.Equal(1, command.Elements("brokerref").Count(), "one broker reference, never duplicate FUT element");
                }
                order.Price = 0.000001m;
                Program.Throws(() => Invoke(ProtocolType, "NewOrderCommand", order, Security(), true), "off-tick signed send rejected");
                order.TypeOrder = OrderPriceType.Market;
                XElement market = XElement.Parse((string)Invoke(ProtocolType, "NewOrderCommand", order, Security(), true));
                Program.Check(market.Element("bymarket") != null && market.Element("price") == null, "market distinguished from zero limit");
            }
            finally { CultureInfo.CurrentCulture = prior; }
        }

        private static void OrdersAndDeltas()
        {
            object protocol = Protocol(); Order order = Order(); Register(protocol, order);
            Order update = Parse(protocol, FullOrder("F2fixture")).OfType<Order>().Single();
            Program.Equal(101, update.NumberUser, "native N remains distinct from transaction 901");
            Program.Equal("901", update.SignedIdentity.Transaction, "transport T recorded separately");
            update = Parse(protocol, "<orders><order><orderno>700</orderno><balance>8</balance></order></orders>").OfType<Order>().Single();
            Program.Check(update.State == OrderStateType.Partial && update.SignedIdentity.Executed == 2, "ordinary venue-keyed partial delta merges retained identity");
            update = Parse(protocol, "<orders><order><orderno>700</orderno><status>cancelled</status><withdrawtime>0</withdrawtime></order></orders>").OfType<Order>().Single();
            Program.Equal(OrderStateType.Pending, update.State, "cancel in progress is not terminal");
            update = Parse(protocol, "<orders><order><orderno>700</orderno><withdrawtime>12:00:01</withdrawtime></order></orders>").OfType<Order>().Single();
            Program.Check(update.State == OrderStateType.Cancel && update.SignedIdentity.Executed == 2, "time-only final cancel delta retains status and balance");
            string cancel = (string)Invoke(protocol, "CancelCommand", order);
            Program.Check(cancel.Contains("<transactionid>901</transactionid>"), "cancel targets proven transport T, not native N");
            Invoke(protocol, "Begin", true);
            Program.Throws(() => Invoke(protocol, "CancelCommand", order), "reconnect never reuses saved T");
            Program.Equal(0, Parse(protocol, "<orders><order><orderno>700</orderno><status>matched</status></order></orders>").Count, "reused V after reconnect cannot independently bind old intent");
            object delayed = Capture(protocol, Fill("F2fixture", time: "23:59:59"), Program.T0.Date.AddHours(23));
            Invoke(protocol, "Invalidate");
            MyTrade late = ((List<object>)Invoke(protocol, "Parse", delayed, Resolve)).OfType<MyTrade>().Single();
            Program.Equal(Program.T0.Date, late.Time.Date, "time-only fill date uses original capture, not processing day");
            Program.Equal(2m, late.Volume, "late owned fill retained after disconnect");
            object concurrent = Protocol();
            Type frameType = Capture(concurrent, "<positions/>").GetType();
            object queue = Activator.CreateInstance(typeof(ConcurrentQueue<>).MakeGenericType(frameType));
            Parallel.For(0, 64, index => Invoke(concurrent, "Enqueue", "<positions/>", Program.T0, queue));
            long[] sequences = ((System.Collections.IEnumerable)queue).Cast<object>()
                .Select(frame => (long)frameType.GetField("Sequence", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(frame)).ToArray();
            Program.Check(sequences.SequenceEqual(sequences.OrderBy(n => n)) && sequences.Distinct().Count() == 64,
                "concurrent source callbacks enqueue in capture sequence order");
            object early = Protocol(); Parse(early, FullOrder("F2early"));
            foreach (string delta in new[] { "<balance>8</balance>", "<status>cancelled</status><withdrawtime>0</withdrawtime>", "<withdrawtime>12:00:01</withdrawtime>" })
            {
                object frame = Capture(early, "<orders><order><orderno>700</orderno>" + delta + "</order></orders>");
                Program.Equal(0, ((List<object>)Invoke(early, "Parse", frame, Resolve)).Count,
                    "unregistered owned delta is buffered until native ownership exists");
                Program.Equal("", (string)Invoke(early, "LegacyPayload", frame),
                    "early owned venue-only delta never leaks into legacy dispatch");
            }
            List<Order> earlyOrders = ((List<object>)Invoke(early, "Register", Order("F2early"), Resolve)).OfType<Order>().ToList();
            Program.Check(earlyOrders.Count == 4 && earlyOrders.Last().State == OrderStateType.Cancel
                && earlyOrders.Last().SignedIdentity.Executed == 2,
                "registration replays full order and partial/cancel deltas in capture order");
            Program.Check(earlyOrders.Select(o => o.SignedIdentity.Sequence).SequenceEqual(
                earlyOrders.Select(o => o.SignedIdentity.Sequence).OrderBy(n => n)), "buffered delta replay preserves original source sequence");
            object oldPending = Protocol(); Parse(oldPending, FullOrder("F2prevenue", "forwarding", venue: "0"));
            object rejectedFrame = Capture(oldPending, "<orders><order transactionid=\"901\"><status>denied</status></order></orders>", Program.T0.AddSeconds(1));
            Invoke(oldPending, "Parse", rejectedFrame, Resolve);
            string originalSession = (string)rejectedFrame.GetType().GetField("Session", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(rejectedFrame);
            Invoke(oldPending, "Begin", true);
            Order prevenue = Order("F2prevenue");
            List<Order> oldReplay = ((List<object>)Invoke(oldPending, "Register", prevenue, Resolve)).OfType<Order>().ToList();
            Program.Check(oldReplay.Count == 2 && oldReplay.Last().State == OrderStateType.Fail,
                "pre-venue T-only terminal remains bound when registration follows a source session switch");
            Program.Check(oldReplay.Last().SignedIdentity.Session == originalSession
                && oldReplay.Last().SignedIdentity.ReceivedAt == Program.T0.AddSeconds(1),
                "old-session replay retains original source generation and receipt");
            Program.Throws(() => Invoke(oldPending, "CancelCommand", prevenue),
                "replayed old T-only terminal never grants current-session cancellation authority");
            object ambiguous = Protocol(); Parse(ambiguous, FullOrder("F2first")); Parse(ambiguous, FullOrder("F2second"));
            Program.Throws(() => Parse(ambiguous, "<orders><order><orderno>700</orderno><balance>8</balance></order></orders>"),
                "ambiguous unregistered same-session venue cannot adopt either reference");
            object unmatched = Protocol(); Parse(unmatched, Fill("F2late"));
            List<object> replay = (List<object>)Invoke(unmatched, "Register", Order("F2late"), Resolve);
            Program.Equal(1, replay.OfType<MyTrade>().Count(), "early persisted reference fill replays after registration");
            Order unknown = (Order)Invoke(unmatched, "ObserveResult", Order("F2late"), null, "lost", Program.T0);
            Program.Equal(OrderStateType.LostAfterActive, unknown.State, "lost response remains unknown");
        }

        private static SyntheticTransaq Realization(object protocol)
        {
            SyntheticTransaq realization = (SyntheticTransaq)AdapterCases.Empty(typeof(SyntheticTransaq));
            AdapterCases.Set(realization, "_signedProtocol", protocol); AdapterCases.Set(realization, "_signedProfile", "Standard union");
            AdapterCases.Set(realization, "_securities", new List<Security> { Security() });
            AdapterCases.Set(realization, "_commandLocker", "offline-signed-command");
            realization.ServerParameters = Enumerable.Repeat<IServerParameter>(null, 16).ToList();
            realization.ServerParameters[15] = new ServerParameterEnum { Value = "Standard union" };
            realization.ServerStatus = ServerConnectStatus.Connect;
            Invoke(realization, "BeginSignedSession"); return realization;
        }

        private static void Dispatch()
        {
            object protocol = Protocol(); SyntheticTransaq realization = Realization(protocol); int sends = 0; int suppressed = 0;
            realization.Command = _ => { sends++; return "<result success=\"true\" transactionid=\"901\"/>"; };
            Order queued = Order(); string session = realization.SignedOrderSession;
            queued.SignedDispatch = new SignedOrderDispatch(new object(), () => realization.SignedOrderSession == session, () => suppressed++);
            Invoke(protocol, "Invalidate"); realization.SendOrder(queued);
            Program.Equal(0, sends, "disconnect before physical send causes no external effect");
            Invoke(protocol, "Begin", true); queued = Order("F2canceled", 102);
            queued.SignedDispatch = new SignedOrderDispatch(new object(), () => true, () => suppressed++);
            queued.SignedDispatch.CancelBeforeSend(); realization.SendOrder(queued);
            Program.Equal(0, sends, "queued cancel suppresses native physical command");
            Order sent = Order("F2sent", 103); List<Order> reports = new List<Order>(); realization.MyOrderEvent += reports.Add;
            sent.SignedDispatch = new SignedOrderDispatch(new object(), () => true, () => suppressed++);
            realization.SendOrder(sent); realization.SendOrder(sent);
            Program.Equal(1, sends, "dispatch authority permits one physical command only");
            Program.Equal(103, sent.NumberUser, "send response never replaces native number");
            Program.Check(reports.Last().State == OrderStateType.Pending, "accepted response awaits broker order facts");
            sent = Order("F2lost", 104); sent.SignedDispatch = new SignedOrderDispatch(new object(), () => true, () => suppressed++);
            realization.Command = _ => { sends++; return null; }; realization.SendOrder(sent);
            Program.Equal(OrderStateType.LostAfterActive, reports.Last().State, "null physical response never declares rejection");
            Program.Check(!sent.SignedDispatch.CancelBeforeSend(), "started unknown cannot be locally canceled as not sent");
        }

        private static AServer ReceiveServer(SyntheticTransaq realization, BotTabSimple tab)
        {
            AServer server = (AServer)AdapterCases.Empty(typeof(TransaqServer));
            AdapterCases.Set(server, "_serverRealization", realization);
            AdapterCases.Set(server, "_ordersToSend", new ConcurrentQueue<Order>());
            AdapterCases.Set(server, "_myTradesToSend", new ConcurrentQueue<MyTrade>());
            AdapterCases.Set(server, "_explicitAccounts", new ConcurrentQueue<ExplicitAccount>());
            AdapterCases.Set(server, "_portfolioToSend", new ConcurrentQueue<List<Portfolio>>());
            AdapterCases.Set(server, "_myTrades", new List<MyTrade>());
            AdapterCases.Set(server, "_explicitQuotes", new ConcurrentDictionary<string, ExplicitQuote>());
            AdapterCases.Set(server, "_ordersToExecute", new ConcurrentQueue<OrderAserverSender>());
            AdapterCases.Set(server, "_serverConnectStatus", ServerConnectStatus.Connect);
            FieldInfo hub = typeof(AServer).GetField("_ordersHub", BindingFlags.Instance | BindingFlags.NonPublic);
            hub.SetValue(server, AdapterCases.Empty(hub.FieldType));
            server.TestValue_CanSendOrdersUp = true; server.TestValue_CanSendMyTradesUp = true;
            realization.MyOrderEvent += order => Invoke(server, "_serverRealization_MyOrderEvent", order);
            realization.MyTradeEvent += trade => Invoke(server, "_serverRealization_MyTradeEvent", trade);
            realization.ExplicitAccountEvent += account => Invoke(server, "RealizationExplicitAccount", account);
            realization.ExplicitQuoteEvent += quote => Invoke(server, "RealizationExplicitQuote", quote);
            server.NewOrderIncomeEvent += order => Invoke(tab.Connector, "ConnectorBot_NewOrderIncomeEvent", order);
            server.NewMyTradeEvent += trade => Invoke(tab.Connector, "ConnectorBot_NewMyTradeEvent", trade);
            tab.Connector.OrderChangeEvent += order => Invoke(tab, "_connector_OrderChangeEvent", order);
            tab.Connector.MyTradeEvent += trade => Invoke(tab, "_connector_MyTradeEvent", trade);
            AdapterCases.Set(tab, "_icebergMaker", AdapterCases.Empty(typeof(IcebergMaker)));
            ChartCandleMaster chart = (ChartCandleMaster)AdapterCases.Empty(typeof(ChartCandleMaster));
            AdapterCases.Set(chart, "_startProgram", StartProgram.IsOsOptimizer); AdapterCases.Set(tab, "_chartMaster", chart);
            return server;
        }

        private static void NativePath()
        {
            BotTabSimple first = AdapterCases.Tab("TEST", out FixtureServerProxy spy);
            BotTabSimple second = AdapterCases.Tab("NEXT", out FixtureServerProxy unused);
            spy.Security.NameClass = "FUT"; AdapterCases.Set(first.Connector, "_securityClass", "FUT");
            spy.Portfolio.Number = "United_fixture"; first.Connector.PortfolioName = "United_fixture";
            object protocol = Protocol(); SyntheticTransaq realization = Realization(protocol);
            AServer server = ReceiveServer(realization, first); List<string> errors = new List<string>();
            using Futures2NativeAdapter adapter = new Futures2NativeAdapter(first, second, null, false, errors.Add);
            adapter.SignedOrdersEnabled = true; adapter.ManualFreeFunds = 210;
            adapter.Engine.Configure(Program.Plan(), new Futures2Policy { IntervalMilliseconds = 0 }, 210, 0);
            adapter.Quotes[0] = Program.Quotes(0, 0)[0]; AdapterCases.Set(adapter, "_now", Program.T0);
            adapter.Reconcile(false); adapter.Engine.Start(true, Program.T0);
            Futures2Action action = adapter.Engine.Decide(adapter.Quotes, Program.T0, true)[0]; Order native = null;
            spy.Execute = order =>
            {
                native = order; action.Intent.ClientKey = "F2native";
                order.SignedIdentity = new SignedOrderIdentity { ClientKey = "F2native" }; realization.RegisterSignedOrder(order);
            };
            Invoke(adapter, "Execute", action);
            void Receive(string xml)
            {
                Invoke(realization, "DispatchSignedFrame", Capture(protocol, xml)); Invoke(server, "DispatchPrivateData");
            }
            Receive(Fill("F2native"));
            Program.Equal(2m, first.PositionsAll.Single().OpenVolume, "real parser/AServer/Connector/Journal early fill creates native exposure");
            Program.Equal(2m, action.Intent.Filled, "same early fill reaches strategy allocations");
            Program.Equal(native.NumberUser, action.Intent.OrderNumber, "native queue retains intent N");
            Receive(Fill("F2native"));
            Program.Equal(2m, action.Intent.Filled, "real AServer replay does not double strategy execution");
            Receive(FullOrder("F2native", "matched", "0"));
            Program.Check(action.Intent.State == Futures2IntentState.Unknown && action.Intent.Remaining == 8,
                "terminal before remaining fill details retains reserve through native journal");
            Receive(Fill("F2native", "8", "801"));
            Program.Equal(10m, first.PositionsAll.Single().OpenVolume, "late terminal fill closes native quantity gap");
            Program.Equal(10m, action.Intent.Filled, "late terminal fill closes projection gap exactly once");
            Program.Equal(Futures2IntentState.Filled, action.Intent.State, "identified terminal fills release uncertainty");
            Receive(Fill("F2foreign", "5", "800"));
            Program.Equal(10m, first.PositionsAll.Single().OpenVolume, "foreign reference sharing venue/trade IDs cannot change owned position");
            adapter.Reconcile(false);
            Futures2Lot lot = adapter.Engine.Data.Book.Lots.Single();
            Futures2Intent close = new Futures2Intent { PlanId = action.Intent.PlanId, Entry = false, Endpoint = 0, Quantity = 3,
                PositionNumber = action.Intent.PositionNumber, Price = 0, Created = Program.T0 };
            close.Allocations.Add(new Futures2Allocation { LevelId = lot.LevelId, LotId = lot.Id, Quantity = 3 });
            adapter.Engine.Data.Book.Record(close);
            spy.Execute = order =>
            {
                close.ClientKey = "F2close"; order.SignedIdentity = new SignedOrderIdentity { ClientKey = close.ClientKey };
                realization.RegisterSignedOrder(order);
            };
            Invoke(adapter, "Execute", new Futures2Action { Intent = close });
            Receive(Fill("F2close", "1", "900", "701").Replace("<buysell>B</buysell>", "<buysell>S</buysell>").Replace("<price>-0.00001</price>", "<price>0</price>"));
            Program.Equal(9m, first.PositionsAll.Single().OpenVolume, "TRANSAQ partial close reduces native position by identified quantity");
            Program.Equal(9m, adapter.Engine.Data.Book.Lots.Sum(l => l.Quantity), "partial close reduces matching allocation inventory");
            Program.Equal(0.00001m, adapter.Engine.Data.Book.Realized, "signed entry to zero partial exit realizes exact economic result");
            Receive(FullOrder("F2close", "cancelled", "2", "701", "902", "<withdrawtime>12:00:04</withdrawtime>")
                .Replace("<quantity>10</quantity>", "<quantity>3</quantity>").Replace("<buysell>B</buysell>", "<buysell>S</buysell>"));
            Program.Check(close.State == Futures2IntentState.Canceled && close.Remaining == 0,
                "confirmed partial-close cancellation releases close reservation while retaining inventory");
            Program.Equal(9m, adapter.Engine.Data.Book.Available(adapter.Engine.Data.Book.Lots.Single()), "canceled partial close leaves remaining owned lots available");
            Program.Equal(PositionStateType.ClosingFail, first.PositionsAll.Single().State, "fixture exercises repeated-terminal close guard");
            Receive("<orders><order><orderno>701</orderno><balance>0</balance></order></orders>");
            Program.Check(close.State == Futures2IntentState.Unknown && close.ReportedExecuted == 3 && close.Remaining == 2,
                "repeated terminal close with increased execution retains missing-fill reservation");
            Program.Check(!adapter.Reconciled && adapter.Engine.Data.Book.Lots.Sum(l => l.Quantity) == 9,
                "execution gap closes authority without inventing delayed close fills");
            Program.Equal(7m, adapter.Engine.Data.Book.Available(adapter.Engine.Data.Book.Lots.Single()),
                "unknown close reserves remaining allocation until identified fills");
            string delayedClose = Fill("F2close", "2", "901", "701").Replace("<buysell>B</buysell>", "<buysell>S</buysell>").Replace("<price>-0.00001</price>", "<price>0</price>");
            Receive(delayedClose); Receive(delayedClose);
            Program.Check(close.State == Futures2IntentState.Filled && first.PositionsAll.Single().OpenVolume == 7,
                "late close details complete reported quantity once through actual native Journal");
            Program.Equal(0.00003m, adapter.Engine.Data.Book.Realized, "duplicate delayed close does not double economic result");
            Program.Equal(0, errors.Count, "native TRANSAQ integration has no hidden adapter exceptions");
        }

        private static void LiveProfilePath()
        {
            BotTabSimple first = AdapterCases.Tab("TEST", out FixtureServerProxy spy);
            BotTabSimple second = AdapterCases.Tab("NEXT", out FixtureServerProxy unused);
            spy.Security.NameClass = "FUT"; spy.Security.NameId = "1";
            AdapterCases.Set(first.Connector, "_securityClass", "FUT");
            spy.Portfolio.Number = "United_fixture"; first.Connector.PortfolioName = "United_fixture";
            object protocol = Protocol(); SyntheticTransaq realization = Realization(protocol);
            AServer server = ReceiveServer(realization, first);
            AdapterCases.Set(server, "_securities", new List<Security> { spy.Security });
            AdapterCases.Set(server, "_frequentlyUsedSecurities", new List<Security>());
            AdapterCases.Set(server, "_securitiesDictionary", new Dictionary<string, Security>());
            AdapterCases.Set(server, "_portfolios", new List<Portfolio> { spy.Portfolio });
            AdapterCases.Set(server, "_alreadyLoadAwaitInfoFromServerPermission", true);
            AdapterCases.Set(server, "HasFirstOrderMessageBeenSent", true);
            server.LastStartServerTime = DateTime.Now.AddDays(-1); server.WaitTimeToTradeAfterFirstStart = 0;
            AdapterCases.Set(first.Connector, "_myServer", server); first.Connector.ServerType = ServerType.Transaq;
            List<string> errors = new List<string>(); int sends = 0;
            realization.Command = _ => { sends++; return "<result success=\"true\" transactionid=\"901\"/>"; };
            string directory = Path.Combine(Path.GetTempPath(), "Futures2-transaq-" + Guid.NewGuid().ToString("N"));
            Futures2Store store = new Futures2Store(Path.Combine(directory, "state.json"));
            using Futures2NativeAdapter adapter = new Futures2NativeAdapter(first, second, store, true, errors.Add);
            adapter.SignedOrdersEnabled = true; adapter.UsePortfolioFunds = true;
            adapter.Engine.Configure(Program.Plan(), new Futures2Policy { IntervalMilliseconds = 0, MaxActions = 1 }, 210, 0);
            void Receive(string xml, DateTime? time = null)
            {
                Invoke(realization, "DispatchSignedFrame", Capture(protocol, xml, time ?? DateTime.Now));
                Invoke(server, "DispatchPrivateData"); Invoke(server, "DispatchExplicitQuotes");
            }
            Receive("<positions><united_limits union=\"fixture\"><free>1000</free></united_limits></positions>");
            Receive("<mc_portfolio union=\"fixture\"><security secid=\"1\"><seccode>TEST</seccode><balance>0</balance></security></mc_portfolio>");
            Receive("<quotes><quote secid=\"1\"><price>0</price><buy>10</buy><sell>10</sell></quote></quotes>");
            Program.Throws(() => adapter.Reconcile(true), "live TRANSAQ broker-free mode cannot silently authorize grid");
            Program.Equal(0, sends, "unsupported broker-free funds mode has zero commands");
            Program.Check(adapter.Engine.Data.Reason.Contains("manual funds"), "unsupported funds mode has explicit operator diagnosis");
            adapter.ManualFreeFunds = 210; adapter.Reconcile(true); adapter.Pump();
            Program.Check(adapter.CanConfirmNativeState, "manual envelope with actual source evidence permits reconciliation");
            DateTime equal = DateTime.Now; string session = realization.SignedOrderSession;
            ExplicitAccount Account(long sequence, decimal net) => new ExplicitAccount(new Portfolio { Number = "United_fixture" }, false,
                new[] { new PositionOnBoard { SecurityNameCode = "TEST", ValueCurrent = net } }, equal, true, session, sequence);
            Invoke(adapter, "OnExplicitAccount", 0, Account(20, 1));
            Program.Check(!adapter.CanConfirmNativeState, "actual source net mismatch closes account gate");
            Invoke(adapter, "OnExplicitAccount", 0, Account(21, 0));
            Program.Check(adapter.CanConfirmNativeState, "equal timestamp newer source sequence updates net");
            Invoke(adapter, "OnExplicitAccount", 0, Account(20, 1));
            Program.Check(adapter.CanConfirmNativeState, "reordered source account cannot overwrite newer net");
            AdapterCases.Set(protocol, "_sequence", 21L);
            adapter.Engine.Start(true, DateTime.Now);
            Futures2Action action = adapter.Engine.Decide(adapter.Quotes, DateTime.Now, true)[0];
            Invoke(adapter, "Execute", action);
            ConcurrentQueue<OrderAserverSender> queue = (ConcurrentQueue<OrderAserverSender>)typeof(AServer)
                .GetField("_ordersToExecute", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(server);
            Program.Check(queue.TryDequeue(out OrderAserverSender queued), "actual native AServer queue holds submitted intent");
            Program.Check(action.Intent.ClientKey.Length > 0 && queued.Order.SignedDispatch != null, "live adapter installs persisted correlation and physical-send authority");
            adapter.SignedOrdersEnabled = false;
            Invoke(server, "ExecuteOrderInRealization", queued);
            Program.Equal(0, sends, "revoked robot authority suppresses queued AServer effect");
            Program.Check(!action.Intent.CanFill && action.Intent.Remaining == 0, "proven unsent command releases only its local reserve");
            Program.Equal(0, adapter.Engine.Data.Book.Rejections, "local queue suppression is not counted as broker rejection");
            adapter.SignedOrdersEnabled = true;
            for (int changedControl = 0; changedControl < 3; changedControl++)
            {
                adapter.Reconcile(true); adapter.Engine.Start(true, DateTime.Now);
                Futures2Action stale = adapter.Engine.Decide(adapter.Quotes, DateTime.Now, true)[0]; Invoke(adapter, "Execute", stale);
                Program.Check(queue.TryDequeue(out OrderAserverSender staleCommand), "runtime-change fixture captures native outgoing queue");
                if (changedControl == 0) adapter.ManualFreeFunds = 0;
                else if (changedControl == 1) spy.Security.Lot = 2;
                else
                {
                    ServerParameterEnum profile = (ServerParameterEnum)realization.ServerParameters[15];
                    profile.Value = "Off"; profile.Value = "Standard union";
                    Program.Equal("", realization.SignedOrderSession, "profile round-trip latches revocation before any adapter Pump");
                }
                Invoke(server, "ExecuteOrderInRealization", staleCommand);
                Program.Check(!stale.Intent.CanFill && sends == 0, "final dispatch rechecks changed funds/contract metadata " + changedControl);
                adapter.ManualFreeFunds = 210; spy.Security.Lot = 1;
                if (changedControl == 2)
                {
                    Invoke(realization, "BeginSignedSession"); adapter.Pump();
                    Receive("<positions><united_limits union=\"fixture\"><free>1000</free></united_limits></positions>");
                    Receive("<mc_portfolio union=\"fixture\"><security secid=\"1\"><seccode>TEST</seccode><balance>0</balance></security></mc_portfolio>");
                    Receive("<quotes><quote secid=\"1\"><price>0</price><buy>10</buy><sell>10</sell></quote></quotes>");
                    Program.Check(realization.SignedOrderSession != session && !adapter.Reconciled,
                        "only explicit connection generation restart restores source, still requiring reconciliation");
                }
            }
            adapter.Reconcile(true); adapter.Engine.Start(true, DateTime.Now);
            action = adapter.Engine.Decide(adapter.Quotes, DateTime.Now, true)[0]; Invoke(adapter, "Execute", action);
            Program.Check(queue.TryDequeue(out queued), "replacement local intent enters native queue");
            realization.Command = _ =>
            {
                Futures2Intent durable = store.Load().Book.Intents.Single(i => i.Id == action.Intent.Id);
                Program.Check(durable.ClientKey == queued.Order.SignedIdentity.ClientKey && durable.OrderNumber == queued.Order.NumberUser
                    && durable.PositionNumber != 0, "native N, position and broker reference flushed before physical command");
                sends++; return null;
            };
            Invoke(server, "ExecuteOrderInRealization", queued); Invoke(server, "DispatchPrivateData");
            Program.Equal(1, sends, "actual AServer dispatch performs one synthetic physical command");
            Program.Equal(Futures2IntentState.Unknown, action.Intent.State, "lost response preserves live-adapter unknown reserve");
            ((ServerParameterEnum)realization.ServerParameters[15]).Value = "Off";
            Invoke(server, "ExecuteOrderInRealization", queued); Invoke(server, "DispatchPrivateData");
            Program.Check(action.Intent.State == Futures2IntentState.Unknown && action.Intent.Remaining > 0,
                "duplicate queued dispatch after profile loss cannot release a started unknown");
            Program.Equal(1, sends, "unknown send is never automatically retried");
            Program.Check(errors.All(message => !message.Contains("Exception")), "live profile fixture has no hidden adapter exceptions: " + string.Join(" | ", errors));

            // Recover both persisted native order and campaign. A new transport registry has no current T.
            string nativeRecord = queued.Order.GetStringForSave().ToString();
            string lostIntent = action.Intent.Id; int nativeNumber = queued.Order.NumberUser;
            adapter.Dispose();
            Order restored = new Order(); restored.SetOrderFromString(nativeRecord);
            Position position = first.PositionsAll.Single(p => p.OpenOrders.Any(o => o.NumberUser == nativeNumber));
            position.OpenOrders[position.OpenOrders.FindIndex(o => o.NumberUser == nativeNumber)] = restored;
            protocol = Protocol(); AdapterCases.Set(realization, "_signedProtocol", protocol);
            ((ServerParameterEnum)realization.ServerParameters[15]).Value = "Standard union";
            Invoke(realization, "BeginSignedSession");
            using Futures2NativeAdapter recovered = new Futures2NativeAdapter(first, second, store, true, errors.Add);
            recovered.SignedOrdersEnabled = true; recovered.ManualFreeFunds = 210;
            Program.Check(restored.SignedDispatch == null, "native reload never restores transient send authority");
            Program.Equal(1, sends, "restart registration performs no resend");
            Receive("<positions><united_limits union=\"fixture\"><free>1000</free></united_limits></positions>");
            Receive("<mc_portfolio union=\"fixture\"><security secid=\"1\"><seccode>TEST</seccode><balance>0</balance></security></mc_portfolio>");
            Receive("<quotes><quote secid=\"1\"><price>0</price><buy>10</buy><sell>10</sell></quote></quotes>");
            Program.Throws(() => recovered.Reconcile(true), "restart keeps unknown native outcome reserved despite fresh account");
            Futures2Intent recoveredIntent = recovered.Engine.Data.Book.Intents.Single(i => i.Id == lostIntent);
            Program.Check(recoveredIntent.Remaining > 0, "unknown reserve survives both native and campaign persistence");
            Program.Throws(() => recovered.ResolveAbsent(lostIntent, 0, false), "absence resolution requires explicit operator evidence");
            recovered.ResolveAbsent(lostIntent, 0, true);
            Program.Check(recoveredIntent.State == Futures2IntentState.Canceled && recoveredIntent.Remaining == 0,
                "operator-verified absent unknown has an explicit no-fabricated-fill resolution path");
            recovered.Reconcile(true);
            Program.Check(recovered.CanConfirmNativeState, "resolved absent order still requires and passes full fresh reconciliation");
            Program.Equal(1, sends, "owner resolution itself sends no broker command");

            // A late real fill is economic evidence even after an owner attestation and transport invalidation.
            object late = Capture(protocol, Fill(recoveredIntent.ClientKey));
            object staleQuote = Capture(protocol, "<quotes><quote secid=\"1\"><price>0</price><buy>99</buy><sell>99</sell></quote></quotes>");
            Invoke(protocol, "Begin", true); recovered.Pump();
            Invoke(realization, "DispatchSignedFrame", staleQuote); Invoke(server, "DispatchExplicitQuotes");
            Program.Check(!recovered.Quotes.ContainsKey(0), "previous-session quote cannot restore live readiness");
            AdapterCases.Set(server, "_serverConnectStatus", ServerConnectStatus.Disconnect);
            Invoke(realization, "DispatchSignedFrame", late); Invoke(server, "DispatchPrivateData");
            Program.Equal(2m, recoveredIntent.Filled, "late owned fill survives disconnected Connector and reaches recovered allocations");
            Program.Equal(2m, position.OpenVolume, "late owned fill preserved in recovered native Journal");
            Program.Equal(PositionStateType.Open, position.State, "late signed entry reopens native failed/canceled position state");
            Invoke(realization, "DispatchSignedFrame", late); Invoke(server, "DispatchPrivateData");
            Program.Equal(2m, recoveredIntent.Filled, "recovered late fill replay remains idempotent");
            Program.Check(!recovered.Reconciled && sends == 1, "late old-session exposure cannot silently resume trading");
            AdapterCases.Set(server, "_serverConnectStatus", ServerConnectStatus.Connect);
            Receive(FullOrder(recoveredIntent.ClientKey, "cancelled", "8", extra: "<withdrawtime>12:00:03</withdrawtime>"));
            Receive("<mc_portfolio union=\"fixture\"><security secid=\"1\"><seccode>TEST</seccode><balance>20</balance></security></mc_portfolio>");
            Receive("<positions><united_limits union=\"fixture\"><free>986</free></united_limits></positions>");
            Receive("<quotes><quote secid=\"1\"><price>0</price><buy>10</buy><sell>10</sell></quote></quotes>");
            recovered.Reconcile(true);
            Program.Check(recovered.CanConfirmNativeState, "new session requires fresh terminal, position and quote evidence");
            object beforeFill = Capture(protocol, "<mc_portfolio union=\"fixture\"><security secid=\"1\"><seccode>TEST</seccode><balance>30</balance></security></mc_portfolio>", DateTime.Now);
            Receive(Fill(recoveredIntent.ClientKey, "1", "801"));
            Invoke(realization, "DispatchSignedFrame", beforeFill); Invoke(server, "DispatchPrivateData");
            Program.Check(!recovered.CanConfirmNativeState, "position captured before current fill cannot reconcile by late delivery time");
            Receive("<mc_portfolio union=\"fixture\"><security secid=\"1\"><seccode>TEST</seccode><balance>30</balance></security></mc_portfolio>");
            Program.Check(recovered.CanConfirmNativeState, "post-fill source position restores quantity agreement");
            Receive("<quotes><quote secid=\"1\"><price>0</price><buy>10</buy><sell>10</sell></quote></quotes>", DateTime.Now.AddMinutes(-2));
            Program.Check(!recovered.Quotes[0].Valid(DateTime.Now, recovered.Engine.Data.Policy.FreshnessSeconds),
                "queued current-session quote retains stale original age");
            recovered.Dispose();
            Program.Equal(3m, store.Load().Book.Intents.Single(i => i.Id == lostIntent).Filled, "late fill checkpoint survives subsequent reload");
            foreach (string suffix in new[] { "", ".bak", ".tmp" }) File.Delete(Path.Combine(directory, "state.json") + suffix);
            Directory.Delete(directory); // This unique fixture directory is now empty; no recursive deletion.
        }
    }
}
