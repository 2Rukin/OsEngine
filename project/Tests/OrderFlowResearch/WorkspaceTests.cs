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
        #region Second Cloud

        private static OrderFlowResearchRequest TwoCloudRequest(string root)
        {
            OrderFlowResearchRequest request = CloudRequest(root,
                Row(Start, 100, 499, Side.Buy), Row(Start, 101, 500, Side.Sell),
                Row(Start, 102, 1000, Side.Buy), Row(Start.AddSeconds(1), 101, 200, Side.Buy),
                Row(Start.AddMinutes(1), 102, 500, Side.Buy));
            request.CalculateCloud2 = true;
            request.Cloud2.MinimumTickVolume = 500;
            return request;
        }

        private static void TestSecondCloudTicks(string root)
        {
            OrderFlowResearchRequest request = TwoCloudRequest(root);
            request.Cloud2.MinimumSumVolume = -1;
            request.Cloud2.MaximumRangeTicks = request.Cloud2.MaximumGapMilliseconds = -1;
            OrderFlowResearchResult result = Replay(request);
            AssertTrue(result.Quality.ResearchAccepted, "Unused chain settings ignored in single mode");
            AssertEqual(3, result.Clouds2.Count, "Only physical ticks at inclusive threshold");
            AssertEqual(500m, result.Clouds2[0].Volume, "No accumulation of filtered ticks");
            AssertEqual(1000m, result.Clouds2[1].Volume, "Same time and repeated technical ID still separate");
            foreach (OrderFlowCloud cloud in result.Clouds2)
            {
                AssertEqual(1, cloud.TradeCount, "Exactly one physical row");
                AssertEqual("SingleTick", cloud.CompletionReason, "Immediate completion including EOF");
                AssertEqual(cloud.Time, cloud.CompletedAt.Value, "Completion requires no later tick");
                AssertEqual(cloud.LastSourceSequence, cloud.CompletionSourceSequence.Value, "Own completion sequence");
                AssertEqual(cloud.LastSourceSequence, cloud.Qualified.SourceSequence, "Own qualification sequence");
                AssertTrue(cloud.CloudId.StartsWith("CL2-"), "Layer ID namespace");
            }
            string originalSpec = result.ResearchSpecHash;
            request.Cloud2.MinimumSumVolume = 123;
            request.Cloud2.MaximumGapMilliseconds = 987;
            AssertEqual(originalSpec, Replay(request).ResearchSpecHash, "Inactive parameters do not change effective spec");
            request.Cloud2.SingleTicks = false;
            request.Cloud2.MinimumSumVolume = 1000;
            request.Cloud2.MaximumGapMilliseconds = 1000;
            request.Cloud2.MaximumRangeTicks = 5;
            OrderFlowResearchResult chain = Replay(request);
            AssertEqual(1, chain.Clouds2.Count, "Cloud 2 also supports independent chains");
            AssertEqual(1500m, chain.Clouds2[0].Volume, "Chain combines eligible neighboring ticks");
            AssertFalse(originalSpec == chain.ResearchSpecHash, "Mode belongs to identity");
        }

        private static void TestSecondCloudCombinations(string root)
        {
            OrderFlowResearchResult all = null;
            for (int modes = 7; modes > 0; modes--)
            {
                OrderFlowResearchRequest request = TwoCloudRequest(root);
                request.CalculateDelta = (modes & 1) != 0;
                request.CalculateCloud = (modes & 2) != 0;
                request.CalculateCloud2 = (modes & 4) != 0;
                if (!request.CalculateCloud) { request.Cloud = null; }
                if (!request.CalculateCloud2) { request.Cloud2 = null; }
                if (!request.CalculateDelta) { request.FeatureWindowSeconds = -1; request.LabelHorizonsSeconds = null; }
                OrderFlowResearchResult result = Replay(request);
                AssertTrue(result.Quality.ResearchAccepted, "All seven nonempty combinations accepted: " + modes);
                all ??= result;
                AssertEqual(all.NormalizedEventHash, result.NormalizedEventHash, "Same reader and physical sequence");
                if (request.CalculateCloud) { AssertEqual(all.CloudHash, result.CloudHash, "Cloud 1 independence"); }
                else { AssertEqual(0, result.Clouds.Count, "Disabled Cloud 1 empty"); }
                if (request.CalculateCloud2) { AssertEqual(all.Cloud2Hash, result.Cloud2Hash, "Cloud 2 independence"); }
                else { AssertEqual(0, result.Clouds2.Count, "Disabled Cloud 2 empty"); }
                if (request.CalculateDelta)
                {
                    AssertEqual(all.FeatureHash, result.FeatureHash, "Delta features independent of both Clouds");
                    AssertEqual(JsonSerializer.Serialize(all.Labels), JsonSerializer.Serialize(result.Labels), "Labels unaffected");
                }
            }
        }

        private static void TestSecondCloudArtifacts(string root)
        {
            OrderFlowResearchRequest request = TwoCloudRequest(root);
            OrderFlowResearchResult result = Export(request);
            string csv = File.ReadAllText(Path.Combine(result.ArtifactDirectory, "clouds2.csv"));
            AssertEqual(4, File.ReadAllLines(Path.Combine(result.ArtifactDirectory, "clouds2.csv")).Length, "Header plus three single trades");
            AssertTrue(csv.Contains("SingleTick") && csv.Contains("CL2-"), "Separate CSV carries completion evidence");
            JsonElement manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(result.ArtifactDirectory, "manifest.json"))).RootElement;
            AssertTrue(manifest.GetProperty("CalculateCloud2").GetBoolean(), "Enabled flag recorded");
            AssertEqual(3, manifest.GetProperty("Cloud2Count").GetInt32(), "Count recorded");
            AssertEqual(result.Cloud2Hash, manifest.GetProperty("Cloud2Hash").GetString(), "Hash recorded");
            AssertEqual(result.ArtifactDirectory, Export(request).ArtifactDirectory, "Identical two-layer artifact reused");
            request.Cloud2.MinimumTickVolume = 1000;
            OrderFlowResearchResult changed = Export(request);
            AssertFalse(result.ArtifactDirectory == changed.ArtifactDirectory, "Second threshold changes run identity");
            AssertEqual(result.CloudHash, changed.CloudHash, "First-layer hash unchanged");
            AssertEqual(csv, File.ReadAllText(Path.Combine(result.ArtifactDirectory, "clouds2.csv")), "Prior artifact immutable");
            File.AppendAllText(request.TicksFilePath, "invalid row\n");
            OrderFlowResearchResult rejected = Export(request);
            AssertFalse(rejected.Quality.ResearchAccepted, "Malformed suffix rejects both-layer research");
            AssertTrue(File.Exists(Path.Combine(rejected.ArtifactDirectory, "clouds2.csv")), "Rejected audit retains separate prefix evidence");
            using (CancellationTokenSource cancel = new CancellationTokenSource())
            {
                cancel.Cancel();
                Expect<OperationCanceledException>(() => new OrderFlowResearchEngine().Run(request, cancel.Token));
            }
        }

        private static void TestSecondCloudReplay(string root)
        {
            OrderFlowResearchRequest request = TwoCloudRequest(root);
            RecordingReplay observer = new RecordingReplay();
            OrderFlowResearchResult result = new OrderFlowResearchEngine().Run(request, CancellationToken.None, observer);
            AssertEqual(JsonSerializer.Serialize(Replay(request)), JsonSerializer.Serialize(result), "Observer has identical final two-layer output");
            AssertEqual(0, observer.Frames[0].Result.Clouds2.Count, "Filtered tick contributes no second-layer Cloud");
            AssertEqual("SingleTick", observer.Frames[1].Result.Clouds2.Single().CompletionReason, "Single is final immediately");
            AssertEqual("Forming", observer.Frames[1].Result.Clouds.Single().CompletionReason, "First chain can still be forming");
            for (int index = 0; index < observer.Frames.Count; index++)
            { AssertEqual(observer.Frozen[index], JsonSerializer.Serialize(observer.Frames[index]), "Both published Cloud lists stay frozen"); }
            request.CalculateCloud = false;
            result = Replay(request);
            using (OrderFlowReplaySession session = new OrderFlowReplaySession(request, result.Input.Sha256, 1, true))
            {
                AssertEqual(1m, session.CloudReferenceVolume, "Cloud 2-only null first reference safe");
                AssertEqual(500m, session.Cloud2ReferenceVolume, "Second reference is known single-tick threshold");
                OrderFlowResearchChart chart = new OrderFlowResearchChart(); chart.SetResult(result);
                foreach (OrderFlowDisplayTimeFrame frame in OrderFlowChartTimeFrames.GetMenuValues())
                {
                    chart.SetTimeFrame(frame); chart.BeginReplay(session.CloudReferenceVolume, session.Cloud2ReferenceVolume);
                    chart.ApplyReplayFrame(observer.Frames[2].Result); RenderDrawingChart(chart);
                    AssertEqual(500m, chart.Cloud2ReferenceVolume, "No future median in prefix");
                    chart.EndReplay();
                    AssertEqual(500m, chart.Cloud2ReferenceVolume, "History median restored independently");
                }
            }
        }

        private static List<(Point Center, double Radius, OrderFlowCloud Cloud)> CloudHits(OrderFlowResearchChart chart, bool second)
        {
            return (List<(Point, double, OrderFlowCloud)>)typeof(OrderFlowResearchChart)
                .GetField(second ? "_cloudHits2" : "_cloudHits", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(chart);
        }

        private static void TestSecondCloudDisplay(string root)
        {
            OrderFlowResearchResult result = Replay(TwoCloudRequest(root));
            string before = JsonSerializer.Serialize(result);
            OrderFlowResearchChart chart = new OrderFlowResearchChart(); chart.SetResult(result); chart.SetCloudPriceMode(true);
            foreach (bool first in new[] { false, true })
            foreach (bool second in new[] { false, true })
            {
                chart.SetLayers(false, first, second); RenderDrawingChart(chart);
                AssertEqual(first ? result.Clouds.Count : 0, CloudHits(chart, false).Count, "First circles/hits visibility");
                AssertEqual(second ? result.Clouds2.Count : 0, CloudHits(chart, true).Count, "Second circles/hits visibility");
                AssertEqual(first, ContainsPen(VisualTreeHelper.GetDrawing(chart), Colors.DeepSkyBlue, 2), "First path visibility");
                AssertEqual(second, ContainsPen(VisualTreeHelper.GetDrawing(chart), Colors.MediumOrchid, 2), "Second path visibility");
            }
            chart.SetLayers(false, true, true); RenderDrawingChart(chart);
            double firstRadius = chart.CloudVolumeRadius(1000);
            double secondRadius = chart.CloudVolumeRadius(1000, true);
            chart.SetCloudScale(3, true); chart.SetCloudContrast(10, true); RenderDrawingChart(chart);
            AssertEqual(firstRadius, chart.CloudVolumeRadius(1000), "Second style leaves first untouched");
            AssertTrue(chart.CloudVolumeRadius(1000, true) > secondRadius, "Second size/contrast applied");
            AssertTrue(chart.CloudAt(CloudHits(chart, true)[0].Center).CloudId.StartsWith("CL2-"), "Topmost second layer owns overlap hit");
            Expect<ArgumentOutOfRangeException>(() => chart.SetCloudContrast(10.01, true));
            Expect<ArgumentOutOfRangeException>(() => chart.SetCloudScale(3.01, true));
            AssertEqual(before, JsonSerializer.Serialize(result), "Visibility/styles leave both results immutable");
        }

        #endregion

        #region Workspace and drawing

        private static void TestWorkspacePadding(string root)
        {
            OrderFlowResearchResult result = Replay(TwoCloudRequest(root));
            OrderFlowResearchChart chart = new OrderFlowResearchChart(); chart.SetResult(result); chart.SetLayers(false, true, true);
            AssertEqual(5d, chart.RightPaddingPercent, "Default five percent");
            int total = chart.TotalBars;
            foreach (double margin in new[] { 0d, 5, 50, 90 })
            {
                chart.SetRightPadding(margin); RenderDrawingChart(chart);
                double end = 92 + 796 * (1 - margin / 100);
                AssertEqual(total, chart.TotalBars, "Padding never invents bars");
                AssertEqual(total - 1, chart.BarIndexAt(end - 0.001), "Last real bar before workspace");
                AssertEqual(-1, chart.BarIndexAt(end + 0.001), "Workspace has no candle tooltip");
                foreach ((Point center, double radius, OrderFlowCloud cloud) in CloudHits(chart, true))
                { AssertTrue(center.X <= end, "Cloud anchors use data width"); }
            }
            chart.SetDrawingTool(OrderFlowDrawingTool.Trend); chart.SetRightPadding(50); RenderDrawingChart(chart);
            chart.DrawingPointerDown(new Point(700, 200)); chart.DrawingPointerDown(new Point(800, 300));
            AssertEqual(1, chart.Drawings.Count, "Annotations can occupy blank right workspace");
            AssertEqual(OrderFlowDrawingTool.Trend, chart.DrawingTool, "Tool survives padding change and creation");
            foreach (double invalid in new[] { -1d, 90.1, double.NaN }) { Expect<ArgumentOutOfRangeException>(() => chart.SetRightPadding(invalid)); }
        }

        private static void TestWorkspaceGrid(string root)
        {
            decimal minimum = 1000000000000.00001m;
            List<decimal> prices = OrderFlowResearchChart.PriceGrid(minimum, minimum + 0.00009m, 450);
            AssertTrue(prices.Count >= 3 && prices.Count <= 11, "Readable nonempty decimal grid");
            AssertTrue(prices.All(price => price >= minimum && price <= minimum + 0.00009m), "Inside visible range");
            AssertTrue(prices.Distinct().Count() == prices.Count, "Tiny price distances at large base are not lost");
            OrderFlowResearchChart chart = new OrderFlowResearchChart(); chart.SetResult(Replay(TwoCloudRequest(root))); chart.SetLayers(false, false, false);
            RenderDrawingChart(chart);
            List<GeometryDrawing> lines = EnumerateGeometry(VisualTreeHelper.GetDrawing(chart))
                .Where(geometry => geometry.Pen?.Brush is SolidColorBrush brush && brush.Color.A == 70).ToList();
            AssertTrue(lines.Count >= 3, "Horizontal price grid actually rendered");
            AssertTrue(lines.All(line => line.Geometry.Bounds.Width == 796 && line.Geometry.Bounds.Height == 0), "Grid spans data and free workspace");
        }

        private static void TestDrawingBodyTranslation(string root)
        {
            OrderFlowResearchChart chart = new OrderFlowResearchChart(); chart.SetResult(Replay(TwoCloudRequest(root))); chart.SetLayers(false, false, false);
            RenderDrawingChart(chart); chart.SetDrawingTool(OrderFlowDrawingTool.Trend);
            chart.DrawingPointerDown(new Point(200, 200)); chart.DrawingPointerDown(new Point(600, 400)); RenderDrawingChart(chart);
            OrderFlowChartLine line = chart.Drawings.Single();
            decimal slopePrice = line.Second.Price - line.First.Price;
            long duration = line.Second.Time.Ticks - line.First.Time.Ticks;
            chart.DrawingPointerDown(new Point(400, 300)); chart.DrawingPointerMove(new Point(450, 325)); chart.CancelDrawingGesture(); RenderDrawingChart(chart);
            AssertEqual(slopePrice, line.Second.Price - line.First.Price, "Body drag keeps price difference");
            AssertEqual(duration, line.Second.Time.Ticks - line.First.Time.Ticks, "Uniform-grid body drag keeps time difference");
            AssertTrue(ReferenceEquals(line, chart.DrawingAt(new Point(450, 325))), "Body follows pointer exactly");
            AssertEqual(OrderFlowDrawingTool.Trend, chart.DrawingTool, "Last tool remains selected after drag");
            chart.DrawingPointerDown(new Point(250, 225)); chart.DrawingPointerMove(new Point(300, 200)); chart.CancelDrawingGesture();
            AssertFalse(slopePrice == line.Second.Price - line.First.Price, "Endpoint drag still reshapes line");
            RenderDrawingChart(chart); chart.DrawingPointerDown(new Point(220, 420)); chart.DrawingPointerDown(new Point(650, 450));
            AssertEqual(2, chart.Drawings.Count, "Next line without reselecting tool");
            chart.SetTimeFrame(OrderFlowDisplayTimeFrame.Day1); chart.SetLayers(true, true, true);
            AssertEqual(OrderFlowDrawingTool.Trend, chart.DrawingTool, "View changes preserve last tool");
        }

        private static void TestDrawingGapTranslation(string root)
        {
            List<OrderFlowDisplayBar> bars = new List<OrderFlowDisplayBar>
            {
                ChartMinute(Start, 1, 2, 1, 2, 1, 0, 0),
                ChartMinute(Start.AddDays(3), 1, 2, 1, 2, 1, 0, 0),
                ChartMinute(Start.AddDays(7), 1, 2, 1, 2, 1, 0, 0)
            };
            OrderFlowDrawingAnchor first = new OrderFlowDrawingAnchor(Start.AddSeconds(30), 100);
            OrderFlowDrawingAnchor second = new OrderFlowDrawingAnchor(Start.AddDays(3).AddSeconds(30), 110);
            (OrderFlowDrawingAnchor a, OrderFlowDrawingAnchor b) = OrderFlowDrawingGeometry.Translate(bars, first, second, 1, 5);
            AssertEqual(1d, OrderFlowDrawingGeometry.BarPosition(bars, b.Time) - OrderFlowDrawingGeometry.BarPosition(bars, a.Time), "Compressed-time gap preserves visual horizontal difference");
            AssertEqual(10m, b.Price - a.Price, "Compressed-time gap preserves price difference");
            AssertEqual(105m, a.Price, "Both prices translate");
            AssertEqual(Start.AddDays(3).AddSeconds(30), a.Time, "Move across closed-market gap uses next real bar");
            (a, b) = OrderFlowDrawingGeometry.Translate(bars, first, second, 5, 0);
            AssertEqual(1d, OrderFlowDrawingGeometry.BarPosition(bars, b.Time) - OrderFlowDrawingGeometry.BarPosition(bars, a.Time), "Future blank area preserves angle too");
        }

        private static void TestWorkspaceMarkup(string root)
        {
            XDocument ui;
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Research.Ui.xaml")) { ui = XDocument.Load(stream); }
            XElement surface = ui.Descendants().Single(element => (string)element.Attribute("Name") == "ScrollViewerChartSurface");
            foreach (string name in new[] { "CheckBoxChartShowCloud", "CheckBoxChartShowCloud2", "SliderChartCloud2Scale", "SliderChartCloud2Contrast", "SliderRightPadding" })
            { AssertTrue(surface.Descendants().Any(element => (string)element.Attribute("Name") == name), "Control moves with separate chart: " + name); }
            XElement margin = surface.Descendants().Single(element => (string)element.Attribute("Name") == "SliderRightPadding");
            AssertEqual("5", (string)margin.Attribute("Value"), "Visible default five");
            AssertTrue(ui.Descendants().Any(element => (string)element.Attribute("Name") == "CheckBoxCloud2SingleTicks" && (string)element.Attribute("IsChecked") == "True"), "Single ticks initially selected");
        }

        #endregion
    }
}
