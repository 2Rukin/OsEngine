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
    /// <summary>Descriptive research on accepted, exactly aligned Close spreads.</summary>
    public class PairResearchAnalyzer
    {
        public const int MaximumEpisodes = 2000000;

        #region Analysis

        public PairResearchResult Analyze(PairStatisticsResult accepted, IReadOnlyList<StatisticsCandle> first,
            IReadOnlyList<StatisticsCandle> second, PairResearchOptions options, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Validate(accepted, first, second, options, token);
            int period = options.SmaPeriod;
            TimeSpan step = options.TimeFrameDuration;
            PairResearchResult result = new PairResearchResult();
            RollingWindow window = new RollingWindow(period);
            DeviationAccumulator deviations = new DeviationAccumulator();
            PairResearchEpisode[] active = new PairResearchEpisode[12];
            PairResearchSample previous = null;
            int indexA = 0;
            int indexB = 0;
            int intervalIndex = 0;
            int previousInterval = -1;
            int segment = -1;
            foreach (PairStatisticsPoint point in accepted.Points)
            {
                token.ThrowIfCancellationRequested();
                while (intervalIndex < accepted.Intervals.Count && point.Time > accepted.Intervals[intervalIndex].End)
                {
                    intervalIndex++;
                }
                if (intervalIndex == accepted.Intervals.Count || point.Time < accepted.Intervals[intervalIndex].Start)
                {
                    throw new InvalidDataException("Accepted point is outside the accepted intervals: " + point.Time.ToString("O"));
                }
                StatisticsCandle a = FindCandle(first, ref indexA, point.Time, token);
                StatisticsCandle b = FindCandle(second, ref indexB, point.Time, token);
                if (a.Volume <= 0 || b.Volume <= 0 || a.Close - b.Close != point.Value)
                {
                    throw new InvalidDataException("Accepted spread does not match active source candles: " + point.Time.ToString("O"));
                }
                bool gap = previous == null || intervalIndex != previousInterval || point.Time - previous.Time != step;
                if (gap)
                {
                    Censor(active, "Gap");
                    window.Reset();
                    previous = null;
                    segment++;
                }
                PairResearchSample sample = new PairResearchSample
                {
                    Time = point.Time, PriceA = a.Close, PriceB = b.Close, VolumeA = a.Volume, VolumeB = b.Volume,
                    Basis = point.Value, Segment = segment
                };
                window.Add(point.Value, token);
                if (window.Ready)
                {
                    sample.Sma = window.Mean;
                    sample.Deviation = point.Value - window.Mean;
                    sample.Sigma = window.Sigma;
                    sample.ZScore = window.Sigma > 0 ? (double)sample.Deviation.Value / window.Sigma : (double?)null;
                    deviations.Add(sample.Deviation.Value);
                    UpdateEpisodes(result.Episodes, active, previous, sample, token);
                }
                result.Samples.Add(sample);
                previous = sample;
                previousInterval = intervalIndex;
            }
            Censor(active, "HistoryEnd");
            result.Deviations = deviations.Complete(token);
            result.EpisodeSummaries = SummarizeEpisodes(result.Episodes, token);
            token.ThrowIfCancellationRequested();
            return result;
        }

        private static StatisticsCandle FindCandle(IReadOnlyList<StatisticsCandle> candles, ref int index,
            DateTime time, CancellationToken token)
        {
            while (index < candles.Count && candles[index].Time < time)
            {
                token.ThrowIfCancellationRequested();
                index++;
            }
            if (index == candles.Count || candles[index].Time != time)
            {
                throw new InvalidDataException("Accepted point has no exact source candle: " + time.ToString("O"));
            }
            return candles[index];
        }

        #endregion

        #region Validation

        private static void Validate(PairStatisticsResult accepted, IReadOnlyList<StatisticsCandle> first,
            IReadOnlyList<StatisticsCandle> second, PairResearchOptions options, CancellationToken token)
        {
            if (accepted == null || accepted.Points == null || accepted.Intervals == null
                || first == null || second == null || options == null)
            {
                throw new ArgumentException("Research requires a result, source candles and options.");
            }
            if (options.SmaPeriod < 1 || options.SmaPeriod > StatisticsDatasetReader.MaximumCandles
                || options.TimeFrameDuration <= TimeSpan.Zero)
            {
                throw new ArgumentException("SMA period must be 1 to 2,000,000 and timeframe duration positive.");
            }
            if (accepted.Points.Count > StatisticsDatasetReader.MaximumCandles)
            {
                throw new InvalidDataException("More than 2,000,000 accepted points. Narrow the date range.");
            }
            ValidateCandles(first, token);
            ValidateCandles(second, token);
            for (int i = 0; i < accepted.Points.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                if (accepted.Points[i] == null || (i > 0 && accepted.Points[i].Time <= accepted.Points[i - 1].Time))
                {
                    throw new InvalidDataException("Accepted points must be nonnull and strictly ordered.");
                }
            }
            for (int i = 0; i < accepted.Intervals.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                PairStatisticsInterval interval = accepted.Intervals[i];
                if (interval == null || interval.Start > interval.End || (i > 0 && interval.Start <= accepted.Intervals[i - 1].End))
                {
                    throw new InvalidDataException("Accepted intervals must be ordered and nonoverlapping.");
                }
            }
        }

        private static void ValidateCandles(IReadOnlyList<StatisticsCandle> candles, CancellationToken token)
        {
            if (candles.Count > StatisticsDatasetReader.MaximumCandles)
            {
                throw new InvalidDataException("More than 2,000,000 source candles. Narrow the date range.");
            }
            for (int i = 0; i < candles.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                if (!StatisticsDatasetReader.IsValidCandle(candles[i]) || (i > 0 && candles[i].Time <= candles[i - 1].Time))
                {
                    throw new InvalidDataException("Research source candles must have valid OHLC/volume and strictly increasing times.");
                }
            }
        }

        #endregion

        #region Episodes

        private static void UpdateEpisodes(List<PairResearchEpisode> episodes, PairResearchEpisode[] active,
            PairResearchSample previous, PairResearchSample sample, CancellationToken token)
        {
            for (int slot = 0; slot < active.Length; slot++)
            {
                token.ThrowIfCancellationRequested();
                int direction = slot < 6 ? 1 : -1;
                double multiple = (slot % 6 + 1) * 0.5;
                decimal directedDeviation = direction * sample.Deviation.Value;
                PairResearchEpisode episode = active[slot];
                if (episode != null)
                {
                    episode.End = sample.Time;
                    episode.DurationMinutes = (episode.End - episode.Start).TotalMinutes;
                    episode.ObservedBars++;
                    episode.MaximumAdverseDeviation = Math.Max(episode.MaximumAdverseDeviation, directedDeviation);
                    episode.FurtherAdverseDeviation = Math.Max(0, episode.MaximumAdverseDeviation - direction * episode.EntryDeviation);
                    if (directedDeviation <= 0)
                    {
                        episode.Status = "Returned";
                        active[slot] = null;
                    }
                    // No overlapping episode at this signed threshold.
                    continue;
                }
                if (previous == null || !previous.ZScore.HasValue || !sample.ZScore.HasValue
                    || direction * previous.ZScore.Value >= multiple || direction * sample.ZScore.Value < multiple)
                {
                    continue;
                }
                if (episodes.Count >= MaximumEpisodes)
                {
                    throw new InvalidDataException("More than 2,000,000 research episodes. Narrow the date range.");
                }
                episode = new PairResearchEpisode
                {
                    Start = sample.Time, End = sample.Time, Direction = direction, SigmaMultiple = multiple,
                    EntrySigma = sample.Sigma.Value, EntryThreshold = (decimal)(multiple * sample.Sigma.Value),
                    EntryDeviation = sample.Deviation.Value, MaximumAdverseDeviation = directedDeviation,
                    ObservedBars = 1
                };
                episodes.Add(episode);
                active[slot] = episode;
            }
        }

        private static void Censor(PairResearchEpisode[] active, string reason)
        {
            for (int i = 0; i < active.Length; i++)
            {
                if (active[i] == null) continue;
                active[i].Status = reason;
                active[i] = null;
            }
        }

        private static List<PairEpisodeSummary> SummarizeEpisodes(List<PairResearchEpisode> episodes, CancellationToken token)
        {
            List<PairEpisodeSummary> summaries = new List<PairEpisodeSummary>();
            List<double>[] durations = new List<double>[12];
            for (int i = 0; i < 12; i++)
            {
                summaries.Add(new PairEpisodeSummary { Direction = i < 6 ? 1 : -1, SigmaMultiple = (i % 6 + 1) * 0.5 });
                durations[i] = new List<double>();
            }
            foreach (PairResearchEpisode episode in episodes)
            {
                token.ThrowIfCancellationRequested();
                int slot = (episode.Direction > 0 ? 0 : 6) + (int)(episode.SigmaMultiple * 2) - 1;
                PairEpisodeSummary summary = summaries[slot];
                summary.Count++;
                summary.MaximumFurtherDeviation = Math.Max(summary.MaximumFurtherDeviation ?? 0, episode.FurtherAdverseDeviation);
                if (episode.Status == "Returned")
                {
                    summary.Returned++;
                    summary.MeanMinutes = (summary.MeanMinutes ?? 0) + (episode.DurationMinutes - (summary.MeanMinutes ?? 0)) / summary.Returned;
                    durations[slot].Add(episode.DurationMinutes);
                }
                else if (episode.Status == "Gap") summary.Gap++;
                else if (episode.Status == "HistoryEnd") summary.HistoryEnd++;
            }
            for (int i = 0; i < summaries.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                if (durations[i].Count == 0) continue;
                Sort(durations[i], token);
                summaries[i].MedianMinutes = Percentile(durations[i], 0.5);
                summaries[i].P90Minutes = Percentile(durations[i], 0.9);
                summaries[i].MaximumMinutes = durations[i][durations[i].Count - 1];
            }
            return summaries;
        }

        private static double Percentile(List<double> sorted, double probability)
        {
            double position = (sorted.Count - 1) * probability;
            int lower = (int)position;
            int upper = Math.Min(lower + 1, sorted.Count - 1);
            return sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
        }

        #endregion

        #region Rolling statistics

        private sealed class RollingWindow
        {
            private readonly decimal[] _values;
            private int _count;
            private int _next;
            private int _slides;
            private int _sameRun;
            private decimal _last;
            private decimal _origin;
            private decimal _mean;
            private double _offsetMean;
            private double _m2;

            public RollingWindow(int period) { _values = new decimal[period]; }
            public bool Ready { get { return _count == _values.Length; } }
            public decimal Mean { get { return _mean; } }
            public double Sigma { get { return Math.Sqrt(Math.Max(0, _m2 / _count)); } }

            public void Reset()
            {
                _count = 0;
                _next = 0;
                _slides = 0;
                _sameRun = 0;
                _mean = 0;
                _offsetMean = 0;
                _m2 = 0;
            }

            public void Add(decimal value, CancellationToken token)
            {
                _sameRun = _count > 0 && value == _last ? Math.Min(_sameRun + 1, _values.Length) : 1;
                _last = value;
                if (_count == 0) _origin = value;
                double offset = (double)(value - _origin);
                if (_count < _values.Length)
                {
                    _count++;
                    _mean += (value - _mean) / _count;
                    double delta = offset - _offsetMean;
                    _offsetMean += delta / _count;
                    _m2 += delta * (offset - _offsetMean);
                }
                else if (_count > 1)
                {
                    decimal old = _values[_next];
                    _mean += (value - old) / _count;
                    double oldOffset = (double)(old - _origin);
                    double reducedMean = _offsetMean - (oldOffset - _offsetMean) / (_count - 1);
                    _m2 -= (oldOffset - _offsetMean) * (oldOffset - reducedMean);
                    double delta = offset - reducedMean;
                    _offsetMean = reducedMean + delta / _count;
                    _m2 += delta * (offset - _offsetMean);
                    _slides++;
                }
                _values[_next] = value;
                _next = (_next + 1) % _values.Length;
                // Exact constants must not produce spurious nonzero sigma through removal roundoff.
                if (_sameRun >= _count)
                {
                    _mean = value;
                    _offsetMean = offset;
                    _m2 = 0;
                }
                if (_slides >= _values.Length)
                {
                    Rebuild(token);
                }
            }

            private void Rebuild(CancellationToken token)
            {
                // Rebase once per N slides: linear overall work, bounded accumulated rounding error.
                _origin = _values[0];
                _mean = 0;
                _offsetMean = 0;
                _m2 = 0;
                for (int i = 0; i < _count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    _mean += (_values[i] - _mean) / (i + 1);
                    double offset = (double)(_values[i] - _origin);
                    double delta = offset - _offsetMean;
                    _offsetMean += delta / (i + 1);
                    _m2 += delta * (offset - _offsetMean);
                }
                _slides = 0;
            }
        }

        private sealed class DeviationAccumulator
        {
            private readonly List<decimal> _absolute = new List<decimal>();
            private decimal _meanAbsolute;
            private decimal _maximumUp;
            private decimal _maximumDown;
            private decimal _origin;
            private double _mean;
            private double _m2;

            public void Add(decimal value)
            {
                if (_absolute.Count == 0) _origin = value;
                decimal absolute = Math.Abs(value);
                _absolute.Add(absolute);
                _meanAbsolute += (absolute - _meanAbsolute) / _absolute.Count;
                _maximumUp = Math.Max(_maximumUp, value);
                _maximumDown = Math.Max(_maximumDown, -value);
                double offset = (double)(value - _origin);
                double delta = offset - _mean;
                _mean += delta / _absolute.Count;
                _m2 += delta * (offset - _mean);
            }

            public PairDeviationSummary Complete(CancellationToken token)
            {
                if (_absolute.Count == 0) return null;
                Sort(_absolute, token);
                return new PairDeviationSummary
                {
                    Count = _absolute.Count, MeanAbsolute = _meanAbsolute,
                    StandardDeviation = Math.Sqrt(Math.Max(0, _m2 / _absolute.Count)),
                    MaximumUp = _maximumUp, MaximumDown = _maximumDown,
                    P50 = Percentile(0.50m), P75 = Percentile(0.75m), P90 = Percentile(0.90m),
                    P95 = Percentile(0.95m), P99 = Percentile(0.99m)
                };
            }

            private decimal Percentile(decimal probability)
            {
                decimal position = (_absolute.Count - 1) * probability;
                int lower = (int)position;
                int upper = Math.Min(lower + 1, _absolute.Count - 1);
                return _absolute[lower] + (_absolute[upper] - _absolute[lower]) * (position - lower);
            }
        }

        private static void Sort<T>(List<T> values, CancellationToken token) where T : IComparable<T>
        {
            token.ThrowIfCancellationRequested();
            try
            {
                values.Sort(new CancellationComparer<T>(token));
            }
            catch (InvalidOperationException) when (token.IsCancellationRequested)
            {
                // List.Sort wraps exceptions from its comparer; retain cancellation semantics.
                throw new OperationCanceledException(token);
            }
            token.ThrowIfCancellationRequested();
        }

        private sealed class CancellationComparer<T> : IComparer<T> where T : IComparable<T>
        {
            private readonly CancellationToken _token;
            private int _comparisons;
            public CancellationComparer(CancellationToken token) { _token = token; }
            public int Compare(T first, T second)
            {
                if ((++_comparisons & 4095) == 0) _token.ThrowIfCancellationRequested();
                return first.CompareTo(second);
            }
        }

        #endregion
    }
}
