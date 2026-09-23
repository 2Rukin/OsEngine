/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.Entity;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Threading;

namespace OsEngine.OsData.OrderFlow
{
    internal enum OrderFlowImbalanceSource { Off, Inside, Context, Both }
    internal enum OrderFlowImbalanceDirection { Any, Buy, Sell }

    /// <summary>Run-owned per-layer context and post-formation filter settings; manual PriceStep is supplied separately.</summary>
    internal sealed class OrderFlowImbalanceSettings
    {
        public int ContextSeconds { get; set; } = 30;
        public OrderFlowImbalanceSource Source { get; set; }
        public OrderFlowImbalanceDirection Direction { get; set; }
        public decimal MinimumRatioPercent { get; set; } = 300;
        public decimal MinimumDominantVolume { get; set; }
        public decimal MinimumDifference { get; set; }
        public decimal MinimumDeltaPercent { get; set; }

        /// <summary>Validates the always-calculated window; disabled filters canonicalize inactive thresholds without reading their values.</summary>
        public void Validate()
        {
            if (ContextSeconds <= 0 || !Enum.IsDefined(Source)) { throw new ArgumentException("Invalid Cloud imbalance window or mode."); }
            if (Source == OrderFlowImbalanceSource.Off)
            {
                Direction = OrderFlowImbalanceDirection.Any;
                MinimumRatioPercent = 300;
                MinimumDominantVolume = MinimumDifference = MinimumDeltaPercent = 0;
                return;
            }
            if (!Enum.IsDefined(Direction) || MinimumRatioPercent < 100 || MinimumDominantVolume < 0 ||
                MinimumDifference < 0 || MinimumDeltaPercent < 0 || MinimumDeltaPercent > 100)
            { throw new ArgumentException("Cloud imbalance requires ratio >= 100%, non-negative volume/difference and delta 0..100%."); }
        }

        /// <summary>Invariant effective settings for the immutable research specification, after validation.</summary>
        public string CanonicalValue()
        {
            return string.Join("|", ContextSeconds, Source, Direction,
                MinimumRatioPercent.ToString("G29", CultureInfo.InvariantCulture),
                MinimumDominantVolume.ToString("G29", CultureInfo.InvariantCulture),
                MinimumDifference.ToString("G29", CultureInfo.InvariantCulture),
                MinimumDeltaPercent.ToString("G29", CultureInfo.InvariantCulture));
        }

        /// <summary>Both requires the same direction to pass both profiles. Missing/zero opposites never pass diagonal filtering.</summary>
        public bool Passes(OrderFlowImbalanceSnapshot inside, OrderFlowImbalanceSnapshot context)
        {
            if (Source == OrderFlowImbalanceSource.Off) { return true; }
            return (Direction != OrderFlowImbalanceDirection.Sell && PassesSide(inside, context, true)) ||
                (Direction != OrderFlowImbalanceDirection.Buy && PassesSide(inside, context, false));
        }

        private bool PassesSide(OrderFlowImbalanceSnapshot inside, OrderFlowImbalanceSnapshot context, bool buy)
        {
            return (Source == OrderFlowImbalanceSource.Context || PassesProfile(inside, buy)) &&
                (Source == OrderFlowImbalanceSource.Inside || PassesProfile(context, buy));
        }

        private bool PassesProfile(OrderFlowImbalanceSnapshot snapshot, bool buy)
        {
            OrderFlowDiagonalPair pair = buy ? snapshot?.EligibleBuy : snapshot?.EligibleSell;
            if (pair == null) { return false; }
            decimal dominant = buy ? pair.Buy : pair.Sell;
            decimal opposite = buy ? pair.Sell : pair.Buy;
            if (OrderFlowVolumeComparison.CompareProducts(dominant, 100, opposite, MinimumRatioPercent) < 0) { return false; }
            if (MinimumDeltaPercent == 0) { return true; }
            return OrderFlowVolumeComparison.DeltaAtLeast(buy ? snapshot.BuyVolume : snapshot.SellVolume,
                buy ? snapshot.SellVolume : snapshot.BuyVolume, MinimumDeltaPercent);
        }
    }

    /// <summary>Immutable diagonal witness: Sell at LowerPrice versus Buy exactly one manual step above it.</summary>
    internal sealed record OrderFlowDiagonalPair(decimal LowerPrice, decimal Buy, decimal Sell)
    {
        public double BuyRatioPercent => (double)Buy / (double)Sell * 100;
        public double SellRatioPercent => (double)Sell / (double)Buy * 100;
    }

