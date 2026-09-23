/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using OsEngine.Entity;
using OsEngine.OsData.OrderFlow;
using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using System.Reflection;

namespace OsEngine.OrderFlowResearch.Tests
{
    internal static partial class Program
    {
        private static void TestDateEntry(string root)
        {
            DatePicker picker = new DatePicker { Language = XmlLanguage.GetLanguage("en-US") };
            picker.Template = (ControlTemplate)XamlReader.Parse(
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' " +
                "xmlns:p='clr-namespace:System.Windows.Controls.Primitives;assembly=PresentationFramework' " +
                "TargetType='{x:Type DatePicker}'><p:DatePickerTextBox x:Name='PART_TextBox'/></ControlTemplate>");
            using OrderFlowDateInput guard = new OrderFlowDateInput(picker);
            picker.ApplyTemplate();
            DatePickerTextBox input = (DatePickerTextBox)picker.Template.FindName("PART_TextBox", picker);
            AssertFalse(guard.ReadDate().HasValue, "Initially blank means full file");
            foreach (bool oldSelection in new[] { false, true })
            {
                guard.Clear();
                if (oldSelection) { picker.SelectedDate = Start.Date; }
                EnterDate(input, "bad-date");
                AssertFalse(picker.SelectedDate.HasValue, "Invalid text cannot retain previous selection");
                AssertEqual("", picker.Text, "Actual WPF LostFocus erases invalid text");
                Expect<ArgumentException>(() => guard.ReadDate());
                EnterDate(input, "09/18/2026");
                AssertEqual(Start.Date, guard.ReadDate().Value, "Correction to same date is accepted");
                EnterDate(input, "bad-again");
                Expect<ArgumentException>(() => guard.ReadDate());
                picker.SelectedDate = Start.Date;
                AssertEqual(Start.Date, guard.ReadDate().Value, "Calendar selection recovers");
                EnterDate(input, "");
                AssertFalse(guard.ReadDate().HasValue, "Intentional deletion of valid date is accepted");
                EnterDate(input, "invalid");
                Expect<ArgumentException>(() => guard.ReadDate());
                guard.Clear();
                AssertFalse(guard.ReadDate().HasValue, "Explicit Full file action recovers from invalid edit");
            }
            using OrderFlowDateInput second = new OrderFlowDateInput(new DatePicker());
            EnterDate(input, "invalid");
            AssertFalse(second.ReadDate().HasValue, "Other endpoint is blank");
            Expect<ArgumentException>(() => guard.ReadDate());
            guard.Clear(); second.Clear();
            AssertFalse(guard.ReadDate().HasValue || second.ReadDate().HasValue, "Explicit both-date reset selects full file");
            AssertTrue(Application.Current == null, "No Application or Window started");
        }

        private static void EnterDate(DatePickerTextBox input, string text)
        {
            input.Text = text;
            input.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
        }

        private static void TestDecimalPresentation(string root)
        {
            OrderFlowResearchRequest request = Request(root, Row(Start, 0.00000020m, 10, Side.Buy),
                Row(Start.AddSeconds(1), 0.00000021m, 200, Side.Sell), Row(Start.AddSeconds(7), 0.00000022m, 1, Side.Buy));
            request.PriceStep = 0.00000001m;
            OrderFlowResearchResult result = Replay(request);
            AssertTrue(OrderFlowCandidateView.Create(result)[0].Details.Contains("0.00000021"), "Detail price is not rounded to zero");
            MethodInfo format = typeof(OrderFlowResearchChart).GetMethod("F", BindingFlags.Static | BindingFlags.NonPublic);
            AssertEqual("0.00000021", (string)format.Invoke(null, new object[] { 0.00000021m }), "Exact tooltip price");
        }

