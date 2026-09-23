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
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;

namespace OsEngine.OrderFlowResearch.Tests
{
    internal static partial class Program
    {
        #region Formulas and profiles

        private static OrderFlowDeal ImbalanceTick(decimal price, decimal volume, Side side, int seconds = 0)
        { return new OrderFlowDeal { Time = Start.AddSeconds(seconds), Price = price, Volume = volume, Side = side }; }

        private static void TestImbalanceFormulas(string root)
        {
            foreach (decimal step in new[] { 1m, 5m, 0.00001m })
            {
                OrderFlowResearchRequest request = CloudRequest(root, Row(Start, 100, 200, Side.Sell), Row(Start, 100 + step, 600, Side.Buy));
                request.PriceStep = step;
                request.Cloud.Imbalance.Source = OrderFlowImbalanceSource.Inside;
                OrderFlowCloud cloud = Replay(request).Clouds.Single();
                AssertEqual(50m, cloud.DeltaPercent, "Volume delta normalized to total");
                AssertEqual(0m, cloud.SidePercent, "Count imbalance is different");
                AssertEqual(300d, cloud.InsideImbalance.BuyRatioPercent.Value, "Diagonal volume ratio, not percent increase");
                AssertEqual(100m, cloud.InsideImbalance.BestBuy.LowerPrice, "Exact adjacent-level witness");
                AssertTrue(cloud.ImbalancePassed, "Inclusive 300% boundary");
                AssertTrue(cloud.InsideImbalance.SellRatioPercent == null, "No dominant sell pair");
            }
            OrderFlowResearchRequest samePrice = CloudRequest(root, Row(Start, 100, 200, Side.Sell), Row(Start, 100, 600, Side.Buy));
            AssertEqual(0, Replay(samePrice).Clouds.Single().InsideImbalance.ComparablePairs, "Same-price volumes are not diagonal");
        }

        private static void TestImbalanceExactComparison(string root)
        {
            decimal tiny = 0.0000000000000000000000000001m;
            AssertTrue(OrderFlowVolumeComparison.CompareProducts(tiny, tiny * 2, tiny, tiny) > 0, "Fractional products cannot underflow to equal zero");
            AssertTrue(OrderFlowVolumeComparison.CompareProducts(decimal.MaxValue, 100, decimal.MaxValue, 99) > 0, "Wide integer products cannot overflow");
            AssertEqual(0, OrderFlowVolumeComparison.CompareProducts(0.3m, 100, 0.1m, 300), "Exact fractional equality");
            OrderFlowImbalanceProfile overflowing = new OrderFlowImbalanceProfile(1, new OrderFlowImbalanceSettings());
            overflowing.Change(ImbalanceTick(100, decimal.MaxValue, Side.Buy));
            overflowing.Change(ImbalanceTick(100, decimal.MaxValue, Side.Sell));
            Expect<OverflowException>(() => overflowing.Snapshot());
            OrderFlowImbalanceSettings settings = new OrderFlowImbalanceSettings { Source = OrderFlowImbalanceSource.Inside };
            OrderFlowImbalanceProfile profile = new OrderFlowImbalanceProfile(1, settings);
            profile.Change(ImbalanceTick(100, tiny, Side.Sell)); profile.Change(ImbalanceTick(101, decimal.MaxValue, Side.Buy));
            AssertTrue(double.IsFinite(new OrderFlowDiagonalPair(100, decimal.MaxValue, tiny).BuyRatioPercent), "Display ratio supports extreme positive decimal ratios");
            Expect<InvalidDataException>(() => profile.Snapshot());
        }

        private static void TestImbalancePrecisionThresholds(string root)
        {
            decimal huge = 10000000000000000000000000000m;
            AssertFalse(OrderFlowVolumeComparison.DifferenceAtLeast(huge, 0.1m, huge), "Difference cannot round up to its floor");
            AssertTrue(OrderFlowVolumeComparison.DifferenceAtLeast(huge, 0.1m, huge - 1), "Exact difference still exceeds lower floor");
            AssertTrue(OrderFlowVolumeComparison.DifferenceAtLeast(0.3m, 0.1m, 0.2m), "Fractional difference equality passes");
            OrderFlowImbalanceSettings settings = new OrderFlowImbalanceSettings { Source = OrderFlowImbalanceSource.Inside, MinimumDeltaPercent = 100 };
            OrderFlowImbalanceSnapshot buy = new OrderFlowImbalanceSnapshot { BuyVolume = huge, SellVolume = 0.1m,
                EligibleBuy = new OrderFlowDiagonalPair(1, huge, 0.1m) };
            OrderFlowImbalanceSnapshot sell = new OrderFlowImbalanceSnapshot { BuyVolume = 0.1m, SellVolume = huge,
                EligibleSell = new OrderFlowDiagonalPair(1, 0.1m, huge) };
            AssertFalse(settings.Passes(buy, null), "Nonzero Sell prevents exact Buy delta100 despite rounded display");
            AssertFalse(settings.Passes(sell, null), "Nonzero Buy prevents exact Sell delta100");
            AssertTrue(OrderFlowVolumeComparison.DeltaAtLeast(0.3m, 0.1m, 50), "Exact fractional delta boundary");
        }

