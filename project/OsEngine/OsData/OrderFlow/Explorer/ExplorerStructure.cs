/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Generic;
using System.Linq;

namespace OsEngine.OsData.OrderFlow.Explorer
{
    /// <summary>Actual extremum and its later confirmation; source sequence distinguishes physical rows with identical timestamps.</summary>
    internal sealed record ExplorerPivot(string Kind, decimal Price, DateTime ObservedAt, long ObservedSequence,
        DateTime? KnownAt, long? KnownSequence, decimal ReversalPrice, string Status);
    /// <summary>First causal crossing of a frozen research threshold. Further prefixes never change this evidence.</summary>
    internal sealed record ExplorerTrigger(string Id, string VolumeId, string Profile, string Kind, DateTime Time, long Sequence,
        decimal Threshold, decimal Volume, decimal Buy, decimal Sell, decimal Price, decimal Low, decimal High, decimal Vwap, int Children)
    {
        public long FirstSequence => long.Parse(VolumeId.Substring(VolumeId.LastIndexOf('/') + 1), System.Globalization.CultureInfo.InvariantCulture);
    }
    /// <summary>Terminal watch, including watches without a breakout; no order, fill or position is implied.</summary>
    internal sealed record ExplorerObservation(string Id, string Direction, DateTime WatchStart, long WatchSequence,
        DateTime Time, long Sequence, string Group, string Status, ExplorerTrigger Trigger,
        ExplorerPivot H0, ExplorerPivot H1, ExplorerPivot L0, ExplorerPivot L1, ExplorerPivot Rebound, ExplorerPivot Turn,
        decimal Price, decimal? DiagnosticStop, decimal? Atr, decimal? WatchVwap, DateTime HistoryStart, int PreviousActivity);
    internal sealed record ExplorerDiagnostic(DateTime Time, long Sequence, string Id, string Status);
    internal sealed record ExplorerWatch(string Id, string Direction, DateTime WatchStart, long WatchSequence, string Group,
        ExplorerTrigger Trigger, ExplorerPivot H1, ExplorerPivot L1, ExplorerPivot Rebound, ExplorerPivot Turn, decimal Vwap);
    internal sealed record ExplorerWatchVwap(string WatchId, DateTime Time, long Sequence, decimal Vwap, decimal Sigma);

    /// <summary>Directional-change swing automaton over all raw trades. Each leg freezes its threshold and retains the first equal extreme.</summary>
    internal sealed class ExplorerSwing
    {
        private readonly ExplorerRunSpec _spec;
        private int _direction;
        private OrderFlowDeal _extreme;
        private decimal _reversal;
        private string _status;
        internal ExplorerPivot Provisional => _extreme == null ? null : new ExplorerPivot(_direction >= 0 ? "High" : "Low", _extreme.Price,
            _extreme.Time, _extreme.SourceSequence, null, null, _reversal, "Provisional/" + _status);
        internal ExplorerSwing(ExplorerRunSpec spec) { _spec = spec; }
        internal ExplorerPivot Add(OrderFlowDeal tick, decimal? atr)
        {
            if (_extreme == null || _extreme.Time.Date != tick.Time.Date)
            { _direction = 0; _extreme = tick; Freeze(atr); return null; }
            if (_direction == 0)
            {
                if (Math.Abs(tick.Price - _extreme.Price) < _reversal) { return null; }
                int direction = tick.Price > _extreme.Price ? 1 : -1;
                ExplorerPivot first = Confirm(direction > 0 ? "Low" : "High", tick);
                _direction = direction; _extreme = tick; Freeze(atr); return first;
            }
            if ((_direction > 0 && tick.Price > _extreme.Price) || (_direction < 0 && tick.Price < _extreme.Price))
            { _extreme = tick; return null; }
            if (_direction * (_extreme.Price - tick.Price) < _reversal) { return null; }
            ExplorerPivot pivot = Confirm(_direction > 0 ? "High" : "Low", tick);
            _direction = -_direction; _extreme = tick; Freeze(atr); return pivot;
        }
        private ExplorerPivot Confirm(string kind, OrderFlowDeal tick) => new ExplorerPivot(kind, _extreme.Price, _extreme.Time,
            _extreme.SourceSequence, tick.Time, tick.SourceSequence, _reversal, _status);
        private void Freeze(decimal? atr)
        {
            _reversal = (_spec.Study.AdaptSwing && atr.HasValue ? Math.Max(1, decimal.Ceiling(_spec.Study.SwingAtrFactor * atr.Value / _spec.PriceStep)) : _spec.Study.SwingReversalTicks) * _spec.PriceStep;
            _status = _spec.Study.AdaptSwing && !atr.HasValue ? "NoAtrBaseline" : "Confirmed";
        }
    }

