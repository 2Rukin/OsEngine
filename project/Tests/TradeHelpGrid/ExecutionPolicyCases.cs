using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using OsEngine.Entity;
using OsEngine.OsTrader.Grids.Futures2;
using OsEngine.OsTrader.Panels.Tab;

namespace OsEngine.TradeHelpGrid.Tests
{
    /// <summary>Offline traversal, quote-limit and publication regressions using the deterministic controller and native adapter spies.</summary>
    /// <remarks>Run through the TradeHelpGrid harness. No credentials, native constructors, GUI, real orders
    /// or venue fills; in-memory fixtures only. Tests do not prove live liquidity, execution prices or profitability.</remarks>
    internal static class ExecutionPolicyCases
    {
        internal static void Run()
        {
            Priority(); PricingMatrix(); OtherPaths(); Publication(); NativePrice();
        }
        private static Futures2Controller Create(Futures2Plan plan = null, Futures2Policy policy = null)
        {
            Futures2Controller engine = new Futures2Controller(new Futures2Checkpoint());
            plan ??= Program.Plan(); policy ??= new Futures2Policy { IntervalMilliseconds = 0 };
            engine.Configure(plan, policy, plan.Input.Budget, 0); engine.Start(true, Program.T0); return engine;
        }
        private static Futures2Plan Plan(Futures2Controller engine) => engine.Data.Plans[engine.Data.ActivePlan];
        private static void Seed(Futures2Controller engine, int level, decimal quantity = 10)
        {
            Futures2Intent intent = Program.Entry(engine.Data.Book, Plan(engine), quantity);
            intent.PositionNumber = level + 1; intent.Allocations[0].LevelId = level;
            engine.Data.Book.Fill(intent, Plan(engine), "fill-" + level, quantity, 0, 0, false, Program.T0);
        }
        private static void Priority()
        {
            Futures2PlanInput input = Program.Input(); input.FixedVolume = 0; input.Budget = 100;
            foreach (bool ascending in new[] { false, true })
            {
                Futures2Plan plan = Futures2Plan.Build(input); string before = JsonSerializer.Serialize(plan);
                Futures2Controller engine = Create(plan, new Futures2Policy { AscendingLevelPriority = ascending, IntervalMilliseconds = 0, MaxActions = 3, DayLimit = 35 });
                Futures2Intent first = engine.Decide(Program.Quotes(-2, -2), Program.T0, true).Single().Intent;
                Program.Equal(ascending ? 2 : 0, first.Allocations.Single().LevelId, "priority and tight day budget choose the intended first level");
                Program.Equal(ascending ? 5m : 4m, first.Quantity, "priority keeps original per-level carry quantity");
                Program.Equal(before, JsonSerializer.Serialize(plan), "traversal never changes IDs/volumes/prices or plan identity");
            }
            Futures2Controller batch = Create(policy: new Futures2Policy { AscendingLevelPriority = true, IntervalMilliseconds = 0, ForbidExits = true });
            Futures2Commands.EnterLevels(batch.Data, batch.Data.ActivePlan, new[] { 0, 2 });
            Futures2Intent selected = batch.Decide(Program.Quotes(2, 2), Program.T0, true).Single().Intent;
            Program.Equal(2, selected.Allocations.Single().LevelId, "selected batch uses configured price priority instead of CSV order");
            batch.Data.Book.Fill(selected, Plan(batch), "partial", 3, 2, 0, false, Program.T0);
            batch.Data.Book.Observe(selected, Futures2IntentState.Canceled, 3);
            batch = new Futures2Controller(Futures2Commands.Copy(batch.Data)); Futures2Store.Validate(batch.Data);
            Futures2Intent next = batch.Decide(Program.Quotes(2, 2), Program.T0, true).Single().Intent;
            Program.Equal(0, next.Allocations.Single().LevelId, "serialized partial/cancel resumes only remaining selected level");
            Program.Equal(2, batch.Data.Book.Intents.Count, "restart creates no duplicate processed batch attempt");
            foreach (bool ascending in new[] { false, true })
            {
                Futures2Controller exits = Create(policy: new Futures2Policy { AscendingLevelPriority = ascending, IntervalMilliseconds = 0 });
                Seed(exits, 0); Seed(exits, 2);
                Futures2Intent close = exits.Decide(Program.Quotes(0.5m, 0.5m), Program.T0, true).Single().Intent;
                Program.Equal(ascending ? 2 : 0, close.Allocations.Single().LevelId, "ordinary exits use selected level price priority");
                Program.Check(!close.Entry, "exit priority precedes ordinary increases");
            }
            Futures2Controller protectedExit = Create(policy: new Futures2Policy { AscendingLevelPriority = true, IntervalMilliseconds = 0 });
            Seed(protectedExit, 0); Seed(protectedExit, 2); protectedExit.Flatten("fixture emergency");
            Futures2Intent protective = protectedExit.Decide(Program.Quotes(), Program.T0, true).Single().Intent;
            Program.Equal(0, protective.Allocations.Single().LevelId, "protective ordering retains book order");
            Futures2Controller grouped = Create(policy: new Futures2Policy { AscendingLevelPriority = true, QuoteOrdinaryLimits = true,
                GroupOrders = true, MaxActions = 1, IntervalMilliseconds = 0, DayLimit = 140 });
            Futures2Intent group = grouped.Decide(Program.Quotes(-2, -2), Program.T0, true).Single().Intent;
            Program.Check(group.Allocations.Select(a => a.LevelId).SequenceEqual(new[] { 2, 1 }), "shared quote groups ascending allocations under MaxActions one");
            Program.Equal(20m, group.Quantity, "grouping cannot exceed remaining daily collateral");
            Program.Equal(140m, grouped.Data.Book.Reserved(grouped.Data.Plans), "grouped quote policy reserves all allocations once");
        }
        private static void PricingMatrix()
        {
            foreach (decimal center in new[] { -2m, 0m, 2m })
                foreach (Futures2Direction direction in Enum.GetValues<Futures2Direction>())
                    foreach (bool hedge in new[] { false, true })
                    {
                        Futures2PlanInput input = Program.Input(); input.Low += center; input.High += center; input.Direction = direction; input.IsHedge = hedge;
                        Futures2Policy policy = new Futures2Policy { QuoteOrdinaryLimits = true, ThresholdTicks = 3, SlippageTicks = 19, IntervalMilliseconds = 0 };
                        Futures2Controller engine = Create(Futures2Plan.Build(input), policy); bool logicalLong = direction == Futures2Direction.Long;
                        bool physicalLong = logicalLong != hedge;
                        Dictionary<int, Futures2Quote> quotes = Program.Quotes(center - 0.20001m, center - 0.2m);
                        Futures2Intent entry = engine.Decide(quotes, Program.T0, true).Single().Intent;
                        Program.Equal(center + (physicalLong ? -0.19997m : -0.20004m), entry.Price, "signed physical entry quote plus threshold only");
                        Program.Check(entry.Entry && !entry.Market, "ordinary quote option keeps limit role");
                        engine.Data.Book.Fill(entry, Plan(engine), "entry", 10, center, 0, false, Program.T0);
                        quotes = logicalLong ? Program.Quotes(center + 0.4m, center + 0.40001m) : Program.Quotes(center - 0.40001m, center - 0.4m);
                        Futures2Intent exit = engine.Decide(quotes, Program.T0.AddSeconds(1), true).Single().Intent;
                        decimal expected = logicalLong ? center + (physicalLong ? 0.39997m : 0.40004m) : center + (physicalLong ? -0.40004m : -0.39997m);
                        Program.Equal(expected, exit.Price, "ordinary physical exit follows current quote instead of logical TP");
                        Program.Check(!exit.Entry && !exit.Protective, "quote limit remains ordinary owned exit");
                    }
            Futures2Controller zero = Create(policy: new Futures2Policy { QuoteOrdinaryLimits = true, ThresholdTicks = 0, IntervalMilliseconds = 0, MarketOrders = true });
            Futures2Intent market = zero.Decide(Program.Quotes(0, 0), Program.T0, true).Single().Intent;
            Program.Check(market.Price == 0 && market.Market, "literal zero reference survives market selection");
            zero = Create(policy: new Futures2Policy { QuoteOrdinaryLimits = true, ThresholdTicks = 2, IntervalMilliseconds = 0 });
            Futures2Intent zeroLimit = zero.Decide(Program.Quotes(-0.00003m, -0.00002m), Program.T0, true).Single().Intent;
            Program.Check(zeroLimit.Price == 0 && !zeroLimit.Market, "negative quote plus threshold yields valid zero limit");
            Program.Equal(0m, Futures2Commands.Copy(zero.Data).Book.Intents.Single().Price, "zero limit and its presence survive checkpoint round trip");
        }
        private static void OtherPaths()
        {
            Futures2Policy policy = new Futures2Policy { QuoteOrdinaryLimits = true, ThresholdTicks = 3, SlippageTicks = 19, IntervalMilliseconds = 0,
                PreEntries = true, PreDistance = 1 };
            Futures2Controller pre = Create(policy: policy);
            Futures2Intent resting = pre.Decide(Program.Quotes(1.2m, 1.2m), Program.T0, true).Single().Intent;
            Program.Equal(1m, resting.Price, "unreached entry preorder retains level price");
            Program.Check(!resting.Market, "preorder remains limit");
            pre = Create(policy: new Futures2Policy { QuoteOrdinaryLimits = true, PreExits = true, PreDistance = 1, IntervalMilliseconds = 0 });
            Seed(pre, 0);
            Program.Equal(0.1m, pre.Decide(Program.Quotes(0, 0), Program.T0, true).Single().Intent.Price, "unreached exit preorder retains TP");
            Futures2Controller manual = Create(policy: Futures2Commands.Copy(policy)); manual.Data.Policy.PreEntries = false;
            Futures2Commands.EnterLevels(manual.Data, manual.Data.ActivePlan, new[] { 0 });
            Program.Equal(2.00019m, manual.Decide(Program.Quotes(2, 2), Program.T0, true).Single().Intent.Price, "manual selected entry keeps SlippageTicks pricing");
            manual = Create(policy: Futures2Commands.Copy(policy)); Seed(manual, 0);
            Futures2Commands.ExitLevels(manual.Data, manual.Data.ActivePlan, new[] { 0 });
            Program.Equal(-0.00019m, manual.Decide(Program.Quotes(0, 0), Program.T0, true).Single().Intent.Price, "manual selected exit keeps SlippageTicks pricing");
            Futures2Controller funded = Create(policy: Futures2Commands.Copy(policy)); funded.Data.Receipts["credit"] = 70;
            Program.Equal(2.00019m, funded.Decide(Program.Quotes(2, 2), Program.T0, true).Single().Intent.Price, "funded entry retains existing aggressive path");
            Futures2Controller replacement = Create(policy: Futures2Commands.Copy(policy));
            replacement.Data.Rollover = new Futures2Rollover { Plan = Plan(replacement), State = "Entering", Required = new Dictionary<int, decimal> { [2] = 4 } };
            Futures2Intent replacementEntry = replacement.Decide(Program.Quotes(2, 2), Program.T0, true).Single().Intent;
            Program.Check(replacementEntry.Price == 2.00019m && replacementEntry.Quantity == 4, "replacement entry retains aggressive price and captured quantity");
            Futures2Controller protective = Create(policy: Futures2Commands.Copy(policy)); Seed(protective, 0); protective.Flatten("fixture");
            Program.Equal(-0.00019m, protective.Decide(Program.Quotes(0, 0), Program.T0, true).Single().Intent.Price, "emergency retains SlippageTicks pricing");
            Futures2Controller blocked = Create(policy: new Futures2Policy { QuoteOrdinaryLimits = true, AscendingLevelPriority = true, ForbidLong = true, IntervalMilliseconds = 0 });
            Program.Equal(0, blocked.Decide(Program.Quotes(), Program.T0, true).Count, "new policies never bypass logical forbid");
            blocked.Data.Policy.ForbidLong = false;
            Program.Equal(0, blocked.Decide(Program.Quotes(funds: 0), Program.T0, true).Count, "new policies never bypass funds");
        }
        private static void Publication()
        {
            Futures2Controller engine = Create();
            Futures2Intent old = engine.Decide(Program.Quotes(), Program.T0, true).Single().Intent;
            old.OrderNumber = 71; old.State = Futures2IntentState.Working; decimal oldPrice = old.Price;
            Futures2Policy replacement = new Futures2Policy { AscendingLevelPriority = true, QuoteOrdinaryLimits = true, IntervalMilliseconds = 0, ForbidExits = true };
            engine.Configure(Plan(engine), replacement, 210, 0);
            Program.Equal(5, engine.Data.Schema, "pending policy promotes schema before side effects");
            Futures2Action cancel = engine.Decide(Program.Quotes(), Program.T0, true).Single();
            Program.Check(cancel.Cancel && !engine.Data.Policy.QuoteOrdinaryLimits, "policy not visible before old working order cancellation");
            old.State = Futures2IntentState.Unknown;
            Program.Equal(0, engine.Decide(Program.Quotes(), Program.T0.AddSeconds(1), true).Count, "unknown does not permit policy publication or resend");
            Program.Check(engine.Data.PendingPolicy.QuoteOrdinaryLimits && !engine.Data.Policy.QuoteOrdinaryLimits, "unknown preserves pending and old accepted policy");
            engine.Data.Book.Fill(old, Plan(engine), "late", 3, oldPrice, 0, false, Program.T0);
            engine.Data.Book.Observe(old, Futures2IntentState.Canceled, 3);
            engine.Decide(Program.Quotes(), Program.T0.AddSeconds(2), true);
            Program.Check(engine.Data.Policy.QuoteOrdinaryLimits && engine.Data.PendingPolicy == null, "confirmed cancellation publishes changed policy after late fill");
            Program.Equal(oldPrice, old.Price, "accepted policy does not rewrite prior durable order price");
            Futures2Checkpoint restored = Futures2Commands.Copy(engine.Data); Futures2Store.Validate(restored);
            Program.Check(restored.Policy.AscendingLevelPriority && restored.Policy.QuoteOrdinaryLimits, "schema5 reload retains both policies and late inventory");
            restored.Schema = 4;
            Program.Throws(() => Futures2Store.Validate(restored), "active execution option in old schema fails closed");
            Futures2Checkpoint pending = new Futures2Checkpoint { Schema = 4, PendingPolicy = replacement };
            Program.Throws(() => Futures2Store.ValidateSchema(pending), "pending execution option in old schema fails closed");
            engine.Configure(Plan(engine), new Futures2Policy(), 210, 0); engine.Decide(Program.Quotes(), Program.T0.AddSeconds(3), true);
            Program.Equal(5, engine.Data.Schema, "disabling policies never downgrades schema");
            Futures2Commands.EnterLevels(engine.Data, engine.Data.ActivePlan, new[] { 0 });
            Program.Equal(5, engine.Data.Schema, "selected-level command preserves newer schema");
            Futures2Policy legacy = JsonSerializer.Deserialize<Futures2Policy>("{}");
            Program.Check(!legacy.AscendingLevelPriority && !legacy.QuoteOrdinaryLimits, "missing legacy flags retain prior defaults");
        }
        private static void NativePrice()
        {
            BotTabSimple first = AdapterCases.Tab("TEST", out FixtureServerProxy spy); BotTabSimple second = AdapterCases.Tab("NEXT", out FixtureServerProxy unused);
            using Futures2NativeAdapter adapter = new Futures2NativeAdapter(first, second, null, false, _ => { });
            adapter.SignedOrdersEnabled = true; adapter.ManualFreeFunds = 210; AdapterCases.Set(adapter, "_now", Program.T0);
            adapter.Engine.Configure(Program.Plan(), new Futures2Policy { QuoteOrdinaryLimits = true, ThresholdTicks = 3, IntervalMilliseconds = 0 }, 210, 0);
            adapter.Quotes[0] = Program.Quotes()[0]; adapter.Reconcile(false); adapter.Engine.Start(true, Program.T0);
            Futures2Action action = adapter.Engine.Decide(adapter.Quotes, Program.T0, true).Single();
            int sends = 0; decimal sent = 0;
            spy.Execute = order => { sends++; sent = order.Price; order.NumberMarket = "native"; order.State = OrderStateType.Active; AdapterCases.Call(adapter, "OnOrder", 0, order); };
            AdapterCases.Call(adapter, "Execute", action);
            Program.Equal(1, sends, "quote policy produces one native dispatch");
            Program.Equal(-0.19997m, sent, "native gateway retains signed quote-derived limit exactly");
            Program.Equal(sent, first.PositionsAll.Single().OpenOrders.Single().Price, "native journal and durable intent agree on new limit");
        }
    }
}
