using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OsEngine.Entity;
using OsEngine.OsTrader.Grids.Futures2;
using OsEngine.OsTrader.Panels.Tab;

namespace OsEngine.TradeHelpGrid.Tests
{
    internal static class BatchCases
    {
        internal static void Run() { Selection(); Entries(); Exits(); EditsAndCancels(); Persistence(); NativeBeforeSend(); }
        private static Futures2Plan Active(Futures2Controller c) => c.Data.Plans[c.Data.ActivePlan];
        private static Dictionary<int, Futures2Quote> Away() => Program.Quotes(2, 2);
        private static Futures2Intent Seed(Futures2Controller c, int level, decimal qty = 2)
        {
            Futures2Intent entry = Program.Entry(c.Data.Book, Active(c), qty);
            entry.Allocations[0].LevelId = level;
            c.Data.Book.Fill(entry, Active(c), "seed-" + level, qty, 10, 0, false, Program.T0); return entry;
        }
        private static void Selection()
        {
            Futures2Plan p = Program.Plan();
            Program.Check(Futures2Commands.SelectLevels(p, "0, 2", 99).SequenceEqual(new[] { 0, 2 }), "explicit CSV overrides scalar fallback");
            Program.Equal(1, Futures2Commands.SelectLevels(p, "1", -1).Single(), "single explicit selection is never all");
            Program.Check(Futures2Commands.SelectLevels(p, " ", -1).SequenceEqual(new[] { 0, 1, 2 }), "explicit scalar -1 selects all");
            foreach (string bad in new[] { "0,0", "0,3", "0,", ",1", "-1,1", "x", "2147483648" })
                Program.Throws(() => Futures2Commands.SelectLevels(p, bad, 0), "invalid full CSV rejects " + bad);
            Program.Throws(() => Futures2Commands.SelectLevels(p, "", 99), "invalid scalar rejected without CSV");
            Futures2Controller c = Program.Controller(); int[] ids = { 0, 2 };
            Futures2Commands.EnterLevels(c.Data, Active(c).Id, ids); ids[0] = 1;
            Program.Equal(0, c.Data.EntryBatch.Levels[0], "entry command captures detached selection");
            Program.Throws(() => Futures2Commands.EnterLevels(c.Data, Active(c).Id, new[] { 1 }), "second entry command cannot silently replace pending batch");
            c = Program.Controller();
            Program.Throws(() => Futures2Commands.EnterLevels(c.Data, Active(c).Id, new[] { 0, 9 }), "invalid batch atomic rejection");
            Program.Check(c.Data.EntryBatch == null && c.Data.Schema == 1 && c.Data.Book.Intents.Count == 0, "invalid command has no partial state/schema/order effects");
        }
        private static void Entries()
        {
            Futures2Controller c = Program.Controller(new Futures2Policy { IntervalMilliseconds = 0, MaxActions = 1, ForbidExits = true });
            Futures2Commands.EnterLevels(c.Data, Active(c).Id, new[] { 2, 0 });
            Futures2Intent first = c.Decide(Away(), Program.T0, true).Single().Intent;
            Program.Equal(0, first.Allocations.Single().LevelId, "entry batch follows accepted level order");
            Program.Check(c.Data.EntryBatch.Levels.SequenceEqual(new[] { 2 }), "only unsent selection remains after action limit");
            c.Data.Book.Fill(first, Active(c), "partial", 3, 0, 0, false, Program.T0);
            c.Data.Book.Observe(first, Futures2IntentState.Canceled, 3);
            Futures2Checkpoint checkpoint = Futures2Commands.Copy(c.Data); Futures2Store.Validate(checkpoint);
            c = new Futures2Controller(checkpoint);
            Futures2Intent second = c.Decide(Away(), Program.T0, true).Single().Intent;
            Program.Equal(2, second.Allocations.Single().LevelId, "restart advances remaining selection after partial/cancel");
            Program.Equal(1, c.Data.Book.Intents.Count(i => i.Entry && i.Allocations.Any(a => a.LevelId == 0)), "batch does not retry completed level");
            Program.Check(c.Data.EntryBatch == null, "batch finishes on intent recording, not fill completion");
            c = Program.Controller(new Futures2Policy { IntervalMilliseconds = 0, MaxActions = 1, GroupOrders = true });
            Futures2Commands.EnterLevels(c.Data, Active(c).Id, new[] { 0, 2 });
            Futures2Intent grouped = c.Decide(Away(), Program.T0, true).Single().Intent;
            Program.Equal(20m, grouped.Quantity, "selected compatible entries share a native intent");
            Program.Check(grouped.Allocations.Select(a => a.LevelId).SequenceEqual(new[] { 0, 2 }) && c.Data.EntryBatch == null, "grouped selection excludes unselected level");
            Futures2Commands.EnterLevels(c.Data, Active(c).Id, new[] { 1 }); Futures2Commands.CancelEntryBatch(c.Data);
            Program.Check(c.Data.EntryBatch == null && grouped.CanFill, "cancel pending entries preserves already recorded native group");
            c.Data.Rollover = new Futures2Rollover { Plan = Program.Plan() };
            Program.Throws(() => Futures2Commands.EnterLevels(c.Data, Active(c).Id, new[] { 1 }), "entry batch cannot be queued during prepared replacement");
            c = Program.Controller(new Futures2Policy { IntervalMilliseconds = 0, MaxActions = 1, ForbidLong = true });
            Futures2Commands.EnterLevels(c.Data, Active(c).Id, new[] { 0, 2 });
            Program.Equal(0, c.Decide(Away(), Program.T0, true).Count, "batch cannot bypass logical direction forbid");
            Program.Equal(2, c.Data.EntryBatch.Levels.Count, "blocked batch preserves progress");
            c.Data.Policy.ForbidLong = false;
            Program.Equal(0, c.Decide(Program.Quotes(2, 2, funds: 0), Program.T0, true).Count, "batch cannot bypass funding gate");
            Seed(c, 0, 10);
            Futures2Intent remaining = c.Decide(Away(), Program.T0, true).Single().Intent;
            Program.Equal(2, remaining.Allocations.Single().LevelId, "already full selected level does not block remaining levels");
            c = Program.Controller(); Futures2Commands.EnterLevels(c.Data, Active(c).Id, new[] { 0, 2 });
            Futures2Plan replacement = Futures2Commands.Shift(Active(c), 0.00001m);
            c.Configure(replacement, c.Data.Policy, 210, 0); c.Decide(Away(), Program.T0, true);
            Program.Check(c.Data.EntryBatch == null && c.Data.Reason.Contains("selected entries canceled"), "changed plan cancels pending batch visibly");
            Futures2Commands.EnterLevels(c.Data, Active(c).Id, new[] { 1 }); c.Flatten("operator");
            Program.Check(c.Data.EntryBatch == null, "emergency clears future manual entry request");
        }
        private static void Exits()
        {
            Futures2Controller c = Program.Controller(new Futures2Policy { IntervalMilliseconds = 0, MaxActions = 1, ForbidEntries = true });
            Seed(c, 0); Seed(c, 1); Seed(c, 2);
            Futures2Commands.ExitLevels(c.Data, Active(c).Id, new[] { 0, 2 });
            Program.Equal(2, c.Data.ManualExitLots.Count, "exit command immediately snapshots selected current lots");
            Program.Throws(() => Futures2Commands.ExitLevels(c.Data, Active(c).Id, new[] { 1 }), "pending exit cannot be silently overwritten");
            Futures2Intent close = c.Decide(Away(), Program.T0, true).Single().Intent;
            c.Data.Book.Fill(close, Active(c), "close-part", 1, 2, 0, false, Program.T0);
            c.Data.Book.Observe(close, Futures2IntentState.Canceled, 1);
            c = new Futures2Controller(Futures2Commands.Copy(c.Data));
            close = c.Decide(Away(), Program.T0, true).Single().Intent;
            Program.Equal(1m, close.Quantity, "selected exit restarts with canceled remainder only");
            c.Data.Book.Fill(close, Active(c), "close-rest", 1, 2, 0, false, Program.T0);
            close = c.Decide(Away(), Program.T0, true).Single().Intent;
            Program.Equal(2, close.Allocations.Single().LevelId, "second selected exit survives one-action cap");
            c.Data.Book.Fill(close, Active(c), "close-last", 2, 2, 0, false, Program.T0);
            c.Decide(Away(), Program.T0, true);
            Program.Equal(2m, c.Data.Book.Lots.Single(l => l.LevelId == 1).Quantity, "unselected owned level remains intact");
            Program.Equal(0, c.Data.ManualExitLots.Count, "exit snapshot settles after confirmed fills");
        }
        private static void EditsAndCancels()
        {
            Futures2Controller c = Program.Controller(new Futures2Policy { IntervalMilliseconds = 0, ForbidEntries = true });
            Futures2Plan p = Active(c);
            Futures2Intent working = Program.Entry(c.Data.Book, p); working.State = Futures2IntentState.Working;
            Futures2Commands.EditLevels(c.Data, p.Id, new[] { 0, 2 }, 0.2m, null, false);
            Program.Equal(1, c.Decide(Away(), Program.T0, true).Count(a => a.Cancel), "batch edit waits for native cancellation");
            Program.Equal(0.1m, p.Levels[0].Markup, "edit does not publish before cancel outcome");
            c.Data.Book.Observe(working, Futures2IntentState.Canceled, 0);
            c.Decide(Away(), Program.T0, true);
            Program.Check(p.Levels[0].Markup == 0.2m && p.Levels[2].Markup == 0.2m && p.Levels[1].Markup == 0.1m, "markup updates only selected levels");
            Program.Check(p.Levels[0].EntryEnabled && !p.Levels[0].ExitEnabled && p.Levels[1].ExitEnabled, "nullable edit preserves unselected fields");
            Futures2Commands.EditLevels(c.Data, p.Id, new[] { 1 }, null, false, null); c.Decide(Away(), Program.T0, true);
            Program.Check(!p.Levels[1].EntryEnabled && p.Levels[0].EntryEnabled, "single selected flag is not applied to every level");
            c = Program.Controller(new Futures2Policy { IntervalMilliseconds = 0, MaxActions = 1, GroupOrders = true });
            Futures2Commands.EnterLevels(c.Data, Active(c).Id, new[] { 0, 2 });
            Futures2Intent grouped = c.Decide(Away(), Program.T0, true).Single().Intent;
            grouped.OrderNumber = 10; grouped.State = Futures2IntentState.Working;
            Futures2Commands.CancelLevels(c.Data, Active(c).Id, new[] { 2 });
            Program.Check(c.Data.SelectedCancels.Contains(grouped.Id), "selected cancel captures whole grouped intent");
            Futures2Action cancel = c.Decide(Away(), Program.T0, true).Single();
            Program.Check(cancel.Cancel && cancel.Intent.Allocations.Count == 2, "cancel preserves all grouped allocations and reservations");
            c.Data.Book.Fill(grouped, Active(c), "late-partial", 3, 0, 0, false, Program.T0);
            c.Data.Book.Observe(grouped, Futures2IntentState.Canceled, 3); c.Data.Policy.ForbidEntries = true;
            c.Decide(Away(), Program.T0, true);
            Program.Equal(3m, c.Data.Book.Lots.Sum(l => l.Quantity), "selected cancel accounts late partial fill");
            Program.Equal(0, c.Data.SelectedCancels.Count, "selected cancellation request clears only after terminal outcome");
        }
        private static void Persistence()
        {
            Futures2Controller c = Program.Controller(); Futures2Commands.EnterLevels(c.Data, Active(c).Id, new[] { 0, 2 });
            Program.Equal(4, c.Data.Schema, "batch upgrades schema before persistence");
            Futures2Checkpoint old = Futures2Commands.Copy(c.Data); old.Schema = 3;
            Program.Throws(() => Futures2Store.Validate(old), "older schema cannot silently ignore entry selection");
            old = Futures2Commands.Copy(c.Data); old.EntryBatch.Levels.Add(0);
            Program.Throws(() => Futures2Store.Validate(old), "recovered duplicate selection rejected");
            old = Futures2Commands.Copy(c.Data); old.ManualEntry = 1;
            Program.Throws(() => Futures2Store.Validate(old), "recovered conflicting legacy and batch command rejected");
            old = Futures2Commands.Copy(c.Data); old.EntryBatch.Plan = "foreign";
            Program.Throws(() => Futures2Store.Validate(old), "recovered wrong plan selection rejected");
            c.Data.EntryBatch = null;
            Futures2PlanInput input = Program.Input(); input.IsHedge = true;
            c.Configure(Futures2Plan.Build(input), new Futures2Policy(), 210, 0);
            Program.Equal(4, c.Data.Schema, "hedge does not downgrade schema4");
            c.Data.PendingPlan = null; c.Data.PendingPolicy = null;
            Futures2Commands.EditLevels(c.Data, Active(c).Id, new[] { 0, 2 }, 0.2m, null, null);
            old = Futures2Commands.Copy(c.Data); old.Schema = 3;
            Program.Throws(() => Futures2Store.Validate(old), "older schema cannot silently turn selected edit into all-level edit");
        }
        private static void NativeBeforeSend()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Futures2-batch-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            BotTabSimple tab = AdapterCases.Tab("TEST", out FixtureServerProxy spy);
            BotTabSimple next = AdapterCases.Tab("NEXT", out FixtureServerProxy unused);
            Futures2Store store = new Futures2Store(Path.Combine(directory, "state.json"));
            using Futures2NativeAdapter adapter = new Futures2NativeAdapter(tab, next, store, false, _ => { });
            try
            {
                adapter.Engine.Configure(Program.Plan(), new Futures2Policy { IntervalMilliseconds = 0, MaxActions = 1 }, 210, 0);
                adapter.SignedOrdersEnabled = true; adapter.ManualFreeFunds = 210; adapter.Quotes[0] = Away()[0];
                AdapterCases.Set(adapter, "_now", Program.T0); adapter.Reconcile(false); adapter.Engine.Start(true, Program.T0);
                Futures2Commands.EnterLevels(adapter.Engine.Data, Active(adapter.Engine).Id, new[] { 0, 2 });
                Futures2Action action = adapter.Engine.Decide(adapter.Quotes, Program.T0, true).Single();
                bool durable = false;
                spy.Execute = order =>
                {
                    Futures2Checkpoint disk = store.Load();
                    durable = disk.EntryBatch.Levels.SequenceEqual(new[] { 2 }) && disk.Book.Intents.Single().OrderNumber == order.NumberUser;
                };
                AdapterCases.Call(adapter, "Execute", action);
                Program.Check(durable, "remaining batch and native identity are durable together before send");
                Futures2Controller recovered = new Futures2Controller(store.Load());
                Program.Equal(0, recovered.Decide(Away(), Program.T0, true).Count, "restart with submit-pending does not resend or advance batch");
                Program.Check(recovered.Data.EntryBatch.Levels.SequenceEqual(new[] { 2 }), "unresolved send retains remaining selected levels");
            }
            finally
            {
                adapter.Dispose(); foreach (string file in Directory.GetFiles(directory)) File.Delete(file); Directory.Delete(directory);
            }
        }
    }
}