        private static void TestSummaryLayout(string root)
        {
            AssertTrue(Application.Current == null, "No application is started by offline layout tests.");
            XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            Assembly assembly = Assembly.GetExecutingAssembly();
            XDocument app;
            XDocument ui;
            using (Stream stream = assembly.GetManifestResourceStream("Research.App.xaml")) { app = XDocument.Load(stream); }
            using (Stream stream = assembly.GetManifestResourceStream("Research.Ui.xaml")) { ui = XDocument.Load(stream); }
            XElement style = app.Descendants(ns + "Style").Single(element =>
                element.Attribute(x + "Key") == null && (string)element.Attribute("TargetType") == "{x:Type TextBox}");
            XElement dictionary = new XElement(ns + "ResourceDictionary", new XAttribute(XNamespace.Xmlns + "x", x), new XElement(style));
            ResourceDictionary resources = (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
            resources.MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(
                new Uri("/OsEngine;component/Themes/ThemeDarkOrange.xaml", UriKind.Relative)));
            XElement summaryElement = ui.Descendants(ns + "TextBox").Single(element => (string)element.Attribute("Name") == "TextBoxSummary");
            TextBox summary = (TextBox)XamlReader.Parse(summaryElement.ToString());
            Grid host = new Grid();
            host.Resources = resources;
            host.Children.Add(summary);
            summary.Text = string.Join(Environment.NewLine, Enumerable.Range(0, 200).Select(i => "Line " + i)) + "\nSUMMARY_END";
            host.Measure(new Size(900, 480));
            host.Arrange(new Rect(0, 0, 900, 480));
            host.UpdateLayout();
            AssertTrue(ReferenceEquals(resources[typeof(TextBox)], summary.Style), "Real implicit application style is exercised.");
            AssertTrue(summary.ActualHeight > 300, "Summary overrides the global 23px height.");
            AssertEqual(VerticalAlignment.Top, summary.VerticalContentAlignment, "Summary starts at the top.");
            ScrollViewer scroll = (ScrollViewer)summary.Template.FindName("PART_ContentHost", summary);
            AssertTrue(scroll.ViewportHeight > 100 && scroll.ScrollableHeight > 0, "Multiline viewport can scroll.");
            summary.ScrollToEnd();
            host.UpdateLayout();
            AssertTrue(scroll.VerticalOffset > 0, "Last summary lines are reachable.");
            AssertEqual(summary.LineCount - 1, summary.GetLastVisibleLineIndex(), "Final line is visible after scrolling.");

            OrderFlowResearchResult result = new OrderFlowResearchResult();
            result.Input = new OrderFlowTickInput();
            result.ArtifactDirectory = @"C:\Research_runs\TICK_INVALID";
            result.Quality.FirstDealTime = new DateTime(2026, 9, 18, 12, 34, 56, 789);
            result.Quality.LastDealTime = result.Quality.FirstDealTime;
            result.Quality.DealCount = 21194;
            result.InputHash = "hash_with_underscore:123";
            result.Quality.Issues.Add(new OrderFlowQualityIssue { ReasonCode = "TICK_INVALID", Message = "detail:with_underscore" });
            foreach (bool russian in new bool[] { false, true })
            {
                string text = OrderFlowResearchUi.BuildSummary(result, russian);
                AssertTrue(text.Contains(result.ArtifactDirectory), "Full Windows path survives localization.");
                AssertTrue(text.Contains("12:34:56.789") && text.Contains("21194"), "Times and totals survive localization.");
                AssertTrue(text.Contains(result.InputHash) && text.Contains("TICK_INVALID") && text.Contains("detail:with_underscore"), "Hashes and reasons survive localization.");
            }
            AssertTrue(Application.Current == null, "Layout test starts no Application or window.");
        }

