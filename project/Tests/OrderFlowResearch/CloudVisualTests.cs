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
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;

namespace OsEngine.OrderFlowResearch.Tests
{
    internal static partial class Program
    {
        private static void TestCloudVolumeContrast(string root)
        {
            OrderFlowResearchResult result = new OrderFlowResearchResult();
            result.Clouds.Add(new OrderFlowCloud { PriceStep = 1, BuyVolume = 1000 });
            result.Clouds.Add(new OrderFlowCloud { PriceStep = 1, BuyVolume = 500 });
            string before = JsonSerializer.Serialize(result);
            OrderFlowResearchChart chart = new OrderFlowResearchChart(); chart.SetResult(result);
            AssertEqual(750m, chart.CloudReferenceVolume, "Even median without source sorting");
            double contrastRatio = chart.CloudVolumeRadius(1000) / chart.CloudVolumeRadius(500);
            AssertTrue(contrastRatio > 2, "Default makes 1000 noticeably larger than 500");
            AssertTrue(chart.CloudVolumeRadius(10000) > chart.CloudVolumeRadius(2000), "Volumes above old ceiling remain distinguishable");
            double originalRadius = chart.CloudVolumeRadius(1000);
            chart.SetCloudScale(3);
            AssertTrue(Math.Abs(chart.CloudVolumeRadius(1000) - originalRadius * 3) < 1e-9, "Overall size remains linear multiplier");
            AssertTrue(Math.Abs(chart.CloudVolumeRadius(1000) / chart.CloudVolumeRadius(500) - contrastRatio) < 1e-9, "Size cannot change relative contrast");
            chart.SetCloudContrast(0.5);
            AssertTrue(chart.CloudVolumeRadius(1000) / chart.CloudVolumeRadius(500) < contrastRatio, "Separate contrast changes ratio");
            chart.SetTimeFrame(OrderFlowDisplayTimeFrame.Week1); chart.Zoom(1000); chart.ScrollTo(100);
            AssertEqual(750m, chart.CloudReferenceVolume, "Reference independent of viewport and timeframe");
            AssertEqual(before, JsonSerializer.Serialize(result), "Sorting and styling never mutate source data");
            result.Clouds.Add(new OrderFlowCloud { PriceStep = 1, BuyVolume = 2000 }); chart.SetResult(result);
            AssertEqual(1000m, chart.CloudReferenceVolume, "Odd median");
            result.Clouds.Clear(); chart.SetResult(result); AssertEqual(1m, chart.CloudReferenceVolume, "Empty reference");
            result.Clouds.Add(new OrderFlowCloud { PriceStep = 1, BuyVolume = 0.0000000000000000000000000001m }); chart.SetResult(result);
            chart.SetCloudContrast(10);
            AssertTrue(double.IsFinite(chart.CloudVolumeRadius(decimal.MaxValue)), "Full decimal ratio never overflows decimal division");
            result.Clouds.Add(new OrderFlowCloud { PriceStep = 1, BuyVolume = decimal.MaxValue });
            result.Clouds.Add(new OrderFlowCloud { PriceStep = 1, BuyVolume = decimal.MaxValue });
            result.Clouds.Add(new OrderFlowCloud { PriceStep = 1, BuyVolume = decimal.MaxValue }); chart.SetResult(result);
            AssertEqual(decimal.MaxValue, chart.CloudReferenceVolume, "Even median does not add two maximal decimals");
            Expect<ArgumentOutOfRangeException>(() => chart.SetCloudContrast(0.4));
            Expect<ArgumentOutOfRangeException>(() => chart.SetCloudContrast(double.PositiveInfinity));
        }

