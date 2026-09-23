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
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;

namespace OsEngine.OrderFlowResearch.Tests
{
    internal static partial class Program
    {
        private static void TestDrawingClipping(string root)
        {
            Rect plot = new Rect(0, 0, 100, 100);
            foreach (bool reverse in new[] { false, true })
            {
                Point first = new Point(20, 40);
                Point second = new Point(40, 60);
                if (reverse) { (first, second) = (second, first); }
                foreach (bool left in new[] { false, true })
                foreach (bool right in new[] { false, true })
                {
                    AssertTrue(OrderFlowDrawingGeometry.Clip(first, second, left, right, plot, out Point from, out Point to), "Visible segment/ray");
                    AssertPoint(left ? new Point(0, 20) : new Point(20, 40), from, "Chronological left extension");
                    AssertPoint(right ? new Point(80, 100) : new Point(40, 60), to, "Right ray clips on price boundary");
                }
            }
            AssertTrue(OrderFlowDrawingGeometry.Clip(new Point(-40, 50), new Point(-20, 50), false, true, plot, out Point start, out Point end), "Offscreen anchors can extend into viewport");
            AssertPoint(new Point(0, 50), start, "Ray entry"); AssertPoint(new Point(100, 50), end, "Ray exit");
            AssertTrue(OrderFlowDrawingGeometry.Clip(new Point(50, 20), new Point(50, 80), true, true, plot, out start, out end), "Collapsed time axis remains finite");
            AssertPoint(new Point(50, 20), start, "Vertical finite start"); AssertPoint(new Point(50, 80), end, "Vertical finite end");
            AssertFalse(OrderFlowDrawingGeometry.Clip(new Point(-1, 20), new Point(-1, 80), true, true, plot, out start, out end), "Outside vertical line rejected");
            AssertTrue(OrderFlowDrawingGeometry.Clip(new Point(30, 30), new Point(30, 30), true, true, plot, out start, out end), "Coincident points safe");
            AssertEqual(5d, OrderFlowDrawingGeometry.Distance(new Point(33, 34), start, end), "Zero-length distance");
        }

        private static void TestDrawingCoordinates(string root)
        {
            DateTime day = new DateTime(2028, 2, 28, 0, 0, 0, DateTimeKind.Utc);
            List<OrderFlowDisplayBar> bars = new List<OrderFlowDisplayBar>
            {
                ChartMinute(day, 1, 2, 1, 2, 1, 0, 0),
                ChartMinute(day.AddDays(1), 1, 2, 1, 2, 1, 0, 0),
                ChartMinute(day.AddDays(2), 1, 2, 1, 2, 1, 0, 0)
            };
            Rect plot = new Rect(92, 24, 800, 400);
            decimal min = 1000000000000.00001m;
            decimal max = min + 0.00004m;
            OrderFlowDrawingAnchor anchor = OrderFlowDrawingGeometry.AnchorAt(bars, 0, 3, plot, min, max, new Point(492, 224));
            AssertEqual(day.AddDays(1).AddSeconds(30), anchor.Time, "Pointer resolves inside source bar, not elapsed days");
            AssertEqual(DateTimeKind.Utc, anchor.Time.Kind, "Source clock kind");
            AssertEqual(min + 0.00002m, anchor.Price, "Large price retains tiny decimal offset");
            AssertPoint(new Point(492, 224), OrderFlowDrawingGeometry.Project(bars, 0, 3, plot, min, max, anchor), "Round trip");
            AssertEqual(1d, OrderFlowDrawingGeometry.BarPosition(bars, day.AddHours(12)), "Empty night collapses to next boundary");
            AssertEqual(1d, OrderFlowDrawingGeometry.BarPosition(bars, day.AddMinutes(1)), "Exact bar end maps to next boundary");
            OrderFlowDrawingAnchor right = OrderFlowDrawingGeometry.AnchorAt(bars, 0, 3, plot, min, max, new Point(plot.Right, plot.Top));
            AssertEqual(bars[2].TimeEnd.AddTicks(-1), right.Time, "Right border remains inside last bar");
            List<OrderFlowDisplayBar> months = OrderFlowChartTimeFrames.AggregateMinutes(bars, OrderFlowDisplayTimeFrame.Month1);
            Point monthly = OrderFlowDrawingGeometry.Project(months, 0, 2, plot, min, max, anchor);
            AssertTrue(monthly.X > plot.Left && monthly.X < plot.Left + 400, "Leap-day anchor inside calendar February");
            Rect resized = new Rect(92, 24, 1600, 800);
            AssertPoint(new Point(892, 424), OrderFlowDrawingGeometry.Project(bars, 0, 3, resized, min, max, anchor), "Resize changes pixels, not anchors");
            Point zoomed = OrderFlowDrawingGeometry.Project(bars, 1, 2, plot, min, max, anchor);
            AssertPoint(new Point(292, 224), zoomed, "Pan/zoom uses new viewport");
        }

