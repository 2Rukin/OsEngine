/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OsEngine.OsData.OrderFlow
{
    internal sealed class OrderFlowCandidateView
    {
        public string CandidateId { get; set; }

        public string TimeText { get; set; }

        public OrderFlowDirection Direction { get; set; }

        public decimal Delta { get; set; }

        public decimal PriceChange { get; set; }

        public decimal PriceResponse { get; set; }

        public decimal Spread { get; set; }

        public decimal BookImbalance { get; set; }

        public long BookAgeMilliseconds { get; set; }

        public string DataQualityCode { get; set; }

        public int HorizonSeconds { get; set; }

        public OrderFlowBarrierOutcome Outcome { get; set; }

        public decimal Mfe { get; set; }

        public decimal Mae { get; set; }

        public string Details { get; set; }

        public static List<OrderFlowCandidateView> Create(OrderFlowResearchResult result)
        {
            Dictionary<string, OrderFlowObservation> observations = result.Observations
                .Where(observation => string.IsNullOrEmpty(observation.CandidateId) == false)
                .ToDictionary(observation => observation.CandidateId, StringComparer.Ordinal);

            List<OrderFlowCandidateView> views = new List<OrderFlowCandidateView>();

            for (int i = 0; i < result.Candidates.Count; i++)
            {
                OrderFlowCandidate candidate = result.Candidates[i];
                OrderFlowObservation observation;
                if (observations.TryGetValue(candidate.CandidateId, out observation) == false)
                {
                    continue;
                }

                OrderFlowMarketPathLabel label = result.Labels
                    .Where(item => item.CandidateId == candidate.CandidateId)
                    .OrderBy(item => item.HorizonSeconds)
                    .FirstOrDefault();
                OrderFlowFeatureSnapshot feature = observation.Features;

                OrderFlowCandidateView view = new OrderFlowCandidateView();
                view.CandidateId = candidate.CandidateId;
                view.TimeText = candidate.Time.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                view.Direction = candidate.Direction;
                view.Delta = feature.Delta;
                view.PriceChange = feature.PriceChange;
                view.PriceResponse = feature.PriceResponse;
                view.Spread = feature.Spread;
                view.BookImbalance = feature.BookImbalance;
                view.BookAgeMilliseconds = feature.BookAgeMilliseconds;
                view.DataQualityCode = feature.DataQualityCode;
                view.HorizonSeconds = label == null ? 0 : label.HorizonSeconds;
                view.Outcome = label == null ? OrderFlowBarrierOutcome.Incomplete : label.Outcome;
                view.Mfe = label == null ? 0 : label.MaximumFavorableExcursion;
                view.Mae = label == null ? 0 : label.MaximumAdverseExcursion;
                view.Details = BuildDetails(candidate, feature, label);
                views.Add(view);
            }

            return views;
        }

        private static string BuildDetails(OrderFlowCandidate candidate, OrderFlowFeatureSnapshot feature,
            OrderFlowMarketPathLabel label)
        {
            string labelText = label == null
                ? "No complete future label"
                : "Shortest label " + label.HorizonSeconds.ToString(CultureInfo.InvariantCulture) +
                  " sec · " + label.Outcome + " · MFE " +
                  label.MaximumFavorableExcursion.ToString("G29", CultureInfo.InvariantCulture) +
                  " · MAE " + label.MaximumAdverseExcursion.ToString("G29", CultureInfo.InvariantCulture);

            return candidate.CandidateId + " · " + candidate.Direction + " · " + candidate.ReasonCode +
                " · price " + candidate.ReferencePrice.ToString("G29", CultureInfo.InvariantCulture) +
                " · delta " + feature.Delta.ToString("G29", CultureInfo.InvariantCulture) +
                " · response " + feature.PriceResponse.ToString("G29", CultureInfo.InvariantCulture) +
                " · spread " + feature.Spread.ToString("G29", CultureInfo.InvariantCulture) +
                " · imbalance " + feature.BookImbalance.ToString("G29", CultureInfo.InvariantCulture) +
                " · book age " + feature.BookAgeMilliseconds.ToString(CultureInfo.InvariantCulture) +
                " ms · " + feature.DataQualityCode + " · " + labelText +
                ". Candidate and future label are separate; neither is a trade.";
        }
    }
}
