/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.Entity;
using OsEngine.OsData.OrderFlow;
using OsEngine.OsData.OrderFlow.Explorer;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OsEngine.OrderFlowResearch.Tests
{
    internal static partial class Program
    {
        private static void RegisterPatternSearch(string root)
        {
            Run("FollowupStrictOptionParsing", root, TestFollowupParsing);
            Run("FollowupAddressedRanges", root, TestFollowupRanges);
            Run("FollowupReaderAndIoErrors", root, TestFollowupErrors);
            Run("FollowupEmptyRangeNeverPublished", root, TestFollowupEmpty);
            Run("FollowupChartIntervalAndIndependentY", root, TestFollowupChart);
            Run("FollowupUiWorkflowAndEpisodeState", root, TestFollowupUi);
            Run("PatternDayWeekCausalSnapshots", root, TestPatternWeek);
            Run("PatternIncompleteWeekAndMissingDates", root, TestPatternIncompleteWeek);
            Run("PatternOrdinalLabelsAndFirstTouch", root, TestPatternLabels);
            Run("PatternFixedHorizonsAndEndOfDay", root, TestPatternDayLabels);
            Run("PatternUnknownNotNegativeAndOverlap", root, TestPatternUnknown);
            Run("PatternWholeWeekSplitAndPurge", root, TestPatternSplit);
            Run("PatternGrammarOrderAndBudget", root, TestPatternGrammar);
            Run("PatternMatchedControlAndFitIsolation", root, TestPatternFit);
            Run("PatternDistinctSupportAndStatus", root, TestPatternSupport);
            Run("PatternEveryPrefixIndependentReplay", root, TestPatternReplay);
            Run("PatternAtomicBundleReuseAndReopen", root, TestPatternArtifacts);
            Run("PatternCancellationAndCorruption", root, TestPatternCancel);
            Run("PatternFixedHorizonExactEof", root, TestPatternExactEof);
            Run("PatternCausalSettingsIdentity", root, TestPatternSettingsIdentity);
            Run("PatternMatchedExclusionsAudit", root, TestPatternMatchAudit);
            Run("PatternRelativeBackgroundPurged", root, TestPatternRelativeHistory);
            Run("FollowupDistantSavedMarkersSameInterval", root, TestFollowupSavedInterval);
            Run("FollowupBusyPagingAndModuleFocus", root, TestFollowupBusyFocus);
            Run("PatternReplayCannotReopenFutureCard", root, TestPatternReplayUi);
            Run("FollowupObservationVwapSurvivesRepaint", root, TestFollowupObservationVwap);
            Run("PatternLatestExampleSelectionWins", root, TestPatternLatestExample);
        }
        private static void InputError(Action action, string field)
        {
            try { action(); } catch (ExplorerInputException error)
            { AssertEqual(field, error.Field, "Stable addressed field"); AssertTrue(ExplorerValidation.UserMessage(error, "test").Any(c => c >= 'А' && c <= 'я'), "Russian corrective message"); return; }
            throw new InvalidOperationException("Expected addressed rejection for " + field);
        }
        private static void TestFollowupParsing(string root)
        {
            foreach (string text in new[] { "", "abc", "1e3", "1.2.3", "999999999999999999999999999999999999999", "1,234.5" })
            { InputError(() => ExplorerValidation.Number(text, "PriceStep"), "PriceStep"); }
            AssertEqual(.01m, ExplorerValidation.Number("0,01", "PriceStep"), "Comma decimal");
            AssertEqual(.01m, ExplorerValidation.Number("0.01", "PriceStep"), "Dot decimal");
            foreach (string text in new[] { "1.5", "2147483648", "-2147483649" }) { InputError(() => ExplorerValidation.Integer(text, "TickWindow"), "TickWindow"); }
            foreach (object defaults in new object[] { new ExplorerProfile(), new ExplorerEpisodeSpec(), new ExplorerStudySpec(), new ExplorerPatternSpec() })
            {
                foreach (PropertyInfo property in defaults.GetType().GetProperties().Where(p => p.SetMethod != null && (p.PropertyType == typeof(bool) || p.PropertyType == typeof(int) || p.PropertyType == typeof(decimal))))
                {
                    ExplorerOption option = new ExplorerOption { Property = property, Name = property.Name, Value = "wrong" };
                    InputError(() => ExplorerOptions.Read(defaults, new[] { option }), property.Name);
                }
            }
            List<ExplorerOption> horizon = ExplorerOptions.Create(new ExplorerStudySpec()).Where(o => o.Property.Name == "HorizonsMinutes").ToList();
            foreach (string text in new[] { "1;", "1.5;2", "1;bad" }) { horizon[0].Value = text; InputError(() => ExplorerOptions.Read(new ExplorerStudySpec(), horizon), "HorizonsMinutes"); }
            List<ExplorerOption> enums = ExplorerOptions.Create(new ExplorerView(), "Layers", "RelatedCloudIds").Where(o => o.Property.PropertyType.IsEnum).ToList();
            foreach (ExplorerOption option in enums) { option.Value = "999"; InputError(() => ExplorerOptions.Read(new ExplorerView(), new[] { option }), option.Property.Name); }
        }
        private static void TestFollowupRanges(string root)
        {
            ExplorerProfile defaults = new ExplorerProfile();
            Dictionary<string, decimal> invalid = new Dictionary<string, decimal>
            { ["MinimumTickVolume"] = 0, ["MaximumGapMilliseconds"] = -1, ["MaximumRangeTicks"] = -1, ["ContextSeconds"] = 0,
                ["TickWindow"] = 100001, ["TickMinimum"] = 0, ["TickPercentile"] = 2, ["PaceSeconds"] = 0, ["PaceMinimum"] = 0, ["PaceFactor"] = 0,
                ["GapMinimum"] = -1, ["GapMaximum"] = -1, ["AtrFactor"] = 0, ["RangeMinimum"] = 0, ["RangeMaximum"] = 0,
                ["VolumeWindow"] = 100001, ["VolumeMinimum"] = 0, ["VolumeDates"] = 0, ["VolumePercentile"] = 2, ["TimeOfDayDates"] = 101, ["TimeOfDayMinutes"] = 721 };
            foreach (KeyValuePair<string, decimal> pair in invalid)
            {
                List<ExplorerOption> options = ExplorerOptions.Create(defaults, "Layer", "Scale"); options.Single(o => o.Property.Name == pair.Key).Value = pair.Value.ToString(CultureInfo.InvariantCulture);
                InputError(() => ExplorerOptions.Read(defaults, options).Validate(), pair.Key);
            }
            ExplorerRunSpec spec = V2Spec(root, Row(Start, 100, 1, Side.Buy)); spec.Validate();
            InputError(() => (spec with { PriceStep = 0 }).Validate(), "PriceStep");
            InputError(() => (spec with { FromDate = Start }).Validate(), "FromDate");
            InputError(() => (spec with { FromDate = Start, ToDate = Start.AddDays(-1) }).Validate(), "FromDate");
            InputError(() => (spec with { Profiles = ImmutableArray<ExplorerProfile>.Empty }).Validate(), "Profiles");
            InputError(() => (spec with { MaximumBufferItems = 1 }).Validate(), "MaximumBufferItems");
            InputError(() => (spec with { MaximumMemoryMegabytes = 1 }).Validate(), "MaximumMemoryMegabytes");
            InputError(() => (spec with { Episodes = new ExplorerEpisodeSpec { AtrFactor = 0 } }).Validate(), "Episodes.AtrFactor");
            InputError(() => (spec with { Study = new ExplorerStudySpec { HorizonsMinutes = ImmutableArray.Create(1, 1) } }).Validate(), "Study.HorizonsMinutes");
            InputError(() => ExplorerValidation.View(new ExplorerView { MinimumVolume = 100, MaximumVolume = 1 }), "MinimumVolume");
            InputError(() => ExplorerValidation.View(new ExplorerView { StartHour = 24 }), "StartHour");
        }
        private static void TestFollowupErrors(string root)
        {
            ExplorerRunSpec spec = V2Spec(root, "wrong");
            try { V2Run(spec); throw new InvalidOperationException("Expected reader error"); }
            catch (InvalidDataException error) { string text = ExplorerValidation.UserMessage(error, "test"); AssertTrue(text.Contains("Строка 1") && text.Contains("семь"), "Line plus Russian reason"); }
            InputError(() => ExplorerValidation.Preflight(spec with { InputPath = Path.Combine(root, "missing") }), "InputPath");
            InputError(() => ExplorerValidation.Preflight(spec with { OutputRootPath = spec.InputPath }), "OutputRootPath");
            AssertTrue(ExplorerValidation.UserMessage(new InvalidDataException("checksum mismatch"), "t").Contains("повреждён"), "Corrupt artifact reason");
            AssertTrue(ExplorerValidation.UserMessage(new InvalidDataException("SHA mismatch"), "t").Contains("изменён"), "Source identity reason");
            AssertTrue(ExplorerValidation.UserMessage(new Exception("internal"), "abc").Contains("abc"), "Unexpected attempt ID");
            AssertTrue(ExplorerValidation.NeedsDiagnosticFile(new ExplorerInputException("InputPath", "Нет доступа", inner: new IOException("technical reason"))), "Wrapped I/O cause requires independent diagnostic file");
            AssertTrue(!ExplorerValidation.NeedsDiagnosticFile(new ExplorerInputException("PriceStep", "Введите число")), "Pure field validation stays non-modal");
            AssertTrue(ExplorerValidation.NeedsDiagnosticFile(new InvalidOperationException("unexpected")), "Unexpected technical cause persists");
        }
        private static void TestFollowupEmpty(string root)
        {
            ExplorerRunSpec spec = V2Spec(root, Row(Start, 100, 1, Side.Buy)) with { FromDate = Start.AddDays(1), ToDate = Start.AddDays(2) };
            InputError(() => V2Run(spec), "FromDate"); AssertEqual(0, Directory.GetDirectories(spec.OutputRootPath).Length, "No published empty/staging output");
        }
        private static void TestFollowupChart(string root)
        {
            ExplorerChart chart = new ExplorerChart(); ExplorerCloud cloud = BaselineCloud(1, 400, Start) with { Profile = "Cloud1/Base", Low = 100, High = 101, Price = 100 };
            chart.Set(new[] { cloud }, new ExplorerView { MinimumVolume = 0 }, 1, null,
                new[] { new ExplorerPivot("High", 999999, Start.AddDays(-30), 1, Start.AddDays(-29), 2, 1, "Known") });
            chart.SetInterval(Start, Start.AddMinutes(1)); chart.SetContext(new[] { new ExplorerBar(Start, Start.AddSeconds(15), 100, 102, 99, 101, 10) }, Array.Empty<ExplorerEpisode>());
            chart.Measure(new Size(1000, 500)); chart.Arrange(new Rect(0, 0, 1000, 500));
            RenderTargetBitmap bitmap = new RenderTargetBitmap(1000, 500, 96, 96, PixelFormats.Pbgra32); bitmap.Render(chart);
            (DateTime from, DateTime to, decimal low, decimal high) = chart.Viewport;
            AssertEqual(Start, from, "Distant pivot does not move X"); AssertTrue(high < 200, "Distant pivot does not move Y");
            chart.PriceAxis(2, .2m); chart.UpdateLayout(); bitmap.Render(chart);
            AssertEqual(to, chart.Viewport.To, "Price axis leaves time unchanged"); AssertTrue(chart.Viewport.High - chart.Viewport.Low < high - low, "Independent Y zoom");
            chart.FitPrice(); chart.UpdateLayout(); bitmap.Render(chart); AssertEqual(low, chart.Viewport.Low, "Fit restores visible prices");
        }
        private static void TestFollowupUi(string root)
        {
            using CloudExplorerControl ui = new CloudExplorerControl();
            AssertEqual(VerticalAlignment.Top, ui.TextBoxSummary.VerticalContentAlignment, "Summary starts at top");
            AssertTrue(ui.TextBlockEpisodeState.Text.Contains("выключен"), "Disabled module explained");
            AssertEqual(0, ui.ComboBoxPatternDirection.SelectedIndex, "Both directions default");
            AssertEqual(0, ui.ComboBoxPatternContext.SelectedIndex, "Both contexts default");
            AssertTrue(ui.ButtonPatternOpen.ToolTip.ToString().Contains("без"), "Saved preview documented");
            ui.Measure(new Size(1000, 700)); ui.Arrange(new Rect(0, 0, 1000, 700));
            RenderTargetBitmap bitmap = new RenderTargetBitmap(1000, 700, 120, 120, PixelFormats.Pbgra32); bitmap.Render(ui);
        }
        private static void TestPatternWeek(string root)
        {
            DateTime monday = new DateTime(2026, 9, 7, 10, 0, 0); List<ExplorerPatternSnapshot> snapshots = new List<ExplorerPatternSnapshot>();
            ExplorerPatternKernel kernel = new ExplorerPatternKernel(V2Spec(root, Row(Start, 1, 1, Side.Buy)), new ExplorerPatternSpec(), snapshots.Add, _ => { });
            kernel.Tick(V2Tick(1, 100, 10, monday), CancellationToken.None); kernel.Tick(V2Tick(2, 101, 20, monday.AddDays(1)), CancellationToken.None);
            ExplorerPatternSnapshot tuesday = snapshots.Last(); string frozen = JsonSerializer.Serialize(tuesday);
            AssertEqual(2, tuesday.WeekDays.Length, "Only Monday and Tuesday prefix"); AssertEqual(30m, tuesday.WeekDays.Sum(d => d.Buy), "Known week volume");
            kernel.Tick(V2Tick(3, 500, 300, monday.AddDays(4)), CancellationToken.None); AssertEqual(frozen, JsonSerializer.Serialize(tuesday), "No future mutation");
            kernel.Tick(V2Tick(4, 100, 4, monday.AddDays(7)), CancellationToken.None);
            AssertEqual(1, snapshots.Last().WeekDays.Length, "Monday reset"); AssertEqual("WeekEmpty", snapshots.Last().WeekStatus, "Explicit empty prior week");
        }
        private static void TestPatternIncompleteWeek(string root)
        {
            List<ExplorerPatternSnapshot> snapshots = new List<ExplorerPatternSnapshot>(); ExplorerRunSpec spec = V2Spec(root, Row(Start, 100, 1, Side.Buy));
            ExplorerPatternKernel kernel = new ExplorerPatternKernel(spec, new ExplorerPatternSpec(), snapshots.Add, _ => { });
            DateTime wednesday = new DateTime(2026, 9, 9, 10, 0, 0); kernel.Tick(V2Tick(1, 100, 1, wednesday), CancellationToken.None);
            AssertEqual("IncompleteWeek", snapshots.Last().WeekStatus, "Source starts midweek");
            kernel.Tick(V2Tick(2, 100, 1, wednesday.AddDays(2)), CancellationToken.None);
            AssertEqual(2, snapshots.Last().WeekDays.Length, "No fabricated missing date"); AssertTrue(!snapshots.Last().WeekDays.Any(d => d.Date == wednesday.AddDays(1).Date), "Missing day stays absent");
        }
        private static ExplorerPatternSnapshot PatternAnchor(DateTime? time = null, string id = "a") => new ExplorerPatternSnapshot
        { Id = id, KnownAt = time ?? Start, ObservedAt = time ?? Start, HistoryStart = (time ?? Start).Date, KnownSequence = 1, Price = 100, Atr = 2, Activity = 100, Kind = "CloudPrefix", DayDelta = .5m, WeekStatus = "Available" };
        private static void TestPatternLabels(string root)
        {
            List<ExplorerPatternRow> rows = new List<ExplorerPatternRow>(); ExplorerPatternLabels labels = new ExplorerPatternLabels(new ExplorerPatternSpec { Direction = "Long", HorizonsMinutes = ImmutableArray.Create(1) }, rows.Add);
            labels.Add(PatternAnchor()); AssertEqual(0, rows.Count, "Future label absent at anchor");
            labels.Tick(V2Tick(2, 103, time: Start)); labels.Tick(V2Tick(3, 97, time: Start)); labels.Tick(V2Tick(4, 101, time: Start.AddSeconds(30)));
            labels.Tick(V2Tick(5, 999, time: Start.AddMinutes(2)));
            ExplorerPatternLabel label = rows.Single(r => r.Label.Minutes == 1).Label;
            AssertEqual("Target", label.FirstHit, "Ordinal first touch"); AssertEqual(2L, label.FirstHitSequence.Value, "Exact first touch ordinal");
            AssertEqual(1.5m, label.MfeAtr.Value, "Full horizon MFE"); AssertEqual(1.5m, label.MaeAtr.Value, "Full horizon MAE even after target"); AssertEqual(1m, label.Return.Value, "Last in-horizon return");
        }
        private static void TestPatternDayLabels(string root)
        {
            DateTime late = Start.Date.AddHours(23).AddMinutes(58); List<ExplorerPatternRow> rows = new List<ExplorerPatternRow>();
            ExplorerPatternLabels labels = new ExplorerPatternLabels(new ExplorerPatternSpec { Direction = "Long", HorizonsMinutes = ImmutableArray.Create(5) }, rows.Add);
            labels.Add(PatternAnchor(late)); labels.Tick(V2Tick(2, 103, time: late.AddMinutes(1))); labels.Tick(V2Tick(3, 999, time: late.Date.AddDays(1)));
            AssertEqual("Complete", rows.Single(r => r.Label.Minutes == 0).Label.Status, "Next date proves EOD");
            AssertEqual(3m, rows.Single(r => r.Label.Minutes == 0).Label.Return.Value, "Next-date price excluded");
            AssertEqual("DateCutoff", rows.Single(r => r.Label.Minutes == 5).Label.Status, "Fixed horizon never overnight");
            rows.Clear(); labels = new ExplorerPatternLabels(new ExplorerPatternSpec { Direction = "Long", HorizonsMinutes = ImmutableArray.Create(5) }, rows.Add);
            labels.Add(PatternAnchor(late)); labels.Tick(V2Tick(2, 103, time: late.AddMinutes(1))); labels.Complete();
            AssertTrue(rows.All(r => r.Label.Status == "Incomplete"), "Target hit does not prove EOF completeness");
        }
        private static void TestPatternUnknown(string root)
        {
            List<ExplorerPatternRow> rows = new List<ExplorerPatternRow>(); ExplorerPatternLabels labels = new ExplorerPatternLabels(new ExplorerPatternSpec { Direction = "Long", HorizonsMinutes = ImmutableArray.Create(1) }, rows.Add);
            labels.Add(PatternAnchor(id: "unknown") with { Atr = null }); AssertTrue(rows.All(r => r.Label.Status == "NoAtr"), "No ATR not zero target"); rows.Clear();
            labels.Add(PatternAnchor()); labels.Add(PatternAnchor(Start.AddSeconds(30), "b")); labels.Add(PatternAnchor(Start.AddMinutes(1), "c"));
            AssertEqual(4, rows.Count(r => r.Label.Status == "Overlap"), "Strict common overlap includes equality");
            labels.Complete(); AssertTrue(rows.Where(r => r.Snapshot.Id == "a").All(r => r.Label.Status == "NoFutureTrade"), "No future not failure");
        }
        private static void TestPatternSplit(string root)
        {
            DateTime monday = new DateTime(2026, 9, 7); DateTime[] dates = Enumerable.Range(0, 6).SelectMany(w => Enumerable.Range(0, w + 1).Select(d => monday.AddDays(7 * w + d))).ToArray();
            DateTime boundary = ExplorerPatternEvaluation.Split(dates).Value; AssertEqual(DayOfWeek.Monday, boundary.DayOfWeek, "Whole-week boundary");
            AssertEqual(monday.AddDays(28), boundary, "Split by weeks not rows");
            ExplorerPatternRow row = PatternRow(PatternAnchor(boundary.AddHours(10)) with { HistoryStart = boundary.AddDays(-1) }, true);
            AssertEqual("Purged", ExplorerPatternEvaluation.Partition(row, boundary), "Feature crosses boundary");
            row = PatternRow(PatternAnchor(boundary.AddDays(-1)), true); row = row with { Label = row.Label with { End = boundary } };
            AssertEqual("Purged", ExplorerPatternEvaluation.Partition(row, boundary), "Label crosses boundary");
        }
        private static void TestPatternGrammar(string root)
        {
            ExplorerPatternRule[] rules = ExplorerPatternGrammar.Rules(new ExplorerPatternSpec()).ToArray();
            AssertTrue(rules.All(r => r.Conditions.Length <= 3), "Bounded grammar"); AssertTrue(rules.Zip(rules.Skip(1), (a, b) => a.Conditions.Length <= b.Conditions.Length).All(v => v), "Simplest first");
            ExplorerPatternRule order = rules.First(r => r.Conditions.Length == 1 && r.Conditions[0].Feature == "SellBeforeLow");
            ExplorerPatternSnapshot snapshot = PatternAnchor() with { KnownSequence = 20, LastSellSequence = 10, LastLowSequence = 11, LastSellTime = Start, LastLowTime = Start };
            AssertEqual(true, ExplorerPatternGrammar.Matches(order, snapshot).Value, "Same-time sequence order");
            AssertEqual(false, ExplorerPatternGrammar.Matches(order, snapshot with { LastLowSequence = 21 }).Value, "Future confirmation excluded");
            ExplorerPatternRule week = rules.First(r => r.Conditions.Length == 1 && r.Conditions[0].Feature == "WeekDelta");
            AssertTrue(!ExplorerPatternGrammar.Matches(week, snapshot with { WeekStatus = "IncompleteWeek" }).HasValue, "Missing week unknown");
        }
        private static ExplorerPatternRow PatternRow(ExplorerPatternSnapshot s, bool success) => new ExplorerPatternRow(s,
            new ExplorerPatternLabel(s.Id, "Long", 15, s.KnownAt.AddMinutes(15), s.KnownSequence + 1, "Complete", success ? "Target" : "Adverse", null, null, 2, 1, 1, "Selected"));
        private static void TestPatternFit(string root)
        {
            DateTime monday = new DateTime(2026, 9, 7, 10, 0, 0), boundary = monday.Date.AddDays(21); List<ExplorerPatternRow> rows = new List<ExplorerPatternRow>();
            for (int week = 0; week < 4; week++)
            {
                for (int day = 0; day < 3; day++)
                {
                    DateTime time = monday.AddDays(7 * week + day);
                    rows.Add(PatternRow(PatternAnchor(time, "case" + week + day), true));
                    rows.Add(PatternRow(PatternAnchor(time.AddMinutes(30), "control" + week + day) with { DayDelta = -.5m }, false));
                    rows.Add(PatternRow(PatternAnchor(time.AddHours(1), "unmatched" + week + day) with { DayDelta = -.5m }, true));
                }
            }
            ExplorerPatternRule rule = ExplorerPatternGrammar.Rules(new ExplorerPatternSpec()).First(r => r.Conditions.Length == 1 && r.Conditions[0].Feature == "DayDelta" && r.Conditions[0].Sign == 1);
            (ExplorerPatternRule, string, int)[] candidates = { (rule, "Long", 15) };
            ExplorerPatternEvaluation.Strata strata = ExplorerPatternEvaluation.TrainStrata(rows.Select(r => r.Snapshot), boundary, 1, CancellationToken.None);
            ExplorerPatternMetrics fit = ExplorerPatternEvaluation.Measure(() => rows, candidates, "Fit", boundary, strata, CancellationToken.None).Values.Single();
            AssertEqual(9L, fit.Eligible, "Matched cases"); AssertEqual(9L, fit.Control, "Other-hour controls excluded"); AssertEqual(1m, fit.Difference.Value, "Matched contrast");
            List<ExplorerPatternRow> changed = rows.Select(r => r.Snapshot.KnownAt >= boundary ? r with { Label = r.Label with { FirstHit = r.Label.FirstHit == "Target" ? "Adverse" : "Target" }, Snapshot = r.Snapshot with { Atr = 99999 } } : r).ToList();
            AssertEqual(JsonSerializer.Serialize(fit), JsonSerializer.Serialize(ExplorerPatternEvaluation.Measure(() => changed, candidates, "Fit", boundary, strata, CancellationToken.None).Values.Single()), "Test labels/features cannot alter Fit");
            AssertEqual(strata, ExplorerPatternEvaluation.TrainStrata(changed.Select(r => r.Snapshot), boundary, 1, CancellationToken.None), "Strata trained only Fit");
            AssertTrue(ExplorerPatternEvaluation.Measure(() => changed, candidates, "Test", boundary, strata, CancellationToken.None).Values.Single().Difference < 0, "Frozen rule loses held-out effect");
        }
        private static void TestPatternSupport(string root)
        {
            ExplorerPatternMetrics oneDay = new ExplorerPatternMetrics(100, 90, 100, 10, 1, 1, .9m, .1m, .8m, .9m, .9m, 0, 0, "a", "b", "c");
            ExplorerPatternSpec plan = new ExplorerPatternSpec(); AssertTrue(!ExplorerPatternEvaluation.Supported(oneDay, plan), "100 anchors not 100 days");
            AssertTrue(!ExplorerPatternEvaluation.Supported(oneDay with { Dates = 3, Weeks = 2, ControlDates = 1, ControlWeeks = 1 }, plan), "Control also needs distinct support");
            AssertTrue(ExplorerPatternEvaluation.Supported(oneDay with { Dates = 3, Weeks = 2, ControlDates = 3, ControlWeeks = 2 }, plan), "Distinct support");
            AssertTrue(ExplorerPatternEvaluation.Status(oneDay, plan).Contains("Недостаточно"), "Tiny Test not ready signal");
            AssertTrue(ExplorerPatternEvaluation.Status(oneDay with { Eligible = 0, Success = 0, Control = 0, Dates = 0, Weeks = 0, Rate = null, Difference = null }, plan).Contains("Недостаточно"), "Empty Test explicit");
            AssertTrue(ExplorerPatternEvaluation.Status(oneDay with { Dates = 3, ControlDates = 3, ControlWeeks = 1, Difference = -.1m }, plan).Contains("Не подтвердился"), "Missing Test effect");
        }
        private static void TestPatternReplay(string root)
        {
            List<OrderFlowDeal> ticks = new List<OrderFlowDeal>();
            for (int i = 0; i < 25; i++) { ticks.Add(V2Tick(i + 1, 100 + i % 3, 25, Start.AddMinutes(i))); }
            ticks.Add(V2Tick(26, 105, 25, ticks.Last().Time)); ticks.Add(V2Tick(27, 100, 25, Start.AddDays(1)));
            ExplorerRun run = V2Run(V2Spec(root, ticks.Select(t => Row(t.Time, t.Price, t.Volume, t.Side)).ToArray()));
            ExplorerPatternSpec plan = new ExplorerPatternSpec { HorizonsMinutes = ImmutableArray.Create(1) };
            using ExplorerReplayCursor cursor = new ExplorerReplayCursor(run, null, CancellationToken.None, plan);
            for (int count = 1; count <= ticks.Count; count++)
            {
                List<ExplorerPatternSnapshot> snapshots = new List<ExplorerPatternSnapshot>(); List<ExplorerPatternLabel> labels = new List<ExplorerPatternLabel>();
                ExplorerPatternKernel independent = new ExplorerPatternKernel(run.Spec, plan, snapshots.Add, r => labels.Add(r.Label));
                foreach (OrderFlowDeal tick in ticks.Take(count)) { independent.Tick(tick, CancellationToken.None); }
                ExplorerFrame frame = cursor.Step();
                AssertEqual(JsonSerializer.Serialize(snapshots.TakeLast(250)), JsonSerializer.Serialize(frame.PatternSnapshots), "Independent causal anchors " + count);
                AssertEqual(JsonSerializer.Serialize(labels.TakeLast(250)), JsonSerializer.Serialize(frame.PatternLabels), "Independent future label availability " + count);
                AssertTrue(frame.PatternSnapshots.All(s => s.KnownSequence <= frame.Sequence), "No future anchor");
                AssertTrue(frame.PatternLabels.All(l => l.EndSequence <= frame.Sequence), "No future proof");
            }
        }
        private static void TestPatternArtifacts(string root)
        {
            ExplorerRunSpec spec = V2Spec(root, Enumerable.Range(0, 30).Select(i => Row(Start.AddMinutes(i), 100 + i % 3, 25, Side.Buy)).Append(Row(Start.AddDays(1), 100, 1, Side.Buy)).ToArray());
            ExplorerPatternSpec plan = new ExplorerPatternSpec { CandidateBudget = 4, HorizonsMinutes = ImmutableArray.Create(1) };
            ExplorerRun catalog = V2Run(spec); string original = ExplorerStorage.HashFile(Path.Combine(catalog.CatalogPath, "manifest.json"), CancellationToken.None);
            ExplorerPatternRun run = ExplorerPatternRunner.Run(spec, plan, CancellationToken.None);
            AssertEqual(original, ExplorerStorage.HashFile(Path.Combine(catalog.CatalogPath, "manifest.json"), CancellationToken.None), "Old catalog unchanged");
            ExplorerStorage.Verify(catalog.CatalogPath, ExplorerRunSpec.CatalogVersion, catalog.Spec.CatalogSpecHash, CancellationToken.None);
            ExplorerPatternQuality quality = JsonSerializer.Deserialize<ExplorerPatternQuality>(File.ReadAllText(Path.Combine(run.Directory, "quality.json")));
            AssertTrue(quality.BudgetExcluded > 0 && quality.Considered == 4, "Explicit budget funnel");
            AssertEqual(run.Manifest.Hash, ExplorerPatternRunner.Run(spec, plan, CancellationToken.None).Manifest.Hash, "Deterministic reuse");
            File.Delete(spec.InputPath); ExplorerPatternRun opened = ExplorerPatternRunner.Open(run.Directory, null, CancellationToken.None);
            AssertEqual(run.Manifest.Hash, opened.Manifest.Hash, "Preview without source file");
            AssertTrue(plan.Hash(run.Run.Spec) != (plan with { TargetAtr = 2 }).Hash(run.Run.Spec), "Advanced numeric setting changes identity");
            AssertEqual(0, Directory.GetDirectories(spec.OutputRootPath).Count(p => Path.GetFileName(p).StartsWith(".")), "No staging leftovers");
        }
        private static void TestPatternCancel(string root)
        {
            ExplorerRunSpec spec = V2Spec(root, Row(Start, 100, 50, Side.Buy));
            using CancellationTokenSource cancellation = new CancellationTokenSource(); cancellation.Cancel();
            Expect<OperationCanceledException>(() => ExplorerPatternRunner.Run(spec, new ExplorerPatternSpec(), cancellation.Token));
            ExplorerPatternRun run = ExplorerPatternRunner.Run(spec, new ExplorerPatternSpec { CandidateBudget = 1 }, CancellationToken.None);
            File.AppendAllText(Path.Combine(run.Directory, "pattern-spec.json"), " ");
            Expect<InvalidDataException>(() => ExplorerPatternRunner.Open(run.Directory, spec.InputPath, CancellationToken.None));
        }
        private static void TestPatternExactEof(string root)
        {
            List<ExplorerPatternRow> rows = new List<ExplorerPatternRow>();
            ExplorerPatternLabels labels = new ExplorerPatternLabels(new ExplorerPatternSpec { Direction = "Long", HorizonsMinutes = ImmutableArray.Create(15) }, rows.Add);
            labels.Add(PatternAnchor()); labels.Tick(V2Tick(2, 103, time: Start.AddMinutes(15))); labels.Tick(V2Tick(3, 97, time: Start.AddMinutes(15))); labels.Complete();
            AssertEqual("Complete", rows.Single(r => r.Label.Minutes == 15).Label.Status, "Exact fixed boundary on EOF is covered");
            AssertEqual(1.5m, rows.Single(r => r.Label.Minutes == 15).Label.MaeAtr.Value, "All equal-time boundary ordinals consumed");
            AssertEqual("Incomplete", rows.Single(r => r.Label.Minutes == 0).Label.Status, "EOF still cannot prove EOD");
        }
        private static void TestPatternSettingsIdentity(string root)
        {
            ExplorerRunSpec spec = V2Spec(root, Row(Start, 100, 1, Side.Buy)); ExplorerPatternSpec plan = new ExplorerPatternSpec();
            foreach (ExplorerStudySpec study in new[] { spec.Study with { ActivitySeconds = 17 }, spec.Study with { SwingReversalTicks = 7 },
                spec.Study with { SwingAtrFactor = 2 }, spec.Study with { AdaptSwing = false }, spec.Study with { Profile = "Cloud2/Base" } })
            { AssertTrue(plan.Hash(spec) != plan.Hash(spec with { Study = study }), "Every causal study setting enters search identity"); }
        }
        private static void TestPatternMatchAudit(string root)
        {
            ExplorerPatternRule rule = ExplorerPatternGrammar.Rules(new ExplorerPatternSpec()).First(r => r.Conditions.Length == 1 && r.Conditions[0].Feature == "DayDelta" && r.Conditions[0].Sign == 1);
            List<ExplorerPatternRow> rows = Enumerable.Range(0, 4).Select(i => PatternRow(PatternAnchor(Start.AddMinutes(i), "case" + i), true)).ToList();
            rows.Add(PatternRow(PatternAnchor(Start.AddMinutes(10), "control") with { DayDelta = -.5m }, false));
            rows.Add(PatternRow(PatternAnchor(Start.AddHours(1), "unmatched"), true));
            List<ExplorerPatternMembership> members = new List<ExplorerPatternMembership>();
            ExplorerPatternMetrics m = ExplorerPatternEvaluation.Measure(() => rows, new[] { (rule, "Long", 15) }, "Fit", null,
                new ExplorerPatternEvaluation.Strata(100, 100, 2, 2), CancellationToken.None, members.Add).Values.Single();
            AssertEqual(5L, m.BeforeMatchingCases, "Pre-match denominator retained"); AssertEqual(3L, m.BalanceExcluded, "Explicit surplus count");
            AssertEqual(1L, m.Unmatched, "Explicit no-comparator count"); AssertEqual(6, members.Count, "Every full anchor membership retained");
            AssertEqual(1, m.ControlDates, "Control dates reported independently");
        }
        private static void TestPatternRelativeHistory(string root)
        {
            DateTime friday = new DateTime(2026, 9, 11, 10, 0, 0), monday = friday.Date.AddDays(3).AddHours(10);
            ExplorerRunSpec spec = V2Spec(root, Row(Start, 100, 1, Side.Buy)) with
            { Profiles = ImmutableArray.Create(new ExplorerProfile { SingleTicks = true, RelativeVolume = true, VolumeMinimum = 1, VolumeDates = 5 }) };
            List<ExplorerPatternSnapshot> snapshots = new List<ExplorerPatternSnapshot>();
            ExplorerPatternKernel kernel = new ExplorerPatternKernel(spec, new ExplorerPatternSpec(), snapshots.Add, _ => { });
            kernel.Tick(V2Tick(1, 100, 30, friday), CancellationToken.None); kernel.Tick(V2Tick(2, 101, 40, friday.AddSeconds(1)), CancellationToken.None);
            kernel.Tick(V2Tick(3, 100, 50, monday), CancellationToken.None);
            ExplorerPatternSnapshot anchor = snapshots.Last(s => s.Kind == "CloudPrefix" && s.KnownAt == monday);
            AssertTrue(anchor.RelativeVolume.HasValue, "Causal prior-date background is available");
            AssertTrue(anchor.HistoryStart < monday.Date, "Background provenance crosses week boundary");
            AssertEqual("Purged", ExplorerPatternEvaluation.Partition(PatternRow(anchor, true), monday.Date), "No silent Fit background in Test");
        }
        private static void TestFollowupSavedInterval(string root)
        {
            DateTime remote = Start.AddDays(20); ExplorerRun run = V2Run(V2Spec(root, Row(Start, 100, 20, Side.Buy), Row(remote, 200, 20, Side.Sell)));
            string study = Path.Combine(root, "saved-markers"); Directory.CreateDirectory(study);
            using (ExplorerStorage.RowWriter<ExplorerPivot> pivots = new ExplorerStorage.RowWriter<ExplorerPivot>(study, "pivots"))
            {
                for (int i = 0; i < 300; i++) { pivots.Add(new ExplorerPivot("High", 100, Start, 1, Start, 1, 1, "Known")); }
                pivots.Add(new ExplorerPivot("Low", 200, remote, 2, remote, 2, 1, "Known"));
            }
            using (ExplorerStorage.RowWriter<ExplorerObservation> observations = new ExplorerStorage.RowWriter<ExplorerObservation>(study, "observations")) { }
            run = run with { StudyPath = study }; File.Delete(run.Spec.InputPath);
            ExplorerChartContext context = ExplorerChartContext.Load(run, remote.Date, remote.Date.AddDays(1).AddTicks(-1), OrderFlowDisplayTimeFrame.Min1, CancellationToken.None);
            AssertEqual(1, context.Pivots.Length, "Loads matching marker beyond first page"); AssertEqual(remote, context.Pivots[0].ObservedAt, "Remote marker not old page");
            AssertTrue(context.Bars.Count > 0 && context.Clouds.All(c => c.Time.Date == remote.Date), "Candles and Cloud same interval without raw");
        }
        private static object UiCall(CloudExplorerControl ui, string name, params object[] args) => typeof(CloudExplorerControl).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ui, args);
        private static T UiField<T>(CloudExplorerControl ui, string name) => (T)typeof(CloudExplorerControl).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ui);
        private static void DrainUiJob(CloudExplorerControl ui)
        {
            Stopwatch timeout = Stopwatch.StartNew();
            while (UiField<ExplorerJob>(ui, "_job") != null)
            { AssertTrue(timeout.Elapsed.TotalSeconds < 15, "UI fixture job timeout"); Thread.Sleep(5); UiCall(ui, "Poll", null, EventArgs.Empty); }
        }
        private static void TestFollowupObservationVwap(string root)
        {
            ExplorerRun run = V2Run(V2Spec(root, Row(Start, 100, 20, Side.Buy)));
            ExplorerCloud cloud = ExplorerStorage.ReadRows<ExplorerCloud>(run.CatalogPath, "catalog").First();
            ExplorerTrigger trigger = new ExplorerTrigger("trigger", cloud.Id, cloud.Profile, "Cloud", Start, 1, 10, 20, 20, 0, 100, 100, 100, 100, 1);
            ExplorerObservation first = new ExplorerObservation("first", "Long", Start, 1, Start.AddSeconds(1), 2, "VolumeArmed", "Complete", trigger,
                null, null, null, null, null, null, 100, null, 1, 101, Start, 0);
            ExplorerObservation second = first with { Id = "second", WatchVwap = 102 };
            string study = Path.Combine(root, "watch-samples"); Directory.CreateDirectory(study);
            using (ExplorerStorage.RowWriter<ExplorerObservation> writer = new ExplorerStorage.RowWriter<ExplorerObservation>(study, "observations")) { writer.Add(first); writer.Add(second); }
            using (ExplorerStorage.RowWriter<ExplorerPivot> writer = new ExplorerStorage.RowWriter<ExplorerPivot>(study, "pivots")) { }
            using (ExplorerStorage.RowWriter<ExplorerTrigger> writer = new ExplorerStorage.RowWriter<ExplorerTrigger>(study, "triggers")) { writer.Add(trigger); }
            using (ExplorerStorage.RowWriter<ExplorerWatchVwap> writer = new ExplorerStorage.RowWriter<ExplorerWatchVwap>(study, "watch-vwap-samples"))
            { writer.Add(new ExplorerWatchVwap(first.Id, Start, 1, 101, 1)); writer.Add(new ExplorerWatchVwap(second.Id, Start, 1, 102, 2)); }
            using CloudExplorerControl ui = new CloudExplorerControl(); UiCall(ui, "InstallRun", run with { StudyPath = study });
            UiCall(ui, "ShowObservation", first); UiCall(ui, "ShowObservation", second); DrainUiJob(ui);
            AssertEqual(second.Id, UiField<string>(ui, "_observationId"), "Busy observation queue retains latest selection");
            UiCall(ui, "Bands", null, new RoutedEventArgs());
            ExplorerChart chart = UiField<ExplorerChart>(ui, "_chart");
            IReadOnlyList<ExplorerVwapSample> samples = (IReadOnlyList<ExplorerVwapSample>)typeof(ExplorerChart).GetField("_vwap", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(chart);
            AssertEqual(102m, samples.Single().Vwap, "Correct selected watch survives interval load and Bands repaint");
            UiCall(ui, "ShowObservation", first with { Trigger = null }); DrainUiJob(ui);
            AssertEqual(0, UiField<IReadOnlyList<ExplorerVwapSample>>(ui, "_observationVwap").Count, "Control observation never inherits armed VWAP");
            typeof(CloudExplorerControl).GetField("_selectedAnchor", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(ui,
                new ExplorerAnchor(cloud.Id, cloud.FirstSequence, cloud.KnownSequence ?? long.MaxValue, cloud.StartTime.Date));
            UiCall(ui, "Anchor", null, new RoutedEventArgs()); UiCall(ui, "ShowObservation", second); DrainUiJob(ui);
            AssertEqual(second.Id, UiField<string>(ui, "_observationId"), "Earlier anchor completion preserves queued observation");
            AssertEqual(102m, UiField<IReadOnlyList<ExplorerVwapSample>>(ui, "_observationVwap").Single().Vwap, "Queued watch samples supersede manual anchor");
            UiCall(ui, "ClearObservationContext"); UiCall(ui, "Paint");
            samples = (IReadOnlyList<ExplorerVwapSample>)typeof(ExplorerChart).GetField("_vwap", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(chart);
            AssertEqual(100m, samples.Single().Vwap, "Leaving observation restores calculated manual-anchor VWAP");
        }
        private static void TestPatternLatestExample(string root)
        {
            ExplorerRun run = V2Run(V2Spec(root, Row(Start, 100, 20, Side.Buy)));
            string directory = Path.Combine(root, "example-selection"); Directory.CreateDirectory(directory);
            ExplorerPatternSnapshot first = PatternAnchor(id: "snapshot/1") with { WeekDays = ImmutableArray<ExplorerPatternDay>.Empty };
            ExplorerPatternSnapshot second = first with { Id = "snapshot/2" }, third = first with { Id = "snapshot/3" };
            using (ExplorerStorage.RowWriter<ExplorerPatternSnapshot> writer = new ExplorerStorage.RowWriter<ExplorerPatternSnapshot>(directory, "snapshots")) { writer.Add(first); writer.Add(second); writer.Add(third); }
            using (ExplorerStorage.RowWriter<ExplorerPatternLabel> writer = new ExplorerStorage.RowWriter<ExplorerPatternLabel>(directory, "labels"))
            { writer.Add(PatternRow(first, true).Label); writer.Add(PatternRow(second, true).Label); writer.Add(PatternRow(third, false).Label); }
            ExplorerPatternSpec plan = new ExplorerPatternSpec();
            ExplorerPatternRun pattern = new ExplorerPatternRun(run, plan, directory, run.Catalog);
            ExplorerPatternMetrics metrics = new ExplorerPatternMetrics(10, 8, 10, 2, 3, 2, .8m, .2m, .6m, .7m, .9m, 0, 0, first.Id, null, null);
            ExplorerPatternCard a = new ExplorerPatternCard("a", ExplorerPatternGrammar.Rules(plan).First(), "Long", 15, 1, metrics, metrics, "A");
            ExplorerPatternCard b = a with { Id = "b", Test = metrics with { SuccessId = second.Id, FailureId = third.Id } };
            using CloudExplorerControl ui = new CloudExplorerControl(); UiCall(ui, "InstallRun", run);
            typeof(CloudExplorerControl).GetField("_patternRun", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(ui, pattern);
            ui.DataGridPatternCards.ItemsSource = new[] { a, b }; ui.DataGridPatternCards.SelectedItem = a;
            Action<object> stale = UiField<Action<object>>(ui, "_finish");
            ui.DataGridPatternCards.SelectedItem = b; ui.ComboBoxPatternExample.SelectedIndex = 1;
            stale((first, PatternRow(first, true).Label));
            AssertTrue(UiField<ExplorerPatternSnapshot>(ui, "_patternExample") == null, "Stale callback cannot attach A under B");
            AssertTrue(!ui.TextBoxPatternCard.Text.Contains("Пример " + first.Id), "Stale callback cannot append old outcome");
            DrainUiJob(ui);
            AssertEqual(b.Id, UiField<ExplorerPatternCard>(ui, "_patternCard").Id, "Latest card retained");
            AssertEqual(third.Id, UiField<ExplorerPatternSnapshot>(ui, "_patternExample").Id, "Latest card and failure class loaded after busy job");
            AssertTrue(!UiField<bool>(ui, "_pendingPatternExample"), "Deferred example consumed once");
        }
        private static void TestFollowupBusyFocus(string root)
        {
            using CloudExplorerControl ui = new CloudExplorerControl();
            UiCall(ui, "FocusInput", new ExplorerInputException("Study.Enabled", "Проверка"));
            AssertEqual(4, ui.TabControlOptions.SelectedIndex, "Study.Enabled selects study not episode");
            UiCall(ui, "FocusInput", new ExplorerInputException("Episodes.AtrFactor", "Проверка"));
            AssertEqual(3, ui.TabControlOptions.SelectedIndex, "Episode factor not formation factor");
            string focused = null; ui.InputFocus = field => focused = field; UiCall(ui, "FocusInput", new ExplorerInputException("FromDate", "Проверка"));
            AssertEqual("FromDate", focused, "Input owner receives exact field");
            ExplorerJob job = new ExplorerJob(token => { token.WaitHandle.WaitOne(); token.ThrowIfCancellationRequested(); return null; });
            typeof(CloudExplorerControl).GetField("_job", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(ui, job);
            typeof(CloudExplorerControl).GetField("_page", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(ui, new ExplorerPage(Array.Empty<ExplorerCloud>(), 0, 0, 0, 250));
            ui.TabControlResult.SelectedIndex = 2; UiCall(ui, "NextPage", null, new RoutedEventArgs());
            Dictionary<string, long> offsets = (Dictionary<string, long>)typeof(CloudExplorerControl).GetField("_tableOffsets", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ui);
            AssertEqual(0, offsets.Count, "Busy click cannot mutate next offset");
        }
        private static void TestPatternReplayUi(string root)
        {
            ExplorerRunSpec spec = V2Spec(root, Row(Start, 100, 20, Side.Buy));
            ExplorerPatternRun run = ExplorerPatternRunner.Run(spec, new ExplorerPatternSpec { CandidateBudget = 1 }, CancellationToken.None);
            using CloudExplorerControl ui = new CloudExplorerControl { RequestProvider = () => spec };
            UiCall(ui, "InstallPattern", run);
            ExplorerPatternMetrics metrics = new ExplorerPatternMetrics(10, 8, 10, 2, 3, 2, .8m, .2m, .6m, .7m, .9m, 0, 0, null, null, null);
            ExplorerPatternRule rule = ExplorerPatternGrammar.Rules(run.Plan).First();
            ExplorerPatternCard first = new ExplorerPatternCard("first", rule, "Long", 15, 1, metrics, metrics, "Исторический результат");
            ExplorerPatternCard second = first with { Id = "second", FitRank = 2 };
            ui.DataGridPatternCards.ItemsSource = new[] { first, second }; ui.DataGridPatternCards.SelectedItem = first;
            UiCall(ui, "PatternReplay", null, new RoutedEventArgs());
            AssertEqual(Visibility.Hidden, ui.DataGridPatternCards.Visibility, "Historical rankings hidden in replay");
            ui.DataGridPatternCards.SelectedItem = second;
            AssertTrue(!ui.TextBoxPatternCard.Text.Contains("8/10"), "Selecting another card cannot expose future outcome");
            AssertTrue(ui.DataGridPatternWeek.ItemsSource == null, "Cannot load future week overview while replay waits");
        }
        /// <summary>Explicit owner-file offline benchmark, using the default frozen preset and a separate output directory.</summary>
        private static int RunPatternOwner(string[] args)
        {
            try
            {
                if (args.Length != 3) { throw new ArgumentException("--pattern-input <source> <separate output>"); }
                ExplorerRunSpec spec = ExplorerPatternSpec.Preset(new ExplorerRunSpec { InputPath = args[1], OutputRootPath = args[2], PriceStep = 1 });
                Stopwatch watch = Stopwatch.StartNew(); double last = -5;
                ExplorerPatternRun run = ExplorerPatternRunner.Run(spec, new ExplorerPatternSpec(), CancellationToken.None, p =>
                { if (watch.Elapsed.TotalSeconds - last >= 5) { last = watch.Elapsed.TotalSeconds; Console.WriteLine(p.Phase + " rows=" + p.Rows + " events=" + p.Clouds + " MiB=" + p.MemoryBytes / 1048576); } });
                long bytes = Directory.GetFiles(args[2], "*", SearchOption.AllDirectories).Sum(p => new FileInfo(p).Length);
                Console.WriteLine("PATTERN OWNER PASS hash=" + run.Manifest.Hash + " sourceSha=" + run.Manifest.InputSha256 + " rows=" + run.Manifest.Rows + " days=" + run.Manifest.Dates.Length +
                    " seconds=" + watch.Elapsed.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture) + " peakWorkingSet=" + Process.GetCurrentProcess().PeakWorkingSet64 + " artifactBytes=" + bytes);
                Console.WriteLine(File.ReadAllText(Path.Combine(run.Directory, "quality.json"))); return 0;
            }
            catch (Exception error) { Console.WriteLine("PATTERN OWNER FAIL " + error); return 1; }
        }
    }
}