        private static void TestDrawingInteraction(string root)
        {
            OrderFlowResearchRequest request = CloudRequest(root, Enumerable.Range(0, 8)
                .Select(i => Row(Start.AddMinutes(i), 100 + i % 4 * 0.1m, 10, Side.Buy)).ToArray());
            OrderFlowResearchResult result = Export(request);
            AssertTrue(result.Quality.ResearchAccepted, "Drawing fixture accepted: " + JsonSerializer.Serialize(result.Quality));
            string before = JsonSerializer.Serialize(result);
            OrderFlowResearchChart chart = new OrderFlowResearchChart();
            chart.SetResult(result);
            RenderDrawingChart(chart);
            chart.SetDrawingTool(OrderFlowDrawingTool.Horizontal);
            AssertFalse(chart.DrawingPointerDown(new Point(20, 200)), "Price scale is not drawing area");
            AssertTrue(chart.DrawingPointerDown(new Point(200, 200)), "First drawing click");
            AssertTrue(chart.HasPendingAnchor, "Pending point shown");
            chart.DrawingPointerMove(new Point(500, 400));
            chart.DrawingPointerDown(new Point(500, 400));
            AssertEqual(1, chart.Drawings.Count, "Horizontal line created");
            OrderFlowChartLine horizontal = chart.Drawings[0];
            AssertEqual(horizontal.First.Price, horizontal.Second.Price, "Second click locks horizontal price");
            AssertEqual(OrderFlowDrawingTool.Horizontal, chart.DrawingTool, "Drawing tool remains active");
            chart.SetDrawingTool(OrderFlowDrawingTool.Trend);
            chart.DrawingPointerDown(new Point(300, 250)); chart.DrawingPointerDown(new Point(700, 450));
            AssertEqual(2, chart.Drawings.Count, "Inclined line created");
            AssertTrue(chart.Drawings[1].First.Price != chart.Drawings[1].Second.Price, "Independent prices");
            RenderDrawingChart(chart);
            chart.DrawingPointerDown(new Point(400, 200));
            AssertTrue(ReferenceEquals(horizontal, chart.SelectedDrawing), "Select existing line");
            chart.SetDrawingStyle(Colors.Magenta, 5, true, true);
            RenderDrawingChart(chart);
            AssertTrue(ReferenceEquals(horizontal, chart.DrawingAt(new Point(94, 200))), "Extended portion selectable");
            AssertTrue(chart.DrawingAt(new Point(91, 200)) == null, "Clipped portion not selectable");
            AssertTrue(ContainsPen(VisualTreeHelper.GetDrawing(chart), Colors.Magenta, 5), "Actual rendered pen uses chosen color and width");
            chart.DrawingPointerDown(new Point(200, 200)); chart.DrawingPointerMove(new Point(250, 220));
            chart.SetDrawingTool(OrderFlowDrawingTool.Select);
            AssertEqual(horizontal.First.Price, horizontal.Second.Price, "Moving horizontal endpoint moves price level");
            OrderFlowDrawingAnchor saved = horizontal.First;
            foreach (OrderFlowDisplayTimeFrame frame in OrderFlowChartTimeFrames.GetMenuValues())
            {
                chart.SetTimeFrame(frame); RenderDrawingChart(chart);
                AssertEqual(saved, horizontal.First, "Timeframe never rewrites source anchor");
            }
            chart.SetTimeFrame(OrderFlowDisplayTimeFrame.Min1); RenderDrawingChart(chart);
            chart.SetDrawingTool(OrderFlowDrawingTool.Trend); chart.DrawingPointerDown(new Point(400, 300));
            chart.SetDrawingTool(OrderFlowDrawingTool.Select);
            AssertFalse(chart.HasPendingAnchor, "Cancel discards unfinished point");
            AssertEqual(2, chart.Drawings.Count, "Cancel preserves completed lines");
            RenderDrawingChart(chart);
            chart.DrawingPointerDown(new Point(400, 220)); chart.DeleteSelectedDrawing();
            AssertEqual(1, chart.Drawings.Count, "Delete selection only");
            foreach (double width in new[] { 0, 7, double.NaN }) { Expect<ArgumentOutOfRangeException>(() => chart.SetDrawingStyle(Colors.Red, width, false, false)); }
            AssertEqual(before, JsonSerializer.Serialize(result), "Annotations never enter results or hashes");
            AssertEqual(result.ArtifactDirectory, new OrderFlowResearchArtifactWriter().Write(request, result), "Existing export remains identical");
            chart.SetResult(result);
            AssertEqual(0, chart.Drawings.Count, "Installing another result clears annotations");
        }

