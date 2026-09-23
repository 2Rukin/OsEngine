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
    /// <summary>Display-only timeframes and detached resampling of existing minute bars.</summary>
    /// <remarks>
    /// Called on the UI thread with completed, time-ordered Min1 bars. No source
    /// DTO, research result, feature, label or artifact is changed. Boundaries
    /// follow source clock time; no exchange calendar or timezone is applied.
    /// Contract: ORDER-FLOW-RESEARCH-001 and ORDER-FLOW-MVP-RUNBOOK-001.
    /// </remarks>
    internal static class OrderFlowChartTimeFrames
    {
        /// <summary>Returns a fixed duration for supported timeframes other than the calendar month.</summary>
        /// <param name="timeFrame">Display timeframe.</param>
        /// <returns>The duration without session or timezone adjustment.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The value is unsupported or is Month1, which has no fixed duration.</exception>
        public static TimeSpan GetDuration(OrderFlowDisplayTimeFrame timeFrame)
        {
            switch (timeFrame)
            {
                case OrderFlowDisplayTimeFrame.Sec15: return TimeSpan.FromSeconds(15);
                case OrderFlowDisplayTimeFrame.Sec30: return TimeSpan.FromSeconds(30);
                case OrderFlowDisplayTimeFrame.Min1: return TimeSpan.FromMinutes(1);
                case OrderFlowDisplayTimeFrame.Min2: return TimeSpan.FromMinutes(2);
                case OrderFlowDisplayTimeFrame.Min3: return TimeSpan.FromMinutes(3);
                case OrderFlowDisplayTimeFrame.Min20: return TimeSpan.FromMinutes(20);
                case OrderFlowDisplayTimeFrame.Min45: return TimeSpan.FromMinutes(45);
                case OrderFlowDisplayTimeFrame.Hour2: return TimeSpan.FromHours(2);
                case OrderFlowDisplayTimeFrame.Hour3: return TimeSpan.FromHours(3);
                case OrderFlowDisplayTimeFrame.Hour6: return TimeSpan.FromHours(6);
                case OrderFlowDisplayTimeFrame.Hour8: return TimeSpan.FromHours(8);
                case OrderFlowDisplayTimeFrame.Hour12: return TimeSpan.FromHours(12);
                case OrderFlowDisplayTimeFrame.Min5: return TimeSpan.FromMinutes(5);
                case OrderFlowDisplayTimeFrame.Min10: return TimeSpan.FromMinutes(10);
                case OrderFlowDisplayTimeFrame.Min15: return TimeSpan.FromMinutes(15);
                case OrderFlowDisplayTimeFrame.Min30: return TimeSpan.FromMinutes(30);
                case OrderFlowDisplayTimeFrame.Min60: return TimeSpan.FromMinutes(60);
                case OrderFlowDisplayTimeFrame.Hour4: return TimeSpan.FromHours(4);
                case OrderFlowDisplayTimeFrame.Day1: return TimeSpan.FromDays(1);
                case OrderFlowDisplayTimeFrame.Week1: return TimeSpan.FromDays(7);
                default: throw new ArgumentOutOfRangeException(nameof(timeFrame));
            }
        }

        /// <summary>Returns the selector order from seconds through calendar month, independently of enum numeric values.</summary>
        public static List<OrderFlowDisplayTimeFrame> GetMenuValues()
        {
            return Enum.GetValues<OrderFlowDisplayTimeFrame>()
                .OrderBy(frame => frame == OrderFlowDisplayTimeFrame.Month1 ? TimeSpan.MaxValue : GetDuration(frame)).ToList();
        }

        /// <summary>Formats an explicit duration for the chart selector and caption.</summary>
        /// <param name="timeFrame">Display timeframe.</param>
        /// <param name="russian">True for Russian unit labels, false for English.</param>
        /// <returns>A duration such as 60 min or 4 h, with localized units.</returns>
        public static string GetDisplayName(OrderFlowDisplayTimeFrame timeFrame, bool russian)
        {
            if (timeFrame == OrderFlowDisplayTimeFrame.Month1) { return russian ? "1 месяц" : "1 month"; }
            TimeSpan duration = GetDuration(timeFrame);
            if (timeFrame == OrderFlowDisplayTimeFrame.Day1) { return russian ? "1 день" : "1 day"; }
            if (timeFrame == OrderFlowDisplayTimeFrame.Week1) { return russian ? "1 неделя" : "1 week"; }
            if (duration.TotalMinutes < 1)
            {
                return duration.TotalSeconds.ToString(CultureInfo.InvariantCulture) + (russian ? " сек" : " sec");
            }
            if (duration.TotalHours >= 2) { return duration.TotalHours.ToString(CultureInfo.InvariantCulture) + (russian ? " ч" : " h"); }
            return duration.TotalMinutes.ToString(CultureInfo.InvariantCulture) + (russian ? " мин" : " min");
        }

        /// <summary>Creates larger OHLC/volume/delta bars without filling intervals that have no trades.</summary>
        /// <param name="minutes">Completed, chronologically ordered Min1 display bars; source objects remain unchanged.</param>
        /// <param name="timeFrame">A supported display timeframe longer than one minute.</param>
        /// <returns>New bars, including the final partial interval with its ordinary aligned end time.</returns>
        /// <remarks>
        /// OHLC uses the first/last trade bars and their extrema; volume/delta are
        /// summed. Response comes from the last trade bar without recomputation.
        /// Four-hour bars start at 00/04/08/12/16/20; daily bars at midnight;
        /// weekly bars at Monday midnight (DateTime epoch is Monday); calendar months
        /// start at midnight on day one and end at the next month, including leap years. All use
        /// the source clock, with no session, timezone or DST conversion.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">The timeframe is unsupported or not longer than Min1.</exception>
        public static List<OrderFlowDisplayBar> AggregateMinutes(List<OrderFlowDisplayBar> minutes,
            OrderFlowDisplayTimeFrame timeFrame)
        {
            bool calendarMonth = timeFrame == OrderFlowDisplayTimeFrame.Month1;
            TimeSpan duration = calendarMonth ? TimeSpan.Zero : GetDuration(timeFrame);
            if (!calendarMonth && duration <= TimeSpan.FromMinutes(1)) { throw new ArgumentOutOfRangeException(nameof(timeFrame)); }
            List<OrderFlowDisplayBar> result = new List<OrderFlowDisplayBar>();
            OrderFlowDisplayBar current = null;
            for (int i = 0; i < minutes.Count; i++)
            {
                OrderFlowDisplayBar minute = minutes[i];
                if (minute.HasTrades == false) { continue; }
                DateTime start = calendarMonth
                    ? new DateTime(minute.TimeStart.Year, minute.TimeStart.Month, 1, 0, 0, 0, minute.TimeStart.Kind)
                    : new DateTime(minute.TimeStart.Ticks - minute.TimeStart.Ticks % duration.Ticks, minute.TimeStart.Kind);
                if (current == null || current.TimeStart != start)
                {
                    current = new OrderFlowDisplayBar();
                    current.TimeFrame = timeFrame;
                    current.TimeStart = start;
                    current.TimeEnd = calendarMonth ? start.AddMonths(1) : start.Add(duration);
                    current.HasTrades = true;
                    current.Open = minute.Open;
                    current.High = minute.High;
                    current.Low = minute.Low;
                    result.Add(current);
                }
                current.High = Math.Max(current.High, minute.High);
                current.Low = Math.Min(current.Low, minute.Low);
                current.Close = minute.Close;
                current.Volume += minute.Volume;
                current.Delta += minute.Delta;
                current.PriceResponse = minute.PriceResponse;
            }
            return result;
        }
    }

    internal sealed class OrderFlowCandidateView
    {
        public string CandidateId { get; set; }

        public string TimeText { get; set; }

        public OrderFlowDirection Direction { get; set; }

        public decimal Delta { get; set; }

        public decimal PriceChange { get; set; }

        public decimal PriceResponse { get; set; }

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

            Dictionary<string, OrderFlowMarketPathLabel> shortest = ShortestLabels(result);
            List<OrderFlowCandidateView> views = new List<OrderFlowCandidateView>();

            for (int i = 0; i < result.Candidates.Count; i++)
            {
                OrderFlowCandidate candidate = result.Candidates[i];
                OrderFlowObservation observation;
                if (observations.TryGetValue(candidate.CandidateId, out observation) == false)
                {
                    continue;
                }

                shortest.TryGetValue(candidate.CandidateId, out OrderFlowMarketPathLabel label);
                OrderFlowFeatureSnapshot feature = observation.Features;

                OrderFlowCandidateView view = new OrderFlowCandidateView();
                view.CandidateId = candidate.CandidateId;
                view.TimeText = candidate.Time.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture);
                view.Direction = candidate.Direction;
                view.Delta = feature.Delta;
                view.PriceChange = feature.PriceChange;
                view.PriceResponse = feature.PriceResponse;
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

        internal static Dictionary<string, OrderFlowMarketPathLabel> ShortestLabels(OrderFlowResearchResult result)
        {
            Dictionary<string, OrderFlowMarketPathLabel> labels = new Dictionary<string, OrderFlowMarketPathLabel>(StringComparer.Ordinal);
            foreach (OrderFlowMarketPathLabel label in result.Labels)
            {
                if (!labels.TryGetValue(label.CandidateId, out OrderFlowMarketPathLabel previous) || label.HorizonSeconds < previous.HorizonSeconds)
                {
                    labels[label.CandidateId] = label;
                }
            }
            return labels;
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
                  label.MaximumFavorableExcursion.ToString("0.############################", CultureInfo.InvariantCulture) +
                  " · MAE " + label.MaximumAdverseExcursion.ToString("0.############################", CultureInfo.InvariantCulture);

            return candidate.Time.ToString("dd.MM.yyyy HH:mm:ss.ffffff", CultureInfo.InvariantCulture) + " · " + candidate.CandidateId + " · " + candidate.Direction + " · " + candidate.ReasonCode +
                L(" · price ", " · цена ") + candidate.ReferencePrice.ToString("0.############################", CultureInfo.InvariantCulture) +
                L(" · delta ", " · дельта окна ") + feature.Delta.ToString("0.############################", CultureInfo.InvariantCulture) +
                L(" · response ", " · отклик окна ") + feature.PriceResponse.ToString("0.############################", CultureInfo.InvariantCulture) +
                " · " + feature.DataQualityCode + " · " + labelText +
                L(". MFE/MAE are price distances, not PnL.", ". MFE/MAE — лучшее/худшее отклонение в единицах цены; это не прибыль.");
        }
    }
}
