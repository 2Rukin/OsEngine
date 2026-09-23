/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace OsEngine.OsData.OrderFlow
{
    internal sealed partial class OrderFlowCloudStatisticsEngine
    {
        #region Fixed fit-only rule search

        private static void BuildRecommendations(OrderFlowCloudStatisticsResult result, CancellationToken cancellation)
        {
            foreach (int layer in new[] { 1, 2 })
            {
                List<OrderFlowCloudStudyEvent> all = result.Events.Where(item => item.Layer == layer).ToList();
                List<OrderFlowCloudStudyEvent> sample = all.Where(item => item.Complete && item.Part != OrderFlowCloudSamplePart.Purged).ToList();
                List<OrderFlowCloudStudyEvent> fit = sample.Where(item => item.Part == OrderFlowCloudSamplePart.Fit).ToList();
                if (fit.Count == 0) { continue; }
                decimal low = Quantile(fit.Select(item => item.Atr), 1, 3); decimal high = Quantile(fit.Select(item => item.Atr), 2, 3);
                List<OrderFlowCloudStudyRule> rules = BuildRules(fit, cancellation);
                Dictionary<OrderFlowCloudStudyRule, HashSet<string>> passed = new Dictionary<OrderFlowCloudStudyRule, HashSet<string>>();
                foreach (OrderFlowCloudStudyRule rule in rules)
                {
                    OrderFlowCloudFilter filter = rule.CreateFilter(); HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
                    foreach (OrderFlowCloudStudyEvent item in sample) { cancellation.ThrowIfCancellationRequested(); if (filter.Passes(item.Cloud)) { ids.Add(item.CloudId); } }
                    passed.Add(rule, ids);
                }
                foreach (string regime in new[] { "All / Все", "Low / Низкая", "Middle / Средняя", "High / Высокая" })
                foreach (int horizon in result.Settings.HorizonsMinutes)
                foreach (bool reversal in new[] { false, true })
                {
                    cancellation.ThrowIfCancellationRequested();
                    List<OrderFlowCloudStudyEvent> cohort = sample.Where(item => InRegime(item.Atr, regime, low, high)).ToList();
                    List<OrderFlowCloudStudyEvent> training = cohort.Where(item => item.Part == OrderFlowCloudSamplePart.Fit).ToList();
                    List<OrderFlowCloudStudyEvent> testing = cohort.Where(item => item.Part == OrderFlowCloudSamplePart.Test).ToList();
                    if (training.Count == 0) { continue; }
                    OrderFlowCloudStudyRule best = rules[0];
                    OrderFlowCloudStudyMetrics baselineFit = Metrics(training, horizon, reversal);
                    OrderFlowCloudStudyMetrics bestFit = baselineFit;
                    foreach (OrderFlowCloudStudyRule rule in rules.Skip(1))
                    {
                        cancellation.ThrowIfCancellationRequested();
                        OrderFlowCloudStudyMetrics metrics = Metrics(training.Where(item => passed[rule].Contains(item.CloudId)), horizon, reversal);
                        if (metrics.Count >= result.Settings.MinimumSamples && (metrics.LowerPercent > bestFit.LowerPercent ||
                            (metrics.LowerPercent == bestFit.LowerPercent && metrics.Count > bestFit.Count)))
                        { best = rule; bestFit = metrics; }
                    }
                    // Selection is complete before any held-out metrics are consulted.
                    OrderFlowCloudStudyMetrics bestTest = Metrics(testing.Where(item => passed[best].Contains(item.CloudId)), horizon, reversal);
                    OrderFlowCloudFilter bestFilter = best.CreateFilter();
                    List<OrderFlowCloudStudyEvent> matching = new List<OrderFlowCloudStudyEvent>();
                    foreach (OrderFlowCloudStudyEvent item in all)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (InRegime(item.Atr, regime, low, high) && bestFilter.Passes(item.Cloud)) { matching.Add(item); }
                    }
                    result.Recommendations.Add(new OrderFlowCloudRecommendation { Layer = layer, Regime = regime,
                        RegimeLower = regime.StartsWith("Middle", StringComparison.Ordinal) ? low : regime.StartsWith("High", StringComparison.Ordinal) ? high : null,
                        RegimeUpper = regime.StartsWith("Low", StringComparison.Ordinal) ? low : regime.StartsWith("Middle", StringComparison.Ordinal) ? high : null,
                        HorizonMinutes = horizon, Reversal = reversal, Rule = best, TriedRules = rules.Count,
                        Fit = bestFit, Test = bestTest, FitBaseline = baselineFit, TestBaseline = Metrics(testing, horizon, reversal),
                        SelectedImprovement = best != rules[0] && bestFit.LowerPercent > baselineFit.LowerPercent,
                        EnoughSamples = bestFit.Count >= result.Settings.MinimumSamples && bestTest.Count >= result.Settings.MinimumSamples,
                        MatchingCloudIds = matching.Select(item => item.CloudId).ToList(),
                        FocusCloudId = (matching.FirstOrDefault(item => item.Part == OrderFlowCloudSamplePart.Test) ?? matching.FirstOrDefault())?.CloudId });
                }
            }
        }

        private static bool InRegime(decimal atr, string regime, decimal low, decimal high)
        {
            if (regime.StartsWith("Low", StringComparison.Ordinal)) { return atr <= low; }
            if (regime.StartsWith("Middle", StringComparison.Ordinal)) { return atr > low && atr <= high; }
            if (regime.StartsWith("High", StringComparison.Ordinal)) { return atr > high; }
            return true;
        }

        private static OrderFlowCloudStudyMetrics Metrics(IEnumerable<OrderFlowCloudStudyEvent> cohort, int horizon, bool reversal)
        {
            List<OrderFlowCloudReaction> reactions = cohort.Select(item => item.Reactions.Single(reaction => reaction.HorizonMinutes == horizon && reaction.Reversal == reversal)).ToList();
            return new OrderFlowCloudStudyMetrics { Count = reactions.Count,
                Wins = reactions.Count(item => item.Outcome == OrderFlowCloudReactionOutcome.TargetFirst),
                MeanMfeAtr = reactions.Count == 0 ? 0 : reactions.Sum(item => item.MfeAtr / reactions.Count),
                MeanMaeAtr = reactions.Count == 0 ? 0 : reactions.Sum(item => item.MaeAtr / reactions.Count) };
        }

        private static List<OrderFlowCloudStudyRule> BuildRules(List<OrderFlowCloudStudyEvent> fit, CancellationToken cancellation)
        {
            List<OrderFlowCloudStudyRule> rules = new List<OrderFlowCloudStudyRule> { new OrderFlowCloudStudyRule { Id = "Baseline / Без фильтра", Source = OrderFlowImbalanceSource.Off } };
            foreach (int count in new[] { 1, 2, 3, 5, 10 })
            { rules.Add(new OrderFlowCloudStudyRule { Id = "Ticks <= " + count, Source = OrderFlowImbalanceSource.Off, MaximumCount = count }); }
            foreach (OrderFlowImbalanceSource source in new[] { OrderFlowImbalanceSource.Inside, OrderFlowImbalanceSource.Context, OrderFlowImbalanceSource.Both })
            {
                foreach (decimal ratio in new decimal[] { 100, 200, 300, 500, 1000 })
                { rules.Add(Rule(source, ratio, 0, 0, 0)); }
                foreach (decimal delta in new decimal[] { 25, 50, 75 })
                { rules.Add(Rule(source, 100, 0, 0, delta)); }
                List<OrderFlowDiagonalPair> pairs = new List<OrderFlowDiagonalPair>();
                foreach (OrderFlowCloudStudyEvent item in fit)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (source != OrderFlowImbalanceSource.Context && item.Cloud.InsideImbalance != null) { pairs.AddRange(item.Cloud.InsideImbalance.Pairs.Values); }
                    if (source != OrderFlowImbalanceSource.Inside && item.Cloud.ContextImbalance != null) { pairs.AddRange(item.Cloud.ContextImbalance.Pairs.Values); }
                }
                if (pairs.Count == 0) { continue; }
                foreach (int percentile in new[] { 50, 75, 90 })
                {
                    rules.Add(Rule(source, 100, Quantile(pairs.Select(pair => Math.Max(pair.Buy, pair.Sell)), percentile, 100), 0, 0));
                    rules.Add(Rule(source, 100, 0, Quantile(pairs.Select(pair => Math.Abs(pair.Buy - pair.Sell)), percentile, 100), 0));
                }
            }
            return rules.GroupBy(rule => rule.Id, StringComparer.Ordinal).Select(group => group.First()).ToList();
        }

        private static OrderFlowCloudStudyRule Rule(OrderFlowImbalanceSource source, decimal ratio, decimal volume, decimal difference, decimal delta)
        {
            string F(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);
            return new OrderFlowCloudStudyRule { Source = source, Ratio = ratio, Volume = volume, Difference = difference, Delta = delta,
                Id = source + "; ratio=" + F(ratio) + "%; volume=" + F(volume) + "; difference=" + F(difference) + "; delta=" + F(delta) + "%" };
        }

        private static decimal Quantile(IEnumerable<decimal> values, int numerator, int denominator)
        {
            decimal[] sorted = values.OrderBy(value => value).ToArray();
            if (sorted.Length == 0) { return 0; }
            // Integer rational indexing avoids decimal thirds rounding an exact index boundary down.
            return sorted[(int)((long)(sorted.Length - 1) * numerator / denominator)];
        }

        #endregion
    }
}
