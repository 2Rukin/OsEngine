/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.Entity;
using OsEngine.Logging;
using OsEngine.Market;
using OsEngine.OsData.OrderFlow;
using OsEngine.OsData.OrderFlow.Calibration;
using OsEngine.OsData.OrderFlow.Explorer;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;

namespace OsEngine.OrderFlowResearch.Tests
{
    internal static partial class Program
    {
        private static void TestCalibrationAnatomy(string root)
        {
            CalibrationRun run = CalRun(CalSpec(root, Row(Start, 100, 5, Side.Sell), Row(Start, 100, 1, Side.Buy),
                Row(Start.AddMilliseconds(50), 101, 20, Side.Buy), Row(Start.AddMilliseconds(50), 101, 10, Side.Buy),
                Row(Start.AddSeconds(2), 110, 1, Side.Sell)) with { MinimumTickVolume = 5 });
            FormationSpec formation = run.Manifest.Cells[1].Formation; CalibrationEvent item = CalEvents(run, formation).Single();
            AnatomyTick[] ticks = CalibrationAnatomy.Ticks(run, formation, item, false, CancellationToken.None).ToArray();
            AnatomyLevel[] levels = CalibrationAnatomy.Levels(run, formation, item, CancellationToken.None).ToArray();
            AssertEqual(item.Volume, ticks.Sum(t => t.Volume), "Tick sum"); AssertEqual(item.Evidence.Count, ticks.Length, "Exact physical count");
            AssertEqual(item.Evidence.Buy, ticks.Where(t => t.Side == "Buy").Sum(t => t.Volume), "Buy sum");
            AssertEqual(item.Delta, levels.Sum(l => l.Delta), "Levels delta"); AssertEqual(100m, levels.Sum(l => l.SharePercent), "Level shares");
            AssertEqual(0m, ticks[2].GapMilliseconds, "Equal-time identity retained");
            AssertEqual(item.Evidence.Context.Volume, CalibrationAnatomy.Ticks(run, formation, item, true, CancellationToken.None).Sum(t => t.Volume), "Context includes small tick, excludes later tick");
            DiagonalPairRow pair = CalibrationAnatomy.Pairs(run, item, new DiagonalSettings(), CancellationToken.None).Single();
            AssertEqual(25m, pair.PairDiagonalDelta, "Pair conservation"); AssertEqual(1, pair.StackOrdinal, "Anatomy stack ordinal");
            CalibrationTableSource levelTable = CalibrationTableSource.Create("levels", t => levels);
            CalibrationTableSource pairTable = CalibrationTableSource.Create("pairs", t => new[] { pair });
            TableQuery filter = new TableQuery(TimeRange: run.Spec.Range.Id, Direction: "Buy");
            AssertEqual(1L, levelTable.Page(filter, CancellationToken.None).Visible, "Anatomy level range and net direction filter");
            AssertEqual(1L, pairTable.Page(filter, CancellationToken.None).Visible, "Anatomy pair range and direction filter");
        }

