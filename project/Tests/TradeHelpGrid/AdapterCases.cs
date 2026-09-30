using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using OsEngine.Candles;
using OsEngine.Entity;
using OsEngine.Market;
using OsEngine.Market.Connectors;
using OsEngine.Market.Servers;
using OsEngine.Market.Servers.Alor;
using OsEngine.OsTrader.Grids.Futures2;
using OsEngine.OsTrader.Panels.Tab;
using OsEngine.OsTrader.Panels.Tab.Internal;
using OsEngine.Robots.MyBots;

namespace OsEngine.TradeHelpGrid.Tests
{
    public interface IFixtureServer : IServer, IExplicitAccountSource { }
    /// <summary>Spy IServer proxy only; no native server constructors or external calls.</summary>
    public class FixtureServerProxy : DispatchProxy
    {
        public Security Security;
        public Portfolio Portfolio;
        public Action<Order> Execute;
        public int Cancels;
        public ServerConnectStatus Status = ServerConnectStatus.Connect;
        public bool AccountsSupported = true;
        public Action<string> ConnectionChanged;
        public Action<ExplicitAccount> AccountChanged;
        public readonly List<string> Unexpected = new List<string>();
        protected override object Invoke(MethodInfo method, object[] args)
        {
            if (method.Name == "add_ConnectStatusChangeEvent") { ConnectionChanged += (Action<string>)args[0]; return null; }
            if (method.Name == "remove_ConnectStatusChangeEvent") { ConnectionChanged -= (Action<string>)args[0]; return null; }
            if (method.Name == "add_ExplicitAccountEvent") { AccountChanged += (Action<ExplicitAccount>)args[0]; return null; }
            if (method.Name == "remove_ExplicitAccountEvent") { AccountChanged -= (Action<ExplicitAccount>)args[0]; return null; }
            if (method.Name.StartsWith("add_") || method.Name.StartsWith("remove_")) return null;
            switch (method.Name)
            {
                case "get_ServerStatus": return Status;
                case "get_ServerType": return ServerType.Optimizer;
                case "get_HasExplicitAccountUpdates": return AccountsSupported;
                case "get_ServerTime": return Program.T0;
                case "get_LastStartServerTime": return Program.T0.AddDays(-1);
                case "GetSecurityForName": return Security;
                case "GetPortfolioForName": return Portfolio;
                case "get_Securities": return new List<Security> { Security };
                case "get_Portfolios": return new List<Portfolio> { Portfolio };
                case "ExecuteOrder": Execute?.Invoke((Order)args[0]); return null;
                case "CancelOrder": Cancels++; return null;
            }
            Unexpected.Add(method.Name);
            return method.ReturnType == typeof(void) ? null : method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null;
        }
    }

