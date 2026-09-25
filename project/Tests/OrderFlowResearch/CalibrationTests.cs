/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.Entity;
using OsEngine.OsData.OrderFlow;
using OsEngine.OsData.OrderFlow.Calibration;
using OsEngine.OsData.OrderFlow.Explorer;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;

namespace OsEngine.OrderFlowResearch.Tests
{
    internal static partial class Program
    {
        /// <summary>Offline calibration acceptance using temporary synthetic fixtures; no desktop app, connector, future labels or real trades.</summary>
        private static void RegisterCalibration(string root)
        {
            Run("CalibrationTimeRangeBoundaries", root, TestCalibrationRanges);
            Run("CalibrationTimezoneDstAndDisabled", root, TestCalibrationTimezone);
            Run("CalibrationRawQualityAndQuantiles", root, TestCalibrationRaw);
            Run("CalibrationChainParityAndGrid", root, TestCalibrationParity);
            Run("CalibrationDateRangeCutoff", root, TestCalibrationCutoff);
            Run("CalibrationDiagonalSumAndStrictStacks", root, TestCalibrationDiagonal);
            Run("CalibrationFiltersNoRawAndStableIds", root, TestCalibrationFilters);
            Run("CalibrationIndependentRulesAndOverlap", root, TestCalibrationRules);
            Run("CalibrationIdentityAndRoundTrip", root, TestCalibrationIdentity);
            Run("CalibrationCancelAndInvalidSuffix", root, TestCalibrationCancel);
            Run("CalibrationResourceLimits", root, TestCalibrationLimits);
            Run("CalibrationNeighborSensitivity", root, TestCalibrationNeighbors);
            Run("CalibrationExactPercentAndDecimalIdentity", root, TestCalibrationExactPercent);
            Run("CalibrationAnatomyConservation", root, TestCalibrationAnatomy);
            Run("CalibrationTableGlobalPaging", root, TestCalibrationTablePaging);
            Run("CalibrationReplayCompletionPrefix", root, TestCalibrationReplay);
            Run("CalibrationDetachedWindowLifecycle", root, TestCalibrationWindows);
            Run("CalibrationThemeAndVisualMarkup", root, TestCalibrationMarkup);
            Run("CalibrationTunerComponent", root, TestCalibrationTuner);
            Run("CalibrationPreliminaryTickWorkflow", root, TestCalibrationPreliminary);
            Run("CalibrationRuleEditorRangeIsolation", root, TestCalibrationEditorIsolation);
            Run("CalibrationMillionTickCompactCache", root, TestCalibrationMillion);
            Run("CalibrationSpilledExactQuantiles", root, TestCalibrationSpilledQuantiles);
            Run("CalibrationHighCardinalityCellMemory", root, TestCalibrationCellMemory);
            Run("CalibrationScratchCancelAndBudget", root, TestCalibrationScratchFailure);
            Run("CalibrationManagedMemoryGuard", root, TestCalibrationManagedGuard);
        }

        private static CalibrationSpec CalSpec(string root, params string[] rows)
        {
            OrderFlowResearchRequest input = Request(root, rows);
            return new CalibrationSpec { InputPath = input.TicksFilePath, OutputRootPath = Path.Combine(root, "calibration"),
                Range = new TimeRangeProfile { Id = "all", Name = "All", DayMask = 127 },
                Gaps = ImmutableArray.Create(100, 1000), Ranges = ImmutableArray.Create(1, 3) };
        }
        private static CalibrationRun CalRun(CalibrationSpec spec) => CalibrationEngine.Run(spec, CancellationToken.None);
        private static CalibrationEvent[] CalEvents(CalibrationRun run, FormationSpec formation) => CalibrationStorage.Events(run, formation, CancellationToken.None).ToArray();

