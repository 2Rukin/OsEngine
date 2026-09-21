/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using OsEngine.Entity;
using OsEngine.Market.Servers.Entity;
using OsEngine.OsData.BinaryEntity;
using OsEngine.OsData.OrderFlow;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using System.Reflection;

namespace OsEngine.OrderFlowResearch.Tests
{
    /// <summary>
    /// Deterministic offline component test stand for the paired-QSH research
    /// path. It creates no network connection, broker order or persistent raw
    /// market fixture and does not prove profitability or live parity.
    /// </summary>
    /// <remarks>
    /// Canonical command from <c>project/</c>:
    /// <c>dotnet run --project Tests/OrderFlowResearch/OsEngine.OrderFlowResearch.Tests.csproj</c>.
    /// The stand has no external service, credential or market-data dependency.
    /// It creates no real order or position and changes no OsEngine setting.
    /// It creates a unique temporary directory and deletes it in <c>finally</c>;
    /// it has no internal timeout, so the invoking build job owns timeout policy.
    /// It covers the synthetic research risks registered by
    /// ORDER-FLOW-QUALIFICATION-001 but does not prove real-QSH, full-day,
    /// execution, profitability or live compatibility.
    /// </remarks>
    internal static partial class Program
    {
        private static int _passed;
        private static int _failed;

        [STAThread]
        private static int Main()
        {
            string root = Path.Combine(Path.GetTempPath(),
                "OsEngine-OrderFlowResearch-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            try
            {
                Run("valid causal replay and separated labels", root, TestValidCausalReplay);
                Run("short candidate is mirrored", root, TestShortCandidate);
                Run("deterministic repeat and write-once artifacts", root, TestDeterministicRepeat);
                Run("case variant names reuse canonical artifacts", root, TestCaseVariantIdentity);
                Run("same timestamp quote cannot alter candidate features", root, TestSameTimestampQuoteIsolation);
                Run("same timestamp deal order cannot alter features", root, TestSameTimestampDealOrderIsolation);
                Run("same timestamp barrier order is ambiguous", root, TestSameTimestampBarrierAmbiguity);
                Run("future suffix changes labels but not prior features", root, TestFutureSuffixIsolation);
                Run("mismatched file pair is rejected", root, TestMismatchedPairRejected);
                Run("disjoint source ranges are rejected", root, TestDisjointRangesRejected);
                Run("unknown deal side is rejected", root, TestUnknownSideRejected);
                Run("negative quote change count is rejected", root, TestNegativeQuoteCountRejected);
                Run("gzip QSH pair is supported", root, TestGzipPair);
                Run("deflate QSH pair is supported", root, TestDeflatePair);
                Run("truncated QSH frame is rejected", root, TestTruncatedFrameRejected);
                Run("missing inputs retain audit artifacts", root, TestMissingInputs);
                Run("malformed headers retain both role identities", root, TestMalformedHeaders);
                Run("locked input retains audit artifacts", root, TestLockedInput);
                Run("hash and replay retain one file handle", root, TestPinnedInput);
                Run("research spec separates observation identities", root, TestSpecIdentity);
                Run("exact feature formulas and top N", root, TestFeatureFormulas);
                Run("book age boundary and missing book", root, TestBookAgeBoundary);
                Run("summary layout and bilingual values", root, TestSummaryLayout);
                Run("chart navigation reaches entire history", root, TestChartNavigation);
                Run("larger chart bars preserve OHLC and last snapshot", root, TestLargerChartBars);
                Run("all chart timeframes render without changing research", root, TestChartTimeFrames);
                Run("pointer zoom and source time ticks", root, TestTimeAxis);
                Run("archive catalog dates and hostile links", root, TestArchiveCatalog);
                Run("archive complete pair publication and reuse", root, TestArchivePair);
                Run("archive transfer failures preserve existing data", root, TestArchiveFailures);
                Run("archive cancellation discards partial pair", root, TestArchiveCancellation);
                Run("archive handoff compares exact instrument identity", root, TestArchiveHandoff);
                Run("archive instructions and status remain readable in themes", root, TestArchiveTextContrast);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }

            Console.WriteLine("Order Flow Research tests: " + _passed + " passed, " + _failed + " failed.");
            return _failed == 0 ? 0 : 1;
        }

        private static void Run(string name, string root, Action<string> test)
        {
            string testRoot = Path.Combine(root, Sanitize(name));
            Directory.CreateDirectory(testRoot);

            try
            {
                test(testRoot);
                _passed++;
                Console.WriteLine("PASS " + name);
            }
            catch (Exception error)
            {
                _failed++;
                Console.WriteLine("FAIL " + name + Environment.NewLine + error);
            }
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
            result.ArtifactDirectory = @"C:\Research_runs\QSH_STEP_OVERRIDE";
            result.Quality.FirstDealTime = new DateTime(2026, 9, 18, 12, 34, 56, 789);
            result.Quality.LastDealTime = result.Quality.FirstDealTime;
            result.Quality.DealCount = 21194;
            result.InputHash = "hash_with_underscore:123";
            result.Quality.Issues.Add(new OrderFlowQualityIssue { ReasonCode = "QSH_STEP_OVERRIDE", Message = "detail:with_underscore" });
            foreach (bool russian in new bool[] { false, true })
            {
                string text = OrderFlowResearchUi.BuildSummary(result, russian);
                AssertTrue(text.Contains(result.ArtifactDirectory), "Full Windows path survives localization.");
                AssertTrue(text.Contains("12:34:56.789") && text.Contains("21194"), "Times and totals survive localization.");
                AssertTrue(text.Contains(result.InputHash) && text.Contains("QSH_STEP_OVERRIDE") && text.Contains("detail:with_underscore"), "Hashes and reasons survive localization.");
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
                ChartMinute(day.AddHours(23).AddMinutes(58), 100, 105, 99, 104, 10, -2, 0.1m, true, false, 100),
                ChartMinute(day.AddHours(23).AddMinutes(59), 104, 110, 97, 108, 20, 7, 0.2m, false, false, -1),
                ChartMinute(day.AddDays(1), 200, 207, 198, 202, 30, -12, 0.3m, true, true, 2000),
                ChartMinute(day.AddDays(1).AddMinutes(1), 202, 209, 199, 208, 40, -5, 0.4m, true, false, 500),
                new OrderFlowDisplayBar { TimeStart = day.AddDays(1).AddHours(4), HasTrades = false },
                ChartMinute(day.AddDays(1).AddHours(8).AddMinutes(1), 300, 310, 280, 301, 50, 49, 0.5m, true, true, 9999)
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
                AssertFalse(bars[0].BookAvailable, "Last missing book replaces preceding available book");
                AssertEqual(-1L, bars[0].BookAgeMilliseconds, "Missing age preserved");
                AssertEqual(70m, bars[1].Volume, "Second interval volume");
                AssertEqual(-17m, bars[1].Delta, "Second interval delta");
                AssertTrue(bars[1].BookAvailable && bars[1].BookStale == false, "Last fresh book replaces stale state");
                AssertEqual(500L, bars[1].BookAgeMilliseconds, "Age is not extended to aggregate end");
                AssertEqual(0.4m, bars[1].BookImbalance, "Last book imbalance preserved");
                AssertTrue(bars[2].BookStale, "Final partial bar retains stale state");
                AssertEqual(301m, bars[2].Close, "Final partial bar retained");
                AssertFalse(ReferenceEquals(minutes[0], bars[0]), "Resampling creates detached objects");
            }
            AssertEqual(original, JsonSerializer.Serialize(minutes), "Minute DTOs remain unchanged");
        }

        private static OrderFlowDisplayBar ChartMinute(DateTime time, decimal open, decimal high, decimal low,
            decimal close, decimal volume, decimal delta, decimal response, bool available, bool stale, long age)
        {
            return new OrderFlowDisplayBar { TimeFrame = OrderFlowDisplayTimeFrame.Min1,
                TimeStart = time, TimeEnd = time.AddMinutes(1), HasTrades = true,
                Open = open, High = high, Low = low, Close = close, Volume = volume, Delta = delta,
                PriceResponse = response, BookImbalance = available ? response : 0,
                BookAvailable = available, BookStale = stale, BookAgeMilliseconds = age };
        }

        private static void TestChartTimeFrames(string root)
        {
            SyntheticPair pair = SyntheticQshFactory.Create(root, "chart-timeframes", 101, 1000, 10, Side.Sell);
            OrderFlowResearchRequest request = CreateRequest(pair, Path.Combine(root, "output"));
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
            AssertEqual(9, frames.Length, "Three existing and six requested timeframes");
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
                ChartMinute(new DateTime(2026, 9, 20, 0, 1, 0), 100, 101, 99, 100, 1, 1, 1, true, false, 1),
                ChartMinute(new DateTime(2026, 9, 20, 8, 1, 0), 100, 101, 99, 100, 1, 1, 1, true, false, 1)
            };
            chart.SetResult(replacement);
            AssertEqual(2, chart.TotalBars, "Changing result invalidates the H4 cache");
            chart.SetResult(new OrderFlowResearchResult());
            AssertEqual(0, chart.TotalBars, "Rejected or empty result clears derived history");
            AssertTrue(Application.Current == null, "No Application or Window was started");
        }

