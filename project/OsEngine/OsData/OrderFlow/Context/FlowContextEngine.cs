/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace OsEngine.OsData.OrderFlow.Context
{
    /// <summary>Single-consumer causal area/event engine fed by source-ordered ticks and closed-Cloud evidence.</summary>
    /// <remarks>
    /// A preparation pass supplies starts only for hidden moment recovery; a seed cannot influence visible evidence before KnownSequence.
    /// Rectangle, parent and event coordinates are frozen. Future market paths are separate objects; no orders or fill model.
    /// Resource limits reject the run. The explicit newest-active policy closes replaced areas without erasing their evidence. Contract: ORDER-FLOW-CONTEXT-001.
    /// </remarks>
    internal sealed class FlowContextEngine
    {
        #region State
        private sealed class Formation
        {
            internal FlowContextSeed First;
            internal FlowContextSeed Last;
            internal FlowContextMoments Moments;
            internal decimal Low;
            internal decimal High;
            internal decimal CloudVolume;
            internal decimal Notional;
            internal int Count;
            internal decimal RangeTicks;
            internal decimal PriorRange;
            internal decimal PriorChange;
        }
        private sealed class Active
        {
            internal FlowContextRegion Region;
            internal FlowContextMoments Moments;
            internal FlowContextSwing Swing;
            internal int Position;
            internal int PendingPosition;
            internal DateTime PendingSince;
            internal int Retests;
            internal int LastOutside;
            internal DateTime LastSample;

            internal bool BrokenHigh;
            internal bool BrokenLow;
        }
        private readonly FlowContextSettings _settings;
        private readonly FlowContextSeed[] _starts;
        private readonly FlowContextSeed[] _known;
        private int _startIndex;
        private int _knownIndex;
        private readonly Dictionary<FlowContextSeed, FlowContextMoments> _hidden = new Dictionary<FlowContextSeed, FlowContextMoments>();
        private readonly Dictionary<FlowContextSeed, (decimal Range, decimal Change)> _seedPrior = new Dictionary<FlowContextSeed, (decimal, decimal)>();
        private readonly Formation[] _forming = new Formation[3];
        private readonly List<Active> _active = new List<Active>();
        private readonly FlowContextOutcomes _outcomes;
        private readonly Dictionary<FlowManualAnchor, (FlowContextTick Tick, FlowContextMoments Moments)> _manual = new Dictionary<FlowManualAnchor, (FlowContextTick, FlowContextMoments)>();
        private readonly HashSet<FlowManualAnchor> _manualDone = new HashSet<FlowManualAnchor>();
        private readonly Queue<FlowTapeWindow> _baseline = new Queue<FlowTapeWindow>();
        private readonly Queue<FlowContextTick> _volatility = new Queue<FlowContextTick>();
        private readonly Dictionary<string, DateTime> _lastEvent = new Dictionary<string, DateTime>();
        private FlowTapeWindow _window;
        private FlowTapeWindow _previousWindow;
        private FlowContextTick _previous;
        private DateTime _sessionStart;
        private int _samples;
        public FlowContextResult Result { get; }

        public FlowContextEngine(FlowContextSettings settings, IEnumerable<FlowContextSeed> seeds)
        {
            _settings = settings.Copy(); _settings.Validate();
            FlowContextSeed[] array = seeds.ToArray();
            if (array.Length > _settings.MaximumRegions * 100L) { throw new InvalidDataException("Превышен лимит исходных клаудов."); }
            if (array.Any(seed => seed.FirstSequence > seed.KnownSequence || seed.Scale < 0 || seed.Scale > 2 || seed.Volume <= 0)) { throw new InvalidDataException("Некорректные причинные границы Cloud."); }
            _starts = array.OrderBy(seed => seed.FirstSequence).ThenByDescending(seed => seed.Scale).ToArray();
            _known = array.OrderBy(seed => seed.KnownSequence).ThenByDescending(seed => seed.Scale).ThenBy(seed => seed.FirstSequence).ToArray();
            Result = new FlowContextResult { Settings = _settings.Copy(), SettingsHash = _settings.Hash() };
            _outcomes = new FlowContextOutcomes(_settings, Result.Outcomes);
        }
        #endregion

        #region Tick processing
        public void Add(FlowContextTick tick, CancellationToken cancellation = default)
        {
            cancellation.ThrowIfCancellationRequested();
            if (tick.Price <= 0 || tick.Volume <= 0 || tick.Sequence <= Result.LastSequence || (_previous != null && tick.Time < _previous.Time)) { throw new InvalidDataException("Нарушен порядок или формат исходных сделок."); }
            Result.LastSequence = tick.Sequence;
            bool included = _settings.SessionIndex(tick.Time) >= 0;
            bool boundary = _previous != null && (tick.Time.Date != _previous.Time.Date || _settings.SessionIndex(tick.Time) != _settings.SessionIndex(_previous.Time));
            bool gap = _previous != null && !boundary && (tick.Time - _previous.Time).TotalSeconds > _settings.MaximumGapSeconds;
            _outcomes.Add(tick, cancellation);
            if (boundary || !included)
            {
                foreach (Active active in _active.ToArray()) { End(active, tick, "SessionEnd"); }
                Array.Clear(_forming); _window = null; _previousWindow = null; _baseline.Clear(); _volatility.Clear();
                _sessionStart = tick.Time;
            }
            if (!included) { Result.ExcludedTicks++; _previous = tick; return; }
            Result.TickCount++;
            if (_previous == null || boundary) { _sessionStart = tick.Time; }
            if (gap)
            {
                Result.GapCount++; _window = null; _previousWindow = null; _baseline.Clear(); _sessionStart = tick.Time; Array.Clear(_forming);
                foreach (Active active in _active) { active.PendingSince = tick.Time; active.PendingPosition = active.Position; }
            }
            foreach (Active active in _active.ToArray())
            {
                cancellation.ThrowIfCancellationRequested();
                if ((tick.Time - active.Region.KnownAt).TotalSeconds > _settings.Scales[active.Region.Scale].LifetimeSeconds) { End(active, tick, "Lifetime"); }
            }
            while (_startIndex < _starts.Length && _starts[_startIndex].FirstSequence <= tick.Sequence)
            {
                FlowContextSeed seed = _starts[_startIndex++];
                if (seed.FirstSequence != tick.Sequence) { throw new InvalidDataException("Якорь Cloud отсутствует в выбранной сессии."); }
                _hidden.Add(seed, new FlowContextMoments());
                _seedPrior.Add(seed, Prior(tick, _settings.Scales[seed.Scale].VolatilityLookbackSeconds));
            }
            foreach (FlowContextMoments moments in _hidden.Values) { moments.Add(tick, _settings); }
            foreach (Formation formation in _forming) { formation?.Moments.Add(tick, _settings); }
            foreach (Active active in _active) { active.Moments.Add(tick, _settings); }
            UpdateManual(tick);
            while (_knownIndex < _known.Length && _known[_knownIndex].KnownSequence <= tick.Sequence)
            {
                FlowContextSeed seed = _known[_knownIndex++];
                if (seed.KnownSequence != tick.Sequence || !_hidden.Remove(seed, out FlowContextMoments moments)) { throw new InvalidDataException("Нарушено сопоставление первого и второго проходов."); }
                AdmitSeed(seed, moments, tick);
                _seedPrior.Remove(seed);
            }
            foreach (Active active in _active.ToArray())
            {
                UpdateLifecycle(active, tick);
                if ((tick.Time - active.LastSample).TotalSeconds >= _settings.SampleSeconds) { Sample(active, tick); }
            }
            UpdateTape(tick);
            _volatility.Enqueue(tick);
            int maximumWindow = _settings.Scales.Max(scale => scale.VolatilityLookbackSeconds);
            while (_volatility.Count > 0 && (tick.Time - _volatility.Peek().Time).TotalSeconds > maximumWindow) { _volatility.Dequeue(); }
            if (_volatility.Count > 1000000 || _hidden.Count > 256) { throw new InvalidDataException("Превышен лимит активных расчётов. Увеличьте пороги событий или сократите период."); }
            _previous = tick;
        }

        /// <summary>Ends file consumption without inventing a market close, completing an open tape window, or satisfying a future horizon.</summary>
        public void Complete()
        {
            if (_previous == null) { return; }
            foreach (Active active in _active) { Sample(active, _previous); }
            _outcomes.Complete();
        }
        #endregion

        #region Areas
        private (decimal Range, decimal Change) Prior(FlowContextTick tick, int seconds)
        {
            FlowContextTick[] prior = _volatility.Where(item => (tick.Time - item.Time).TotalSeconds <= seconds).ToArray();
            return prior.Length == 0 ? (0, 0) : ((prior.Max(item => item.Price) - prior.Min(item => item.Price)) / _settings.PriceStep, (prior[prior.Length - 1].Price - prior[0].Price) / _settings.PriceStep);
        }
        private void AdmitSeed(FlowContextSeed seed, FlowContextMoments moments, FlowContextTick tick)
        {
            FlowContextScale rules = _settings.Scales[seed.Scale];
            if (!rules.Enabled || seed.Start.Date != tick.Time.Date || (tick.Time - seed.Start).TotalSeconds > rules.FormationSeconds) { _forming[seed.Scale] = null; return; }
            Formation formation = _forming[seed.Scale];
            if (formation != null && ((seed.Start - formation.Last.End).TotalSeconds > rules.CloudPauseSeconds || (tick.Time - formation.First.Start).TotalSeconds > rules.FormationSeconds || (Math.Max(seed.High, formation.High) - Math.Min(seed.Low, formation.Low)) / _settings.PriceStep > formation.RangeTicks))
            { formation = null; }
            if (formation == null)
            {
                (decimal range, decimal change) = _seedPrior[seed];
                formation = new Formation { First = seed, Moments = moments.Copy(), Low = seed.Low, High = seed.High, PriorRange = range, PriorChange = change,
                    RangeTicks = Math.Min(rules.MaximumAdaptiveRangeTicks, Math.Max(rules.RegionRangeTicks, range * rules.RegionVolatilityFactor)) };
            }
            formation.Last = seed; formation.Low = Math.Min(formation.Low, seed.Low); formation.High = Math.Max(formation.High, seed.High);
            formation.CloudVolume += seed.Volume; formation.Notional += seed.Notional; formation.Count++;
            _forming[seed.Scale] = formation;
            if (formation.Count < rules.MinimumClouds || formation.CloudVolume < rules.RegionVolume || (formation.High - formation.Low) / _settings.PriceStep > formation.RangeTicks) { return; }
            Confirm(seed.Scale, formation.First.Start, formation.First.FirstSequence, tick, formation.Low, formation.High, formation.Moments,
                formation.Count, formation.Notional / formation.CloudVolume, formation.PriorRange, formation.PriorChange, false);
            _forming[seed.Scale] = null;
        }
        private void UpdateManual(FlowContextTick tick)
        {
            foreach (FlowManualAnchor anchor in _settings.ManualAnchors.OrderByDescending(item => item.Scale))
            {
                if (_manualDone.Contains(anchor) || tick.Time < anchor.Start) { continue; }
                if (tick.Time.Date != anchor.Start.Date || _settings.SessionIndex(tick.Time) != _settings.SessionIndex(anchor.Start)) { _manualDone.Add(anchor); _manual.Remove(anchor); continue; }
                if (!_manual.TryGetValue(anchor, out (FlowContextTick Tick, FlowContextMoments Moments) state))
                { state = (tick, new FlowContextMoments()); _manual.Add(anchor, state); }
                state.Moments.Add(tick, _settings);
                if (tick.Time < anchor.KnownAt) { continue; }
                (decimal range, decimal change) = Prior(tick, _settings.Scales[anchor.Scale].VolatilityLookbackSeconds);
                Confirm(anchor.Scale, state.Tick.Time, state.Tick.Sequence, tick, anchor.Low, anchor.High, state.Moments, 0, state.Moments.Mean, range, change, true, _settings.ManualAnchors.IndexOf(anchor));
                _manual.Remove(anchor); _manualDone.Add(anchor);
            }
        }
        private void Confirm(int scale, DateTime start, long first, FlowContextTick tick, decimal low, decimal high, FlowContextMoments moments, int clouds, decimal cloudMean, decimal priorRange, decimal priorChange, bool manual, int manualIndex = -1)
        {
            if (Result.Regions.Count >= _settings.MaximumRegions) { throw new InvalidDataException("Превышен лимит областей. Сократите период или увеличьте пороги."); }
            Active parent = _active.Where(item => item.Region.Scale > scale && item.Region.Start <= start).OrderBy(item => item.Region.Scale).ThenByDescending(item => item.Region.KnownSequence).FirstOrDefault();
            FlowContextRegion region = new FlowContextRegion { Id = (manual ? "M" : "A") + scale + "/" + first + "/" + tick.Sequence + (manual ? "/" + manualIndex : ""), Scale = scale, Start = start, FirstSequence = first,
                KnownAt = tick.Time, KnownSequence = tick.Sequence, Low = low, High = high, InitialMean = moments.Mean, CloudMean = cloudMean, SourceVolume = moments.Volume,
                CloudCount = clouds, ParentId = parent?.Region.Id, Manual = manual, PriorRangeTicks = priorRange, PriorChangeTicks = priorChange };
            FlowContextScale rules = _settings.Scales[scale];
            while (_active.Count(item => item.Region.Scale == scale) >= _settings.RetainedActivePerScale) { End(_active.First(item => item.Region.Scale == scale), tick, "ReplacedByNewArea"); }
            Active active = new Active { Region = region, Moments = moments, Swing = new FlowContextSwing(Math.Max(rules.SwingTicks, priorRange * rules.SwingVolatilityFactor) * _settings.PriceStep), LastSample = tick.Time };
            _active.Add(active); Result.Regions.Add(region); Sample(active, tick);
            Emit("Область подтверждена", tick, 0, region.Id);
        }
        private void End(Active active, FlowContextTick tick, string reason)
        {
            active.Region.End = tick.Time; active.Region.EndSequence = tick.Sequence; active.Region.EndReason = reason;
            _active.Remove(active);
        }
        private void UpdateLifecycle(Active active, FlowContextTick tick)
        {
            FlowContextScale rules = _settings.Scales[active.Region.Scale];
            int position = tick.Price > active.Region.High + rules.BreakoutTicks * _settings.PriceStep ? 1 : tick.Price < active.Region.Low - rules.BreakoutTicks * _settings.PriceStep ? -1 : 0;
            if (position != active.PendingPosition) { active.PendingPosition = position; active.PendingSince = tick.Time; }
            if (position != active.Position && (tick.Time - active.PendingSince).TotalSeconds >= rules.HoldSeconds)
            {
                int old = active.Position; active.Position = position;
                if (position != 0) { active.LastOutside = position; Emit(position > 0 ? "Выход вверх" : "Выход вниз", tick, position, active.Region.Id); }
                else if (old != 0) { active.Retests++; Emit(old > 0 ? "Возврат сверху" : "Возврат снизу", tick, -old, active.Region.Id); }
                Sample(active, tick);
            }
            FlowContextTick pivot = active.Swing.Add(tick, out int direction);
            if (pivot != null)
            {
                active.BrokenHigh = false; active.BrokenLow = false;
                Emit(direction > 0 ? "Подтверждённый минимум" : "Подтверждённый максимум", tick, direction, active.Region.Id, observed: pivot);
                Sample(active, tick);
            }
            if (!active.BrokenHigh && active.Swing.LastHigh.HasValue && tick.Price > active.Swing.LastHigh + rules.BreakoutTicks * _settings.PriceStep)
            { active.BrokenHigh = true; Emit("Пробой структурного максимума", tick, 1, active.Region.Id); }
            if (!active.BrokenLow && active.Swing.LastLow.HasValue && tick.Price < active.Swing.LastLow - rules.BreakoutTicks * _settings.PriceStep)
            { active.BrokenLow = true; Emit("Пробой структурного минимума", tick, -1, active.Region.Id); }
        }
        private void Sample(Active active, FlowContextTick tick)
        {
            if (active.Region.Points.Count > 0 && active.Region.Points[active.Region.Points.Count - 1].Sequence == tick.Sequence) { active.Region.Points.RemoveAt(active.Region.Points.Count - 1); _samples--; }
            if (++_samples > _settings.MaximumSamples) { throw new InvalidDataException("Превышен лимит точек линий. Увеличьте интервал отрисовки или сократите период."); }
            FlowContextPoint point = new FlowContextPoint { Sequence = tick.Sequence, Time = tick.Time, Vwap = active.Moments.Mean, Twap = active.Moments.Twap, Sigma = active.Moments.Sigma,
                Volume = active.Moments.Volume, Delta = active.Moments.Delta, Position = active.Position, Retests = active.Retests, Structure = active.Swing.Structure, LastHigh = active.Swing.LastHigh, LastLow = active.Swing.LastLow };
            active.Region.Points.Add(point); active.LastSample = tick.Time;
        }
        #endregion

        #region Tape events and coordinates
        private void UpdateTape(FlowContextTick tick)
        {
            DateTime start = new DateTime(tick.Time.Ticks - tick.Time.Ticks % (TimeSpan.TicksPerSecond * _settings.WindowSeconds), tick.Time.Kind);
            if (_window != null && start != _window.Start)
            {
                FlowTapeWindow closed = _window;
                while (_baseline.Count > 0 && (closed.Start - _baseline.Peek().Start).TotalSeconds >= _settings.BaselineSeconds) { _baseline.Dequeue(); }
                decimal baselineTps = _baseline.Sum(item => (decimal)item.Count) / _settings.BaselineSeconds;
                decimal ratio = baselineTps > 0 ? closed.Count / (decimal)_settings.WindowSeconds / baselineTps : 0;
                (int count, int direction) = closed.Mode();
                decimal share = count / (decimal)closed.Count;
                if (closed.Count >= _settings.MinimumTrades && closed.Volume >= _settings.MinimumEventVolume)
                {
                    if (share >= _settings.SameSizeShare) { Emit("Одинаковые размеры", tick, direction, window: closed, share: share, tps: ratio); }
                    if ((closed.Start - _sessionStart).TotalSeconds >= _settings.BaselineSeconds && ratio >= _settings.TpsMultiplier) { Emit("Ускорение ленты", tick, Math.Sign(closed.Delta), window: closed, share: share, tps: ratio); }
                    int aggression = Math.Sign(closed.Delta);
                    if (aggression != 0 && Math.Abs(closed.Delta) / closed.Volume >= _settings.AggressionShare && aggression * (closed.Last.Price - closed.First.Price) / _settings.PriceStep <= _settings.MaximumProgressTicks)
                    { Emit("Поглощение — кандидат", tick, -aggression, window: closed, share: share, tps: ratio); }
                }
                if (_previousWindow != null && (closed.Start - _previousWindow.Start).TotalSeconds == _settings.WindowSeconds)
                {
                    if (_previousWindow.Sell >= _settings.MinimumEventVolume && closed.Sell > 0 && closed.Sell <= _previousWindow.Sell * _settings.ExhaustionRatio && closed.Low >= _previousWindow.Low)
                    { Emit("Угасание продаж — кандидат", tick, 1, window: closed, share: share, tps: ratio); }
                    if (_previousWindow.Buy >= _settings.MinimumEventVolume && closed.Buy > 0 && closed.Buy <= _previousWindow.Buy * _settings.ExhaustionRatio && closed.High <= _previousWindow.High)
                    { Emit("Угасание покупок — кандидат", tick, -1, window: closed, share: share, tps: ratio); }
                }
                _baseline.Enqueue(closed); _previousWindow = closed; _window = null;
            }
            _window ??= new FlowTapeWindow { Start = start };
            _window.Add(tick);
            if (_window.Count > 1000000) { throw new InvalidDataException("Слишком много сделок в локальном окне."); }
        }
        private void Emit(string kind, FlowContextTick tick, int direction, string region = null, FlowTapeWindow window = null, decimal share = 0, decimal tps = 0, FlowContextTick observed = null)
        {
            string key = kind + "/" + region;
            if (window != null && _lastEvent.TryGetValue(key, out DateTime previous) && (tick.Time - previous).TotalSeconds < _settings.EventCooldownSeconds) { return; }
            if (Result.Events.Count >= _settings.MaximumEvents) { throw new InvalidDataException("Превышен лимит событий. Увеличьте пороги или сократите период."); }
            FlowContextEvent item = new FlowContextEvent { Id = "E/" + tick.Sequence + "/" + Result.Events.Count, Kind = kind, RegionId = region, Sequence = tick.Sequence,
                Time = tick.Time, ObservedAt = observed?.Time ?? window?.Last.Time ?? tick.Time, ObservedPrice = observed?.Price ?? window?.Last.Price ?? tick.Price,
                Price = tick.Price, Direction = direction, Volume = window?.Volume ?? 0, Delta = window?.Delta ?? 0, Trades = window?.Count ?? 0, SameSizeShare = share, TpsRatio = tps,
                ProgressTicks = window == null ? 0 : (window.Last.Price - window.First.Price) / _settings.PriceStep, WarmedUp = (tick.Time - _sessionStart).TotalSeconds >= _settings.WarmupSeconds };
            Dictionary<int, Active> context = _active.GroupBy(value => value.Region.Scale).ToDictionary(group => group.Key, group => group.OrderByDescending(value => value.Region.KnownSequence).First());
            Active own = _active.FirstOrDefault(value => value.Region.Id == region);
            while (own != null)
            {
                context[own.Region.Scale] = own;
                own = _active.FirstOrDefault(value => value.Region.Id == own.Region.ParentId);
            }
            foreach (Active active in context.Values.OrderBy(value => value.Region.Scale))
            {
                FlowContextMoments moments = active.Moments;
                item.Coordinates.Add(new FlowContextCoordinate { RegionId = active.Region.Id, Scale = active.Region.Scale, VwapTicks = (tick.Price - moments.Mean) / _settings.PriceStep,
                    TwapTicks = moments.Twap.HasValue ? (tick.Price - moments.Twap) / _settings.PriceStep : null, SigmaDistance = moments.Sigma == 0 ? null : (tick.Price - moments.Mean) / moments.Sigma,
                    VwapTwapTicks = moments.Twap.HasValue ? (moments.Mean - moments.Twap) / _settings.PriceStep : null, AgeSeconds = (decimal)(tick.Time - active.Region.Start).TotalSeconds,
                    Volume = moments.Volume, LowDistanceTicks = (tick.Price - active.Region.Low) / _settings.PriceStep, HighDistanceTicks = (tick.Price - active.Region.High) / _settings.PriceStep,
                    Position = active.Position, Retests = active.Retests, Structure = active.Swing.Structure,
                    VwapSlopeTicksPerMinute = moments.SlopeTicksPerMinute(_settings.PriceStep) });
            }
            Result.Events.Add(item); _lastEvent[key] = tick.Time;
            if (direction == 0 || !item.WarmedUp) { return; }
            foreach (int horizon in _settings.HorizonsSeconds) { AddOutcome(item, tick.Time.AddSeconds(horizon), horizon.ToString(CultureInfo.InvariantCulture) + "s"); }
            if (_settings.EndOfDay) { AddOutcome(item, _settings.DayEnd(tick.Time), "EOD"); }
        }
        #endregion

        #region Future paths
        private void AddOutcome(FlowContextEvent item, DateTime due, string horizon) => _outcomes.Schedule(item, due, horizon);
        #endregion
    }
}
