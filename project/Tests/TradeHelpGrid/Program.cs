using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using OsEngine.Entity;
using OsEngine.OsTrader.Grids.Futures2;

namespace OsEngine.TradeHelpGrid.Tests
{
    /// <summary>
    /// Runs offline synthetic component tests by default, without robot/server construction or application launch.
    /// Explicit <c>--native-*</c> modes create an isolated WPF application, Tester server and, for replay,
    /// a Futures2Grid robot; each native mode terminates its dedicated process after writing evidence.
    /// No mode uses network access, credentials or a broker connection.
    /// </summary>
    internal static class Program
    {
        internal static readonly DateTime T0 = new DateTime(2026, 9, 29, 12, 0, 0);
        private static int _passed;
        private static int _failed;
        [STAThread]
        private static int Main(string[] args)
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            if (args.Length > 0 && args[0] == "--native-zero-loader")
            {
                if (args.Length != 2) throw new ArgumentException("Usage: --native-zero-loader <new-output-directory>");
                return Sru6HistoricalRun.RunZeroLoader(args[1]);
            }
            if (args.Length > 0 && args[0] == "--native-sru6")
            {
                if (args.Length != 5) throw new ArgumentException("Usage: --native-sru6 <txt|qsh> <source> <yyyy-MM-dd> <new-output-directory>");
                return Sru6HistoricalRun.Run(args[1], args[2], DateTime.ParseExact(args[3], "yyyy-MM-dd", CultureInfo.InvariantCulture), args[4]);
            }
            Run("math", MathCases); Run("book", BookCases); Run("controller", ControllerCases);
            Run("manual/replacement/funding", ReplacementCases);
            Run("helpers", HelperCases); Run("persistence/coordination", StoreCases); Run("native", NativeCases.Run); Run("adapter", AdapterCases.Run); Run("TRANSAQ", TransaqCases.Run); Run("shared account diagnostics", ExternalOwnershipCases.Run);
            Run("manual inventory and recovery", InventoryCases.Run);
            Run("hedge direction and recovery", HedgeCases.Run);
            Run("selected level batches", BatchCases.Run);
            Run("empty robot owner removal", EmptyRemovalCases.Run);
            Run("execution policy options", ExecutionPolicyCases.Run);
            Run("range level settings", RangeStateCases.Run);
            Console.WriteLine("OFFLINE ASSERTIONS: " + _passed + "/" + (_passed + _failed) + " PASS; failures=" + _failed);
            Console.WriteLine("No live connector, native replay session, GUI or broker operations executed.");
            return _failed == 0 ? 0 : 1;
        }
        private static void Run(string name, Action test)
        {
            try { test(); }
            catch (Exception error) { _failed++; Console.WriteLine("FAIL " + name + ": " + error); }
        }
        internal static void Check(bool condition, string name)
        {
            if (condition) _passed++; else { _failed++; Console.WriteLine("FAIL " + name); }
        }
        internal static void Equal<T>(T expected, T actual, string name) => Check(EqualityComparer<T>.Default.Equals(expected, actual), name + " expected=" + expected + " actual=" + actual);
        internal static void Throws(Action action, string name)
        {
            bool failed = false; try { action(); } catch (Exception) { failed = true; } Check(failed, name);
        }
        internal static Futures2PlanInput Input() => new Futures2PlanInput { Instrument = "TEST", Currency = "RUB", Low = -1, High = 1, Count = 3,
            Tick = 0.00001m, TickValue = 0.00001m, VolumeStep = 1, MinimumVolume = 1, FixedVolume = 10, Collateral = 7, Budget = 210, Markup = 0.1m, PercentBase = 2 };
        internal static Futures2Plan Plan() => Futures2Plan.Build(Input());
        internal static Futures2Intent Entry(Futures2Book book, Futures2Plan plan, decimal quantity = 10)
        {
            Futures2Intent intent = new Futures2Intent { PlanId = plan.Id, Entry = true, Quantity = quantity, PositionNumber = 1, OrderNumber = book.Intents.Count + 1, Created = T0 };
            intent.Allocations.Add(new Futures2Allocation { LevelId = 0, Quantity = quantity }); book.Record(intent); return intent;
        }
        internal static Dictionary<int, Futures2Quote> Quotes(decimal bid = -0.20001m, decimal ask = -0.2m, DateTime? time = null, decimal funds = 210)
            => new Dictionary<int, Futures2Quote> { [0] = new Futures2Quote { HasBid = true, HasAsk = true, Bid = bid, Ask = ask, Time = time ?? T0, Ready = true, FreeMargin = funds } };
        internal static Futures2Controller Controller(Futures2Policy policy = null)
        {
            Futures2Controller controller = new Futures2Controller(new Futures2Checkpoint());
            controller.Configure(Plan(), policy ?? new Futures2Policy { IntervalMilliseconds = 0 }, 210, 0);
            controller.Start(true, T0); return controller;
        }
        private static void MathCases()
        {
            Equal(-1.23460m, SignedPriceMath.Quantize(-1.23456m, 0.00005m, false), "negative floor");
            Equal(-1.23455m, SignedPriceMath.Quantize(-1.23456m, 0.00005m, true), "negative ceiling");
            Equal(0m, SignedPriceMath.Quantize(-0.00001m, 0.00005m, true), "zero is successful rounding");
            Equal(-0.00005m, SignedPriceMath.Quantize(-0.00001m, 0.00005m, false), "negative epsilon floor");
            Check(SignedPriceMath.HasFivePlaces(1.230000m) && !SignedPriceMath.HasFivePlaces(1.230001m), "numeric precision ignores trailing zeroes");
            decimal[] expected = { -0.00010m, -0.00004m, 0.00003m, 0.00010m };
            Check(expected.SequenceEqual(SignedPriceMath.BuildGrid(-0.00010m, 0.00010m, 4, 0.00001m)), "integer tick interpolation across zero");
            Throws(() => SignedPriceMath.BuildGrid(-0.00001m, 0.00001m, 4, 0.00001m), "no duplicate levels");
            Throws(() => SignedPriceMath.ValidateTick(0.000001m), "six-place tick rejected");
            Throws(() => SignedPriceMath.BuildGrid(-1.23456m, 0, 3, 0.00005m), "off-tick bound rejected");
            Throws(() => SignedPriceMath.BuildGrid(-1, 1, 1, 1), "one level rejected");
            Futures2PlanInput input = Input(); input.FixedVolume = 0; input.Budget = 100;
            Futures2Plan plan = Futures2Plan.Build(input);
            Check(plan.Levels.Select(l => l.Volume).SequenceEqual(new[] { 4m, 5m, 5m }), "exact budget carry");
            Equal(98m, plan.Reserved, "positive collateral reserve");
            Futures2PlanInput automatic = Input(); automatic.CountMode = Futures2CountMode.Budget;
            Equal(3, Futures2Plan.Build(automatic).Input.Count, "count calculated from positive collateral and fixed quantity");
            automatic.CountMode = Futures2CountMode.Step; automatic.RequestedStep = 0.5m; automatic.FixedVolume = 0;
            Equal(5, Futures2Plan.Build(automatic).Input.Count, "count calculated from requested spacing");
            input.Low = -100;
            Equal(-1m, plan.Input.Low, "immutable input copy");
            input = Input(); input.FixedVolume = 0; input.Budget = 1; input.Collateral = 0.7m; input.MinimumVolume = input.VolumeStep = 0.1m;
            plan = Futures2Plan.Build(input);
            Check(plan.Levels.Select(l => l.Volume).SequenceEqual(new[] { 0.4m, 0.5m, 0.5m }), "fractional exact carry");
            input = Input(); input.FixedVolume = 5; input.Budget = 100;
            Throws(() => Futures2Plan.Build(input), "fixed plan does not silently shrink");
            plan = Plan();
            Equal(-0.9m, plan.Target(plan.Levels[0], -1, Futures2ExitMode.LevelFromEnter, false), "signed long target");
            Equal(0.5m, plan.Distance(25, Futures2DistanceUnit.PercentBase), "independent percent reference");
            Equal(0.00003m, plan.Distance(3, Futures2DistanceUnit.Ticks), "tick distance");
            input = Input(); input.Direction = Futures2Direction.Short; plan = Futures2Plan.Build(input);
            Equal(-1.1m, plan.Target(plan.Levels[0], -1, Futures2ExitMode.LevelFromEnter, false), "signed short target");
            Equal(-1m, plan.Levels[0].Price, "short ascending order");
            Equal(1m, Plan().Levels[0].Price, "long descending order");
        }
        private static void BookCases()
        {
            Futures2Plan plan = Plan();
            Dictionary<string, Futures2Plan> plans = new Dictionary<string, Futures2Plan> { [plan.Id] = plan };
            Futures2Book book = new Futures2Book(); Futures2Intent entry = Entry(book, plan);
            book.Fill(entry, plan, "a", 3, -1, 0, false, T0);
            Equal(3m, book.Lots[0].Quantity, "partial entry held"); Equal(-3m, book.Lots[0].Cost, "signed remaining cost");
            Equal(70m, book.Reserved(plans), "partial transfers reservation to inventory"); Equal(10m, book.Occupied(plan.Id, 0), "entry remainder still occupies level");
            book.Fill(entry, plan, "b", 2, 1, 0, false, T0);
            Equal(-0.2m, book.Lots[0].Average.Value, "partial average crosses zero");
            Check(!book.Fill(entry, plan, "b", 2, 1, 0, false, T0), "fill dedup");
            book.Observe(entry, Futures2IntentState.Canceled, 7);
            Equal(Futures2IntentState.Unknown, entry.State, "cancel before missing fills is unknown"); Equal(70m, book.Reserved(plans), "unknown retains reserve");
            book.Fill(entry, plan, "c", 2, 0, 0, false, T0);
            Equal(Futures2IntentState.Canceled, entry.State, "late fill restores remembered cancel"); Equal(49m, book.Reserved(plans), "only canceled remainder releases");
            book.Fill(entry, plan, "d", 1, 0, 0, false, T0);
            Equal(8m, book.Lots[0].Quantity, "valid late canceled fill accounted"); Equal(Futures2IntentState.Canceled, entry.State, "late fill does not resurrect canceled remainder");
            Futures2Book copy = Futures2Commands.Copy(book);
            Check(!copy.Fill(copy.Intents[0], plan, "d", 1, 0, 0, false, T0), "restart dedup persisted");
            book = new Futures2Book(); entry = Entry(book, plan);
            book.Observe(entry, Futures2IntentState.Filled, 0);
            Equal(Futures2IntentState.Unknown, entry.State, "contradictory Done cannot invent fills");
            book.Fill(entry, plan, "1", 4, 0, 0, false, T0);
            Equal(Futures2IntentState.Unknown, entry.State, "partial identified details retain unknown");
            book.Fill(entry, plan, "2", 6, 0, 0, false, T0);
            Equal(Futures2IntentState.Filled, entry.State, "full identified details complete Done");
            Check(book.Lots[0].Average.HasValue && book.Lots[0].Average == 0, "zero average has presence");
            Futures2Intent close = new Futures2Intent { PlanId = plan.Id, Quantity = 10, PositionNumber = 1,
                Allocations = new List<Futures2Allocation> { new Futures2Allocation { LotId = book.Lots[0].Id, Quantity = 10 } } };
            book.Record(close); book.Fill(close, plan, "x", 3, 1, 0, false, T0);
            Equal(7m, book.Lots[0].Quantity, "partial exit leaves held"); Equal(0m, book.Available(book.Lots[0]), "pending exits reserve remainder"); Equal(3m, book.Realized, "signed absolute PnL");
            book.Observe(close, Futures2IntentState.Canceled, 5); book.Fill(close, plan, "y", 2, 1, 0, true, T0);
            Equal(5m, book.Available(book.Lots[0]), "late exit details release canceled remainder");
            Equal(56m, book.DaySpent, "credit only configured confirmed exit");
            book = new Futures2Book(); entry = Entry(book, plan, 2);
            string before = System.Text.Json.JsonSerializer.Serialize(book);
            Throws(() => book.Fill(entry, plan, "overflow", 2, decimal.MaxValue, 0, false, T0), "fill overflow rejected");
            Equal(before, System.Text.Json.JsonSerializer.Serialize(book), "overflow leaves book wholly unchanged");
            Throws(() => book.Fill(entry, plan, "zero", 0, 0, 0, false, T0), "nonpositive fill rejected");
            book.Day = T0.Date; book.DaySpent = 20;
            book.Fill(entry, plan, "old", 1, 0, 0, false, T0.AddDays(-1));
            Equal(T0.Date, book.Day, "late fill cannot regress day"); Equal(27m, book.DaySpent, "late evidence charged conservatively without reset");
            entry.State = Futures2IntentState.CancelPending;
            book.Observe(entry, Futures2IntentState.Working, 1);
            Equal(Futures2IntentState.CancelPending, entry.State, "stale working does not undo cancel pending");
        }
        private static void ControllerCases()
        {
            Futures2Controller controller = Controller();
            List<Futures2Action> actions = controller.Decide(Quotes(), T0, true);
            Equal(1, actions.Count, "current-price predicate needs no crossing"); Equal(10m, actions[0].Intent.Quantity, "planned volume");
            Futures2Intent entry = actions[0].Intent; entry.State = Futures2IntentState.Working; entry.OrderNumber = 1;
            controller.Pause(); actions = controller.Decide(Quotes(), T0.AddSeconds(1), true);
            Check(actions.Count == 1 && actions[0].Cancel, "pause cancels entry"); Equal(Futures2IntentState.CancelPending, entry.State, "cancel is pending");
            Equal(0, controller.Decide(Quotes(), T0.AddSeconds(2), true).Count, "cancel not resent blindly");
            controller.Decide(Quotes(time: T0.AddSeconds(32)), T0.AddSeconds(32), true);
            Equal(Futures2IntentState.Unknown, entry.State, "cancel timeout unknown"); Equal(70m, controller.Data.Book.Reserved(controller.Data.Plans), "timeout keeps money reserved");
            controller = Controller(new Futures2Policy { GroupOrders = true, MaxActions = 1, IntervalMilliseconds = 0 });
            actions = controller.Decide(Quotes(), T0, true);
            Equal(1, actions.Count, "one grouped native order"); Equal(20m, actions[0].Intent.Quantity, "compatible levels grouped deterministically"); Equal(2, actions[0].Intent.Allocations.Count, "group allocation identities");
            controller = Controller(new Futures2Policy { PreEntries = true, PreDistance = 2, ForbidLong = true, IntervalMilliseconds = 0 });
            Equal(0, controller.Decide(Quotes(1.1m, 1.2m), T0, true).Count, "preorders obey directional ban");
            controller = Controller(new Futures2Policy { MaxActions = 3, IntervalMilliseconds = 0 });
            actions = controller.Decide(Quotes(funds: 100), T0, true);
            Equal(1, actions.Count, "batch free margin is reserved");
            controller = Controller(); Futures2Plan plan = controller.Data.Plans[controller.Data.ActivePlan];
            entry = Entry(controller.Data.Book, plan); controller.Data.Book.Fill(entry, plan, "held", 10, 0, 0, false, T0);
            Futures2Policy changed = new Futures2Policy { ExitMode = Futures2ExitMode.WholePosition, IntervalMilliseconds = 0, ForbidEntries = true };
            controller.Configure(plan, changed, 210, 0);
            controller.Decide(Quotes(), T0, true);
            Equal(Futures2ExitMode.WholePosition, controller.Data.Policy.ExitMode, "runtime policy applies without re-funding held inventory");
            Futures2PlanInput different = Input(); different.Direction = Futures2Direction.Short;
            Throws(() => controller.Configure(Futures2Plan.Build(different), changed, 210, 0), "direction change with inventory rejected");
            controller.Flatten("test");
            actions = controller.Decide(Quotes(), T0.AddSeconds(1), true);
            Check(actions.Count == 1 && !actions[0].Cancel && actions[0].Intent.Protective, "emergency emits owned protective reduction");
            Futures2Intent reduction = actions[0].Intent; reduction.State = Futures2IntentState.Working; reduction.OrderNumber = 2;
            Equal(0, controller.Decide(Quotes(), T0.AddSeconds(2), true).Count, "emergency does not cancel its own new reduction");
            controller.Data.Book.Fill(reduction, plan, "closed", 10, -0.2m, 0, false, T0.AddSeconds(3));
            controller.Decide(Quotes(), T0.AddSeconds(3), false);
            Check(controller.Data.State != Futures2State.FlatConfirmed, "local empty is not confirmed flat");
            controller.Decide(Quotes(), T0.AddSeconds(3), true);
            Equal(Futures2State.FlatConfirmed, controller.Data.State, "native/account agreement confirms flat");
            controller = Controller(); plan = controller.Data.Plans[controller.Data.ActivePlan];
            Futures2Plan next = Futures2Commands.Shift(plan, 0.00001m);
            controller.Configure(next, new Futures2Policy(), 210, 0); controller.Flatten("stop during configure");
            Check(controller.Data.PendingPlan == null, "emergency prevents pending configuration publication");
            controller = Controller(new Futures2Policy { DayLimit = 70, MaxActions = 3, IntervalMilliseconds = 0 });
            Equal(1, controller.Decide(Quotes(), T0, true).Count, "daily pending collateral budget");
            controller = Controller(); plan = controller.Data.Plans[controller.Data.ActivePlan];
            controller.Data.LevelEdit = new Futures2LevelEdit { Plan = plan.Id, Level = 0, Entry = false, Markup = 0.2m };
            controller.Decide(Quotes(), T0, true);
            Check(!plan.Levels[0].EntryEnabled && plan.Levels[1].EntryEnabled, "selected level skip"); Equal(0.2m, plan.Levels[0].Markup, "selected markup");
            controller = Controller(); plan = controller.Data.Plans[controller.Data.ActivePlan];
            controller.Data.Receipts["inbound"] = 70;
            actions = controller.Decide(Quotes(), T0, true);
            Equal("inbound", actions[0].Intent.TransferId, "destination entry correlated to funded transfer");
            Equal(70m, actions[0].Intent.TransferBudget, "destination allocation bounded by transfer credit");
            actions[0].Intent.State = Futures2IntentState.Working;
            Equal(0m, actions[0].Intent.Filled, "credit alone is not transferred position completion");
            controller = Controller(); plan = controller.Data.Plans[controller.Data.ActivePlan];
            entry = Entry(controller.Data.Book, plan); controller.Data.Book.Fill(entry, plan, "r", 10, 0, 0, false, T0);
            controller.Reduce(0, "first reduction"); actions = controller.Decide(Quotes(), T0, true);
            reduction = actions[0].Intent; reduction.State = Futures2IntentState.Working; reduction.OrderNumber = 2;
            controller.Reduce(35, "revised retain"); actions = controller.Decide(Quotes(), T0.AddSeconds(1), true);
            Check(actions.Count == 1 && actions[0].Cancel, "revised reduction cancels older protective generation");
            controller = Controller(new Futures2Policy { Trailing = true, TrailTarget = 10, EqualizeVolumes = true, IntervalMilliseconds = 0 });
            controller.Decide(Quotes(), T0, true, 0, true);
            actions = controller.Decide(Quotes(), T0.AddSeconds(1), true, -2, true);
            Check(actions.All(a => a.Cancel) && !controller.Data.Reducing, "portfolio helper delegates retention before local close");
            Futures2Quote quote = Quotes(0, 0)[0]; Check(quote.Valid(T0, 10), "both present zero sides valid");
            quote.HasAsk = false; Check(!quote.Valid(T0, 10), "missing ask differs from zero");
            quote.HasAsk = true; quote.Time = T0.AddSeconds(-10); Check(quote.Valid(T0, 10), "freshness inclusive boundary");
            quote.Time = quote.Time.AddTicks(-1); Check(!quote.Valid(T0, 10), "freshness just outside boundary");
        }
        private static void HelperCases()
        {
            Futures2Hj hj = new Futures2Hj(); hj.Reset(0, 1); hj.Update(-1);
            Check(hj.Depth == 1 && !hj.Active, "HJ initial movement"); hj.Update(-2);
            Equal(-4m, hj.From, "HJ lower boundary"); Equal(-2m, hj.To, "HJ upper boundary");
            Check(hj.Allows(-4) && !hj.Allows(-3) && hj.Allows(-2), "HJ strict interior");
            hj.Update(-3); hj.Update(-4); Equal(3, hj.Depth, "HJ triangular depth"); Equal(-7m, hj.From, "HJ widened boundary");
            hj.Update(-3); Equal(3, hj.Depth, "HJ reversal hysteresis"); hj.Update(-2); Check(hj.Depth == 1 && !hj.Active, "HJ accepted reversal");
            Futures2Hj resumed = Futures2Commands.Copy(hj); hj.Update(5); resumed.Update(5);
            Equal(System.Text.Json.JsonSerializer.Serialize(hj), System.Text.Json.JsonSerializer.Serialize(resumed), "HJ persistence replay");
            Futures2Policy policy = new Futures2Policy { Trailing = true, TrailTarget = 10 };
            Futures2Trail trail = new Futures2Trail(); trail.Reset(T0);
            Check(!trail.Evaluate(policy, 0, false, T0) && !trail.Evaluate(policy, -1, false, T0) && trail.Evaluate(policy, -1.00001m, false, T0), "primary trailing strictly greater");
            policy = new Futures2Policy { Trailing = true, TrailDynamic = true, TrailStep = 3, TrailTarget = 6, TrailMinimum = 1 };
            trail = new Futures2Trail(); trail.Reset(T0); trail.Evaluate(policy, 1, false, T0);
            Check(!trail.Evaluate(policy, -0.1m, false, T0), "source dynamic decreasing stop formula");
            policy = new Futures2Policy { Trailing = true, TrailFromMax = false, TrailTarget = 3 };
            trail = new Futures2Trail(); trail.Reset(T0); trail.Evaluate(policy, -1, false, T0);
            Check(!trail.Evaluate(policy, -2, false, T0), "From0 negative maximum uses H minus P");
            policy = new Futures2Policy { Trailing = true, DeferredClose = true, TrailTarget = 10 };
            trail = new Futures2Trail(); trail.Reset(T0); trail.Evaluate(policy, 0, false, T0); trail.Evaluate(policy, -1.1m, false, T0);
            Check(!trail.Evaluate(policy, -0.5m, false, T0) && trail.ForbidEntries, "deferred recovery forbids increases");
            policy = new Futures2Policy { Trailing = true, TrailStep = 10, TrailTarget = 10, MiniStop = true, MiniMinutes = 1, MiniLimited = true };
            trail = new Futures2Trail { FirstEntry = T0 }; trail.Reset(T0.AddMinutes(-10));
            Check(!trail.Evaluate(policy, -0.6m, false, T0.AddSeconds(60)), "mini strict opening-time boundary");
            Check(trail.Evaluate(policy, -0.6m, false, T0.AddSeconds(61)), "mini within window");
            policy.MiniStop = false; policy.TimeStopMinutes = 1;
            trail = new Futures2Trail { FirstEntry = T0 }; trail.Reset(T0.AddMinutes(-10));
            Check(!trail.Evaluate(policy, 0, false, T0.AddSeconds(60)), "time stop strict entry-time boundary");
            Check(trail.Evaluate(policy, 0, false, T0.AddSeconds(61)), "time stop after entry deadline");
            policy.TimeStopMinutes = 0; policy.EmptyStopMinutes = 1;
            trail = new Futures2Trail(); trail.Reset(T0); trail.Evaluate(policy, 0, true, T0.AddSeconds(61));
            Check(trail.Disabled, "empty stop latched"); trail.Evaluate(policy, 0, true, T0.AddMinutes(5)); Check(trail.Disabled, "empty stop does not auto-rearm");
            trail.Reset(T0); Check(!trail.Disabled && !trail.HasStopped, "explicit helper rearm");
            Futures2Plan plan = Plan(); Futures2Plan shifted = Futures2Commands.Shift(plan, -0.00001m);
            Equal(-1.00001m, shifted.Input.Low, "signed shift"); Equal(-1m, plan.Input.Low, "shift source untouched");
            Throws(() => Futures2Commands.Shift(plan, 0.000001m), "off-tick shift rejected");
            Futures2Plan wide = Futures2Commands.Widen(plan, -2, 3); Equal(-2m, wide.Input.Low, "widen lower"); Equal(3m, wide.Input.High, "widen upper");
        }
        private static void ReplacementCases()
        {
            Futures2Controller controller = Controller(new Futures2Policy { MaxActions = 1, IntervalMilliseconds = 0, ForbidEntries = true });
            Futures2Plan plan = controller.Data.Plans[controller.Data.ActivePlan];
            for (int n = 0; n < 2; n++)
            {
                Futures2Intent entry = Entry(controller.Data.Book, plan, 2);
                controller.Data.Book.Fill(entry, plan, "manual-entry-" + n, 2, 0, 0, false, T0);
            }
            controller.Data.ManualExit = 0; controller.Data.ManualExitPlan = plan.Id;
            Futures2Intent close = controller.Decide(Quotes(), T0, true).Single().Intent;
            Equal(2, controller.Data.ManualExitLots.Count, "manual exit snapshots all selected lots");
            controller.Data.Book.Fill(close, plan, "manual-close-part", 1, -0.2m, 0, false, T0);
            controller.Data.Book.Observe(close, Futures2IntentState.Canceled, 1);
            close = controller.Decide(Quotes(), T0, true).Single().Intent;
            Equal(1m, close.Quantity, "manual exit retries confirmed unfilled remainder");
            controller.Data.Book.Fill(close, plan, "manual-close-rest", 1, -0.2m, 0, false, T0);
            close = controller.Decide(Quotes(), T0, true).Single().Intent;
            Equal(2m, close.Quantity, "manual exit reaches second lot despite one-action limit");
            controller.Data.Book.Fill(close, plan, "manual-close-last", 2, -0.2m, 0, false, T0);
            controller.Decide(Quotes(), T0, true); Equal(0, controller.Data.ManualExitLots.Count, "manual command ends after identified closes");

            controller = Controller(); plan = controller.Data.Plans[controller.Data.ActivePlan];
            controller.Data.Receipts["credit-a"] = 35; controller.Data.Receipts["credit-b"] = 35;
            Futures2Intent funded = controller.Decide(Quotes(5, 5), T0, true).Single().Intent;
            Equal(2, funded.TransferBudgets.Count, "one entry attributes multiple incoming credits");
            Equal(70m, funded.TransferBudgets.Values.Sum(), "combined funding cannot disappear");
            Equal(5m + controller.Data.Policy.SlippageTicks * plan.Input.Tick, funded.Price, "funded transfer enters independently of grid price predicate");
            controller.Data.Book.Fill(funded, plan, "funded-part", 5, 5, 0, false, T0);
            Equal(17.5m, funded.TransferBudgets["credit-b"] * funded.Filled / funded.Quantity, "partial multi-source attribution proportional and exact");

            controller = Controller(new Futures2Policy { StopAfterExit = true, IntervalMilliseconds = 0 });
            plan = controller.Data.Plans[controller.Data.ActivePlan];
            Futures2Intent original = Entry(controller.Data.Book, plan);
            controller.Data.Book.Fill(original, plan, "old", 10, 0, 0, false, T0);
            Futures2PlanInput replacementInput = Input(); replacementInput.Instrument = "NEXT"; replacementInput.Low = 2; replacementInput.High = 4;
            Futures2Rollover rollover = new Futures2Rollover { Plan = Futures2Plan.Build(replacementInput), Endpoint = 1, State = "Draining" };
            controller.Data.Rollover = rollover; rollover.Capture(controller.Data);
            Equal(10m, rollover.Required[0], "replacement freezes actual old exposure");
            controller.Reduce(0, "replacement"); rollover.State = "Reducing";
            close = controller.Decide(Quotes(2, 2), T0, true).Single().Intent;
            controller.Data.Book.Fill(close, plan, "old-close", 10, 2, 0, false, T0);
            rollover.CarryBasis(controller.Data);
            Equal(-2m, rollover.Plan.ExitCarry[0], "old realized move carried as exit basis only");
            Equal(20m, controller.Data.Book.Realized, "carry does not rewrite realized fills");
            controller.Data.Reducing = false;
            controller.Configure(rollover.Plan, controller.Data.Policy, 210, 1); controller.Data.ResumeAfterConfigure = true; rollover.State = "Entering";
            Dictionary<int, Futures2Quote> quotes = Quotes(2, 2); quotes[1] = Quotes(5, 5)[0];
            controller.Decide(quotes, T0, true);
            Futures2Intent replacement = controller.Decide(quotes, T0, true).Single().Intent;
            Check(replacement.Entry && replacement.Endpoint == 1 && replacement.Quantity == 10, "replacement enters mapped volume above grid despite StopAfterExit");
            controller.Data.Book.Fill(replacement, rollover.Plan, "new", 10, 5, 0, false, T0);
            controller.Decide(quotes, T0, true);
            Equal("Applied", rollover.State, "replacement completes on identified new fills");
            Equal(3.1m, rollover.Plan.Target(rollover.Plan.Levels[0], 5 + rollover.Plan.ExitCarry[0], Futures2ExitMode.LevelFromEnter, false), "replacement exit economic basis");
            controller = Controller(); controller.Data.Rollover = new Futures2Rollover { State = "Draining", Plan = Plan() };
            controller.Flatten("interrupt replacement"); Equal("Canceled", controller.Data.Rollover.State, "emergency cannot resume a replacement");

            controller = Controller(); plan = controller.Data.Plans[controller.Data.ActivePlan];
            original = Entry(controller.Data.Book, plan); original.State = Futures2IntentState.Working;
            Futures2PlanInput shortInput = Input(); shortInput.Direction = Futures2Direction.Short;
            controller.Configure(Futures2Plan.Build(shortInput), controller.Data.Policy, 210, 0);
            Check(controller.Decide(Quotes(), T0, true).Single().Cancel, "direction change first requests old entry cancellation");
            controller.Data.Book.Fill(original, plan, "late-direction", 1, 0, 0, false, T0);
            controller.Data.Book.Observe(original, Futures2IntentState.Canceled, 1);
            controller.Decide(Quotes(), T0, true);
            Equal(plan.Id, controller.Data.ActivePlan, "late old-direction fill prevents opposite plan publication");
            Check(controller.Data.PendingPlan == null && controller.Data.State != Futures2State.Active, "conflicting direction configuration rejected with entries paused");

            controller = Controller(new Futures2Policy { ForbidEntries = true, WholeMarkup = 1, IntervalMilliseconds = 0 });
            plan = controller.Data.Plans[controller.Data.ActivePlan]; plan.Levels[0].Markup = 1;
            original = Entry(controller.Data.Book, plan, 2); original.Allocations[0].ExitCarry = -10;
            controller.Data.Book.Fill(original, plan, "carry-part1", 1, 200, 0, false, T0);
            controller.Data.Book.Fill(original, plan, "carry-part2", 1, 200, 0, false, T0);
            Futures2Lot carried = controller.Data.Book.Lots.Single(); Equal(-10m, carried.ExitCarry, "partial replacement fills retain per-unit carry");
            close = controller.Decide(Quotes(191, 191), T0, true).Single().Intent;
            Equal(191m, close.Price, "carried first replacement target");
            controller.Data.Book.Fill(close, plan, "carry-exit-part", 1, 191, 0, false, T0);
            Futures2Intent fresh = Entry(controller.Data.Book, plan, 1); controller.Data.Book.Fill(fresh, plan, "fresh-cycle", 1, 200, 0, false, T0);
            Equal(0m, controller.Data.Book.Lots.Single(l => l.Id.StartsWith(fresh.Id)).ExitCarry, "ordinary reentry never inherits settled plan carry");
            controller.Data.Book.Observe(close, Futures2IntentState.Canceled, 1); controller.Data.Policy.ExitMode = Futures2ExitMode.WholePosition;
            controller.Data.Policy.PreExits = true; controller.Data.Policy.PreDistance = 100;
            close = controller.Decide(Quotes(190, 190), T0, true).Single().Intent;
            Equal(196m, close.Price, "whole-position average weights only remaining carried inventory");

            controller = Controller(new Futures2Policy { GapReferencesKnown = true, PreviousSessionClose = 10, CurrentSessionOpen = 8,
                Rules = "entry;gap;1;3;any", IntervalMilliseconds = 0 });
            Equal(0, controller.Decide(Quotes(), T0, true).Count, "negative signed gap is blocked by its documented absolute magnitude");
            Throws(() => new Futures2Policy { Rules = "entry;gap;-3;-1;any" }.Validate(), "negative gap magnitude bounds rejected explicitly");
        }
        private static void StoreCases()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Futures2-offline-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string path = Path.Combine(directory, "state.json"); Futures2Store store = new Futures2Store(path);
                Check(store.Load() == null, "new store absent"); Futures2Controller controller = Controller(); store.Save(controller.Data);
                Equal(controller.Data.Campaign, store.Load().Campaign, "checkpoint roundtrip");
                controller.Pause(); store.Save(controller.Data); Check(File.Exists(path + ".bak"), "atomic replacement preserves backup");
                File.WriteAllText(path, "{}"); Throws(() => store.Load(), "corrupt checkpoint blocks without silent backup rollback");
                Check(!Futures2Store.Name("../../owner").Contains("/"), "state filename cannot traverse");
            }
            finally
            {
                // Delete only individually named files inside this uniquely created temporary directory.
                foreach (string file in new[] { "state.json", "state.json.bak", "state.json.tmp" }) File.Delete(Path.Combine(directory, file));
                Directory.Delete(directory);
            }
            Futures2Coordination.Publish(new Futures2Peer { Name = "test/a", Currency = "RUB", Capital = 100, ReturnBase = 100, Profit = 10, Held = 70, Time = T0, AcceptCoordination = true });
            Futures2Coordination.Publish(new Futures2Peer { Name = "test/b", Currency = "RUB", Capital = 200, ReturnBase = 200, Profit = -4, Held = 140, Time = T0, AcceptCoordination = true });
            Futures2Peer shifted = Futures2Coordination.Get("test/a"); shifted.Capital = 0; Futures2Coordination.Publish(shifted);
            Equal(2m, Futures2Coordination.Helper(new[] { "test/a", "test/b" }, "RUB", T0, 10).Percent, "full source capital transfer preserves group fixed return basis");
            shifted.Capital = 999; Futures2Coordination.Publish(shifted);
            Equal(2m, Futures2Coordination.Percent(new[] { "test/a", "test/b" }, "RUB", T0, 10).Value, "manual capital change cannot change group denominator");
            Equal(2m, Futures2Coordination.Percent(new[] { "test/a", "test/b" }, "RUB", T0, 10).Value, "portfolio weighted return");
            Check(!Futures2Coordination.Percent(new[] { "test/a", "missing" }, "RUB", T0, 10).HasValue, "missing participant invalidates aggregate");
            Check(!Futures2Coordination.Percent(new[] { "test/a" }, "USD", T0, 10).HasValue, "no implicit currency conversion");
            Check(!Futures2Coordination.Percent(new[] { "test/a" }, "RUB", T0.AddSeconds(11), 10).HasValue, "stale group mark invalidates aggregate");
            Check(Futures2Coordination.Send("test/b", new Futures2PeerCommand { Id = "transfer", Source = "test/a", Currency = "RUB", Amount = 20 }), "consenting mailbox accepts");
            Check(Futures2Coordination.Receive("test/b", out Futures2PeerCommand command) && command.Amount == 20, "mailbox detached transfer command");
            Check(!Futures2Coordination.Receive("test/b", out command), "mailbox consumed once");
            Futures2Coordination.Remove("test/a"); Futures2Coordination.Remove("test/b");
        }
    }
}