        private static void TestImbalancePrecisionWindow(string root)
        {
            decimal huge = 10000000000000000000000000000m;
            OrderFlowImbalanceSettings settings = new OrderFlowImbalanceSettings { ContextSeconds = 2,
                Source = OrderFlowImbalanceSource.Context, Direction = OrderFlowImbalanceDirection.Sell };
            OrderFlowImbalanceWindow lossy = new OrderFlowImbalanceWindow(1, settings);
            lossy.Add(ImbalanceTick(2, huge, Side.Buy));
            Expect<InvalidDataException>(() => lossy.Add(ImbalanceTick(2, 0.1m, Side.Buy, 1)));
            OrderFlowImbalanceWindow crossLevel = new OrderFlowImbalanceWindow(1, settings);
            crossLevel.Add(ImbalanceTick(2, huge, Side.Buy));
            Expect<InvalidDataException>(() => crossLevel.Add(ImbalanceTick(3, 0.1m, Side.Buy, 1)));
            OrderFlowImbalanceWindow exact = new OrderFlowImbalanceWindow(1, settings);
            exact.Add(ImbalanceTick(2, huge / 10, Side.Buy));
            exact.Add(ImbalanceTick(2, 0.1m, Side.Buy, 1));
            exact.Add(ImbalanceTick(1, 1, Side.Sell, 3));
            OrderFlowImbalanceSnapshot snapshot = exact.Snapshot();
            AssertEqual(0.1m, snapshot.BuyVolume, "Expiry retains small active tick at inclusive boundary");
            AssertEqual(1000d, snapshot.SellRatioPercent.Value, "Remaining diagonal volume stays exact");
            AssertTrue(settings.Passes(null, snapshot), "Exact supported aggregates pass after large tick expires");
        }

        private static void TestImbalancePrecisionRejectedResult(string root)
        {
            decimal huge = 10000000000000000000000000000m;
            foreach (bool sameSide in new[] { false, true })
            {
                OrderFlowResearchRequest request = CloudRequest(root, sameSide ? new[] {
                    Row(Start, 2, huge, Side.Buy), Row(Start.AddSeconds(1), 2, 0.1m, Side.Buy), Row(Start.AddSeconds(3), 1, 1, Side.Sell) }
                    : new[] { Row(Start, 1, 0.1m, Side.Sell), Row(Start.AddSeconds(1), 2, huge, Side.Buy) });
                request.CalculateCloud = false; request.CalculateCloud2 = true;
                request.Cloud2.MinimumTickVolume = 1;
                request.Cloud2.Imbalance.Source = OrderFlowImbalanceSource.Context; request.Cloud2.Imbalance.ContextSeconds = 2;
                OrderFlowResearchResult result = Replay(request);
                AssertFalse(result.Quality.ResearchAccepted, "Unrepresentable level/side/combined aggregate rejects result");
                AssertEqual("TICK_INVALID", result.Input.FailureReasonCode, "Precision rejection is visible in input audit");
                RecordingReplay observer = new RecordingReplay();
                AssertEqual(JsonSerializer.Serialize(result), JsonSerializer.Serialize(new OrderFlowResearchEngine().Run(request, CancellationToken.None, observer)),
                    "Replay and normal run reject same precision input");
                AssertTrue(result.Clouds2.All(cloud => cloud.ContextImbalance.SellVolume == 0), "No invalid context was published before rejection");
            }
        }