        private static void TestChartNavigation(string root)
        {
            OrderFlowResearchResult result = new OrderFlowResearchResult();
            DateTime start = new DateTime(2026, 9, 18, 10, 0, 0);
            foreach (OrderFlowDisplayTimeFrame frame in new OrderFlowDisplayTimeFrame[] { OrderFlowDisplayTimeFrame.Sec15, OrderFlowDisplayTimeFrame.Sec30, OrderFlowDisplayTimeFrame.Min1 })
            {
                int seconds = frame == OrderFlowDisplayTimeFrame.Min1 ? 60 : frame == OrderFlowDisplayTimeFrame.Sec30 ? 30 : 15;
                List<OrderFlowDisplayBar> bars = new List<OrderFlowDisplayBar>();
                for (int i = 0; i < 900 * 60 / seconds; i++)
                {
                    bars.Add(new OrderFlowDisplayBar { TimeStart = start.AddSeconds(i * seconds), TimeEnd = start.AddSeconds((i + 1) * seconds) });
                }
                result.Bars.Add(frame, bars);
            }
            result.Candidates.Add(new OrderFlowCandidate { CandidateId = "C1", Time = start.AddMinutes(450) });
            OrderFlowResearchChart chart = new OrderFlowResearchChart();
            chart.SetResult(result);
            AssertEqual(780, chart.StartIndex, "Initial viewport shows the last 120 bars.");
            chart.ScrollTo(-100);
            AssertEqual(0, chart.StartIndex, "History starts at the first bar.");
            for (int index = 0; index <= 780; index++)
            {
                chart.ScrollTo(index);
                AssertEqual(index, chart.StartIndex, "Every legal viewport is reachable.");
            }
            chart.SelectCandidate("C1");
            AssertEqual(390, chart.StartIndex, "Selection centers the candidate.");
            chart.ScrollTo(200);
            chart.InvalidateVisual();
            AssertEqual(200, chart.StartIndex, "Redraw must not recenter on selection.");
            chart.SetTimeFrame(OrderFlowDisplayTimeFrame.Sec30);
            AssertEqual(start.AddMinutes(260), result.Bars[OrderFlowDisplayTimeFrame.Sec30][chart.StartIndex + chart.VisibleCount / 2].TimeStart,
                "Timeframe retains viewport midpoint instead of jumping to selection.");
            chart.Zoom(chart.TotalBars);
            AssertEqual(0, chart.StartIndex, "Full history starts at zero.");
            AssertEqual(1800, chart.VisibleCount, "Full history includes every trade bar.");
            chart.SetTimeFrame(OrderFlowDisplayTimeFrame.Sec15);
            AssertEqual(3600, chart.VisibleCount, "Full-history view survives timeframe change.");
            chart.Zoom(30);
            chart.ScrollTo(int.MaxValue);
            AssertEqual(chart.TotalBars, chart.StartIndex + chart.VisibleCount, "Right edge reaches final bar after zoom.");
            chart.SetResult(new OrderFlowResearchResult());
            chart.ScrollTo(100);
            chart.Zoom(0);
            AssertEqual(0, chart.VisibleCount, "Empty result remains navigable without invalid ranges.");
            AssertEqual(1, result.Candidates.Count, "Navigation does not rewrite research output.");
        }

        private static void TestLargerChartBars(string root)
        {
            DateTime day = new DateTime(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc);
            List<OrderFlowDisplayBar> minutes = new List<OrderFlowDisplayBar>
            {
                ChartMinute(day.AddHours(23).AddMinutes(58), 100, 105, 99, 104, 10, -2, 0.1m),
                ChartMinute(day.AddHours(23).AddMinutes(59), 104, 110, 97, 108, 20, 7, 0.2m),
                ChartMinute(day.AddDays(1), 200, 207, 198, 202, 30, -12, 0.3m),
                ChartMinute(day.AddDays(1).AddMinutes(1), 202, 209, 199, 208, 40, -5, 0.4m),
                new OrderFlowDisplayBar { TimeStart = day.AddDays(1).AddHours(4), HasTrades = false },
                ChartMinute(day.AddDays(1).AddHours(8).AddMinutes(1), 300, 310, 280, 301, 50, 49, 0.5m)
            };
            OrderFlowDisplayTimeFrame[] frames = { OrderFlowDisplayTimeFrame.Min5, OrderFlowDisplayTimeFrame.Min10,
                OrderFlowDisplayTimeFrame.Min15, OrderFlowDisplayTimeFrame.Min30, OrderFlowDisplayTimeFrame.Min60, OrderFlowDisplayTimeFrame.Hour4 };
            int[] durationMinutes = { 5, 10, 15, 30, 60, 240 };
            int[] firstStartMinutes = { 1435, 1430, 1425, 1410, 1380, 1200 };
            string original = JsonSerializer.Serialize(minutes);
            for (int i = 0; i < frames.Length; i++)
            {
                List<OrderFlowDisplayBar> bars = OrderFlowChartTimeFrames.AggregateMinutes(minutes, frames[i]);
                AssertEqual(3, bars.Count, "No fabricated empty intervals for " + frames[i]);
                AssertEqual(day.AddMinutes(firstStartMinutes[i]), bars[0].TimeStart, "Clock-aligned first interval " + frames[i]);
                AssertEqual(day.AddDays(1), bars[0].TimeEnd, "Prior day ends at midnight " + frames[i]);
                AssertEqual(day.AddDays(1), bars[1].TimeStart, "Exact boundary starts next bar " + frames[i]);
                AssertEqual(day.AddDays(1).AddMinutes(durationMinutes[i]), bars[1].TimeEnd, "Correct requested duration " + frames[i]);
                AssertEqual(day.AddDays(1).AddHours(8), bars[2].TimeStart, "Gap skips empty bars " + frames[i]);
                AssertEqual(DateTimeKind.Utc, bars[2].TimeStart.Kind, "Source time kind preserved");
                AssertEqual(frames[i], bars[0].TimeFrame, "Target timeframe recorded");
                AssertEqual(100m, bars[0].Open, "First minute open");
                AssertEqual(110m, bars[0].High, "Maximum high");
                AssertEqual(97m, bars[0].Low, "Minimum low");
                AssertEqual(108m, bars[0].Close, "Last minute close");
                AssertEqual(30m, bars[0].Volume, "Summed volume");
                AssertEqual(5m, bars[0].Delta, "Summed signed delta");
                AssertEqual(0.2m, bars[0].PriceResponse, "Response is the last snapshot, not recomputed from OHLC");
                AssertEqual(70m, bars[1].Volume, "Second interval volume");
                AssertEqual(-17m, bars[1].Delta, "Second interval delta");
                AssertEqual(301m, bars[2].Close, "Final partial bar retained");
                AssertFalse(ReferenceEquals(minutes[0], bars[0]), "Resampling creates detached objects");
            }
            AssertEqual(original, JsonSerializer.Serialize(minutes), "Minute DTOs remain unchanged");
        }

