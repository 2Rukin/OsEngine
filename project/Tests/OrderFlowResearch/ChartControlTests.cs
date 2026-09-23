/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
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
        private static void TestMonthlyChartBars(string root)
        {
            DateTime[] times = {
                new DateTime(2027, 1, 31, 23, 59, 0, DateTimeKind.Utc),
                new DateTime(2027, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2027, 2, 28, 23, 59, 0, DateTimeKind.Utc),
                new DateTime(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2027, 5, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2027, 12, 31, 23, 59, 0, DateTimeKind.Utc),
                new DateTime(2028, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2028, 2, 29, 23, 59, 0, DateTimeKind.Utc),
                new DateTime(2028, 3, 1, 0, 0, 0, DateTimeKind.Utc) };
            List<OrderFlowDisplayBar> minutes = times.Select((time, index) =>
                ChartMinute(time, 100 + index, 105 + index, 95 - index, 101 + index, 10 + index, index - 3, index / 10m)).ToList();
            string original = JsonSerializer.Serialize(minutes);
            List<OrderFlowDisplayBar> months = OrderFlowChartTimeFrames.AggregateMinutes(minutes, OrderFlowDisplayTimeFrame.Month1);
            AssertEqual(8, months.Count, "Missing months are omitted");
            AssertEqual(new DateTime(2027, 1, 1), months[0].TimeStart, "Partial January starts at calendar boundary");
            AssertEqual(new DateTime(2027, 2, 1), months[0].TimeEnd, "January is not thirty days");
            AssertEqual(new DateTime(2027, 3, 1), months[1].TimeEnd, "Non-leap February has 28 days");
            AssertEqual(28d, (months[1].TimeEnd - months[1].TimeStart).TotalDays, "Actual February duration");
            AssertEqual(new DateTime(2027, 5, 1), months[3].TimeStart, "Empty April omitted");
            AssertEqual(new DateTime(2028, 1, 1), months[4].TimeEnd, "December ends in next year");
            AssertEqual(months[4].TimeEnd, months[5].TimeStart, "Exact year boundary starts new month");
            AssertEqual(29d, (months[6].TimeEnd - months[6].TimeStart).TotalDays, "Leap February has 29 days");
            AssertEqual(101m, months[1].Open, "First February open");
            AssertEqual(107m, months[1].High, "Maximum high");
            AssertEqual(93m, months[1].Low, "Minimum low");
            AssertEqual(103m, months[1].Close, "Last February close");
            AssertEqual(23m, months[1].Volume, "Summed volume");
            AssertEqual(-3m, months[1].Delta, "Summed delta");
            AssertEqual(0.2m, months[1].PriceResponse, "Last response, without feature recalculation");
            AssertEqual(DateTimeKind.Utc, months[1].TimeStart.Kind, "Source Kind preserved");
            AssertEqual(original, JsonSerializer.Serialize(minutes), "Source minute bars untouched");
            Expect<ArgumentOutOfRangeException>(() => OrderFlowChartTimeFrames.GetDuration(OrderFlowDisplayTimeFrame.Month1));
        }

        private static void TestChartControlBindings(string root)
        {
            List<OrderFlowDisplayTimeFrame> values = OrderFlowChartTimeFrames.GetMenuValues();
            AssertEqual(21, values.Distinct().Count(), "Unique choices");
            AssertEqual(OrderFlowDisplayTimeFrame.Sec15, values[0], "Shortest first");
            AssertEqual(OrderFlowDisplayTimeFrame.Month1, values.Last(), "Calendar month last");
            AssertEqual(OrderFlowDisplayTimeFrame.Min2, values[3], "M2 sorted after M1 despite appended enum");
            for (int i = 1; i < values.Count - 1; i++)
            {
                AssertTrue(OrderFlowChartTimeFrames.GetDuration(values[i - 1]) < OrderFlowChartTimeFrames.GetDuration(values[i]), "Increasing menu durations");
            }
            XDocument ui;
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Research.Ui.xaml")) { ui = XDocument.Load(stream); }
            XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            string[] names = { "ComboBoxTimeFrame", "ComboBoxChartTimeFrame", "SliderCloudScale", "SliderChartCloudScale" };
            List<XElement> controls = names.Select(name => ui.Descendants().Single(item => (string)item.Attribute("Name") == name)).ToList();
            AssertTrue(controls[1].Ancestors(ns + "TabItem").Any(item => (string)item.Attribute("Name") == "TabItemChart"), "Selector is directly inside chart result tab");
            AssertTrue(controls[3].Ancestors(ns + "TabItem").Any(item => (string)item.Attribute("Name") == "TabItemChart"), "Size control is directly inside chart result tab");
            FrameworkElement host = (FrameworkElement)XamlReader.Parse(new XElement(ns + "StackPanel", controls.Select(item => new XElement(item))).ToString());
            ComboBox primary = (ComboBox)host.FindName(names[0]);
            ComboBox mirror = (ComboBox)host.FindName(names[1]);
            Slider scale = (Slider)host.FindName(names[2]);
            Slider mirrorScale = (Slider)host.FindName(names[3]);
            primary.ItemsSource = values.Select(value => new KeyValuePair<OrderFlowDisplayTimeFrame, string>(value,
                OrderFlowChartTimeFrames.GetDisplayName(value, true))).ToList();
            primary.SelectedValue = OrderFlowDisplayTimeFrame.Min1;
            host.Measure(new Size(900, 400)); host.Arrange(new Rect(0, 0, 900, 400)); host.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.DataBind, new Action(() => { }));
            AssertEqual(21, mirror.Items.Count, "Chart dropdown receives all choices");
            foreach (OrderFlowDisplayTimeFrame value in values)
            {
                mirror.SelectedValue = value;
                AssertEqual(value, (OrderFlowDisplayTimeFrame)primary.SelectedValue, "Chart choice reaches existing chart event source");
            }
            primary.SelectedValue = OrderFlowDisplayTimeFrame.Hour12;
            AssertEqual(primary.SelectedValue, mirror.SelectedValue, "Settings choice reflected above chart");
            mirrorScale.Value = 2.5;
            AssertEqual(2.5, scale.Value, "Chart size propagates to event source");
            scale.Value = 0.1;
            AssertEqual(0.1, mirrorScale.Value, "Settings size reflected above chart");
            AssertEqual("1 месяц", OrderFlowChartTimeFrames.GetDisplayName(OrderFlowDisplayTimeFrame.Month1, true), "Month label");
            AssertEqual("12 h", OrderFlowChartTimeFrames.GetDisplayName(OrderFlowDisplayTimeFrame.Hour12, false), "Hour label");
            AssertTrue(Application.Current == null, "No application or window started");
        }

        private static void TestCloudVisualScale(string root)
        {
            OrderFlowResearchRequest request = CloudRequest(root, BasicTicks());
            OrderFlowResearchResult result = Export(request);
            string original = JsonSerializer.Serialize(result);
            OrderFlowResearchChart chart = new OrderFlowResearchChart();
            chart.SetResult(result);
            double baseRadius = 0;
            foreach (double coefficient in new[] { 1d, 0.1, 2.5, 3 })
            {
                chart.SetCloudScale(coefficient);
                AssertTrue(chart.CloudAt(new Point(100, 24)) == null, "Old hit targets cleared before redraw");
                chart.Measure(new Size(1000, 500)); chart.Arrange(new Rect(0, 0, 1000, 500)); chart.UpdateLayout();
                new RenderTargetBitmap(1000, 500, 96, 96, PixelFormats.Pbgra32).Render(chart);
                List<(Point Center, double Radius, OrderFlowCloud Cloud)> hits =
                    (List<(Point, double, OrderFlowCloud)>)typeof(OrderFlowResearchChart).GetField("_cloudHits", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(chart);
                AssertTrue(hits.Count > 0, "Cloud circles painted");
                if (coefficient == 1) { baseRadius = hits[0].Radius; }
                AssertTrue(Math.Abs(baseRadius * coefficient - hits[0].Radius) < 0.000001, "Coefficient scales actual painted/hit radius");
                Point visible = hits[0].Center + new Vector(0, hits[0].Radius * 0.75);
                AssertTrue(chart.CloudAt(visible) != null, "Scaled visible circle responds to hover");
                AssertTrue(chart.CloudAt(hits[0].Center - new Vector(0, 1)) == null, "Scaled circle still respects top clipping");
            }
            foreach (double invalid in new[] { 0, -1, 0.09, 3.01, double.NaN, double.PositiveInfinity })
            { Expect<ArgumentOutOfRangeException>(() => chart.SetCloudScale(invalid)); }
            AssertEqual(original, JsonSerializer.Serialize(result), "Scale does not modify data or hashes");
            AssertEqual(result.ArtifactDirectory, new OrderFlowResearchArtifactWriter().Write(request, result), "Immutable export unchanged");
        }
    }
}