    /// <summary>Paired Long/Short structural watches and control group, driven on one source-order queue.</summary>
    /// <remarks>
    /// Gate decisions are fixed at WatchStart using previous raw data. Terminal/breakout checks precede same-row arming.
    /// Optional VWAP uses the same watch anchor for both groups. Observations are research-only and cannot send orders.
    /// </remarks>
    internal sealed class ExplorerStructure
    {
        private sealed class Watch
        {
            internal string Id;
            internal int Direction;
            internal DateTime Start;
            internal long Sequence;
            internal ExplorerPivot H0, H1, L0, L1, Rebound, Turn;
            internal ExplorerTrigger Trigger;
            internal readonly ExplorerVwap Vwap = new ExplorerVwap();
            internal int Activity;
        }
        private readonly ExplorerRunSpec _spec;
        private readonly ExplorerSwing _swing;
        private readonly Action<ExplorerPivot> _pivot;
        private readonly Action<ExplorerObservation> _observation;
        private readonly Action<ExplorerDiagnostic> _diagnostic;
        private readonly List<ExplorerPivot> _highs = new List<ExplorerPivot>(), _lows = new List<ExplorerPivot>();
        private readonly Watch[] _watches = new Watch[2];
        private readonly string[] _usedContext = new string[2];
        private readonly Action<ExplorerWatchVwap> _vwapSample;
        private DateTime _date;
        internal ExplorerPivot Provisional => _swing.Provisional;
        internal IReadOnlyList<ExplorerWatch> Active => _watches.Where(w => w != null).Select(w => new ExplorerWatch(w.Id,
            w.Direction > 0 ? "WatchLong" : "WatchShort", w.Start, w.Sequence, w.Trigger == null ? "Control" : "VolumeArmed",
            w.Trigger, w.H1, w.L1, w.Rebound, w.Turn, w.Vwap.Mean)).ToArray();
        internal ExplorerStructure(ExplorerRunSpec spec, Action<ExplorerPivot> pivot, Action<ExplorerObservation> observation, Action<ExplorerDiagnostic> diagnostic,
            Action<ExplorerWatchVwap> vwapSample = null)
        { _spec = spec; _swing = new ExplorerSwing(spec); _pivot = pivot; _observation = observation; _diagnostic = diagnostic; _vwapSample = vwapSample; }

