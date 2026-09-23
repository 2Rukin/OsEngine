/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using OsEngine.Entity;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace OsEngine.OsData.OrderFlow
{
    /// <summary>Tick-compatible Cloud filters in recorded volume units and manual price ticks.</summary>
    /// <remarks>
    /// Based on the public SBProX Clouds guide, with local boundary choices documented in
    /// ORDER-FLOW-MVP-RUNBOOK-001. Spread modes and the undisclosed Smart formula are unsupported.
    /// One run owns this mutable DTO; validation freezes only effective settings by convention.
    /// </remarks>
    internal sealed class OrderFlowCloudSettings
    {
        /// <summary>True emits each qualifying physical tick immediately; false accumulates eligible chains.</summary>
        public bool SingleTicks { get; set; }
        public decimal MinimumTickVolume { get; set; } = 1;
        public decimal MinimumSumVolume { get; set; } = 100;
        public int MaximumGapMilliseconds { get; set; } = 1000;
        public int MaximumRangeTicks { get; set; } = 5;
        public OrderFlowImbalanceSettings Imbalance { get; set; } = new OrderFlowImbalanceSettings();

        public void Validate()
        {
            (Imbalance ?? throw new ArgumentException("Cloud imbalance settings are required.")).Validate();
            if (SingleTicks)
            {
                if (MinimumTickVolume <= 0) { throw new ArgumentException("Single ticks require a positive volume threshold."); }
                MinimumSumVolume = MinimumTickVolume;
                MaximumGapMilliseconds = MaximumRangeTicks = 0;
                return;
            }
            if (MinimumTickVolume <= 0 || MinimumSumVolume <= 0 || MaximumGapMilliseconds < 0 || MaximumRangeTicks < 0)
            {
                throw new ArgumentException("Cloud requires positive volumes and non-negative gap/range.");
            }
        }

        public string CanonicalValue()
        {
            return string.Join("|", SingleTicks ? "SINGLE_TICKS" : "CHAIN", MinimumTickVolume.ToString("G29", CultureInfo.InvariantCulture),
                MinimumSumVolume.ToString("G29", CultureInfo.InvariantCulture),
                MaximumGapMilliseconds.ToString(CultureInfo.InvariantCulture), MaximumRangeTicks.ToString(CultureInfo.InvariantCulture), Imbalance.CanonicalValue());
        }
    }

    /// <summary>
    /// Historical chain or detached forming replay snapshot, anchored at its last included tick; not an entry signal.
    /// </summary>
    /// <remarks>
    /// CompletedAt is the breaking eligible tick time in chain mode; null means Forming or OpenAtEnd.
    /// SingleTicks completes at its own tick time/sequence with reason SingleTick, never Forming or OpenAtEnd.
    /// Anchor and closing source sequences disambiguate equal timestamps, including one-tick chains.
    /// Qualified is frozen at the first threshold crossing; final fields must not be read at that earlier time.
    /// The current importer accepts Buy/Sell only; no quotes are reconstructed.
    /// </remarks>
    internal sealed class OrderFlowCloud
    {
        /// <summary>Copies the chain and its frozen qualification evidence for a detached playback frame.</summary>
        internal OrderFlowCloud Copy()
        {
            OrderFlowCloud copy = (OrderFlowCloud)MemberwiseClone();
            copy.Qualified = Qualified?.Copy();
            return copy;
        }

        public string CloudId { get; set; }
        public long FirstSourceSequence { get; set; }
        public decimal FirstPrice { get; set; }
        public decimal Notional { get; set; }
        public decimal PriceStep { get; set; }
        public OrderFlowCloudSnapshot Qualified { get; set; }
        public string Kind { get { return TradeCount == 1 ? "SingleTrade" : "Accumulated"; } }
        public decimal Vwap { get { return Volume == 0 ? 0 : Notional / Volume; } }
        public decimal RangePrice { get { return High - Low; } }
        public decimal RangeTicks { get { return RangePrice / PriceStep; } }
        public decimal PriceChange { get { return Price - FirstPrice; } }
        public decimal DurationMilliseconds { get { return (Time.Ticks - StartTime.Ticks) / (decimal)TimeSpan.TicksPerMillisecond; } }
        public decimal UnknownVolume { get { return 0; } }
        public DateTime StartTime { get; set; }
        public DateTime Time { get; set; }
        public DateTime? CompletedAt { get; set; }
        public long LastSourceSequence { get; set; }
        public long? CompletionSourceSequence { get; set; }
        public string CompletionReason { get; set; }
        public decimal Price { get; set; }
        public decimal Low { get; set; }
        public decimal High { get; set; }
        public decimal BuyVolume { get; set; }
        public decimal SellVolume { get; set; }
        public int BuyCount { get; set; }
        public int SellCount { get; set; }
        public decimal LargestTick { get; set; }
        public decimal Volume { get { return BuyVolume + SellVolume; } }
        public decimal Delta { get { return BuyVolume - SellVolume; } }
        /// <summary>Signed volume delta as a percentage of Cloud volume; independent of count imbalance.</summary>
        public decimal DeltaPercent => Volume == 0 ? 0 : Delta / Volume * 100;
        /// <summary>Immutable internal and all-tick context evidence frozen at Time/LastSourceSequence, never at the later closing tick.</summary>
        public OrderFlowImbalanceSnapshot InsideImbalance { get; set; }
        public OrderFlowImbalanceSnapshot ContextImbalance { get; set; }
        /// <summary>Post-formation filter verdict; failing records remain in tables/artifacts without changing chain boundaries.</summary>
        public bool ImbalancePassed { get; set; } = true;
        public OrderFlowImbalanceSource ImbalanceSource { get; set; }
        public int TradeCount { get { return BuyCount + SellCount; } }
        public decimal SidePercent { get { return TradeCount == 0 ? 0 : 100m * (BuyCount - SellCount) / TradeCount; } }
    }

    /// <summary>Detached prefix evidence captured once when cumulative volume first reaches the threshold.</summary>
    /// <remarks>No final chain fields or later ticks enter this snapshot. SourceSequence resolves equal-time qualification.</remarks>
    internal sealed class OrderFlowCloudSnapshot
    {
        internal OrderFlowCloudSnapshot Copy() { return (OrderFlowCloudSnapshot)MemberwiseClone(); }

        public DateTime Time { get; set; }
        public long SourceSequence { get; set; }
        public decimal Price { get; set; }
        public decimal Low { get; set; }
        public decimal High { get; set; }
        public decimal Volume { get; set; }
        public decimal BuyVolume { get; set; }
        public decimal SellVolume { get; set; }
        public int TradeCount { get; set; }
        public decimal Vwap { get; set; }
    }

    /// <summary>Single-consumer, source-ordered chain accumulator for already validated, period-selected ticks.</summary>
    /// <remarks>
    /// Validated settings select chain accumulation or individual qualifying physical ticks.
    /// SingleTicks finalizes each accepted tick immediately at its own time/sequence; excluded ticks never accumulate.
    /// Inclusive volume/gap/range boundaries in chain mode; excluded sizes do not update or break a chain.
    /// An eligible breaking tick closes the old chain and starts the next one. Output is final historical
    /// grouping with separate completion evidence; EOF does not prove that a chain ended in the market.
    /// No daily reset, deduplication, orders or labels. Decimal overflow propagates to replay rejection.
    /// Contract: ORDER-FLOW-DATA-001 and ORDER-FLOW-MVP-RUNBOOK-001.
    /// </remarks>
    internal sealed class OrderFlowCloudAccumulator
    {
        private readonly OrderFlowCloudSettings _settings;
        private readonly decimal _priceStep;
        private readonly List<OrderFlowCloud> _output;
        private OrderFlowCloud _current;
        private readonly string _idPrefix;
        private readonly OrderFlowImbalanceWindow _context;
        private OrderFlowImbalanceProfile _inside;

        public OrderFlowCloudAccumulator(OrderFlowCloudSettings settings, decimal priceStep, List<OrderFlowCloud> output, string idPrefix = "CL-")
        {
            _settings = settings;
            _priceStep = priceStep;
            _output = output;
            _idPrefix = idPrefix;
            _context = new OrderFlowImbalanceWindow(priceStep, settings.Imbalance);
        }

        /// <summary>Advances all-tick context before Cloud size selection; freezes both profiles only when this Cloud includes the row.</summary>
        /// <remarks>Cancellation propagates, including during context expiry. Later breaking rows never replace a previous Cloud anchor snapshot.</remarks>
        public void Add(OrderFlowDeal tick, CancellationToken cancellationToken = default)
        {
            _context.Add(tick, cancellationToken);
            if (tick.Volume < _settings.MinimumTickVolume) { return; }

            if (_current != null)
            {
                bool gap = tick.Time.Ticks - _current.Time.Ticks > (long)_settings.MaximumGapMilliseconds * TimeSpan.TicksPerMillisecond;
                bool range = (Math.Max(_current.High, tick.Price) - Math.Min(_current.Low, tick.Price)) / _priceStep > _settings.MaximumRangeTicks;
                if (gap || range) { Finish(tick, gap ? "Gap" : "Range"); }
            }
            if (_current == null)
            {
                _inside = new OrderFlowImbalanceProfile(_priceStep, _settings.Imbalance);
                _current = new OrderFlowCloud { StartTime = tick.Time, Low = tick.Price, High = tick.Price,
                    FirstPrice = tick.Price, FirstSourceSequence = tick.SourceSequence, PriceStep = _priceStep };
            }
            _current.Time = tick.Time;
            _current.Price = tick.Price;
            _current.LastSourceSequence = tick.SourceSequence;
            _current.Notional += tick.Price * tick.Volume;
            _current.Low = Math.Min(_current.Low, tick.Price);
            _current.High = Math.Max(_current.High, tick.Price);
            _current.LargestTick = Math.Max(_current.LargestTick, tick.Volume);
            if (tick.Side == Side.Buy) { _current.BuyVolume += tick.Volume; _current.BuyCount++; }
            else { _current.SellVolume += tick.Volume; _current.SellCount++; }
            _inside.Change(tick);
            _current.InsideImbalance = _inside.Snapshot();
            _current.ContextImbalance = _context.Snapshot();
            _current.ImbalanceSource = _settings.Imbalance.Source;
            _current.ImbalancePassed = _settings.Imbalance.Passes(_current.InsideImbalance, _current.ContextImbalance);
            if (_current.Qualified == null && _current.Volume >= _settings.MinimumSumVolume)
            {
                _current.Qualified = new OrderFlowCloudSnapshot { Time = tick.Time, SourceSequence = tick.SourceSequence,
                    Price = tick.Price, Low = _current.Low, High = _current.High, Volume = _current.Volume,
                    BuyVolume = _current.BuyVolume, SellVolume = _current.SellVolume,
                    TradeCount = _current.TradeCount, Vwap = _current.Vwap };
            }
            if (_settings.SingleTicks) { Finish(tick, "SingleTick"); }
        }

        /// <summary>Copies completed chains and a qualified forming chain using only already consumed ticks.</summary>
        /// <remarks>Finished DTOs are shared read-only; the active chain and its qualification snapshot are copied. Does not finalize.</remarks>
        internal List<OrderFlowCloud> Snapshot()
        {
            List<OrderFlowCloud> clouds = new List<OrderFlowCloud>(_output);
            if (_current?.Qualified != null)
            {
                OrderFlowCloud forming = _current.Copy();
                forming.CloudId = _idPrefix + forming.FirstSourceSequence.ToString("D10", CultureInfo.InvariantCulture) + "-FORMING";
                forming.CompletionReason = "Forming";
                clouds.Add(forming);
            }
            return clouds;
        }

        /// <summary>Publishes the trailing chain as open-at-end, without borrowing ticks outside the selected period.</summary>
        public void Complete() { Finish(null, "OpenAtEnd"); }

        private void Finish(OrderFlowDeal closingTick, string reason)
        {
            if (_current == null) { return; }
            if (_current.Qualified != null)
            {
                _current.CloudId = _idPrefix + _current.FirstSourceSequence.ToString("D10", CultureInfo.InvariantCulture) + "-" + _current.LastSourceSequence.ToString("D10", CultureInfo.InvariantCulture);
                _current.CompletedAt = closingTick?.Time;
                _current.CompletionSourceSequence = closingTick?.SourceSequence;
                _current.CompletionReason = reason;
                _output.Add(_current);
            }
            _current = null;
        }
    }
}
