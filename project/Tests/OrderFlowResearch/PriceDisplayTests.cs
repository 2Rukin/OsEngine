/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.Entity;
using OsEngine.OsData.OrderFlow;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace OsEngine.OrderFlowResearch.Tests
{
    internal static partial class Program
    {
        private static void TestPriceDisplayModes(string root)
        {
            OrderFlowResearchResult result = new OrderFlowResearchResult { DeltaCalculated = false };
            result.Bars.Add(OrderFlowDisplayTimeFrame.Min1, new List<OrderFlowDisplayBar>
            {
                ChartMinute(Start, 100, 110, 90, 105, 1, 0, 0),
                ChartMinute(Start.AddMinutes(1), 105, 111, 94, 99, 1, 0, 0),
                ChartMinute(Start.AddMinutes(2), 99, 108, 95, 106, 1, 0, 0)
            });
            string before = JsonSerializer.Serialize(result);
            OrderFlowResearchChart chart = new OrderFlowResearchChart(); chart.SetResult(result);
            foreach (OrderFlowPriceDisplay mode in Enum.GetValues<OrderFlowPriceDisplay>())
            {
                chart.SetPriceDisplay(mode); RenderDrawingChart(chart);
                List<GeometryDrawing> geometry = EnumerateGeometry(VisualTreeHelper.GetDrawing(chart)).ToList();
                int rectangles = geometry.Count(item => item.Geometry is RectangleGeometry);
                AssertEqual(mode == OrderFlowPriceDisplay.Candles ? 4 : 1, rectangles, "Only candles have filled price bodies");
                if (mode == OrderFlowPriceDisplay.Bars)
                {
                    AssertEqual(9, geometry.Count(item => item.Geometry is LineGeometry && item.Pen?.Thickness == 1.2), "OHLC draws stem and open/close ticks for each bar");
                }
                if (mode == OrderFlowPriceDisplay.MutedHighLow)
                {
                    AssertEqual(2, geometry.Count(item => item.Geometry is StreamGeometry && item.Pen?.Brush is SolidColorBrush brush && brush.Color == Color.FromArgb(105, 145, 145, 145)), "Ordinary grey view shares exact Cloud background");
                    AssertFalse(ContainsPen(VisualTreeHelper.GetDrawing(chart), Colors.DeepSkyBlue, 2), "Grey view does not enable Cloud path");
                }
                if (mode == OrderFlowPriceDisplay.HighLow)
                {
                    AssertEqual(2, geometry.Count(item => item.Geometry is StreamGeometry && item.Pen?.Thickness == 1.3), "Separate high and low paths");
                }
            }
            chart.SetCloudPriceMode(true); RenderDrawingChart(chart);
            AssertFalse(ContainsPen(VisualTreeHelper.GetDrawing(chart), Colors.DeepSkyBlue, 2), "Missing Clouds never fabricate a path");
            AssertEqual(before, JsonSerializer.Serialize(result), "Price view is display-only");
        }

        private static void TestCloudPricePathRange(string root)
        {
            List<OrderFlowCloud> clouds = new List<OrderFlowCloud>
            {
                new OrderFlowCloud { Time = Start, Price = 100 },
                new OrderFlowCloud { Time = Start.AddMinutes(10), Price = 105 },
                new OrderFlowCloud { Time = Start.AddMinutes(10), Price = 95 },
                new OrderFlowCloud { Time = Start.AddMinutes(30), Price = 110 }
            };
            AssertEqual((0, 2), OrderFlowResearchChart.CloudPathRange(clouds, Start.AddMinutes(2), Start.AddMinutes(8)), "Empty viewport connects nearest outside vertices");
            AssertEqual((0, 4), OrderFlowResearchChart.CloudPathRange(clouds, Start.AddMinutes(10), Start.AddMinutes(11)), "Equal-time vertices retained in order");
            AssertEqual((0, 1), OrderFlowResearchChart.CloudPathRange(clouds, Start.AddHours(-2), Start.AddHours(-1)), "Before history only first neighbor");
            AssertEqual((3, 4), OrderFlowResearchChart.CloudPathRange(clouds, Start.AddHours(1), Start.AddHours(2)), "After history only last neighbor");
            AssertEqual((0, 0), OrderFlowResearchChart.CloudPathRange(new List<OrderFlowCloud>(), Start, Start.AddHours(1)), "Empty Clouds");
        }

        private static void TestCloudPriceRendering(string root)
        {
            OrderFlowResearchRequest request = CloudRequest(root,
                Row(Start, 100, 1, Side.Buy), Row(Start, 105, 1, Side.Buy),
                Row(Start.AddMinutes(10), 95, 1, Side.Sell), Row(Start.AddMinutes(30), 110, 1, Side.Buy));
            request.Cloud.MaximumRangeTicks = 0;
            OrderFlowResearchResult result = Export(request);
            AssertEqual(4, result.Clouds.Count, "Cloud fixture vertices");
            string before = JsonSerializer.Serialize(result);
            OrderFlowResearchChart chart = new OrderFlowResearchChart(); chart.SetResult(result); chart.SetCloudPriceMode(true);
            chart.SetLayers(false, true);
            foreach (OrderFlowDisplayTimeFrame frame in OrderFlowChartTimeFrames.GetMenuValues())
            {
                chart.SetTimeFrame(frame); chart.Zoom(chart.TotalBars); RenderDrawingChart(chart);
                List<GeometryDrawing> geometry = EnumerateGeometry(VisualTreeHelper.GetDrawing(chart)).ToList();
                List<LineGeometry> segments = geometry.Where(item => item.Pen?.Brush is SolidColorBrush brush && brush.Color == Colors.DeepSkyBlue && item.Pen.Thickness == 2)
                    .Select(item => item.Geometry).OfType<LineGeometry>().ToList();
                AssertEqual(3, segments.Count, "Every Cloud-to-Cloud segment survives timeframe " + frame);
                AssertTrue(Math.Abs(segments[0].StartPoint.X - segments[0].EndPoint.X) < 0.000001, "Equal timestamps keep vertical segment");
                AssertEqual(2, geometry.Count(item => item.Geometry is StreamGeometry && item.Pen?.Brush is SolidColorBrush brush && brush.Color.A == 105), "Muted high/low background");
                AssertEqual(4, geometry.Count(item => item.Geometry is EllipseGeometry && item.Brush is SolidColorBrush brush && brush.Color == Colors.DeepSkyBlue), "Cloud vertices not grouped by candle");
            }
            chart.SetCloudPriceMode(false); chart.SetPriceDisplay(OrderFlowPriceDisplay.Bars); RenderDrawingChart(chart);
            AssertFalse(ContainsPen(VisualTreeHelper.GetDrawing(chart), Colors.DeepSkyBlue, 2), "Turning mode off restores normal view");
            AssertEqual(before, JsonSerializer.Serialize(result), "Cloud path does not change data, source order or hashes");
            AssertEqual(result.ArtifactDirectory, new OrderFlowResearchArtifactWriter().Write(request, result), "Cloud view never changes export");
        }

        private static IEnumerable<GeometryDrawing> EnumerateGeometry(Drawing drawing)
        {
            if (drawing is GeometryDrawing geometry) { yield return geometry; }
            if (drawing is DrawingGroup group)
            {
                foreach (Drawing child in group.Children)
                foreach (GeometryDrawing item in EnumerateGeometry(child)) { yield return item; }
            }
        }
    }
}
