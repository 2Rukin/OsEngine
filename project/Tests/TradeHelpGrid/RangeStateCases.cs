using System;
using System.Linq;
using System.Text.Json;
using OsEngine.OsTrader.Grids.Futures2;
using OsEngine.OsTrader.Panels.Tab;

namespace OsEngine.TradeHelpGrid.Tests
{
    /// <summary>Offline range-transform regressions for explicit level controls and native no-send rejection.</summary>
    /// <remarks>Run through TradeHelpGrid harness with in-memory managed fixtures; no native constructors,
    /// credentials, GUI or live orders. Tests cover target Shift/Widen, not literal source geometry or venue fills.</remarks>
    internal static class RangeStateCases
    {
        internal static void Run()
        {
            Preservation(); CountChange(); Publication(); AutomaticRejection();
        }
        private static Futures2Plan Customized(Futures2PlanInput input)
        {
            Futures2Plan plan = Futures2Plan.Build(input);
            plan.Levels[0].EntryEnabled = false; plan.Levels[1].ExitEnabled = false; plan.Levels[2].Markup = 0.54321m;
            plan.EndpointIdentity = "fixture-endpoint"; plan.PaperExecution = true;
            return plan;
        }
        private static void Preservation()
        {
            foreach (Futures2Direction direction in Enum.GetValues<Futures2Direction>())
                foreach (bool hedge in new[] { false, true })
                {
                    Futures2PlanInput input = Program.Input(); input.Direction = direction; input.IsHedge = hedge;
                    input.LowerStopEnabled = input.UpperStopEnabled = true; input.LowerStop = -2; input.UpperStop = 2;
                    Futures2Plan plan = Customized(input); string before = JsonSerializer.Serialize(plan);
                    foreach (Futures2Plan changed in new[] { Futures2Commands.Shift(plan, -0.00005m), Futures2Commands.Widen(plan, -1.5m, 1.5m) })
                    {
                        Program.Check(changed.Id != plan.Id && changed.EndpointIdentity == plan.EndpointIdentity && changed.PaperExecution, "range version retains native profile with new plan identity");
                        Program.Check(changed.Levels.Select(l => l.Id).SequenceEqual(plan.Levels.Select(l => l.Id)), "range preserves level ordinal mapping");
                        Program.Check(!changed.Levels[0].EntryEnabled && !changed.Levels[1].ExitEnabled && changed.Levels[2].Markup == 0.54321m, "range preserves both disabled controls and custom markup");
                        Program.Check(changed.Levels.Select(l => l.Volume).SequenceEqual(plan.Levels.Select(l => l.Volume)), "same-count unchanged budget retains level quantities");
                        Program.Check(changed.Input.LowerStop < changed.Input.Low && changed.Input.UpperStop > changed.Input.High, "transformed signed boundaries remain outside grid");
                        Program.Equal(0.54321m * (direction == Futures2Direction.Long ? 1 : -1),
                            changed.Target(changed.Levels[2], 0, Futures2ExitMode.LevelFromEnter, false), "custom target survives transformation across zero");
                    }
                    Program.Equal(before, JsonSerializer.Serialize(plan), "pure transformations never mutate original plan settings");
                    Futures2Plan shifted = Futures2Commands.Shift(plan, 0.00005m);
                    Program.Check(shifted.Levels.Select(l => l.Price).SequenceEqual(plan.Levels.Select(l => l.Price + 0.00005m)), "signed shift keeps exact tick displacement");
                }
        }
        private static void CountChange()
        {
            Futures2PlanInput input = Program.Input(); input.CountMode = Futures2CountMode.Step; input.RequestedStep = 1; input.FixedVolume = 0;
            Futures2Plan customized = Customized(input); string before = JsonSerializer.Serialize(customized);
            Program.Throws(() => Futures2Commands.Widen(customized, -1, 2), "count change with explicit controls rejects entire range transformation");
            Program.Equal(before, JsonSerializer.Serialize(customized), "count rejection leaves old plan and overrides intact");
            Futures2Plan untouched = Futures2Plan.Build(input), widened = Futures2Commands.Widen(untouched, -1, 2);
            Program.Equal(4, widened.Levels.Count, "untouched step-count grid retains existing widening calculation");
            Program.Equal(210m, widened.Reserved, "untouched count change recomputes original collateral envelope");
            Program.Check(widened.Levels.All(l => l.EntryEnabled && l.ExitEnabled && l.Markup == input.Markup), "new topology uses explicit initial controls when no overrides exist");
            foreach (int control in new[] { 0, 1, 2 })
            {
                Futures2Plan single = Futures2Plan.Build(input);
                if (control == 0) single.Levels[0].EntryEnabled = false;
                if (control == 1) single.Levels[0].ExitEnabled = false;
                if (control == 2) single.Levels[0].Markup = 0.2m;
                Program.Throws(() => Futures2Commands.Widen(single, -1, 2), "each individual override independently prevents ambiguous remapping " + control);
            }
        }
        private static void Publication()
        {
            Futures2Controller engine = Program.Controller(); Futures2Plan oldPlan = engine.Data.Plans[engine.Data.ActivePlan];
            oldPlan.Levels[1].Markup = 0.54321m; oldPlan.Levels[2].EntryEnabled = false;
            Futures2Intent pending = engine.Decide(Program.Quotes(), Program.T0, true).Single().Intent;
            pending.State = Futures2IntentState.Working; pending.OrderNumber = 1;
            Futures2Plan shifted = Futures2Commands.Shift(oldPlan, 0.00005m);
            engine.Configure(shifted, engine.Data.Policy, 210, 0);
            Program.Check(engine.Decide(Program.Quotes(), Program.T0.AddSeconds(1), true).Single().Cancel, "range change drains old native intent before publication");
            engine.Data.Book.Fill(pending, oldPlan, "late", 3, 0, 0, false, Program.T0);
            engine.Data.Book.Observe(pending, Futures2IntentState.Canceled, 3);
            engine.Decide(Program.Quotes(), Program.T0.AddSeconds(2), true);
            Program.Equal(shifted.Id, engine.Data.ActivePlan, "confirmed range change publishes new plan");
            Program.Equal(oldPlan.Id, engine.Data.Book.Lots.Single().PlanId, "cancellation-time fill remains attached to old plan");
            Program.Check(!shifted.Levels[2].EntryEnabled && shifted.Levels[1].Markup == 0.54321m, "published plan keeps level-specific controls");
            Futures2Checkpoint recovered = Futures2Commands.Copy(engine.Data); Futures2Store.Validate(recovered);
            Program.Check(recovered.Plans[shifted.Id].Levels[1].Markup == 0.54321m && !recovered.Plans[shifted.Id].Levels[2].EntryEnabled,
                "round trip retains transformed plan controls without a new schema");
            Futures2PlanInput step = Program.Input(); step.CountMode = Futures2CountMode.Step; step.RequestedStep = 1; step.FixedVolume = 0;
            Futures2Plan custom = Customized(step); string state = JsonSerializer.Serialize(engine.Data);
            Program.Throws(() => engine.Configure(Futures2Commands.Widen(custom, -1, 2), engine.Data.Policy, 210, 0), "rejected command never reaches Configure");
            Program.Equal(state, JsonSerializer.Serialize(engine.Data), "rejected command stages no cancel/new intent or plan");
        }
        private static void AutomaticRejection()
        {
            BotTabSimple first = AdapterCases.Tab("TEST", out FixtureServerProxy spy), second = AdapterCases.Tab("NEXT", out FixtureServerProxy unused);
            using Futures2NativeAdapter adapter = new Futures2NativeAdapter(first, second, null, false, _ => { });
            adapter.SignedOrdersEnabled = true; adapter.ManualFreeFunds = 210; AdapterCases.Set(adapter, "_now", Program.T0);
            Futures2PlanInput input = Program.Input(); input.CountMode = Futures2CountMode.Step; input.RequestedStep = 1; input.FixedVolume = 0;
            Futures2Plan plan = Customized(input);
            adapter.Engine.Configure(plan, new Futures2Policy { WidenWhenFlat = true }, 210, 0);
            adapter.Quotes[0] = Program.Quotes(2, 2)[0]; adapter.Reconcile(false); adapter.Engine.Start(true, Program.T0);
            int sends = 0; spy.Execute = _ => sends++;
            adapter.Pump();
            Program.Equal(Futures2State.Faulted, adapter.Engine.Data.State, "automatic ambiguous count change faults adapter visibly");
            Program.Check(adapter.Engine.Data.Reason.Contains("custom controls") && adapter.Engine.Data.ActivePlan == plan.Id,
                "automatic refusal explains remediation and keeps prior plan");
            Program.Equal(0, sends + spy.Cancels, "automatic range refusal sends no orders or cancellations");
        }
    }
}