        private static void TestImbalanceMissingNeighbors(string root)
        {
            OrderFlowImbalanceSettings settings = new OrderFlowImbalanceSettings { Source = OrderFlowImbalanceSource.Inside };
            OrderFlowImbalanceProfile profile = new OrderFlowImbalanceProfile(1, settings);
            profile.Change(ImbalanceTick(100, 1, Side.Sell)); profile.Change(ImbalanceTick(102, 1000, Side.Buy));
            AssertEqual(0, profile.Snapshot().ComparablePairs, "Do not jump over absent price101");
            AssertFalse(settings.Passes(profile.Snapshot(), null), "Missing opponent never gives infinity or pass");
            profile.Change(ImbalanceTick(101, 2, Side.Sell));
            AssertEqual(50000d, profile.Snapshot().BuyRatioPercent.Value, "New adjacent opponent creates comparison");
            profile.Change(ImbalanceTick(101, 2, Side.Sell), true);
            AssertEqual(0, profile.Snapshot().ComparablePairs, "Removing opponent removes stale ratio");
            OrderFlowImbalanceProfile precision = new OrderFlowImbalanceProfile(0.0000000000000000000000000001m, settings);
            precision.Change(ImbalanceTick(decimal.MaxValue, 1, Side.Sell)); precision.Change(ImbalanceTick(decimal.MaxValue, 10, Side.Buy));
            AssertEqual(0, precision.Snapshot().ComparablePairs, "Unrepresentable neighbor cannot round to same-price comparison");
        }

        private static void TestImbalanceVolumeFloors(string root)
        {
            OrderFlowImbalanceSettings settings = new OrderFlowImbalanceSettings { Source = OrderFlowImbalanceSource.Inside,
                MinimumDominantVolume = 600, MinimumDifference = 400, MinimumRatioPercent = 300 };
            OrderFlowImbalanceProfile profile = new OrderFlowImbalanceProfile(1, settings);
            profile.Change(ImbalanceTick(100, 1, Side.Sell)); profile.Change(ImbalanceTick(101, 10, Side.Buy));
            profile.Change(ImbalanceTick(102, 200, Side.Sell)); profile.Change(ImbalanceTick(103, 600, Side.Buy));
            OrderFlowImbalanceSnapshot snapshot = profile.Snapshot();
            AssertEqual(100m, snapshot.BestBuy.LowerPrice, "Highest raw ratio is tiny-volume pair");
            AssertEqual(102m, snapshot.EligibleBuy.LowerPrice, "Filter can accept lower ratio with sufficient volume/difference");
            AssertTrue(settings.Passes(snapshot, null), "Inclusive volume/difference/ratio thresholds");
            settings.MinimumDeltaPercent = 60;
            AssertFalse(settings.Passes(snapshot, null), "Optional directional volume delta applies to whole profile");
        }

        private static void TestImbalanceBothDirections(string root)
        {
            OrderFlowImbalanceSettings settings = new OrderFlowImbalanceSettings { Source = OrderFlowImbalanceSource.Both };
            OrderFlowImbalanceSnapshot buy = new OrderFlowImbalanceSnapshot { BuyVolume = 600, SellVolume = 200, EligibleBuy = new OrderFlowDiagonalPair(100, 600, 200) };
            OrderFlowImbalanceSnapshot sell = new OrderFlowImbalanceSnapshot { BuyVolume = 200, SellVolume = 600, EligibleSell = new OrderFlowDiagonalPair(100, 200, 600) };
            AssertFalse(settings.Passes(buy, sell), "Both requires a common direction");
            AssertTrue(settings.Passes(buy, buy), "Both buys pass"); AssertTrue(settings.Passes(sell, sell), "Both sells pass");
            settings.Direction = OrderFlowImbalanceDirection.Buy;
            AssertFalse(settings.Passes(sell, sell), "Selected direction enforced");
            settings.Source = OrderFlowImbalanceSource.Context; settings.Direction = OrderFlowImbalanceDirection.Sell;
            settings.MinimumDeltaPercent = 50;
            AssertTrue(settings.Passes(buy, sell), "Context-only and signed sell delta boundary");
        }

