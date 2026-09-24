/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.Entity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace OsEngine.OsData.OrderFlow.Explorer
{
    /// <summary>Single-owner streaming full catalog. All nonempty chains survive, independently of research or display thresholds.</summary>
    /// <remarks>
    /// ORDER-FLOW-CLOUD-EXPLORER-V2-001. No legacy accumulator, identities or settings are mutated.
    /// Profiles freeze at start; source-date cutoff is explicit. Sinks must consume immutable evidence synchronously.
    /// Cancellation/arithmetic errors propagate to the run owner, which must not publish an unfinished bundle.
    /// </remarks>
    internal sealed class ExplorerCatalog
    {
        private readonly ExplorerRunSpec _spec;
        private readonly ExplorerRawMetrics _metrics;
        private readonly List<Layer> _layers = new List<Layer>();
        internal ExplorerRawMetrics Metrics => _metrics;
        internal long CompletedCount { get; private set; }
        internal IReadOnlyList<ExplorerCloud> Forming => _layers.Where(l => l.Current != null).Select(l => l.Current).ToArray();

        internal ExplorerCatalog(ExplorerRunSpec spec, Action<ExplorerCloud> completed, Action<ExplorerPrefix> prefix)
        {
            _spec = spec; _metrics = new ExplorerRawMetrics(spec);
            foreach (ExplorerProfile profile in spec.Profiles)
            { _layers.Add(new Layer(spec, profile, _metrics, c => { CompletedCount++; completed(c); }, prefix)); }
        }
        internal void Add(OrderFlowDeal tick, CancellationToken cancellation = default)
        {
            bool newDate = _metrics.Date != default && _metrics.Date != tick.Time.Date;
            if (newDate) { foreach (Layer layer in _layers) { layer.Finish(tick, "DateCutoff"); } }
            _metrics.Before(tick);
            foreach (Layer layer in _layers) { layer.Add(tick, cancellation); }
            _metrics.After(tick);
        }
        internal void Complete()
        {
            foreach (Layer layer in _layers) { layer.Finish(null, "OpenAtEnd"); }
        }

        private sealed class Layer
        {
            private readonly ExplorerRunSpec _spec;
            private readonly ExplorerProfile _profile;
            private readonly ExplorerRawMetrics _metrics;
            private readonly Action<ExplorerCloud> _completed;
            private readonly Action<ExplorerPrefix> _prefix;
            private readonly ExplorerVolumeBaseline _volumes;
            private readonly Queue<decimal> _ticks = new Queue<decimal>();
            private readonly ExplorerDistribution _tickValues = new ExplorerDistribution();
            private OrderFlowImbalanceWindow _context;
            private OrderFlowImbalanceProfile _inside;
            private int _date;
            private int _contextRows;
            private readonly Queue<DateTime> _contextTimes = new Queue<DateTime>();
            private readonly OrderFlowImbalanceSettings _imbalance;
            private readonly string _idPrefix;
            internal ExplorerCloud Current { get; private set; }

            #region Profile formation

            internal Layer(ExplorerRunSpec spec, ExplorerProfile profile, ExplorerRawMetrics metrics, Action<ExplorerCloud> completed, Action<ExplorerPrefix> prefix)
            {
                _spec = spec; _profile = profile; _metrics = metrics; _completed = completed; _prefix = prefix;
                _volumes = new ExplorerVolumeBaseline(profile, spec.MaximumBufferItems);
                _imbalance = new OrderFlowImbalanceSettings { ContextSeconds = profile.ContextSeconds };
                _idPrefix = spec.CatalogSpecHash + "/" + profile.Key + "/";
            }
            internal void Add(OrderFlowDeal tick, CancellationToken cancellation)
            {
                if (_date != _metrics.DateOrdinal)
                {
                    _date = _metrics.DateOrdinal; _context = new OrderFlowImbalanceWindow(_spec.PriceStep, _imbalance);
                    while (_ticks.Count > 0) { _tickValues.Remove(_ticks.Dequeue()); }
                    _contextTimes.Clear(); _contextRows = 0;
                }
                _volumes.Advance(_date);
                while (_contextTimes.Count > 0 && tick.Time - _contextTimes.Peek() > TimeSpan.FromSeconds(_profile.ContextSeconds))
                { _contextTimes.Dequeue(); _contextRows--; }
                if (++_contextRows > _spec.MaximumBufferItems) { throw new System.IO.InvalidDataException("Explorer context window limit exceeded."); }
                _contextTimes.Enqueue(tick.Time); _context.Add(tick, cancellation);
                decimal threshold = Current?.Effective.TickVolume ?? TickThreshold();
                if (_profile.AllTicks || tick.Volume >= threshold)
                {
                    if (Current != null)
                    {
                        bool gap = tick.Time.Ticks - Current.Time.Ticks > (long)Current.Effective.GapMilliseconds * TimeSpan.TicksPerMillisecond;
                        bool range = (Math.Max(Current.High, tick.Price) - Math.Min(Current.Low, tick.Price)) / _spec.PriceStep > Current.Effective.RangeTicks;
                        if (gap || range) { Finish(tick, gap ? "Gap" : "Range"); }
                    }
                    if (Current == null && (_profile.AllTicks || tick.Volume >= TickThreshold())) { Start(tick); }
                    if (Current != null)
                    {
                        _inside.Change(tick);
                        if (_inside.Snapshot().Pairs.Count > _spec.MaximumBufferItems) { throw new System.IO.InvalidDataException("Explorer price profile limit exceeded."); }
                        decimal buy = Current.Buy, sell = Current.Sell;
                        if (tick.Side == Side.Buy) { buy = OrderFlowVolumeComparison.AddExact(buy, tick.Volume); }
                        else { sell = OrderFlowVolumeComparison.AddExact(sell, tick.Volume); }
                        Current = Current with { LastSequence = tick.SourceSequence, Time = tick.Time, Price = tick.Price,
                            Buy = buy, Sell = sell, Count = checked(Current.Count + 1), Low = Math.Min(Current.Low, tick.Price), High = Math.Max(Current.High, tick.Price),
                            Notional = OrderFlowVolumeComparison.AddExact(Current.Notional, ExplorerArithmetic.ProductExact(tick.Price, tick.Volume)),
                            Inside = _inside.Snapshot(), Context = _context.Snapshot(), RawVolumeThrough = OrderFlowVolumeComparison.AddExact(_metrics.RawVolume, tick.Volume) };
                        _prefix(ToPrefix(Current, false));
                        if (_profile.SingleTicks) { Finish(tick, "SingleTick"); }
                    }
                }
                _ticks.Enqueue(tick.Volume); _tickValues.Add(tick.Volume);
                if (_ticks.Count > _profile.TickWindow) { _tickValues.Remove(_ticks.Dequeue()); }
            }
            private decimal TickThreshold() => _profile.AdaptTick && _ticks.Count >= _profile.TickMinimum ? _tickValues.Quantile(_profile.TickPercentile) : _profile.MinimumTickVolume;
            private void Start(OrderFlowDeal tick)
            {
                List<string> status = new List<string>();
                if (_profile.AdaptTick && _ticks.Count < _profile.TickMinimum) { status.Add("NoTickBaseline"); }
                int pace = _metrics.CountPrevious(tick.Time, _profile.PaceSeconds);
                int gap = _profile.MaximumGapMilliseconds, range = _profile.MaximumRangeTicks;
                if (_profile.AdaptGap)
                {
                    if (pace >= _profile.PaceMinimum) { gap = (int)Math.Clamp(decimal.Ceiling(_profile.PaceFactor * _profile.PaceSeconds * 1000m / pace), _profile.GapMinimum, _profile.GapMaximum); }
                    else { status.Add("NoPaceBaseline"); }
                }
                if (_profile.AdaptRange)
                {
                    if (_metrics.Atr.HasValue) { range = (int)Math.Clamp(decimal.Ceiling(_profile.AtrFactor * _metrics.Atr.Value / _spec.PriceStep), _profile.RangeMinimum, _profile.RangeMaximum); }
                    else { status.Add("NoAtrBaseline"); }
                }
                decimal? rolling = _volumes.Rolling(tick.SourceSequence), clock = _volumes.TimeOfDay(tick.Time, tick.SourceSequence);
                decimal? relative = _profile.TimeOfDayVolume ? clock : rolling;
                if (!rolling.HasValue) { status.Add("NoVolumeBaseline"); }
                if (_profile.TimeOfDayVolume && !clock.HasValue) { status.Add("NoTimeOfDayBaseline"); }
                _inside = new OrderFlowImbalanceProfile(_spec.PriceStep, _imbalance);
                Current = new ExplorerCloud { Id = _idPrefix + tick.SourceSequence, Profile = _profile.Key, FirstSequence = tick.SourceSequence,
                    StartTime = tick.Time, Time = tick.Time, Low = tick.Price, High = tick.Price, FirstPrice = tick.Price, RawVolumeBefore = _metrics.RawVolume,
                    Effective = new ExplorerEffective(_profile.AllTicks ? 0 : TickThreshold(), gap, range, _metrics.Atr, rolling, clock,
                        _profile.RelativeVolume || _profile.TimeOfDayVolume ? relative : null, string.Join(",", status)) };
            }
            #endregion

            #region Immutable completion

            internal void Finish(OrderFlowDeal closing, string reason)
            {
                if (Current == null) { return; }
                ExplorerCloud cloud = Current with { KnownAt = closing?.Time, KnownSequence = closing?.SourceSequence, Reason = reason,
                    RelativePercentile = _profile.TimeOfDayVolume ? _volumes.TimeOfDayPercentile(Current.StartTime, Current.Volume, Current.FirstSequence) : _volumes.Percentile(Current.Volume, Current.FirstSequence) };
                _completed(cloud);
                _prefix(ToPrefix(cloud, true) with { Sequence = closing?.SourceSequence ?? long.MaxValue, Time = closing?.Time ?? cloud.Time });
                _volumes.Add(cloud, _date); Current = null;
            }
            private static ExplorerPrefix ToPrefix(ExplorerCloud cloud, bool complete) => new ExplorerPrefix(cloud.Id, cloud.Profile, cloud.FirstSequence,
                cloud.LastSequence, cloud.Time, cloud.Price, cloud.Low, cloud.High, cloud.Buy, cloud.Sell, cloud.Vwap, cloud.Count,
                cloud.Effective.RelativeThreshold, complete, cloud.Reason);
            #endregion
        }
    }
}
