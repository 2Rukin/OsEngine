/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.Entity;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace OsEngine.OsData.OrderFlow.Explorer
{
    /// <summary>Single-consumer causal day/week kernel shared by batch and replay. No saved finals or future rows enter features.</summary>
    internal sealed class ExplorerPatternKernel
    {
        private readonly ExplorerRunSpec _run;
        private readonly ExplorerPatternSpec _plan;
        private readonly string _hash;
        private readonly ExplorerRawMetrics _metrics;
        private readonly ExplorerCatalog _catalog;
        private readonly ExplorerEpisodes _episodes;
        private readonly ExplorerSwing _swing;
        private readonly Action<ExplorerPatternSnapshot> _snapshot;
        private readonly ExplorerPatternLabels _labels;
        private readonly List<ExplorerPatternDay> _days = new List<ExplorerPatternDay>();
        private readonly HashSet<string> _crossed = new HashSet<string>();
        private readonly List<DateTime> _sourceDates = new List<DateTime>();
        private readonly List<(string Id, string Kind, string Profile, DateTime Observed, decimal Volume, decimal Delta, decimal? Relative)> _anchors = new List<(string, string, string, DateTime, decimal, decimal, decimal?)>();
        private ExplorerPatternDay _day;
        private DateTime _firstDate, _week, _dayStart;
        private long _gridSlot = -1;
        private bool _incompleteWeek;
        private decimal _notional;
        private long _lastBuy, _lastSell, _lastLow, _lastHigh;
        private DateTime? _buyTime, _sellTime, _lowTime, _highTime;
        internal long AnchorCount { get; private set; }
        internal long EpisodeCount { get; private set; }
        internal long CloudCount => _catalog.CompletedCount;
        internal ExplorerPatternKernel(ExplorerRunSpec run, ExplorerPatternSpec plan, Action<ExplorerPatternSnapshot> snapshot, Action<ExplorerPatternRow> label)
        {
            run.Validate(); plan.Validate(); _run = run; _plan = plan; _hash = plan.Hash(run); _snapshot = snapshot;
            _metrics = new ExplorerRawMetrics(run); _swing = new ExplorerSwing(run); _labels = new ExplorerPatternLabels(plan, label);
            _episodes = new ExplorerEpisodes(run, Episode, _ => { }, _ => { });
            _catalog = new ExplorerCatalog(run, Cloud, Prefix);
        }
        private void Cloud(ExplorerCloud cloud)
        {
            if (_day != null && cloud.Time.Date == _day.Date) { _day = _day with { Clouds = _day.Clouds + 1 }; }
            if (_run.Episodes.Enabled) { _episodes.Add(cloud, _metrics.DateOrdinal); }
        }
        private void Prefix(ExplorerPrefix prefix)
        {
            if (prefix.Completed) { _crossed.Remove(prefix.Id); return; }
            decimal volume = prefix.Buy + prefix.Sell;
            if (volume < _plan.AnchorVolume || !_crossed.Add(prefix.Id)) { return; }
            decimal delta = (prefix.Buy - prefix.Sell) / volume;
            if (delta > 0) { _lastBuy = prefix.Sequence; _buyTime = prefix.Time; }
            if (delta < 0) { _lastSell = prefix.Sequence; _sellTime = prefix.Time; }
            _anchors.Add((prefix.Id, "CloudPrefix", prefix.Profile, prefix.Time, volume, delta,
                prefix.RelativeThreshold > 0 ? volume / prefix.RelativeThreshold.Value : null));
        }
        private void Episode(ExplorerEpisode episode)
        {
            EpisodeCount++;
            if (_day == null || episode.StartTime.Date != _day.Date || episode.Reason == "EpisodeOpenAtEnd") { return; }
            _day = _day with { Episodes = _day.Episodes + 1 };
            _anchors.Add((episode.Id, "EpisodeClosed", episode.Profile, episode.Time, episode.Volume,
                episode.Volume == 0 ? 0 : (episode.Buy - episode.Sell) / episode.Volume,
                episode.RelativeThreshold > 0 ? episode.Volume / episode.RelativeThreshold.Value : null));
        }
        internal void Tick(OrderFlowDeal tick, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested(); _labels.Tick(tick);
            if (_day == null || _day.Date != tick.Time.Date)
            {
                if (_day != null) { _days.Add(_day); }
                _firstDate = _firstDate == default ? tick.Time.Date : _firstDate;
                _sourceDates.Add(tick.Time.Date);
                int historyDates = _run.Profiles.Max(p => Math.Max(p.VolumeDates, p.TimeOfDayDates + 1));
                while (_sourceDates.Count > historyDates) { _sourceDates.RemoveAt(0); }
                if (_sourceDates.Count > _run.MaximumBufferItems) { throw new System.IO.InvalidDataException("Превышен лимит дат фона; уменьшите окно истории."); }
                DateTime week = ExplorerPatternSpec.Week(tick.Time);
                if (_week != week)
                {
                    _days.Clear(); _week = week;
                    _incompleteWeek = _firstDate >= week && _firstDate.DayOfWeek != DayOfWeek.Monday;
                }
                _day = new ExplorerPatternDay(tick.Time.Date, 0, 0, tick.Price, tick.Price, tick.Price, tick.Price, 0, 0, 0);
                _notional = 0; _gridSlot = -1; _dayStart = tick.Time;
                _lastBuy = _lastSell = _lastLow = _lastHigh = 0; _buyTime = _sellTime = _lowTime = _highTime = null;
            }
            _metrics.Before(tick);
            _day = _day with { Buy = OrderFlowVolumeComparison.AddExact(_day.Buy, tick.Side == Side.Buy ? tick.Volume : 0),
                Sell = OrderFlowVolumeComparison.AddExact(_day.Sell, tick.Side == Side.Sell ? tick.Volume : 0), Close = tick.Price,
                High = Math.Max(_day.High, tick.Price), Low = Math.Min(_day.Low, tick.Price), Ticks = _day.Ticks + 1 };
            _notional = OrderFlowVolumeComparison.AddExact(_notional, ExplorerArithmetic.ProductExact(tick.Price, tick.Volume));
            _catalog.Add(tick, cancellation); _episodes.DateCutoff(tick);
            ExplorerPivot pivot = _swing.Add(tick, _metrics.Atr);
            if (pivot != null)
            {
                if (pivot.Kind == "Low") { _lastLow = tick.SourceSequence; _lowTime = tick.Time; }
                else { _lastHigh = tick.SourceSequence; _highTime = tick.Time; }
                _anchors.Add((_hash + "/pivot/" + tick.SourceSequence, "Pivot" + pivot.Kind, _run.Study.Profile, pivot.ObservedAt, 0, 0, null));
            }
            long slot = tick.Time.Ticks / (TimeSpan.TicksPerMinute * _plan.ControlMinutes);
            if (slot != _gridSlot) { _anchors.Add((_hash + "/clock/" + tick.SourceSequence, "ControlClock", "All", tick.Time, 0, 0, null)); _gridSlot = slot; }
            decimal dayVolume = _day.Buy + _day.Sell, weekBuy = _days.Sum(d => d.Buy) + _day.Buy, weekSell = _days.Sum(d => d.Sell) + _day.Sell;
            decimal? atr = _metrics.Atr;
            foreach ((string id, string kind, string profile, DateTime observed, decimal volume, decimal delta, decimal? relative) in _anchors)
            {
                DateTime history = _plan.Context == "Day" ? _dayStart : _days.Count == 0 ? _dayStart : _days[0].Date;
                if (relative.HasValue)
                {
                    ExplorerProfile formation = _run.Profiles.First(p => p.Key == profile);
                    int lookback = Math.Max(formation.RelativeVolume || kind == "EpisodeClosed" ? formation.VolumeDates : 1, formation.TimeOfDayVolume ? formation.TimeOfDayDates + 1 : 1);
                    DateTime background = _sourceDates[Math.Max(0, _sourceDates.Count - lookback)];
                    if (background < history) { history = background; }
                }
                ExplorerPatternSnapshot snapshot = new ExplorerPatternSnapshot
                {
                    Id = _hash + "/anchor/" + ++AnchorCount, EventId = id, Kind = kind, Profile = profile, ObservedAt = observed,
                    KnownAt = tick.Time, KnownSequence = tick.SourceSequence, Price = tick.Price, Atr = atr,
                    HistoryStart = history,
                    Activity = _metrics.CountPrevious(tick.Time, _run.Study.ActivitySeconds), DayDelta = (_day.Buy - _day.Sell) / dayVolume,
                    WeekDelta = (weekBuy - weekSell) / (weekBuy + weekSell), CloudDelta = delta, CloudVolume = volume, RelativeVolume = relative,
                    VwapDistance = atr.HasValue ? (tick.Price - _notional / dayVolume) / atr : null,
                    LowDistance = atr.HasValue ? (tick.Price - _day.Low) / atr : null, HighDistance = atr.HasValue ? (_day.High - tick.Price) / atr : null,
                    WeekStatus = _incompleteWeek ? "IncompleteWeek" : _days.Count == 0 ? "WeekEmpty" : "Available",
                    WeekDays = _days.Append(_day).ToImmutableArray(), DayClouds = _day.Clouds, DayEpisodes = _day.Episodes,
                    LastBuySequence = _lastBuy, LastSellSequence = _lastSell, LastLowSequence = _lastLow, LastHighSequence = _lastHigh,
                    LastBuyTime = _buyTime, LastSellTime = _sellTime, LastLowTime = _lowTime, LastHighTime = _highTime
                };
                _snapshot(snapshot); _labels.Add(snapshot);
            }
            _anchors.Clear(); _metrics.After(tick);
        }
        internal void Complete() { _labels.Complete(); }
    }

    /// <summary>Bounded horizon paths. Sampling is fixed before outcomes, shared by every rule and its controls.</summary>
    internal sealed class ExplorerPatternLabels
    {
        private sealed class Pending
        {
            internal ExplorerPatternSnapshot Snapshot;
            internal int Minutes, Direction;
            internal DateTime Until, LastTime;
            internal long LastSequence, Future;
            internal decimal Mfe, Mae, Return;
            internal string Hit = "Timeout";
            internal DateTime? HitAt;
            internal long? HitSequence;
        }
        private readonly ExplorerPatternSpec _spec;
        private readonly Action<ExplorerPatternRow> _sink;
        private readonly List<Pending> _pending = new List<Pending>();
        private readonly Dictionary<int, DateTime> _occupied = new Dictionary<int, DateTime>();
        private OrderFlowDeal _last;
        internal ExplorerPatternLabels(ExplorerPatternSpec spec, Action<ExplorerPatternRow> sink) { _spec = spec; _sink = sink; }
        internal void Add(ExplorerPatternSnapshot snapshot)
        {
            foreach (int minutes in _spec.HorizonsMinutes.Append(0))
            {
                DateTime until = minutes == 0 ? snapshot.KnownAt.Date.AddDays(1).AddTicks(-1) : snapshot.KnownAt.AddMinutes(minutes);
                string sampling = !snapshot.Atr.HasValue ? "NoAtr" : _occupied.TryGetValue(minutes, out DateTime previous) && snapshot.KnownAt <= previous ? "Overlap" : "Selected";
                if (sampling == "Selected") { _occupied[minutes] = until < snapshot.KnownAt.Date.AddDays(1) ? until : snapshot.KnownAt.Date.AddDays(1).AddTicks(-1); }
                foreach (int direction in _spec.Direction == "Long" ? new[] { 1 } : _spec.Direction == "Short" ? new[] { -1 } : new[] { 1, -1 })
                {
                    Pending pending = new Pending { Snapshot = snapshot, Direction = direction, Minutes = minutes, Until = until, LastTime = snapshot.KnownAt, LastSequence = snapshot.KnownSequence };
                    if (sampling != "Selected") { Finish(pending, sampling, snapshot.KnownSequence, sampling); }
                    else { _pending.Add(pending); }
                }
            }
        }
        internal void Tick(OrderFlowDeal tick)
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                Pending p = _pending[i];
                if (tick.Time.Date != p.Snapshot.KnownAt.Date || tick.Time > p.Until)
                {
                    string status = p.Minutes == 0 || p.Until < p.Snapshot.KnownAt.Date.AddDays(1) ? "Complete" : "DateCutoff";
                    Finish(p, status, tick.SourceSequence); _pending.RemoveAt(i); continue;
                }
                if (tick.SourceSequence <= p.Snapshot.KnownSequence) { continue; }
                p.Future++; p.LastTime = tick.Time; p.LastSequence = tick.SourceSequence;
                p.Return = p.Direction * (tick.Price - p.Snapshot.Price);
                decimal change = p.Return / p.Snapshot.Atr.Value;
                p.Mfe = Math.Max(p.Mfe, change); p.Mae = Math.Max(p.Mae, -change);
                if (p.Hit == "Timeout" && (change >= _spec.TargetAtr || change <= -_spec.AdverseAtr))
                { p.Hit = change >= _spec.TargetAtr ? "Target" : "Adverse"; p.HitAt = tick.Time; p.HitSequence = tick.SourceSequence; }
            }
            _last = tick;
        }
        internal void Complete()
        { foreach (Pending p in _pending) { Finish(p, p.Minutes > 0 && p.LastTime >= p.Until ? "Complete" : "Incomplete", _last?.SourceSequence ?? p.Snapshot.KnownSequence); } _pending.Clear(); }
        private void Finish(Pending p, string status, long proofSequence, string sampling = "Selected")
        {
            if (sampling == "Selected" && p.Future == 0) { status = "NoFutureTrade"; }
            DateTime end = status == "Complete" ? p.Until : p.LastTime;
            _sink(new ExplorerPatternRow(p.Snapshot, new ExplorerPatternLabel(p.Snapshot.Id, p.Direction > 0 ? "Long" : "Short", p.Minutes,
                end, proofSequence, status, p.Hit, p.HitAt, p.HitSequence, p.Future == 0 ? null : p.Mfe, p.Future == 0 ? null : p.Mae,
                p.Future == 0 ? null : p.Return, sampling)));
        }
    }
}
