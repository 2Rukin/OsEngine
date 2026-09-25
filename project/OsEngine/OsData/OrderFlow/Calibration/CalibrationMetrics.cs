/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.OsData.OrderFlow.Explorer;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;

namespace OsEngine.OsData.OrderFlow.Calibration
{
    internal sealed record DiagonalPairRow(decimal LowerPrice, decimal UpperPrice, decimal SellLower, decimal BuyUpper,
        decimal PairDiagonalDelta, double BuyRatioPercent, double SellRatioPercent, string Direction, int StackId, int StackOrdinal, string TimeRangeId = "");

    /// <summary>Descriptive diagonal sums and strict contiguous stacks over existing comparable pairs only.</summary>
    internal sealed record DiagonalMetrics(decimal Delta, decimal ComparableVolume, int BuyStack, int SellStack,
        int PassingBuy, int PassingSell, double BuyRatio, double SellRatio, decimal MaximumPairDelta)
    {
        public decimal DeltaPercent => ComparableVolume == 0 ? 0 : Delta / ComparableVolume * 100;
        public int StackLength => Math.Max(BuyStack, SellStack);

        internal static DiagonalMetrics Calculate(OrderFlowImbalanceSnapshot snapshot, decimal step, DiagonalSettings settings,
            List<DiagonalPairRow> rows = null, CancellationToken cancellation = default)
        {
            decimal delta = 0, volume = 0, maximum = 0;
            int buyStack = 0, sellStack = 0, buy = 0, sell = 0, run = 0, stackId = 0;
            double buyRatio = 0, sellRatio = 0;
            decimal? previous = null;
            string previousDirection = "";
            foreach (OrderFlowDiagonalPair pair in snapshot.Pairs.Values)
            {
                cancellation.ThrowIfCancellationRequested();
                decimal difference = OrderFlowVolumeComparison.AddExact(pair.Buy, -pair.Sell);
                delta = OrderFlowVolumeComparison.AddExact(delta, difference);
                volume = OrderFlowVolumeComparison.AddExact(volume, OrderFlowVolumeComparison.AddExact(pair.Buy, pair.Sell));
                maximum = Math.Max(maximum, Math.Abs(difference));
                buyRatio = Math.Max(buyRatio, pair.BuyRatioPercent); sellRatio = Math.Max(sellRatio, pair.SellRatioPercent);
                bool passingBuy = Passes(pair.Buy, pair.Sell, settings);
                bool passingSell = Passes(pair.Sell, pair.Buy, settings);
                string direction = passingBuy ? "Buy" : passingSell ? "Sell" : "";
                if (direction.Length == 0) { run = 0; }
                else
                {
                    bool adjacent = previous.HasValue && pair.LowerPrice - previous.Value == step && previousDirection == direction;
                    if (!adjacent) { run = 0; stackId++; }
                    run++;
                    if (passingBuy) { buy++; buyStack = Math.Max(buyStack, run); }
                    else { sell++; sellStack = Math.Max(sellStack, run); }
                }
                rows?.Add(new DiagonalPairRow(pair.LowerPrice, pair.LowerPrice + step, pair.Sell, pair.Buy, difference,
                    pair.BuyRatioPercent, pair.SellRatioPercent, direction, direction.Length == 0 ? 0 : stackId, run));
                previous = pair.LowerPrice; previousDirection = direction;
            }
            return new DiagonalMetrics(delta, volume, buyStack, sellStack, buy, sell, buyRatio, sellRatio, maximum);
        }

        private static bool Passes(decimal dominant, decimal opposite, DiagonalSettings settings) => dominant > opposite &&
            dominant >= settings.MinimumDominantVolume && OrderFlowVolumeComparison.DifferenceAtLeast(dominant, opposite, settings.MinimumDifference) &&
            OrderFlowVolumeComparison.CompareProducts(dominant, 100, opposite, settings.RatioThreshold) >= 0;
    }

    internal sealed record DistributionPoint(decimal Value, long Count);
    internal sealed record DistributionSummary(long Count, decimal? P50, decimal? P75, decimal? P90, decimal? P95,
        decimal? P99, decimal? P995, decimal? P999, decimal? Maximum, ImmutableArray<DistributionPoint> Histogram);


