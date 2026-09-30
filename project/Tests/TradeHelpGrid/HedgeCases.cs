using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OsEngine.Entity;
using OsEngine.Market.Servers;
using OsEngine.OsTrader.Grids.Futures2;
using OsEngine.OsTrader.Panels.Tab;

namespace OsEngine.TradeHelpGrid.Tests
{
    internal static class HedgeCases
    {
        internal static void Run()
        {
            foreach (Futures2Direction direction in Enum.GetValues<Futures2Direction>())
                foreach (bool hedge in new[] { false, true })
                { Decisions(direction, hedge); Native(direction, hedge); }
            RecoveryAndChange(); StaleMarginAfterModeChange(); Inventory(Futures2Direction.Long); Inventory(Futures2Direction.Short);
        }
        private static Futures2Plan Plan(Futures2Direction direction, bool hedge)
        {
            Futures2PlanInput input = Program.Input(); input.Direction = direction; input.IsHedge = hedge;
            return Futures2Plan.Build(input);
        }
        private static Futures2Controller Controller(Futures2Plan plan, Futures2Policy policy = null)
        {
            Futures2Controller engine = new Futures2Controller(new Futures2Checkpoint());
            engine.Configure(plan, policy ?? new Futures2Policy { IntervalMilliseconds = 0, MaxActions = 1 }, 210, 0);
            engine.Start(true, Program.T0); return engine;
        }
        private static void Decisions(Futures2Direction direction, bool hedge)
        {
            Futures2Plan plan = Plan(direction, hedge), normal = Plan(direction, false);
            bool logicalLong = direction == Futures2Direction.Long;
            bool actualLong = logicalLong != hedge;
            Program.Equal(actualLong, plan.IsLongInventory, "physical inventory matrix");
            Program.Check(plan.Levels.Select(l => l.Price).SequenceEqual(normal.Levels.Select(l => l.Price)), "hedge preserves grid geometry");
            Program.Equal(logicalLong ? 0.1m : -0.1m, plan.Target(plan.Levels[1], 0, Futures2ExitMode.LevelFromEnter, false), "logical target across zero");
            Program.Equal(normal.Target(normal.Levels[1], -0.12345m, Futures2ExitMode.LevelFromPlan, true),
                plan.Target(plan.Levels[1], -0.12345m, Futures2ExitMode.LevelFromPlan, true), "logical FromPlan/across target");
            Futures2Policy policy = new Futures2Policy { IntervalMilliseconds = 0, MaxActions = 1, ThresholdTicks = 1, SlippageTicks = 2 };
            Futures2Controller engine = Controller(plan, policy);
            foreach (Futures2Level level in plan.Levels) level.EntryEnabled = level.Id == 1;
            Dictionary<int, Futures2Quote> quotes = Program.Quotes(logicalLong ? -0.00003m : 0.00002m, logicalLong ? -0.00002m : 0.00003m);
            Futures2Intent entry = engine.Decide(quotes, Program.T0, true).Single().Intent;
            if (hedge) Program.Equal(actualLong ? 0.00006m : -0.00006m, entry.Price, "hedge entry uses actual quote and bounded ticks");
            engine.Data.Book.Fill(entry, plan, "entry", 10, 0, 0.01m, false, Program.T0);
            policy.ForbidEntries = true;
            Program.Equal(actualLong ? (0.2m * 10 - 0.1m) / 210 * 100 : (-0.21m * 10 - 0.1m) / 210 * 100,
                engine.ProfitPercent(Program.Quotes(0.2m, 0.21m)), "liquidation quote and physical unrealized PnL");
            quotes = Program.Quotes(logicalLong ? 0.10002m : -0.10003m, logicalLong ? 0.10003m : -0.10002m);
            Futures2Intent exit = engine.Decide(quotes, Program.T0.AddMilliseconds(1), true).Single().Intent;
            Program.Check(!exit.Entry, "logical target triggers exit for all four modes");
            if (hedge) Program.Equal(actualLong ? -0.10006m : 0.10006m, exit.Price, "hedge exit uses actual book not logical target as limit");
            engine.Data.Book.Fill(exit, plan, "exit", 10, logicalLong ? 0.1m : -0.1m, 0.01m, false, Program.T0);
            Program.Equal(hedge ? -1.2m : 0.8m, engine.Data.Book.Realized, "hedge logical target can lose money and fees remain expenses");
            Futures2Policy blocked = new Futures2Policy { IntervalMilliseconds = 0, ForbidLong = logicalLong, ForbidShort = !logicalLong };
            Program.Equal(0, Controller(Plan(direction, hedge), blocked).Decide(Program.Quotes(0, 0), Program.T0, true).Count, "forbid follows logical branch");
            Futures2Policy hj = new Futures2Policy { IntervalMilliseconds = 0, HjEntries = true, HjWidth = 0.001m };
            Program.Equal(Controller(Plan(direction, false), hj).Decide(Program.Quotes(0, 0), Program.T0, true).Count,
                Controller(Plan(direction, hedge), hj).Decide(Program.Quotes(0, 0), Program.T0, true).Count, "HJ entry selection retains logical geometry");
        }
        private static void RecoveryAndChange()
        {
            Futures2Plan old = Plan(Futures2Direction.Long, false), next = Plan(Futures2Direction.Long, true);
            Futures2Controller engine = Controller(old);
            Futures2Intent entry = Program.Entry(engine.Data.Book, old);
            entry.State = Futures2IntentState.Working;
            engine.Configure(next, new Futures2Policy(), 210, 0);
            Program.Equal(3, engine.Data.Schema, "pending hedge persists schema before publication");
            Futures2Checkpoint recovered = JsonSerializer.Deserialize<Futures2Checkpoint>(JsonSerializer.Serialize(engine.Data));
            Futures2Store.Validate(recovered);
            Program.Check(!recovered.Plans[old.Id].Input.IsHedge && recovered.PendingPlan.Input.IsHedge, "restart retains old intent orientation during hedge change");
            engine.Data.Book.Fill(entry, old, "late", 10, 0, 0, false, Program.T0);
            engine.Decide(Program.Quotes(), Program.T0, true);
            Program.Equal(old.Id, engine.Data.ActivePlan, "cancellation-time fill prevents orientation reinterpretation");
            Program.Check(engine.Data.PendingPlan == null && engine.Data.State == Futures2State.PausingEntries, "conflicting configuration is rejected and paused");
            Program.Throws(() => engine.Configure(next, new Futures2Policy(), 210, 0), "held inventory rejects hedge change");
            Program.Throws(() => new Futures2Rollover { Plan = next }.Capture(engine.Data), "rollover cannot change hedge orientation");
            Program.Throws(() => Controller(next, new Futures2Policy { PreEntries = true }), "hedge pre-entry explicit rejection");
            Program.Throws(() => Controller(next, new Futures2Policy { PreExits = true }), "hedge pre-exit explicit rejection");
            recovered.Schema = 2;
            Program.Throws(() => Futures2Store.Validate(recovered), "pending hedge rejects schema2");
            Futures2Checkpoint active = Controller(next).Data; active.Schema = 1;
            Program.Throws(() => new Futures2Controller(active), "active hedge rejects schema1 at engine construction");
            Futures2Checkpoint replacement = Controller(old).Data; replacement.Rollover = new Futures2Rollover { Plan = next };
            Program.Throws(() => Futures2Store.Validate(replacement), "replacement hedge rejects old schema");
            string legacy = JsonSerializer.Serialize(old.Input).Replace("\"IsHedge\":false,", "");
            Program.Check(!JsonSerializer.Deserialize<Futures2PlanInput>(legacy).IsHedge, "legacy input defaults to ordinary mode");
            Futures2Controller flat = Controller(old); flat.Configure(next, new Futures2Policy(), 210, 0);
            flat.Decide(Program.Quotes(), Program.T0, true);
            Program.Equal(next.Id, flat.Data.ActivePlan, "flat mode change publishes after reconciliation barrier");
        }
        private static void Native(Futures2Direction direction, bool hedge)
        {
            BotTabSimple first = AdapterCases.Tab("TEST", out FixtureServerProxy spy);
            BotTabSimple second = AdapterCases.Tab("NEXT", out FixtureServerProxy unused);
            List<string> errors = new List<string>();
            using Futures2NativeAdapter adapter = new Futures2NativeAdapter(first, second, null, false, errors.Add);
            Futures2Plan plan = Plan(direction, hedge);
            adapter.Engine.Configure(plan, new Futures2Policy { IntervalMilliseconds = 0, MaxActions = 1 }, 210, 0);
            adapter.SignedOrdersEnabled = true; adapter.ManualFreeFunds = 210;
            adapter.Quotes[0] = Program.Quotes(0, 0)[0]; AdapterCases.Set(adapter, "_now", Program.T0);
            adapter.Reconcile(false); adapter.Engine.Start(true, Program.T0); adapter.Engine.Data.ManualEntry = 1;
            Order sent = null;
            spy.Execute = order => { sent = order; order.NumberMarket = "hedge-" + order.NumberUser; order.State = OrderStateType.Active; first._journal.SetNewOrder(order, false); AdapterCases.Call(adapter, "OnOrder", 0, order); };
            Futures2Action action = adapter.Engine.Decide(adapter.Quotes, Program.T0, true).Single();
            AdapterCases.Call(adapter, "Execute", action);
            Side side = plan.IsLongInventory ? Side.Buy : Side.Sell;
            Program.Equal(side, sent.Side, "native entry dispatch actual side");
            Program.Equal(side, first.PositionsAll.Single().Direction, "native journal actual direction");
            MyTrade wrong = new MyTrade { NumberTrade = "wrong", NumberOrderParent = sent.NumberMarket,
                SecurityNameCode = "TEST", Side = side == Side.Buy ? Side.Sell : Side.Buy, Volume = 1, Price = 0, Time = Program.T0 };
            Program.Throws(() => AdapterCases.Call(adapter, "ProjectFill", action.Intent, wrong), "physical wrong-side fill rejected");
            Fill(first, adapter, sent, "entry-part", 4, 0);
            Program.Equal(4m, adapter.Engine.Data.Book.Lots.Single().Quantity, "native entry partial projection");
            Fill(first, adapter, sent, "entry-rest", 6, 0);
            spy.Security.MarginBuy = spy.Security.MarginSell = 99;
            adapter.Engine.Data.Policy.ForbidEntries = true;
            adapter.Engine.Data.ManualExit = 1;
            adapter.Quotes[0] = Program.Quotes(-0.12345m, -0.12344m)[0];
            adapter.Reconcile(false); adapter.Engine.Start(true, Program.T0);
            Futures2Action close = adapter.Engine.Decide(adapter.Quotes, Program.T0, true).Single();
            AdapterCases.Call(adapter, "Execute", close);
            Program.Equal(side == Side.Buy ? Side.Sell : Side.Buy, sent.Side, "native exit dispatch actual opposite side");
            Fill(first, adapter, sent, "close-part", 3, -0.12345m);
            Program.Equal(7m, first.PositionsAll.Single().OpenVolume, "native hedge close partial quantity");
            Fill(first, adapter, sent, "close-rest", 7, -0.12345m);
            Program.Equal(0m, first.PositionsAll.Single().OpenVolume, "native hedge closes flat");
            Program.Equal(plan.IsLongInventory ? -1.2345m : 1.2345m, adapter.Engine.Data.Book.Realized, "native signed-price physical realized PnL");
            spy.Security.MarginBuy = 3; spy.Security.MarginSell = 5;
            AdapterCases.Call(adapter, "OnQuote", 0, new ExplicitQuote("TEST", true, 0, true, 0, Program.T0));
            Program.Equal<decimal?>(plan.IsLongInventory ? 3 : 5, adapter.Quotes[0].Margin, "actual side selects collateral metadata");
            Program.Check(errors.All(e => e == "Active: Started"), "native hedge matrix has no swallowed errors: " + string.Join(";", errors));
        }
        private static void StaleMarginAfterModeChange()
        {
            BotTabSimple first = AdapterCases.Tab("TEST", out FixtureServerProxy spy);
            BotTabSimple second = AdapterCases.Tab("NEXT", out FixtureServerProxy unused);
            using Futures2NativeAdapter adapter = new Futures2NativeAdapter(first, second, null, false, _ => { });
            adapter.SignedOrdersEnabled = true; adapter.ManualFreeFunds = 210;
            spy.Security.MarginBuy = 3; spy.Security.MarginSell = 9;
            adapter.Engine.Configure(Plan(Futures2Direction.Long, false), new Futures2Policy { IntervalMilliseconds = 0, MaxActions = 1 }, 210, 0);
            adapter.Quotes[0] = Program.Quotes(0, 0)[0]; adapter.Quotes[0].Margin = 3;
            AdapterCases.Set(adapter, "_now", Program.T0); adapter.Reconcile(false); adapter.Engine.Start(true, Program.T0);
            adapter.Engine.Configure(Plan(Futures2Direction.Long, true), adapter.Engine.Data.Policy, 210, 0);
            adapter.Engine.Decide(adapter.Quotes, Program.T0, true);
            Program.Check(adapter.Reconciled && adapter.Engine.Data.Plans[adapter.Engine.Data.ActivePlan].Input.IsHedge,
                "flat mode change retains native reconciliation without a new quote");
            adapter.Engine.Data.ManualEntry = 1;
            Futures2Action action = adapter.Engine.Decide(adapter.Quotes, Program.T0.AddMilliseconds(1), true).Single();
            int sends = 0; spy.Execute = _ => sends++;
            AdapterCases.Call(adapter, "Execute", action);
            Program.Equal(0, sends, "actual Sell margin blocks stale Buy-margin quote before send");
            Program.Equal(0, first.PositionsAll.Count, "margin rejection occurs before native position creation");
            Program.Equal(Futures2IntentState.Rejected, action.Intent.State, "local margin suppression releases only its unsent intent");
            Program.Check(adapter.Engine.Data.Reason.Contains("actual entry side")
                && adapter.Engine.Data.State == Futures2State.PausingEntries, "physical margin rejection is visible and pauses increases");
            Program.Equal(0m, adapter.Engine.Data.Book.Reserved(adapter.Engine.Data.Plans), "suppressed entry does not retain fictitious collateral");
        }
        private static void Fill(BotTabSimple tab, Futures2NativeAdapter adapter, Order order, string id, decimal quantity, decimal price)
        {
            MyTrade trade = new MyTrade { NumberTrade = id, NumberOrderParent = order.NumberMarket, SecurityNameCode = "TEST",
                Side = order.Side, Volume = quantity, Price = price, Time = Program.T0 };
            Program.Check(tab._journal.SetNewMyTrade(trade), "native journal accepts physical fill");
            AdapterCases.Call(adapter, "OnFill", 0, trade);
        }
        private static void Inventory(Futures2Direction direction)
        {
            string directory = Path.Combine(Path.GetTempPath(), "Futures2-hedge-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            BotTabSimple first = AdapterCases.Tab("TEST", out FixtureServerProxy spy);
            BotTabSimple second = AdapterCases.Tab("NEXT", out FixtureServerProxy unused);
            Futures2Store store = new Futures2Store(Path.Combine(directory, "state.json"));
            Futures2NativeAdapter adapter = new Futures2NativeAdapter(first, second, store, true, _ => { });
            try
            {
                Futures2Plan plan = Plan(direction, true); plan.EndpointIdentity = adapter.EndpointIdentity(0);
                adapter.Engine.Configure(plan, new Futures2Policy(), 210, 0);
                adapter.Quotes[0] = Program.Quotes(0, 0, DateTime.Now)[0]; adapter.ExternalNet[0] = 3;
                decimal sign = plan.IsLongInventory ? 1 : -1;
                spy.AccountChanged(Observation(3 + sign * 5));
                Futures2InventoryRequest register = new Futures2InventoryRequest { Id = "hedge-register", PlanId = plan.Id,
                    Kind = Futures2InventoryKind.SetQuantity, Levels = new List<int> { 1 }, Quantity = 5, Price = 0 };
                adapter.AdjustInventory(register);
                Position native = first.PositionsAll.Single();
                Program.Equal(plan.IsLongInventory ? Side.Buy : Side.Sell, native.Direction, "registered hedge native direction");
                Program.Equal(3, store.Load().Schema, "inventory does not downgrade hedge schema3");
                Program.Check(adapter.DescribeNativeState().Contains("expected=" + (3 + sign * 5)), "account diagnostics include physical owned plus peer net");
                spy.AccountChanged(Observation(3 + sign * 3));
                Futures2InventoryRequest external = new Futures2InventoryRequest { Id = "hedge-external", PlanId = plan.Id,
                    Kind = Futures2InventoryKind.ExternalDecrease, Levels = new List<int> { 1 }, Quantity = 2, Price = 0.12345m,
                    ExecutedAt = DateTime.Now.AddSeconds(-1), ExecutionReference = "broker-external", Fee = 0.02m };
                adapter.AdjustInventory(external);
                Program.Equal(sign * 0.24690m - 0.02m, adapter.Engine.Data.Book.Realized, "external reduction physical PnL and fee");
                native.SetSignedBidAsk(true, 0, true, 0);
                Program.Equal(sign * 0.24690m - 0.02m, native.ProfitPortfolioAbs, "native external reduction matches campaign physical result");
                adapter.Dispose();
                using Futures2NativeAdapter recovered = new Futures2NativeAdapter(first, second, store, true, _ => { });
                Program.Equal(3m, recovered.Engine.Data.Book.Lots.Single().Quantity, "hedge registration restart quantity");
                Program.Equal(native.Direction, first.PositionsAll.Single().Direction, "hedge registration restart direction");
                Program.Equal(sign * 0.24690m - 0.02m, recovered.Engine.Data.Book.Realized, "hedge restart no duplicate economic operation");
            }
            finally
            {
                adapter.Dispose();
                foreach (string name in Directory.GetFiles(directory)) File.Delete(name);
                Directory.Delete(directory);
            }
        }
        private static ExplicitAccount Observation(decimal net) => new ExplicitAccount(new Portfolio { Number = "fixture", ValueCurrent = 1000 }, true,
            new[] { new PositionOnBoard { PortfolioName = "fixture", SecurityNameCode = "TEST", ValueCurrent = net } }, DateTime.Now);
    }
}
