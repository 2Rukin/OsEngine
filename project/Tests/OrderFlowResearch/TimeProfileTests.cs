/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.Entity;
using OsEngine.Indicators;
using OsEngine.OsData.OrderFlow;
using OsEngine.OsData.OrderFlow.Calibration;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OsEngine.OrderFlowResearch.Tests
{
    internal static partial class Program
    {
        /// <summary>Offline source-clock, independent-instance and adapter regressions. Synthetic temp files only; no application, connector or orders.</summary>
        private static void RegisterTimeProfiles(string root)
        {
            Run("TimeProfilesInclusiveMinutesAndOvernight", root, TestTimeProfileBoundaries);
            Run("TimeProfilesRejectOverlapAndUnsupportedMetrics", root, TestTimeProfileValidation);
            Run("TimeProfilesOccurrenceAndDisabledParity", root, TestTimeProfileCursor);
            Run("TimeProfilesCloudBoundariesPreserveChains", root, TestTimeProfileCloudBoundaries);
            Run("TimeProfilesDeltaResetAndBucketTiming", root, TestTimeProfileDelta);
            Run("TimeProfilesCloudMetricQualification", root, TestTimeProfileMetrics);
            Run("TimeProfilesScheduledDiagonalThresholds", root, TestTimeProfileDiagonal);
            Run("TimeProfilesFortyIndependentClouds", root, TestTimeProfileInstances);
            Run("TimeProfilesReplayPrefixAndIsolation", root, TestTimeProfileReplay);
            Run("TimeProfilesWorkspaceAndArtifacts", root, TestTimeProfilePersistence);
            Run("TimeProfilesNativeIndicatorMetricsAndModes", root, TestTimeProfileIndicator);
            Run("TimeProfilesNativeCumulativeResetAndDisable", root, TestTimeProfileCumulativeIndicator);
            Run("TimeProfilesChartHitVisibilityAndPostfilter", root, TestTimeProfileChart);
            Run("TimeProfilesUnshownEditorsAndMainControls", root, TestTimeProfileUi);
            Run("TimeProfilesMainRequestDiagonalIntegration", root, TestTimeProfileMainRequest);
        }

        private static ThresholdTimePeriod Period(int from, int to, params (string Key, decimal Value)[] values) => new ThresholdTimePeriod
        { FromMinute = from, ToMinute = to, Values = values.ToImmutableDictionary(p => p.Key, p => p.Value) };
        private static ThresholdTimeProfiles Schedule(bool reset, bool outside, params ThresholdTimePeriod[] periods) => new ThresholdTimeProfiles
        { Enabled = true, ResetOnStart = reset, UseBaseOutside = outside, Periods = periods.ToImmutableArray() };

        private static void TestTimeProfileBoundaries(string root)
        {
            ThresholdTimeProfiles profiles = Schedule(true, false, Period(600, 659, ("MinimumTickVolume", 10)));
            profiles.Validate(OrderFlowTimeThresholds.Cloud); ThresholdTimeCursor cursor = new ThresholdTimeCursor(profiles);
            AssertFalse(cursor.Select(Start.AddTicks(-1)).Active, "Before inclusive start");
            AssertTrue(cursor.Select(Start).Active, "Exact start");
            AssertTrue(cursor.Select(Start.AddHours(1).AddTicks(-1)).Active, "Whole ending minute including fractional ticks");
            AssertFalse(cursor.Select(Start.AddHours(1)).Active, "Next minute excluded");
            ThresholdTimeCursor overnight = new ThresholdTimeCursor(Schedule(true, false, Period(1380, 60)));
            AssertTrue(overnight.Select(Start.Date.AddHours(23)).Reset, "Night start");
            AssertFalse(overnight.Select(Start.Date.AddDays(1).AddMinutes(60)).Reset, "No midnight reset inside one overnight occurrence");
            AssertFalse(overnight.Select(Start.Date.AddDays(1).AddMinutes(61)).Active, "End minute followed by inactive minute");
            AssertTrue(new ThresholdTimeCursor(Schedule(false, false, Period(600, 600))).Select(Start.AddSeconds(59)).Active, "Equal bounds mean one minute");
            AssertEqual(1439, ThresholdTimeProfiles.ParseMinute("23:59"), "Clock parsing");
            Expect<ArgumentException>(() => ThresholdTimeProfiles.ParseMinute("24:00"));
        }
        private static void TestTimeProfileValidation(string root)
        {
            Expect<ArgumentException>(() => Schedule(true, true, Period(600, 660), Period(660, 720)).Validate(OrderFlowTimeThresholds.Cloud));
            Expect<ArgumentException>(() => Schedule(true, true, Period(1380, 60), Period(0, 30)).Validate(OrderFlowTimeThresholds.Cloud));
            Expect<ArgumentException>(() => Schedule(true, true, Period(600, 600, ("OpenInterest", 1))).Validate(OrderFlowTimeThresholds.Cloud));
            Expect<ArgumentException>(() => Schedule(true, true, Period(600, 600, ("MinimumTradeCount", 1.5m))).Validate(OrderFlowTimeThresholds.Cloud));
            ThresholdTimeProfiles left = Schedule(true, true, Period(600, 659, ("MinimumTickVolume", 10.0m)), Period(660, 719));
            ThresholdTimeProfiles right = Schedule(true, true, Period(660, 719), Period(600, 659, ("MinimumTickVolume", 10.00m)));
            AssertEqual(left.CanonicalValue(), right.CanonicalValue(), "Row order and decimal scale are not semantic");
            AssertEqual(left.CanonicalValue(), ThresholdTimeProfiles.Decode(left.Encode()).CanonicalValue(), "Scalar parameter round trip");
            AssertEqual(left.Encode(), right.Encode(), "Scalar encoding also canonicalizes row order and decimal scale");
            Expect<FormatException>(() => ThresholdTimeProfiles.Decode("broken"));
            AssertEqual(0.125m, ThresholdTimeProfiles.ParseThreshold("0,125"), "Comma decimal accepted exactly");
            Expect<FormatException>(() => ThresholdTimeProfiles.ParseThreshold("invalid"));
            Expect<FormatException>(() => ThresholdTimeProfiles.ParseThreshold("INVALID-E"));
            Expect<FormatException>(() => ThresholdTimeProfiles.ParseThreshold("1e-29"));
            Expect<FormatException>(() => ThresholdTimeProfiles.ParseThreshold("0.12345678901234567890123456789"));
            AssertEqual(0.0000000000000000000000000001m, ThresholdTimeProfiles.ParseThreshold("1e-28"), "Smallest positive decimal is exact");
            AssertEqual(decimal.MaxValue, ThresholdTimeProfiles.ParseThreshold(decimal.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture)), "Full decimal integer range retained");
        }
        private static void TestTimeProfileCursor(string root)
        {
            ThresholdTimeCursor cursor = new ThresholdTimeCursor(Schedule(true, true, Period(600, 600), Period(601, 601)));
            AssertTrue(cursor.Select(Start).Reset, "First interval"); AssertFalse(cursor.Select(Start).Reset, "Duplicate timestamp no double reset");
            AssertTrue(cursor.Select(Start.AddMinutes(1)).Reset, "Adjacent inclusive minutes switch");
            AssertFalse(cursor.Select(Start.AddMinutes(2)).Reset, "Base outside does not erase state");
            AssertTrue(cursor.Select(Start.AddDays(2)).Reset, "Unobserved days still create a new occurrence");
            ThresholdTimeProfiles disabled = new ThresholdTimeProfiles();
            AssertEqual("", disabled.CanonicalValue(), "Disabled has no legacy identity suffix");
            AssertTrue(new ThresholdTimeCursor(disabled).Select(Start).Active, "Disabled retains base behavior");
        }
        private static OrderFlowResearchRequest BoundaryCloudRequest(string root, bool reset)
        {
            DateTime boundary = Start.AddMinutes(1);
            OrderFlowResearchRequest request = CloudRequest(root, Row(boundary.AddMilliseconds(-500), 100, 5, Side.Buy),
                Row(boundary, 100, 7, Side.Buy), Row(boundary.AddMilliseconds(500), 100, 9, Side.Sell));
            request.Cloud.TimeProfiles = Schedule(reset, false, Period(600, 600), Period(601, 601)); return request;
        }
        private static void TestTimeProfileCloudBoundaries(string root)
        {
            OrderFlowResearchRequest request = BoundaryCloudRequest(root, true);
            ThresholdTimeProfiles shared = Schedule(true, false, Period(600, 600), Period(601, 601, ("MinimumTickVolume", 8)));
            request.Cloud.TimeProfiles = shared;
            AssertTrue(shared.ResetOnStart, "Cloud adaptation does not mutate a cumulative consumer's shared schedule");
            AssertFalse(request.Cloud.TimeProfiles.ResetOnStart, "Cloud always normalizes the irrelevant reset bit");
            request.CalculateCloud2 = true;
            request.Cloud2 = JsonSerializer.Deserialize<OrderFlowCloudSettings>(JsonSerializer.Serialize(request.Cloud));
            request.CloudLayers.Add(new OrderFlowCloudLayer { Settings = JsonSerializer.Deserialize<OrderFlowCloudSettings>(JsonSerializer.Serialize(request.Cloud)) });
            RecordingReplay observer = new RecordingReplay();
            OrderFlowResearchResult result = new OrderFlowResearchEngine().Run(request, CancellationToken.None, observer);
            foreach (OrderFlowCloud cloud in new[] { result.Clouds.Single(), result.Clouds2.Single(), result.CloudLayers.Single().Clouds.Single() })
            {
                AssertEqual(14m, cloud.Volume, "New period changes tick threshold, without splitting a Cloud");
                AssertEqual(21m, cloud.ContextImbalance.Volume, "Cloud context survives interval boundary and includes size-excluded active tick");
                AssertEqual(5m, cloud.Qualified.Volume, "First qualification remains unchanged across interval boundary");
                AssertEqual("OpenAtEnd", cloud.CompletionReason, "No invented interval completion reason");
            }
            AssertEqual(5m, observer.Frames[1].Result.Clouds.Single().Volume, "Replay retains prior forming chain across excluded boundary tick");
            OrderFlowResearchResult exported = Export(request);
            request.Cloud.TimeProfiles = shared with { ResetOnStart = false };
            AssertEqual(exported.ArtifactDirectory, Export(request).ArtifactDirectory, "Reset-bit normalization preserves Cloud artifact identity");
            string raw = JsonSerializer.Serialize(new { MinimumSumVolume = 1, TimeProfiles = shared }).Replace("\"ResetOnStart\":true,", "");
            OrderFlowCloudSettings imported = JsonSerializer.Deserialize<OrderFlowCloudSettings>(raw);
            AssertFalse(imported.TimeProfiles.ResetOnStart, "Older JSON with missing reset flag is normalized at the Cloud settings boundary");
            OrderFlowCloudAccumulator direct = new OrderFlowCloudAccumulator(imported, 1, new List<OrderFlowCloud>());
            direct.Add(new OrderFlowDeal { Time = Start.AddMinutes(1).AddMilliseconds(-500), Price = 100, Volume = 5, Side = Side.Buy, SourceSequence = 1 });
            direct.Add(new OrderFlowDeal { Time = Start.AddMinutes(1), Price = 100, Volume = 9, Side = Side.Buy, SourceSequence = 2 });
            AssertEqual(14m, direct.Snapshot().Single().Volume, "Direct accumulator needs no request validation to preserve boundary chain");

            request = BoundaryCloudRequest(root, true);
            AssertEqual(21m, Replay(request).Clouds.Single().Volume, "Equal thresholds preserve full accumulation across intervals");
            request.Cloud.TimeProfiles = Schedule(false, false, Period(600, 600));
            AssertEqual(5m, Replay(request).Clouds.Single().Volume, "Inactive input excluded from accumulation");
            request.Cloud.TimeProfiles = Schedule(false, true, Period(600, 600));
            AssertEqual(21m, Replay(request).Clouds.Single().Volume, "Base outside continues accumulation");
            request.Cloud.MaximumGapMilliseconds = 400;
            AssertEqual("Gap", Replay(request).Clouds.First().CompletionReason, "Normal gap boundary remains effective");
            DateTime midnight = Start.Date.AddDays(1);
            request = CloudRequest(root, Row(midnight.AddMilliseconds(-250), 100, 5, Side.Buy), Row(midnight.AddMilliseconds(250), 100, 7, Side.Buy));
            request.Cloud.TimeProfiles = Schedule(true, false, Period(1439, 1439), Period(0, 0));
            AssertEqual(12m, Replay(request).Clouds.Single().Volume, "Day and interval boundaries cannot split a Cloud");
            request = CloudRequest(root, Row(Start, 100, 5, Side.Buy), Row(Start.AddMilliseconds(100), 101, 7, Side.Buy));
            request.Cloud.TimeProfiles = Schedule(true, false, Period(600, 600)); request.Cloud.MaximumRangeTicks = 0;
            AssertEqual("Range", Replay(request).Clouds.First().CompletionReason, "Normal price-range boundary remains effective");
        }
        private static void TestTimeProfileDelta(string root)
        {
            DateTime boundary = Start.AddMinutes(1);
            OrderFlowResearchRequest request = Request(root, Row(boundary.AddSeconds(-1), 100, 100, Side.Buy),
                Row(boundary, 100, 20, Side.Buy), Row(boundary.AddSeconds(1), 100, 1, Side.Buy));
            request.FeatureWindowSeconds = 180;
            request.DeltaTimeProfiles = Schedule(true, false, Period(600, 600, ("MinimumAbsoluteDelta", 50)), Period(601, 601, ("MinimumAbsoluteDelta", 10)));
            OrderFlowResearchResult reset = Replay(request);
            AssertEqual(2, reset.Candidates.Count, "Reset clears cooldown as well as feature state");
            AssertEqual(20m, reset.Observations.Single(o => o.Features.Time == boundary).Features.Delta, "New window excludes old interval");
            AssertTrue(reset.Labels.Any(l => l.CandidateId == reset.Candidates[0].CandidateId), "Existing candidate labels survive reset");
            request.DeltaTimeProfiles = request.DeltaTimeProfiles with { ResetOnStart = false };
            OrderFlowResearchResult continuous = Replay(request);
            AssertEqual(1, continuous.Candidates.Count, "Unchecked reset retains cooldown");
            request.CandidateCooldownMilliseconds = 0;
            continuous = Replay(request);
            AssertEqual(120m, continuous.Observations.Single(o => o.Features.Time == boundary).Features.Delta, "No-reset preserves rolling data");
            request.DeltaTimeProfiles = Schedule(true, false, Period(601, 601, ("MinimumAbsoluteDelta", 10), ("MinimumTradeCount", 2)));
            OrderFlowResearchResult filtered = Replay(request);
            AssertEqual(boundary.AddSeconds(1), filtered.Candidates.Single().Time, "Trade-count profile changes calculation, not just visibility");
        }
        private static void TestTimeProfileMetrics(string root)
        {
            OrderFlowResearchRequest request = CloudRequest(root, Row(Start, 100, 10, Side.Buy), Row(Start.AddMilliseconds(10), 100, 4, Side.Sell), Row(Start.AddMilliseconds(20), 100, 10, Side.Buy));
            request.Cloud.TimeProfiles = Schedule(false, true, Period(600, 600, ("MinimumTradeCount", 2), ("MaximumTradeCount", 2), ("MinimumAbsoluteDelta", 5), ("MaximumAbsoluteDelta", 10)));
            OrderFlowCloud cloud = Replay(request).Clouds.Single();
            AssertEqual(2, cloud.Qualified.TradeCount, "Qualification uses scheduled count and delta");
            AssertFalse(cloud.ThresholdPassed, "Final prefix can fail while original qualification stays frozen");
            AssertFalse(new OrderFlowCloudFilter(new OrderFlowImbalanceSettings()).Apply(cloud).ImbalancePassed, "Legacy display filter respects calculation threshold verdict");
            request.Cloud.TimeProfiles = Schedule(false, true, Period(600, 600, ("MaximumTickVolume", 5)));
            AssertEqual(4m, Replay(request).Clouds.Single().Volume, "Physical upper-volume filter changes input set");
        }
        private static OrderFlowResearchRequest ManyCloudRequest(string root)
        {
            OrderFlowResearchRequest request = CloudRequest(root, Row(Start, 100, 10, Side.Buy), Row(Start.AddMilliseconds(100), 100, 30, Side.Buy));
            request.CalculateCloud = false;
            for (int i = 0; i < 40; i++)
            {
                request.CloudLayers.Add(new OrderFlowCloudLayer { Id = i.ToString("x32"), Name = "Cloud " + i,
                    Settings = new OrderFlowCloudSettings { MinimumSumVolume = 1, TimeProfiles = Schedule(false, false, Period(600, 600, ("MinimumTickVolume", i % 2 == 0 ? 1 : 20))) } });
            }
            return request;
        }
        private static void TestTimeProfileDiagonal(string root)
        {
            OrderFlowResearchRequest request = CloudRequest(root, Row(Start, 100, 200, Side.Sell), Row(Start, 101, 600, Side.Buy),
                Row(Start.AddMinutes(1), 100, 200, Side.Sell), Row(Start.AddMinutes(1), 101, 600, Side.Buy));
            request.Cloud.Imbalance.Source = OrderFlowImbalanceSource.Inside;
            request.Cloud.Imbalance.MinimumDominantVolume = 1000;
            request.Cloud.TimeProfiles = Schedule(true, false, Period(600, 600, ("ImbalanceMinimumDominantVolume", 600), ("ImbalanceMinimumDifference", 400), ("ImbalanceMinimumDeltaPercent", 50)),
                Period(601, 601, ("ImbalanceMinimumDominantVolume", 601)));
            OrderFlowResearchResult result = Replay(request);
            AssertEqual(1, result.Clouds.Count, "Scheduled diagonal thresholds affect qualification, not just visibility");
            AssertTrue(result.Clouds[0].ThresholdPassed, "Inclusive diagonal floor and delta boundaries");
            OrderFlowCloudFilter display = new OrderFlowCloudFilter(request.Cloud.Imbalance, timeProfiles: request.Cloud.TimeProfiles);
            AssertTrue(display.Passes(result.Clouds[0]) && display.Apply(result.Clouds[0]).ImbalancePassed, "Initial display preserves scheduled diagonal floors");
            AssertEqual(600m, result.Clouds[0].InsideImbalance.EligibleBuy.Buy, "Scheduled floor re-evaluates full saved pairs despite higher base floor");
            AssertEqual(1000m, request.Cloud.Imbalance.MinimumDominantVolume, "Schedule never mutates accumulator base floors");
            request.Cloud.TimeProfiles = request.Cloud.TimeProfiles with { Periods = ImmutableArray.Create(Period(600, 600, ("ImbalanceMinimumDeltaPercent", 101))) };
            Expect<ArgumentException>(() => request.Validate());
        }
        private static void TestTimeProfileInstances(string root)
        {
            OrderFlowResearchRequest request = ManyCloudRequest(root); OrderFlowResearchResult result = Replay(request);
            AssertEqual(40, result.CloudLayers.Count, "More than previous 32-layer ceiling");
            AssertEqual(40, result.CloudLayers.SelectMany(l => l.Clouds).Select(c => c.CloudId).Distinct().Count(), "Identical parameters remain separate instances");
            AssertEqual(40m, result.CloudLayers[0].Clouds.Single().Volume, "First filter"); AssertEqual(30m, result.CloudLayers[1].Clouds.Single().Volume, "Second independent filter");
            string unaffected = result.CloudLayers[1].Hash;
            request.CloudLayers[0].Settings.TimeProfiles = Schedule(false, false, Period(600, 600, ("MinimumTickVolume", 25)));
            AssertEqual(unaffected, Replay(request).CloudLayers[1].Hash, "Other instance remains byte-identical after edit");
            request.CloudLayers[0].Enabled = false;
            AssertEqual(39, Replay(request).CloudLayers.Count, "Disabled instance not calculated");
            request.CloudLayers[0].Id = request.CloudLayers[1].Id;
            Expect<ArgumentException>(() => request.Validate());
            request.CloudLayers[0].Id = new string('a', 32); request.CloudLayers[1].Id = new string('A', 32);
            Expect<ArgumentException>(() => request.Validate());
            Expect<InvalidDataException>(() => OrderFlowResearchUi.ValidateTimeWorkspace(new OrderFlowTimeWorkspace("order-flow-time-1", null, null, null, request.CloudLayers)));
        }
        private static void TestTimeProfileReplay(string root)
        {
            OrderFlowResearchRequest request = ManyCloudRequest(root); RecordingReplay observer = new RecordingReplay();
            OrderFlowResearchResult result = new OrderFlowResearchEngine().Run(request, CancellationToken.None, observer);
            AssertEqual(JsonSerializer.Serialize(Replay(request)), JsonSerializer.Serialize(result), "History and replay share the same calculators");
            AssertEqual(10m, observer.Frames[0].Result.CloudLayers[0].Clouds.Single().Volume, "First frame excludes future volume");
            AssertEqual(0, observer.Frames[0].Result.CloudLayers[1].Clouds.Count, "Independent high threshold not reached early");
            OrderFlowResearchChart chart = new OrderFlowResearchChart(); chart.SetResult(result); chart.BeginReplay(1);
            AssertEqual(0, chart.DisplayedCloudLayers.Count, "Additional result table starts empty before first replay tick");
            chart.ApplyReplayFrame(observer.Frames[0].Result);
            AssertEqual(10m, chart.CloudLayerRows(chart.DisplayedCloudLayers[0]).Single().Volume, "Additional table reads the same consumed prefix as the chart");
            AssertEqual(0, chart.CloudLayerRows(chart.DisplayedCloudLayers[1]).Count, "Additional table cannot reveal future high-threshold Cloud");
            chart.EndReplay(); AssertEqual(40m, chart.CloudLayerRows(chart.DisplayedCloudLayers[0]).Single().Volume, "Returning restores historical table source");
            for (int i = 0; i < observer.Frames.Count; i++) { AssertEqual(observer.Frozen[i], JsonSerializer.Serialize(observer.Frames[i]), "Earlier frames immutable"); }
            using CancellationTokenSource cancel = new CancellationTokenSource(); cancel.Cancel();
            Expect<OperationCanceledException>(() => new OrderFlowResearchEngine().Run(request, cancel.Token));
        }
        private static void TestTimeProfilePersistence(string root)
        {
            OrderFlowResearchRequest request = ManyCloudRequest(root);
            OrderFlowTimeWorkspace workspace = new OrderFlowTimeWorkspace("order-flow-time-1", null, null, null, request.CloudLayers);
            OrderFlowTimeWorkspace restored = JsonSerializer.Deserialize<OrderFlowTimeWorkspace>(JsonSerializer.Serialize(workspace));
            OrderFlowResearchUi.ValidateTimeWorkspace(restored); AssertEqual(40, restored.Layers.Count, "Workspace count round trip");
            AssertEqual(request.CloudLayers[0].Settings.CanonicalValue(), restored.Layers[0].Settings.CanonicalValue(), "Threshold persistence");
            Expect<InvalidDataException>(() => OrderFlowResearchUi.ValidateTimeWorkspace(restored with { Version = "unknown" }));
            OrderFlowResearchResult result = Export(request);
            AssertEqual(40, Directory.GetFiles(result.ArtifactDirectory, "cloud-*.csv").Count(p => !p.EndsWith("-pairs.csv")), "Every instance exported");
            AssertTrue(File.Exists(Path.Combine(result.ArtifactDirectory, "time-profiles.json")), "Versioned time evidence");
            AssertTrue(File.Exists(Path.Combine(result.ArtifactDirectory, "time-threshold-verdicts.json")), "Calculation verdicts exported separately");
            AssertEqual(result.ResearchSpecHash, Export(request).ResearchSpecHash, "Repeat immutable publication");
            request.CloudLayers[0].Name = "renamed"; request.CloudLayers[0].Color = "#FF112233";
            AssertEqual(result.ArtifactDirectory, Export(request).ArtifactDirectory, "Presentation change cannot conflict with immutable artifacts");
            request.CloudLayers.Reverse();
            foreach (OrderFlowCloudLayer layer in request.CloudLayers) { layer.Settings.MinimumSumVolume = 1.00m; }
            AssertEqual(result.ArtifactDirectory, Export(request).ArtifactDirectory, "Instance ordering cannot conflict with immutable artifacts");
        }

        private sealed class MetricThresholdProbe : Aindicator
        {
            internal int Resets;
            public override void OnStateChange(IndicatorState state)
            {
                if (state != IndicatorState.Configure) { return; }
                CreateSeries("Accepted volume", System.Drawing.Color.Blue, IndicatorChartPaintType.Column, true);
                ConfigureThresholdTimeProfiles(new[] { new ThresholdDefinition("V", "Volume", ThresholdMetric.Volume),
                    new ThresholdDefinition("D", "Delta", ThresholdMetric.Delta), new ThresholdDefinition("OI", "Open interest", ThresholdMetric.OpenInterest),
                    new ThresholdDefinition("T", "Trades", ThresholdMetric.Trades, 0, int.MaxValue, true) }, ResetAccumulation);
            }
            private void ResetAccumulation(int index) { Resets++; }
            public override void OnProcess(List<Candle> source, int index)
            {
                Candle candle = source[index]; decimal delta = candle.Trades.Sum(t => t.Side == Side.Buy ? t.Volume : -t.Volume);
                DataSeries[0].Values[index] = candle.Volume >= ResolveTimeThreshold("V", 1) && Math.Abs(delta) >= ResolveTimeThreshold("D", 1) &&
                    candle.OpenInterest >= ResolveTimeThreshold("OI", 1) && candle.Trades.Count >= ResolveTimeThreshold("T", 1) ? candle.Volume : 0;
            }
        }
        private static void TestTimeProfileIndicator(string root)
        {
            string original = Environment.CurrentDirectory; Directory.CreateDirectory(Path.Combine(root, "Engine")); Environment.CurrentDirectory = root;
            try
            {
                ThresholdTimeProfiles profiles = Schedule(true, false, Period(600, 600, ("OI", 50), ("T", 2)), Period(601, 601, ("V", 20)));
                List<Candle> candles = Enumerable.Range(0, 3).Select(i => new Candle { TimeStart = Start.AddMinutes(i), Volume = 10, OpenInterest = 100,
                    Open = 100, High = 100, Low = 100, Close = 100, Trades = new List<Trade> { new Trade { Side = Side.Buy, Volume = 7 }, new Trade { Side = Side.Sell, Volume = 3 } } }).ToList();
                foreach (StartProgram mode in new[] { StartProgram.IsOsTrader, StartProgram.IsTester, StartProgram.IsOsOptimizer })
                {
                    MetricThresholdProbe indicator = new MetricThresholdProbe { StartProgram = mode }; indicator.Init("probe" + mode, mode); indicator.SetTimeProfiles(profiles);
                    indicator.Process(candles);
                    AssertEqual("10,0,0", string.Join(",", indicator.DataSeries[0].Values), "All registered metrics and outside mode " + mode);
                    int resets = indicator.Resets; indicator.Process(candles); AssertEqual(resets, indicator.Resets, "Same last candle does not reset again");
                    indicator.Reload(); AssertEqual("10,0,0", string.Join(",", indicator.DataSeries[0].Values), "Full replay result " + mode);
                    Expect<InvalidOperationException>(() => indicator.Process(new List<decimal> { 1, 2 }));
                    if (mode == StartProgram.IsTester)
                    {
                        string identity = indicator.ParametersSpecification; indicator.Save();
                        MetricThresholdProbe reopened = new MetricThresholdProbe { StartProgram = mode }; reopened.Init("probe" + mode, mode);
                        AssertEqual(identity, reopened.ParametersSpecification, "Native scalar persistence"); AssertEqual(profiles.CanonicalValue(), reopened.TimeProfiles.CanonicalValue(), "Native schedule reopened");
                        reopened.Delete();
                    }
                    indicator.Delete();
                }
            }
            finally { Environment.CurrentDirectory = original; }
        }
        private static void TestTimeProfileChart(string root)
        {
            OrderFlowResearchRequest request = ManyCloudRequest(root); request.CloudLayers = request.CloudLayers.Take(2).ToList();
            OrderFlowResearchResult result = Replay(request); OrderFlowResearchChart chart = new OrderFlowResearchChart(); chart.SetResult(result); RenderDrawingChart(chart);
            List<(Point Center, double Radius, OrderFlowCloud Cloud)> hits = CalField<List<(Point, double, OrderFlowCloud)>>(chart, "_additionalCloudHits");
            AssertEqual(2, hits.Count, "Both additional Cloud instances rendered");
            AssertTrue(chart.CloudAt(hits[1].Center)?.CloudId == hits[1].Cloud.CloudId, "Topmost additional hit ownership");
            OrderFlowCloudLayer hidden = result.CloudLayers[1].Layer.Copy(); hidden.Visible = false; chart.SetCloudLayerView(hidden); RenderDrawingChart(chart);
            AssertEqual(1, hits.Count, "Hidden layer has no hits");
            OrderFlowCloudSettings filter = new OrderFlowCloudSettings { MinimumSumVolume = 100 };
            chart.SetCloudLayerPostFilter(result.CloudLayers[0].Layer.Id, filter); RenderDrawingChart(chart);
            AssertEqual(0, hits.Count, "Post-filter hides existing result without recalculation");
            AssertTrue(result.CloudLayers[0].Clouds.Single().ThresholdPassed, "Source evidence unchanged by post-filter");
            AssertFalse(chart.CloudLayerRows(result.CloudLayers[0]).Single().ThresholdPassed, "Table uses the same post-filter");
            File.Delete(request.TicksFilePath); chart.SetCloudLayerPostFilter(result.CloudLayers[0].Layer.Id, new OrderFlowCloudSettings { MinimumSumVolume = 1 });
            RenderDrawingChart(chart); AssertEqual(1, hits.Count, "Post-filter requires no raw input");
        }
        private sealed class CumulativeThresholdProbe : Aindicator
        {
            private readonly SortedDictionary<int, decimal> _accepted = new SortedDictionary<int, decimal>();
            internal int Resets;
            public override void OnStateChange(IndicatorState state)
            {
                if (state != IndicatorState.Configure) { return; }
                CreateSeries("Accumulated admitted volume", System.Drawing.Color.Blue, IndicatorChartPaintType.Column, true);
                ConfigureThresholdTimeProfiles(new[] { new ThresholdDefinition("V", "Volume", ThresholdMetric.Volume) },
                    index => { _accepted.Clear(); Resets++; });
            }
            public override void OnProcess(List<Candle> source, int index)
            {
                _accepted[index] = source[index].Volume >= ResolveTimeThreshold("V", 1) ? source[index].Volume : 0;
                DataSeries[0].Values[index] = _accepted.Values.Sum();
            }
        }
        private static void TestTimeProfileCumulativeIndicator(string root)
        {
            string original = Environment.CurrentDirectory; Directory.CreateDirectory(Path.Combine(root, "Engine")); Environment.CurrentDirectory = root;
            try
            {
                foreach (StartProgram mode in new[] { StartProgram.IsOsTrader, StartProgram.IsTester, StartProgram.IsOsOptimizer })
                {
                    List<Candle> candles = Enumerable.Range(0, 3).Select(i => new Candle { TimeStart = Start.AddMinutes(i), Volume = 10 }).ToList();
                    CumulativeThresholdProbe indicator = new CumulativeThresholdProbe(); indicator.Init("cumulative" + mode, mode);
                    ThresholdTimeProfiles profiles = Schedule(true, false, Period(600, 600), Period(601, 602));
                    indicator.SetTimeProfiles(profiles); indicator.Process(candles);
                    AssertEqual("10,10,20", string.Join(",", indicator.DataSeries[0].Values), "Real private accumulation resets per interval " + mode);
                    int resets = indicator.Resets; indicator.Process(candles);
                    AssertEqual(resets, indicator.Resets, "Active last candle has no second reset");
                    AssertEqual(20m, indicator.DataSeries[0].Values.Last(), "Active repeated candle does not duplicate accumulated volume");
                    candles[2].Volume = 15; indicator.Process(candles);
                    AssertEqual(25m, indicator.DataSeries[0].Values.Last(), "Forming candle replaces previous contribution");
                    indicator.SetTimeProfiles(profiles with { Enabled = false }); indicator.Reload();
                    AssertEqual("10,20,35", string.Join(",", indicator.DataSeries[0].Values), "Disable followed by full rebuild clears old segment state");
                    indicator.SetTimeProfiles(profiles with { ResetOnStart = false }); indicator.Reload();
                    AssertEqual("10,20,35", string.Join(",", indicator.DataSeries[0].Values), "Unchecked reset preserves admitted accumulation");
                    indicator.SetTimeProfiles(Schedule(false, false, Period(600, 600), Period(602, 602))); indicator.Reload();
                    AssertEqual("10,0,25", string.Join(",", indicator.DataSeries[0].Values), "Inactive middle candle is excluded from private accumulation");
                    indicator.Delete();
                }
            }
            finally { Environment.CurrentDirectory = original; }
        }
        private static void TestTimeProfileUi(string root)
        {
            ThresholdTimeProfilesUi profiles = new ThresholdTimeProfilesUi(Schedule(true, false, Period(600, 600)), OrderFlowTimeThresholds.Delta);
            ThresholdTimeProfilesUi cloudProfiles = new ThresholdTimeProfilesUi(Schedule(true, false, Period(600, 600)), OrderFlowTimeThresholds.Cloud, supportsReset: false);
            OrderFlowCloudLayerUi layer = new OrderFlowCloudLayerUi(new OrderFlowCloudLayer());
            try
            {
                AssertTrue(CalField<CheckBox>(profiles, "CheckBoxReset").IsChecked == true, "Reset checkbox reflects settings");
                AssertEqual(Visibility.Visible, CalField<CheckBox>(profiles, "CheckBoxReset").Visibility, "Cumulative consumers retain reset control");
                AssertEqual(Visibility.Collapsed, CalField<CheckBox>(cloudProfiles, "CheckBoxReset").Visibility, "Cloud editor has no reset control");
                AssertFalse(CalField<CheckBox>(cloudProfiles, "CheckBoxReset").IsChecked == true, "Hidden Cloud reset ignores old true setting");
                AssertTrue(CalField<CheckBox>(profiles, "CheckBoxBaseOutside").IsChecked == false, "Outside checkbox reflects settings");
                AssertTrue(CalField<Button>(layer, "ButtonTimeProfiles") != null, "Each instance exposes its time editor");
                AssertTrue(typeof(OrderFlowResearchUi).GetField("ButtonAddCloud", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) != null, "Main workbench has Add Cloud");
                AssertTrue(Application.Current == null, "No desktop application launched");
            }
            finally { profiles.Close(); cloudProfiles.Close(); layer.Close(); }
        }

        private static void TestTimeProfileMainRequest(string root)
        {
            OrderFlowResearchRequest fixture = CloudRequest(root, Row(Start, 100, 200, Side.Sell), Row(Start, 101, 600, Side.Buy));
            string previousDirectory = Environment.CurrentDirectory; Environment.CurrentDirectory = root;
            OrderFlowResearchUi ui = null;
            try
            {
                ui = new OrderFlowResearchUi(new ResourceDictionary { { "WindowStyleCanResize", new Style(typeof(Window)) } });
                OrderFlowResearchChart chart = CalField<OrderFlowResearchChart>(ui, "_chart");
                CalField<Slider>(ui, "SliderChartCloudScale").Value = 2.5;
                CalField<Slider>(ui, "SliderChartCloudContrast").Value = 3.5;
                AssertEqual(2.5, CalField<double>(chart, "_cloudScale"), "Real shared size slider reaches renderer");
                AssertEqual(3.5, CalField<double>(chart, "_cloudContrast"), "Real shared contrast slider reaches renderer");
                CalField<TextBox>(ui, "TextBoxTicksPath").Text = fixture.TicksFilePath;
                CalField<TextBox>(ui, "TextBoxOutputPath").Text = fixture.OutputRootPath;
                CalField<TextBox>(ui, "TextBoxPriceStep").Text = "1";
                CalField<CheckBox>(ui, "CheckBoxCalculateDelta").IsChecked = false;
                CalField<CheckBox>(ui, "CheckBoxCloud2SingleTicks").IsChecked = false;
                foreach (bool second in new[] { false, true })
                {
                    CalField<CheckBox>(ui, "CheckBoxCalculateCloud").IsChecked = !second;
                    CalField<CheckBox>(ui, "CheckBoxCalculateCloud2").IsChecked = second;
                    string prefix = second ? "Cloud2" : "Cloud";
                    CalField<TextBox>(ui, "TextBox" + prefix + "MinTick").Text = "1";
                    CalField<TextBox>(ui, "TextBox" + prefix + "Sum").Text = "1";
                    CalField<TextBox>(ui, "TextBox" + prefix + "ContextSeconds").Text = "17";
                    CalField<ComboBox>(ui, "ComboBox" + prefix + "ImbalanceSource").SelectedValue = OrderFlowImbalanceSource.Inside;
                    CalField<TextBox>(ui, "TextBox" + prefix + "ImbalanceVolume").Text = "1000";
                    FieldInfo profileField = typeof(OrderFlowResearchUi).GetField(second ? "_cloud2TimeProfiles" : "_cloudTimeProfiles", BindingFlags.Instance | BindingFlags.NonPublic);
                    ThresholdTimeProfiles schedule = Schedule(true, false, Period(600, 600, ("ImbalanceMinimumDominantVolume", 601)));
                    profileField.SetValue(ui, schedule);
                    MethodInfo build = typeof(OrderFlowResearchUi).GetMethod("BuildRequest", BindingFlags.Instance | BindingFlags.NonPublic);
                    OrderFlowResearchRequest request = (OrderFlowResearchRequest)build.Invoke(ui, null);
                    OrderFlowCloudSettings settings = second ? request.Cloud2 : request.Cloud;
                    AssertEqual(OrderFlowImbalanceSource.Inside, settings.Imbalance.Source, "Real main UI forwards scheduled source " + prefix);
                    AssertEqual(17, settings.Imbalance.ContextSeconds, "Calculation reads current context duration, not old display context");
                    AssertEqual(1000m, settings.Imbalance.MinimumDominantVolume, "Base floor read for omitted overrides");
                    OrderFlowResearchResult rejected = Replay(request);
                    AssertEqual(0, (second ? rejected.Clouds2 : rejected.Clouds).Count, "Scheduled diagonal condition changes actual UI-built calculation");
                    profileField.SetValue(ui, schedule with { Enabled = false });
                    request = (OrderFlowResearchRequest)build.Invoke(ui, null);
                    AssertEqual(OrderFlowImbalanceSource.Off, (second ? request.Cloud2 : request.Cloud).Imbalance.Source, "Disabled schedule preserves legacy display-only source");
                    OrderFlowResearchResult legacy = Replay(request);
                    AssertEqual(1, (second ? legacy.Clouds2 : legacy.Clouds).Count, "Disabled schedule preserves legacy qualification");
                }
                AssertTrue(Application.Current == null && !ui.IsVisible, "Main window component was never shown and no application was started");
            }
            finally { ui?.Close(); Environment.CurrentDirectory = previousDirectory; }
        }
    }
}
