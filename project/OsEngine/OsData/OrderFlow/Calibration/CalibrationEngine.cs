/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.Entity;
using OsEngine.OsData.OrderFlow.Explorer;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace OsEngine.OsData.OrderFlow.Calibration
{
    internal sealed record CalibrationProgress(string Stage, long Rows, DateTime? Date, int Cell, int Cells);

    /// <summary>Single-worker offline transaction: prepare raw statistics alone or form a bounded grid from a compact cache, then publish all-or-nothing.</summary>
    /// <remarks>Fresh preparation validates every source row, including excluded dates. A grid may reuse an explicitly supplied verified snapshot without reopening raw text. Caller owns cancellation/worker; no UI or trading state is touched.</remarks>
    internal static class CalibrationEngine
    {
        #region Run transaction

        internal static CalibrationRun Run(CalibrationSpec request, CancellationToken cancellation, Action<CalibrationProgress> progress = null, CalibrationRun prepared = null)
        {
            request.Validate();
            string staging = ExplorerStorage.Stage(request.OutputRootPath, "cloud-calibration");
            try
            {
                CalibrationQuality quality;
                if (prepared != null)
                {
                    if (request.TickDistributionOnly || !CanReuse(prepared, request)) { throw new InvalidDataException("Подготовленный cache не соответствует input/dates/PriceStep."); }
                    prepared = CalibrationStorage.Open(prepared.Directory, cancellation);
                    if (request.InputSha256 != null && request.InputSha256 != prepared.Spec.InputSha256) { throw new InvalidDataException("Input SHA-256 подготовленного cache отличается."); }
                    if (prepared.Manifest.Quality.SourceDates > request.MaximumBufferItems)
                    { throw new InvalidDataException("Превышен лимит source dates подготовленного cache."); }
                    foreach (string file in new[] { "ticks.cache", "bars.bin", "bars.idx" })
                    {
                        using FileStream source = File.OpenRead(Path.Combine(prepared.Directory, file));
                        using FileStream destination = File.Create(Path.Combine(staging, file));
                        byte[] buffer = new byte[81920]; int read;
                        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                        { cancellation.ThrowIfCancellationRequested(); destination.Write(buffer, 0, read); CheckDisk(staging, request); }
                    }
                    quality = prepared.Manifest.Quality;
                    progress?.Invoke(new CalibrationProgress("Prepared snapshot", quality.Accepted, quality.Last?.Date, 0, 0));
                }
                else
                {
                    OrderFlowTickInput input = new OrderFlowTickInput();
                    long count = 0, duplicates = 0, zero = 0, buy = 0, sell = 0;
                    DateTime? first = null, last = null;
                    HashSet<DateTime> dates = new HashSet<DateTime>();
                    using (OrderFlowTickReader reader = new OrderFlowTickReader(request.InputPath, input, cancellation))
                    using (BinaryWriter cache = new BinaryWriter(File.Create(Path.Combine(staging, "ticks.cache")), Encoding.UTF8))
                    using (ExplorerStorage.RowWriter<ExplorerBar> barsWriter = new ExplorerStorage.RowWriter<ExplorerBar>(staging, "bars"))
                    {
                        ExplorerBars bars = new ExplorerBars(barsWriter.Add);
                        while (reader.TryRead(out OrderFlowDeal tick))
                        {
                            if (!request.IncludesDate(tick.Time)) { continue; }
                            if (last == tick.Time) { duplicates++; } first ??= tick.Time; last = tick.Time;
                            count++; if (tick.Time.Ticks % TimeSpan.TicksPerSecond == 0) { zero++; }
                            if (tick.Side == Side.Buy) { buy++; } else { sell++; }
                            dates.Add(tick.Time.Date);
                            if (dates.Count > request.MaximumBufferItems || cache.BaseStream.Position + CalibrationCache.RecordBytes > request.MaximumCacheBytes)
                            { throw new InvalidDataException("Превышен лимит compact cache или source dates."); }
                            CalibrationCache.Write(cache, tick);
                            bars.Add(tick);
                            if (count % 8192 == 0) { CheckMemory(request); progress?.Invoke(new CalibrationProgress("Parsing", count, tick.Time.Date, 0, request.Gaps.Length * request.Ranges.Length + 1)); }
                        }
                        bars.Complete();
                    }
                    if (count == 0) { throw new InvalidDataException("Нет сделок выбранного периода."); }
                    if (request.InputSha256 != null && request.InputSha256 != input.Sha256) { throw new InvalidDataException("Input SHA-256 изменился."); }
                    quality = new CalibrationQuality(input.Sha256, count, first, last, dates.Count, duplicates, zero, buy, sell);
                }
                CalibrationSpec spec = request with { InputSha256 = quality.InputSha256, FromDate = request.FromDate?.Date, ToDate = request.ToDate?.Date };
                CheckDisk(staging, spec); CheckMemory(spec);
                TickStatistics raw = Ticks(staging, spec, null, spec.MinimumTickVolume, cancellation);
                List<FormationSpec> formations = new List<FormationSpec>();
                if (!spec.TickDistributionOnly)
                {
                    formations.Add(new FormationSpec(FormationMode.Single, spec.MinimumTickVolume, 0, 0, spec.ContextSeconds));
                    foreach (int gap in spec.Gaps) { foreach (int range in spec.Ranges) { formations.Add(new FormationSpec(FormationMode.Chain, spec.MinimumTickVolume, gap, range, spec.ContextSeconds)); } }
                }
                List<ParameterCell> cells = new List<ParameterCell>();
                for (int i = 0; i < formations.Count; i++)
                {
                    cancellation.ThrowIfCancellationRequested(); CheckMemory(spec);
                    cells.Add(FormCell(staging, spec, formations[i], raw.ActiveDays, i + 1, formations.Count, cancellation, progress));
                    CheckDisk(staging, spec); CheckMemory(spec);
                    progress?.Invoke(new CalibrationProgress("Cell complete", quality.Accepted, quality.Last?.Date, i + 1, formations.Count));
                }
                CalibrationManifest manifest = new CalibrationManifest(CalibrationSpec.Version, CalibrationSpec.Formulas, spec.Hash,
                    spec with { InputPath = "", OutputRootPath = "" }, quality, raw.ActiveDays,
                    EventStatistics.Neighbors(cells, spec.Gaps, spec.Ranges), null) { RangeDates = raw.ActiveDates };
                CalibrationRun result = CalibrationStorage.Publish(staging, spec.OutputRootPath, manifest, cancellation);
                staging = null; return result with { SourcePath = Path.GetFullPath(request.InputPath) };
            }
            finally { if (staging != null && Directory.Exists(staging)) { Directory.Delete(staging, true); } }
        }

        internal static bool CanReuse(CalibrationRun run, CalibrationSpec spec) => run?.SourcePath != null &&
            string.Equals(run.SourcePath, Path.GetFullPath(spec.InputPath), StringComparison.OrdinalIgnoreCase) &&
            run.Spec.FromDate == spec.FromDate?.Date && run.Spec.ToDate == spec.ToDate?.Date && run.Spec.PriceStep == spec.PriceStep;

        /// <summary>Enforces the process-wide managed-memory cap after reclaiming collectible objects under pressure.</summary>
        /// <remarks>This is a periodic managed-heap guard, not an OS working-set cap. Other live application objects count too.
        /// A blocking collection starts at 75% pressure, with 6.25% new-allocation headroom above the previous blocking live heap
        /// to avoid repeatedly collecting an unchanged large application baseline. Live usage above the cap still fails.</remarks>
        internal static void CheckMemory(CalibrationSpec spec)
        {
            long limit = spec.MaximumMemoryMegabytes * 1024L * 1024;
            // A process-wide reading includes collectible serialization/previous-cell objects. Reclaim under pressure
            // before rejecting, while leaving headroom between periodic checks. Live objects still enforce the same cap.
            if (GC.GetTotalMemory(false) >= limit * 3 / 4)
            {
                GCMemoryInfo last = GC.GetGCMemoryInfo(GCKind.FullBlocking);
                long collectAt = Math.Min(limit, Math.Max(limit * 3 / 4, last.HeapSizeBytes - last.FragmentedBytes + limit / 16));
                if (GC.GetTotalMemory(false) >= collectAt) { GC.Collect(2, GCCollectionMode.Forced, true, false); }
            }
            if (GC.GetTotalMemory(false) > limit)
            { throw new InvalidDataException("Превышен явный лимит managed memory calibration."); }
        }
        private static void CheckDisk(string directory, CalibrationSpec spec)
        {
            if (Directory.EnumerateFiles(directory).Sum(p => new FileInfo(p).Length) > spec.MaximumCacheBytes)
            { throw new InvalidDataException("Превышен явный лимит дисковых artifacts calibration."); }
        }

        #endregion

        /// <summary>Lifetime boundary: only immutable summaries escape; scratch, writers and distribution buffers close before the next cell.</summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static ParameterCell FormCell(string staging, CalibrationSpec spec, FormationSpec formation, int activeDays,
            int cellNumber, int cells, CancellationToken cancellation, Action<CalibrationProgress> progress)
        {
            string hash = spec.FormationHash(formation), name = "events-" + hash;
            using CalibrationStatisticsWorkspace workspace = new CalibrationStatisticsWorkspace(spec, staging, cancellation);
            using EventStatistics statistics = new EventStatistics(workspace, spec.MaximumBufferItems);
            CloudFilters filters = new CloudFilters { Diagonal = spec.Diagonal with { Enabled = false } };
            using (ExplorerStorage.RowWriter<CalibrationEvent> writer = new ExplorerStorage.RowWriter<CalibrationEvent>(staging, name))
            {
                RangeCatalog catalog = new RangeCatalog(spec, formation, item => { writer.Add(item); statistics.Add(item, spec.PriceStep, RuleKind.Standard, filters); });
                long processed = 0;
                foreach (OrderFlowDeal tick in CalibrationCache.Read(staging, cancellation))
                {
                    catalog.Add(tick, cancellation); processed++;
                    if (processed % 8192 == 0)
                    {
                        workspace.Check();
                        progress?.Invoke(new CalibrationProgress("Formation", processed, tick.Time.Date, cellNumber, cells));
                    }
                }
                catalog.Complete();
            }
            return new ParameterCell(formation, hash, name, statistics.Snapshot(activeDays), 0, ImmutableDictionary<string, NumericFilter>.Empty);
        }

        #region Saved-data statistics

        internal static TickStatistics Ticks(string directory, CalibrationSpec spec, Side? side, decimal threshold, CancellationToken cancellation)
        {
            using CalibrationStatisticsWorkspace workspace = new CalibrationStatisticsWorkspace(spec, directory, cancellation);
            using CalibrationDistribution volumes = new CalibrationDistribution(workspace, spec.MaximumBufferItems / 2);
            using CalibrationDistribution gaps = new CalibrationDistribution(workspace, spec.MaximumBufferItems / 2);
            HashSet<DateTime> active = new HashSet<DateTime>();
            DateTime? previous = null, first = null, last = null;
            long total = 0, passed = 0;
            foreach (OrderFlowDeal tick in CalibrationCache.Read(directory, cancellation))
            {
                first ??= tick.Time.Date; last = tick.Time.Date;
                if (!spec.Range.Includes(tick.Time)) { continue; }
                active.Add(tick.Time.Date);
                if (active.Count > spec.MaximumBufferItems) { throw new InvalidDataException("Превышен лимит source dates статистики."); }
                if (side.HasValue && tick.Side != side) { continue; }
                total++; volumes.Add(tick.Volume); if (total % 8192 == 0) { workspace.Check(); }
                if (tick.Volume < threshold) { continue; }
                passed++;
                if (previous.HasValue && previous.Value.Date == tick.Time.Date) { gaps.Add((tick.Time.Ticks - previous.Value.Ticks) / (decimal)TimeSpan.TicksPerMillisecond); }
                previous = tick.Time;
            }
            int calendar = 0;
            DateTime? from = spec.FromDate ?? first, to = spec.ToDate ?? last;
            if (from.HasValue && to.HasValue)
            {
                for (DateTime date = from.Value.Date; ; date = date.AddDays(1))
                {
                    cancellation.ThrowIfCancellationRequested();
                    if ((spec.Range.DayMask & (1 << (int)date.DayOfWeek)) != 0) { calendar++; }
                    if (date >= to.Value.Date) { break; }
                }
            }
            return new TickStatistics(total, passed, active.Count, calendar, volumes.Snapshot(), gaps.Snapshot()) { ActiveDates = active.OrderBy(d => d).ToImmutableArray() };
        }

        internal static EventSummary Filter(CalibrationRun run, FormationSpec formation, RuleKind kind, CloudFilters filters,
            int bucket, CancellationToken cancellation)
        {
            filters.Validate();
            using CalibrationStatisticsWorkspace workspace = new CalibrationStatisticsWorkspace(run.Spec, run.Directory, cancellation);
            using EventStatistics statistics = new EventStatistics(workspace, run.Spec.MaximumBufferItems, bucket);
            long count = 0;
            foreach (CalibrationEvent item in CalibrationStorage.Events(run, formation, cancellation))
            { statistics.Add(item, run.Spec.PriceStep, kind, filters); if (++count % 8192 == 0) { workspace.Check(); } }
            return statistics.Snapshot(run.Manifest.ActiveDays);
        }

        #endregion

        /// <summary>Range/date adapter around the unchanged ExplorerCatalog. Only completion metadata is specialized at a range cutoff.</summary>
        internal sealed class RangeCatalog
        {
            private readonly CalibrationSpec _spec;
            private readonly FormationSpec _formation;
            private readonly Action<CalibrationEvent> _sink;
            private readonly string _hash;
            private ExplorerCatalog _catalog;
            private DateTime _date;
            private OrderFlowDeal _tick, _cutoff;
            private decimal _largest;
            private readonly Dictionary<decimal, decimal> _levels = new Dictionary<decimal, decimal>();

            internal RangeCatalog(CalibrationSpec spec, FormationSpec formation, Action<CalibrationEvent> sink)
            { _spec = spec; _formation = formation; _sink = sink; _hash = spec.FormationHash(formation); }

            internal void Add(OrderFlowDeal tick, CancellationToken cancellation)
            {
                bool included = _spec.Range.Includes(tick.Time);
                if (_catalog != null && (!included || tick.Time.Date != _date))
                { _cutoff = tick; _catalog.Complete(); _catalog = null; _cutoff = null; }
                if (!included) { return; }
                if (_catalog == null)
                {
                    _date = tick.Time.Date;
                    ExplorerProfile profile = new ExplorerProfile { SingleTicks = _formation.Mode == FormationMode.Single,
                        MinimumTickVolume = _formation.MinimumTickVolume, MaximumGapMilliseconds = _formation.MaximumGapMilliseconds,
                        MaximumRangeTicks = _formation.MaximumRangeTicks, ContextSeconds = _formation.ContextSeconds };
                    ExplorerRunSpec spec = new ExplorerRunSpec { InputSha256 = _spec.InputSha256, PriceStep = _spec.PriceStep,
                        Profiles = ImmutableArray.Create(profile), MaximumBufferItems = _spec.MaximumBufferItems,
                        Study = new ExplorerStudySpec { Enabled = false, SwingsEnabled = false } };
                    _catalog = new ExplorerCatalog(spec, Completed, Prefix);
                }
                _tick = tick; _catalog.Add(tick, cancellation);
            }
            internal void Complete() { _catalog?.Complete(); _catalog = null; }

            private void Prefix(ExplorerPrefix prefix)
            {
                if (prefix.Completed) { return; }
                if (prefix.Count == 1) { _largest = 0; _levels.Clear(); }
                _largest = Math.Max(_largest, _tick.Volume);
                _levels.TryGetValue(_tick.Price, out decimal volume);
                if (!_levels.ContainsKey(_tick.Price) && _levels.Count >= _spec.MaximumBufferItems)
                { throw new InvalidDataException("Превышен лимит уровней event anatomy."); }
                _levels[_tick.Price] = OrderFlowVolumeComparison.AddExact(volume, _tick.Volume);
            }
            private void Completed(ExplorerCloud cloud)
            {
                if ((long)cloud.Inside.Pairs.Count + cloud.Context.Pairs.Count > 100000)
                { throw new InvalidDataException("Превышен лимит 100000 сохранённых diagonal pairs на событие; уменьшите окно context или диапазон."); }
                if (_cutoff != null)
                { cloud = cloud with { KnownAt = _cutoff.Time, KnownSequence = _cutoff.SourceSequence, Reason = _cutoff.Time.Date == _date ? "TimeRangeCutoff" : "DateCutoff" }; }
                string id = _hash + "/" + cloud.FirstSequence;
                _sink(new CalibrationEvent(id, _spec.Range.Id, _hash, cloud, _largest, _levels.Count,
                    _levels.Count == 0 ? 0 : _levels.Values.Max() / cloud.Volume * 100));
            }
        }
    }
}