        private sealed record QueryFixture(int Id, decimal Value, string TimeRangeId, string Direction);
        private static void TestCalibrationTablePaging(string root)
        {
            QueryFixture[] source = Enumerable.Range(0, 1703).Select(i => new QueryFixture(i, i % 13, i % 2 == 0 ? "Morning" : "Main", i % 3 == 0 ? "Buy" : "Sell")).ToArray();
            CalibrationTableSource table = CalibrationTableSource.Create("test", token => source);
            foreach (int direction in new[] { 0, 1, -1 })
            {
                TableQuery query = new TableQuery(SortColumn: "Value", SortDirection: direction); List<int> seen = new List<int>();
                while (true)
                {
                    TablePage page = table.Page(query, CancellationToken.None, 37);
                    AssertTrue(page.Rows.Count <= 37, "Bounded page"); AssertEqual(1703L, page.Total, "Whole source count");
                    seen.AddRange(page.Rows.Select(r => ((QueryFixture)r.Payload).Id));
                    if (page.Next == null) { break; } query = query with { After = page.Next };
                }
                IEnumerable<QueryFixture> expected = direction == 0 ? source : direction == 1 ? source.OrderBy(r => r.Value).ThenBy(r => r.Id) : source.OrderByDescending(r => r.Value).ThenBy(r => r.Id);
                AssertTrue(seen.SequenceEqual(expected.Select(r => r.Id)), "Global stable sort across all pages");
            }
            TableQuery filtered = new TableQuery(TimeRange: "Morning", Direction: "Buy", NumericColumn: "Value", Minimum: 5, Maximum: 7);
            AssertEqual((long)source.Count(r => r.TimeRangeId == "Morning" && r.Direction == "Buy" && r.Value >= 5 && r.Value <= 7), table.Page(filtered, CancellationToken.None).Visible, "Combined view predicates");
        }
        private static void TestCalibrationReplay(string root)
        {
            string[] rows = { Row(Start, 100, 5, Side.Sell), Row(Start, 101, 10, Side.Buy), Row(Start, 104, 20, Side.Buy) };
            CalibrationRun run = CalRun(CalSpec(root, rows));
            CloudRule rule = new CloudRule { Provenance = run.Spec, BundlePath = run.Directory, Formation = run.Manifest.Cells[1].Formation };
            using CalibrationReplayCursor cursor = new CalibrationReplayCursor(new[] { rule }, CancellationToken.None);
            AssertEqual(0, cursor.Advance(2, false)[0].Markers.Length, "No historical final before breaking tick");
            ImmutableArray<CalibrationMarker> atBreak = cursor.Advance(3, false)[0].Markers;
            AssertEqual(1, atBreak.Length, "Same timestamp completion sequence"); AssertEqual(2L, atBreak[0].Event.Evidence.LastSequence, "Correct closed prefix");
            AssertEqual(2, cursor.Advance(3, true)[0].Markers.Length, "EOF only after Complete");
            CalibrationChartData data = CalibrationPresentation.Load(run, new[] { rule }, OrderFlowDisplayTimeFrame.Min1, CancellationToken.None);
            OrderFlowResearchChart chart = new OrderFlowResearchChart(); chart.SetResult(data.Prices); chart.SetCalibrationLayers(data.Layers);
            chart.BeginReplay(1); chart.SetCalibrationReplay(3, false); chart.SetCalibrationPlaybackLayers(cursor.Advance(3, true));
            AssertTrue(!data.Layers[0].Markers.Last().Known(3, false), "Historical EOF gate remains independent of mailbox");
            RecordingReplay observer = new RecordingReplay();
            new OrderFlowResearchEngine().Run(Request(root, rows), CancellationToken.None, observer);
            OrderFlowReplayFrame prefix = observer.Frames.Last();
            AssertTrue(prefix.Result.InputHash == null && prefix.Result.Input == null, "Real legacy prefix omits provenance");
            chart.ApplyReplayFrame(prefix.Result);
            MethodInfo visible = typeof(OrderFlowResearchChart).GetMethod("CalibrationVisible", BindingFlags.Instance | BindingFlags.NonPublic);
            AssertTrue((bool)visible.Invoke(chart, new object[] { atBreak[0] }), "Known marker visible on actual captured prefix");
            AssertFalse((bool)visible.Invoke(chart, new object[] { data.Layers[0].Markers.Last() }), "EOF hidden on actual nonterminal prefix");
            CalibrationMarker foreign = atBreak[0] with { Rule = rule with { Provenance = rule.Provenance with { InputSha256 = "foreign" } } };
            AssertFalse((bool)visible.Invoke(chart, new object[] { foreign }), "Foreign source still rejected during replay");
            chart.SetCalibrationReplay(3, true);
            AssertTrue((bool)visible.Invoke(chart, new object[] { data.Layers[0].Markers.Last() }), "EOF visible only at explicit completion");
            chart.EndReplay();
            CalibrationChartData transferred = CalibrationPresentation.Load(run, new[] { rule }, OrderFlowDisplayTimeFrame.Min5, CancellationToken.None);
            AssertEqual(OrderFlowDisplayTimeFrame.Min5, transferred.TimeFrame, "Transfer retains explicitly selected timeframe");
            chart.SetResult(transferred.Prices); chart.SetCalibrationLayers(transferred.Layers);
            foreach (OrderFlowDisplayTimeFrame frame in OrderFlowChartTimeFrames.GetMenuValues())
            {
                chart.SetTimeFrame(frame); AssertTrue(chart.TotalBars > 0 && chart.TotalBars <= 4000, "Every main selector timeframe has bounded bars");
                AssertEqual(35m, transferred.Prices.Bars[frame].Sum(b => b.Volume), "Per-timeframe stored-source volume conservation");
            }
        }
        private static void TestCalibrationWindows(string root)
        {
            using CalibrationWindowSet windows = new CalibrationWindowSet(); int made = 0, shown = 0;
            Window Factory() { made++; return new CalibrationTableWindow(CalibrationTableSource.Create("empty", t => Array.Empty<QueryFixture>()), "test"); }
            Window first = windows.Open("same", Factory, w => shown++), again = windows.Open("same", Factory, w => shown++);
            AssertTrue(ReferenceEquals(first, again) && made == 1 && shown == 1, "One window per exact context");
            AssertTrue(first.Owner == null && !first.Topmost, "Independent work window"); first.Close();
            Window reopened = windows.Open("same", Factory, w => shown++); AssertTrue(!ReferenceEquals(first, reopened) && made == 2, "Close releases registry");
            windows.CloseAll();
        }
        private static void TestCalibrationMarkup(string root)
        {
            XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            foreach (string resource in new[] { "Research.Calibration.xaml", "Research.Calibration.Table.xaml", "Research.Calibration.Anatomy.xaml" })
            {
                using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource); XDocument doc = XDocument.Load(stream);
                AssertTrue(doc.Root.Attribute("Style").Value.Contains("DynamicResource WindowStyleCanResize"), "Resizable theme style");
                foreach (XAttribute color in doc.Descendants().Attributes().Where(a => new[] { "Background", "Foreground", "BorderBrush", "Fill", "Stroke" }.Contains(a.Name.LocalName)))
                { AssertTrue(color.Value.StartsWith("{DynamicResource", StringComparison.Ordinal), "No hardcoded UI color"); }
                foreach (XElement button in doc.Descendants(wpf + "Button")) { AssertEqual("Auto", button.Attribute("Height")?.Value, "No fixed action height"); }
                if (resource != "Research.Calibration.Table.xaml") { AssertEqual(0, doc.Descendants(wpf + "DataGrid").Count(), "No embedded analytical tables"); }
                else
                {
                    XElement grid = doc.Descendants(wpf + "DataGrid").Single(); AssertEqual("True", grid.Attribute("EnableRowVirtualization")?.Value, "Row virtualization");
                    AssertEqual("True", grid.Attribute("EnableColumnVirtualization")?.Value, "Column virtualization");
                }
            }
        }
        private static T CalField<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(target);
        private static void CalCall(object target, string method, params object[] args) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
        private static void CalDrain(CloudCalibrationWindow window)
        {
            Stopwatch timeout = Stopwatch.StartNew();
            while (CalField<ExplorerJob>(window, "_job") != null && timeout.Elapsed < TimeSpan.FromSeconds(20))
            { CalCall(window, "Poll", null, EventArgs.Empty); Thread.Sleep(1); }
            AssertTrue(CalField<ExplorerJob>(window, "_job") == null, "Calibration worker reached terminal result");
        }
        private static void TestCalibrationPreliminary(string root)
        {
            CalibrationSpec spec = CalSpec(root, Row(Start, 100, 5, Side.Sell), Row(Start.AddMilliseconds(50), 101, 20, Side.Buy));
            CloudCalibrationWindow window = new CloudCalibrationWindow(() => new ExplorerRunSpec { InputPath = spec.InputPath, OutputRootPath = spec.OutputRootPath, PriceStep = 1 }, (data, marker) => { });
            try
            {
                CalField<ComboBox>(window, "ComboBoxProfile").SelectedIndex = 0;
                CalField<TextBox>(window, "TextBoxGaps").Text = "invalid-grid";
                CalField<TextBox>(window, "TextBoxCellBudget").Text = "invalid-grid-budget";
                CalCall(window, "AnalyzeTicksClick", null, new RoutedEventArgs()); CalDrain(window);
                CalibrationRun prepared = CalField<CalibrationRun>(window, "_run");
                AssertTrue(prepared != null && prepared.Spec.TickDistributionOnly && prepared.Manifest.Cells.IsEmpty, "Initial histogram needs no formation or valid grid");
                AssertEqual(8, CalField<WrapPanel>(window, "WrapPanelQuantiles").Children.Count, "Quantile buttons rendered before first formation");
                AssertEqual(0, Directory.GetFiles(prepared.Directory, "events-*").Length, "No hidden Single/Chain work during preparation");
                AssertTrue(CalibrationStorage.Open(prepared.Directory, CancellationToken.None).Spec.TickDistributionOnly, "Raw-only bundle round trip");
                int expectedErrors = 0;
                Action<string, LogMessageType> captureError = (message, type) => { if (type == LogMessageType.Error && message.Contains("invalid-grid", StringComparison.Ordinal)) { expectedErrors++; } };
                ServerMaster.LogMessageEvent += captureError;
                try { CalCall(window, "RunClick", null, new RoutedEventArgs()); }
                finally { ServerMaster.LogMessageEvent -= captureError; }
                AssertEqual(1, expectedErrors, "Invalid grid is reported through logging without an interactive dialog");
                AssertTrue(ReferenceEquals(prepared, CalField<CalibrationRun>(window, "_run")), "Invalid grid cannot discard preliminary statistics");
                CalField<TextBox>(window, "TextBoxGaps").Text = "100"; CalField<TextBox>(window, "TextBoxRanges").Text = "1";
                CalField<TextBox>(window, "TextBoxCellBudget").Text = "1"; CalField<TextBox>(window, "TextBoxTickVolume").Text = "10"; CalDrain(window);
                File.Delete(spec.InputPath);
                CalCall(window, "RunClick", null, new RoutedEventArgs()); CalDrain(window);
                CalibrationRun formed = CalField<CalibrationRun>(window, "_run");
                AssertTrue(!formed.Spec.TickDistributionOnly && formed.Manifest.Cells.Length == 2, "Explicit grid uses prepared snapshot without raw file");
                AssertEqual(prepared.Spec.InputSha256, formed.Spec.InputSha256, "Prepared input SHA retained");
                AssertEqual(1L, formed.Manifest.Cells[0].Summary.Total, "User-selected threshold applied after histogram");
            }
            finally { window.Close(); }

            CalibrationSpec manyDates = CalSpec(root, Enumerable.Range(0, 17).Select(day => Row(Start.AddDays(day), 100, 1, Side.Buy)).ToArray()) with
            { TickDistributionOnly = true, Gaps = ImmutableArray<int>.Empty, Ranges = ImmutableArray<int>.Empty, MaximumBufferItems = 32 };
            CalibrationRun dateCache = CalRun(manyDates);
            CalibrationSpec smallerBudget = manyDates with { TickDistributionOnly = false, Gaps = ImmutableArray.Create(100),
                Ranges = ImmutableArray.Create(1), MaximumBufferItems = 16, MinimumTickVolume = 2 };
            int beforeFailure = Directory.GetDirectories(smallerBudget.OutputRootPath).Length;
            Expect<InvalidDataException>(() => CalRun(smallerBudget));
            Expect<InvalidDataException>(() => CalibrationEngine.Run(smallerBudget, CancellationToken.None, null, dateCache));
            AssertEqual(beforeFailure, Directory.GetDirectories(smallerBudget.OutputRootPath).Length, "Direct/reused lowered date budget both reject without publishing");
            AssertEqual(17, CalibrationStorage.Open(dateCache.Directory, CancellationToken.None).Manifest.Quality.SourceDates, "Rejected grid preserves prepared snapshot");
        }
        private static void TestCalibrationEditorIsolation(string root)
        {
            CalibrationSpec spec = CalSpec(root, Row(Start, 100, 5, Side.Sell), Row(Start.AddMilliseconds(50), 101, 20, Side.Buy));
            CalibrationRun first = CalRun(spec), second = CalRun(spec with { Range = spec.Range with { Id = "second", Name = "Second" } });
            CloudRule original = new CloudRule { Provenance = first.Spec, BundlePath = first.Directory, Formation = first.Manifest.Cells[0].Formation };
            CalibrationStorage.SaveRule(spec.OutputRootPath, original, CancellationToken.None);
            CloudCalibrationWindow window = new CloudCalibrationWindow(() => new ExplorerRunSpec { InputPath = spec.InputPath, OutputRootPath = spec.OutputRootPath, PriceStep = 1 }, (data, marker) => { });
            try
            {
                CalCall(window, "SelectRule", original); CalDrain(window);
                AssertEqual(original.RuleId, CalField<CloudRule>(window, "_editingRule").RuleId, "Editing rule A established");
                CalCall(window, "AcceptRun", second, false);
                CalField<ComboBox>(window, "ComboBoxFormation").SelectedItem = FormationMode.Single;
                CalCall(window, "Debounced", null, EventArgs.Empty); CalDrain(window);
                AssertTrue(CalField<CloudRule>(window, "_editingRule") == null, "New study clears previous editor identity");
                CalCall(window, "SaveRuleClick", null, new RoutedEventArgs()); CalDrain(window);
                ImmutableArray<CloudRule> rules = CalibrationStorage.LoadRules(spec.OutputRootPath, CancellationToken.None);
                AssertEqual(2, rules.Length, "Independent Single B persisted");
                CloudRule retained = rules.Single(r => r.RuleId == original.RuleId);
                AssertTrue(retained.Enabled && retained.Visible, "Saving B does not disable previously edited range A");
            }
            finally { window.Close(); }
        }
        private static void TestCalibrationTuner(string root)
        {
            CalibrationSpec spec = CalSpec(root, Row(Start, 100, 5, Side.Sell), Row(Start.AddMilliseconds(50), 101, 20, Side.Buy)); CalibrationRun run = CalRun(spec);
            CloudCalibrationWindow window = new CloudCalibrationWindow(() => new ExplorerRunSpec { InputPath = spec.InputPath, OutputRootPath = spec.OutputRootPath, PriceStep = 1 }, (data, marker) => { });
            try
            {
                CalCall(window, "AcceptRun", run, false);
                AssertTrue(CalField<ParameterCell>(window, "_cell") == null, "No automatic winner");
                ParameterCell chosen = run.Manifest.Cells[1]; CalCall(window, "CellSelected", chosen); CalCall(window, "Debounced", null, EventArgs.Empty);
                Stopwatch timeout = Stopwatch.StartNew();
                while (CalField<ExplorerJob>(window, "_job") != null && timeout.Elapsed < TimeSpan.FromSeconds(10)) { CalCall(window, "Poll", null, EventArgs.Empty); Thread.Sleep(1); }
                AssertEqual(chosen.FormationHash, CalField<CloudRule>(window, "_preview").FormationHash, "Explicit selection bound to preview");
                AssertTrue(CalField<Button>(window, "ButtonSaveRule").IsEnabled, "Save only after preview evidence");
                File.Delete(spec.InputPath);
                CalCall(window, "Debounced", null, EventArgs.Empty);
                while (CalField<ExplorerJob>(window, "_job") != null && timeout.Elapsed < TimeSpan.FromSeconds(10)) { CalCall(window, "Poll", null, EventArgs.Empty); Thread.Sleep(1); }
                AssertTrue(CalField<Button>(window, "ButtonSaveRule").IsEnabled, "UI filter works without raw file");
            }
            finally { window.Close(); }
        }
        private static void TestCalibrationMillion(string root)
        {
            string path = Path.Combine(root, "million.txt");
            using (StreamWriter writer = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                for (int i = 0; i < 1000000; i++) { writer.WriteLine(Row(Start.AddTicks(i * 20000L), 100, i % 1000 == 0 ? 10 : 1, Side.Buy)); }
            }
            CalibrationSpec spec = new CalibrationSpec { InputPath = path, OutputRootPath = Path.Combine(root, "million-output"),
                Range = new TimeRangeProfile { Id = "million", Name = "Million", DayMask = 127 }, MinimumTickVolume = 10,
                Gaps = ImmutableArray.Create(1000), Ranges = ImmutableArray.Create(1), MaximumCacheBytes = 256L * 1024 * 1024 };
            CalibrationRun run = CalRun(spec);
            AssertEqual(1000000L, run.Manifest.Quality.Accepted, "Million validated physical rows");
            AssertEqual(49000000L, new FileInfo(Path.Combine(run.Directory, "ticks.cache")).Length, "Fixed-size disk cache rather than object graph");
            AssertEqual(1000L, run.Manifest.Cells[0].Summary.Total, "Physical qualifying singles");
            AssertEqual(1000L, run.Manifest.Cells[1].Summary.Total, "Breaking included ticks retained across million-row raw context");
        }
    }
}
