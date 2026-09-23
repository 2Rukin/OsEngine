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
            string[] names = { "SliderCloudContrast", "SliderChartCloudContrast", "TextBlockChartCloudContrast" };
            FrameworkElement content = (FrameworkElement)XamlReader.Parse(new XElement(ns + "StackPanel", names.Select(name =>
                new XElement(ui.Descendants().Single(element => (string)element.Attribute("Name") == name)))).ToString());
            Slider primary = (Slider)content.FindName(names[0]); Slider mirror = (Slider)content.FindName(names[1]);
            TextBlock text = (TextBlock)content.FindName(names[2]);
            OrderFlowChartHost.BindVisualSlider(mirror, primary, text, "{0:F1}");
            ContentControl first = new ContentControl { Content = content }; ContentControl second = new ContentControl();
            using (OrderFlowChartHost host = new OrderFlowChartHost(first))
            {
                AssertEqual(2d, primary.Value, "Default contrast");
                AssertEqual(0.5, primary.Minimum, "Minimum contrast"); AssertEqual(10d, primary.Maximum, "Maximum contrast");
                host.MoveTo(second); mirror.Value = 2.5;
                AssertEqual(2.5, primary.Value, "Separate window still changes primary event source");
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.DataBind, new Action(() => { }));
                AssertTrue(text.Text.Contains("2"), "Readout stays bound after transfer");
                host.Restore(); primary.Value = 0.5;
                AssertEqual(0.5, mirror.Value, "Return retains two-way binding");
            }
        }
    }
}