    /// <summary>Immutable profile evidence at a consumed physical tick; ratios are display doubles, filtering uses exact decimal products.</summary>
    /// <remarks>Best pairs ignore volume floors; Eligible pairs meet configured volume/difference floors. No zero-denominator pair exists.</remarks>
    internal sealed class OrderFlowImbalanceSnapshot
    {
        public decimal BuyVolume { get; init; }
        public decimal SellVolume { get; init; }
        public decimal Volume => BuyVolume + SellVolume;
        public decimal DeltaPercent => Volume == 0 ? 0 : (BuyVolume - SellVolume) / Volume * 100;
        public int ComparablePairs { get; init; }
        /// <summary>All nonzero exact-neighbor pairs at this anchor, in price order. Persistent roots share immutable nodes across snapshots.</summary>
        public ImmutableSortedDictionary<decimal, OrderFlowDiagonalPair> Pairs { get; init; } = ImmutableSortedDictionary<decimal, OrderFlowDiagonalPair>.Empty;

        /// <summary>Re-evaluates volume/difference floors from saved evidence without reading ticks or changing the original snapshot.</summary>
        internal OrderFlowImbalanceSnapshot WithFloors(OrderFlowImbalanceSettings settings)
        {
            if (settings.MinimumDominantVolume == 0 && settings.MinimumDifference == 0)
            { return new OrderFlowImbalanceSnapshot { BuyVolume = BuyVolume, SellVolume = SellVolume, ComparablePairs = ComparablePairs,
                Pairs = Pairs, BestBuy = BestBuy, BestSell = BestSell, EligibleBuy = BestBuy, EligibleSell = BestSell }; }
            OrderFlowDiagonalPair buy = null; OrderFlowDiagonalPair sell = null;
            foreach (OrderFlowDiagonalPair pair in Pairs.Values)
            {
                if (pair.Buy == pair.Sell || Math.Max(pair.Buy, pair.Sell) < settings.MinimumDominantVolume ||
                    !OrderFlowVolumeComparison.DifferenceAtLeast(Math.Max(pair.Buy, pair.Sell), Math.Min(pair.Buy, pair.Sell), settings.MinimumDifference)) { continue; }
                if (pair.Buy > pair.Sell) { buy = BetterPair(buy, pair, true); }
                else { sell = BetterPair(sell, pair, false); }
            }
            return new OrderFlowImbalanceSnapshot { BuyVolume = BuyVolume, SellVolume = SellVolume, ComparablePairs = ComparablePairs,
                Pairs = Pairs, BestBuy = BestBuy, BestSell = BestSell, EligibleBuy = buy, EligibleSell = sell };
        }

        private static OrderFlowDiagonalPair BetterPair(OrderFlowDiagonalPair current, OrderFlowDiagonalPair candidate, bool buy)
        {
            if (current == null) { return candidate; }
            int comparison = buy ? OrderFlowVolumeComparison.CompareProducts(candidate.Buy, current.Sell, current.Buy, candidate.Sell)
                : OrderFlowVolumeComparison.CompareProducts(candidate.Sell, current.Buy, current.Sell, candidate.Buy);
            return comparison > 0 || (comparison == 0 && candidate.LowerPrice < current.LowerPrice) ? candidate : current;
        }
        public OrderFlowDiagonalPair BestBuy { get; init; }
        public OrderFlowDiagonalPair BestSell { get; init; }
        public OrderFlowDiagonalPair EligibleBuy { get; init; }
        public OrderFlowDiagonalPair EligibleSell { get; init; }
        public double? BuyRatioPercent => BestBuy?.BuyRatioPercent;
        public double? SellRatioPercent => BestSell?.SellRatioPercent;
    }

    /// <summary>Exact volume comparisons and reversible decimal aggregation; scaled integers prevent rounded thresholds and detect precision loss.</summary>
    internal static class OrderFlowVolumeComparison
    {
        #region Exact arithmetic

        public static int CompareProducts(decimal a, decimal b, decimal c, decimal d)
        {
            if (a == decimal.Truncate(a) && b == decimal.Truncate(b) && c == decimal.Truncate(c) && d == decimal.Truncate(d))
            {
                try { return (a * b).CompareTo(c * d); }
                catch (OverflowException) { /* Fall through to exact wide integer products. */ }
            }
            {
                (BigInteger ai, int scaleA) = Parts(a); (BigInteger bi, int scaleB) = Parts(b);
                (BigInteger ci, int scaleC) = Parts(c); (BigInteger di, int scaleD) = Parts(d);
                return (ai * bi * BigInteger.Pow(10, scaleC + scaleD)).CompareTo(ci * di * BigInteger.Pow(10, scaleA + scaleB));
            }
        }