        private static void TestDrawingDuringAxisDrag(string root)
        {
            OrderFlowResearchRequest request = CloudRequest(root, Enumerable.Range(0, 8)
                .Select(i => Row(Start.AddMinutes(i), 100 + i, 10, Side.Buy)).ToArray());
            OrderFlowResearchChart chart = new OrderFlowResearchChart(); chart.SetResult(Export(request));
            RenderDrawingChart(chart);
            chart.SetDrawingTool(OrderFlowDrawingTool.Trend);
            chart.DrawingPointerDown(new Point(200, 200));
            AssertTrue(chart.HasPendingAnchor, "First point is pending before axis drag");
            Dictionary<string, object> dragState = new Dictionary<string, object>
            {
                ["_dragging"] = true, ["_dragTimeAxis"] = true, ["_dragOrigin"] = new Point(400, 580),
                ["_dragStart"] = chart.StartIndex, ["_dragCount"] = chart.VisibleCount, ["_dragAnchor"] = 0.5d
            };
            foreach (KeyValuePair<string, object> field in dragState)
            { typeof(OrderFlowResearchChart).GetField(field.Key, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(chart, field.Value); }
            AssertTrue(chart.MovePointerInteraction(new Point(580, 580)), "Active axis gesture consumed");
            AssertEqual(3, chart.VisibleCount, "Axis drag zooms even with a pending line");
            AssertTrue(chart.HasPendingAnchor && chart.Drawings.Count == 0, "Zoom neither completes nor drops first point");
            chart.SetDrawingTool(OrderFlowDrawingTool.Select);
        }

        private static void TestChartSurfaceTransfer(string root)
        {
            ComboBox primary = new ComboBox { SelectedValuePath = "Key", ItemsSource = OrderFlowChartTimeFrames.GetMenuValues()
                .Select(frame => new KeyValuePair<OrderFlowDisplayTimeFrame, string>(frame, frame.ToString())).ToList() };
            primary.SelectedValue = OrderFlowDisplayTimeFrame.Min1;
            Slider primaryScale = new Slider { Minimum = 0.1, Maximum = 5, Value = 1 };
            ComboBox mirror = new ComboBox { SelectedValuePath = "Key" };
            Slider mirrorScale = new Slider { Minimum = 0.1, Maximum = 5 };
            TextBlock scaleText = new TextBlock();
            OrderFlowResearchChart chart = new OrderFlowResearchChart();
            StackPanel surface = new StackPanel();
            surface.Children.Add(mirror); surface.Children.Add(mirrorScale); surface.Children.Add(scaleText); surface.Children.Add(chart);
            ContentControl embedded = new ContentControl { Content = surface };
            ContentControl separate = new ContentControl();
            NameScope.SetNameScope(embedded, new NameScope()); NameScope.SetNameScope(separate, new NameScope());
            OrderFlowChartHost.BindSettings(mirror, primary, mirrorScale, primaryScale, scaleText);
            using (OrderFlowChartHost host = new OrderFlowChartHost(embedded))
            {
                for (int i = 0; i < 3; i++)
                {
                    host.MoveTo(separate);
                    separate.Measure(new Size(1000, 650)); separate.Arrange(new Rect(0, 0, 1000, 650)); separate.UpdateLayout();
                    Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.DataBind, new Action(() => { }));
                    AssertTrue(embedded.Content == null && ReferenceEquals(surface, separate.Content), "Single parent and same surface");
                    AssertTrue(ReferenceEquals(chart, surface.Children[3]), "Chart not recreated");
                    mirror.SelectedValue = OrderFlowDisplayTimeFrame.Month1;
                    AssertEqual(OrderFlowDisplayTimeFrame.Month1, (OrderFlowDisplayTimeFrame)primary.SelectedValue, "Detached timeframe propagates");
                    primary.SelectedValue = OrderFlowDisplayTimeFrame.Hour4;
                    AssertEqual(primary.SelectedValue, mirror.SelectedValue, "Settings propagate to detached selector");
                    mirrorScale.Value = 2.5;
                    AssertEqual(2.5, primaryScale.Value, "Detached scale propagates");
                    primaryScale.Value = 0.5;
                    AssertEqual(0.5, mirrorScale.Value, "Settings scale propagates");
                    AssertTrue(scaleText.Text.EndsWith("5×"), "Readout binding survives namescope change");
                    host.Restore(); host.Restore();
                    AssertTrue(separate.Content == null && ReferenceEquals(surface, embedded.Content), "Idempotent return");
                }
                ContentControl occupied = new ContentControl { Content = new TextBlock() };
                Expect<InvalidOperationException>(() => host.MoveTo(occupied));
                AssertTrue(ReferenceEquals(surface, embedded.Content), "Failed transfer retains original parent");
                host.MoveTo(separate);
            }
            AssertTrue(separate.Content == null && embedded.Content == null, "Owner disposal detaches instead of restoring");
            AssertTrue(Application.Current == null, "No app or window started");
        }

