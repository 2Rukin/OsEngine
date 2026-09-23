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
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace OsEngine.OrderFlowResearch.Tests
{
    internal static partial class Program
    {
        #region Cloud fixtures

        private static OrderFlowResearchRequest CloudRequest(string root, params string[] rows)
        {
            OrderFlowResearchRequest request = Request(root, rows);
            request.CalculateDelta = false;
            request.CalculateCloud = true;
            request.Cloud.MinimumSumVolume = 1;
            return request;
        }

        private static void TestCloudBoundaries(string root)
        {
            OrderFlowResearchRequest request = CloudRequest(root,
                Row(Start, 100, 8, Side.Buy),
                Row(Start.AddMilliseconds(1000), 105, 10, Side.Sell),
                Row(Start.AddMilliseconds(2000).AddTicks(10), 104, 12, Side.Buy),
                Row(Start.AddMilliseconds(2001), 110, 13, Side.Sell));
            request.Cloud.MinimumTickVolume = 8;
            request.Cloud.MinimumSumVolume = 12;
            OrderFlowResearchResult result = Replay(request);
            AssertTrue(result.Quality.ResearchAccepted, "Cloud replay accepted");
            AssertEqual(3, result.Clouds.Count, "Gap and full-range boundaries close independent chains");
            AssertEqual(18m, result.Clouds[0].Volume, "Equal max gap and range included");
            AssertEqual(Start.AddMilliseconds(1000), result.Clouds[0].Time, "Anchor is last included tick");
            AssertEqual(Start.AddMilliseconds(2000).AddTicks(10), result.Clouds[0].CompletedAt.Value, "Microsecond above gap closes later");
            AssertEqual("Gap", result.Clouds[0].CompletionReason, "Gap evidence");
            AssertEqual("Range", result.Clouds[1].CompletionReason, "Range evidence");
            AssertEqual(12m, result.Clouds[1].Volume, "Breaking tick starts next chain");
            AssertEqual(13m, result.Clouds[2].Volume, "Next range-breaking tick retained");
            AssertFalse(result.Clouds[2].CompletedAt.HasValue, "EOF is not market completion");
            AssertEqual(3L, result.Clouds[0].CompletionSourceSequence.Value, "Closing sequence captured");
        }

        private static void TestCloudFilteredTicks(string root)
        {
            OrderFlowResearchRequest request = CloudRequest(root,
                Row(Start, 100, 8, Side.Buy), Row(Start.AddMilliseconds(500), 1000, 1, Side.Buy),
                Row(Start.AddMilliseconds(750), 1000, 2, Side.Buy), Row(Start.AddMilliseconds(1000), 100, 10, Side.Sell));
            request.Cloud.MinimumTickVolume = 8;
            request.Cloud.MinimumSumVolume = 18;
            request.Cloud.MaximumRangeTicks = 0;
            OrderFlowResearchResult result = Replay(request);
            AssertEqual(1, result.Clouds.Count, "Filtered sizes do not break chain or change extrema");
            AssertEqual(18m, result.Clouds[0].Volume, "Minimum size equality admitted");
            AssertEqual(2, result.Clouds[0].TradeCount, "Only eligible rows counted");
            request.Cloud.MinimumSumVolume = 18.00001m;
            AssertEqual(0, Replay(request).Clouds.Count, "Sum below threshold rejected");
        }

        private static void TestCloudModes(string root)
        {
            OrderFlowResearchRequest request = CloudRequest(root, Row(Start, 100, 10, Side.Buy),
                Row(Start, 100, 10, Side.Buy), Row(Start, 100, 100, Side.Sell));
            OrderFlowCloud cloud = Replay(request).Clouds.Single();
            AssertTrue(cloud.SidePercent > 0 && cloud.Delta < 0, "Count imbalance differs from volume delta");
            AssertEqual("Accumulated", cloud.Kind, "Mixed sides share one chain");
            AssertEqual(3, cloud.TradeCount, "Repeated IDs and timestamps preserved");
            request = CloudRequest(root, Row(Start, 100, 200, Side.Sell));
            cloud = Replay(request).Clouds.Single();
            AssertEqual("SingleTrade", cloud.Kind, "One qualifying tick is a single-trade cloud");
            AssertEqual(200m, cloud.Qualified.Volume, "Single snapshot qualifies on its tick");
            AssertFalse(cloud.CompletedAt.HasValue, "Single at EOF has no later closure evidence");
        }

        private static void TestCloudQualification(string root)
        {
            string[] rows = {
                "20260316,092233,31300,1,Buy,0,639256675320162135",
                "20260316,092333,31297,1,Sell,0,639256675320169496",
                "20260316,092333,31281,5,Sell,0,639256675320169522",
                "20260316,092333,31258,1,Sell,0,639256675320169531",
                "20260316,092333,31241,2,Sell,0,639256675320169539",
                "20260316,092333,31237,2,Sell,0,639256675320169546",
                "20260316,092334,31242,1,Sell,0,639256675320168743" };
            OrderFlowResearchRequest request = CloudRequest(root, rows);
            request.Cloud.MinimumSumVolume = 10;
            request.Cloud.MaximumRangeTicks = 60;
            OrderFlowCloud cloud = Replay(request).Clouds.Single();
            AssertEqual(12m, cloud.Volume, "Owner ADR example total");
            AssertEqual(6, cloud.TradeCount, "Owner example count");
            AssertEqual(-12m, cloud.Delta, "Owner example delta");
            AssertEqual(60m, cloud.RangeTicks, "Whole-chain range equality");
            AssertEqual(-55m, cloud.PriceChange, "First to last price");
            AssertEqual(375158m / 12m, cloud.Vwap, "Weighted price, without arbitrary rounding");
            AssertEqual(11m, cloud.Qualified.Volume, "First qualification precedes final total");
            AssertEqual(new DateTime(2026, 3, 16, 9, 23, 33), cloud.Qualified.Time, "Exact qualification timestamp");
            AssertEqual(6L, cloud.Qualified.SourceSequence, "Equal-time qualification ordinal");
            AssertEqual(7L, cloud.LastSourceSequence, "Decreasing technical ID does not change row order");
            string prefix = JsonSerializer.Serialize(cloud.Qualified);
            request = CloudRequest(root, rows.Concat(new[] { "20260316,092334,31280,100,Buy,1,future" }).ToArray());
            request.Cloud.MinimumSumVolume = 10;
            request.Cloud.MaximumRangeTicks = 60;
            AssertEqual(prefix, JsonSerializer.Serialize(Replay(request).Clouds.Single().Qualified), "Future suffix cannot rewrite qualified snapshot");
            request = CloudRequest(root, rows);
            request.Cloud.MinimumSumVolume = 10;
            request.Cloud.MaximumRangeTicks = 60;
            request.Cloud.MinimumTickVolume = 2;
            AssertEqual(0, Replay(request).Clouds.Count, "ADR tick filter yields volume9 below10");
            request.Cloud.MinimumTickVolume = 1;
            request.Cloud.MaximumRangeTicks = 59;
            AssertEqual(0, Replay(request).Clouds.Count, "ADR full range exceeding by1 splits both below10");
        }

        private static void TestCloudPeriodAndDecimals(string root)
        {
            DateTime day = Start.Date;
            foreach (decimal step in new[] { 0.00001m, 5m })
            {
                OrderFlowResearchRequest request = CloudRequest(root,
                    Row(day.AddTicks(-10), 10 * step, 999, Side.Buy),
                    Row(day, 10 * step, 10, Side.Buy), Row(day, 11 * step, 20, Side.Sell),
                    Row(day.AddTicks(10), 12 * step, 30, Side.Buy), Row(day.AddDays(1), 13 * step, 999, Side.Buy));
                request.PriceStep = step;
                request.FromDate = request.ToDate = day;
                request.Cloud.MaximumGapMilliseconds = 0;
                request.Cloud.MaximumRangeTicks = 1;
                OrderFlowResearchResult result = Replay(request);
                AssertEqual(5L, result.Input.RecordCount, "Whole file validated");
                AssertEqual(3L, result.Quality.DealCount, "Only selected ticks accumulate");
                AssertEqual(2, result.Clouds.Count, "Zero gap requires equal timestamps");
                AssertEqual(30m, result.Clouds[0].Volume, "Decimal range equality included");
                AssertFalse(result.Clouds[1].CompletedAt.HasValue, "Excluded future tick cannot close chain");
                AssertEqual(3L, result.Clouds[0].LastSourceSequence, "Original file sequence retained across selected period");
            }
        }

        private static void TestIndependentCalculations(string root)
        {
            OrderFlowResearchRequest deltaRequest = Request(root, BasicTicks());
            OrderFlowResearchResult delta = Replay(deltaRequest);
            OrderFlowResearchRequest bothRequest = Request(root, BasicTicks());
            bothRequest.CalculateCloud = true;
            OrderFlowResearchResult both = Replay(bothRequest);
            OrderFlowResearchRequest cloudRequest = CloudRequest(root, BasicTicks());
            cloudRequest.Cloud.MinimumSumVolume = bothRequest.Cloud.MinimumSumVolume;
            cloudRequest.FeatureWindowSeconds = -1;
            cloudRequest.LabelHorizonsSeconds = null;
            OrderFlowResearchResult onlyCloud = Replay(cloudRequest);
            AssertEqual(delta.FeatureHash, both.FeatureHash, "Adding Cloud does not change delta features");
            AssertEqual(delta.Candidates.Count, both.Candidates.Count, "Adding Cloud does not change candidates");
            AssertEqual(JsonSerializer.Serialize(delta.Labels), JsonSerializer.Serialize(both.Labels), "Future labels unchanged");
            AssertEqual(both.CloudHash, onlyCloud.CloudHash, "Cloud independent of delta");
            AssertEqual(delta.NormalizedEventHash, both.NormalizedEventHash, "One shared source order");
            AssertEqual(0, onlyCloud.Observations.Count + onlyCloud.Candidates.Count + onlyCloud.Labels.Count, "Disabled delta produces no delta research");
            AssertTrue(onlyCloud.Bars.Values.All(bars => bars.Count > 0), "Cloud-only still produces OHLC");
            AssertEqual(0, delta.Clouds.Count, "Disabled Cloud empty");
            AssertFalse(delta.ResearchSpecHash == both.ResearchSpecHash || both.ResearchSpecHash == onlyCloud.ResearchSpecHash, "Calculation choices are provenance");
            cloudRequest.CalculateCloud = false;
            Expect<ArgumentException>(() => cloudRequest.Validate());
        }

        private static void TestTechnicalIdsIgnored(string root)
        {
            OrderFlowResearchRequest request = CloudRequest(root, BasicTicks());
            request.CalculateDelta = true;
            OrderFlowResearchResult original = Export(request);
            string[] changed = File.ReadAllLines(request.TicksFilePath).Select((line, index) =>
                line.Substring(0, line.LastIndexOf(',') + 1) + (index % 2 == 0 ? "ignored;technical|value" : "")).ToArray();
            File.WriteAllLines(request.TicksFilePath, changed);
            OrderFlowResearchResult other = Export(request);
            AssertEqual(original.NormalizedEventHash, other.NormalizedEventHash, "ID-only changes do not alter decoded events");
            AssertEqual(original.FeatureHash, other.FeatureHash, "ID-only changes do not alter features");
            AssertEqual(original.CloudHash, other.CloudHash, "ID-only changes do not alter Cloud digest");
            AssertEqual(JsonSerializer.Serialize(original.Clouds), JsonSerializer.Serialize(other.Clouds), "Cloud snapshots ignore IDs entirely");
            AssertEqual(original.Candidates.Count, other.Candidates.Count, "Candidate calculation independent of IDs");
            AssertFalse(original.Input.Sha256 == other.Input.Sha256, "Raw source fingerprint still identifies every file byte");
            foreach (string path in Directory.GetFiles(other.ArtifactDirectory))
            {
                AssertFalse(File.ReadAllText(path).Contains("ignored;technical|value"), "Input IDs are not exported");
            }
        }

        private static void TestCloudArtifacts(string root)
        {
            OrderFlowResearchRequest request = CloudRequest(root, BasicTicks());
            OrderFlowResearchResult result = Export(request);
            string original = File.ReadAllText(Path.Combine(result.ArtifactDirectory, "clouds.csv"));
            AssertEqual(result.ArtifactDirectory, Export(request).ArtifactDirectory, "Cloud bundle deterministic reuse");
            AssertTrue(original.Contains("completed_at") && original.Contains("OpenAtEnd"), "Completion boundary persisted");
            JsonElement manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(result.ArtifactDirectory, "manifest.json"))).RootElement;
            AssertFalse(manifest.GetProperty("CalculateDelta").GetBoolean(), "Disabled mode recorded");
            AssertEqual(result.CloudHash, manifest.GetProperty("CloudHash").GetString(), "Cloud digest recorded");
            request.Cloud.MaximumGapMilliseconds++;
            AssertFalse(result.ResearchSpecHash == Export(request).ResearchSpecHash, "Cloud filters enter immutable identity");
            AssertEqual(original, File.ReadAllText(Path.Combine(result.ArtifactDirectory, "clouds.csv")), "Prior bundle unchanged");
        }

        #endregion

        #region Shared chart

        private static void TestCloudChart(string root)
        {
            OrderFlowResearchRequest request = CloudRequest(root, BasicTicks());
            request.CalculateDelta = true;
            OrderFlowResearchResult result = Replay(request);
            string original = JsonSerializer.Serialize(result);
            OrderFlowResearchChart chart = new OrderFlowResearchChart();
            chart.SetResult(result);
            foreach (bool delta in new[] { true, false })
            foreach (bool cloud in new[] { true, false })
            foreach (OrderFlowDisplayTimeFrame frame in Enum.GetValues<OrderFlowDisplayTimeFrame>())
            {
                chart.SetLayers(delta, cloud);
                chart.SetTimeFrame(frame);
                chart.SelectCloud(result.Clouds.Last().CloudId);
                chart.Measure(new Size(1000, 500));
                chart.Arrange(new Rect(0, 0, 1000, 500));
                chart.UpdateLayout();
                RenderTargetBitmap bitmap = new RenderTargetBitmap(1000, 500, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(chart);
                System.Collections.ICollection hits = (System.Collections.ICollection)typeof(OrderFlowResearchChart)
                    .GetField("_cloudHits", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(chart);
                AssertEqual(cloud ? result.Clouds.Count : 0, hits.Count, "Cloud layer rendered or hidden for " + frame);
                if (cloud)
                {
                    List<(Point Center, double Radius, OrderFlowCloud Cloud)> circles =
                        (List<(Point, double, OrderFlowCloud)>)hits;
                    Rect clip = (Rect)typeof(OrderFlowResearchChart).GetField("_cloudPlotBounds", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(chart);
                    (Point Center, double Radius, OrderFlowCloud Cloud) top = circles.OrderBy(item => item.Center.Y).First();
                    Point visible = new Point(top.Center.X, clip.Top + 1);
                    Point hidden = new Point(top.Center.X, clip.Top - 1);
                    AssertTrue((hidden - top.Center).Length < top.Radius, "Regression point lies in unclipped circle");
                    AssertTrue(chart.CloudAt(visible) != null, "Visible part owns tooltip");
                    AssertTrue(chart.CloudAt(hidden) == null, "Clipped-off part cannot own tooltip");
                }

            }
            AssertEqual(original, JsonSerializer.Serialize(result), "All layer/timeframe changes leave results untouched");
            AssertTrue(OrderFlowResearchChart.CloudDetails(result.Clouds.Last()).Contains("OpenAtEnd"), "Tooltip shows trailing-chain boundary");
            XDocument ui;
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Research.Ui.xaml")) { ui = XDocument.Load(stream); }
            XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            XElement settings = ui.Descendants(ns + "TabControl").Single(item => (string)item.Attribute("Name") == "TabControlSettings");
            AssertEqual(4, settings.Elements(ns + "TabItem").Count(), "Four settings tabs");
            AssertEqual(1, ui.Descendants(ns + "ContentControl").Count(item => (string)item.Attribute("Name") == "ContentControlChart"), "One shared chart host");
            AssertTrue(Application.Current == null, "No application started");
        }

        #endregion
    }
}