        /// <summary>Adds or removes volume exactly; an unrepresentable decimal sum rejects the run instead of losing a fractional tick.</summary>
        /// <exception cref="InvalidDataException">The exact sum needs more decimal precision than the aggregate format supports.</exception>
        /// <exception cref="OverflowException">The sum exceeds the decimal range.</exception>
        public static decimal AddExact(decimal a, decimal b)
        {
            decimal sum = a + b;
            if ((a != decimal.Truncate(a) || b != decimal.Truncate(b)) && Units(sum) != Units(a) + Units(b))
            { throw new InvalidDataException("Cloud volume sum cannot be represented exactly as decimal."); }
            return sum;
        }

        /// <summary>Compares a dominant-side difference with its floor without rounding the difference first.</summary>
        public static bool DifferenceAtLeast(decimal dominant, decimal opposite, decimal minimum)
        {
            if (dominant < opposite) { return false; }
            if (dominant == decimal.Truncate(dominant) && opposite == decimal.Truncate(opposite))
            { return dominant - opposite >= minimum; }
            return Units(dominant) - Units(opposite) >= Units(minimum);
        }

        /// <summary>Compares directional (dominant-opposite)/(dominant+opposite)*100 with a threshold using exact scaled integers.</summary>
        public static bool DeltaAtLeast(decimal dominant, decimal opposite, decimal minimumPercent)
        {
            if (dominant < opposite) { return false; }
            BigInteger dominantUnits = Units(dominant); BigInteger oppositeUnits = Units(opposite);
            return (dominantUnits - oppositeUnits) * 100 * DecimalUnit >=
                (dominantUnits + oppositeUnits) * Units(minimumPercent);
        }

        #endregion

        #region Decimal representation

        private static readonly BigInteger DecimalUnit = BigInteger.Pow(10, 28);

        private static BigInteger Units(decimal value)
        {
            (BigInteger coefficient, int scale) = Parts(value);
            BigInteger units = coefficient * BigInteger.Pow(10, 28 - scale);
            return value < 0 ? -units : units;
        }

        private static (BigInteger Value, int Scale) Parts(decimal value)
        {
            int[] bits = decimal.GetBits(value);
            BigInteger coefficient = (uint)bits[0] + ((BigInteger)(uint)bits[1] << 32) + ((BigInteger)(uint)bits[2] << 64);
            return (coefficient, (bits[3] >> 16) & 0xff);
        }

        #endregion
    }

    /// <summary>Single-consumer price profile with incremental diagonal indexes. Each changed level updates at most two exact-step pairs.</summary>
    /// <remarks>
    /// O(log P) index updates; snapshots share immutable witnesses. Missing intermediate prices are never skipped or rounded.
    /// Decimal total overflow or precision loss propagates to the engine's rejected-input audit. Contract: ORDER-FLOW-DATA-001.
    /// </remarks>
    internal sealed class OrderFlowImbalanceProfile
    {
        #region State and updates

        private readonly decimal _step;
        private readonly OrderFlowImbalanceSettings _settings;
        private readonly Dictionary<decimal, (decimal Buy, decimal Sell)> _levels = new Dictionary<decimal, (decimal, decimal)>();
        private ImmutableSortedDictionary<decimal, OrderFlowDiagonalPair> _pairs = ImmutableSortedDictionary<decimal, OrderFlowDiagonalPair>.Empty;
        private readonly SortedSet<OrderFlowDiagonalPair> _buy = new SortedSet<OrderFlowDiagonalPair>(new PairComparer(true));
        private readonly SortedSet<OrderFlowDiagonalPair> _sell = new SortedSet<OrderFlowDiagonalPair>(new PairComparer(false));
        private readonly SortedSet<OrderFlowDiagonalPair> _eligibleBuy = new SortedSet<OrderFlowDiagonalPair>(new PairComparer(true));
        private readonly SortedSet<OrderFlowDiagonalPair> _eligibleSell = new SortedSet<OrderFlowDiagonalPair>(new PairComparer(false));
        private decimal _buyVolume;
        private decimal _sellVolume;

        public OrderFlowImbalanceProfile(decimal step, OrderFlowImbalanceSettings settings) { _step = step; _settings = settings; }

        /// <summary>Adds a validated positive trade, or removes a previously added trade during window expiry. Caller owns source ordering.</summary>
        public void Change(OrderFlowDeal tick, bool remove = false)
        {
            _levels.TryGetValue(tick.Price, out (decimal Buy, decimal Sell) level);
            decimal amount = remove ? -tick.Volume : tick.Volume;
            if (tick.Side == Side.Buy)
            { level.Buy = OrderFlowVolumeComparison.AddExact(level.Buy, amount); _buyVolume = OrderFlowVolumeComparison.AddExact(_buyVolume, amount); }
            else
            { level.Sell = OrderFlowVolumeComparison.AddExact(level.Sell, amount); _sellVolume = OrderFlowVolumeComparison.AddExact(_sellVolume, amount); }
            if (level.Buy == 0 && level.Sell == 0) { _levels.Remove(tick.Price); }
            else { _levels[tick.Price] = level; }
            Refresh(tick.Price);
            if (tick.Price >= _step && tick.Price - (tick.Price - _step) == _step) { Refresh(tick.Price - _step); }
        }

