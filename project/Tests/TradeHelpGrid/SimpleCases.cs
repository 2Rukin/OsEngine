using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using OsEngine.Entity;
using OsEngine.Logging;
using OsEngine.OsTrader.Grids.Futures2;
using OsEngine.OsTrader.Panels.Tab;
using OsEngine.Robots;
using OsEngine.Robots.MyBots;

namespace OsEngine.TradeHelpGrid.Tests
{
    /// <summary>Managed Simple geometry, native discovery and controller/recovery regressions without robot construction.</summary>
    /// <remarks>Runs in the default TradeHelpGrid console suite with synthetic Security/quote/book fixtures.
    /// No GUI, server, credentials or native order effects; does not qualify live fills or physical UI.</remarks>
    internal static class SimpleCases
    {
        internal static void Run()
        {
            Geometry(); Invalid(); Reconfigure(); PausedReconfiguration(); SecurityReadiness(); Recovery(); Discovery();
        }
        private static Security Security() => new Security { Name = "SIMPLE", PriceStep = 0.00001m, PriceStepCost = 0.00001m,
            Lot = 1, DecimalsVolume = 0, VolumeStep = 1, MinTradeAmount = 1, MinTradeAmountType = MinTradeAmountType.Contract };
        private static Futures2SimpleSettings Settings() => new Futures2SimpleSettings
        { Low = -0.00010m, High = 0.00010m, Step = 0.00005m, Markup = 0.00002m, Lots = 2, Collateral = 7, Funds = 1000 };
        private static void Geometry()
        {
            foreach (Futures2Direction direction in Enum.GetValues<Futures2Direction>())
            {
                Futures2SimpleSettings settings = Settings(); settings.Direction = direction;
                settings.ZoneLots = "1=3;5=4"; settings.ZoneMarkups = "1=0,00003;3=0.00005";
                Futures2Plan plan = settings.Build(Security());
                Futures2Level[] ascending = plan.Levels.OrderBy(l => l.Price).ToArray();
                Program.Check(ascending.Select(l => l.Price).SequenceEqual(new[] { -0.00010m, -0.00005m, 0m, 0.00005m, 0.00010m }), "Simple exact five-place signed prices " + direction);
                Program.Check(ascending.Select(l => l.Volume).SequenceEqual(new[] { 3m, 2m, 2m, 2m, 4m }), "Simple lots indexed from low independently of direction");
                Program.Check(ascending.Select(l => l.Markup).SequenceEqual(new[] { 0.00003m, 0.00002m, 0.00005m, 0.00002m, 0.00002m }), "Simple decimal comma and per-zone markups");
                Program.Check(ascending.Select(l => Futures2SimpleSettings.Zone(plan, l)).SequenceEqual(new[] { 1, 2, 3, 4, 5 }), "Simple human zone numbering");
                Program.Equal(91m, plan.Reserved, "Simple reserve is sum of actual zone quantities");
                Program.Equal(direction == Futures2Direction.Long ? 0.00005m : -0.00005m,
                    plan.Target(ascending[2], 0, Futures2ExitMode.LevelFromEnter, false), "Simple target from signed actual average");
                settings.Low = 10;
                Program.Equal(-0.00010m, plan.Input.Low, "Simple settings detached from accepted plan");
            }
            Futures2SimpleSettings fractional = Settings(); fractional.Lots = 0.25m; fractional.ZoneLots = "2=0.50";
            Security fractionalSecurity = Security(); fractionalSecurity.DecimalsVolume = 2;
            fractionalSecurity.VolumeStep = fractionalSecurity.MinTradeAmount = 0.25m;
            Program.Equal(10.50m, fractional.Build(fractionalSecurity).Reserved, "Simple native fractional quantities");
            Futures2SimpleSettings cap = Settings(); cap.Low = 0; cap.High = 9999; cap.Step = 1;
            cap.Lots = 1; cap.Collateral = 1; cap.Funds = 10000;
            Program.Equal(10000, cap.Build(Security()).Levels.Count, "Simple explicit 10000-level boundary");
        }
        private static void Invalid()
        {
            List<Action<Futures2SimpleSettings>> changes = new List<Action<Futures2SimpleSettings>>
            {
                s => s.Step = 0, s => s.Step = -1, s => s.Step = 0.00003m,
                s => s.Low = s.High, s => s.High = s.Low - 1, s => s.Step = 1,
                s => s.Low = -0.000101m, s => s.Step = 0.000001m, s => s.Markup = 0,
                s => s.Markup = 0.000001m, s => s.Lots = 0, s => s.Lots = 1.5m,
                s => s.ZoneLots = "1=0", s => s.ZoneLots = "1=-2", s => s.ZoneLots = "1=1.5",
                s => s.ZoneLots = "0=1", s => s.ZoneLots = "6=1", s => s.ZoneLots = "1=2;1=3",
                s => s.ZoneLots = "1=2;", s => s.ZoneLots = "1=2,3,4", s => s.ZoneMarkups = "2=0",
                s => s.ZoneMarkups = "2=0.000001", s => s.Funds = 69, s => s.Collateral = 0,
                s => s.Direction = (Futures2Direction)99, s => { s.Low = 0; s.High = 10000; s.Step = 1; }
            };
            for (int index = 0; index < changes.Count; index++)
            {
                Futures2SimpleSettings settings = Settings(); changes[index](settings);
                Program.Throws(() => settings.Build(Security()), "Simple invalid input rejects before configuration " + index);
            }
            Security security = Security(); security.MarginBuy = 8;
            Program.Throws(() => Settings().Build(security), "Simple known broker collateral cannot be understated");
            security = Security(); security.MinTradeAmountType = MinTradeAmountType.C_Currency;
            Program.Throws(() => Settings().Build(security), "Simple rejects notional minimum instrument");
            security = Security(); security.PriceStep = 0.00002m;
            Program.Throws(() => Settings().Build(security), "Simple spacing must align with instrument tick");
        }
        private static void Reconfigure()
        {
            Futures2SimpleSettings settings = Settings(); Futures2Plan original = settings.Build(Security());
            Futures2Controller engine = new Futures2Controller(new Futures2Checkpoint());
            Futures2Policy policy = new Futures2Policy { IntervalMilliseconds = 0 };
            engine.Configure(original, policy, original.Reserved, 0, true); engine.Start(true, Program.T0);
            Dictionary<int, Futures2Quote> quotes = Program.Quotes(-0.00011m, -0.00010m, funds: 1000);
            Futures2Intent entry = engine.Decide(quotes, Program.T0, true).Single().Intent;
            entry.State = Futures2IntentState.Working; entry.PositionNumber = 1; entry.OrderNumber = 1;
            settings.ZoneLots = "1=3"; settings.ZoneMarkups = "5=0.00004";
            Futures2Plan replacement = settings.Build(Security());
            Program.Equal(JsonSerializer.Serialize(original.Input), JsonSerializer.Serialize(replacement.Input), "Simple fixture changes only level overrides");
            engine.Configure(replacement, policy, replacement.Reserved, 0, true);
            Program.Equal(original.Id, engine.Data.ActivePlan, "Simple override waits for working order outcomes");
            Program.Check(engine.Decide(quotes, Program.T0.AddSeconds(1), true).Single().Cancel, "Simple override emits cancel before publication");
            engine.Data.Book.Fill(entry, original, "simple-partial", 1, -0.00010m, 0, false, Program.T0);
            engine.Data.Book.Observe(entry, Futures2IntentState.Canceled, 1);
            engine.Decide(quotes, Program.T0.AddSeconds(2), true);
            Program.Equal(replacement.Id, engine.Data.ActivePlan, "Simple equal-input per-zone changes publish as new plan");
            Program.Equal(3m, replacement.Levels.Last().Volume, "Simple new per-zone capacity accepted");
            Program.Equal(0.00004m, replacement.Levels[0].Markup, "Simple new per-zone markup accepted");
            Program.Equal(original.Id, engine.Data.Book.Lots[0].PlanId, "Simple cancellation-time partial fill retains old plan");
            Program.Equal(0.00002m, engine.Data.Plans[original.Id].Levels[0].Markup, "Simple retained position keeps its old exit markup");
            Futures2Controller legacy = new Futures2Controller(new Futures2Checkpoint());
            legacy.Configure(original, policy, 1000, 0);
            legacy.Configure(replacement, policy, 1000, 0);
            Program.Equal(original.Id, legacy.Data.PendingPlan.Id, "Full Futures2 four-argument configuration retains manual edits");
            Futures2SimpleSettings smaller = Settings(); smaller.Lots = 1;
            Futures2Plan tiny = smaller.Build(Security());
            Futures2Controller held = new Futures2Controller(new Futures2Checkpoint());
            held.Configure(original, policy, original.Reserved, 0, true);
            Futures2Intent full = Program.Entry(held.Data.Book, original, 10);
            full.Allocations.Clear();
            foreach (Futures2Level level in original.Levels) full.Allocations.Add(new Futures2Allocation { LevelId = level.Id, Quantity = level.Volume });
            held.Data.Book.Fill(full, original, "held", 10, 0, 0, false, Program.T0); full.State = Futures2IntentState.Filled;
            held.Configure(tiny, policy, tiny.Reserved, 0, true);
            Program.Throws(() => held.Decide(quotes, Program.T0, true), "Simple shrink cannot release held collateral");
        }
        private static void Recovery()
        {
            Futures2Plan plan = Settings().Build(Security());
            Futures2Controller engine = new Futures2Controller(new Futures2Checkpoint());
            engine.Configure(plan, new Futures2Policy(), plan.Reserved, 0, true);
            engine.Start(true, Program.T0);
            Futures2Intent entry = engine.Decide(Program.Quotes(-0.00011m, -0.00010m, funds: 1000), Program.T0, true).Single().Intent;
            entry.PositionNumber = 1; entry.OrderNumber = 1;
            entry.State = Futures2IntentState.SubmitPending;
            Futures2Checkpoint recovered = Futures2Commands.Copy(engine.Data); Futures2Store.Validate(recovered);
            Futures2Controller restarted = new Futures2Controller(recovered);
            restarted.Reconcile("Restart fixture");
            Program.Throws(() => restarted.Start(true, Program.T0), "Simple recovered submit-pending never blindly resumes");
            Program.Check(restarted.Decide(Program.Quotes(), Program.T0.AddSeconds(1), false).All(a => a.Cancel), "Simple uncertain submit never creates replacement entry");
            Program.Equal(1, restarted.Data.Book.Intents.Count, "Simple restart keeps original durable intent");
            Program.Equal(plan.Levels[0].Volume * plan.Input.Collateral, restarted.Data.Book.Reserved(restarted.Data.Plans), "Simple uncertainty retains reservation");
        }
        private static void PausedReconfiguration()
        {
            foreach (bool liveHost in new[] { false, true })
                foreach (bool pauseBeforeConfigure in new[] { false, true })
                {
                    BotTabSimple first = AdapterCases.Tab("SIMPLE", out FixtureServerProxy unused);
                    BotTabSimple second = AdapterCases.Tab("RESERVE", out FixtureServerProxy unusedSecond);
                    using Futures2NativeAdapter adapter = new Futures2NativeAdapter(first, second, null, false, _ => { });
                    Futures2SimpleSettings settings = Settings(); Futures2Plan original = settings.Build(Security());
                    Futures2Policy policy = new Futures2Policy { IntervalMilliseconds = 0 };
                    Futures2Controller engine = adapter.Engine;
                    engine.Configure(original, policy, original.Reserved, 0, true); engine.Start(true, Program.T0);
                    Futures2Intent entry = Program.Entry(engine.Data.Book, original, 1);
                    engine.Data.Book.Fill(entry, original, "paused-fill", 1, 0, 0, false, Program.T0); entry.State = Futures2IntentState.Filled;
                    Dictionary<int, Futures2Quote> quotes = Program.Quotes(0.00005m, 0.00006m, funds: 1000);
                    Futures2Intent oldExit = engine.Decide(quotes, Program.T0, true).Single().Intent;
                    Program.Check(!oldExit.Entry, "Simple paused fixture starts with owned working exit");
                    oldExit.State = Futures2IntentState.Working; oldExit.OrderNumber = 2;
                    if (pauseBeforeConfigure) engine.Pause();
                    settings.Markup = 0.00003m;
                    Futures2Plan replacement = settings.Build(Security());
                    engine.Configure(replacement, policy, replacement.Reserved, 0, true);
                    engine.Pause();
                    Program.Check(engine.Decide(quotes, Program.T0.AddSeconds(1), true).Single().Cancel, "Simple Off configuration drains previous exit");
                    engine.Data.Book.Observe(oldExit, Futures2IntentState.Canceled, 0);
                    engine.Decide(quotes, Program.T0.AddSeconds(2), true);
                    Program.Equal(Futures2State.Ready, engine.Data.State, "Simple paused fixture reaches shared publication boundary");
                    Futures2GridSimple robot = (Futures2GridSimple)AdapterCases.Empty(typeof(Futures2GridSimple));
                    AdapterCases.Set(robot, "_adapter", adapter); AdapterCases.Set(robot, "_tab", first); AdapterCases.Set(robot, "_live", liveHost);
                    AdapterCases.Set(robot, "_settings", new Dictionary<string, IIStrategyParameter>
                    {
                        ["Regime"] = new StrategyParameterString("Regime", "Off", new List<string> { "Off", "On" }),
                        ["Signed order capability selected"] = new StrategyParameterBool("Signed", true),
                        ["External net"] = new StrategyParameterDecimal("External", 0, 0, 1, 1)
                    });
                    AdapterCases.Call(robot, "BeforeDecision");
                    List<Futures2Action> actions = engine.Decide(quotes, Program.T0.AddSeconds(3), true);
                    Program.Check(actions.Count == 1 && !actions[0].Intent.Entry && !actions[0].Cancel,
                        "Simple Off restores owned exit after configure without enabling entries");
                    Program.Equal(original.Id, actions[0].Intent.PlanId, "Simple paused exit retains original lot plan");
                    Program.Equal(0.00002m, actions[0].Intent.Price, "Simple paused exit retains original markup");
                    Program.Equal(Futures2State.PausedEntries, engine.Data.State, "Simple Off remains entry-paused after exit decision");
                }
        }
        private static void SecurityReadiness()
        {
            foreach (bool liveHost in new[] { false, true })
            {
                BotTabSimple first = AdapterCases.Tab("SIMPLE", out FixtureServerProxy spy);
                BotTabSimple second = AdapterCases.Tab("RESERVE", out FixtureServerProxy unused);
                using Futures2NativeAdapter adapter = new Futures2NativeAdapter(first, second, null, false, _ => { });
                Futures2GridSimple robot = (Futures2GridSimple)AdapterCases.Empty(typeof(Futures2GridSimple));
                AdapterCases.Set(robot, "_adapter", adapter); AdapterCases.Set(robot, "_tab", first); AdapterCases.Set(robot, "_live", liveHost);
                AdapterCases.Set(robot, "_configuration", ""); AdapterCases.Set(robot, "_error", ""); AdapterCases.Set(robot, "_preparationMessage", "");
                Dictionary<string, IIStrategyParameter> parameters = new Dictionary<string, IIStrategyParameter>
                {
                    ["Regime"] = new StrategyParameterString("Regime", "Off", new List<string> { "Off", "On" }),
                    ["Direction"] = new StrategyParameterString("Direction", "Long", new List<string> { "Long", "Short" }),
                    ["Signed order capability selected"] = new StrategyParameterBool("Signed", true),
                    ["Zone markup overrides"] = new StrategyParameterString("Markup overrides", ""),
                    ["Zone lot overrides"] = new StrategyParameterString("Lot overrides", "")
                };
                Dictionary<string, decimal> numbers = new Dictionary<string, decimal>
                {
                    ["Lower bound"] = -0.00010m, ["Upper bound"] = 0.00010m, ["Grid step"] = 0.00005m,
                    ["Markup per zone"] = 0.00002m, ["Lots per zone"] = 2, ["Funds limit"] = 1000,
                    ["Collateral per lot"] = 7, ["External net"] = 0
                };
                foreach (KeyValuePair<string, decimal> number in numbers)
                    parameters[number.Key] = new StrategyParameterDecimal(number.Key, number.Value, -1000, 1000, 0.00001m);
                AdapterCases.Set(robot, "_settings", parameters);
                List<string> messages = new List<string>(); int errors = 0;
                robot.LogMessageEvent += (message, type) => { messages.Add(message); if (type == LogMessageType.Error) errors++; };
                Security selected = spy.Security; spy.Security = null; first.Security = null;
                AdapterCases.Set(first.Connector, "_securityName", "");
                string before = JsonSerializer.Serialize(adapter.Engine.Data);
                bool applied = true;
                Action apply = () => applied = (bool)AdapterCases.Call(robot, "ApplyConfiguration");
                AdapterCases.Call(robot, "Run", apply); AdapterCases.Call(robot, "Run", apply);
                Program.Check(!applied && messages.Count == 1 && messages[0].Contains("не выбран"), "Simple missing selection gives one readable preparation message");
                Program.Equal(before, JsonSerializer.Serialize(adapter.Engine.Data), "Simple missing selection changes no accepted campaign or orders");
                AdapterCases.Set(first.Connector, "_securityName", "SIMPLE");
                AdapterCases.Call(robot, "Run", apply); AdapterCases.Call(robot, "Run", apply);
                Program.Check(!applied && messages.Count == 2 && messages[1].Contains("ещё не получено"), "Simple selected-but-unavailable metadata gets distinct deduplicated message");
                AdapterCases.Call(robot, "Run", (Action)(() => AdapterCases.Call(robot, "Start")));
                Program.Equal("Off", ((StrategyParameterString)parameters["Regime"]).ValueString, "Simple unavailable Start does not arm trading");
                Program.Equal(before, JsonSerializer.Serialize(adapter.Engine.Data), "Simple missing metadata leaves original checkpoint intact");
                ((StrategyParameterString)parameters["Regime"]).ValueString = "On";
                adapter.Quotes[0] = Program.Quotes(-0.00010m, -0.00009m, funds: 1000)[0];
                AdapterCases.Call(robot, "BeforeDecision");
                Program.Equal(before, JsonSerializer.Serialize(adapter.Engine.Data), "Simple initial quote without metadata waits without fault, reconciliation or submit");
                spy.Security = selected;
                if (liveHost)
                {
                    AdapterCases.Call(robot, "BeforeDecision");
                    Program.Equal("", adapter.Engine.Data.ActivePlan, "Simple metadata arrival never auto-starts live host");
                    AdapterCases.Call(robot, "Run", apply);
                    Program.Check(applied && adapter.Engine.Data.State == Futures2State.Ready, "Simple explicit Apply succeeds after metadata becomes available");
                }
                else
                {
                    AdapterCases.Call(robot, "BeforeDecision");
                    Program.Equal(Futures2State.Active, adapter.Engine.Data.State, "Simple armed simulation resumes preparation after metadata arrival");
                }
                Program.Equal(0, errors, "Simple absent instrument is not an Error stack trace");
                Program.Check(adapter.Engine.Data.ActivePlan.Length > 0 && adapter.Engine.Data.Book.Intents.Count == 0,
                    "Simple readiness preparation alone submits no orders");
                Program.Check(!((string)typeof(Futures2GridSimple).GetField("_preparationMessage", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(robot)).Any(),
                    "Simple successful preparation clears wait diagnostic");
            }
        }
        private static void Discovery()
        {
            Program.Check(BotFactory.GetIncludeNamesStrategy().Contains("Futures2GridSimple"), "Simple native factory discovers robot name");
            Program.Check(typeof(Futures2GridSimple).GetConstructor(new[] { typeof(string), typeof(StartProgram) }) != null, "Simple native constructor contract");
            Futures2Plan plan = Settings().Build(Security());
            Futures2Plan copy = Futures2Commands.Copy(plan); copy.Id = Guid.NewGuid().ToString("N");
            MethodInfo same = typeof(Futures2GridSimple).GetMethod("SamePlan", BindingFlags.Static | BindingFlags.NonPublic);
            Program.Check((bool)same.Invoke(null, new object[] { plan, copy }), "Simple repeated apply ignores only transient plan identity");
            copy.Levels[1].Volume++;
            Program.Check(!(bool)same.Invoke(null, new object[] { plan, copy }), "Simple apply detects volume-only override");
        }
    }
}
