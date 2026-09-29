/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.OsData.OrderFlow.Context;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.IO;
using System.Threading;
using System.Diagnostics;
using System.Globalization;

namespace OsEngine.OrderFlowContext.Tests
{
    /// <summary>Portable offline tests of exact production core sources. No WPF, broker, credentials, orders or UI/live claims.</summary>
    internal static class Program
    {
        private static readonly DateTime Start = new DateTime(2026, 6, 22, 10, 0, 0);
        private static int _passed;
        private static int _failed;
        private static int Main(string[] args)
        {
            if (args.Length > 0) { return RealFile(args); }
            Run("midnight equality preserves both dates and closes strictly later", Midnight);
            Run("manual IDs remain unique and duplicate anchors rejected", ManualIdentity);
            Run("manual confirmation cannot cross session break", ManualSession);
            Run("price index agrees with independent path oracle", PathOracle);
            Run("real adapter determinism and tampered study rejection", Adapter);
            Run("cancelled save preserves destination", CancelSave);
            Run("statistics respects scale and direction", StatisticsFilter);
            Run("all horizon timestamp rows and ambiguous barrier", EqualDue);
            Run("gap ending at horizon is censored", OutcomeGap);
            Run("drawing cadence cannot change events or labels", SampleIndependence);
            Run("first cloud exceeds formation period", LongSeed);
            Run("own area coordinates survive newer area", OwnCoordinates);
            Run("fractional notional underflow is rejected", Precision);
            Run("VWAP/TWAP have different weights", Moments);
            Run("equal timestamps and excluded time", TimeWeights);
            Run("confirmation and immutable rectangle", Confirmation);
            Run("prefix recalculation equals recorded prefix", Prefixes);
            Run("causal parent outside source rectangle", Parent);
            Run("manual area cannot appear before knowledge", Manual);
            Run("future labels remain separate and censored", Labels);
            Run("no same-tick duplicate moments", NoDuplicate);
            Run("session boundaries do not fabricate close", Session);
            Run("budget fails explicitly", Limits);
            Run("invalid profile rejected", Validation);
            Console.WriteLine("Passed: " + _passed + "; failed: " + _failed);
            return _failed == 0 ? 0 : 1;
        }
        private static void Midnight()
        {
            FlowContextSettings settings = Settings(); settings.TargetTicks = settings.StopTicks = 10;
            DateTime midnight = Start.Date.AddDays(1);
            List<FlowContextOutcome> output = new List<FlowContextOutcome>(); FlowContextOutcomes paths = new FlowContextOutcomes(settings, output);
            FlowContextTick before = new FlowContextTick { Time = midnight.AddSeconds(-1), Sequence = 1, Price = 100, Volume = 1 };
            paths.Add(before, CancellationToken.None);
            paths.Schedule(new FlowContextEvent { Id = "old", Time = before.Time, Sequence = 1, Price = 100, Direction = 1 }, midnight, "EOD");
            paths.Add(new FlowContextTick { Time = midnight, Sequence = 2, Price = 120, Volume = 1 }, CancellationToken.None);
            paths.Schedule(new FlowContextEvent { Id = "new", Time = midnight, Sequence = 2, Price = 120, Direction = 1 }, midnight.AddSeconds(10), "10s");
            paths.Add(new FlowContextTick { Time = midnight, Sequence = 3, Price = 80, Volume = 1 }, CancellationToken.None);
            Check(output.All(value => value.KnownSequence == null), "equal midnight cannot finish outcome");
            paths.Add(new FlowContextTick { Time = midnight.AddSeconds(1), Sequence = 4, Price = 100, Volume = 1 }, CancellationToken.None);
            FlowContextOutcome old = output.Single(value => value.EventId == "old");
            Check(old.Status == "Complete" && old.KnownSequence == 4 && old.ChangeTicks == -20 && old.MfeTicks == 20 && old.MaeTicks == 20 && old.Barrier == "AmbiguousSameTimestamp", "midnight path includes every equality row");
            paths.Add(new FlowContextTick { Time = midnight.AddSeconds(11), Sequence = 5, Price = 150, Volume = 1 }, CancellationToken.None); paths.Complete();
            FlowContextOutcome next = output.Single(value => value.EventId == "new");
            Check(next.Status == "Complete" && next.KnownSequence == 5 && next.ChangeTicks == -20 && next.MaeTicks == 40, "new-date event and equal rows retained");
            output = new List<FlowContextOutcome>(); paths = new FlowContextOutcomes(settings, output); paths.Add(before, CancellationToken.None);
            paths.Schedule(new FlowContextEvent { Id = "eof", Time = before.Time, Sequence = 1, Price = 100, Direction = 1 }, midnight, "EOD");
            paths.Add(new FlowContextTick { Time = midnight, Sequence = 2, Price = 120, Volume = 1 }, CancellationToken.None); paths.Complete();
            Check(output.Single().Status == "Incomplete" && output.Single().KnownSequence == null && output.Single().MfeTicks == 20, "midnight EOF remains incomplete");
        }
        private static void ManualIdentity()
        {
            FlowContextSettings settings = Settings();
            settings.ManualAnchors.Add(new FlowManualAnchor { Start = Start, KnownAt = Start.AddSeconds(2), Low = 99, High = 101 });
            settings.ManualAnchors.Add(new FlowManualAnchor { Start = Start.AddMilliseconds(100), KnownAt = Start.AddSeconds(2).AddMilliseconds(100), Low = 98, High = 102 });
            FlowContextEngine engine = new FlowContextEngine(settings, Array.Empty<FlowContextSeed>());
            engine.Add(Tick(1, 1)); engine.Add(Tick(2, 3)); engine.Complete();
            Check(engine.Result.Regions.Count == 2 && engine.Result.Regions.Select(value => value.Id).Distinct().Count() == 2, "different anchors mapping to same raw sequences need unique IDs");
            string hash = new string('a', 64); engine.Result.SourceHash = hash;
            FlowContextRun run = new FlowContextRun { Data = engine.Result, Prices = new OsEngine.OsData.OrderFlow.OrderFlowResearchResult { InputHash = hash } };
            run.PayloadHash = FlowContextRunner.PayloadHash(run);
            FlowContextRunner.ValidateSaved(JsonSerializer.Deserialize<FlowContextRun>(JsonSerializer.Serialize(run)));
            settings.ManualAnchors.Add(settings.ManualAnchors[0]); bool rejected = false;
            try { settings.Validate(); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "repeated exact anchor rejected");
        }
        private static void ManualSession()
        {
            FlowContextSettings settings = Settings(); settings.Sessions = new List<FlowContextSession> { new FlowContextSession { StartMinute = 600, EndMinute = 660 }, new FlowContextSession { StartMinute = 690, EndMinute = 750 } };
            settings.ManualAnchors.Add(new FlowManualAnchor { Start = Start.AddMinutes(58), KnownAt = Start.AddMinutes(59), Low = 99, High = 101 });
            FlowContextEngine engine = new FlowContextEngine(settings, Array.Empty<FlowContextSeed>());
            engine.Add(Tick(1, 3510)); engine.Add(Tick(2, 5400, 200)); engine.Complete();
            Check(engine.Result.Regions.Count == 0, "unfinished manual cannot confirm in a later session");
        }
        private static void PathOracle()
        {
            Random random = new Random(1979); FlowContextSettings settings = Settings(); settings.TargetTicks = 12; settings.StopTicks = 9;
            List<FlowContextTick> ticks = new List<FlowContextTick>(); int seconds = 0;
            for (int sequence = 1; sequence <= 400; sequence++) { seconds += random.Next(0, 3); ticks.Add(Tick(sequence, seconds, 100 + random.Next(-20, 21))); }
            List<FlowContextOutcome> actual = new List<FlowContextOutcome>(); FlowContextOutcomes paths = new FlowContextOutcomes(settings, actual);
            Dictionary<string, FlowContextEvent> events = new Dictionary<string, FlowContextEvent>();
            foreach (FlowContextTick tick in ticks)
            {
                paths.Add(tick, CancellationToken.None);
                if (tick.Sequence % 7 != 0) { continue; }
                FlowContextEvent item = new FlowContextEvent { Id = tick.Sequence.ToString(), Sequence = tick.Sequence, Time = tick.Time, Price = tick.Price, Direction = tick.Sequence % 2 == 0 ? 1 : -1 };
                events.Add(item.Id, item); paths.Schedule(item, tick.Time.AddSeconds(35), "35s");
            }
            paths.Complete();
            foreach (FlowContextOutcome result in actual)
            {
                FlowContextEvent item = events[result.EventId]; FlowContextTick[] future = ticks.Where(tick => tick.Sequence > item.Sequence && tick.Time <= result.Due).ToArray();
                decimal[] changes = future.Select(tick => item.Direction * (tick.Price - item.Price) / settings.PriceStep).ToArray();
                FlowContextTick known = ticks.FirstOrDefault(tick => tick.Time > result.Due);
                Near(changes.Length == 0 ? 0 : Math.Max(0, changes.Max()), result.MfeTicks, "oracle MFE");
                Near(changes.Length == 0 ? 0 : Math.Max(0, -changes.Min()), result.MaeTicks, "oracle MAE");
                Check(result.Status == (known == null ? "Incomplete" : "Complete") && result.KnownSequence == known?.Sequence, "oracle closing knowledge");
                if (known != null) { Near(changes.Length == 0 ? 0 : changes.Last(), result.ChangeTicks.Value, "oracle final change"); }
                FlowContextTick target = future.FirstOrDefault(tick => item.Direction * (tick.Price - item.Price) >= settings.TargetTicks);
                FlowContextTick stop = future.FirstOrDefault(tick => item.Direction * (tick.Price - item.Price) <= -settings.StopTicks);
                string barrier = target == null && stop == null ? "Neither" : target == null ? "StopFirst" : stop == null ? "TargetFirst" : target.Time == stop.Time ? "AmbiguousSameTimestamp" : target.Sequence < stop.Sequence ? "TargetFirst" : "StopFirst";
                Check(result.Barrier == barrier, "oracle first barrier");
            }
        }
        private static void Adapter()
        {
            string root = Path.Combine(Path.GetTempPath(), "context-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try
            {
                string path = Path.Combine(root, "ticks.txt");
                File.WriteAllLines(path, Enumerable.Range(0, 180).Select(i => Start.AddSeconds(i).ToString("yyyyMMdd,HHmmss", CultureInfo.InvariantCulture) + "," + (100 + i % 7).ToString(CultureInfo.InvariantCulture) + ",10," + (i % 2 == 0 ? "Buy" : "Sell") + ",0," + i));
                FlowContextSettings settings = Settings(); foreach (FlowContextScale scale in settings.Scales) { scale.CloudVolume = 10; }
                FlowContextRun first = FlowContextRunner.Run(path, Start.Date, Start.Date, settings, CancellationToken.None), second = FlowContextRunner.Run(path, Start.Date, Start.Date, settings, CancellationToken.None);
                Check(first.PayloadHash == second.PayloadHash && first.Data.TickCount == 180 && first.Data.Regions.Count > 0, "adapter determinism");
                FlowContextRunner.Save(Path.Combine(root, "study.json"), first);
                FlowContextRun restored = JsonSerializer.Deserialize<FlowContextRun>(File.ReadAllText(Path.Combine(root, "study.json"))); FlowContextRunner.ValidateSaved(restored);
                restored.Data.Events[0].Price += 1; bool rejected = false; try { FlowContextRunner.ValidateSaved(restored); } catch (InvalidDataException) { rejected = true; } Check(rejected, "tamper rejected");
                using CancellationTokenSource cancellation = new CancellationTokenSource(); cancellation.Cancel(); rejected = false;
                try { FlowContextRunner.Run(path, Start.Date, Start.Date, settings, cancellation.Token); } catch (OperationCanceledException) { rejected = true; } Check(rejected, "runner cancels before publishing");
            }
            finally { Directory.Delete(root, true); }
        }
        private static void CancelSave()
        {
            string root = Path.Combine(Path.GetTempPath(), "context-save-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try
            {
                string path = Path.Combine(root, "old.json"); File.WriteAllText(path, "previous"); using CancellationTokenSource cancellation = new CancellationTokenSource(); cancellation.Cancel();
                bool cancelled = false; try { FlowContextRunner.Save(path, Settings(), cancellation.Token); } catch (OperationCanceledException) { cancelled = true; }
                Check(cancelled && File.ReadAllText(path) == "previous" && Directory.GetFiles(root).Length == 1, "cancel must preserve destination and remove temp");
            }
            finally { Directory.Delete(root, true); }
        }
        private static void StatisticsFilter()
        {
            FlowContextResult result = new FlowContextResult();
            result.Events.Add(new FlowContextEvent { Id = "local", Kind = "event", Time = Start, Direction = 1, Coordinates = new List<FlowContextCoordinate> { new FlowContextCoordinate { Scale = 0, RegionId = "L" } } });
            result.Events.Add(new FlowContextEvent { Id = "senior", Kind = "event", Time = Start, Direction = -1, Coordinates = new List<FlowContextCoordinate> { new FlowContextCoordinate { Scale = 2, RegionId = "S" } } });
            result.Outcomes.Add(new FlowContextOutcome { EventId = "local", Horizon = "10s", Status = "Complete", ChangeTicks = 1 });
            result.Outcomes.Add(new FlowContextOutcome { EventId = "senior", Horizon = "10s", Status = "Complete", ChangeTicks = 100 });
            FlowContextStatisticsRow row = FlowContextPresentation.Statistics(result, new FlowContextView { Scale = 0 }).Single();
            Check(row.СреднееШагов == 1 && row.Направление == "Вверх", "scale filter");
        }
        private static int RealFile(string[] args)
        {
            if (args.Length < 4 || args[0] != "--input") { Console.Error.WriteLine("--input TXT YYYY-MM-DD YYYY-MM-DD [study.json]"); return 2; }
            try
            {
                Stopwatch watch = Stopwatch.StartNew();
                FlowContextSettings settings = new FlowContextSettings();
                // Example source uses MOEX daytime/evening executions; calendar is explicit, never inferred as exchange truth.
                settings.Sessions = new List<FlowContextSession> { new FlowContextSession { StartMinute = 540, EndMinute = 1430 } };
                FlowContextRun run = FlowContextRunner.Run(args[1], DateTime.ParseExact(args[2], "yyyy-MM-dd", CultureInfo.InvariantCulture), DateTime.ParseExact(args[3], "yyyy-MM-dd", CultureInfo.InvariantCulture), settings, CancellationToken.None,
                    message => { if (watch.Elapsed.TotalSeconds > 2) { Console.WriteLine(message); } });
                FlowContextRunner.ValidateSaved(run);
                string path = args.Length > 4 ? args[4] : Path.Combine(Path.GetTempPath(), "context-" + Guid.NewGuid().ToString("N") + ".json");
                FlowContextRunner.Save(path, run);
                using (FileStream stream = File.OpenRead(path)) { FlowContextRunner.ValidateSaved(JsonSerializer.Deserialize<FlowContextRun>(stream)); }
                if (args.Length <= 4) { File.Delete(path); }
                Console.WriteLine("PASS real parser/detectors/core/persistence round-trip; ticks=" + run.Data.TickCount + "; regions=" + run.Data.Regions.Count + "; events=" + run.Data.Events.Count + "; outcomes=" + run.Data.Outcomes.Count + "; seconds=" + watch.Elapsed.TotalSeconds.ToString("0.0"));
                Console.WriteLine("source=" + run.Data.SourceHash + "; payload=" + run.PayloadHash);
                foreach (IGrouping<string, FlowContextOutcome> group in run.Data.Outcomes.GroupBy(value => value.Status)) { Console.WriteLine(group.Key + "=" + group.Count()); }
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
        private static FlowContextOutcome Outcome(FlowContextTick[] ticks, int gap = 60)
        {
            FlowContextSettings settings = Settings(); settings.MaximumGapSeconds = gap; settings.TargetTicks = 10; settings.StopTicks = 10;
            List<FlowContextOutcome> output = new List<FlowContextOutcome>(); FlowContextOutcomes outcomes = new FlowContextOutcomes(settings, output);
            outcomes.Add(ticks[0], CancellationToken.None);
            outcomes.Schedule(new FlowContextEvent { Id = "test", Sequence = ticks[0].Sequence, Time = ticks[0].Time, Price = ticks[0].Price, Direction = 1 }, Start.AddSeconds(10), "10s");
            foreach (FlowContextTick tick in ticks.Skip(1)) { outcomes.Add(tick, CancellationToken.None); }
            outcomes.Complete(); return output.Single();
        }
        private static void EqualDue()
        {
            FlowContextOutcome outcome = Outcome(new[] { Tick(1, 0), Tick(2, 10, 120), Tick(3, 10, 80), Tick(4, 11, 100) });
            Check(outcome.Status == "Complete" && outcome.KnownSequence == 4, "strictly later knowledge"); Near(-20, outcome.ChangeTicks.Value, "last equal-time row");
            Near(20, outcome.MfeTicks, "MFE"); Near(20, outcome.MaeTicks, "MAE"); Check(outcome.Barrier == "AmbiguousSameTimestamp", "same-time target/stop");
            Check(Outcome(new[] { Tick(1, 0), Tick(2, 10, 120) }).Status == "Incomplete", "EOF cannot close equal timestamp");
        }
        private static void OutcomeGap()
        {
            Check(Outcome(new[] { Tick(1, 0), Tick(2, 10, 110), Tick(3, 11) }, 5).Status == "DataGap", "gap ends exactly Due");
            Check(Outcome(new[] { Tick(1, 0), Tick(2, 4), Tick(3, 12) }, 5).Status == "DataGap", "gap straddles Due");
        }
        private static void SampleIndependence()
        {
            FlowContextSettings a = Settings(), b = Settings(); b.SampleSeconds = 120;
            FlowContextEngine first = new FlowContextEngine(a, new[] { Seed() }), second = new FlowContextEngine(b, new[] { Seed() });
            for (int i = 1; i <= 200; i++) { FlowContextTick tick = Tick(i, i - 1, 100 + i % 10); first.Add(tick); second.Add(tick); }
            first.Complete(); second.Complete();
            Check(JsonSerializer.Serialize(first.Result.Events) == JsonSerializer.Serialize(second.Result.Events), "display cadence affects events");
            Check(JsonSerializer.Serialize(first.Result.Outcomes) == JsonSerializer.Serialize(second.Result.Outcomes), "display cadence affects labels");
        }
        private static void LongSeed()
        {
            FlowContextSettings settings = Settings(); settings.Scales[0].FormationSeconds = 1;
            FlowContextEngine engine = new FlowContextEngine(settings, new[] { Seed() });
            engine.Add(Tick(1, 0)); engine.Add(Tick(2, 1)); engine.Add(Tick(3, 2)); Check(engine.Result.Regions.Count == 0, "first cloud duration");
        }
        private static void OwnCoordinates()
        {
            FlowContextEngine engine = new FlowContextEngine(Settings(), new[] { Seed(0, 1, 2), Seed(0, 3, 4, 110, 110) });
            for (int i = 1; i <= 6; i++) { engine.Add(Tick(i, i - 1, i < 3 ? 100 : 110)); }
            foreach (FlowContextEvent item in engine.Result.Events.Where(value => value.RegionId != null)) { Check(item.Coordinates.Any(value => value.RegionId == item.RegionId), "own coordinate missing"); }
        }
        private static void Precision()
        {
            bool failed = false; try { new FlowContextMoments().Add(Tick(1, 0, .000000000000001m, .000000000000001m), Settings()); } catch (InvalidDataException) { failed = true; }
            Check(failed, "tiny raw product rounded silently");
        }
        private static void Run(string name, Action test)
        { try { test(); _passed++; Console.WriteLine("PASS " + name); } catch (Exception error) { _failed++; Console.Error.WriteLine("FAIL " + name + ": " + error); } }
        private static void Check(bool condition, string message) { if (!condition) { throw new Exception(message); } }
        private static void Near(decimal expected, decimal actual, string name) => Check(Math.Abs(expected - actual) < .00001m, name + ": " + actual + " != " + expected);
        private static FlowContextSettings Settings()
        {
            FlowContextSettings settings = new FlowContextSettings { WarmupSeconds = 0, MaximumGapSeconds = 3600, HorizonsSeconds = new List<int> { 10 }, EndOfDay = false, SampleSeconds = 1 };
            foreach (FlowContextScale scale in settings.Scales) { scale.MinimumClouds = 1; scale.RegionVolume = 1; scale.SwingTicks = 2; scale.HoldSeconds = 0; }
            return settings;
        }
        private static FlowContextTick Tick(int sequence, int seconds, decimal price = 100, decimal volume = 1, bool buy = true) => new FlowContextTick { Sequence = sequence, Time = Start.AddSeconds(seconds), Price = price, Volume = volume, Buy = buy };
        private static FlowContextSeed Seed(int scale = 0, int first = 1, int known = 3, decimal low = 100, decimal high = 100) => new FlowContextSeed { Scale = scale, FirstSequence = first, KnownSequence = known, Start = Start.AddSeconds(first - 1), End = Start.AddSeconds(known - 2), KnownAt = Start.AddSeconds(known - 1), Low = low, High = high, Volume = 10, Notional = 1000 };
        private static void Moments()
        {
            FlowContextSettings settings = Settings(); FlowContextMoments moments = new FlowContextMoments();
            moments.Add(Tick(1, 0, 100, 90), settings); moments.Add(Tick(2, 600, 110, 10), settings); moments.Add(Tick(3, 3600, 110, 1), settings);
            Near(10210m / 101, moments.Mean, "VWAP"); Near(108.333333333m, moments.Twap.Value, "TWAP");
            decimal expectedVariance = (90 * (100 - moments.Mean) * (100 - moments.Mean) + 11 * (110 - moments.Mean) * (110 - moments.Mean)) / 101;
            Near((decimal)Math.Sqrt((double)expectedVariance), moments.Sigma, "population sigma");
        }
        private static void TimeWeights()
        {
            FlowContextSettings settings = Settings(); settings.MaximumGapSeconds = 60;
            FlowContextMoments moments = new FlowContextMoments(); moments.Add(Tick(1, 0, 100), settings); moments.Add(Tick(2, 0, 110), settings);
            Check(!moments.Twap.HasValue, "no invented equal-time duration"); moments.Add(Tick(3, 10, 120), settings); Near(110, moments.Twap.Value, "last price integral");
            moments.Add(Tick(4, 1000, 130), settings); Near(110, moments.Twap.Value, "entire long gap excluded");
        }
        private static void Confirmation()
        {
            FlowContextEngine engine = new FlowContextEngine(Settings(), new[] { Seed() });
            engine.Add(Tick(1, 0)); engine.Add(Tick(2, 1)); Check(engine.Result.Regions.Count == 0, "no future seed"); engine.Add(Tick(3, 2, 102));
            FlowContextRegion region = engine.Result.Regions.Single(); Check(region.KnownSequence == 3 && region.Low == 100 && region.High == 100, "frozen geometry");
            engine.Add(Tick(4, 3, 150)); Check(region.High == 100, "no growing source rectangle"); Near(113, region.Points.Last().Vwap, "all raw trades include breaking tick");
        }
        private static void Prefixes()
        {
            FlowContextSeed[] seeds = { Seed(), Seed(1, 2, 5) }; FlowContextTick[] ticks = Enumerable.Range(1, 8).Select(index => Tick(index, index - 1, 100 + index)).ToArray();
            FlowContextEngine full = new FlowContextEngine(Settings(), seeds); foreach (FlowContextTick tick in ticks) { full.Add(tick); }
            for (int length = 1; length <= ticks.Length; length++)
            {
                FlowContextEngine prefix = new FlowContextEngine(Settings(), seeds.Where(seed => seed.KnownSequence <= length));
                foreach (FlowContextTick tick in ticks.Take(length)) { prefix.Add(tick); }
                string actual = JsonSerializer.Serialize(prefix.Result.Events);
                string expected = JsonSerializer.Serialize(full.Result.Events.Where(item => item.Sequence <= length));
                Check(actual == expected, "events differ at prefix " + length);
                foreach (FlowContextRegion region in prefix.Result.Regions)
                {
                    FlowContextRegion original = full.Result.Regions.Single(item => item.Id == region.Id);
                    Check(JsonSerializer.Serialize(region.Points) == JsonSerializer.Serialize(original.Points.Where(point => point.Sequence <= length)), "line prefix differs");
                }
            }
        }
        private static void Parent()
        {
            FlowContextEngine engine = new FlowContextEngine(Settings(), new[] { Seed(2, 1, 2), Seed(0, 3, 4, 110, 111) });
            foreach (FlowContextTick tick in new[] { Tick(1, 0), Tick(2, 1), Tick(3, 2, 110), Tick(4, 3, 111) }) { engine.Add(tick); }
            Check(engine.Result.Regions[1].ParentId == engine.Result.Regions[0].Id, "outside pullback retains temporal parent");
        }
        private static void Manual()
        {
            FlowContextSettings settings = Settings(); settings.ManualAnchors.Add(new FlowManualAnchor { Start = Start, KnownAt = Start.AddSeconds(2), Low = 99, High = 101 });
            FlowContextEngine engine = new FlowContextEngine(settings, Array.Empty<FlowContextSeed>()); engine.Add(Tick(1, 0)); engine.Add(Tick(2, 1)); Check(engine.Result.Regions.Count == 0, "manual confirmation");
            engine.Add(Tick(3, 2)); Check(engine.Result.Regions.Single().Manual, "manual region published"); Near(3, engine.Result.Regions.Single().Points.Last().Volume, "raw manual volume");
        }
        private static void Labels()
        {
            FlowContextEngine engine = new FlowContextEngine(Settings(), new[] { Seed() });
            for (int index = 1; index <= 7; index++) { engine.Add(Tick(index, index - 1, index < 4 ? 100 : 110)); }
            engine.Complete(); Check(engine.Result.Outcomes.Count > 0, "path labels exist"); Check(engine.Result.Outcomes.All(item => item.Status == "Incomplete"), "EOF does not satisfy horizon");
        }
        private static void NoDuplicate()
        {
            FlowContextEngine engine = new FlowContextEngine(Settings(), new[] { Seed() }); engine.Add(Tick(1, 0, 100, 2)); engine.Add(Tick(2, 1, 100, 3)); engine.Add(Tick(3, 2, 110, 5));
            Near(10, engine.Result.Regions.Single().Points.Single().Volume, "confirmation tick exactly once"); Near(105, engine.Result.Regions.Single().InitialMean, "source mean");
        }
        private static void Session()
        {
            FlowContextSettings settings = Settings(); settings.Sessions = new List<FlowContextSession> { new FlowContextSession { StartMinute = 600, EndMinute = 601 } };
            FlowContextEngine engine = new FlowContextEngine(settings, new[] { Seed() }); engine.Add(Tick(1, 0)); engine.Add(Tick(2, 1)); engine.Add(Tick(3, 2)); engine.Add(Tick(4, 70, 200));
            Check(engine.Result.Regions.Single().EndReason == "SessionEnd", "session ended"); Near(100, engine.Result.Regions.Single().Points.Last().Vwap, "outside price excluded");
        }
        private static void Limits()
        {
            FlowContextSettings settings = Settings(); settings.MaximumRegions = 1; FlowContextEngine engine = new FlowContextEngine(settings, new[] { Seed(), Seed(1, 2, 4) });
            bool thrown = false; try { for (int index = 1; index <= 4; index++) { engine.Add(Tick(index, index - 1)); } } catch (System.IO.InvalidDataException) { thrown = true; }
            Check(thrown, "budget failure visible");
        }
        private static void Validation()
        {
            FlowContextSettings settings = Settings(); settings.SameSizeShare = 2; bool thrown = false; try { settings.Validate(); } catch (ArgumentException) { thrown = true; } Check(thrown, "share validation");
        }
    }
}
