/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace OsEngine.OsData.OrderFlow.Explorer
{
    /// <summary>Exact nearest-rank multiset with AVL rank queries; bounded callers own retention and thread access.</summary>
    internal sealed class ExplorerDistribution
    {
        private sealed class Node
        {
            internal decimal Value;
            internal int Copies = 1, Count = 1, Height = 1;
            internal Node Left, Right;
        }
        private Node _root;
        #region Order statistics

        internal int Count => Size(_root);
        internal void Add(decimal value) { _root = Change(_root, value, 1); }
        internal void Remove(decimal value) { _root = Change(_root, value, -1); }
        internal int AtMost(decimal value) => Rank(_root, value);
        internal decimal Quantile(decimal p) => Select((int)decimal.Ceiling(p * Count) - 1);
        internal decimal Select(int index)
        {
            if (index < 0 || index >= Count) { throw new ArgumentOutOfRangeException(nameof(index)); }
            Node node = _root;
            while (node != null)
            {
                int left = Size(node.Left);
                if (index < left) { node = node.Left; }
                else if (index < left + node.Copies) { return node.Value; }
                else { index -= left + node.Copies; node = node.Right; }
            }
            throw new InvalidOperationException("Invalid distribution rank.");
        }
        #endregion

        #region Balanced tree

        private static int Size(Node node) => node?.Count ?? 0;
        private static int Height(Node node) => node?.Height ?? 0;
        private static void Update(Node node) { node.Count = Size(node.Left) + Size(node.Right) + node.Copies; node.Height = 1 + Math.Max(Height(node.Left), Height(node.Right)); }
        private static Node RotateLeft(Node node) { Node next = node.Right; node.Right = next.Left; next.Left = node; Update(node); Update(next); return next; }
        private static Node RotateRight(Node node) { Node next = node.Left; node.Left = next.Right; next.Right = node; Update(node); Update(next); return next; }
        private static int Rank(Node node, decimal value) => node == null ? 0 : value < node.Value ? Rank(node.Left, value) : Size(node.Left) + node.Copies + Rank(node.Right, value);
        private static Node Change(Node node, decimal value, int change)
        {
            if (node == null)
            {
                if (change < 0) { throw new InvalidOperationException("Missing distribution value."); }
                return new Node { Value = value };
            }
            if (value < node.Value) { node.Left = Change(node.Left, value, change); }
            else if (value > node.Value) { node.Right = Change(node.Right, value, change); }
            else
            {
                node.Copies += change;
                if (node.Copies == 0)
                {
                    if (node.Left == null) { return node.Right; }
                    if (node.Right == null) { return node.Left; }
                    Node successor = node.Right;
                    while (successor.Left != null) { successor = successor.Left; }
                    node.Value = successor.Value; node.Copies = successor.Copies;
                    successor.Copies = 1; node.Right = Change(node.Right, successor.Value, -1);
                }
            }
            Update(node);
            int balance = Height(node.Left) - Height(node.Right);
            if (balance > 1)
            {
                if (Height(node.Left.Left) < Height(node.Left.Right)) { node.Left = RotateLeft(node.Left); }
                return RotateRight(node);
            }
            if (balance < -1)
            {
                if (Height(node.Right.Right) < Height(node.Right.Left)) { node.Right = RotateRight(node.Right); }
                return RotateLeft(node);
            }
            return node;
        }
        #endregion
    }

    /// <summary>Causal M1 ATR and source-date raw windows. Before advances closed minutes; After admits the current physical row.</summary>
    /// <remarks>Single worker ownership. Date/gaps over five minutes reset ATR. Bounded queues fail explicitly instead of dropping observations.</remarks>
    internal sealed class ExplorerRawMetrics
    {
        private readonly ExplorerRunSpec _spec;
        private readonly List<DateTime> _recent = new List<DateTime>();
        private int _recentStart;
        private readonly Queue<decimal> _tr = new Queue<decimal>();
        private DateTime _minute;
        private decimal _high, _low, _close, _previousClose;
        private bool _bar, _haveClose;
        internal DateTime Date { get; private set; }
        internal DateTime? PreviousTime { get; private set; }
        internal DateTime FirstTime { get; private set; }
        internal int DateOrdinal { get; private set; }
        internal decimal RawVolume { get; private set; }
        internal decimal? Atr => _tr.Count == 20 && _tr.Sum() > 0 ? _tr.Sum() / 20 : null;
        internal long AtrStartSequence { get; private set; }
        internal ExplorerRawMetrics(ExplorerRunSpec spec) { _spec = spec; }

        #region Raw tick admission

        internal void Before(OrderFlowDeal tick)
        {
            bool newDate = Date != tick.Time.Date;
            bool gap = PreviousTime.HasValue && tick.Time - PreviousTime.Value > TimeSpan.FromMinutes(5);
            if (newDate)
            {
                Date = tick.Time.Date; DateOrdinal++; _recent.Clear(); _recentStart = 0; RawVolume = 0;
                PreviousTime = null; FirstTime = tick.Time;
            }
            if (newDate || gap) { _tr.Clear(); _bar = _haveClose = false; AtrStartSequence = tick.SourceSequence; }
            DateTime minute = new DateTime(tick.Time.Ticks - tick.Time.Ticks % TimeSpan.TicksPerMinute);
            if (_bar && minute > _minute)
            {
                if (_haveClose)
                {
                    decimal range = Math.Max(_high - _low, Math.Max(Math.Abs(_high - _previousClose), Math.Abs(_low - _previousClose)));
                    _tr.Enqueue(range); if (_tr.Count > 20) { _tr.Dequeue(); }
                }
                _previousClose = _close; _haveClose = true; _bar = false;
            }
            int retention = Math.Max(_spec.Study.ActivitySeconds, _spec.Profiles.Max(p => p.PaceSeconds));
            while (_recentStart < _recent.Count && (tick.Time - _recent[_recentStart]).TotalSeconds > retention) { _recentStart++; }
            if (_recentStart > 4096 && _recentStart > _recent.Count / 2) { _recent.RemoveRange(0, _recentStart); _recentStart = 0; }
        }
        #endregion

        #region Activity windows

        internal int CountPrevious(DateTime time, int seconds)
        {
            long boundary = time.Ticks - (long)seconds * TimeSpan.TicksPerSecond;
            int left = _recentStart, right = _recent.Count;
            while (left < right) { int middle = left + (right - left) / 2; if (_recent[middle].Ticks < boundary) { left = middle + 1; } else { right = middle; } }
            return _recent.Count - left;
        }
        internal bool ActivityKnown(DateTime time) => PreviousTime.HasValue && time - FirstTime >= TimeSpan.FromSeconds(_spec.Study.ActivitySeconds);
        internal bool ActivityPasses(DateTime time) => ActivityKnown(time) && CountPrevious(time, _spec.Study.ActivitySeconds) >= _spec.Study.ActivityMinimum &&
            time - PreviousTime.Value <= TimeSpan.FromSeconds(_spec.Study.ActivityMaximumPauseSeconds);
        internal void After(OrderFlowDeal tick)
        {
            if (_recent.Count - _recentStart >= _spec.MaximumBufferItems) { throw new InvalidDataException("Explorer raw window limit exceeded; narrow dates/window or raise the resource limit."); }
            _recent.Add(tick.Time); PreviousTime = tick.Time;
            RawVolume = OrderFlowVolumeComparison.AddExact(RawVolume, tick.Volume);
            if (!_bar) { _minute = new DateTime(tick.Time.Ticks - tick.Time.Ticks % TimeSpan.TicksPerMinute); _high = _low = tick.Price; _bar = true; }
            _high = Math.Max(_high, tick.Price); _low = Math.Min(_low, tick.Price); _close = tick.Price;
        }
        #endregion
    }

    /// <summary>Previous completed volume baselines, including exact time-of-day ranges over previous observed dates.</summary>
    /// <remarks>Minute AVL indexes provide rank queries; only two boundary minutes need scanning. The explicit item cap rejects excessive memory.</remarks>
    internal sealed class ExplorerVolumeBaseline
    {
        private sealed record Entry(int Date, long Clock, decimal Volume, long KnownSequence);
        private readonly ExplorerProfile _profile;
        private readonly int _limit;
        private readonly Queue<Entry> _rolling = new Queue<Entry>();
        private readonly ExplorerDistribution _rollingValues = new ExplorerDistribution();
        private readonly Queue<Entry> _history = new Queue<Entry>();
        private readonly Queue<Entry> _today = new Queue<Entry>();
        private readonly ExplorerDistribution[] _minutes = new ExplorerDistribution[1440];
        private readonly HashSet<Entry>[] _edges = new HashSet<Entry>[1440];
        private readonly ExplorerDistribution _all = new ExplorerDistribution();
        private Entry _latestHistory;
        private int _date;
        internal ExplorerVolumeBaseline(ExplorerProfile profile, int limit) { _profile = profile; _limit = limit; }
        #region Completed history

        internal void Advance(int date)
        {
            if (date != _date)
            {
                while (_today.Count > 0)
                {
                    AddHistory(_today.Dequeue());
                }
                _date = date;
            }
            while (_rolling.Count > 0 && _rolling.Peek().Date < date - _profile.VolumeDates + 1) { _rollingValues.Remove(_rolling.Dequeue().Volume); }
            while (_history.Count > 0 && _history.Peek().Date < date - _profile.TimeOfDayDates)
            {
                Entry entry = _history.Dequeue(); int minute = (int)(entry.Clock / TimeSpan.TicksPerMinute);
                _minutes[minute].Remove(entry.Volume); _edges[minute].Remove(entry); _all.Remove(entry.Volume);
            }
        }
        internal void Add(ExplorerCloud cloud, int date)
        {
            if (cloud.Reason == "OpenAtEnd") { return; }
            Entry entry = new Entry(date, cloud.StartTime.TimeOfDay.Ticks, cloud.Volume, cloud.KnownSequence.Value);
            _rolling.Enqueue(entry); _rollingValues.Add(entry.Volume);
            while (_rolling.Count > _profile.VolumeWindow + 1) { _rollingValues.Remove(_rolling.Dequeue().Volume); }
            if (_profile.TimeOfDayVolume)
            {
                if (_history.Count + _today.Count >= _limit) { throw new InvalidDataException("Explorer time-of-day baseline cache limit exceeded."); }
                if (date < _date) { AddHistory(entry); } else { _today.Enqueue(entry); }
            }
        }
        private void AddHistory(Entry entry)
        {
            _history.Enqueue(entry); _latestHistory = entry;
            int minute = (int)(entry.Clock / TimeSpan.TicksPerMinute);
            (_minutes[minute] ??= new ExplorerDistribution()).Add(entry.Volume);
            (_edges[minute] ??= new HashSet<Entry>()).Add(entry); _all.Add(entry.Volume);
        }
        #endregion

        #region Causal rank queries

        internal decimal? Rolling(long firstSequence)
        {
            // A chain closed by this row cannot train the next chain that starts on the same row.
            Entry[] ties = Excluded(firstSequence);
            foreach (Entry entry in ties) { _rollingValues.Remove(entry.Volume); }
            decimal? result = _rollingValues.Count >= _profile.VolumeMinimum ? _rollingValues.Quantile(_profile.VolumePercentile) : null;
            foreach (Entry entry in ties) { _rollingValues.Add(entry.Volume); }
            return result;
        }
        internal decimal? Percentile(decimal volume, long firstSequence)
        {
            Entry[] excluded = Excluded(firstSequence);
            int count = _rollingValues.Count - excluded.Length;
            if (count < _profile.VolumeMinimum) { return null; }
            return 100m * (_rollingValues.AtMost(volume) - excluded.Count(e => e.Volume <= volume)) / count;
        }
        private Entry[] Excluded(long firstSequence)
        {
            Entry[] future = _rolling.Where(e => e.KnownSequence >= firstSequence).ToArray();
            int oldest = Math.Max(0, _rolling.Count - future.Length - _profile.VolumeWindow);
            return oldest == 0 ? future : future.Concat(_rolling.Where(e => e.KnownSequence < firstSequence).Take(oldest)).ToArray();
        }
        internal decimal? TimeOfDay(DateTime time, long beforeSequence = long.MaxValue)
        {
            if (!_profile.TimeOfDayVolume || _all.Count == 0) { return null; }
            long lo = Math.Max(0, time.TimeOfDay.Ticks - _profile.TimeOfDayMinutes * TimeSpan.TicksPerMinute);
            long hi = Math.Min(TimeSpan.TicksPerDay - 1, time.TimeOfDay.Ticks + _profile.TimeOfDayMinutes * TimeSpan.TicksPerMinute);
            int total = ClockRank(lo, hi, decimal.MaxValue, beforeSequence);
            if (total < _profile.VolumeMinimum) { return null; }
            int target = (int)decimal.Ceiling(total * _profile.VolumePercentile), left = 0, right = _all.Count - 1;
            while (left < right)
            {
                int middle = left + (right - left) / 2;
                if (ClockRank(lo, hi, _all.Select(middle), beforeSequence) >= target) { right = middle; } else { left = middle + 1; }
            }
            return _all.Select(left);
        }
        internal decimal? TimeOfDayPercentile(DateTime time, decimal volume, long beforeSequence = long.MaxValue)
        {
            long lo = Math.Max(0, time.TimeOfDay.Ticks - _profile.TimeOfDayMinutes * TimeSpan.TicksPerMinute);
            long hi = Math.Min(TimeSpan.TicksPerDay - 1, time.TimeOfDay.Ticks + _profile.TimeOfDayMinutes * TimeSpan.TicksPerMinute);
            int count = ClockRank(lo, hi, decimal.MaxValue, beforeSequence);
            return count < _profile.VolumeMinimum ? null : 100m * ClockRank(lo, hi, volume, beforeSequence) / count;
        }
        private int ClockRank(long lo, long hi, decimal volume, long beforeSequence)
        {
            int first = (int)(lo / TimeSpan.TicksPerMinute), last = (int)(hi / TimeSpan.TicksPerMinute), count = 0;
            for (int minute = first; minute <= last; minute++)
            {
                if (minute > first && minute < last) { count += _minutes[minute]?.AtMost(volume) ?? 0; }
                else if (_edges[minute] != null)
                { foreach (Entry entry in _edges[minute]) { if (entry.Clock >= lo && entry.Clock <= hi && entry.Volume <= volume) { count++; } } }
            }
            // At a date boundary, exactly one chain of this sequential profile may have
            // just completed on the current row. It cannot train that row's new chain/watch.
            if (_latestHistory != null && _latestHistory.Date >= _date - _profile.TimeOfDayDates &&
                _latestHistory.KnownSequence >= beforeSequence && _latestHistory.Clock >= lo && _latestHistory.Clock <= hi && _latestHistory.Volume <= volume) { count--; }
            return count;
        }
        #endregion
    }

    /// <summary>Stable weighted online moments over all raw ticks of a single source date; no Cloud-size filtering.</summary>
    internal sealed class ExplorerVwap
    {
        internal decimal Weight { get; private set; }
        internal decimal Mean { get; private set; }
        private decimal _moment;
        private decimal _notional;
        internal decimal Sigma => Weight == 0 ? 0 : (decimal)Math.Sqrt((double)Math.Max(0, _moment / Weight));
        internal void Add(decimal price, decimal volume)
        {
            decimal weight = OrderFlowVolumeComparison.AddExact(Weight, volume);
            decimal difference = price - Mean;
            _notional = OrderFlowVolumeComparison.AddExact(_notional, ExplorerArithmetic.ProductExact(price, volume));
            decimal mean = _notional / weight;
            // Normalized central moments involve rounded divisions; raw W and PV sums remain exact.
            _moment += volume * difference * (price - mean);
            Weight = weight; Mean = mean;
        }
    }

    internal static class ExplorerArithmetic
    {
        internal static decimal ProductExact(decimal a, decimal b)
        {
            decimal result = a * b;
            if (a == decimal.Truncate(a) && b == decimal.Truncate(b)) { return result; }
            (BigInteger Value, int Scale) left = Parts(a), right = Parts(b), actual = Parts(result);
            if (left.Value * right.Value * BigInteger.Pow(10, actual.Scale) != actual.Value * BigInteger.Pow(10, left.Scale + right.Scale))
            { throw new InvalidDataException("Explorer price-volume product cannot be represented exactly as decimal."); }
            return result;
        }
        private static (BigInteger, int) Parts(decimal value)
        {
            int[] bits = decimal.GetBits(value);
            BigInteger integer = ((BigInteger)(uint)bits[2] << 64) | ((BigInteger)(uint)bits[1] << 32) | (uint)bits[0];
            return ((bits[3] & int.MinValue) == 0 ? integer : -integer, (bits[3] >> 16) & 255);
        }
    }
}