        private static void TestCalendarChartBars(string root)
        {
            DateTime sunday = new DateTime(2027, 1, 3, 23, 59, 0, DateTimeKind.Unspecified);
            List<OrderFlowDisplayBar> minutes = new List<OrderFlowDisplayBar>
            {
                ChartMinute(sunday.AddDays(-3), 10, 12, 9, 11, 2, -1, 0.1m),
                ChartMinute(sunday, 11, 15, 8, 14, 3, 2, 0.2m),
                ChartMinute(sunday.AddMinutes(1), 20, 25, 18, 24, 4, -2, 0.3m),
                ChartMinute(sunday.AddDays(15).AddMinutes(1), 30, 35, 28, 34, 5, 3, 0.4m)
            };
            string before = JsonSerializer.Serialize(minutes);
            List<OrderFlowDisplayBar> weeks = OrderFlowChartTimeFrames.AggregateMinutes(minutes, OrderFlowDisplayTimeFrame.Week1);
            AssertEqual(3, weeks.Count, "Empty calendar weeks omitted");
            AssertEqual(new DateTime(2026, 12, 28), weeks[0].TimeStart, "Week spans year boundary from Monday");
            AssertEqual(new DateTime(2027, 1, 4), weeks[0].TimeEnd, "Sunday belongs to previous week");
            AssertEqual(weeks[0].TimeEnd, weeks[1].TimeStart, "Exact Monday starts next week");
            AssertEqual(new DateTime(2027, 1, 18), weeks[2].TimeStart, "Gap preserves calendar alignment");
            AssertEqual(10m, weeks[0].Open, "Weekly first open");
            AssertEqual(15m, weeks[0].High, "Weekly maximum");
            AssertEqual(8m, weeks[0].Low, "Weekly minimum");
            AssertEqual(14m, weeks[0].Close, "Weekly last close");
            AssertEqual(5m, weeks[0].Volume, "Weekly total volume");
            AssertEqual(1m, weeks[0].Delta, "Weekly signed delta");
            AssertEqual(0.2m, weeks[0].PriceResponse, "Last response retained");
            AssertEqual(DateTimeKind.Unspecified, weeks[0].TimeStart.Kind, "No timezone invented");
            List<OrderFlowDisplayBar> days = OrderFlowChartTimeFrames.AggregateMinutes(minutes, OrderFlowDisplayTimeFrame.Day1);
            AssertEqual(4, days.Count, "Empty days omitted");
            AssertEqual(sunday.Date, days[1].TimeStart, "Daily midnight");
            AssertEqual(sunday.Date.AddDays(1), days[1].TimeEnd, "Next midnight exclusive");
            DateTime leap = new DateTime(2028, 2, 29, 23, 59, 0);
            days = OrderFlowChartTimeFrames.AggregateMinutes(new List<OrderFlowDisplayBar>
            {
                ChartMinute(leap, 1, 1, 1, 1, 1, 1, 0),
                ChartMinute(leap.AddMinutes(1), 2, 2, 2, 2, 2, -2, 0)
            }, OrderFlowDisplayTimeFrame.Day1);
            AssertEqual(new DateTime(2028, 3, 1), days[0].TimeEnd, "Leap day ends at March midnight");
            AssertEqual(days[0].TimeEnd, days[1].TimeStart, "Leap boundary does not merge days");
            AssertEqual(before, JsonSerializer.Serialize(minutes), "Calendar resampling leaves research untouched");
        }

