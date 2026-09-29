/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace OsEngine.OsData.OrderFlow.Context
{
    /// <summary>Offline future labels on one date of raw trades, isolated from causal features.</summary>
    /// <remarks>
    /// Range extrema and first-barrier queries use a segment tree. A horizon includes every row at Due,
    /// and is known only at the first strictly later source row. EOF cannot prove a closing timestamp.
    /// Scheduled session breaks are excluded; observed gaps intersecting the path conservatively censor it.
    /// Memory is bounded to two million rows: one date and its equal-midnight boundary group.
    /// </remarks>
    internal sealed class FlowContextOutcomes
    {
        private sealed class Pending
        {
            internal FlowContextEvent Event;
            internal FlowContextOutcome Outcome;
        }
        private readonly FlowContextSettings _settings;
        private readonly List<FlowContextOutcome> _output;
        private readonly List<FlowContextTick> _raw = new List<FlowContextTick>();
        private readonly List<Pending> _pending = new List<Pending>();
        private CancellationToken _cancellation;
        public FlowContextOutcomes(FlowContextSettings settings, List<FlowContextOutcome> output) { _settings = settings; _output = output; }
        public void Add(FlowContextTick tick, CancellationToken cancellation)
        {
            _cancellation = cancellation;
            // Keep the entire midnight timestamp. New-date events may already be scheduled
            // while that group is read; flush only the previous date and retain the boundary rows.
            if (_raw.Count > 0 && _raw[0].Time.Date != tick.Time.Date && tick.Time > _raw[0].Time.Date.AddDays(1))
            {
                DateTime completedDate = _raw[0].Time.Date;
                Evaluate(tick, completedDate);
                _raw.RemoveAll(value => value.Time.Date == completedDate);
                _pending.RemoveAll(value => value.Event.Time.Date == completedDate);
            }
            if (_raw.Count >= 2000000) { throw new InvalidDataException("Превышен лимит двух миллионов сделок за сутки для исходов."); }
            _raw.Add(tick);
        }
        public void Schedule(FlowContextEvent item, DateTime due, string horizon)
        {
            FlowContextOutcome outcome = new FlowContextOutcome { EventId = item.Id, Due = due, Horizon = horizon };
            _output.Add(outcome);
            if (due > _settings.DayEnd(item.Time)) { outcome.Status = "OutsideDay"; return; }
            if (due != _settings.DayEnd(item.Time) && _settings.SessionIndex(due) < 0) { outcome.Status = "OutsideSession"; return; }
            _pending.Add(new Pending { Event = item, Outcome = outcome });
        }
        public void Complete() { Evaluate(null, null); _pending.Clear(); _raw.Clear(); }
        private void Evaluate(FlowContextTick nextDay, DateTime? completedDate)
        {
            if (_pending.Count == 0) { return; }
            FlowContextTick[] ticks = _raw.Where(tick => _settings.SessionIndex(tick.Time) >= 0).ToArray();
            PriceIndex index = new PriceIndex(ticks, _cancellation);
            int[] gaps = new int[ticks.Length + 1];
            for (int i = 1; i < ticks.Length; i++)
            {
                bool gap = _settings.SessionIndex(ticks[i].Time) == _settings.SessionIndex(ticks[i - 1].Time) && (ticks[i].Time - ticks[i - 1].Time).TotalSeconds > _settings.MaximumGapSeconds;
                gaps[i + 1] = gaps[i] + (gap ? 1 : 0);
            }
            foreach (Pending pending in _pending.Where(value => !completedDate.HasValue || value.Event.Time.Date == completedDate.Value))
            {
                _cancellation.ThrowIfCancellationRequested();
                FlowContextEvent item = pending.Event; FlowContextOutcome result = pending.Outcome;
                int first = UpperSequence(ticks, item.Sequence);
                int after = UpperTime(ticks, result.Due);
                int last = after - 1;
                int rawAfter = UpperTime(_raw, result.Due);
                FlowContextTick known = rawAfter < _raw.Count ? _raw[rawAfter] : nextDay?.Time > result.Due ? nextDay : null;
                result.KnownSequence = known?.Sequence;
                decimal finalPrice = last >= first ? ticks[last].Price : item.Price;
                DateTime finalTime = last >= first ? ticks[last].Time : item.Time;
                if (last >= first)
                {
                    (decimal low, decimal high) = index.Extrema(first, last);
                    result.MfeTicks = Math.Max(0, (item.Direction > 0 ? high - item.Price : item.Price - low) / _settings.PriceStep);
                    result.MaeTicks = Math.Max(0, (item.Direction > 0 ? item.Price - low : high - item.Price) / _settings.PriceStep);
                    int target = index.First(first, last, item.Price + item.Direction * _settings.TargetTicks * _settings.PriceStep, item.Direction > 0);
                    int stop = index.First(first, last, item.Price - item.Direction * _settings.StopTicks * _settings.PriceStep, item.Direction < 0);
                    if (target >= 0 || stop >= 0)
                    {
                        int hit = target < 0 ? stop : stop < 0 ? target : Math.Min(target, stop);
                        result.BarrierTime = ticks[hit].Time;
                        result.Barrier = target >= 0 && stop >= 0 && ticks[target].Time == ticks[stop].Time ? "AmbiguousSameTimestamp" : hit == target ? "TargetFirst" : "StopFirst";
                    }
                }
                // Include a gap ending after Due when it starts before Due; there is no observable path inside it.
                int gapLast = after < ticks.Length && after > 0 && ticks[after - 1].Time < result.Due ? after : last;
                bool hasGap = gapLast >= first && gaps[gapLast + 1] - gaps[first] > 0;
                bool stale = (result.Due - finalTime).TotalSeconds > _settings.MaximumGapSeconds;
                result.Status = known == null ? "Incomplete" : hasGap || stale ? "DataGap" : "Complete";
                result.ChangeTicks = result.Status == "Complete" ? item.Direction * (finalPrice - item.Price) / _settings.PriceStep : null;
            }
        }
        private static int UpperTime(IReadOnlyList<FlowContextTick> ticks, DateTime time)
        {
            int low = 0, high = ticks.Count;
            while (low < high) { int middle = low + (high - low) / 2; if (ticks[middle].Time <= time) { low = middle + 1; } else { high = middle; } }
            return low;
        }
        private static int UpperSequence(IReadOnlyList<FlowContextTick> ticks, long sequence)
        {
            int low = 0, high = ticks.Count;
            while (low < high) { int middle = low + (high - low) / 2; if (ticks[middle].Sequence <= sequence) { low = middle + 1; } else { high = middle; } }
            return low;
        }
        private sealed class PriceIndex
        {
            private readonly decimal[] _low;
            private readonly decimal[] _high;
            private readonly int _size;
            internal PriceIndex(FlowContextTick[] ticks, CancellationToken cancellation)
            {
                _size = 1; while (_size < ticks.Length) { _size *= 2; }
                _low = new decimal[_size * 2]; _high = new decimal[_size * 2];
                Array.Fill(_low, decimal.MaxValue); Array.Fill(_high, decimal.MinValue);
                for (int i = 0; i < ticks.Length; i++) { if (i % 8192 == 0) { cancellation.ThrowIfCancellationRequested(); } _low[_size + i] = _high[_size + i] = ticks[i].Price; }
                for (int i = _size - 1; i > 0; i--) { _low[i] = Math.Min(_low[i * 2], _low[i * 2 + 1]); _high[i] = Math.Max(_high[i * 2], _high[i * 2 + 1]); }
            }
            internal (decimal Low, decimal High) Extrema(int first, int last)
            {
                decimal low = decimal.MaxValue, high = decimal.MinValue;
                for (int left = first + _size, right = last + _size; left <= right; left /= 2, right /= 2)
                {
                    if ((left & 1) == 1) { low = Math.Min(low, _low[left]); high = Math.Max(high, _high[left++]); }
                    if ((right & 1) == 0) { low = Math.Min(low, _low[right]); high = Math.Max(high, _high[right--]); }
                }
                return (low, high);
            }
            internal int First(int first, int last, decimal threshold, bool above) => Find(1, 0, _size - 1, first, last, threshold, above);
            private int Find(int node, int low, int high, int first, int last, decimal threshold, bool above)
            {
                if (high < first || low > last || (above ? _high[node] < threshold : _low[node] > threshold)) { return -1; }
                if (low == high) { return low; }
                int middle = low + (high - low) / 2;
                int left = Find(node * 2, low, middle, first, last, threshold, above);
                return left >= 0 ? left : Find(node * 2 + 1, middle + 1, high, first, last, threshold, above);
            }
        }
    }
}
