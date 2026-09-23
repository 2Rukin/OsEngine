/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.Entity;
using OsEngine.OsData.OrderFlow;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace OsEngine.OrderFlowResearch.Tests
{
    internal static partial class Program
    {
        #region Causal fixtures

        private static List<string> StudyWarmup(DateTime start)
        {
            List<string> rows = new List<string>();
            for (int i = 0; i < 21; i++)
            {
                rows.Add(Row(start.AddMinutes(i), 100, 1, Side.Buy));
                rows.Add(Row(start.AddMinutes(i).AddSeconds(30), 102, 1, Side.Sell));
                rows.Add(Row(start.AddMinutes(i).AddSeconds(50), 100, 1, Side.Buy));
            }
            return rows;
        }

        private static OrderFlowResearchRequest StudyRequest(string root, IEnumerable<string> rows, bool singles = true)
        {
            OrderFlowResearchRequest request = CloudRequest(root, rows.ToArray());
            request.Cloud.MinimumTickVolume = 10; request.Cloud.SingleTicks = singles;
            return request;
        }

        private static OrderFlowCloudStatisticsSettings StudySettings(params int[] horizons) => new OrderFlowCloudStatisticsSettings {
            HorizonsMinutes = horizons.Length == 0 ? new[] { 3 } : horizons, MinimumSamples = 1 };

        private static OrderFlowCloudStatisticsResult Study(OrderFlowResearchRequest request, OrderFlowCloudStatisticsSettings settings = null)
        { return new OrderFlowCloudStatisticsEngine().Run(request, Replay(request), settings ?? StudySettings(), CancellationToken.None); }

        private static List<string> StudyOneEvent(params string[] future)
        {
            List<string> rows = StudyWarmup(Start);
            rows.Add(Row(Start.AddMinutes(21), 100, 10, Side.Buy)); rows.AddRange(future);
            return rows;
        }

        #endregion

        #region ATR and reaction boundaries

        private static void TestStudyAtrCausality(string root)
        {
            List<string> rows = StudyOneEvent(Row(Start.AddMinutes(21).AddSeconds(1), 10000, 1, Side.Buy), Row(Start.AddMinutes(24), 100, 1, Side.Buy));
            OrderFlowCloudStatisticsResult result = Study(StudyRequest(root, rows));
            OrderFlowCloudStudyEvent item = result.Events.Single();
            AssertEqual(2m, item.Atr, "Current minute and its future extreme cannot affect completed-minute ATR20");
            AssertEqual(Start, item.FeatureStart, "Twenty ranges need twenty-one observed bars including previous close");
            rows = StudyWarmup(Start).Take(60).ToList();
            rows.Add(Row(Start.AddMinutes(20), 100, 10, Side.Buy)); rows.Add(Row(Start.AddMinutes(23), 100, 1, Side.Buy));
            result = Study(StudyRequest(root, rows));
            AssertEqual(0, result.Events.Count, "Twenty bars supply only nineteen true ranges");
            AssertEqual(1, result.SkippedAtr, "Warm-up omission is counted");
            List<OrderFlowDisplayBar> bars = new List<OrderFlowDisplayBar> {
                new OrderFlowDisplayBar { High = 102, Low = 100, Close = 101 },
                new OrderFlowDisplayBar { High = 111, Low = 110, Close = 110 },
                new OrderFlowDisplayBar { High = 110, Low = 108, Close = 109 } };
            AssertEqual(6m, OrderFlowCloudStatisticsEngine.AtrSeries(bars, 2)[2], "Gap from previous observed close enters true range: (10+2)/2");
        }

        private static void TestStudyCompletionReference(string root)
        {
            List<string> rows = StudyWarmup(Start);
            rows.Add(Row(Start.AddMinutes(21), 100, 10, Side.Buy));
            rows.Add(Row(Start.AddMinutes(21).AddSeconds(2), 110, 10, Side.Sell));
            rows.Add(Row(Start.AddMinutes(21).AddSeconds(3), 107, 1, Side.Buy));
            rows.Add(Row(Start.AddMinutes(24).AddSeconds(2), 110, 1, Side.Buy));
            OrderFlowCloudStatisticsResult result = Study(StudyRequest(root, rows, false));
            OrderFlowCloudStudyEvent item = result.Events.Single();
            AssertEqual(100m, item.Cloud.Price, "Drawn anchor precedes completion");
            AssertEqual(110m, item.ReferencePrice, "Study reference is actual closing tick price");
            AssertEqual(Start.AddMinutes(21).AddSeconds(2), item.Time, "Horizons start at known completion");
            AssertEqual(OrderFlowCloudReactionOutcome.AdverseFirst, item.Reactions.Single(reaction => !reaction.Reversal).Outcome,
                "No retroactive target hit from earlier anchor");
            AssertEqual(1, result.SkippedUnfinished, "EOF chain excluded from completed-event study");
        }

        private static void TestStudySameTimeOrder(string root)
        {
            DateTime time = Start.AddMinutes(21);
            foreach (bool targetFirst in new[] { true, false })
            {
                OrderFlowCloudStatisticsResult result = Study(StudyRequest(root, StudyOneEvent(
                    Row(time, targetFirst ? 103 : 97, 1, Side.Buy), Row(time, targetFirst ? 97 : 103, 1, Side.Sell), Row(time.AddMinutes(3), 100, 1, Side.Buy))));
                OrderFlowCloudStudyEvent item = result.Events.Single();
                OrderFlowCloudReaction forward = item.Reactions.Single(reaction => !reaction.Reversal);
                AssertEqual(targetFirst ? OrderFlowCloudReactionOutcome.TargetFirst : OrderFlowCloudReactionOutcome.AdverseFirst,
                    forward.Outcome, "Physical row order resolves equal-time touches");
                AssertEqual(item.SourceSequence + 1, forward.FirstTouchSequence.Value, "Next equal-time row is future evidence");
                AssertEqual(0m, forward.FirstTouchSeconds.Value, "Subsequent physical row may have same timestamp");
                AssertEqual(1.5m, forward.MfeAtr, "MFE measures full horizon after first touch");
                AssertEqual(1.5m, forward.MaeAtr, "MAE measures full horizon after first touch");
                AssertEqual(targetFirst ? OrderFlowCloudReactionOutcome.AdverseFirst : OrderFlowCloudReactionOutcome.TargetFirst,
                    item.Reactions.Single(reaction => reaction.Reversal).Outcome, "Reversal is evaluated separately");
            }
        }

        private static void TestStudyHorizonCoverage(string root)
        {
            DateTime time = Start.AddMinutes(21);
            OrderFlowCloudStatisticsResult result = Study(StudyRequest(root, StudyOneEvent(Row(time.AddMinutes(3), 103, 1, Side.Buy))));
            AssertTrue(result.Events.Single().Complete, "EOF at exact horizon boundary is complete");
            AssertEqual(OrderFlowCloudReactionOutcome.TargetFirst, result.Events[0].Reactions[0].Outcome, "Boundary tick contributes target hit");
            result = Study(StudyRequest(root, StudyOneEvent(Row(time.AddMinutes(3).AddTicks(-10), 103, 1, Side.Buy))));
            AssertFalse(result.Events.Single().Complete, "EOF one microsecond before horizon is incomplete even after early target");
            AssertEqual(1, result.SkippedIncomplete, "Incomplete event excluded from recommendation denominator");
            result = Study(StudyRequest(root, StudyOneEvent(Row(time.AddMinutes(4), 103, 1, Side.Buy))));
            AssertTrue(result.Events[0].Reactions.All(reaction => reaction.IsComplete && reaction.FutureTrades == 0), "Later file coverage does not invent within-horizon trades");
            AssertFalse(result.Events[0].Complete, "No-future-trade event is not a timeout observation");
            result = Study(StudyRequest(root, StudyOneEvent(Row(time.AddMinutes(1), 100, 1, Side.Buy), Row(time.AddMinutes(3), 100, 1, Side.Buy))), StudySettings(1, 3, 9));
            AssertFalse(result.Events[0].Complete, "All horizons share one fully observed cohort");
        }

        private static void TestStudyNeutralAndUnfinished(string root)
        {
            List<string> rows = StudyWarmup(Start);
            rows.Add(Row(Start.AddMinutes(21), 100, 10, Side.Buy)); rows.Add(Row(Start.AddMinutes(21), 100, 10, Side.Sell));
            rows.Add(Row(Start.AddMinutes(21).AddSeconds(2), 105, 10, Side.Buy)); rows.Add(Row(Start.AddMinutes(24), 105, 1, Side.Buy));
            OrderFlowCloudStatisticsResult result = Study(StudyRequest(root, rows, false));
            AssertEqual(1, result.SkippedNeutral, "Neutral volume delta supplies no continuation sign");
            AssertEqual(1, result.SkippedUnfinished, "Unfinished chain cannot be used as completed live event");
            AssertEqual(0, result.Recommendations.Count, "No eligible evidence gives no recommendation");
        }

        #endregion

        #region Sampling, fit and chronological check

        private static void TestStudyExactThirds(string root)
        {
            List<string> rows = new List<string>();
            for (int day = 0; day < 6; day++)
            {
                DateTime start = Start.AddDays(day); decimal range = day + 1;
                for (int minute = 0; minute < 21; minute++)
                {
                    rows.Add(Row(start.AddMinutes(minute), 100, 1, Side.Buy));
                    rows.Add(Row(start.AddMinutes(minute).AddSeconds(30), 100 + range, 1, Side.Sell));
                    rows.Add(Row(start.AddMinutes(minute).AddSeconds(50), 100, 1, Side.Buy));
                }
                rows.Add(Row(start.AddMinutes(21), 100, 10, Side.Buy));
                rows.Add(Row(start.AddMinutes(24), 100 + range * 2, 1, Side.Buy));
            }
            OrderFlowCloudStatisticsResult result = Study(StudyRequest(root, rows));
            AssertTrue(result.Events.Where(item => item.Part == OrderFlowCloudSamplePart.Fit).Select(item => item.Atr)
                .SequenceEqual(new decimal[] { 1, 2, 3, 4 }), "Four distinct complete fit ATR values at exact thirds boundary");
            OrderFlowCloudRecommendation low = result.Recommendations.Single(row => row.Regime.StartsWith("Low") && !row.Reversal);
            OrderFlowCloudRecommendation middle = result.Recommendations.Single(row => row.Regime.StartsWith("Middle") && !row.Reversal);
            OrderFlowCloudRecommendation high = result.Recommendations.Single(row => row.Regime.StartsWith("High") && !row.Reversal);
            AssertEqual(2m, low.RegimeUpper.Value, "Exact floor((4-1)/3)=1 selects ATR2, without decimal-third rounding");
            AssertEqual(2, low.Fit.Count, "ATR1 and ATR2 belong to lower group");
            AssertEqual(2m, middle.RegimeLower.Value, "Middle starts above ATR2");
            AssertEqual(3m, middle.RegimeUpper.Value, "Exact upper third selects ATR3");
            AssertEqual(1, middle.Fit.Count, "ATR3 is the only middle fit observation");
            AssertEqual(1, high.Fit.Count, "ATR4 is the only upper fit observation");
        }

        private static void TestStudyFixedSampling(string root)
        {
            DateTime time = Start.AddMinutes(21);
            List<string> rows = StudyOneEvent(Row(time.AddMinutes(1), 100, 10, Side.Buy), Row(time.AddMinutes(3), 100, 10, Side.Buy),
                Row(time.AddMinutes(3).AddTicks(10), 100, 10, Side.Buy), Row(time.AddMinutes(7), 103, 1, Side.Buy));
            OrderFlowResearchRequest request = StudyRequest(root, rows);
            request.CalculateCloud2 = true; request.Cloud2.MinimumTickVolume = 10;
            OrderFlowCloudStatisticsResult result = Study(request);
            foreach (int layer in new[] { 1, 2 })
            {
                List<OrderFlowCloudStudyEvent> events = result.Events.Where(item => item.Layer == layer).ToList();
                AssertEqual(4, events.Count, "All causal events kept for visual selection");
                AssertTrue(events.Select(item => item.Sampled).SequenceEqual(new[] { true, false, false, true }),
                    "Pre-filter sample requires next start strictly after previous maximum horizon");
            }
            AssertEqual(4, result.SkippedOverlap, "Overlapping layer observations counted separately");
            AssertTrue(result.Recommendations.Select(row => row.Layer).Distinct().OrderBy(layer => layer).SequenceEqual(new[] { 1, 2 }), "No pooled two-layer recommendation");
        }

        private static void TestStudySplitPurging(string root)
        {
            DateTime midnight = Start.Date.AddDays(1);
            List<string> rows = StudyWarmup(midnight.AddMinutes(-23));
            rows.Add(Row(midnight.AddMinutes(-2), 100, 10, Side.Buy));
            rows.Add(Row(midnight.AddMinutes(1), 100, 10, Side.Buy));
            rows.AddRange(StudyWarmup(midnight.AddMinutes(4)));
            rows.Add(Row(midnight.AddMinutes(25), 100, 10, Side.Buy)); rows.Add(Row(midnight.AddMinutes(28), 103, 1, Side.Buy));
            OrderFlowCloudStatisticsSettings settings = StudySettings(); settings.FitPercent = 50;
            OrderFlowCloudStatisticsResult result = Study(StudyRequest(root, rows), settings);
            AssertEqual(midnight, result.SplitDate.Value, "Split uses observed calendar dates");
            AssertEqual(OrderFlowCloudSamplePart.Purged, result.Events[0].Part, "Fit label crossing split is purged");
            AssertEqual(OrderFlowCloudSamplePart.Purged, result.Events[1].Part, "Test event with ATR features before split is purged");
            AssertEqual(OrderFlowCloudSamplePart.Test, result.Events[2].Part, "Feature span fully on test side is eligible");
            AssertTrue(result.Events[2].FeatureStart >= midnight, "Earlier ATR reference close is included in purge span");
        }

        private static List<string> StudyDays(bool reverseCheck)
        {
            List<string> rows = new List<string>();
            for (int day = 0; day < 10; day++)
            {
                DateTime start = Start.AddDays(day); bool strong = day % 2 == 0;
                rows.AddRange(StudyWarmup(start)); rows.Add(Row(start.AddMinutes(21).AddSeconds(-1), 99, 1, Side.Sell));
                rows.Add(Row(start.AddMinutes(21), 100, strong ? 30 : 10, Side.Buy));
                bool rises = strong ^ (reverseCheck && day >= 7);
                rows.Add(Row(start.AddMinutes(21).AddSeconds(1), rises ? 110 : 90, 1, Side.Buy));
                rows.Add(Row(start.AddMinutes(24), 100, 1, Side.Buy));
            }
            return rows;
        }

        private static void TestStudyHeldOutIsolation(string root)
        {
            OrderFlowCloudStatisticsSettings settings = StudySettings(); settings.MinimumSamples = 2;
            OrderFlowCloudStatisticsResult original = Study(StudyRequest(root, StudyDays(false)), settings);
            OrderFlowCloudStatisticsResult changed = Study(StudyRequest(root, StudyDays(true)), settings);
            AssertEqual(Start.Date.AddDays(7), original.SplitDate.Value, "Seventy percent split over ten observed days");
            AssertEqual(original.Recommendations.Count, changed.Recommendations.Count, "Future outcomes do not change rule grid size");
            for (int i = 0; i < original.Recommendations.Count; i++)
            {
                OrderFlowCloudRecommendation a = original.Recommendations[i]; OrderFlowCloudRecommendation b = changed.Recommendations[i];
                AssertEqual(a.Parameters, b.Parameters, "Held-out outcomes cannot choose or retune rules");
                AssertEqual(a.RegimeLower, b.RegimeLower, "ATR quantile lower boundary uses fit only");
                AssertEqual(a.RegimeUpper, b.RegimeUpper, "ATR quantile upper boundary uses fit only");
                AssertEqual(JsonSerializer.Serialize(a.Fit), JsonSerializer.Serialize(b.Fit), "Fit metrics unchanged");
                AssertTrue(a.TriedRules <= 48, "Bounded declared one-coordinate grid");
            }
            OrderFlowCloudRecommendation forward = original.Recommendations.Single(row => row.Regime.StartsWith("All") && !row.Reversal);
            OrderFlowCloudRecommendation reversed = changed.Recommendations.Single(row => row.Regime.StartsWith("All") && !row.Reversal);
            AssertTrue(forward.SelectedImprovement, "Fixture actually selects a non-baseline fit rule");
            AssertFalse(forward.Test.WinPercent == reversed.Test.WinPercent, "Changed held-out outcomes remain visible in check only");
            AssertTrue(forward.Status.Contains("Мало наблюдений"), "One matching test event is explicitly insufficient");
        }

        private static void TestStudyDateAndSettings(string root)
        {
            List<string> rows = StudyWarmup(Start);
            rows.Add(Row(Start.AddDays(1), 100, 10, Side.Buy)); rows.Add(Row(Start.AddDays(1).AddMinutes(3), 103, 1, Side.Buy));
            OrderFlowResearchRequest request = StudyRequest(root, rows); request.FromDate = request.ToDate = Start.Date.AddDays(1);
            AssertEqual(1, Study(request).SkippedAtr, "Excluded dates cannot warm up ATR");
            OrderFlowCloudStatisticsSettings parameters = StudySettings(18, 3, 9, 3);
            OrderFlowCloudStatisticsSettings frozen = parameters.CopyValidated(); parameters.HorizonsMinutes[0] = 1;
            AssertTrue(frozen.HorizonsMinutes.SequenceEqual(new[] { 3, 9, 18 }), "Validated settings own a sorted distinct horizon array");
            frozen.AtrPeriod = 0; Expect<ArgumentException>(() => frozen.CopyValidated());
            frozen.AtrPeriod = 20; frozen.TargetAtr = 0; Expect<ArgumentException>(() => frozen.CopyValidated());
            frozen.TargetAtr = 1.5m; frozen.FitPercent = 100; Expect<ArgumentException>(() => frozen.CopyValidated());
            AssertTrue(OrderFlowCloudStudyMetrics.WilsonLower(10, 10) < 1 && OrderFlowCloudStudyMetrics.WilsonLower(10, 10) > 0,
                "Ten observed wins do not give a certain lower rate");
        }

        #endregion

        #region Identity, immutable publication and worker lifetime

        private static void TestStudyBarrierUnderflow(string root)
        {
            List<string> rows = new List<string>();
            for (int minute = 0; minute < 21; minute++)
            {
                rows.Add(Row(Start.AddMinutes(minute), 100, 1, Side.Buy));
                rows.Add(Row(Start.AddMinutes(minute).AddSeconds(30), 100.00001m, 1, Side.Sell));
                rows.Add(Row(Start.AddMinutes(minute).AddSeconds(50), 100, 1, Side.Buy));
            }
            rows.Add(Row(Start.AddMinutes(21), 100, 10, Side.Buy));
            rows.Add(Row(Start.AddMinutes(21).AddSeconds(1), 100, 1, Side.Buy));
            rows.Add(Row(Start.AddMinutes(24), 100, 1, Side.Buy));
            OrderFlowResearchRequest request = StudyRequest(root, rows); request.PriceStep = 0.00001m;
            OrderFlowResearchResult source = Replay(request);
            OrderFlowCloudStatisticsEngine engine = new OrderFlowCloudStatisticsEngine();
            OrderFlowCloudStatisticsResult normal = engine.Run(request, source, StudySettings(), CancellationToken.None);
            AssertEqual(0.00001m, normal.Events.Single().Atr, "Ordinary tiny price step supplies positive ATR");
            AssertTrue(normal.Events[0].Complete && normal.Events[0].Reactions.All(item => item.Outcome == OrderFlowCloudReactionOutcome.Timeout),
                "Stationary future with representable barriers is a full timeout");
            foreach (bool target in new[] { true, false })
            {
                OrderFlowCloudStatisticsSettings settings = StudySettings();
                if (target) { settings.TargetAtr = 0.0000000000000000000000000001m; }
                else { settings.AdverseAtr = 0.0000000000000000000000000001m; }
                Expect<InvalidDataException>(() => engine.Run(request, source, settings, CancellationToken.None));
                using OrderFlowCloudStatisticsJob job = new OrderFlowCloudStatisticsJob(request, source, settings);
                job.Start(); AssertTrue(SpinWait.SpinUntil(() => job.Finished, 10000), "Invalid-distance worker terminates");
                AssertTrue(job.Error is InvalidDataException && job.Result == null, "Worker refuses a zero-distance reaction before publication");
            }
            AssertFalse(Directory.Exists(request.OutputRootPath), "Underflow cannot publish a falsely successful study bundle");
        }

        private static void TestStudyIdentityAndCancellation(string root)
        {
            OrderFlowResearchRequest request = StudyRequest(root, StudyOneEvent(Row(Start.AddMinutes(24), 103, 1, Side.Buy)));
            OrderFlowResearchResult source = Replay(request); string before = JsonSerializer.Serialize(source);
            OrderFlowCloudStatisticsEngine engine = new OrderFlowCloudStatisticsEngine();
            OrderFlowCloudStatisticsResult result = engine.Run(request, source, StudySettings(), CancellationToken.None);
            AssertEqual(before, JsonSerializer.Serialize(source), "Statistics do not change raw Clouds, bars, qualified prefixes or hashes");
            OrderFlowCloudStatisticsSettings other = StudySettings(); other.TargetAtr = 2;
            AssertFalse(result.StudyHash == engine.Run(request, source, other, CancellationToken.None).StudyHash, "Method settings enter study identity");
            using CancellationTokenSource cancelled = new CancellationTokenSource(); cancelled.Cancel();
            Expect<OperationCanceledException>(() => engine.Run(request, source, StudySettings(), cancelled.Token));
            File.AppendAllText(request.TicksFilePath, "\r\n");
            Expect<InvalidDataException>(() => engine.Run(request, source, StudySettings(), CancellationToken.None));
            using (FileStream writable = new FileStream(request.TicksFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            { AssertTrue(writable.Length > 0, "Hash failure releases pinned input handle"); }
        }

        private static void TestStudyArtifactPublication(string root)
        {
            OrderFlowResearchRequest request = StudyRequest(root, StudyOneEvent(Row(Start.AddMinutes(24), 103, 1, Side.Buy)));
            OrderFlowResearchResult source = Export(request);
            Dictionary<string, string> hashes = Directory.GetFiles(source.ArtifactDirectory).ToDictionary(Path.GetFileName, StoredHash);
            OrderFlowCloudStatisticsResult result = new OrderFlowCloudStatisticsEngine().Run(request, source, StudySettings(), CancellationToken.None);
            result.ArtifactDirectory = OrderFlowCloudStatisticsArtifacts.Write(request.OutputRootPath, result, CancellationToken.None);
            AssertFalse(result.ArtifactDirectory == source.ArtifactDirectory, "Separate study bundle leaves raw calculation identity intact");
            AssertEqual(result.ArtifactDirectory, OrderFlowCloudStatisticsArtifacts.Write(request.OutputRootPath, result, CancellationToken.None), "Second publication is byte-identical despite assigned output path");
            string[] csv = File.ReadAllLines(Path.Combine(result.ArtifactDirectory, "reactions.csv"));
            AssertEqual(3, csv.Length, "Both directions exported for one horizon");
            AssertTrue(csv.Skip(1).All(row => row.Split(',').Length == csv[0].Split(',').Length), "CSV null first-touch fields retain width");
            foreach (KeyValuePair<string, string> pair in hashes)
            { AssertEqual(pair.Value, StoredHash(Path.Combine(source.ArtifactDirectory, pair.Key)), "Source bundle unchanged " + pair.Key); }
            File.AppendAllText(Path.Combine(result.ArtifactDirectory, "study.json"), "changed");
            Expect<InvalidOperationException>(() => OrderFlowCloudStatisticsArtifacts.Write(request.OutputRootPath, result, CancellationToken.None));
            AssertFalse(Directory.GetDirectories(request.OutputRootPath).Any(path => path.Contains(".staging-")), "Failed publication cleans only its staging");
            using CancellationTokenSource cancelled = new CancellationTokenSource(); cancelled.Cancel();
            string cancelledRoot = Path.Combine(root, "cancelled-output");
            Expect<OperationCanceledException>(() => OrderFlowCloudStatisticsArtifacts.Write(cancelledRoot, result, cancelled.Token));
            AssertFalse(Directory.Exists(cancelledRoot), "Pre-cancel publishes no files");
        }

        private static void TestStudyWorkerLifetime(string root)
        {
            OrderFlowResearchRequest request = StudyRequest(root, StudyOneEvent(Row(Start.AddMinutes(24), 103, 1, Side.Buy)));
            OrderFlowResearchResult source = Replay(request);
            using (OrderFlowCloudStatisticsJob job = new OrderFlowCloudStatisticsJob(request, source, StudySettings(), false))
            {
                job.Start(); AssertTrue(SpinWait.SpinUntil(() => job.Finished, 10000), "Worker finishes without UI join");
                AssertTrue(job.Result != null && job.Error == null && !job.Cancelled, "Completed worker publishes result");
                Expect<InvalidOperationException>(() => job.Start());
            }
            OrderFlowCloudStatisticsJob cancelled = new OrderFlowCloudStatisticsJob(request, source, StudySettings(), false);
            cancelled.Cancel(); cancelled.Start(); cancelled.Dispose();
            AssertTrue(SpinWait.SpinUntil(() => cancelled.Finished, 10000), "Disposed cancelled worker terminates");
            AssertTrue(cancelled.Cancelled && cancelled.Result == null && cancelled.Error == null, "Cancellation cannot publish a late result");
            cancelled.Dispose(); Expect<InvalidOperationException>(() => cancelled.Start());
            using OrderFlowCloudStatisticsJob failed = new OrderFlowCloudStatisticsJob(request, source, StudySettings(), false);
            File.AppendAllText(request.TicksFilePath, "\n"); failed.Start();
            AssertTrue(SpinWait.SpinUntil(() => failed.Finished, 10000), "Failed worker terminates");
            AssertTrue(failed.Error is InvalidDataException && failed.Result == null, "Worker surfaces input identity failure without partial recommendation");
        }

        #endregion
    }
}