        private void Refresh(decimal lower)
        {
            if (_pairs.TryGetValue(lower, out OrderFlowDiagonalPair old))
            { _pairs = _pairs.Remove(lower); _buy.Remove(old); _sell.Remove(old); _eligibleBuy.Remove(old); _eligibleSell.Remove(old); }
            if (lower > decimal.MaxValue - _step || (lower + _step) - lower != _step || !_levels.TryGetValue(lower, out (decimal Buy, decimal Sell) low) ||
                !_levels.TryGetValue(lower + _step, out (decimal Buy, decimal Sell) high) || low.Sell <= 0 || high.Buy <= 0) { return; }
            OrderFlowDiagonalPair pair = new OrderFlowDiagonalPair(lower, high.Buy, low.Sell);
            _pairs = _pairs.SetItem(lower, pair);
            if (pair.Buy == pair.Sell) { return; }
            bool buy = pair.Buy > pair.Sell;
            (buy ? _buy : _sell).Add(pair);
            if (Math.Max(pair.Buy, pair.Sell) >= _settings.MinimumDominantVolume &&
                OrderFlowVolumeComparison.DifferenceAtLeast(Math.Max(pair.Buy, pair.Sell), Math.Min(pair.Buy, pair.Sell), _settings.MinimumDifference))
            { (buy ? _eligibleBuy : _eligibleSell).Add(pair); }
        }

        #endregion

        #region Snapshot and ordering

        /// <summary>Returns detached immutable evidence; validates combined volume before publication so overflow and precision loss are rejected by the engine.</summary>
        public OrderFlowImbalanceSnapshot Snapshot()
        {
            _ = OrderFlowVolumeComparison.AddExact(_buyVolume, _sellVolume);
            return new OrderFlowImbalanceSnapshot { BuyVolume = _buyVolume, SellVolume = _sellVolume,
                ComparablePairs = _pairs.Count, Pairs = _pairs, BestBuy = _buy.Max, BestSell = _sell.Max,
                EligibleBuy = _eligibleBuy.Max, EligibleSell = _eligibleSell.Max };
        }

        private sealed class PairComparer : IComparer<OrderFlowDiagonalPair>
        {
            private readonly bool _buy;
            public PairComparer(bool buy) { _buy = buy; }
            public int Compare(OrderFlowDiagonalPair a, OrderFlowDiagonalPair b)
            {
                if (ReferenceEquals(a, b)) { return 0; }
                if (a == null) { return -1; } if (b == null) { return 1; }
                int ratio = _buy ? OrderFlowVolumeComparison.CompareProducts(a.Buy, b.Sell, b.Buy, a.Sell)
                    : OrderFlowVolumeComparison.CompareProducts(a.Sell, b.Buy, b.Sell, a.Buy);
                return ratio != 0 ? ratio : b.LowerPrice.CompareTo(a.LowerPrice);
            }
        }

        #endregion
    }

    /// <summary>Run-local all-tick context over [anchor-window, anchor], inclusive and in physical source order.</summary>
    /// <remarks>Called before the Cloud size filter. Same-time later rows are absent until consumed. Excluded dates never warm the window.</remarks>
    internal sealed class OrderFlowImbalanceWindow
    {
        private readonly long _duration;
        private readonly Queue<OrderFlowDeal> _ticks = new Queue<OrderFlowDeal>();
        private readonly OrderFlowImbalanceProfile _profile;
        public OrderFlowImbalanceWindow(decimal step, OrderFlowImbalanceSettings settings)
        { _duration = (long)settings.ContextSeconds * TimeSpan.TicksPerSecond; _profile = new OrderFlowImbalanceProfile(step, settings); }

        /// <summary>Consumes one validated selected-period row; cancellation is also checked during expiration of a dense old window.</summary>
        public void Add(OrderFlowDeal tick, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long cutoff = Math.Max(0, tick.Time.Ticks - _duration);
            while (_ticks.Count > 0 && _ticks.Peek().Time.Ticks < cutoff)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _profile.Change(_ticks.Dequeue(), true);
            }
            _profile.Change(tick); _ticks.Enqueue(tick);
        }

        /// <summary>Captures the current physical prefix without mutating or completing any Cloud.</summary>
        public OrderFlowImbalanceSnapshot Snapshot() { return _profile.Snapshot(); }
    }
}