    /// <summary>Native journal and adapter component paths without creating WPF, settings workers or sessions.</summary>
    internal static class AdapterCases
    {
        internal static void Run()
        {
            DurableBeforeSend(); CreditsAndRuntimeSettings(); AccountAndLifecycle(); RiskCheckpoint(); RealizationPresence();
        }
        private static ExplicitAccount Frame(Portfolio portfolio) => new ExplicitAccount(portfolio, true, portfolio.GetPositionOnBoard(), DateTime.Now);
        internal static object Empty(Type type) => RuntimeHelpers.GetUninitializedObject(type);
        internal static void Set(object target, string name, object value)
        {
            Type type = target.GetType(); FieldInfo field = null;
            while (type != null && field == null) { field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public); type = type.BaseType; }
            if (field == null) throw new MissingFieldException(target.GetType().Name, name);
            field.SetValue(target, value);
        }
        internal static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
        private static object Nested(object target, string field)
        {
            FieldInfo info = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object value = Empty(info.FieldType); info.SetValue(target, value); return value;
        }
        internal static BotTabSimple Tab(string instrument, out FixtureServerProxy spy)
        {
            IServer server = DispatchProxy.Create<IFixtureServer, FixtureServerProxy>(); spy = (FixtureServerProxy)(object)server;
            spy.Security = new Security { Name = instrument, NameClass = "TEST", PriceStep = 0.00001m, PriceStepCost = 0.00001m,
                VolumeStep = 1, MinTradeAmount = 1, DecimalsVolume = 0, Lot = 1 };
            spy.Portfolio = new Portfolio { Number = "fixture", ServerUniqueName = "fixture", ValueCurrent = 10000, PositionOnBoard = new List<PositionOnBoard>() };
            BotTabSimple tab = (BotTabSimple)Empty(typeof(BotTabSimple));
            ConnectorCandles connector = (ConnectorCandles)Nested(tab, "_connector");
            Set(connector, "_myServer", server); Nested(connector, "_mySeries");
            Set(connector, "_securityName", instrument); Set(connector, "_securityClass", "TEST");
            connector.PortfolioName = "fixture"; connector.ServerFullName = "fixture"; connector.ServerType = ServerType.Optimizer;
            connector.StartProgram = StartProgram.IsOsOptimizer;
            connector.TimeFrameBuilder = (TimeFrameBuilder)Empty(typeof(TimeFrameBuilder)); Set(connector.TimeFrameBuilder, "_timeFrame", TimeFrame.Min1);
            Set(tab, "_security", spy.Security); Set(tab, "_portfolio", spy.Portfolio);
            tab.StartProgram = StartProgram.IsOsOptimizer; tab.TabName = instrument; tab.BotClassName = "Futures2Grid";
            tab._dealCreator = new PositionCreator();
            object journal = Nested(tab, "_journal"); object controller = Nested(journal, "_positionController");
            Set(controller, "_startProgram", StartProgram.IsOsOptimizer); Set(controller, "_dealsLocker", "offline-" + Guid.NewGuid());
            Set(controller, "_deals", new List<Position>()); Set(controller, "_openPositions", new List<Position>()); Set(controller, "_emptyList", new List<Position>());
            object manual = Nested(tab, "ManualPositionSupport"); Set(manual, "OrderTypeTime", OrderTypeTime.Specified);
            return tab;
        }
        private static void DurableBeforeSend()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Futures2-native-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            Futures2NativeAdapter adapter = null;
            try
            {
                BotTabSimple first = Tab("TEST", out FixtureServerProxy spy); BotTabSimple second = Tab("NEXT", out FixtureServerProxy unused);
                Futures2Store store = new Futures2Store(Path.Combine(directory, "state.json")); List<string> errors = new List<string>();
                adapter = new Futures2NativeAdapter(first, second, store, false, errors.Add);
                adapter.SignedOrdersEnabled = true; adapter.ManualFreeFunds = 210;
                adapter.Engine.Configure(Program.Plan(), new Futures2Policy { IntervalMilliseconds = 0, MaxActions = 3 }, 210, 0);
                adapter.Quotes[0] = Program.Quotes(0, 0)[0]; Set(adapter, "_now", Program.T0);
                adapter.Reconcile(false); adapter.Engine.Start(true, Program.T0);
                List<Futures2Action> batch = adapter.Engine.Decide(adapter.Quotes, Program.T0, true);
                Futures2Action action = batch[0];
                bool persistedBeforeSend = false; int submits = 0;
                spy.Execute = order =>
                {
                    submits++;
                    Futures2Checkpoint persisted = store.Load(); Futures2Intent intent = persisted.Book.Intents.Single(i => i.OrderNumber == order.NumberUser);
                    persistedBeforeSend = intent.OrderNumber == order.NumberUser && intent.PositionNumber != 0
                        && intent.State == Futures2IntentState.SubmitPending && first.PositionsAll.Single().Number == intent.PositionNumber;
                    order.NumberMarket = "venue"; order.State = OrderStateType.Active;
                    Call(adapter, "OnOrder", 0, order);
                };
                Call(adapter, "Execute", action);
                Program.Check(persistedBeforeSend, "native identity durable before server dispatch");
                Program.Equal(1, submits, "reentrant acknowledgement does not duplicate submit");
                Program.Equal(Futures2IntentState.Working, action.Intent.State, "reentrant callback finds already bound intent");
                Program.Equal(0m, first.PositionsAll[0].OpenOrders[0].Price, "native gateway preserves literal zero");
                Program.Check(first.PositionsAll[0].UsesSignedPrices, "native journal owns signed position");
                Program.Equal(0, spy.Cancels, "signed gateway has no hidden cancel-before-send");
                int count = first.PositionsAll.Count;
                Program.Throws(() => first.SubmitSignedOrder(null, Side.Buy, 1, 0.000001m, false, 2, "invalid", (_, _) => { }), "native off-tick rejected before dispatch");
                Program.Equal(count, first.PositionsAll.Count, "invalid request does not create native journal position");
                Program.Throws(() => first.SubmitSignedOrder(null, Side.Buy, 0.1m, 0, false, 2, "bad-volume", (_, _) => { }), "native quantity step checked before journal mutation");
                Program.Equal(count, first.PositionsAll.Count, "invalid quantity has no journal side effect");
                Order native = first.PositionsAll[0].OpenOrders[0]; native.State = OrderStateType.LostAfterActive;
                Call(adapter, "OnOrder", 0, native);
                Program.Check(!adapter.Reconciled && action.Intent.State == Futures2IntentState.Unknown, "LostAfterActive closes readiness and preserves reservation");
                Call(adapter, "Execute", batch[1]);
                Program.Equal(1, submits, "fault during batch prevents remaining native submissions");
                Program.Equal(0, adapter.Engine.Data.Book.Rejections, "local suppressed submit is not a broker rejection");
                Program.Check(errors.Count == 0, "native fixture has no swallowed gateway error");
            }
            finally
            {
                adapter?.Dispose();
                foreach (string file in new[] { "state.json", "state.json.bak", "state.json.tmp" }) File.Delete(Path.Combine(directory, file));
                Directory.Delete(directory);
            }
        }
        internal static Futures2Grid Robot(Futures2NativeAdapter adapter, BotTabSimple[] tabs, string identity)
        {
            Futures2Grid robot = (Futures2Grid)Empty(typeof(Futures2Grid));
            Set(robot, "_adapter", adapter); Set(robot, "_tabs", tabs); Set(robot, "_identity", identity);
            Set(robot, "_settings", new Dictionary<string, IIStrategyParameter>()); Set(robot, "_peerKey", "");
            Set(robot, "_configuration", ""); Set(robot, "_gridConfiguration", "");
            Set(robot, "<Parameters>k__BackingField", new List<IIStrategyParameter>());
            Set(robot, "StartProgram", StartProgram.IsOsOptimizer);
            Call(robot, "Add", "Accept coordination", true, "fixture"); Call(robot, "Add", "Portfolio leader", false, "fixture");
            Call(robot, "Add", "Portfolio participants", "", "fixture");
            Call(robot, "Add", "Capital", 210m, "fixture"); Call(robot, "Add", "Regime", "Off", "fixture");
            Call(robot, "Add", "Signed order capability selected", true, "fixture");
            Call(robot, "Add", "Use portfolio available funds", false, "fixture"); Call(robot, "Add", "Manual available funds", 210m, "fixture");
            Call(robot, "Add", "External net endpoint 0", 0m, "fixture"); Call(robot, "Add", "External net endpoint 1", 0m, "fixture");
            Call(robot, "AddObject", "Grid", Program.Input(), new[] { "Instrument", "Tick" });
            Call(robot, "AddObject", "Policy", new Futures2Policy(), Array.Empty<string>());
            Set(robot, "_manualCapital", 210m); Set(robot, "_configuration", Call(robot, "Fingerprint")); Set(robot, "_gridConfiguration", Call(robot, "GridFingerprint"));
            return robot;
        }
        private static void CreditsAndRuntimeSettings()
        {
            BotTabSimple first = Tab("TEST", out FixtureServerProxy spy); BotTabSimple second = Tab("NEXT", out FixtureServerProxy unused);
            List<string> errors = new List<string>();
            using Futures2NativeAdapter adapter = new Futures2NativeAdapter(first, second, null, false, errors.Add);
            adapter.Engine.Configure(Program.Plan(), new Futures2Policy(), 210, 0); Set(adapter, "_now", Program.T0);
            string identity = "credit-" + Guid.NewGuid().ToString("N"); string key = "Tester/" + identity;
            Futures2Grid robot = Robot(adapter, new[] { first, second }, identity);
            try
            {
                Call(robot, "Coordinate");
                Futures2Coordination.Send(key, new Futures2PeerCommand { Id = "fund", Source = "source", Currency = "RUB", Amount = 70 });
                Futures2Coordination.Send(key, new Futures2PeerCommand { Id = "fund", Source = "source", Currency = "RUB", Amount = 140 });
                Call(robot, "Coordinate");
                Program.Equal(350m, adapter.Engine.Data.Capital, "cumulative credits retained before pending plan publication");
                Program.Equal(140m, adapter.Engine.Data.FundingDelta, "cumulative funding delta");
                Program.Equal(140m, adapter.Engine.Data.Receipts["fund"], "receipt acknowledges durable cumulative credit");
                Program.Equal(350m, adapter.Engine.Data.PendingCapital, "pending capital includes both credits");
                Futures2Coordination.Send(key, new Futures2PeerCommand { Id = "fund", Source = "source", Currency = "RUB", Amount = 140 });
                Futures2Coordination.Send(key, new Futures2PeerCommand { Id = "fund", Source = "source", Currency = "RUB", Amount = 70 });
                Call(robot, "Coordinate"); Program.Equal(350m, adapter.Engine.Data.Capital, "duplicate and older cumulative credit ignored");
                ((StrategyParameterBool)robot.Parameters.Single(p => p.Name == "Policy.ForbidLong")).ValueBool = true;
                Set(adapter, "_processing", true); Call(robot, "SettingsChanged"); Set(adapter, "_processing", false);
                Program.Equal(350m, adapter.Engine.Data.PendingCapital, "policy edit retains pending transferred capital");
                Program.Equal(350m, adapter.Engine.Data.PendingPlan.Input.Budget, "policy edit retains funded plan geometry");
                Futures2Coordination.Send(key, new Futures2PeerCommand { Id = "new-revoke", Source = "leader", SourceCampaign = "epoch", Sequence = 2, Currency = "RUB", Reduce = true, Revoke = true });
                Futures2Coordination.Send(key, new Futures2PeerCommand { Id = "old-close", Source = "leader", SourceCampaign = "epoch", Sequence = 1, Currency = "RUB", Reduce = true });
                Call(robot, "Coordinate");
                Program.Check(adapter.Engine.Data.RecoverReduction && !adapter.Engine.Data.Reducing, "stale group close cannot override newer recovery");
                Program.Check(adapter.Engine.Data.GroupReceipts.Contains("old-close"), "stale group command acknowledged without execution");
                adapter.Engine.Flatten("credit survives emergency");
                Program.Equal(350m, adapter.Engine.Data.Capital, "emergency cannot erase acknowledged credit");
                Program.Equal(140m, adapter.Engine.Data.Receipts["fund"], "receipt survives canceled pending plan");
                Futures2Checkpoint copy = Futures2Commands.Copy(adapter.Engine.Data);
                Program.Equal(140m, copy.FundingDelta, "funding ledger survives checkpoint copy");
            }
            finally { Futures2Coordination.Remove(key); }
        }
        private static void AccountAndLifecycle()
        {
            BotTabSimple first = Tab("TEST", out FixtureServerProxy spy); BotTabSimple second = Tab("NEXT", out FixtureServerProxy unused);
            Futures2NativeAdapter adapter = new Futures2NativeAdapter(first, second, null, true, _ => { });
            try
            {
                adapter.Engine.Configure(Program.Plan(), new Futures2Policy(), 210, 0);
                adapter.Quotes[0] = Program.Quotes(time: DateTime.Now)[0];
                spy.Portfolio.PositionOnBoard.Add(new PositionOnBoard { SecurityNameCode = "TEST", ValueCurrent = 0 });
                ExplicitAccount frame = Frame(spy.Portfolio);
                spy.Portfolio.ValueBlocked = 9990;
                Program.Equal(0m, frame.Blocked, "account capture detached from native later mutation");
                spy.AccountChanged(frame); adapter.Reconcile(true);
                Program.Check(adapter.Reconciled, "live component reconciles source-captured empty account");
                spy.AccountsSupported = false;
                Program.Throws(() => adapter.Reconcile(true), "unsupported account realization fails closed despite fresh cached values");
                spy.AccountsSupported = true; adapter.Reconcile(true);
                DateTime[] accountTimes = (DateTime[])typeof(Futures2NativeAdapter).GetField("_accountAt", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(adapter);
                DateTime before = accountTimes[0];
                spy.AccountChanged(Frame(new Portfolio { Number = "another", ValueCurrent = 100, PositionOnBoard = new List<PositionOnBoard>() }));
                Program.Equal(before, accountTimes[0], "another account cannot refresh selected account receipt");
                spy.AccountChanged(Frame(new Portfolio { Number = "fixture", ValueCurrent = 10000, PositionOnBoard = new List<PositionOnBoard>() }));
                Program.Equal(before, accountTimes[0], "cash-only receipt cannot refresh missing instrument row");
                DateTime[] fillTimes = (DateTime[])typeof(Futures2NativeAdapter).GetField("_fillAt", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(adapter);
                fillTimes[0] = DateTime.Now.AddSeconds(1);
                Program.Throws(() => adapter.Reconcile(true), "account captured before last fill cannot be accepted on explicit reconcile");
                fillTimes[0] = DateTime.MinValue; adapter.Reconcile(true);
                spy.ConnectionChanged("Connect"); Program.Check(adapter.Reconciled, "duplicate connect notification does not reset ownership");
                adapter.UsePortfolioFunds = true; spy.AccountChanged(Frame(spy.Portfolio)); adapter.Pump();
                Program.Equal(10m, adapter.Quotes[0].FreeMargin.Value, "funds refreshed from account callback without a quote");
                spy.Status = ServerConnectStatus.Disconnect; spy.ConnectionChanged("Disconnect");
                Program.Check(!adapter.Reconciled && adapter.Quotes.Count == 0, "network disconnect invalidates quote and ownership readiness");
                spy.Status = ServerConnectStatus.Connect; spy.ConnectionChanged("Connect");
                Program.Check(!adapter.Reconciled, "reconnect cannot silently resume");
                adapter.Dispose();
                Program.Check(spy.AccountChanged == null && spy.ConnectionChanged == null, "dispose releases exact source subscriptions");
                int count = adapter.Engine.Data.Book.Intents.Count; adapter.Pump();
                Program.Equal(count, adapter.Engine.Data.Book.Intents.Count, "disposed adapter cannot create new intents");
            }
            finally { adapter.Dispose(); }
        }
        private static void RiskCheckpoint()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Futures2-risk-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            Futures2NativeAdapter adapter = null;
            try
            {
                BotTabSimple first = Tab("TEST", out FixtureServerProxy unused); BotTabSimple second = Tab("NEXT", out FixtureServerProxy other);
                Futures2Store store = new Futures2Store(Path.Combine(directory, "state.json"));
                adapter = new Futures2NativeAdapter(first, second, store, false, _ => { }); Set(adapter, "_now", Program.T0);
                adapter.Engine.Configure(Program.Plan(), new Futures2Policy { Trailing = true, TrailTarget = 100, TrailStep = 1,
                    ForbidEntries = true, HjWidth = 1, IntervalMilliseconds = 0 }, 210, 0);
                adapter.Quotes[0] = Program.Quotes(0, 0)[0]; adapter.Reconcile(false); adapter.Engine.Start(true, Program.T0);
                adapter.Pump(); adapter.Engine.Data.Book.Realized = 4.2m; adapter.Quotes[0] = Program.Quotes(-2, -2)[0]; adapter.Pump();
                Futures2Checkpoint disk = store.Load(); // Deliberately before Dispose or any unrelated command/save.
                Program.Equal(2m, disk.Trail.Maximum, "quote-only trailing peak survives checkpoint reload without Dispose");
                Program.Equal(adapter.Engine.Data.Hj.Depth, disk.Hj.Depth, "quote-only HJ transition persisted");
                disk.Book.Realized = 1.05m;
                Futures2Controller restarted = new Futures2Controller(disk); restarted.Start(true, Program.T0);
                Futures2Plan plan = disk.Plans[disk.ActivePlan]; Futures2Intent held = Program.Entry(disk.Book, plan, 1);
                disk.Book.Fill(held, plan, "held-after-reload", 1, 0, 0, false, Program.T0);
                restarted.Decide(Program.Quotes(0, 0), Program.T0, true);
                Program.Check(restarted.Data.Reducing, "recovered observed peak triggers the required drawdown reduction");
            }
            finally
            {
                adapter?.Dispose();
                foreach (string file in new[] { "state.json", "state.json.bak", "state.json.tmp" }) File.Delete(Path.Combine(directory, file));
                Directory.Delete(directory);
            }
        }
        private static void RealizationPresence()
        {
            AlorServerRealization source = (AlorServerRealization)Empty(typeof(AlorServerRealization));
            Portfolio first = new Portfolio { Number = "A_FORTS", ValueCurrent = 100, PositionOnBoard = new List<PositionOnBoard>
                { new PositionOnBoard { SecurityNameCode = "X", ValueCurrent = 0 } } };
            Portfolio second = new Portfolio { Number = "B_FORTS", ValueCurrent = 100, PositionOnBoard = new List<PositionOnBoard>() };
            Set(source, "_myPortfolios", new List<Portfolio> { first, second });
            List<ExplicitAccount> observations = new List<ExplicitAccount>(); source.ExplicitAccountEvent += observations.Add;
            string row = "{\"symbol\":\"Y\",\"qty\":\"2\",\"dailyUnrealisedPl\":\"0\"}";
            Call(source, "UpDatePositionOnBoard", row, "A");
            Program.Check(observations.Count == 1 && observations[0].Positions.ContainsKey("Y") && !observations[0].Positions.ContainsKey("X")
                && !observations[0].FundsUpdated, "actual realization update A.Y cannot refresh cached A.X or funds");
            observations.Clear(); Call(source, "UpDatePositionOnBoard", row, "B");
            Program.Check(observations.Count == 1 && observations[0].Number == "B_FORTS", "actual realization B update never republishes A as an observation");
            observations.Clear();
            Call(source, "UpDateMyPortfolio", "{\"portfolioLiquidationValue\":\"100\",\"buyingPower\":\"80\",\"profit\":\"0\"}", "A");
            Program.Check(observations.Count == 1 && observations[0].FundsUpdated && observations[0].Positions.Count == 0,
                "actual realization funds callback cannot refresh any cached position row");
        }
    }
}
