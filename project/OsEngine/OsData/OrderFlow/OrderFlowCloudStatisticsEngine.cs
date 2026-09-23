/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace OsEngine.OsData.OrderFlow
{
    /// <summary>Separate verified-file pass for completed-Cloud reactions; never rebuilds Clouds or modifies the original result.</summary>
    /// <remarks>
    /// A caller worker owns one synchronous run. Causal arithmetic ATR uses completed Min1 bars, completion ordinal supplies the actual reference price.
    /// A fixed non-overlapping sample precedes filter search; day split purges feature and label overlap. No orders, fees, fills or live qualification.
    /// Results publish only after full input validation. Contract: ORDER-FLOW-RESEARCH-001 and ORDER-FLOW-MVP-RUNBOOK-001.
    /// </remarks>
    internal sealed partial class OrderFlowCloudStatisticsEngine
    {
        #region Calculation boundary

        /// <summary>Measures a completed accepted result against its original pinned input. Invalid settings, changed input, arithmetic errors and cancellation propagate without a partial result.</summary>
        public OrderFlowCloudStatisticsResult Run(OrderFlowResearchRequest request, OrderFlowResearchResult source,
            OrderFlowCloudStatisticsSettings parameters, CancellationToken cancellationToken)
        {
            if (request == null || source?.Quality.ResearchAccepted != true || source.Input?.ReadComplete != true ||
                (!source.CloudCalculated && !source.Cloud2Calculated))
            { throw new ArgumentException("A completed accepted Cloud calculation is required for statistics."); }
            OrderFlowCloudStatisticsSettings settings = parameters.CopyValidated();
            cancellationToken.ThrowIfCancellationRequested();
            OrderFlowCloudStatisticsResult result = new OrderFlowCloudStatisticsResult { Settings = settings,
                InputSha256 = source.Input.Sha256, SourceSpecHash = source.ResearchSpecHash };
            result.StudyHash = OrderFlowCanonicalHash.Calculate(settings.CanonicalValue() + "|" + source.InputHash + "|" + source.ResearchSpecHash + "|" + source.CloudHash + "|" + source.Cloud2Hash);
            BuildEvents(request, source, result, cancellationToken);
            ReadReactions(request, result, cancellationToken);
            result.SkippedIncomplete = result.Events.Count(item => item.Sampled && !item.Complete);
            BuildRecommendations(result, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }

        private static void BuildEvents(OrderFlowResearchRequest request, OrderFlowResearchResult source,
            OrderFlowCloudStatisticsResult result, CancellationToken cancellation)
        {
            List<OrderFlowDisplayBar> bars = source.Bars[OrderFlowDisplayTimeFrame.Min1].Where(bar => bar.HasTrades).ToList();
            DateTime[] days = bars.Select(bar => bar.TimeStart.Date).Distinct().OrderBy(day => day).ToArray();
            result.ObservedDays = days.Length;
            result.FitDays = days.Length < 2 ? days.Length : Math.Clamp(days.Length * result.Settings.FitPercent / 100, 1, days.Length - 1);
            result.SplitDate = days.Length < 2 ? null : days[result.FitDays];
            decimal[] atr = AtrSeries(bars, result.Settings.AtrPeriod, cancellation);
            int longest = result.Settings.HorizonsMinutes.Max();
            foreach ((int layer, List<OrderFlowCloud> clouds, int contextSeconds) in new[] {
                (1, source.Clouds, request.Cloud?.Imbalance.ContextSeconds ?? 30), (2, source.Clouds2, request.Cloud2?.Imbalance.ContextSeconds ?? 30) })
            {
                DateTime? sampledEnd = null;
                foreach (OrderFlowCloud cloud in clouds.OrderBy(item => item.CompletionSourceSequence ?? long.MaxValue))
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (!cloud.CompletedAt.HasValue || !cloud.CompletionSourceSequence.HasValue) { result.SkippedUnfinished++; continue; }
                    if (cloud.Delta == 0) { result.SkippedNeutral++; continue; }
                    int index = LastCompletedBar(bars, cloud.CompletedAt.Value);
                    if (index < result.Settings.AtrPeriod || atr[index] <= 0) { result.SkippedAtr++; continue; }
                    DateTime contextFrom = new DateTime(Math.Max(0, cloud.Time.Ticks - (long)contextSeconds * TimeSpan.TicksPerSecond));
                    DateTime featureStart = new[] { cloud.StartTime, contextFrom, bars[index - result.Settings.AtrPeriod].TimeStart }.Min();
                    DateTime end = cloud.CompletedAt.Value.AddMinutes(longest);
                    OrderFlowCloudSamplePart part = !result.SplitDate.HasValue || end < result.SplitDate.Value ? OrderFlowCloudSamplePart.Fit
                        : featureStart >= result.SplitDate.Value ? OrderFlowCloudSamplePart.Test : OrderFlowCloudSamplePart.Purged;
                    bool sampled = !sampledEnd.HasValue || cloud.CompletedAt.Value > sampledEnd.Value;
                    if (sampled) { sampledEnd = end; } else { result.SkippedOverlap++; }
                    if (sampled && part == OrderFlowCloudSamplePart.Purged) { result.Purged++; }
                    result.Events.Add(new OrderFlowCloudStudyEvent { Cloud = cloud, Layer = layer, Atr = atr[index],
                        FeatureStart = featureStart, Part = part, Sampled = sampled });
                }
            }
            result.Events.Sort((a, b) => { int order = a.SourceSequence.CompareTo(b.SourceSequence); return order == 0 ? a.Layer.CompareTo(b.Layer) : order; });
        }

        /// <summary>Arithmetic mean of the last period true ranges, requiring a real previous close; gaps use the preceding observed minute.</summary>
        internal static decimal[] AtrSeries(List<OrderFlowDisplayBar> bars, int period, CancellationToken cancellation = default)
        {
            decimal[] atr = new decimal[bars.Count]; decimal[] ranges = new decimal[bars.Count]; decimal sum = 0;
            for (int i = 1; i < bars.Count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                ranges[i] = Math.Max(bars[i].High - bars[i].Low, Math.Max(Math.Abs(bars[i].High - bars[i - 1].Close), Math.Abs(bars[i].Low - bars[i - 1].Close)));
                sum += ranges[i];
                if (i > period) { sum -= ranges[i - period]; }
                if (i >= period) { atr[i] = sum / period; }
            }
            return atr;
        }

        private static int LastCompletedBar(List<OrderFlowDisplayBar> bars, DateTime time)
        {
            int low = 0; int high = bars.Count;
            while (low < high) { int mid = low + (high - low) / 2; if (bars[mid].TimeEnd <= time) { low = mid + 1; } else { high = mid; } }
            return low - 1;
        }

        #endregion

        #region Verified tick path

        private static void ReadReactions(OrderFlowResearchRequest request, OrderFlowCloudStatisticsResult result, CancellationToken cancellation)
        {
            OrderFlowTickInput metadata = new OrderFlowTickInput();
            using OrderFlowTickReader reader = new OrderFlowTickReader(request.TicksFilePath, metadata, cancellation);
            if (!string.Equals(metadata.Sha256, result.InputSha256, StringComparison.Ordinal))
            { throw new InvalidDataException("The tick file changed since Cloud calculation. Run the original calculation again."); }
            List<ReactionTracker> active = new List<ReactionTracker>(); int next = 0; DateTime? lastTime = null;
            while (reader.TryRead(out OrderFlowDeal tick))
            {
                cancellation.ThrowIfCancellationRequested();
                if (request.FromDate.HasValue && (tick.Time.Date < request.FromDate.Value || tick.Time.Date > request.ToDate.Value)) { continue; }
                lastTime = tick.Time;
                for (int i = active.Count - 1; i >= 0; i--)
                {
                    if (tick.Time > active[i].End) { active[i].Finish(true); active.RemoveAt(i); }
                    else { active[i].Apply(tick); }
                }
                while (next < result.Events.Count && result.Events[next].SourceSequence <= tick.SourceSequence)
                {
                    OrderFlowCloudStudyEvent item = result.Events[next++];
                    if (item.SourceSequence != tick.SourceSequence || item.Time != tick.Time)
                    { throw new InvalidDataException("Cloud completion does not match the verified tick prefix."); }
                    item.ReferencePrice = tick.Price;
                    if (!item.Sampled) { continue; }
                    foreach (int horizon in result.Settings.HorizonsMinutes)
                    foreach (bool reversal in new[] { false, true })
                    { active.Add(new ReactionTracker(item, horizon, reversal, result.Settings)); }
                }
            }
            if (!metadata.ReadComplete || next != result.Events.Count) { throw new InvalidDataException("The verified file did not contain all Cloud completion events."); }
            foreach (ReactionTracker tracker in active) { tracker.Finish(lastTime.HasValue && lastTime.Value >= tracker.End); }
        }

        private sealed class ReactionTracker
        {
            private readonly OrderFlowCloudStudyEvent _event;
            private readonly OrderFlowCloudReaction _reaction;
            private readonly int _side;
            private readonly decimal _target;
            private readonly decimal _adverse;
            private decimal _mfe;
            private decimal _mae;
            private decimal _last;
            public DateTime End { get; }

            public ReactionTracker(OrderFlowCloudStudyEvent item, int horizon, bool reversal, OrderFlowCloudStatisticsSettings settings)
            {
                _event = item; _side = item.ContinuationSide * (reversal ? -1 : 1);
                _target = settings.TargetAtr * item.Atr; _adverse = settings.AdverseAtr * item.Atr;
                if (_target <= 0 || _adverse <= 0)
                { throw new InvalidDataException("The ATR target or adverse distance is smaller than decimal precision. Increase the positive ATR multiplier."); }
                End = item.Time.AddMinutes(horizon);
                _reaction = new OrderFlowCloudReaction { HorizonMinutes = horizon, Reversal = reversal };
                item.Reactions.Add(_reaction);
            }

            public void Apply(OrderFlowDeal tick)
            {
                if (tick.SourceSequence <= _event.SourceSequence) { return; }
                decimal movement = (tick.Price - _event.ReferencePrice) * _side;
                _last = movement; _mfe = Math.Max(_mfe, movement); _mae = Math.Max(_mae, -movement);
                _reaction.FutureTrades++;
                if (!_reaction.FirstTouchSequence.HasValue && (movement >= _target || movement <= -_adverse))
                {
                    _reaction.Outcome = movement >= _target ? OrderFlowCloudReactionOutcome.TargetFirst : OrderFlowCloudReactionOutcome.AdverseFirst;
                    _reaction.FirstTouchSequence = tick.SourceSequence;
                    _reaction.FirstTouchSeconds = (tick.Time.Ticks - _event.Time.Ticks) / (decimal)TimeSpan.TicksPerSecond;
                }
            }

            public void Finish(bool complete)
            {
                _reaction.IsComplete = complete;
                _reaction.MfeAtr = _mfe / _event.Atr; _reaction.MaeAtr = _mae / _event.Atr; _reaction.EndReturnAtr = _last / _event.Atr;
            }
        }

        #endregion
    }
}