        private static void TestValidCausalReplay(string root)
        {
            SyntheticPair pair = SyntheticQshFactory.Create(root, "base", 101, 1000, 10, Side.Sell);
            OrderFlowResearchResult result = RunPair(pair, Path.Combine(root, "output"));

            AssertTrue(result.Quality.ResearchAccepted, "Valid synthetic pair must be accepted.");
            AssertEqual(4L, result.Quality.DealCount, "Deal count");
            AssertEqual(3L, result.Quality.QuoteCount, "Quote count");
            AssertEqual(1, result.Candidates.Count, "Candidate count");

            OrderFlowCandidate candidate = result.Candidates[0];
            AssertEqual(OrderFlowDirection.Long, candidate.Direction, "Candidate direction");
            OrderFlowObservation observation = result.Observations.Find(
                item => item.CandidateId == candidate.CandidateId);
            AssertNotNull(observation, "Candidate observation");
            AssertEqual(pair.StartTime, observation.Features.BookTime.Value,
                "Candidate must use the previous closed quote, not its same-timestamp quote.");
            AssertEqual(0m, observation.Features.BookImbalance, "Previous-book imbalance");

            OrderFlowMarketPathLabel label = result.Labels.Single(
                item => item.CandidateId == candidate.CandidateId && item.HorizonSeconds == 5);
            AssertTrue(label.IsComplete, "Five-second label must be complete.");
            AssertEqual(OrderFlowBarrierOutcome.TargetFirst, label.Outcome, "Future barrier outcome");
            AssertTrue(label.MaximumFavorableExcursion >= 1m, "MFE must include the future price rise.");

            AssertTrue(File.Exists(Path.Combine(result.ArtifactDirectory, "observations.csv")),
                "Causal observations artifact");
            AssertTrue(File.Exists(Path.Combine(result.ArtifactDirectory, "market-path-labels.csv")),
                "Future labels artifact");

            string observationHeader = File.ReadLines(Path.Combine(result.ArtifactDirectory, "observations.csv")).First();
            string labelHeader = File.ReadLines(Path.Combine(result.ArtifactDirectory, "market-path-labels.csv")).First();
            AssertFalse(observationHeader.Contains("outcome", StringComparison.OrdinalIgnoreCase),
                "Observation schema must not contain future outcome.");
            AssertFalse(observationHeader.Contains("mfe", StringComparison.OrdinalIgnoreCase),
                "Observation schema must not contain future MFE.");
            AssertFalse(labelHeader.Contains("book_imbalance", StringComparison.OrdinalIgnoreCase),
                "Label schema must not duplicate causal book features.");
            AssertEqual(3, result.Bars.Count, "Diagnostic timeframe count");
            AssertManifestRole(result, "Deals", pair.DealsPath, true);
            AssertManifestRole(result, "Quotes", pair.QuotesPath, true);
            using (JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(result.ArtifactDirectory, "manifest.json"))))
            using (JsonDocument quality = JsonDocument.Parse(File.ReadAllText(Path.Combine(result.ArtifactDirectory, "quality.json"))))
            {
                JsonElement data = manifest.RootElement;
                AssertTrue(data.GetProperty("ResearchAccepted").GetBoolean(), "Manifest acceptance");
                AssertEqual(result.InputHash, data.GetProperty("InputHash").GetString(), "Manifest input identity");
                AssertEqual(result.ResearchSpecHash, data.GetProperty("ResearchSpecHash").GetString(), "Manifest spec identity");
                AssertEqual(result.NormalizedEventHash, data.GetProperty("NormalizedEventHash").GetString(), "Manifest event hash");
                AssertEqual(result.FeatureHash, data.GetProperty("FeatureHash").GetString(), "Manifest feature hash");
                AssertEqual(result.CandidateHash, data.GetProperty("CandidateHash").GetString(), "Manifest candidate hash");
                AssertEqual(OrderFlowResearchSchema.ArtifactSchemaVersion, data.GetProperty("ArtifactSchemaVersion").GetString(), "Artifact schema");
                AssertEqual(OrderFlowResearchSchema.CandidateDetectorVersion, data.GetProperty("CandidateDetectorVersion").GetString(), "Detector version");
                AssertEqual(10, data.GetProperty("FeatureWindowSeconds").GetInt32(), "Frozen feature window");
                AssertEqual(100m, data.GetProperty("MinimumAbsoluteDelta").GetDecimal(), "Frozen delta threshold");
                AssertEqual(5, data.GetProperty("LabelHorizonsSeconds")[0].GetInt32(), "Frozen label horizon");
                AssertEqual(result.Observations.Count, data.GetProperty("ObservationCount").GetInt32(), "Manifest observations");
                AssertEqual(result.Candidates.Count, data.GetProperty("CandidateCount").GetInt32(), "Manifest candidates");
                AssertEqual(result.Labels.Count, data.GetProperty("LabelCount").GetInt32(), "Manifest labels");
                JsonElement report = quality.RootElement;
                AssertTrue(report.GetProperty("ResearchAccepted").GetBoolean(), "Quality acceptance");
                AssertFalse(report.GetProperty("ExecutionMetadataComplete").GetBoolean(), "Execution evidence unavailable");
                AssertEqual(4L, report.GetProperty("DealCount").GetInt64(), "Serialized Deals count");
                AssertEqual(3L, report.GetProperty("QuoteCount").GetInt64(), "Serialized Quotes count");
                AssertEqual(pair.StartTime.AddSeconds(1), report.GetProperty("FirstDealTime").GetDateTime(), "First deal time");
                AssertEqual(pair.StartTime.AddSeconds(4), report.GetProperty("LastDealTime").GetDateTime(), "Last deal time");
                AssertEqual(pair.StartTime, report.GetProperty("FirstQuoteTime").GetDateTime(), "First quote time");
                AssertEqual(pair.StartTime.AddSeconds(8), report.GetProperty("LastQuoteTime").GetDateTime(), "Last quote time");
                AssertTrue(report.GetProperty("Issues").EnumerateArray().Any(item =>
                    item.GetProperty("ReasonCode").GetString() == "EXECUTION_METADATA_NOT_COLLECTED"), "Research-only warning");
            }
        }

        private static void TestDeterministicRepeat(string root)
        {
            SyntheticPair pair = SyntheticQshFactory.Create(root, "repeat", 101, 1000, 10, Side.Sell);
            string output = Path.Combine(root, "output");
            OrderFlowResearchResult first = RunPair(pair, output);
            OrderFlowResearchResult second = RunPair(pair, output);

            AssertEqual(first.NormalizedEventHash, second.NormalizedEventHash, "Event hash");
            AssertEqual(first.FeatureHash, second.FeatureHash, "Feature hash");
            AssertEqual(first.CandidateHash, second.CandidateHash, "Candidate hash");
            AssertEqual(first.ArtifactDirectory, second.ArtifactDirectory, "Idempotent artifact directory");
        }

        private static void TestCaseVariantIdentity(string root)
        {
            SyntheticPair source = SyntheticQshFactory.Create(root, "case-source", 101, 1000, 10, Side.Sell);
            string caseDirectory = Path.Combine(root, "case-variant");
            Directory.CreateDirectory(caseDirectory);
            string dealsPath = Path.Combine(caseDirectory, "test.2026-09-18.deals.qsh");
            string quotesPath = Path.Combine(caseDirectory, "test.2026-09-18.quotes.qsh");
            File.Copy(source.DealsPath, dealsPath);
            File.Copy(source.QuotesPath, quotesPath);

            SyntheticPair caseVariant = new SyntheticPair();
            caseVariant.DealsPath = dealsPath;
            caseVariant.QuotesPath = quotesPath;
            caseVariant.StartTime = source.StartTime;
            string output = Path.Combine(root, "output");

            OrderFlowResearchResult first = RunPair(source, output);
            OrderFlowResearchResult second = RunPair(caseVariant, output);

            AssertTrue(first.Quality.ResearchAccepted && second.Quality.ResearchAccepted,
                "Both case variants must be accepted.");
            AssertEqual(first.InputHash, second.InputHash, "Canonical case-variant input identity");
            AssertEqual(first.DealsHeader.FileName, second.DealsHeader.FileName,
                "Canonical Deals manifest filename");
            AssertEqual(first.QuotesHeader.FileName, second.QuotesHeader.FileName,
                "Canonical Quotes manifest filename");
            AssertEqual(first.ArtifactDirectory, second.ArtifactDirectory,
                "Case variants must reuse one byte-identical artifact bundle");
        }

        private static void TestShortCandidate(string root)
        {
            SyntheticPair pair = SyntheticQshFactory.Create(root, "short", 99, 1000, 10, Side.Buy);
            OrderFlowResearchResult result = RunPair(pair, Path.Combine(root, "output"));

            AssertTrue(result.Quality.ResearchAccepted, "Valid mirrored pair must be accepted.");
            AssertEqual(1, result.Candidates.Count, "Short candidate count");
            AssertEqual(OrderFlowDirection.Short, result.Candidates[0].Direction, "Mirrored candidate direction");
            OrderFlowMarketPathLabel label = result.Labels.Single(item => item.HorizonSeconds == 5);
            AssertEqual(OrderFlowBarrierOutcome.TargetFirst, label.Outcome, "Short favorable barrier outcome");
        }

        private static void TestSameTimestampQuoteIsolation(string root)
        {
            SyntheticPair positiveBook = SyntheticQshFactory.Create(root, "book-positive", 101, 1000, 10, Side.Sell);
            SyntheticPair negativeBook = SyntheticQshFactory.Create(root, "book-negative", 101, 10, 1000, Side.Sell);
            OrderFlowResearchResult first = RunPair(positiveBook, Path.Combine(root, "output-positive"));
            OrderFlowResearchResult second = RunPair(negativeBook, Path.Combine(root, "output-negative"));

            OrderFlowFeatureSnapshot firstFeature = GetOnlyCandidateFeature(first);
            OrderFlowFeatureSnapshot secondFeature = GetOnlyCandidateFeature(second);
            AssertEqual(firstFeature.Time, secondFeature.Time, "Candidate time");
            AssertEqual(firstFeature.Delta, secondFeature.Delta, "Candidate delta");
            AssertEqual(firstFeature.PriceChange, secondFeature.PriceChange, "Candidate price change");
            AssertEqual(firstFeature.BookTime, secondFeature.BookTime, "Previous closed book time");
            AssertEqual(firstFeature.BookImbalance, secondFeature.BookImbalance,
                "Same-timestamp quote must not alter candidate imbalance.");
            AssertFalse(first.NormalizedEventHash == second.NormalizedEventHash,
                "Different quote payloads must change the normalized event hash.");
        }

        private static void TestFutureSuffixIsolation(string root)
        {
            SyntheticPair favorable = SyntheticQshFactory.Create(root, "future-up", 101, 1000, 10, Side.Sell);
            SyntheticPair adverse = SyntheticQshFactory.Create(root, "future-down", 99, 1000, 10, Side.Sell);
            OrderFlowResearchResult upResult = RunPair(favorable, Path.Combine(root, "output-up"));
            OrderFlowResearchResult downResult = RunPair(adverse, Path.Combine(root, "output-down"));

            OrderFlowFeatureSnapshot upFeature = GetOnlyCandidateFeature(upResult);
            OrderFlowFeatureSnapshot downFeature = GetOnlyCandidateFeature(downResult);
            AssertEqual(upFeature.Time, downFeature.Time, "Prior snapshot time");
            AssertEqual(upFeature.ReferencePrice, downFeature.ReferencePrice, "Prior reference price");
            AssertEqual(upFeature.Delta, downFeature.Delta, "Prior delta");
            AssertEqual(upFeature.PriceChange, downFeature.PriceChange, "Prior price change");
            AssertEqual(upFeature.BookImbalance, downFeature.BookImbalance, "Prior book imbalance");

            OrderFlowMarketPathLabel upLabel = upResult.Labels.Single(item => item.HorizonSeconds == 5);
            OrderFlowMarketPathLabel downLabel = downResult.Labels.Single(item => item.HorizonSeconds == 5);
            AssertEqual(OrderFlowBarrierOutcome.TargetFirst, upLabel.Outcome, "Favorable suffix label");
            AssertEqual(OrderFlowBarrierOutcome.InvalidationFirst, downLabel.Outcome, "Adverse suffix label");
            AssertEqual(OrderFlowDirection.Long, downResult.Candidates.Single().Direction, "Failed Long retains its direction");
            AssertFalse(downResult.Observations.Any(item => item.Direction == OrderFlowDirection.Short),
                "A failed Long label must not produce a Short observation.");
            AssertFalse(downResult.Journal.Any(item => item.ReasonCode == "BUY_FLOW_PRICE_RESILIENCE"),
                "A failed Long label must not create a Short candidate journal event.");
        }

        private static void TestSameTimestampDealOrderIsolation(string root)
        {
            SyntheticPair ascending = SyntheticQshFactory.CreateSameTimestampPermutation(
                root, "deals-ascending", false);
            SyntheticPair descending = SyntheticQshFactory.CreateSameTimestampPermutation(
                root, "deals-descending", true);
            OrderFlowResearchResult first = RunPair(ascending, Path.Combine(root, "output-ascending"));
            OrderFlowResearchResult second = RunPair(descending, Path.Combine(root, "output-descending"));

            OrderFlowFeatureSnapshot firstFeature = GetOnlyCandidateFeature(first);
            OrderFlowFeatureSnapshot secondFeature = GetOnlyCandidateFeature(second);
            AssertEqual(firstFeature.ReferencePrice, secondFeature.ReferencePrice,
                "Bucket VWAP must not depend on same-timestamp deal order.");
            AssertEqual(firstFeature.PriceChange, secondFeature.PriceChange,
                "Price change must not depend on same-timestamp deal order.");
            AssertEqual(first.FeatureHash, second.FeatureHash,
                "Causal feature hash must be invariant to same-timestamp deal permutation.");
            AssertFalse(first.NormalizedEventHash == second.NormalizedEventHash,
                "Normalized event hash must preserve source file order.");
            AssertEqual(99m, first.Bars[OrderFlowDisplayTimeFrame.Sec15][0].Open,
                "Display OHLC must preserve the first source deal.");
            AssertEqual(101m, second.Bars[OrderFlowDisplayTimeFrame.Sec15][0].Open,
                "Display OHLC must reflect the reversed source order.");
        }

        private static void TestSameTimestampBarrierAmbiguity(string root)
        {
            SyntheticPair targetFirst = SyntheticQshFactory.CreateBarrierTie(
                root, "barrier-target-first", false);
            SyntheticPair invalidationFirst = SyntheticQshFactory.CreateBarrierTie(
                root, "barrier-invalidation-first", true);
            OrderFlowResearchResult first = RunPair(targetFirst, Path.Combine(root, "output-target-first"));
            OrderFlowResearchResult second = RunPair(invalidationFirst, Path.Combine(root, "output-invalidation-first"));

            OrderFlowMarketPathLabel firstLabel = first.Labels.Single(item => item.HorizonSeconds == 5);
            OrderFlowMarketPathLabel secondLabel = second.Labels.Single(item => item.HorizonSeconds == 5);
            AssertEqual(OrderFlowBarrierOutcome.AmbiguousSameTimestamp, firstLabel.Outcome,
                "Target-first source order remains epistemically ambiguous at one timestamp.");
            AssertEqual(OrderFlowBarrierOutcome.AmbiguousSameTimestamp, secondLabel.Outcome,
                "Invalidation-first source order remains epistemically ambiguous at one timestamp.");
            AssertEqual(firstLabel.SignedReturn, secondLabel.SignedReturn,
                "Closing bucket VWAP must be order independent.");
        }

        private static void TestMismatchedPairRejected(string root)
        {
            SyntheticPair pair = SyntheticQshFactory.Create(root, "mismatch-source", 101, 1000, 10, Side.Sell);
            string output = Path.Combine(root, "output");
            OrderFlowResearchResult validResult = RunPair(pair, output);
            string mismatchDirectory = Path.Combine(root, "mismatch");
            Directory.CreateDirectory(mismatchDirectory);
            string mismatchedQuotes = Path.Combine(mismatchDirectory, "OTHER.2026-09-18.Quotes.qsh");
            File.Copy(pair.QuotesPath, mismatchedQuotes);

            SyntheticPair mismatchedPair = new SyntheticPair();
            mismatchedPair.DealsPath = pair.DealsPath;
            mismatchedPair.QuotesPath = mismatchedQuotes;
            mismatchedPair.StartTime = pair.StartTime;
            OrderFlowResearchResult result = RunPair(mismatchedPair, output);

            AssertFalse(result.Quality.ResearchAccepted, "Mismatched pair must be rejected.");
            AssertTrue(result.Quality.Issues.Any(issue => issue.ReasonCode == "PAIR_FILE_INSTRUMENT_MISMATCH"),
                "Mismatched filename reason code");
            AssertFalse(validResult.InputHash == result.InputHash,
                "Semantic file names and roles must participate in input identity.");
            AssertFalse(validResult.ArtifactDirectory == result.ArtifactDirectory,
                "Rejected renamed input must not collide with a valid artifact bundle.");
            AssertTrue(Directory.Exists(result.ArtifactDirectory), "Rejected pair audit bundle");
        }

        private static void TestDisjointRangesRejected(string root)
        {
            SyntheticPair pair = SyntheticQshFactory.CreateDisjointRanges(root, "disjoint");
            OrderFlowResearchResult result = RunPair(pair, Path.Combine(root, "output"));

            AssertFalse(result.Quality.ResearchAccepted, "Non-overlapping source ranges must be rejected.");
            AssertTrue(result.Quality.Issues.Any(issue => issue.ReasonCode == "PAIR_TIME_RANGES_DISJOINT"),
                "Disjoint range reason code");
        }

        private static void TestUnknownSideRejected(string root)
        {
            SyntheticPair pair = SyntheticQshFactory.Create(root, "unknown-side", 101, 1000, 10, Side.None);
            OrderFlowResearchResult result = RunPair(pair, Path.Combine(root, "output"));

            AssertFalse(result.Quality.ResearchAccepted, "Unknown side must reject research acceptance.");
            AssertTrue(result.Quality.UnknownSideCount > 0, "Unknown side counter");
            AssertTrue(result.Quality.Issues.Any(issue => issue.ReasonCode == "DEAL_SIDE_UNKNOWN"),
                "Unknown side reason code");
        }

        private static void TestGzipPair(string root)
        {
            SyntheticPair pair = SyntheticQshFactory.Create(root, "gzip-raw", 101, 1000, 10, Side.Sell);
            SyntheticPair gzipPair = SyntheticQshFactory.CompressGzip(root, pair);
            OrderFlowResearchResult result = RunPair(gzipPair, Path.Combine(root, "output"));
            AssertTrue(result.Quality.ResearchAccepted, "GZip QSH pair must be accepted.");
            AssertEqual(1, result.Candidates.Count, "GZip candidate count");
        }

        private static void TestNegativeQuoteCountRejected(string root)
        {
            SyntheticPair pair = SyntheticQshFactory.CreateNegativeQuoteCount(root, "negative-quote-count");
            OrderFlowResearchResult result = RunPair(pair, Path.Combine(root, "output"));

            AssertFalse(result.Quality.ResearchAccepted, "A negative Quotes change count must be rejected.");
            AssertTrue(result.Quality.Issues.Any(issue => issue.ReasonCode == "QSH_INVALID"),
                "Malformed Quotes reason code");
        }

        private static void TestDeflatePair(string root)
        {
            SyntheticPair pair = SyntheticQshFactory.Create(root, "deflate-raw", 101, 1000, 10, Side.Sell);
            SyntheticPair deflatePair = SyntheticQshFactory.CompressDeflate(root, pair);
            OrderFlowResearchResult result = RunPair(deflatePair, Path.Combine(root, "output"));
            AssertTrue(result.Quality.ResearchAccepted, "Deflate QSH pair must be accepted.");
            AssertEqual(1, result.Candidates.Count, "Deflate candidate count");
        }

        private static void TestTruncatedFrameRejected(string root)
        {
            SyntheticPair pair = SyntheticQshFactory.Create(root, "truncated-source", 101, 1000, 10, Side.Sell);
            string directory = Path.Combine(root, "truncated");
            Directory.CreateDirectory(directory);
            string dealsPath = Path.Combine(directory, "TEST.2026-09-18.Deals.qsh");
            string quotesPath = Path.Combine(directory, "TEST.2026-09-18.Quotes.qsh");
            File.Copy(pair.DealsPath, dealsPath);
            File.Copy(pair.QuotesPath, quotesPath);

            using (FileStream stream = new FileStream(dealsPath, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                stream.SetLength(stream.Length - 1);
            }

            SyntheticPair truncatedPair = new SyntheticPair();
            truncatedPair.DealsPath = dealsPath;
            truncatedPair.QuotesPath = quotesPath;
            truncatedPair.StartTime = pair.StartTime;
            OrderFlowResearchResult result = RunPair(truncatedPair, Path.Combine(root, "output"));

            AssertFalse(result.Quality.ResearchAccepted, "A QSH frame truncated after its header must be rejected.");
            AssertTrue(result.Quality.Issues.Any(issue => issue.ReasonCode == "QSH_INVALID"),
                "Truncated frame reason code");
        }

        private static void TestMissingInputs(string root)
        {
            for (int role = 0; role < 2; role++)
            {
                SyntheticPair pair = SyntheticQshFactory.Create(root, "missing-" + role, 101, 1000, 10, Side.Sell);
                string missing = role == 0 ? pair.DealsPath : pair.QuotesPath;
                File.Delete(missing);
                missing = Path.Combine(root, "missing-parent-" + role, Path.GetFileName(missing));
                if (role == 0)
                {
                    pair.DealsPath = missing;
                }
                else
                {
                    pair.QuotesPath = missing;
                }
                string output = Path.Combine(root, "output-" + role);
                OrderFlowResearchResult result = RunPair(pair, output);
                AssertRejectedBundle(result, "QSH_FILE_MISSING");
                AssertManifestRole(result, role == 0 ? "Quotes" : "Deals", role == 0 ? pair.QuotesPath : pair.DealsPath, true);
                using (JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(result.ArtifactDirectory, "manifest.json"))))
                {
                    JsonElement unavailable = manifest.RootElement.GetProperty(role == 0 ? "Deals" : "Quotes");
                    AssertEqual(Path.GetFileName(missing).ToUpperInvariant(), unavailable.GetProperty("FileName").GetString(), "Missing role filename");
                    AssertEqual(JsonValueKind.Null, unavailable.GetProperty("Sha256").ValueKind, "Missing checksum is unknown");
                    AssertEqual(JsonValueKind.Null, unavailable.GetProperty("FileSize").ValueKind, "Missing size is unknown");
                    AssertFalse(unavailable.GetProperty("HeaderComplete").GetBoolean(), "Missing header not decoded");
                    AssertEqual("QSH_FILE_MISSING", unavailable.GetProperty("FailureReasonCode").GetString(), "Missing role reason");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(missing));
                AssertEqual(result.ArtifactDirectory, RunPair(pair, output).ArtifactDirectory,
                    "Missing directory and missing file reuse the same byte-identical rejection bundle");
            }
        }

        private static void TestMalformedHeaders(string root)
        {
            for (int role = 0; role < 2; role++)
            {
                for (int corruption = 0; corruption < 3; corruption++)
                {
                    SyntheticPair pair = SyntheticQshFactory.Create(root, "malformed-" + role + "-" + corruption, 101, 1000, 10, Side.Sell);
                    string path = role == 0 ? pair.DealsPath : pair.QuotesPath;
                    byte[] bytes = File.ReadAllBytes(path);
                    if (corruption == 0)
                    {
                        File.WriteAllBytes(path, new byte[] { 0, 1, 2 });
                    }
                    else if (corruption == 1)
                    {
                        bytes[Encoding.UTF8.GetByteCount("QScalp History Data")] = 3;
                        File.WriteAllBytes(path, bytes);
                    }
                    else
                    {
                        File.WriteAllBytes(path, bytes.Take(Encoding.UTF8.GetByteCount("QScalp History Data") + 2).ToArray());
                    }

                    OrderFlowResearchResult result = RunPair(pair, Path.Combine(root, "output-" + role + "-" + corruption));
                    AssertRejectedBundle(result, "QSH_INVALID");
                    AssertManifestRole(result, "Deals", pair.DealsPath, role != 0);
                    AssertManifestRole(result, "Quotes", pair.QuotesPath, role != 1);
                    using (FileStream released = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                        AssertTrue(released.CanWrite, "Malformed input handle released after rejection");
                    }
                }
            }
        }

        private static void TestLockedInput(string root)
        {
            SyntheticPair pair = SyntheticQshFactory.Create(root, "locked", 101, 1000, 10, Side.Sell);
            using (FileStream blocker = new FileStream(pair.DealsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                OrderFlowResearchResult result = RunPair(pair, Path.Combine(root, "output"));
                AssertRejectedBundle(result, "QSH_IO_ERROR");
                AssertManifestRole(result, "Quotes", pair.QuotesPath, true);
            }

            AssertTrue(RunPair(pair, Path.Combine(root, "output")).Quality.ResearchAccepted,
                "Releasing an input lock permits a separate accepted bundle.");
        }

        private static void TestPinnedInput(string root)
        {
            SyntheticPair raw = SyntheticQshFactory.Create(root, "pinned", 101, 1000, 10, Side.Sell);
            SyntheticPair[] pairs = new SyntheticPair[] { raw, SyntheticQshFactory.CompressGzip(root, raw), SyntheticQshFactory.CompressDeflate(root, raw) };
            foreach (SyntheticPair pair in pairs)
            {
                string expected = StoredHash(pair.DealsPath);
                OrderFlowQshHeader header = new OrderFlowQshHeader();
                header.FileName = Path.GetFileName(pair.DealsPath).ToUpperInvariant();
                header.FileType = "Deals";
                using (OrderFlowDealsQshReader reader = new OrderFlowDealsQshReader(pair.DealsPath, 0, 0, header))
                {
                    AssertEqual(expected, header.Sha256, "Hash of stored raw/compressed bytes");
                    bool writeBlocked = false;
                    try
                    {
                        using (FileStream writer = new FileStream(pair.DealsPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
                        {
                            AssertTrue(writer.CanWrite, "Probe owns a writable handle");
                        }
                    }
                    catch (IOException)
                    {
                        writeBlocked = true;
                    }

                    AssertTrue(writeBlocked, "Input must stay protected against writers between hashing and replay.");
                    OrderFlowDeal deal;
                    AssertTrue(reader.TryRead(out deal), "Pinned stream remains available for decoding");
                    AssertEqual(100m, deal.Price, "Pinned payload price");
                }

                using (FileStream released = new FileStream(pair.DealsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    AssertEqual(header.FileSize.Value, released.Length, "Input handle released after reader disposal");
                }
            }
        }

        private static void TestSpecIdentity(string root)
        {
            SyntheticPair pair = SyntheticQshFactory.Create(root, "spec", 101, 1000, 10, Side.Sell);
            string output = Path.Combine(root, "output");
            OrderFlowResearchResult first = RunPair(pair, output);
            OrderFlowResearchRequest changed = CreateRequest(pair, output);
            changed.TopBookLevels = 4;
            OrderFlowResearchRunner runner = new OrderFlowResearchRunner();
            OrderFlowResearchResult second = runner.RunAndExport(changed, CancellationToken.None);
            OrderFlowResearchResult repeat = runner.RunAndExport(changed, CancellationToken.None);
            AssertEqual(first.InputHash, second.InputHash, "Spec does not change input identity");
            AssertEqual(first.NormalizedEventHash, second.NormalizedEventHash, "Spec does not change source events");
            AssertEqual(first.FeatureHash, second.FeatureHash, "One-level fixture gives identical features at top-4/top-5");
            AssertFalse(first.ResearchSpecHash == second.ResearchSpecHash, "Specs have distinct hashes");
            AssertFalse(first.ArtifactDirectory == second.ArtifactDirectory, "Specs have distinct artifact bundles");
            AssertTrue(first.Observations.Any(item => item.ObservationType == OrderFlowObservationType.Background), "Background identity covered");
            AssertTrue(first.Observations.Any(item => item.ObservationType == OrderFlowObservationType.Candidate), "Candidate identity covered");
            HashSet<string> originalKeys = new HashSet<string>(first.Observations.Select(item => item.ObservationKey));
            AssertFalse(second.Observations.Any(item => originalKeys.Contains(item.ObservationKey)), "Observation identities cannot collide across specs");
            AssertTrue(second.Observations.Select(item => item.ObservationKey).SequenceEqual(repeat.Observations.Select(item => item.ObservationKey)), "Same spec reproduces identities");
            AssertTrue(second.Observations.All(item => item.ObservationKey.Contains("|" + OrderFlowResearchSchema.CandidateDetectorVersion + "|", StringComparison.Ordinal)), "Keys explicitly identify detector version");
        }

        private static void TestFeatureFormulas(string root)
        {
            DateTime start = new DateTime(2026, 9, 18, 10, 0, 0, DateTimeKind.Utc);
            OrderFlowResearchRequest request = new OrderFlowResearchRequest();
            request.FeatureWindowSeconds = 1;
            request.TopBookLevels = 2;
            request.MaximumBookAgeMilliseconds = 2000;
            OrderFlowBookSnapshot book = FormulaBook(start);
            OrderFlowFeatureWindow window = new OrderFlowFeatureWindow();
            window.Build(FormulaBucket(start.AddSeconds(1), 1, 100, 10, Side.Buy), book, request);
            OrderFlowFeatureSnapshot feature = window.Build(FormulaBucket(start.AddSeconds(2), 2, 102, 30, Side.Sell), book, request);
            AssertEqual(10m, feature.BuyVolume, "Window includes left boundary");
            AssertEqual(30m, feature.SellVolume, "Sell volume");
            AssertEqual(-20m, feature.Delta, "Signed delta");
            AssertEqual(2, feature.TradeCount, "Window deal count");
            AssertEqual(102m, feature.ReferencePrice, "Current bucket reference");
            AssertEqual(2m, feature.PriceChange, "Change from earliest bucket VWAP");
            AssertEqual(0.1m, feature.PriceResponse, "Response divides by absolute delta");
            AssertEqual(2m, feature.Spread, "Absolute price spread");
            AssertEqual(0.5m, feature.BookImbalance, "Top-2 excludes third-level volumes");
            AssertEqual(2000L, feature.BookAgeMilliseconds, "Book age milliseconds");
            OrderFlowFeatureSnapshot next = window.Build(FormulaBucket(start.AddMilliseconds(2001), 3, 104, 30, Side.Buy), book, request);
            AssertEqual(2, next.TradeCount, "Evicts only deals strictly before cutoff");
            AssertEqual(0m, next.Delta, "Balanced flow after eviction");
            AssertEqual(2m, next.PriceChange, "Reference moves after oldest bucket eviction");
            AssertEqual(0m, next.PriceResponse, "Zero delta response avoids division by zero");
            AssertEqual(10m, feature.BuyVolume, "Earlier snapshot remains detached");
        }

        private static void TestBookAgeBoundary(string root)
        {
            DateTime start = new DateTime(2026, 9, 18, 10, 0, 0, DateTimeKind.Utc);
            OrderFlowResearchRequest request = new OrderFlowResearchRequest();
            request.FeatureWindowSeconds = 10;
            request.TopBookLevels = 2;
            request.MaximumBookAgeMilliseconds = 2000;
            OrderFlowFeatureWindow window = new OrderFlowFeatureWindow();
            OrderFlowBookSnapshot book = FormulaBook(start);
            OrderFlowFeatureSnapshot exact = window.Build(FormulaBucket(start.AddSeconds(2), 1, 100, 10, Side.Buy), book, request);
            AssertFalse(exact.BookStale, "Book remains fresh exactly at the age limit");
            AssertEqual("OK", exact.DataQualityCode, "Boundary quality");
            OrderFlowFeatureSnapshot stale = window.Build(FormulaBucket(start.AddMilliseconds(2001), 2, 100, 10, Side.Buy), book, request);
            AssertTrue(stale.BookStale, "Book becomes stale one millisecond beyond limit");
            AssertEqual(2001L, stale.BookAgeMilliseconds, "Stale age");
            AssertEqual("BOOK_STALE", stale.DataQualityCode, "Staleness is explicit");
            OrderFlowBookSnapshot invalid = FormulaBook(start);
            invalid.IsValid = false;
            foreach (OrderFlowBookSnapshot unavailable in new OrderFlowBookSnapshot[] { null, invalid, FormulaBook(start.AddSeconds(3)) })
            {
                OrderFlowFeatureSnapshot missing = new OrderFlowFeatureWindow().Build(FormulaBucket(start.AddSeconds(3), 3, 100, 10, Side.Buy), unavailable, request);
                AssertFalse(missing.BookAvailable, "Absent, invalid and same-timestamp book unavailable");
                AssertEqual(-1L, missing.BookAgeMilliseconds, "Unknown book age sentinel");
                AssertEqual("BOOK_NOT_CAUSALLY_AVAILABLE", missing.DataQualityCode, "Unavailable reason");
            }
        }

        private static OrderFlowBookSnapshot FormulaBook(DateTime time)
        {
            OrderFlowBookSnapshot book = new OrderFlowBookSnapshot();
            book.Time = time;
            book.IsValid = true;
            book.Bids.Add(new OrderFlowBookLevel() { Price = 99, Volume = 30 });
            book.Bids.Add(new OrderFlowBookLevel() { Price = 98, Volume = 120 });
            book.Bids.Add(new OrderFlowBookLevel() { Price = 97, Volume = 1 });
            book.Asks.Add(new OrderFlowBookLevel() { Price = 101, Volume = 20 });
            book.Asks.Add(new OrderFlowBookLevel() { Price = 102, Volume = 30 });
            book.Asks.Add(new OrderFlowBookLevel() { Price = 103, Volume = 900 });
            return book;
        }

        private static OrderFlowBucket FormulaBucket(DateTime time, long sequence, decimal price, decimal volume, Side side)
        {
            OrderFlowBucket bucket = new OrderFlowBucket();
            bucket.Time = time;
            bucket.BucketSequence = sequence;
            bucket.Deals.Add(new OrderFlowDeal() { Time = time, Price = price, Volume = volume, Side = side });
            return bucket;
        }

        private static void AssertManifestRole(OrderFlowResearchResult result, string role, string path, bool complete)
        {
            using (JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(result.ArtifactDirectory, "manifest.json"))))
            {
                JsonElement input = manifest.RootElement.GetProperty(role);
                AssertEqual(role, input.GetProperty("FileType").GetString(), "Manifest role");
                AssertEqual(Path.GetFileName(path).ToUpperInvariant(), input.GetProperty("FileName").GetString(), "Canonical filename");
                AssertEqual(StoredHash(path), input.GetProperty("Sha256").GetString(), "Stored file SHA-256");
                AssertEqual(new FileInfo(path).Length, input.GetProperty("FileSize").GetInt64(), "Stored file size");
                AssertEqual(complete, input.GetProperty("HeaderComplete").GetBoolean(), "Header completion state");
                if (complete)
                {
                    AssertEqual("TEST", input.GetProperty("FileInstrument").GetString(), "Filename instrument");
                    AssertEqual("TEST", input.GetProperty("HeaderInstrument").GetString(), "Header instrument");
                    AssertEqual(new DateTime(2026, 9, 18), input.GetProperty("TradingDate").GetDateTime(), "Trading date");
                    AssertEqual(1m, input.GetProperty("EffectivePriceStep").GetDecimal(), "Price units");
                    AssertEqual(1m, input.GetProperty("EffectiveVolumeStep").GetDecimal(), "Volume units");
                }
                else
                {
                    AssertEqual("QSH_INVALID", input.GetProperty("FailureReasonCode").GetString(), "Malformed header reason");
                }
            }
        }

        private static string StoredHash(string path)
        {
            return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        }

        private static void AssertRejectedBundle(OrderFlowResearchResult result, string reason)
        {
            AssertFalse(result.Quality.ResearchAccepted, "Invalid input must reject research");
            AssertEqual(0, result.Candidates.Count, "Header failure must not run the candidate detector");
            AssertEqual(0, result.Labels.Count, "Header failure must not produce labels");
            using (JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(result.ArtifactDirectory, "manifest.json"))))
            using (JsonDocument quality = JsonDocument.Parse(File.ReadAllText(Path.Combine(result.ArtifactDirectory, "quality.json"))))
            {
                AssertFalse(manifest.RootElement.GetProperty("ResearchAccepted").GetBoolean(), "Manifest rejection");
                AssertFalse(quality.RootElement.GetProperty("ResearchAccepted").GetBoolean(), "Quality rejection");
                AssertTrue(quality.RootElement.GetProperty("Issues").EnumerateArray().Any(item => item.GetProperty("ReasonCode").GetString() == reason), "Serialized rejection reason");
            }

            AssertTrue(File.ReadAllText(Path.Combine(result.ArtifactDirectory, "event-journal.csv")).Contains("RESEARCH_REJECTED", StringComparison.Ordinal), "Journal terminal rejection");
        }

        private static OrderFlowResearchResult RunPair(SyntheticPair pair, string output)
        {
            return new OrderFlowResearchRunner().RunAndExport(CreateRequest(pair, output), CancellationToken.None);
        }

        private static OrderFlowResearchRequest CreateRequest(SyntheticPair pair, string output)
        {
            OrderFlowResearchRequest request = new OrderFlowResearchRequest();
            request.DealsFilePath = pair.DealsPath;
            request.QuotesFilePath = pair.QuotesPath;
            request.OutputRootPath = output;
            request.FeatureWindowSeconds = 10;
            request.MinimumAbsoluteDelta = 100;
            request.MinimumPriceChangeTicks = 0;
            request.TopBookLevels = 5;
            request.MaximumBookAgeMilliseconds = 5000;
            request.CandidateCooldownMilliseconds = 10000;
            request.BackgroundSampleSeconds = 30;
            request.LabelHorizonsSeconds = new List<int>() { 5 };
            request.TargetTicks = 1;
            request.InvalidationTicks = 1;

            return request;
        }

        private static OrderFlowFeatureSnapshot GetOnlyCandidateFeature(OrderFlowResearchResult result)
        {
            AssertEqual(1, result.Candidates.Count, "Expected one broad candidate");
            string candidateId = result.Candidates[0].CandidateId;
            OrderFlowObservation observation = result.Observations.Single(item => item.CandidateId == candidateId);
            return observation.Features;
        }

        private static string Sanitize(string name)
        {
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                char value = name[i];
                builder.Append(char.IsLetterOrDigit(value) ? value : '-');
            }

            return builder.ToString();
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (condition == false)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static void AssertFalse(bool condition, string message)
        {
            AssertTrue(condition == false, message);
        }

        private static void AssertNotNull(object value, string message)
        {
            AssertTrue(value != null, message);
        }

        private static void AssertEqual<T>(T expected, T actual, string message)
        {
            if (EqualityComparer<T>.Default.Equals(expected, actual) == false)
            {
                throw new InvalidOperationException(message + ". Expected " + expected + ", actual " + actual + ".");
            }
        }
    }

    internal sealed class SyntheticPair
    {
        public string DealsPath { get; set; }

        public string QuotesPath { get; set; }

        public DateTime StartTime { get; set; }
    }

    internal static class SyntheticQshFactory
    {
        private static readonly byte[] _prefix = Encoding.UTF8.GetBytes("QScalp History Data");

        public static SyntheticPair Create(string root, string name, decimal futurePrice,
            long sameTimestampBidVolume, long sameTimestampAskVolume, Side candidateDealSide)
        {
            string directory = Path.Combine(root, name);
            Directory.CreateDirectory(directory);
            string dealsPath = Path.Combine(directory, "TEST.2026-09-18.Deals.qsh");
            string quotesPath = Path.Combine(directory, "TEST.2026-09-18.Quotes.qsh");
            DateTime start = new DateTime(2026, 9, 18, 10, 0, 0, DateTimeKind.Utc);

            WriteDeals(dealsPath, start, futurePrice, candidateDealSide);
            WriteQuotes(quotesPath, start, sameTimestampBidVolume, sameTimestampAskVolume);

            SyntheticPair pair = new SyntheticPair();
            pair.DealsPath = dealsPath;
            pair.QuotesPath = quotesPath;
            pair.StartTime = start;
            return pair;
        }

        public static SyntheticPair CreateSameTimestampPermutation(string root, string name, bool reverse)
        {
            string directory = Path.Combine(root, name);
            Directory.CreateDirectory(directory);
            string dealsPath = Path.Combine(directory, "TEST.2026-09-18.Deals.qsh");
            string quotesPath = Path.Combine(directory, "TEST.2026-09-18.Quotes.qsh");
            DateTime start = new DateTime(2026, 9, 18, 10, 0, 0, DateTimeKind.Utc);

            WriteSameTimestampDeals(dealsPath, start, reverse);
            WriteQuotes(quotesPath, start, 100, 100);

            SyntheticPair pair = new SyntheticPair();
            pair.DealsPath = dealsPath;
            pair.QuotesPath = quotesPath;
            pair.StartTime = start;
            return pair;
        }

        public static SyntheticPair CreateBarrierTie(string root, string name, bool reverse)
        {
            string directory = Path.Combine(root, name);
            Directory.CreateDirectory(directory);
            string dealsPath = Path.Combine(directory, "TEST.2026-09-18.Deals.qsh");
            string quotesPath = Path.Combine(directory, "TEST.2026-09-18.Quotes.qsh");
            DateTime start = new DateTime(2026, 9, 18, 10, 0, 0, DateTimeKind.Utc);

            WriteBarrierTieDeals(dealsPath, start, reverse);
            WriteQuotes(quotesPath, start, 100, 100);

            SyntheticPair pair = new SyntheticPair();
            pair.DealsPath = dealsPath;
            pair.QuotesPath = quotesPath;
            pair.StartTime = start;
            return pair;
        }

        public static SyntheticPair CreateDisjointRanges(string root, string name)
        {
            string directory = Path.Combine(root, name);
            Directory.CreateDirectory(directory);
            string dealsPath = Path.Combine(directory, "TEST.2026-09-18.Deals.qsh");
            string quotesPath = Path.Combine(directory, "TEST.2026-09-18.Quotes.qsh");
            DateTime start = new DateTime(2026, 9, 18, 10, 0, 0, DateTimeKind.Utc);

            WriteDeals(dealsPath, start, 101, Side.Sell);
            WriteQuotes(quotesPath, start.AddDays(1), 100, 100);

            SyntheticPair pair = new SyntheticPair();
            pair.DealsPath = dealsPath;
            pair.QuotesPath = quotesPath;
            pair.StartTime = start;
            return pair;
        }

        public static SyntheticPair CreateNegativeQuoteCount(string root, string name)
        {
            string directory = Path.Combine(root, name);
            Directory.CreateDirectory(directory);
            string dealsPath = Path.Combine(directory, "TEST.2026-09-18.Deals.qsh");
            string quotesPath = Path.Combine(directory, "TEST.2026-09-18.Quotes.qsh");
            DateTime start = new DateTime(2026, 9, 18, 10, 0, 0, DateTimeKind.Utc);

            WriteDeals(dealsPath, start, 101, Side.Sell);
            WriteQuotesWithNegativeCount(quotesPath, start);

            SyntheticPair pair = new SyntheticPair();
            pair.DealsPath = dealsPath;
            pair.QuotesPath = quotesPath;
            pair.StartTime = start;
            return pair;
        }

        public static SyntheticPair CompressGzip(string root, SyntheticPair source)
        {
            string directory = Path.Combine(root, "gzip");
            Directory.CreateDirectory(directory);
            string dealsPath = Path.Combine(directory, "TEST.2026-09-18.Deals.qsh");
            string quotesPath = Path.Combine(directory, "TEST.2026-09-18.Quotes.qsh");
            CompressFileGzip(source.DealsPath, dealsPath);
            CompressFileGzip(source.QuotesPath, quotesPath);

            SyntheticPair pair = new SyntheticPair();
            pair.DealsPath = dealsPath;
            pair.QuotesPath = quotesPath;
            pair.StartTime = source.StartTime;
            return pair;
        }

        public static SyntheticPair CompressDeflate(string root, SyntheticPair source)
        {
            string directory = Path.Combine(root, "deflate");
            Directory.CreateDirectory(directory);
            string dealsPath = Path.Combine(directory, "TEST.2026-09-18.Deals.qsh");
            string quotesPath = Path.Combine(directory, "TEST.2026-09-18.Quotes.qsh");
            CompressFileDeflate(source.DealsPath, dealsPath);
            CompressFileDeflate(source.QuotesPath, quotesPath);

            SyntheticPair pair = new SyntheticPair();
            pair.DealsPath = dealsPath;
            pair.QuotesPath = quotesPath;
            pair.StartTime = source.StartTime;
            return pair;
        }

        private static void WriteDeals(string path, DateTime start, decimal futurePrice, Side candidateDealSide)
        {
            using (FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (DataBinaryWriter writer = new DataBinaryWriter(stream))
            {
                WriteHeader(writer, start, 0x20);
                long lastFrame = TimeManager.GetTimeStampMillisecondsFromStartTime(start);
                DealsStream dealsStream = new DealsStream();

                WriteDealFrame(writer, dealsStream, start.AddSeconds(1), ref lastFrame,
                    100, 60, candidateDealSide, "1");
                WriteDealFrame(writer, dealsStream, start.AddSeconds(2), ref lastFrame,
                    100, 60, candidateDealSide, "2");
                WriteDealFrame(writer, dealsStream, start.AddSeconds(3), ref lastFrame,
                    futurePrice, 1, Side.Buy, "3");
                WriteDealFrame(writer, dealsStream, start.AddSeconds(4), ref lastFrame,
                    futurePrice, 1, Side.Buy, "4");
            }
        }

        private static void WriteDealFrame(DataBinaryWriter writer, DealsStream dealsStream, DateTime time,
            ref long lastFrame, decimal price, decimal volume, Side side, string id)
        {
            long timestamp = TimeManager.GetTimeStampMillisecondsFromStartTime(time);
            writer.WriteGrowing(timestamp - lastFrame);
            lastFrame = timestamp;

            Trade trade = new Trade();
            trade.Time = time;
            trade.Price = price;
            trade.Volume = volume;
            trade.Side = side;
            trade.Id = id;
            dealsStream.Write(writer, trade, 1, 1);
        }

        private static void WriteSameTimestampDeals(string path, DateTime start, bool reverse)
        {
            using (FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (DataBinaryWriter writer = new DataBinaryWriter(stream))
            {
                WriteHeader(writer, start, 0x20);
                long lastFrame = TimeManager.GetTimeStampMillisecondsFromStartTime(start);
                DealsStream dealsStream = new DealsStream();
                decimal firstPrice = reverse ? 101 : 99;
                decimal secondPrice = reverse ? 99 : 101;

                WriteDealFrame(writer, dealsStream, start.AddSeconds(1), ref lastFrame,
                    firstPrice, 60, Side.Sell, "1");
                WriteDealFrame(writer, dealsStream, start.AddSeconds(1), ref lastFrame,
                    secondPrice, 60, Side.Sell, "2");
                WriteDealFrame(writer, dealsStream, start.AddSeconds(2), ref lastFrame,
                    101, 1, Side.Buy, "3");
                WriteDealFrame(writer, dealsStream, start.AddSeconds(3), ref lastFrame,
                    101, 1, Side.Buy, "4");
            }
        }

        private static void WriteBarrierTieDeals(string path, DateTime start, bool reverse)
        {
            using (FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (DataBinaryWriter writer = new DataBinaryWriter(stream))
            {
                WriteHeader(writer, start, 0x20);
                long lastFrame = TimeManager.GetTimeStampMillisecondsFromStartTime(start);
                DealsStream dealsStream = new DealsStream();
                decimal firstBarrierPrice = reverse ? 99 : 101;
                decimal secondBarrierPrice = reverse ? 101 : 99;

                WriteDealFrame(writer, dealsStream, start.AddSeconds(1), ref lastFrame,
                    100, 60, Side.Sell, "1");
                WriteDealFrame(writer, dealsStream, start.AddSeconds(1), ref lastFrame,
                    100, 60, Side.Sell, "2");
                WriteDealFrame(writer, dealsStream, start.AddSeconds(2), ref lastFrame,
                    firstBarrierPrice, 1, Side.Buy, "3");
                WriteDealFrame(writer, dealsStream, start.AddSeconds(2), ref lastFrame,
                    secondBarrierPrice, 1, Side.Buy, "4");
            }
        }

        private static void WriteQuotes(string path, DateTime start, long bidVolume, long askVolume)
        {
            using (FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (DataBinaryWriter writer = new DataBinaryWriter(stream))
            {
                WriteHeader(writer, start, 0x10);
                long lastFrame = TimeManager.GetTimeStampMillisecondsFromStartTime(start);
                long lastPrice = 0;

                WriteQuoteFrame(writer, start, ref lastFrame, ref lastPrice,
                    new QuoteChange[]
                    {
                        new QuoteChange(99, -100),
                        new QuoteChange(101, 100)
                    });
                WriteQuoteFrame(writer, start.AddSeconds(2), ref lastFrame, ref lastPrice,
                    new QuoteChange[]
                    {
                        new QuoteChange(99, -bidVolume),
                        new QuoteChange(101, askVolume)
                    });
                WriteQuoteFrame(writer, start.AddSeconds(8), ref lastFrame, ref lastPrice,
                    new QuoteChange[0]);
            }
        }

        private static void WriteQuotesWithNegativeCount(string path, DateTime start)
        {
            using (FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (DataBinaryWriter writer = new DataBinaryWriter(stream))
            {
                WriteHeader(writer, start, 0x10);
                long lastFrame = TimeManager.GetTimeStampMillisecondsFromStartTime(start);
                long lastPrice = 0;
                WriteQuoteFrame(writer, start, ref lastFrame, ref lastPrice,
                    new QuoteChange[]
                    {
                        new QuoteChange(99, -100),
                        new QuoteChange(101, 100)
                    });

                long timestamp = TimeManager.GetTimeStampMillisecondsFromStartTime(start.AddSeconds(2));
                writer.WriteGrowing(timestamp - lastFrame);
                writer.WriteLeb128(-1);
            }
        }

        private static void WriteQuoteFrame(DataBinaryWriter writer, DateTime time, ref long lastFrame,
            ref long lastPrice, QuoteChange[] changes)
        {
            long timestamp = TimeManager.GetTimeStampMillisecondsFromStartTime(time);
            writer.WriteGrowing(timestamp - lastFrame);
            lastFrame = timestamp;
            writer.WriteLeb128(changes.Length);

            for (int i = 0; i < changes.Length; i++)
            {
                writer.WriteLeb128(changes[i].PriceTicks - lastPrice);
                lastPrice = changes[i].PriceTicks;
                writer.WriteLeb128(changes[i].SignedVolumeSteps);
            }
        }

        private static void WriteHeader(DataBinaryWriter writer, DateTime start, byte streamType)
        {
            writer.Write(_prefix);
            writer.Write((byte)4);
            writer.Write("OrderFlowSyntheticFixture");
            writer.Write("VolumeStep:1");
            writer.Write(start.Ticks);
            writer.Write((byte)1);
            writer.Write(streamType);
            writer.Write("Synthetic:TEST:Futures:1:1");
        }

        private static void CompressFileGzip(string sourcePath, string targetPath)
        {
            using (FileStream source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (FileStream target = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (GZipStream gzip = new GZipStream(target, CompressionLevel.Optimal))
            {
                source.CopyTo(gzip);
            }
        }

        private static void CompressFileDeflate(string sourcePath, string targetPath)
        {
            using (FileStream source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (FileStream target = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (DeflateStream deflate = new DeflateStream(target, CompressionLevel.Optimal))
            {
                source.CopyTo(deflate);
            }
        }
    }

    internal readonly struct QuoteChange
    {
        public QuoteChange(long priceTicks, long signedVolumeSteps)
        {
            PriceTicks = priceTicks;
            SignedVolumeSteps = signedVolumeSteps;
        }

        public long PriceTicks { get; }

        public long SignedVolumeSteps { get; }
    }
}
