/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using OsEngine.Language;
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

        private static string L(string english, string russian)
        {
            return OsLocalization.CurLocalization == OsLocalization.OsLocalType.Ru ? russian : english;
        }

        private static string BuildDetails(OrderFlowCandidate candidate, OrderFlowFeatureSnapshot feature,
            OrderFlowMarketPathLabel label)
        {
            string labelText = label == null
                ? L("No future label", "Нет оценки будущего")
                : L("Shortest horizon ", "Короткий горизонт ") + label.HorizonSeconds.ToString(CultureInfo.InvariantCulture) +
                  L(" sec · ", " сек · ") + label.Outcome + " · MFE " +
                  label.MaximumFavorableExcursion.ToString("0.######", CultureInfo.InvariantCulture) +
                  " · MAE " + label.MaximumAdverseExcursion.ToString("0.######", CultureInfo.InvariantCulture);

            return candidate.Time.ToString("dd.MM.yyyy HH:mm:ss.fff", CultureInfo.InvariantCulture) + " · " + candidate.CandidateId + " · " + candidate.Direction + " · " + candidate.ReasonCode +
                L(" · price ", " · цена ") + candidate.ReferencePrice.ToString("0.######", CultureInfo.InvariantCulture) +
                L(" · delta ", " · дельта окна ") + feature.Delta.ToString("0.######", CultureInfo.InvariantCulture) +
                L(" · response ", " · отклик окна ") + feature.PriceResponse.ToString("0.######", CultureInfo.InvariantCulture) +
                L(" · spread ", " · спред ") + feature.Spread.ToString("0.######", CultureInfo.InvariantCulture) +
                L(" · imbalance ", " · дисбаланс ") + feature.BookImbalance.ToString("0.######", CultureInfo.InvariantCulture) +
                L(" · book age ", " · возраст стакана ") + feature.BookAgeMilliseconds.ToString(CultureInfo.InvariantCulture) +
                " ms · " + feature.DataQualityCode + " · " + labelText +
                L(". MFE/MAE are price distances, not PnL.", ". MFE/MAE — лучшее/худшее отклонение в единицах цены; это не прибыль.");
        }
    }
}