        private static void TestSharedCloudAppearance(string root)
        {
            OrderFlowResearchRequest request = TwoCloudRequest(root);
            request.CloudLayers = new List<OrderFlowCloudLayer>
            {
                new OrderFlowCloudLayer { Name = "Added chain", Settings = new OrderFlowCloudSettings { MinimumSumVolume = 1 } },
                new OrderFlowCloudLayer { Name = "Added single", Settings = new OrderFlowCloudSettings { SingleTicks = true, MinimumTickVolume = 500 } }
            };
            RecordingReplay observer = new RecordingReplay();
            OrderFlowResearchResult result = new OrderFlowResearchEngine().Run(request, System.Threading.CancellationToken.None, observer);
            string before = JsonSerializer.Serialize(result);
            OrderFlowResearchChart chart = new OrderFlowResearchChart(); chart.SetResult(result); chart.SetLayers(false, true, true);
            foreach (bool replay in new[] { false, true })
            {
                if (replay) { chart.BeginReplay(request.Cloud.MinimumSumVolume, request.Cloud2.MinimumTickVolume); chart.ApplyReplayFrame(observer.Frames.Last().Result); }
                chart.SetCloudScale(1); chart.SetCloudContrast(2); RenderDrawingChart(chart);
                List<(Point Center, double Radius, OrderFlowCloud Cloud)> added = CalField<List<(Point, double, OrderFlowCloud)>>(chart, "_additionalCloudHits");
                List<(Point Center, double Radius, OrderFlowCloud Cloud)>[] groups = { CloudHits(chart, false), CloudHits(chart, true), added };
                Dictionary<string, double> radii = groups.SelectMany(g => g).ToDictionary(h => h.Cloud.CloudId, h => h.Radius);
                AssertTrue(groups.All(g => g.Count > 0), "Legacy and added layers render in history/replay");
                AssertTrue(added.Any(h => h.Cloud.TradeCount == 1) && added.Any(h => h.Cloud.TradeCount > 1), "Both added marker shapes covered");
                chart.SetCloudScale(2);
                AssertTrue(groups.All(g => g.Count == 0), "Shared size invalidates every old hit list");
                RenderDrawingChart(chart);
                foreach ((Point center, double radius, OrderFlowCloud cloud) in groups.SelectMany(g => g))
                { AssertTrue(Math.Abs(radius - radii[cloud.CloudId] * 2) < 1e-8, "Shared size scales every actual painted/hit radius"); }
                radii = groups.SelectMany(g => g).ToDictionary(h => h.Cloud.CloudId, h => h.Radius);
                chart.SetCloudContrast(4);
                AssertTrue(groups.All(g => g.Count == 0), "Shared contrast invalidates every old hit list");
                RenderDrawingChart(chart);
                foreach (List<(Point Center, double Radius, OrderFlowCloud Cloud)> group in groups)
                { AssertTrue(group.Any(h => Math.Abs(h.Radius - radii[h.Cloud.CloudId]) > 1e-8), "Shared contrast changes each legacy/added group"); }
                if (replay) { chart.EndReplay(); }
            }
            AssertEqual(before, JsonSerializer.Serialize(result), "Shared controls and replay never modify research results");
            foreach (OrderFlowReplayFrame frame in observer.Frames)
            { AssertEqual(observer.Frozen[observer.Frames.IndexOf(frame)], JsonSerializer.Serialize(frame), "Rendered replay snapshots stay immutable"); }
        }

        private static void TestCloudVolumeLabels(string root)
        {
            OrderFlowResearchResult result = new OrderFlowResearchEngine().Run(CloudRequest(root, Row(Start, 100, 500, Side.Buy)), System.Threading.CancellationToken.None);
            OrderFlowResearchChart chart = new OrderFlowResearchChart(); chart.SetResult(result); chart.SetLayers(false, true);
            chart.SetCloudScale(2); RenderDrawingChart(chart);
            AssertTrue(DrawingTexts(VisualTreeHelper.GetDrawing(chart)).Contains("500"), "Fitting circle shows exact volume by default");
            chart.SetCloudVolumeLabels(false); RenderDrawingChart(chart);
            AssertFalse(DrawingTexts(VisualTreeHelper.GetDrawing(chart)).Contains("500"), "Label toggle removes volume text");
            chart.SetCloudVolumeLabels(true); chart.SetCloudScale(0.1); RenderDrawingChart(chart);
            AssertFalse(DrawingTexts(VisualTreeHelper.GetDrawing(chart)).Contains("500"), "Tiny circles do not receive overflowing labels");
        }

        private static IEnumerable<string> DrawingTexts(Drawing drawing)
        {
            if (drawing is GlyphRunDrawing glyph) { yield return new string(glyph.GlyphRun.Characters.ToArray()); }
            if (drawing is DrawingGroup group)
            {
                foreach (Drawing child in group.Children) { foreach (string text in DrawingTexts(child)) { yield return text; } }
            }
        }

        private static void TestCloudContrastBindings(string root)
        {
            XDocument ui;
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Research.Ui.xaml")) { ui = XDocument.Load(stream); }
            XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            string[] names = { "SliderChartCloudContrast", "TextBlockChartCloudContrast" };
            FrameworkElement content = (FrameworkElement)XamlReader.Parse(new XElement(ns + "StackPanel", names.Select(name =>
                new XElement(ui.Descendants().Single(element => (string)element.Attribute("Name") == name)))).ToString());
            Slider primary = (Slider)content.FindName(names[0]);
            TextBlock text = (TextBlock)content.FindName(names[1]);
            OrderFlowChartHost.BindVisualReadout(primary, text, "{0:F1}");
            ContentControl first = new ContentControl { Content = content }; ContentControl second = new ContentControl();
            using (OrderFlowChartHost host = new OrderFlowChartHost(first))
            {
                AssertEqual(2d, primary.Value, "Default contrast");
                AssertEqual(0.5, primary.Minimum, "Minimum contrast"); AssertEqual(10d, primary.Maximum, "Maximum contrast");
                host.MoveTo(second); primary.Value = 2.5;
                AssertEqual(2.5, primary.Value, "Detached slider retains value");
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.DataBind, new Action(() => { }));
                AssertTrue(text.Text.Contains("2"), "Readout stays bound after transfer");
                host.Restore(); primary.Value = 0.5;
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.DataBind, new Action(() => { }));
                AssertTrue(text.Text.EndsWith("5"), "Readout stays bound after return");
            }
        }
    }
}