    internal sealed record TickStatistics(long Total, long Passed, int ActiveDays, int CalendarDates,
        DistributionSummary Volumes, DistributionSummary Gaps)
    {
        public ImmutableArray<DateTime> ActiveDates { get; init; } = ImmutableArray<DateTime>.Empty;
        public decimal PassedPercent => Total == 0 ? 0 : 100m * Passed / Total;
        public decimal EventsPerActiveDay => ActiveDays == 0 ? 0 : (decimal)Passed / ActiveDays;
    }
    internal sealed record TimeBucket(DateTime Date, int Minute, long Count, decimal Volume, decimal AbsoluteDelta, decimal AbsoluteDiagonalDelta);
    internal sealed record EventSummary(long Total, long Passed, long Singles, int ActiveDays,
        ImmutableDictionary<string, DistributionSummary> Distributions, ImmutableArray<TimeBucket> TimeMap)
    {
        public ImmutableDictionary<int, long> StackFrequencies { get; init; } = ImmutableDictionary<int, long>.Empty;
        public decimal EventsPerActiveDay => ActiveDays == 0 ? 0 : (decimal)Passed / ActiveDays;
        public decimal ChainsPerActiveDay => ActiveDays == 0 ? 0 : (decimal)(Passed - Singles) / ActiveDays;
        public decimal SinglesPerActiveDay => ActiveDays == 0 ? 0 : (decimal)Singles / ActiveDays;
    }
    internal sealed record ParameterCell(FormationSpec Formation, string FormationHash, string FileName, EventSummary Summary,
        decimal NeighborSensitivity, ImmutableDictionary<string, NumericFilter> NeighborRanges);

    /// <summary>Post-formation AND predicate. Recomputes diagonal thresholds from saved pairs without changing event identity.</summary>
    internal static class CalibrationFilter
    {
        internal static bool Passes(CalibrationEvent item, decimal step, RuleKind kind, CloudFilters filters, out DiagonalMetrics diagonal)
        {
            ExplorerCloud c = item.Evidence;
            diagonal = DiagonalMetrics.Calculate(filters.Diagonal.Source == DiagonalSource.Inside ? c.Inside : c.Context, step, filters.Diagonal);
            if (!filters.Volume.Passes(item.Volume) || !filters.TradeCount.Passes(c.Count) || !filters.Duration.Passes(c.DurationMilliseconds) ||
                !filters.RangeTicks.Passes((c.High - c.Low) / step) || !filters.LargestTick.Passes(item.LargestTick) ||
                !filters.Delta.Passes(item.Delta) || !filters.AbsoluteDelta.Passes(Math.Abs(item.Delta)) ||
                !CalibrationArithmetic.PercentPasses(item.Delta, item.Volume, filters.DeltaPercent) || !CalibrationArithmetic.PercentPasses(item.Delta, item.Volume, filters.AbsoluteDeltaPercent, true) ||
                filters.DeltaDirection == FlowDirection.Buy && item.Delta <= 0 || filters.DeltaDirection == FlowDirection.Sell && item.Delta >= 0 ||
                !filters.DiagonalDelta.Passes(diagonal.Delta) || !filters.AbsoluteDiagonalDelta.Passes(Math.Abs(diagonal.Delta)) ||
                !CalibrationArithmetic.PercentPasses(diagonal.Delta, diagonal.ComparableVolume, filters.DiagonalDeltaPercent)) { return false; }
            if (kind == RuleKind.Standard && !filters.Diagonal.Enabled) { return true; }
            return filters.Diagonal.Direction != FlowDirection.Sell && diagonal.BuyStack >= filters.Diagonal.MinimumStackLength ||
                filters.Diagonal.Direction != FlowDirection.Buy && diagonal.SellStack >= filters.Diagonal.MinimumStackLength;
        }
    }

