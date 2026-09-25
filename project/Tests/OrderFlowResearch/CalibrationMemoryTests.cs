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
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;

namespace OsEngine.OrderFlowResearch.Tests
{
    internal static partial class Program
    {
        /// <summary>High-cardinality exact values, duplicate weights and histogram boundaries agree across spill capacities and insertion order.</summary>
        private static void TestCalibrationSpilledQuantiles(string root)
        {
            CalibrationSpec spec = new CalibrationSpec { MaximumBufferItems = 128 };
            decimal[] values = Enumerable.Range(0, 60000).Select(i => (i % 45001 - 20000m) / 137).ToArray();
            decimal[] sorted = values.OrderBy(v => v).ToArray();
            decimal[] percentiles = { .5m, .75m, .9m, .95m, .99m, .995m, .999m, 1m };
            DistributionSummary previous = null;
            foreach (int capacity in new[] { 128, 4096 })
            {
                using CalibrationStatisticsWorkspace workspace = new CalibrationStatisticsWorkspace(spec, root, CancellationToken.None);
                using CalibrationDistribution distribution = new CalibrationDistribution(workspace, capacity);
                foreach (decimal value in values.Reverse()) { distribution.Add(value); }
                AssertTrue(workspace.ScratchBytes > 0 && distribution.BufferCapacity == capacity, "Spilled without per-distinct object graph");
                DistributionSummary summary = distribution.Snapshot();
                decimal?[] quantiles = { summary.P50, summary.P75, summary.P90, summary.P95, summary.P99, summary.P995, summary.P999, summary.Maximum };
                for (int i = 0; i < percentiles.Length; i++)
                { AssertEqual(sorted[(int)decimal.Ceiling(values.Length * percentiles[i]) - 1], quantiles[i].Value, "Exact weighted nearest rank"); }
                IGrouping<decimal, decimal>[] groups = sorted.GroupBy(v => v).ToArray();
                int bucket = (groups.Length + 39) / 40;
                DistributionPoint[] histogram = groups.Chunk(bucket).Select(chunk => new DistributionPoint(chunk[0].Key, chunk.Sum(g => (long)g.Count()))).ToArray();
                AssertTrue(histogram.SequenceEqual(summary.Histogram), "Exact original distinct-group histogram");
                AssertEqual(0, distribution.BufferCapacity, "Snapshot releases value buffer");
                AssertEqual(0L, workspace.ScratchBytes, "Snapshot removes every run");
                if (previous != null) { AssertEqual(JsonSerializer.Serialize(previous), JsonSerializer.Serialize(summary), "Reproducible independent of spill capacity"); }
                previous = summary;
            }
        }