        private static void TestDrawingToolbarMarkup(string root)
        {
            XDocument ui;
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Research.Ui.xaml")) { ui = XDocument.Load(stream); }
            XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            XElement surface = ui.Descendants().Single(element => (string)element.Attribute("Name") == "ScrollViewerChartSurface");
            string[] names = { "ButtonChartPopOut", "ComboBoxDrawingTool", "ButtonDrawingColor", "ComboBoxDrawingThickness",
                "CheckBoxDrawingLeft", "CheckBoxDrawingRight", "ButtonDrawingDelete", "ButtonDrawingClear", "ContentControlChart", "ScrollBarChart" };
            foreach (string name in names) { AssertTrue(surface.Descendants().Any(element => (string)element.Attribute("Name") == name), "Tool and viewport move together: " + name); }
            XElement grid = ui.Descendants(ns + "WrapPanel").Single(element => element.Descendants().Any(item => (string)item.Attribute("Name") == "ComboBoxDrawingTool"));
            FrameworkElement toolbar = (FrameworkElement)XamlReader.Parse(grid.ToString());
            toolbar.Measure(new Size(900, 200)); toolbar.Arrange(new Rect(0, 0, 900, 200)); toolbar.UpdateLayout();
            AssertTrue(toolbar.DesiredSize.Height > 0 && toolbar.DesiredSize.Width <= 900, "Toolbar wraps within viewport width");
        }

        private static void RenderDrawingChart(OrderFlowResearchChart chart)
        {
            chart.Measure(new Size(900, 600)); chart.Arrange(new Rect(0, 0, 900, 600)); chart.UpdateLayout();
            new RenderTargetBitmap(900, 600, 96, 96, PixelFormats.Pbgra32).Render(chart);
        }

        private static bool ContainsPen(Drawing drawing, Color color, double thickness)
        {
            if (drawing is GeometryDrawing geometry && geometry.Pen?.Brush is SolidColorBrush brush && brush.Color == color && geometry.Pen.Thickness == thickness) { return true; }
            return drawing is DrawingGroup group && group.Children.Any(child => ContainsPen(child, color, thickness));
        }

        private static void AssertPoint(Point expected, Point actual, string message)
        {
            AssertTrue((expected - actual).Length < 0.000001, message + " expected " + expected + " actual " + actual);
        }
    }
}