    /// <summary>Worker-owned summaries with bounded disk-backed exact distributions; Dispose releases all per-cell working state.</summary>
    /// <remarks>Time maps retain bounded source-date/event-start buckets. Only immutable summary cards/maps survive a completed cell.</remarks>
    internal sealed class EventStatistics : IDisposable
    {
        internal static readonly string[] Metrics = { "Volume", "Delta", "AbsoluteDelta", "DeltaPercent", "DiagonalDelta", "AbsoluteDiagonalDelta",
            "DiagonalDeltaPercent", "StackLength", "Duration", "TradeCount", "RangeTicks", "PriceLevels", "TopLevelShare", "LargestTickShare" };
        private readonly Dictionary<string, CalibrationDistribution> _distributions;
        private readonly Dictionary<(DateTime Date, int Minute), TimeBucket> _time = new Dictionary<(DateTime, int), TimeBucket>();
        private readonly int _limit, _bucket;
        private long _total, _passed, _single;
        private readonly Dictionary<int, long> _stacks = new[] { 1, 2, 3, 5, 7, 10, 15 }.ToDictionary(k => k, k => 0L);
        internal int DistributionBufferItems => _distributions.Values.Sum(d => d.BufferCapacity);
        internal EventStatistics(CalibrationStatisticsWorkspace workspace, int limit, int bucket = 15)
        {
            if (bucket != 5 && bucket != 15 && bucket != 30 && bucket != 60) { throw new ArgumentException("Некорректный time bucket."); }
            _limit = limit; _bucket = bucket;
            _distributions = Metrics.ToDictionary(m => m, m => new CalibrationDistribution(workspace, limit / Metrics.Length));
        }
        internal void Add(CalibrationEvent item, decimal step, RuleKind kind, CloudFilters filters)
        {
            _total++;
            if (!CalibrationFilter.Passes(item, step, kind, filters, out DiagonalMetrics diagonal)) { return; }
            _passed++; if (item.Evidence.Count == 1) { _single++; }
            ReadOnlySpan<int> stackLengths = stackalloc int[] { 1, 2, 3, 5, 7, 10, 15 };
            foreach (int length in stackLengths) { if (diagonal.StackLength >= length) { _stacks[length]++; } }
            ReadOnlySpan<decimal> values = stackalloc decimal[] { item.Volume, item.Delta, Math.Abs(item.Delta), item.DeltaPercent, diagonal.Delta, Math.Abs(diagonal.Delta),
                diagonal.DeltaPercent, diagonal.StackLength, item.Evidence.DurationMilliseconds, item.Evidence.Count,
                (item.Evidence.High - item.Evidence.Low) / step, item.PriceLevels, item.TopLevelShare, item.LargestTick / item.Volume * 100 };
            for (int i = 0; i < Metrics.Length; i++) { _distributions[Metrics[i]].Add(values[i]); }
            DateTime date = item.Evidence.StartTime.Date;
            int minute = (int)item.Evidence.StartTime.TimeOfDay.TotalMinutes / _bucket * _bucket;
            (DateTime, int) key = (date, minute);
            if (!_time.TryGetValue(key, out TimeBucket previous))
            {
                if (_time.Count >= _limit) { throw new InvalidDataException("Превышен лимит ячеек time map."); }
                previous = new TimeBucket(date, minute, 0, 0, 0, 0);
            }
            _time[key] = previous with { Count = previous.Count + 1,
                Volume = OrderFlowVolumeComparison.AddExact(previous.Volume, item.Volume),
                AbsoluteDelta = OrderFlowVolumeComparison.AddExact(previous.AbsoluteDelta, Math.Abs(item.Delta)),
                AbsoluteDiagonalDelta = OrderFlowVolumeComparison.AddExact(previous.AbsoluteDiagonalDelta, Math.Abs(diagonal.Delta)) };
        }
        internal EventSummary Snapshot(int activeDays) => new EventSummary(_total, _passed, _single, activeDays,
            _distributions.ToImmutableDictionary(p => p.Key, p => p.Value.Snapshot()),
            _time.Values.OrderBy(v => v.Date).ThenBy(v => v.Minute).ToImmutableArray()) { StackFrequencies = _stacks.ToImmutableDictionary() };

        public void Dispose()
        {
            try { foreach (CalibrationDistribution distribution in _distributions.Values) { distribution.Dispose(); } }
            finally { _distributions.Clear(); _time.Clear(); _stacks.Clear(); }
        }

        internal static ImmutableArray<ParameterCell> Neighbors(IReadOnlyList<ParameterCell> cells, IReadOnlyList<int> gaps, IReadOnlyList<int> ranges)
        {
            int[] orderedGaps = gaps.OrderBy(v => v).ToArray(), orderedRanges = ranges.OrderBy(v => v).ToArray();
            List<ParameterCell> result = new List<ParameterCell>();
            foreach (ParameterCell cell in cells)
            {
                if (cell.Formation.Mode == FormationMode.Single) { result.Add(cell); continue; }
                int g = Array.IndexOf(orderedGaps, cell.Formation.MaximumGapMilliseconds), r = Array.IndexOf(orderedRanges, cell.Formation.MaximumRangeTicks);
                ParameterCell[] adjacent = cells.Where(c => c.Formation.Mode == FormationMode.Chain &&
                    (c.Formation.MaximumGapMilliseconds == cell.Formation.MaximumGapMilliseconds && Math.Abs(Array.IndexOf(orderedRanges, c.Formation.MaximumRangeTicks) - r) == 1 ||
                    c.Formation.MaximumRangeTicks == cell.Formation.MaximumRangeTicks && Math.Abs(Array.IndexOf(orderedGaps, c.Formation.MaximumGapMilliseconds) - g) == 1)).ToArray();
                decimal sensitivity = adjacent.Length == 0 ? 0 : adjacent.Max(c => Math.Abs(c.Summary.ChainsPerActiveDay - cell.Summary.ChainsPerActiveDay)) /
                    Math.Max(cell.Summary.ChainsPerActiveDay, .000000001m) * 100;
                ImmutableDictionary<string, NumericFilter>.Builder bounds = ImmutableDictionary.CreateBuilder<string, NumericFilter>();
                foreach (string key in new[] { "Volume", "AbsoluteDelta", "Duration", "TradeCount" })
                {
                    decimal[] values = adjacent.Select(c => c.Summary.Distributions[key].P95).Where(v => v.HasValue).Select(v => v.Value).ToArray();
                    bounds[key] = values.Length == 0 ? new NumericFilter() : new NumericFilter(values.Min(), values.Max());
                }
                result.Add(cell with { NeighborSensitivity = sensitivity, NeighborRanges = bounds.ToImmutable() });
            }
            return result.ToImmutableArray();
        }
    }
}