        private static void TestImbalanceIndexUpdates(string root)
        {
            OrderFlowImbalanceSettings settings = new OrderFlowImbalanceSettings();
            OrderFlowImbalanceProfile profile = new OrderFlowImbalanceProfile(1, settings);
            Dictionary<decimal, (decimal Buy, decimal Sell)> levels = new Dictionary<decimal, (decimal, decimal)>();
            Queue<OrderFlowDeal> active = new Queue<OrderFlowDeal>();
            Random random = new Random(17);
            for (int i = 0; i < 300; i++)
            {
                OrderFlowDeal tick = ImbalanceTick(100 + random.Next(8), random.Next(1, 30), random.Next(2) == 0 ? Side.Buy : Side.Sell);
                profile.Change(tick); active.Enqueue(tick);
                if (active.Count > 40) { profile.Change(active.Dequeue(), true); }
                levels.Clear();
                foreach (OrderFlowDeal item in active)
                {
                    levels.TryGetValue(item.Price, out (decimal Buy, decimal Sell) value);
                    if (item.Side == Side.Buy) { value.Buy += item.Volume; } else { value.Sell += item.Volume; }
                    levels[item.Price] = value;
                }
                List<OrderFlowDiagonalPair> pairs = new List<OrderFlowDiagonalPair>();
                foreach (KeyValuePair<decimal, (decimal Buy, decimal Sell)> level in levels)
                {
                    if (level.Value.Sell > 0 && levels.TryGetValue(level.Key + 1, out (decimal Buy, decimal Sell) upper) && upper.Buy > 0)
                    { pairs.Add(new OrderFlowDiagonalPair(level.Key, upper.Buy, level.Value.Sell)); }
                }
                OrderFlowDiagonalPair bestBuy = pairs.Where(pair => pair.Buy > pair.Sell).OrderByDescending(pair => pair.BuyRatioPercent).ThenBy(pair => pair.LowerPrice).FirstOrDefault();
                OrderFlowDiagonalPair bestSell = pairs.Where(pair => pair.Sell > pair.Buy).OrderByDescending(pair => pair.SellRatioPercent).ThenBy(pair => pair.LowerPrice).FirstOrDefault();
                OrderFlowImbalanceSnapshot snapshot = profile.Snapshot();
                AssertEqual(pairs.Count, snapshot.ComparablePairs, "Index matches naive occupied-price scan");
                AssertEqual(bestBuy, snapshot.BestBuy, "Incremental buy winner updates after expiry");
                AssertEqual(bestSell, snapshot.BestSell, "Incremental sell winner updates after expiry");
            }
        }

        #endregion

        #region Causality and integration

        private static void TestImbalanceContextCausality(string root)
        {
            OrderFlowResearchRequest request = CloudRequest(root, Row(Start, 101, 10, Side.Buy),
                Row(Start.AddMilliseconds(100), 100, 3, Side.Sell), Row(Start.AddMilliseconds(200), 100, 10, Side.Sell),
                Row(Start.AddMilliseconds(300), 101, 4, Side.Buy));
            request.Cloud.MinimumTickVolume = 10; request.Cloud.MinimumSumVolume = 20; request.Cloud.Imbalance.ContextSeconds = 2;
            OrderFlowCloud cloud = Replay(request).Clouds.Single();
            AssertEqual(10m, cloud.InsideImbalance.SellVolume, "Inside excludes small trades");
            AssertEqual(13m, cloud.ContextImbalance.SellVolume, "Context includes small preceding trade");
            AssertEqual(10m, cloud.ContextImbalance.BuyVolume, "Later filtered trade cannot rewrite Cloud anchor snapshot");
            request = CloudRequest(root, Row(Start, 101, 10, Side.Buy), Row(Start.AddSeconds(2), 100, 20, Side.Sell));
            request.Cloud.Imbalance.ContextSeconds = 1;
            OrderFlowResearchResult result = Replay(request);
            AssertEqual(10m, result.Clouds[0].ContextImbalance.BuyVolume, "Breaking tick cannot expire previous Cloud evidence");
            AssertEqual(0m, result.Clouds[0].ContextImbalance.SellVolume, "Breaking tick not borrowed");
            AssertEqual(0m, result.Clouds[1].ContextImbalance.BuyVolume, "New Cloud sees expired window");
            request = CloudRequest(root, Row(Start, 100, 10, Side.Buy), Row(Start, 100, 2, Side.Sell), Row(Start, 101, 20, Side.Sell));
            request.Cloud.MinimumTickVolume = 10; request.Cloud.MaximumRangeTicks = 0;
            result = Replay(request);
            AssertEqual(0m, result.Clouds[0].ContextImbalance.SellVolume, "Same timestamp later rows cannot enter earlier anchor");
            AssertEqual(22m, result.Clouds[1].ContextImbalance.SellVolume, "New anchor sees all earlier same-time rows");
        }