        /// <summary>Six high-cardinality cells release collectors/scratch and retain only compact summaries, not prior metric values.</summary>
        private static void TestCalibrationCellMemory(string root)
        {
            List<EventSummary> retained = new List<EventSummary>();
            long minimum = long.MaxValue, maximum = 0;
            for (int cell = 0; cell < 6; cell++)
            {
                WeakReference collector = CalibrationHighCardinalityCell(root, retained);
                long live = GC.GetTotalMemory(true);
                AssertTrue(!collector.IsAlive, "Completed cell collector is unreachable while its summary survives");
                minimum = Math.Min(minimum, live); maximum = Math.Max(maximum, live);
            }
            AssertEqual(6, retained.Count, "All cell summaries retained");
            AssertTrue(maximum - minimum < 4 * 1024 * 1024, "No inter-cell live metric-memory accumulation (4 MiB tolerance)");
            AssertTrue(!Directory.EnumerateFileSystemEntries(root).Any(), "No artifacts retained in test root");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference CalibrationHighCardinalityCell(string root, List<EventSummary> retained)
        {
            CalibrationSpec spec = new CalibrationSpec();
            string scratch;
            WeakReference reference;
            using (CalibrationStatisticsWorkspace workspace = new CalibrationStatisticsWorkspace(spec, root, CancellationToken.None))
            using (EventStatistics statistics = new EventStatistics(workspace, spec.MaximumBufferItems))
            {
                scratch = workspace.DirectoryPath; reference = new WeakReference(statistics);
                for (int i = 40000; i >= 1; i--)
                {
                    OrderFlowImbalanceSnapshot pairs = new OrderFlowImbalanceSnapshot { Pairs =
                        ImmutableSortedDictionary<decimal, OrderFlowDiagonalPair>.Empty.Add(100, new OrderFlowDiagonalPair(100, i + 1, 1)) };
                    ExplorerCloud cloud = new ExplorerCloud { Buy = i + 1, Sell = 1, Count = i + 2, StartTime = Start,
                        Time = Start.AddTicks(i), Low = 100, High = 100 + i, Inside = pairs, Context = pairs };
                    statistics.Add(new CalibrationEvent("test", "all", "formation", cloud, i + 1, i + 1, i / (i + 2m) * 100),
                        1, RuleKind.Standard, new CloudFilters());
                }
                AssertEqual(14 * 4096, statistics.DistributionBufferItems, "All metric buffers have fixed aggregate capacity");
                AssertTrue(workspace.ScratchBytes > 0, "High-cardinality metrics really spilled");
                EventSummary summary = statistics.Snapshot(1);
                AssertEqual(40000L, summary.Passed, "No event sampling");
                AssertEqual(38002m, summary.Distributions["Volume"].P95.Value, "Volume P95");
                AssertEqual(38000m, summary.Distributions["DiagonalDelta"].P95.Value, "Diagonal P95 unchanged");
                AssertEqual(38000m / 38002m * 100, summary.Distributions["DeltaPercent"].P95.Value, "Fraction P95 exact");
                AssertEqual(0, statistics.DistributionBufferItems, "All cell value buffers released at snapshot");
                AssertEqual(0L, workspace.ScratchBytes, "No cell spill files survive snapshot");
                retained.Add(summary);
            }
            AssertTrue(!Directory.Exists(scratch), "Cell-owned scratch directory removed"); return reference;
        }

        private static void TestCalibrationScratchFailure(string root)
        {
            foreach (bool cancel in new[] { false, true })
            {
                string scratch = null; bool rejected = false;
                using CancellationTokenSource cancellation = new CancellationTokenSource();
                try
                {
                    CalibrationSpec spec = new CalibrationSpec { MaximumCacheBytes = cancel ? 1024 * 1024 : 1024 };
                    using CalibrationStatisticsWorkspace workspace = new CalibrationStatisticsWorkspace(spec, root, cancellation.Token);
                    scratch = workspace.DirectoryPath;
                    using CalibrationDistribution distribution = new CalibrationDistribution(workspace, 16);
                    for (int i = 0; i < 100; i++)
                    { if (cancel && i == 40) { cancellation.Cancel(); } distribution.Add(i); }
                    distribution.Snapshot();
                }
                catch (OperationCanceledException) when (cancel) { rejected = true; }
                catch (InvalidDataException) when (!cancel) { rejected = true; }
                AssertTrue(rejected && !Directory.Exists(scratch), "Cancelled/over-budget merge removes all scratch");
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void AllocateCalibrationGarbage() { byte[] garbage = new byte[80 * 1024 * 1024]; GC.KeepAlive(garbage); }

        private static void TestCalibrationManagedGuard(string root)
        {
            CalibrationSpec spec = new CalibrationSpec { MaximumMemoryMegabytes = 64 };
            GC.Collect(); AllocateCalibrationGarbage();
            CalibrationEngine.CheckMemory(spec);
            byte[] live = new byte[80 * 1024 * 1024];
            bool rejected = false;
            try { CalibrationEngine.CheckMemory(spec); } catch (InvalidDataException) { rejected = true; }
            GC.KeepAlive(live);
            AssertTrue(rejected, "Real live managed memory above the unchanged cap is rejected");
        }

        /// <summary>Explicit offline owner-file qualification: full source, FORTS Main, threshold 12 and unchanged default 57-cell grid.</summary>
        /// <remarks>Command: --calibration-input &lt;tick path&gt; &lt;new output root&gt; &lt;manual PriceStep&gt;.
        /// No app, connector or orders; output bundles/log survive for inspection. Caller owns cancellation via process termination.
        /// Managed memory is sampled every 20 ms and at progress; working set uses the OS process high-water mark.
        /// Sampling is observational and does not force collections. This does not qualify physical UI/DPI or live trading.</remarks>
        private static int RunCalibrationOwner(string[] args)
        {
            Stopwatch watch = Stopwatch.StartNew();
            using Process process = Process.GetCurrentProcess();
            long peakManaged = GC.GetTotalMemory(false), baselineManaged = peakManaged;
            object samplingGate = new object();
            Action sample = () => { lock (samplingGate) { peakManaged = Math.Max(peakManaged, GC.GetTotalMemory(false)); } };
            using Timer sampler = new Timer(_ => sample(), null, 0, 20);
            int completed = 0;
            string stage = "Start";
            try
            {
                if (args.Length != 4) { throw new ArgumentException("--calibration-input <tick path> <new output root> <manual PriceStep>"); }
                if (Directory.Exists(args[2])) { throw new ArgumentException("Owner run requires a new output directory."); }
                CalibrationSpec spec = new CalibrationSpec { InputPath = args[1], OutputRootPath = args[2],
                    PriceStep = decimal.Parse(args[3], CultureInfo.InvariantCulture), MinimumTickVolume = 12 };
                Console.WriteLine(JsonSerializer.Serialize(new { Stage = "Configuration", spec.Range, spec.PriceStep, spec.MinimumTickVolume,
                    spec.Gaps, spec.Ranges, spec.MaximumBufferItems, spec.MaximumMemoryMegabytes, spec.MaximumCacheBytes,
                    InputBytes = new FileInfo(spec.InputPath).Length, BaselineManagedBytes = baselineManaged, ProcessId = Environment.ProcessId,
                    ServerGC = System.Runtime.GCSettings.IsServerGC, Environment.ProcessorCount }));
                double last = -10;
                Action<CalibrationProgress> progress = p =>
                {
                    stage = p.Stage; sample();
                    if (p.Stage == "Cell complete") { completed = p.Cell; }
                    if (watch.Elapsed.TotalSeconds - last < 10 && p.Stage != "Cell complete") { return; }
                    last = watch.Elapsed.TotalSeconds; process.Refresh();
                    Console.WriteLine(JsonSerializer.Serialize(new { p.Stage, p.Rows, p.Date, p.Cell, p.Cells,
                        Seconds = watch.Elapsed.TotalSeconds, ManagedBytes = GC.GetTotalMemory(false),
                        PeakManagedBytes = Interlocked.Read(ref peakManaged), WorkingSetBytes = process.WorkingSet64,
                        PeakWorkingSetBytes = process.PeakWorkingSet64, Gen2Collections = GC.CollectionCount(2) }));
                };
                CalibrationRun prepared = CalibrationEngine.Run(spec with { TickDistributionOnly = true, MinimumTickVolume = 1 }, CancellationToken.None, progress);
                TickStatistics ticks = CalibrationEngine.Ticks(prepared.Directory, prepared.Spec, null, 1, CancellationToken.None);
                AssertEqual(12m, ticks.Volumes.P95.Value, "Owner-selected P95 is 12");
                CalibrationWorkspace pinned = new CalibrationWorkspace(ImmutableArray<TimeRangeProfile>.Empty, ImmutableArray<PinnedCandidate>.Empty,
                    ImmutableArray.Create(spec.MinimumTickVolume));
                string workspacePath = Path.Combine(spec.OutputRootPath, "cloud-calibration-workspace.json");
                File.WriteAllText(workspacePath, JsonSerializer.Serialize(pinned));
                AssertEqual(12m, JsonSerializer.Deserialize<CalibrationWorkspace>(File.ReadAllText(workspacePath)).TickThresholds.Single(), "Pinned threshold round-trip");
                Console.WriteLine(JsonSerializer.Serialize(new { Stage = "Prepared", prepared.Directory, prepared.Spec.InputSha256,
                    Rows = prepared.Manifest.Quality.Accepted, P95 = ticks.Volumes.P95, Seconds = watch.Elapsed.TotalSeconds }));
                double gridStart = watch.Elapsed.TotalSeconds;
                CalibrationRun run = CalibrationEngine.Run(spec, CancellationToken.None, progress, prepared);
                double gridSeconds = watch.Elapsed.TotalSeconds - gridStart;
                AssertEqual(57, run.Manifest.Cells.Length, "Full default grid");
                Console.WriteLine(JsonSerializer.Serialize(new { Stage = "Grid complete", Cells = run.Manifest.Cells.Length, GridSeconds = gridSeconds }));
                run = CalibrationStorage.Open(run.Directory, CancellationToken.None);
                ParameterCell cell = run.Manifest.Cells.Last();
                CloudFilters filters = new CloudFilters { TradeCount = new NumericFilter(2) };
                EventSummary tuned = CalibrationEngine.Filter(run, cell.Formation, RuleKind.Standard, filters, 15, CancellationToken.None);
                CloudRule rule = new CloudRule { Name = "Owner offline smoke", Provenance = run.Spec, BundlePath = run.Directory,
                    Formation = cell.Formation, Filters = filters };
                CalibrationStorage.SaveRule(spec.OutputRootPath, rule, CancellationToken.None);
                CloudRule reopened = CalibrationStorage.LoadRules(spec.OutputRootPath, CancellationToken.None).Single();
                AssertEqual(rule.RuleId, reopened.RuleId, "Rule persisted and reopened");
                CalibrationChartData chart = CalibrationPresentation.Load(run, new[] { reopened }, OrderFlowDisplayTimeFrame.Min1, CancellationToken.None);
                AssertEqual(tuned.Passed, chart.Layers.Single().PassedCount, "Tuner and saved chart layer agree");
                CalibrationEvent anatomy = chart.Layers.Single().Markers.Last().Event;
                AssertEqual(anatomy.Volume, CalibrationAnatomy.Ticks(run, cell.Formation, anatomy, false, CancellationToken.None).Sum(t => t.Volume), "Anatomy tick conservation");
                AssertEqual(anatomy.Volume, CalibrationAnatomy.Levels(run, cell.Formation, anatomy, CancellationToken.None).Sum(l => l.BuyVolume + l.SellVolume), "Anatomy level conservation");
                DiagonalSettings diagonal = new DiagonalSettings { Source = DiagonalSource.Context, Enabled = true, MinimumStackLength = 2 };
                EventSummary diagonalFiltered = CalibrationEngine.Filter(run, cell.Formation, RuleKind.Diagonal,
                    filters with { Diagonal = diagonal }, 15, CancellationToken.None);
                AssertTrue(diagonalFiltered.Passed <= tuned.Passed, "Diagonal post-filter remains subset of same formation");
                sample(); process.Refresh();
                Console.WriteLine(JsonSerializer.Serialize(new { Result = "PASS", Completed = completed, Cells = run.Manifest.Cells.Length,
                    run.Directory, run.Spec.InputSha256, Rows = run.Manifest.Quality.Accepted, run.Manifest.ActiveDays,
                    TotalSeconds = watch.Elapsed.TotalSeconds, GridSeconds = gridSeconds, TunerPassed = tuned.Passed,
                    DiagonalPassed = diagonalFiltered.Passed, ChartMarkers = chart.Layers.Single().Markers.Length, Anatomy = "PASS",
                    PeakManagedBytes = Interlocked.Read(ref peakManaged),
                    PeakWorkingSetBytes = process.PeakWorkingSet64, FinalManagedBytes = GC.GetTotalMemory(false) }));
                return 0;
            }
            catch (Exception error)
            {
                sample(); process.Refresh();
                Console.WriteLine(JsonSerializer.Serialize(new { Result = "FAIL", Stage = stage, Completed = completed,
                    Seconds = watch.Elapsed.TotalSeconds, PeakManagedBytes = Interlocked.Read(ref peakManaged),
                    PeakWorkingSetBytes = process.PeakWorkingSet64, Error = error.ToString() }));
                return 1;
            }
        }
    }
}
