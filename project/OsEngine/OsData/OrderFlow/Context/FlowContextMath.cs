/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Generic;
using System.Linq;

namespace OsEngine.OsData.OrderFlow.Context
{
    /// <summary>All-trade weighted population moments and last-price time integration owned by one calculation thread.</summary>
    /// <remarks>Equal timestamps have zero time weight. An entire interval exceeding the configured gap or crossing a session is excluded, without interpolation.</remarks>
    internal sealed class FlowContextMoments
    {
        private decimal _notional;
        private decimal _moment;
        private decimal _timeNotional;
        private decimal _seconds;
        private DateTime _slopeTime;
        private decimal _slopeMean;
        private decimal _slope;
        public decimal SlopeTicksPerMinute(decimal step) => _slope / step;
        private FlowContextTick _previous;
        public decimal Volume { get; private set; }
        public decimal Mean { get; private set; }
        public decimal Delta { get; private set; }
        public decimal Sigma => Volume == 0 ? 0 : (decimal)Math.Sqrt((double)Math.Max(0, _moment / Volume));
        public decimal? Twap => _seconds > 0 ? _timeNotional / _seconds : null;
        public void Add(FlowContextTick tick, FlowContextSettings settings)
        {
            if (_previous != null && tick.Time.Date == _previous.Time.Date && settings.SessionIndex(tick.Time) == settings.SessionIndex(_previous.Time))
            {
                decimal elapsed = (tick.Time.Ticks - _previous.Time.Ticks) / (decimal)TimeSpan.TicksPerSecond;
                if (elapsed >= 0 && elapsed <= settings.MaximumGapSeconds)
                { _seconds += elapsed; _timeNotional += _previous.Price * elapsed; }
            }
            // Fixed sixty-second anchors are independent of display sampling and event emission.
            if (_previous == null) { _slopeTime = tick.Time; _slopeMean = tick.Price; }
            if ((tick.Time - _slopeTime).TotalSeconds >= 60)
            {
                _slope = (Mean - _slopeMean) / ((decimal)(tick.Time - _slopeTime).TotalSeconds / 60);
                _slopeTime = tick.Time; _slopeMean = Mean;
            }
            decimal difference = tick.Price - Mean;
            Volume = FlowContextArithmetic.Add(Volume, tick.Volume);
            _notional = FlowContextArithmetic.Add(_notional, FlowContextArithmetic.Product(tick.Price, tick.Volume));
            Mean = _notional / Volume;
            _moment += tick.Volume * difference * (tick.Price - Mean);
            Delta += tick.Buy ? tick.Volume : -tick.Volume;
            _previous = tick;
        }
        public FlowContextMoments Copy() => (FlowContextMoments)MemberwiseClone();
    }

    /// <summary>Rejects loss of positive raw quantity/notional precision instead of silently accepting decimal underflow.</summary>
    internal static class FlowContextArithmetic
    {
        public static decimal Add(decimal left, decimal right) => OrderFlowVolumeComparison.AddExact(left, right);
        public static decimal Product(decimal left, decimal right)
        {
            decimal value = left * right;
            if (OrderFlowVolumeComparison.CompareProducts(left, right, value, 1) != 0) { throw new System.IO.InvalidDataException("Недостаточно точности decimal для оборота исходной сделки."); }
            return value;
        }
    }

    internal sealed class FlowTapeWindow
    {
        public DateTime Start;
        public FlowContextTick First;
        public FlowContextTick Last;
        public decimal Buy;
        public decimal Sell;
        public decimal High;
        public decimal Low;
        public int Count;
        public readonly Dictionary<(bool Buy, decimal Size), int> Sizes = new Dictionary<(bool, decimal), int>();
        public decimal Volume => Buy + Sell;
        public decimal Delta => Buy - Sell;
        public void Add(FlowContextTick tick)
        {
            if (First == null) { First = tick; High = Low = tick.Price; }
            Last = tick; Count++; High = Math.Max(High, tick.Price); Low = Math.Min(Low, tick.Price);
            if (tick.Buy) { Buy += tick.Volume; } else { Sell += tick.Volume; }
            (bool Buy, decimal Size) key = (tick.Buy, tick.Volume);
            Sizes.TryGetValue(key, out int count); Sizes[key] = count + 1;
        }
        public (int Count, int Direction) Mode()
        {
            KeyValuePair<(bool Buy, decimal Size), int> best = Sizes.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key.Size).ThenBy(pair => pair.Key.Buy).First();
            return (best.Value, best.Key.Buy ? 1 : -1);
        }
    }

    /// <summary>Confirmed directional-change pivots. Extreme time and recognition time remain distinct.</summary>
    internal sealed class FlowContextSwing
    {
        private int _direction;
        private FlowContextTick _extreme;
        private FlowContextTick _high;
        private FlowContextTick _low;
        private decimal? _previousHigh;
        private decimal? _previousLow;
        private readonly decimal _threshold;
        public decimal? LastHigh { get; private set; }
        public decimal? LastLow { get; private set; }
        public int Structure => LastHigh > _previousHigh && LastLow > _previousLow ? 1 : LastHigh < _previousHigh && LastLow < _previousLow ? -1 : 0;
        public FlowContextSwing(decimal threshold) { _threshold = threshold; }
        public FlowContextTick Add(FlowContextTick tick, out int pivotDirection)
        {
            pivotDirection = 0;
            _high ??= tick; _low ??= tick; _extreme ??= tick;
            if (_direction == 0)
            {
                if (tick.Price > _high.Price) { _high = tick; }
                if (tick.Price < _low.Price) { _low = tick; }
                if (tick.Price - _low.Price >= _threshold) { _direction = 1; _extreme = tick; LastLow = _low.Price; pivotDirection = 1; return _low; }
                if (_high.Price - tick.Price >= _threshold) { _direction = -1; _extreme = tick; LastHigh = _high.Price; pivotDirection = -1; return _high; }
                return null;
            }
            if ((_direction > 0 && tick.Price > _extreme.Price) || (_direction < 0 && tick.Price < _extreme.Price)) { _extreme = tick; }
            if ((_direction > 0 && _extreme.Price - tick.Price < _threshold) || (_direction < 0 && tick.Price - _extreme.Price < _threshold)) { return null; }
            FlowContextTick confirmed = _extreme;
            pivotDirection = -_direction;
            if (_direction > 0) { _previousHigh = LastHigh; LastHigh = confirmed.Price; }
            else { _previousLow = LastLow; LastLow = confirmed.Price; }
            _direction = -_direction; _extreme = tick;
            return confirmed;
        }
    }
}