        private static void TestImbalanceWindowBoundary(string root)
        {
            OrderFlowImbalanceWindow window = new OrderFlowImbalanceWindow(1, new OrderFlowImbalanceSettings { ContextSeconds = 2 });
            window.Add(ImbalanceTick(100, 100, Side.Sell)); window.Add(ImbalanceTick(101, 300, Side.Buy, 2));
            OrderFlowImbalanceSnapshot frozen = window.Snapshot();
            AssertEqual(300d, frozen.BuyRatioPercent.Value, "Left time boundary included");
            OrderFlowDeal next = ImbalanceTick(101, 1, Side.Buy, 2); next.Time = next.Time.AddTicks(10); window.Add(next);
            AssertEqual(0m, window.Snapshot().SellVolume, "One microsecond outside boundary expires");
            AssertEqual(100m, frozen.SellVolume, "Earlier snapshot immutable");
            using (CancellationTokenSource cancel = new CancellationTokenSource())
            {
                cancel.Cancel();
                Expect<OperationCanceledException>(() => window.Add(ImbalanceTick(100, 1, Side.Buy, 10), cancel.Token));
            }
            DateTime day = Start.Date;
            OrderFlowResearchRequest request = CloudRequest(root, Row(day.AddTicks(-10), 100, 200, Side.Sell), Row(day, 101, 600, Side.Buy));
            request.FromDate = request.ToDate = day;
            AssertEqual(0m, Replay(request).Clouds.Single().ContextImbalance.SellVolume, "Excluded date cannot warm context");
        }

        private static void TestImbalanceSingleTickReplay(string root)
        {
            OrderFlowResearchRequest request = TwoCloudRequest(root);
            File.WriteAllLines(request.TicksFilePath, new[] { Row(Start, 100, 200, Side.Sell), Row(Start, 101, 1000, Side.Buy) });
            request.Cloud2.Imbalance.Source = OrderFlowImbalanceSource.Context;
            request.Cloud2.Imbalance.MinimumRatioPercent = 500;
            RecordingReplay observer = new RecordingReplay();
            OrderFlowResearchResult result = new OrderFlowResearchEngine().Run(request, CancellationToken.None, observer);
            OrderFlowCloud single = result.Clouds2.Single();
            AssertEqual(0, single.InsideImbalance.ComparablePairs, "Single tick has no internal diagonal");
            AssertEqual(500d, single.ContextImbalance.BuyRatioPercent.Value, "Single tick can use surrounding smaller trades");
            AssertTrue(single.ImbalancePassed, "Context single tick passes");
            AssertEqual(JsonSerializer.Serialize(result), JsonSerializer.Serialize(Replay(request)), "Observer and normal engine match");
            for (int i = 0; i < observer.Frames.Count; i++)
            { AssertEqual(observer.Frozen[i], JsonSerializer.Serialize(observer.Frames[i]), "Published context/internal evidence stays immutable"); }
            request.Cloud2.Imbalance.Source = OrderFlowImbalanceSource.Inside;
            AssertFalse(Replay(request).Clouds2.Single().ImbalancePassed, "Same tick fails internal filter without comparison");
        }

        private static void TestImbalanceFilterIndependence(string root)
        {
            OrderFlowResearchRequest request = TwoCloudRequest(root); request.CalculateDelta = true;
            OrderFlowResearchResult original = Replay(request);
            request.Cloud.Imbalance.Source = OrderFlowImbalanceSource.Both; request.Cloud.Imbalance.MinimumRatioPercent = 100000;
            OrderFlowResearchResult filtered = Replay(request);
            AssertEqual(original.Clouds.Count, filtered.Clouds.Count, "Filter retains all raw chains");
            AssertTrue(filtered.Clouds.All(cloud => !cloud.ImbalancePassed), "Extreme threshold records failed verdicts");
            for (int i = 0; i < original.Clouds.Count; i++)
            {
                AssertEqual(original.Clouds[i].CloudId, filtered.Clouds[i].CloudId, "Segmentation and source IDs unchanged");
                AssertEqual(original.Clouds[i].Volume, filtered.Clouds[i].Volume, "No tick loss or regrouping");
                AssertEqual(JsonSerializer.Serialize(original.Clouds[i].Qualified), JsonSerializer.Serialize(filtered.Clouds[i].Qualified), "Volume qualification snapshot unchanged");
            }
            AssertEqual(original.Cloud2Hash, filtered.Cloud2Hash, "Other layer unchanged");
            AssertEqual(original.FeatureHash, filtered.FeatureHash, "Delta calculation unchanged");
            AssertEqual(JsonSerializer.Serialize(original.Labels), JsonSerializer.Serialize(filtered.Labels), "Future labels unchanged");
            AssertFalse(original.ResearchSpecHash == filtered.ResearchSpecHash, "Filter settings belong to run provenance");
        }