        internal void Add(OrderFlowDeal tick, ExplorerRawMetrics metrics, IReadOnlyList<ExplorerTrigger> triggers, bool relativeKnown)
        {
            if (_date != tick.Time.Date)
            {
                for (int i = 0; i < 2; i++) { if (_watches[i] != null) { End(i, tick, "DateCutoff", metrics.Atr); } }
                _date = tick.Time.Date; _highs.Clear(); _lows.Clear(); _usedContext[0] = _usedContext[1] = null;
            }
            ExplorerPivot pivot = _swing.Add(tick, metrics.Atr);
            if (pivot != null)
            {
                _pivot(pivot); List<ExplorerPivot> list = pivot.Kind == "High" ? _highs : _lows;
                list.Add(pivot); if (list.Count > 3) { list.RemoveAt(0); }
            }
            if (!_spec.Study.Enabled) { return; }
            for (int i = 0; i < 2; i++)
            {
                Watch watch = _watches[i];
                if (watch == null) { continue; }
                watch.Vwap.Add(tick.Price, tick.Volume);
                _vwapSample?.Invoke(new ExplorerWatchVwap(watch.Id, tick.Time, tick.SourceSequence, watch.Vwap.Mean, watch.Vwap.Sigma));
                if (tick.Time - watch.Start >= TimeSpan.FromMinutes(_spec.Study.WatchMinutes)) { End(i, tick, "Expired", metrics.Atr); continue; }
                if (pivot != null && pivot.KnownSequence > watch.Sequence && pivot.Kind == (watch.Direction > 0 ? "Low" : "High"))
                {
                    ExplorerPivot original = watch.Direction > 0 ? watch.L1 : watch.H1;
                    decimal change = watch.Direction * (pivot.Price - original.Price);
                    if (change < 0) { End(i, tick, "Invalidated", metrics.Atr); continue; }
                    if (change == 0) { End(i, tick, watch.Direction > 0 ? "EqualLow" : "EqualHigh", metrics.Atr); continue; }
                    List<ExplorerPivot> opposite = watch.Direction > 0 ? _highs : _lows;
                    ExplorerPivot rebound = opposite.LastOrDefault(p => p.ObservedSequence > original.ObservedSequence && p.ObservedSequence < pivot.ObservedSequence);
                    if (watch.Turn == null && rebound != null) { watch.Turn = pivot; watch.Rebound = rebound; }
                }
                if (watch.Turn != null && tick.SourceSequence > watch.Turn.KnownSequence &&
                    watch.Direction * (tick.Price - watch.Rebound.Price) > 0)
                {
                    string status = !_spec.Study.VwapGate ? "Breakout" : watch.Vwap.Weight == 0 ? "UnknownVWAP" :
                        watch.Direction * (tick.Price - watch.Vwap.Mean) > 0 ? "Breakout" : "VwapRejected";
                    End(i, tick, status, metrics.Atr);
                }
            }
            if (_highs.Count >= 2 && _lows.Count >= 2)
            {
                ExplorerPivot h0 = _highs[_highs.Count - 2], h1 = _highs[_highs.Count - 1], l0 = _lows[_lows.Count - 2], l1 = _lows[_lows.Count - 1];
                int direction = h0.Price > h1.Price && l0.Price > l1.Price ? 1 : h0.Price < h1.Price && l0.Price < l1.Price ? -1 : 0;
                if (direction != 0)
                {
                    int index = direction > 0 ? 0 : 1; string context = h1.KnownSequence + "/" + l1.KnownSequence;
                    if (_watches[index] == null && _usedContext[index] != context && Math.Max(h1.KnownSequence.Value, l1.KnownSequence.Value) == tick.SourceSequence)
                    {
                        _usedContext[index] = context;
                        Watch watch = new Watch { Id = _spec.StudyHash + "/" + tick.SourceSequence + "/" + (direction > 0 ? "Long" : "Short"),
                            Direction = direction, Start = tick.Time, Sequence = tick.SourceSequence, H0 = h0, H1 = h1, L0 = l0, L1 = l1,
                            Activity = metrics.CountPrevious(tick.Time, _spec.Study.ActivitySeconds) };
                        watch.Vwap.Add(tick.Price, tick.Volume); _watches[index] = watch;
                        _vwapSample?.Invoke(new ExplorerWatchVwap(watch.Id, tick.Time, tick.SourceSequence, watch.Vwap.Mean, watch.Vwap.Sigma));
                        if (tick.Time.Hour < _spec.Study.StartHour || tick.Time.Hour >= _spec.Study.EndHour) { End(index, tick, "HourExcluded", metrics.Atr); }
                        else if (_spec.Study.RelativeTrigger && !relativeKnown) { End(index, tick, "UnknownVolumeBaseline", metrics.Atr); }
                        else if (_spec.Study.ActivityGate && !metrics.ActivityKnown(tick.Time)) { End(index, tick, "UnknownActivity", metrics.Atr); }
                        else if (_spec.Study.ActivityGate && !metrics.ActivityPasses(tick.Time)) { End(index, tick, "Inactive", metrics.Atr); }
                    }
                }
            }
            foreach (ExplorerTrigger trigger in triggers)
            {
                bool assigned = false;
                for (int i = 0; i < 2; i++)
                {
                    Watch watch = _watches[i]; if (watch == null || trigger.Sequence < watch.Sequence) { continue; }
                    assigned = true;
                    if (watch.Trigger == null) { watch.Trigger = trigger; }
                    else { _diagnostic(new ExplorerDiagnostic(tick.Time, tick.SourceSequence, trigger.Id, "AdditionalEvidence")); }
                }
                if (!assigned) { _diagnostic(new ExplorerDiagnostic(tick.Time, tick.SourceSequence, trigger.Id, "NoTrendContext")); }
            }
        }
        internal void Complete(OrderFlowDeal last, decimal? atr)
        { if (last != null) { for (int i = 0; i < 2; i++) { if (_watches[i] != null) { End(i, last, "OpenAtEnd", atr); } } } }
        private void End(int index, OrderFlowDeal tick, string status, decimal? atr)
        {
            Watch watch = _watches[index];
            ExplorerTrigger trigger = watch.Trigger?.Sequence < tick.SourceSequence ? watch.Trigger : null;
            bool excluded = status == "HourExcluded" || status == "UnknownVolumeBaseline" || status == "UnknownActivity" || status == "Inactive";
            _observation(new ExplorerObservation(watch.Id, watch.Direction > 0 ? "Long" : "Short", watch.Start, watch.Sequence,
                tick.Time, tick.SourceSequence, excluded ? "Excluded" : trigger == null ? "Control" : "VolumeArmed", status, trigger,
                watch.H0, watch.H1, watch.L0, watch.L1, watch.Rebound, watch.Turn, tick.Price,
                status == "Breakout" ? watch.Turn.Price - watch.Direction * _spec.PriceStep : null,
                atr, watch.Vwap.Weight > 0 ? watch.Vwap.Mean : null,
                new[] { watch.H0.ObservedAt, watch.L0.ObservedAt }.Min(), watch.Activity));
            _watches[index] = null;
        }
    }
}