        private static OrderFlowDisplayBar ChartMinute(DateTime time, decimal open, decimal high, decimal low,
            decimal close, decimal volume, decimal delta, decimal response)
        {
            return new OrderFlowDisplayBar { TimeFrame = OrderFlowDisplayTimeFrame.Min1,
                TimeStart = time, TimeEnd = time.AddMinutes(1), HasTrades = true,
                Open = open, High = high, Low = low, Close = close, Volume = volume, Delta = delta,
                PriceResponse = response };
        }

        private static void TestChartTimeFrames(string root)
        {
            OrderFlowResearchRequest request = Request(root, BasicTicks());
            OrderFlowResearchResult result = new OrderFlowResearchRunner().RunAndExport(request, CancellationToken.None);
            string original = JsonSerializer.Serialize(result);
            OrderFlowResearchChart chart = new OrderFlowResearchChart();
            chart.SetResult(result);
            XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            XDocument ui;
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Research.Ui.xaml")) { ui = XDocument.Load(stream); }
            ComboBox selector = (ComboBox)XamlReader.Parse(ui.Descendants(ns + "ComboBox").Single(element =>
                (string)element.Attribute("Name") == "ComboBoxTimeFrame").ToString());
            OrderFlowDisplayTimeFrame[] frames = Enum.GetValues<OrderFlowDisplayTimeFrame>();
            AssertEqual(21, frames.Length, "Complete display timeframe menu");
            selector.ItemsSource = frames.Select(frame => new KeyValuePair<OrderFlowDisplayTimeFrame, string>(frame,
                OrderFlowChartTimeFrames.GetDisplayName(frame, true))).ToList();
            foreach (OrderFlowDisplayTimeFrame frame in frames)
            {
                selector.SelectedValue = frame;
                AssertEqual(frame, ((KeyValuePair<OrderFlowDisplayTimeFrame, string>)selector.SelectedItem).Key,
                    "Selector value reaches the requested frame");
                chart.SetTimeFrame((OrderFlowDisplayTimeFrame)selector.SelectedValue);
                AssertTrue(chart.TotalBars > 0, "Bars available for " + frame);
                chart.SelectCandidate(result.Candidates[0].CandidateId);
                chart.Zoom(chart.TotalBars);
                chart.Measure(new Size(900, 400));
                chart.Arrange(new Rect(0, 0, 900, 400));
                chart.UpdateLayout();
                RenderTargetBitmap bitmap = new RenderTargetBitmap(900, 400, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(chart);
                AssertEqual(3, result.Bars.Count, "Derived timeframes stay outside the engine result");
            }
            AssertEqual("60 мин", OrderFlowChartTimeFrames.GetDisplayName(OrderFlowDisplayTimeFrame.Min60, true), "Minutes label");
            AssertEqual("4 ч", OrderFlowChartTimeFrames.GetDisplayName(OrderFlowDisplayTimeFrame.Hour4, true), "Russian hours label");
            AssertEqual("4 h", OrderFlowChartTimeFrames.GetDisplayName(OrderFlowDisplayTimeFrame.Hour4, false), "English hours label");
            AssertEqual(original, JsonSerializer.Serialize(result), "All research DTOs and hashes stay unchanged after switching/rendering");
            AssertEqual(result.ArtifactDirectory, new OrderFlowResearchArtifactWriter().Write(request, result),
                "Existing immutable bundle remains byte-identical after chart use");
            OrderFlowResearchResult replacement = new OrderFlowResearchResult();
            replacement.Bars[OrderFlowDisplayTimeFrame.Min1] = new List<OrderFlowDisplayBar>
            {
                ChartMinute(new DateTime(2026, 9, 20, 0, 1, 0), 100, 101, 99, 100, 1, 1, 1),
                ChartMinute(new DateTime(2026, 9, 20, 8, 1, 0), 100, 101, 99, 100, 1, 1, 1)
            };
            chart.SetTimeFrame(OrderFlowDisplayTimeFrame.Hour4);
            chart.SetResult(replacement);
            AssertEqual(2, chart.TotalBars, "Changing result invalidates the H4 cache");
            chart.SetResult(new OrderFlowResearchResult());
            AssertEqual(0, chart.TotalBars, "Rejected or empty result clears derived history");
            AssertTrue(Application.Current == null, "No Application or Window was started");
        }

        private static void TestTimeAxis(string root)
        {
            AssertEqual(115, OrderFlowChartNavigation.ZoomStart(100, 120, 60, 1000, 0.25), "Pointer time anchor");
            AssertEqual(100, OrderFlowChartNavigation.ZoomStart(100, 120, 60, 1000, 0), "Left anchor");
            AssertEqual(160, OrderFlowChartNavigation.ZoomStart(100, 120, 60, 1000, 1), "Right anchor");
            AssertEqual(0, OrderFlowChartNavigation.ZoomStart(0, 20, 100, 1000, 1), "Left history bound");
            AssertEqual(900, OrderFlowChartNavigation.ZoomStart(980, 20, 100, 1000, 0), "Right history bound");
            AssertEqual(3, OrderFlowChartNavigation.WheelCount(2, 100, -120), "Wheel must leave two-bar minimum");
            AssertEqual(3, OrderFlowChartNavigation.WheelCount(2, 100, -1), "Small wheel-out delta is not lost");
            AssertEqual(2, OrderFlowChartNavigation.WheelCount(3, 100, 1), "Small wheel-in delta is not lost");
            AssertEqual(100, OrderFlowChartNavigation.WheelCount(100, 100, -960), "Wheel cannot exceed history");
            List<OrderFlowDisplayBar> bars = new List<OrderFlowDisplayBar>();
            AssertEqual(0, OrderFlowChartNavigation.TimeTicks(bars, 1000).Count, "Empty time scale");
            DateTime time = new DateTime(2026, 9, 18, 23, 59, 45);
            bars.Add(new OrderFlowDisplayBar { TimeStart = time, TimeEnd = time.AddSeconds(15) });
            time = time.AddHours(10);
            bars.Add(new OrderFlowDisplayBar { TimeStart = time, TimeEnd = time.AddSeconds(15) });
            List<OrderFlowChartTimeTick> ticks = OrderFlowChartNavigation.TimeTicks(bars, 1000);
            AssertEqual("18.09 23:59:45", ticks[0].Text, "Source seconds and date");
            AssertEqual("19.09 09:59:45", ticks[1].Text, "Gap does not invent intermediate bars");
            AssertEqual(1, ticks[1].BarIndex, "Tick references actual displayed bar");
            List<OrderFlowChartTimeTick> narrow = OrderFlowChartNavigation.TimeTicks(bars, 96);
            AssertEqual(1, narrow.Count, "Narrow plot has no overlapping endpoint labels");
            FormattedText label = new FormattedText(narrow[0].Text, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, Brushes.White, 1);
            AssertTrue(label.Width <= 96, "Time label fits minimum supported plot width");
            OrderFlowResearchResult result = new OrderFlowResearchResult();
            result.Bars.Add(OrderFlowDisplayTimeFrame.Min1, Enumerable.Range(0, 1000).Select(index =>
                new OrderFlowDisplayBar { TimeStart = time.AddMinutes(index), TimeEnd = time.AddMinutes(index + 1) }).ToList());
            OrderFlowResearchChart chart = new OrderFlowResearchChart();
            chart.SetResult(result);
            chart.ScrollTo(100);
            chart.ZoomAt(60, 0.25);
            AssertEqual(115, chart.StartIndex, "Presenter uses pointer anchor");
            chart.ZoomAt(2, 0.25);
            chart.ZoomAt(OrderFlowChartNavigation.WheelCount(chart.VisibleCount, chart.TotalBars, -120), 0.25);
            AssertEqual(3, chart.VisibleCount, "Presenter can zoom back out from minimum");
        }

    }
}