        private static void TestCalibrationRanges(string root)
        {
            ImmutableArray<TimeRangeProfile> ranges = TimeRangeProfile.Presets();
            DateTime day = new DateTime(2026, 9, 18);
            AssertTrue(ranges[0].Includes(day.AddHours(10.5).AddTicks(-1)), "Morning upper interior");
            AssertTrue(!ranges[0].Includes(day.AddHours(10.5)) && ranges[1].Includes(day.AddHours(10.5)), "10:30 exact");
            AssertTrue(!ranges[1].Includes(day.AddHours(19)) && ranges[2].Includes(day.AddHours(19)), "19:00 exact");
            AssertTrue(ranges[2].Includes(day.AddMinutes(1430)) && !ranges[2].Includes(day.AddMinutes(1431)), "Evening inclusive minute");
            AssertTrue(ranges[6].Includes(day.AddDays(1)) && !ranges[7].Includes(day.AddDays(1)) && ranges[7].Includes(day.AddDays(2)), "Weekend isolation");
            TimeRangeProfile custom = new TimeRangeProfile { StartTime = TimeSpan.FromHours(12), EndTime = TimeSpan.FromHours(13), DayMask = 1 << 5 };
            AssertTrue(custom.Includes(day.AddHours(12)) && !custom.Includes(day.AddDays(1).AddHours(12)), "Custom mask");
            bool rejected = false; try { (custom with { EndTime = TimeSpan.FromHours(1) }).Validate(); } catch (ArgumentException) { rejected = true; }
            AssertTrue(rejected, "No overnight range");
        }
        private static void TestCalibrationTimezone(string root)
        {
            TimeRangeProfile overlay = TimeRangeProfile.Presets()[8];
            AssertTrue(!overlay.Enabled && !overlay.Includes(Start), "Disabled without mapping");
            overlay = overlay with { SourceTimeZone = "UTC" };
            AssertTrue(overlay.Includes(new DateTime(2026, 1, 12, 13, 30, 0)), "Winter 08:30 NY");
            AssertTrue(!overlay.Includes(new DateTime(2026, 1, 12, 12, 30, 0)), "Winter not fixed summer offset");
            AssertTrue(overlay.Includes(new DateTime(2026, 7, 13, 12, 30, 0)), "Summer DST");
            AssertTrue(!overlay.Includes(new DateTime(2026, 7, 13, 13, 30, 0)), "Open exclusive");
            AssertTrue(!overlay.Includes(new DateTime(2026, 7, 11, 12, 30, 0)), "No weekend NY overlay");
        }
        private static void TestCalibrationRaw(string root)
        {
            CalibrationRun run = CalRun(CalSpec(root, Row(Start, 100, 1, Side.Buy), Row(Start, 101, 2, Side.Sell),
                Row(Start.AddSeconds(1), 100, 3, Side.Buy), Row(Start.AddSeconds(2), 101, 4, Side.Sell)));
            TickStatistics stats = CalibrationEngine.Ticks(run.Directory, run.Spec, null, 2, CancellationToken.None);
            AssertEqual(2m, stats.Volumes.P50.Value, "Nearest rank P50"); AssertEqual(4m, stats.Volumes.P95.Value, "P95");
            AssertEqual(3L, stats.Passed, "Threshold equality"); AssertEqual(1L, run.Manifest.Quality.DuplicateTimestamps, "Duplicates");
            AssertEqual(4L, run.Manifest.Quality.ZeroMicroseconds, "Second resolution count"); AssertEqual(1, stats.ActiveDays, "Denominator");
            AssertEqual(1000m, stats.Gaps.P50.Value, "Included gaps");
            TickStatistics buy = CalibrationEngine.Ticks(run.Directory, run.Spec, Side.Buy, 2, CancellationToken.None);
            AssertEqual(2L, buy.Total, "Buy distribution"); AssertEqual(1L, buy.Passed, "Buy threshold");
        }
        private static void TestCalibrationParity(string root)
        {
            CalibrationSpec spec = CalSpec(root, Row(Start, 100, 5, Side.Sell), Row(Start.AddMilliseconds(50), 999, 1, Side.Buy),
                Row(Start.AddMilliseconds(100), 101, 5, Side.Buy), Row(Start.AddMilliseconds(200).AddTicks(10), 104, 6, Side.Buy),
                Row(Start.AddMilliseconds(300), 104, 7, Side.Sell)) with { MinimumTickVolume = 5 };
            CalibrationRun run = CalRun(spec);
            foreach (ParameterCell cell in run.Manifest.Cells)
            {
                List<ExplorerCloud> expected = new List<ExplorerCloud>();
                ExplorerProfile p = new ExplorerProfile { MinimumTickVolume = 5, SingleTicks = cell.Formation.Mode == FormationMode.Single,
                    MaximumGapMilliseconds = cell.Formation.MaximumGapMilliseconds, MaximumRangeTicks = cell.Formation.MaximumRangeTicks };
                ExplorerCatalog direct = new ExplorerCatalog(new ExplorerRunSpec { PriceStep = 1, Profiles = ImmutableArray.Create(p) }, expected.Add, _ => { });
                foreach (OrderFlowDeal tick in CalibrationCache.Read(run.Directory, CancellationToken.None)) { direct.Add(tick); }
                direct.Complete(); CalibrationEvent[] actual = CalEvents(run, cell.Formation);
                AssertEqual(expected.Count, actual.Length, "Grid count");
                for (int i = 0; i < actual.Length; i++)
                {
                    AssertEqual(expected[i].FirstSequence, actual[i].Evidence.FirstSequence, "First source");
                    AssertEqual(expected[i].LastSequence, actual[i].Evidence.LastSequence, "Last source");
                    AssertEqual(expected[i].Reason, actual[i].Evidence.Reason, "Priority");
                    AssertEqual(expected[i].Volume, actual[i].Volume, "Volume parity");
                    AssertEqual(expected[i].Inside.Pairs.Count, actual[i].Evidence.Inside.Pairs.Count, "Pairs parity");
                }
            }
            CalibrationEvent[] exact = CalEvents(run, run.Manifest.Cells.First(c => c.Formation.Mode == FormationMode.Chain && c.Formation.MaximumGapMilliseconds == 100 && c.Formation.MaximumRangeTicks == 1).Formation);
            AssertEqual(2, exact[0].Evidence.Count, "Excluded small tick neither changes nor breaks Chain");
            AssertEqual("Gap", exact[0].Evidence.Reason, "Simultaneous gap/range priority");
            AssertEqual(5m, exact[0].LargestTick, "Largest physical included tick");
        }
        private static void TestCalibrationCutoff(string root)
        {
            DateTime boundary = Start.Date.AddHours(10.5);
            CalibrationRun run = CalRun(CalSpec(root, Row(boundary.AddTicks(-10), 100, 1, Side.Buy), Row(boundary, 100, 1, Side.Buy),
                Row(boundary.AddDays(3).AddTicks(-10), 100, 1, Side.Buy)) with { Range = TimeRangeProfile.Presets()[0] });
            CalibrationEvent[] events = CalEvents(run, run.Manifest.Cells[1].Formation);
            AssertEqual(2, events.Length, "Separate ranges/dates"); AssertEqual("TimeRangeCutoff", events[0].Evidence.Reason, "Range completion");
            AssertEqual(boundary, events[0].Evidence.KnownAt.Value, "Known at excluded tick"); AssertEqual("OpenAtEnd", events[1].Evidence.Reason, "EOF remains unknown");
            CalibrationRun all = CalRun(CalSpec(root, Row(Start, 100, 1, Side.Buy), Row(Start.AddDays(1), 100, 1, Side.Buy)));
            AssertEqual("DateCutoff", CalEvents(all, all.Manifest.Cells[1].Formation)[0].Evidence.Reason, "Source date cutoff");
        }
        private static void TestCalibrationDiagonal(string root)
        {
            ImmutableSortedDictionary<decimal, OrderFlowDiagonalPair> pairs = new Dictionary<decimal, OrderFlowDiagonalPair> {
                [100] = new OrderFlowDiagonalPair(100, 30, 10), [101] = new OrderFlowDiagonalPair(101, 40, 10),
                [103] = new OrderFlowDiagonalPair(103, 50, 10), [104] = new OrderFlowDiagonalPair(104, 10, 40),
                [105] = new OrderFlowDiagonalPair(105, 10, 30) }.ToImmutableSortedDictionary();
            DiagonalMetrics metrics = DiagonalMetrics.Calculate(new OrderFlowImbalanceSnapshot { Pairs = pairs }, 1, new DiagonalSettings());
            AssertEqual(40m, metrics.Delta, "Pair delta sum"); AssertEqual(240m, metrics.ComparableVolume, "Comparable denominator");
            AssertEqual(2, metrics.BuyStack, "Missing comparable pair breaks buy stack"); AssertEqual(2, metrics.SellStack, "Sell stack");
            AssertEqual(3, metrics.PassingBuy, "Passing buy pairs");
            DiagonalMetrics floor = DiagonalMetrics.Calculate(new OrderFlowImbalanceSnapshot { Pairs = pairs }, 1, new DiagonalSettings { MinimumDominantVolume = 40 });
            AssertEqual(1, floor.BuyStack, "Floors from saved pairs");
            OrderFlowImbalanceProfile profile = new OrderFlowImbalanceProfile(1, new OrderFlowImbalanceSettings());
            profile.Change(new OrderFlowDeal { Time = Start, Price = 100, Volume = 5, Side = Side.Sell });
            profile.Change(new OrderFlowDeal { Time = Start, Price = 102, Volume = 50, Side = Side.Buy });
            AssertEqual(0, DiagonalMetrics.Calculate(profile.Snapshot(), 1, new DiagonalSettings()).PassingBuy, "No skipped level/infinite ratio");
        }
        private static void TestCalibrationFilters(string root)
        {
            CalibrationSpec spec = CalSpec(root, Row(Start, 100, 5, Side.Sell), Row(Start.AddMilliseconds(50), 101, 20, Side.Buy));
            CalibrationRun run = CalRun(spec); FormationSpec formation = run.Manifest.Cells[1].Formation;
            CalibrationEvent item = CalEvents(run, formation).Single(); string id = item.EventId; File.Delete(spec.InputPath);
            CloudFilters filters = new CloudFilters { Volume = new NumericFilter(25, 25), TradeCount = new NumericFilter(2, 2),
                Delta = new NumericFilter(15, 15), DeltaPercent = new NumericFilter(60, 60), DiagonalDelta = new NumericFilter(15, 15) };
            AssertEqual(1L, CalibrationEngine.Filter(run, formation, RuleKind.Diagonal, filters, 5, CancellationToken.None).Passed, "AND filters saved evidence");
            AssertEqual(0L, CalibrationEngine.Filter(run, formation, RuleKind.Standard, filters with { Volume = new NumericFilter(26) }, 15, CancellationToken.None).Passed, "Post-volume excludes");
            AssertEqual(id, CalEvents(run, formation).Single().EventId, "No EventId rebuild");
        }
        private static void TestCalibrationRules(string root)
        {
            CalibrationSpec spec = CalSpec(root, Row(Start, 100, 5, Side.Buy)); CalibrationRun run = CalRun(spec);
            CloudRule a = new CloudRule { Provenance = run.Spec, BundlePath = run.Directory, Formation = run.Manifest.Cells[1].Formation };
            CloudRule b = a with { Provenance = run.Spec with { Range = run.Spec.Range with { Id = "overlap", Name = "Overlap" } },
                Filters = a.Filters with { Volume = new NumericFilter(999) } };
            AssertTrue(a.RuleId != b.RuleId && a.Filters.Volume.Minimum == null, "Independent copied rule");
            CalibrationRun overlapping = CalRun(spec with { Range = b.Provenance.Range });
            AssertTrue(CalEvents(run, a.Formation)[0].EventId != CalEvents(overlapping, a.Formation)[0].EventId, "Overlap independent IDs");
            CalibrationStorage.SaveRule(spec.OutputRootPath, a, CancellationToken.None);
            CloudRule saved = CalibrationStorage.LoadRules(spec.OutputRootPath, CancellationToken.None).Single();
            AssertEqual(a.RuleId, saved.RuleId, "Saved rule roundtrip"); AssertEqual(a.RuleId, (a with { Name = "Renamed", Enabled = false, Visible = false }).RuleId, "View identity independent");
            CloudRule edited = a with { Filters = a.Filters with { Volume = new NumericFilter(2) } };
            CalibrationStorage.SaveRule(spec.OutputRootPath, edited, CancellationToken.None, a.RuleId);
            ImmutableArray<CloudRule> updated = CalibrationStorage.LoadRules(spec.OutputRootPath, CancellationToken.None);
            AssertTrue(!updated.Single(r => r.RuleId == a.RuleId).Enabled && updated.Single(r => r.RuleId == edited.RuleId).Enabled, "Semantic edit updates index atomically and preserves old rule");
        }
        private static void TestCalibrationIdentity(string root)
        {
            CalibrationSpec spec = CalSpec(root, Row(Start, 100, 1, Side.Buy)); CalibrationRun first = CalRun(spec), second = CalRun(spec);
            AssertEqual(first.Directory, second.Directory, "Atomic identical reuse");
            AssertEqual(first.Manifest.Hash, CalibrationStorage.Open(first.Directory, CancellationToken.None).Manifest.Hash, "Reopen verifies");
            AssertTrue(first.Spec.Hash != (first.Spec with { PriceStep = 2 }).Hash, "Step identity");
            AssertTrue(first.Spec.Hash != (first.Spec with { MaximumCells = 64 }).Hash, "Resources identity");
            AssertEqual(first.Spec.Hash, (first.Spec with { Range = first.Spec.Range with { Name = "Display name" } }).Hash, "Profile name view only");
            File.AppendAllText(Path.Combine(first.Directory, "ticks.cache"), "bad"); bool rejected = false;
            try { CalibrationStorage.Open(first.Directory, CancellationToken.None); } catch (InvalidDataException) { rejected = true; }
            AssertTrue(rejected, "Corrupt cache rejected");
        }
        private static void TestCalibrationCancel(string root)
        {
            CalibrationSpec spec = CalSpec(root, Enumerable.Range(0, 9000).Select(i => Row(Start.AddTicks(i * 10), 100, 1, Side.Buy)).ToArray());
            using CancellationTokenSource cancel = new CancellationTokenSource(); bool cancelled = false;
            try { CalibrationEngine.Run(spec, cancel.Token, p => cancel.Cancel()); } catch (OperationCanceledException) { cancelled = true; }
            AssertTrue(cancelled && !Directory.EnumerateDirectories(spec.OutputRootPath).Any(), "Cancel removes staging, no successful bundle");
            using (FileStream writable = new FileStream(spec.InputPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { AssertTrue(writable.CanWrite, "Released input"); }
            File.AppendAllText(spec.InputPath, "bad\n"); bool rejected = false;
            try { CalRun(spec with { FromDate = Start.Date.AddDays(-1), ToDate = Start.Date.AddDays(-1) }); } catch (InvalidDataException) { rejected = true; }
            AssertTrue(rejected && !Directory.EnumerateDirectories(spec.OutputRootPath).Any(), "Excluded invalid suffix rejects all");
        }
        private static void TestCalibrationLimits(string root)
        {
            CalibrationSpec spec = CalSpec(root, Enumerable.Range(0, 40).Select(i => Row(Start.AddTicks(i * 10), 100, i + 1, Side.Buy)).ToArray()) with { MaximumBufferItems = 16 };
            bool rejected = false; try { CalRun(spec); } catch (InvalidDataException) { rejected = true; }
            AssertTrue(rejected && !Directory.EnumerateDirectories(spec.OutputRootPath).Any(), "Context buffer budget still rejects without partial publication");
            bool grid = false; try { (spec with { MaximumCells = 1 }).Validate(); } catch (ArgumentException) { grid = true; }
            AssertTrue(grid, "Explicit grid budget");
        }
        private static void TestCalibrationNeighbors(string root)
        {
            CalibrationRun run = CalRun(CalSpec(root, Row(Start, 100, 1, Side.Buy), Row(Start.AddMilliseconds(500), 101, 1, Side.Buy),
                Row(Start.AddMilliseconds(600), 103, 1, Side.Buy)));
            ParameterCell cell = run.Manifest.Cells.First(c => c.Formation.Mode == FormationMode.Chain && c.Formation.MaximumGapMilliseconds == 1000 && c.Formation.MaximumRangeTicks == 3);
            ParameterCell[] adjacent = run.Manifest.Cells.Where(c => c.Formation.Mode == FormationMode.Chain && c != cell &&
                (c.Formation.MaximumGapMilliseconds == 1000 || c.Formation.MaximumRangeTicks == 3)).ToArray();
            decimal expected = adjacent.Max(c => Math.Abs(c.Summary.ChainsPerActiveDay - cell.Summary.ChainsPerActiveDay)) / cell.Summary.ChainsPerActiveDay * 100;
            AssertEqual(expected, cell.NeighborSensitivity, "Frequency sensitivity formula");
        }
        private static void TestCalibrationExactPercent(string root)
        {
            decimal lower = 33.333333333333333333333333333m, upper = 33.333333333333333333333333334m;
            AssertTrue(CalibrationArithmetic.PercentPasses(1, 3, new NumericFilter(lower)), "Exact lower bound");
            AssertTrue(!CalibrationArithmetic.PercentPasses(1, 3, new NumericFilter(upper)), "No rounded upper pass");
            AssertTrue(!CalibrationArithmetic.PercentPasses(1, 3, new NumericFilter(null, lower)), "No rounded maximum pass");
            AssertTrue(CalibrationArithmetic.PercentPasses(-1, 3, new NumericFilter(-upper, -lower)), "Signed exact bounds");
            CalibrationSpec spec = CalSpec(root, Row(Start, 100, 1, Side.Buy));
            AssertEqual(spec.Hash, (spec with { MinimumTickVolume = 1.000m, PriceStep = 1.00m }).Hash, "Decimal representation does not change semantics");
        }
    }
}
