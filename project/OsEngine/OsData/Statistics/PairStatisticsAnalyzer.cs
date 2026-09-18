/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace OsEngine.OsData.Statistics
{
    /// <summary>Exact-time, positive-volume Close A minus Close B analysis.</summary>
    public class PairStatisticsAnalyzer
    {
        #region Analysis

        public PairStatisticsResult Analyze(IReadOnlyList<StatisticsCandle> first,
            IReadOnlyList<StatisticsCandle> second, PairStatisticsOptions options, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (first == null || second == null || options == null)
            {
                throw new ArgumentNullException(first == null ? nameof(first) : second == null ? nameof(second) : nameof(options));
            }
            StatisticsDatasetReader.ValidateDates(options.From, options.To);
            if (options.MinVolumeA < 0 || options.MinVolumeB < 0
                || options.MinActiveBarsA < 1 || options.MinActiveBarsB < 1
                || options.MinCoveragePercent < 0 || options.MinCoveragePercent > 100)
            {
                throw new ArgumentException("Volumes must be nonnegative, active bar minima positive, and coverage between 0 and 100.", nameof(options));
            }
            ValidateInput(first, "A", options, token);
            ValidateInput(second, "B", options, token);
            PairStatisticsResult result = new PairStatisticsResult();
            SummaryAccumulator total = new SummaryAccumulator();
            SummaryAccumulator interval = null;
            PairStatisticsInterval currentInterval = null;
            DateTime? previousAcceptedDate = null;
            int indexA = 0;
            int indexB = 0;
            while (indexA < first.Count || indexB < second.Count)
            {
                token.ThrowIfCancellationRequested();
                StatisticsCandle a = indexA < first.Count ? first[indexA] : null;
                StatisticsCandle b = indexB < second.Count ? second[indexB] : null;
                DateTime date = a == null ? b.Time.Date : b == null ? a.Time.Date
                    : (a.Time < b.Time ? a.Time.Date : b.Time.Date);
                if (!StatisticsDatasetReader.InRange(date, options.From, options.To))
                {
                    while (indexA < first.Count && first[indexA].Time.Date == date)
                    {
                        token.ThrowIfCancellationRequested();
                        indexA++;
                    }
                    while (indexB < second.Count && second[indexB].Time.Date == date)
                    {
                        token.ThrowIfCancellationRequested();
                        indexB++;
                    }
                    continue;
                }
                PairStatisticsDay day = new PairStatisticsDay { Date = date };
                List<PairStatisticsPoint> points = new List<PairStatisticsPoint>();
                while ((indexA < first.Count && first[indexA].Time.Date == date)
                    || (indexB < second.Count && second[indexB].Time.Date == date))
                {
                    token.ThrowIfCancellationRequested();
                    a = indexA < first.Count && first[indexA].Time.Date == date ? first[indexA] : null;
                    b = indexB < second.Count && second[indexB].Time.Date == date ? second[indexB] : null;
                    bool takeA = a != null && (b == null || a.Time <= b.Time);
                    bool takeB = b != null && (a == null || b.Time <= a.Time);
                    if (takeA)
                    {
                        day.VolumeA += a.Volume;
                        if (a.Volume > 0) day.ActiveBarsA++;
                        indexA++;
                    }
                    if (takeB)
                    {
                        day.VolumeB += b.Volume;
                        if (b.Volume > 0) day.ActiveBarsB++;
                        indexB++;
                    }
                    if (takeA && takeB && a.Volume > 0 && b.Volume > 0)
                    {
                        points.Add(new PairStatisticsPoint { Time = a.Time, Value = a.Close - b.Close });
                    }
                }
                day.MatchedBars = points.Count;
                int union = day.ActiveBarsA + day.ActiveBarsB - day.MatchedBars;
                day.CoveragePercent = union == 0 ? 0 : 100m * day.MatchedBars / union;
                day.Reason = GetReason(day, options);
                day.Accepted = day.Reason == "Accepted";
                result.Days.Add(day);
                if (!day.Accepted)
                {
                    previousAcceptedDate = null;
                    continue;
                }
                if (!previousAcceptedDate.HasValue || (date - previousAcceptedDate.Value).Days != 1)
                {
                    interval = new SummaryAccumulator();
                    currentInterval = new PairStatisticsInterval { Start = points[0].Time };
                    result.Intervals.Add(currentInterval);
                }
                SummaryAccumulator daily = new SummaryAccumulator();
                foreach (PairStatisticsPoint point in points)
                {
                    token.ThrowIfCancellationRequested();
                    daily.Add(point);
                    total.Add(point);
                    interval.Add(point);
                    result.Points.Add(point);
                }
                day.Summary = daily.GetSummary();
                currentInterval.End = points[points.Count - 1].Time;
                currentInterval.Days++;
                currentInterval.Summary = interval.GetSummary();
                previousAcceptedDate = date;
            }
            token.ThrowIfCancellationRequested();
            result.Summary = total.GetSummary();
            return result;
        }

        #endregion

        #region Validation

        private static void ValidateInput(IReadOnlyList<StatisticsCandle> candles, string leg,
            PairStatisticsOptions options, CancellationToken token)
        {
            int selected = 0;
            DateTime? previous = null;
            for (int i = 0; i < candles.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                StatisticsCandle candle = candles[i];
                if (!StatisticsDatasetReader.IsValidCandle(candle))
                {
                    throw new InvalidDataException("Leg " + leg + ", candle " + (i + 1) + ": null candle, negative volume or inconsistent OHLC.");
                }
                if (previous.HasValue && candle.Time <= previous.Value)
                {
                    throw new InvalidDataException("Leg " + leg + ", candle " + (i + 1) + ": timestamps must be strictly increasing.");
                }
                previous = candle.Time;
                if (StatisticsDatasetReader.InRange(candle.Time, options.From, options.To)
                    && ++selected > StatisticsDatasetReader.MaximumCandles)
                {
                    throw new InvalidDataException("Leg " + leg + " exceeds 2,000,000 candles in the selected range. Narrow the date range.");
                }
            }
        }

        private static string GetReason(PairStatisticsDay day, PairStatisticsOptions options)
        {
            if (day.MatchedBars == 0) return "NoMatchedBars";
            if (day.VolumeA < options.MinVolumeA) return "LowVolumeA";
            if (day.VolumeB < options.MinVolumeB) return "LowVolumeB";
            if (day.ActiveBarsA < options.MinActiveBarsA) return "LowActiveBarsA";
            if (day.ActiveBarsB < options.MinActiveBarsB) return "LowActiveBarsB";
            if (day.CoveragePercent < options.MinCoveragePercent) return "LowCoverage";
            return "Accepted";
        }

        #endregion

        #region Summary

        private sealed class SummaryAccumulator
        {
            private int _count;
            private DateTime _start;
            private DateTime _end;
            private decimal _minimum;
            private decimal _maximum;
            private decimal _origin;
            private decimal _mean;
            private double _offsetMean;
            private double _m2;

            public void Add(PairStatisticsPoint point)
            {
                if (_count == 0)
                {
                    _start = point.Time;
                    _minimum = point.Value;
                    _maximum = point.Value;
                    _origin = point.Value;
                    _mean = point.Value;
                }
                _end = point.Time;
                _minimum = Math.Min(_minimum, point.Value);
                _maximum = Math.Max(_maximum, point.Value);
                _count++;
                decimal offset = point.Value - _origin;
                // Avoid overflowing a sum even when the final mean is representable.
                _mean += (point.Value - _mean) / _count;
                // Shift before converting to double to retain small variations at large price levels.
                double value = (double)offset;
                double delta = value - _offsetMean;
                _offsetMean += delta / _count;
                _m2 += delta * (value - _offsetMean);
            }

            public PairStatisticsSummary GetSummary()
            {
                if (_count == 0) return null;
                return new PairStatisticsSummary
                {
                    Count = _count,
                    Start = _start,
                    End = _end,
                    Minimum = _minimum,
                    Maximum = _maximum,
                    Range = _maximum - _minimum,
                    Mean = _mean,
                    StandardDeviation = Math.Sqrt(Math.Max(0, _m2 / _count))
                };
            }
        }

        #endregion
    }
}