        private static void TestImbalanceArtifactSchema(string root)
        {
            OrderFlowResearchRequest request = TwoCloudRequest(root); request.Cloud2.Imbalance.Source = OrderFlowImbalanceSource.Inside;
            OrderFlowResearchResult result = Export(request);
            string[] rows = File.ReadAllLines(Path.Combine(result.ArtifactDirectory, "clouds2.csv"));
            string[] header = rows[0].Split(',');
            AssertTrue(header.Contains("inside_best_buy_ratio_percent") && header.Contains("context_eligible_sell_lower_price"), "Raw and passing witnesses exported separately");
            AssertTrue(rows.Skip(1).All(row => row.Split(',').Length == header.Length), "Nullable pair fields keep CSV width");
            AssertEqual(result.Clouds2.Count + 1, rows.Length, "Failed Clouds retained in CSV");
            JsonElement manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(result.ArtifactDirectory, "manifest.json"))).RootElement;
            AssertEqual(0, manifest.GetProperty("Cloud2PassedCount").GetInt32(), "Pass count distinguishes from raw count");
            AssertEqual(30, manifest.GetProperty("Cloud2").GetProperty("Imbalance").GetProperty("ContextSeconds").GetInt32(), "Window persists");
            AssertEqual(result.ArtifactDirectory, Export(request).ArtifactDirectory, "Immutable reuse includes new statistics");
            request.Cloud2.Imbalance.ContextSeconds++;
            AssertFalse(result.ArtifactDirectory == Export(request).ArtifactDirectory, "Context window changes identity");
        }

        private static void TestImbalanceDisplayAndMarkup(string root)
        {
            OrderFlowResearchRequest request = TwoCloudRequest(root); request.Cloud2.Imbalance.Source = OrderFlowImbalanceSource.Inside;
            OrderFlowResearchResult result = Replay(request); string original = JsonSerializer.Serialize(result);
            OrderFlowResearchChart chart = new OrderFlowResearchChart(); chart.SetResult(result); chart.SetLayers(false, true, true); chart.SetCloudPriceMode(true);
            foreach (OrderFlowDisplayTimeFrame frame in OrderFlowChartTimeFrames.GetMenuValues())
            {
                chart.SetTimeFrame(frame); chart.SetShowRejectedClouds(false, false); RenderDrawingChart(chart);
                AssertEqual(0, CloudHits(chart, true).Count, "Failed singles have no circles/hits on " + frame);
                AssertFalse(ContainsPen(VisualTreeHelper.GetDrawing(chart), Colors.MediumOrchid, 2), "Failed second path hidden");
                chart.SetShowRejectedClouds(false, true); RenderDrawingChart(chart);
                AssertEqual(result.Clouds2.Count, CloudHits(chart, true).Count, "Second-only bypass restores rejected points");
                AssertTrue(ContainsPen(VisualTreeHelper.GetDrawing(chart), Colors.MediumOrchid, 2), "Bypass restores second Cloud path on " + frame);
                AssertEqual(result.Clouds.Count, CloudHits(chart, false).Count, "First layer unchanged");
            }
            AssertEqual(original, JsonSerializer.Serialize(result), "Bypass never changes statistics/hash");
            AssertTrue(OrderFlowResearchChart.CloudDetails(result.Clouds2[0]).Contains("FAIL"), "Tooltip exposes verdict");
            XDocument ui;
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Research.Ui.xaml")) { ui = XDocument.Load(stream); }
            foreach (string prefix in new[] { "Cloud", "Cloud2" })
            foreach (string name in new[] { "ComboBox" + prefix + "ImbalanceSource", "TextBox" + prefix + "ContextSeconds", "TextBox" + prefix + "ImbalanceRatio" })
            { AssertEqual(1, ui.Descendants().Count(element => (string)element.Attribute("Name") == name), "Independent control exists once " + name); }
            XElement surface = ui.Descendants().Single(element => (string)element.Attribute("Name") == "ScrollViewerChartSurface");
            AssertTrue(surface.Descendants().Any(element => (string)element.Attribute("Name") == "CheckBoxCloud2Rejected"), "Bypass moves with detached chart");
            OrderFlowImbalanceSettings settings = new OrderFlowImbalanceSettings { Source = OrderFlowImbalanceSource.Off, MinimumRatioPercent = -1 };
            settings.Validate(); AssertEqual(300m, settings.MinimumRatioPercent, "Disabled thresholds canonicalized");
            settings.ContextSeconds = 0; Expect<ArgumentException>(() => settings.Validate());
        }

        #endregion
    }
}
