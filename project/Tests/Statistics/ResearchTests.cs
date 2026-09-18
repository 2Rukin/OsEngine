/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OsEngine.OsData.Statistics;

namespace OsEngine.Statistics.Tests
{
    internal static class ResearchTests
    {
        public static readonly Action[] All =
        {
            CatalogMatchesOnlySameContractCode, CatalogRejectsAmbiguityAndInvalidPrefixes,
            CatalogRequiresCommonTimeFrame, TimeFrameDurationsAreExplicit,
            SmaIncludesCurrentAndPreservesSources, ResearchWarmupAndConstantSeries,
            PeriodOneResetsEveryValue, DeviationPercentilesUseLinearInterpolation,
            SignedDeviationPopulationSigma, RollingMatchesFreshWindowsAtLargePrices,
            NoEntryOnFirstValidZ, ExactThresholdAndMultipleLevels,
            ReturnUsesDynamicSmaAndFixedEntrySigma, NegativeEpisodesMirrorPositive,
            NoOverlappingEpisodeAtSameThreshold, ZeroSigmaReturnsButDoesNotCreateCrossing,
            GapsResetWarmupAndCensorAtLastObservation, AcceptedIntervalBoundaryResetsSma,
            CompletedDurationsExcludeCensoredEpisodes, ResearchRejectsInvalidAndMismatchedInputs,
            ResearchUsesAcceptedPointsOnly, ResearchCancellationAndCatalogCancellation,
            ResearchCancellationDuringInputValidation
        };

        #region Fixtures and assertions

        private static readonly DateTime Start = new DateTime(2026, 3, 2, 10, 0, 0);

        private sealed class Fixture
        {
            public List<StatisticsCandle> First;
            public List<StatisticsCandle> Second;
            public PairStatisticsResult Accepted;
        }

        private static Fixture Create(decimal[] basis, int[] minutes = null)
        {
            Fixture fixture = new Fixture { First = new List<StatisticsCandle>(), Second = new List<StatisticsCandle>() };
            for (int i = 0; i < basis.Length; i++)
            {
                DateTime time = Start.AddMinutes(minutes == null ? i : minutes[i]);
                fixture.First.Add(Candle(time, 100 + basis[i], i + 1));
                fixture.Second.Add(Candle(time, 100, i + 2));
            }
            fixture.Accepted = new PairStatisticsAnalyzer().Analyze(fixture.First, fixture.Second,
                new PairStatisticsOptions(), CancellationToken.None);
            return fixture;
        }

        private static StatisticsCandle Candle(DateTime time, decimal close, decimal volume)
        {
            return new StatisticsCandle { Time = time, Open = close, High = close, Low = close, Close = close, Volume = volume };
        }

        private static PairResearchResult Analyze(decimal[] basis, int period, int[] minutes = null)
        {
            return Analyze(Create(basis, minutes), period);
        }

        private static PairResearchResult Analyze(Fixture fixture, int period)
        {
            return new PairResearchAnalyzer().Analyze(fixture.Accepted, fixture.First, fixture.Second,
                new PairResearchOptions { SmaPeriod = period }, CancellationToken.None);
        }

        private static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException("Expected " + expected + "; actual " + actual);
        }

        private static void Near(double expected, double actual, double tolerance = 0.0000001)
        {
            if (!double.IsFinite(actual) || Math.Abs(expected - actual) > tolerance)
                throw new InvalidOperationException("Expected " + expected + "; actual " + actual);
        }

        private static Exception Throws(Action action)
        {
            try { action(); }
            catch (Exception error) { return error; }
            throw new InvalidOperationException("Expected exception was not thrown.");
        }

        private static StatisticsInstrument Instrument(string name, params string[] frames)
        {
            StatisticsInstrument instrument = new StatisticsInstrument { Name = name, FullName = "Full " + name };
            foreach (string frame in frames) instrument.CandleFiles.Add(frame, name + "/" + frame + "/" + name + ".txt");
            return instrument;
        }

        private static List<StatisticsPair> Pairs(params StatisticsInstrument[] instruments)
        {
            return new StatisticsPairCatalog().BuildPairs(instruments, "SR", "SP", CancellationToken.None);
        }

        #endregion

        #region Catalog

        private static void CatalogMatchesOnlySameContractCode()
        {
            List<StatisticsPair> pairs = Pairs(Instrument("srH6", "Min1"), Instrument("SPh6", "Min1"),
                Instrument("SRM6", "Min1"), Instrument("SPM26", "Min1"),
                Instrument("SRjunk", "Min1"), Instrument("SPjunk", "Min1"),
                Instrument("SRH", "Min1"), Instrument("SPH", "Min1"),
                Instrument("SRH１２", "Min1"), Instrument("SPH１２", "Min1"),
                Instrument("SRH12345", "Min1"), Instrument("SPH12345", "Min1"),
                Instrument("SR", "Min1"), Instrument("SP", "Min1"));
            Equal(1, pairs.Count);
            Equal("srH6", pairs[0].First.Name);
            Equal("SPh6", pairs[0].Second.Name);
            Equal("H6", pairs[0].Expiry);
            Equal("Full srH6", pairs[0].First.FullName);
        }

        private static void CatalogRejectsAmbiguityAndInvalidPrefixes()
        {
            Throws(() => Pairs(Instrument("SRH6", "Min1"), Instrument("srh6", "Min1"), Instrument("SPH6", "Min1")));
            foreach (string prefix in new[] { "", "S", "SRH", "S1", "СР" })
                Throws(() => new StatisticsPairCatalog().BuildPairs(new StatisticsInstrument[0], prefix, "SP", CancellationToken.None));
            Throws(() => new StatisticsPairCatalog().BuildPairs(new StatisticsInstrument[0], "SR", "sr", CancellationToken.None));
        }

        private static void CatalogRequiresCommonTimeFrame()
        {
            Equal(0, Pairs(Instrument("SRH6", "Min1"), Instrument("SPH6", "Min5")).Count);
            List<StatisticsPair> pairs = Pairs(Instrument("SRH6", "Min1", "Min5", "Hour1"), Instrument("SPH6", "Min5", "Day"));
            Equal(1, pairs[0].TimeFrames.Count);
            Equal("Min5", pairs[0].TimeFrames[0]);
        }

        private static void TimeFrameDurationsAreExplicit()
        {
            Equal(TimeSpan.FromSeconds(20), StatisticsPairCatalog.GetTimeFrameDuration("Sec20"));
            Equal(TimeSpan.FromMinutes(45), StatisticsPairCatalog.GetTimeFrameDuration("Min45"));
            Equal(TimeSpan.FromHours(4), StatisticsPairCatalog.GetTimeFrameDuration("Hour4"));
            Equal(TimeSpan.FromDays(1), StatisticsPairCatalog.GetTimeFrameDuration("Day"));
            Throws(() => StatisticsPairCatalog.GetTimeFrameDuration("Tick"));
        }

        #endregion

        #region Rolling and deviations

        private static void SmaIncludesCurrentAndPreservesSources()
        {
            PairResearchResult result = Analyze(new decimal[] { 0, 2, 4, 8 }, 3);
            Equal(4, result.Samples.Count);
            Equal<decimal?>(null, result.Samples[0].Sma);
            Equal<decimal?>(null, result.Samples[1].Deviation);
            Equal<decimal?>(2m, result.Samples[2].Sma);
            Equal<decimal?>(2m, result.Samples[2].Deviation);
            Near(Math.Sqrt(8.0 / 3), result.Samples[2].Sigma.Value);
            Equal(104m, result.Samples[2].PriceA);
            Equal(100m, result.Samples[2].PriceB);
            Equal(3m, result.Samples[2].VolumeA);
            Equal(4m, result.Samples[2].VolumeB);
            Near(14.0 / 3, (double)result.Samples[3].Sma.Value);
        }

        private static void ResearchWarmupAndConstantSeries()
        {
            PairResearchResult shortResult = Analyze(new decimal[] { 1, 2 }, 3);
            Equal<PairDeviationSummary>(null, shortResult.Deviations);
            Equal(0, shortResult.Episodes.Count);
            PairResearchResult constant = Analyze(new decimal[] { 7, 7, 7, 7, 7 }, 3);
            Equal(3, constant.Deviations.Count);
            Equal(0m, constant.Deviations.MeanAbsolute);
            Near(0, constant.Deviations.StandardDeviation);
            Equal<double?>(null, constant.Samples[4].ZScore);
            Near(0, constant.Samples[4].Sigma.Value);
            Equal(12, constant.EpisodeSummaries.Count);
        }

        private static void PeriodOneResetsEveryValue()
        {
            PairResearchResult result = Analyze(new decimal[] { 1, -7, 15, 15, 0 }, 1);
            foreach (PairResearchSample sample in result.Samples)
            {
                Equal(sample.Basis, sample.Sma.Value);
                Equal<decimal?>(0, sample.Deviation);
                Near(0, sample.Sigma.Value);
                Equal<double?>(null, sample.ZScore);
            }
            Equal(0, result.Episodes.Count);
        }

        private static void DeviationPercentilesUseLinearInterpolation()
        {
            PairDeviationSummary summary = Analyze(new decimal[] { 0, 2, 6, 12, 20 }, 2).Deviations;
            Equal(4, summary.Count);
            Equal(2.5m, summary.MeanAbsolute);
            Equal(2.5m, summary.P50);
            Equal(3.25m, summary.P75);
            Equal(3.7m, summary.P90);
            Equal(3.85m, summary.P95);
            Equal(3.97m, summary.P99);
            Equal(4m, summary.MaximumUp);
            Equal(0m, summary.MaximumDown);
            Near(Math.Sqrt(1.25), summary.StandardDeviation);
        }

        private static void SignedDeviationPopulationSigma()
        {
            PairDeviationSummary summary = Analyze(new decimal[] { 0, 2, 0, 4, 0 }, 2).Deviations;
            Equal(1.5m, summary.MeanAbsolute);
            Near(Math.Sqrt(2.5), summary.StandardDeviation);
            Equal(2m, summary.MaximumUp);
            Equal(2m, summary.MaximumDown);
        }

        private static void RollingMatchesFreshWindowsAtLargePrices()
        {
            const int period = 17;
            decimal[] values = new decimal[2000];
            Random random = new Random(2026);
            for (int i = 0; i < values.Length; i++) values[i] = 100000000000000000000m + random.Next(-200, 201) / 100m;
            PairResearchResult result = Analyze(values, period);
            for (int i = period - 1; i < values.Length; i++)
            {
                decimal origin = values[i - period + 1];
                decimal sumOffsets = 0;
                for (int j = i - period + 1; j <= i; j++) sumOffsets += values[j] - origin;
                double averageOffset = (double)(sumOffsets / period);
                double squares = 0;
                for (int j = i - period + 1; j <= i; j++)
                {
                    double difference = (double)(values[j] - origin) - averageOffset;
                    squares += difference * difference;
                }
                Near(Math.Sqrt(squares / period), result.Samples[i].Sigma.Value, 0.000000001);
                Near(0, (double)(result.Samples[i].Sma.Value - (origin + sumOffsets / period)), 0.000001);
            }
        }

        #endregion

        #region Episodes

        private static void NoEntryOnFirstValidZ()
        {
            PairResearchResult result = Analyze(new decimal[] { 0, 0, 0, 0, 1 }, 5);
            Near(2, result.Samples[4].ZScore.Value);
            Equal(0, result.Episodes.Count);
        }

        private static void ExactThresholdAndMultipleLevels()
        {
            PairResearchResult result = Analyze(new decimal[] { 1, 0, 0, 0, 0, 1 }, 5);
            Near(2, result.Samples[5].ZScore.Value);
            PairResearchEpisode[] positive = result.Episodes.Where(item => item.Direction == 1).ToArray();
            Equal(4, positive.Length);
            Equal(2.0, positive[3].SigmaMultiple);
            Equal("HistoryEnd", positive[3].Status);
            Equal(0.8m, positive[3].EntryThreshold);
            Equal(1, positive[3].ObservedBars);
        }

        private static void ReturnUsesDynamicSmaAndFixedEntrySigma()
        {
            PairResearchResult result = Analyze(new decimal[] { 2, 0, 2, 10, 0 }, 2);
            PairResearchEpisode episode = result.Episodes.Single(item => item.Direction == 1 && item.SigmaMultiple == 0.5);
            Equal(Start.AddMinutes(2), episode.Start);
            Equal(Start.AddMinutes(4), episode.End);
            Equal("Returned", episode.Status);
            Near(2, episode.DurationMinutes);
            Near(1, episode.EntrySigma);
            Equal(0.5m, episode.EntryThreshold);
            Equal(1m, episode.EntryDeviation);
            Equal(4m, episode.MaximumAdverseDeviation);
            Equal(3m, episode.FurtherAdverseDeviation);
            Equal(3, episode.ObservedBars);
        }

        private static void NegativeEpisodesMirrorPositive()
        {
            PairResearchResult result = Analyze(new decimal[] { -2, 0, -2, -10, 0 }, 2);
            PairResearchEpisode episode = result.Episodes.Single(item => item.Direction == -1 && item.SigmaMultiple == 0.5);
            Equal("Returned", episode.Status);
            Equal(-1m, episode.EntryDeviation);
            Equal(4m, episode.MaximumAdverseDeviation);
            Equal(3m, episode.FurtherAdverseDeviation);
        }

        private static void NoOverlappingEpisodeAtSameThreshold()
        {
            PairResearchResult result = Analyze(new decimal[] { 0, 1, 2, 3, 4, 5, 8, 9, 13, 14, 14, 14, 14, 14 }, 5);
            PairResearchEpisode[] selected = result.Episodes.Where(item => item.Direction == 1 && item.SigmaMultiple == 1.5).ToArray();
            Equal(1, selected.Length);
            Equal(Start.AddMinutes(6), selected[0].Start);
            Equal("Returned", selected[0].Status);
        }

        private static void ZeroSigmaReturnsButDoesNotCreateCrossing()
        {
            PairResearchResult result = Analyze(new decimal[] { 2, 0, 2, 2, 4 }, 2);
            PairResearchEpisode[] selected = result.Episodes.Where(item => item.Direction == 1 && item.SigmaMultiple == 0.5).ToArray();
            Equal(1, selected.Length);
            Equal("Returned", selected[0].Status);
            Equal(Start.AddMinutes(3), selected[0].End);
            Equal<double?>(null, result.Samples[3].ZScore);
            Near(0, result.Samples[3].Sigma.Value);
        }

        private static void GapsResetWarmupAndCensorAtLastObservation()
        {
            PairResearchResult result = Analyze(new decimal[] { 2, 0, 2, 3, 2, 0, 2 }, 2,
                new[] { 0, 1, 2, 3, 10, 11, 12 });
            PairResearchEpisode[] selected = result.Episodes.Where(item => item.Direction == 1 && item.SigmaMultiple == 0.5).ToArray();
            Equal(2, selected.Length);
            Equal("Gap", selected[0].Status);
            Equal(Start.AddMinutes(3), selected[0].End);
            Near(1, selected[0].DurationMinutes);
            Equal("HistoryEnd", selected[1].Status);
            Equal<decimal?>(null, result.Samples[4].Sma);
            Equal(1, result.Samples[4].Segment);
        }

        private static void AcceptedIntervalBoundaryResetsSma()
        {
            Fixture fixture = Create(new decimal[] { 2, 0, 2, 3 });
            fixture.Accepted.Intervals = new List<PairStatisticsInterval>
            {
                new PairStatisticsInterval { Start = Start, End = Start.AddMinutes(2) },
                new PairStatisticsInterval { Start = Start.AddMinutes(3), End = Start.AddMinutes(3) }
            };
            PairResearchResult result = Analyze(fixture, 2);
            Equal<decimal?>(null, result.Samples[3].Sma);
            Equal("Gap", result.Episodes[0].Status);
            Equal(Start.AddMinutes(2), result.Episodes[0].End);
        }

        private static void CompletedDurationsExcludeCensoredEpisodes()
        {
            PairResearchResult result = Analyze(new decimal[] { 2, 0, 2, 10, 0, 2, 3 }, 2);
            PairEpisodeSummary summary = result.EpisodeSummaries.Single(item => item.Direction == 1 && item.SigmaMultiple == 0.5);
            Equal(2, summary.Count);
            Equal(1, summary.Returned);
            Equal(1, summary.HistoryEnd);
            Equal(0, summary.Gap);
            Equal<double?>(2, summary.MeanMinutes);
            Equal<double?>(2, summary.MedianMinutes);
            Equal<double?>(2, summary.P90Minutes);
            Equal<double?>(2, summary.MaximumMinutes);
            Equal<decimal?>(3, summary.MaximumFurtherDeviation);
            foreach (PairEpisodeSummary group in result.EpisodeSummaries)
                Equal(group.Count, group.Returned + group.Gap + group.HistoryEnd);
            PairEpisodeSummary empty = result.EpisodeSummaries.Single(item => item.Direction == 1 && item.SigmaMultiple == 3);
            Equal<double?>(null, empty.MeanMinutes);
            Equal<decimal?>(null, empty.MaximumFurtherDeviation);
        }

        #endregion

        #region Validation and cancellation

        private static void ResearchRejectsInvalidAndMismatchedInputs()
        {
            Fixture fixture = Create(new decimal[] { 0, 1, 2 });
            Throws(() => Analyze(fixture, 0));
            Throws(() => new PairResearchAnalyzer().Analyze(fixture.Accepted, fixture.First, fixture.Second,
                new PairResearchOptions { TimeFrameDuration = TimeSpan.Zero }, CancellationToken.None));
            fixture.Accepted.Points[1].Value++;
            Throws(() => Analyze(fixture, 2));
            fixture = Create(new decimal[] { 0, 1, 2 });
            fixture.Second.RemoveAt(1);
            Throws(() => Analyze(fixture, 2));
            fixture = Create(new decimal[] { 0, 1, 2 });
            fixture.Accepted.Intervals.Clear();
            Throws(() => Analyze(fixture, 2));
        }

        private static void ResearchUsesAcceptedPointsOnly()
        {
            Fixture fixture = Create(new decimal[] { 0, 1000, 4, 6 });
            fixture.First[1].Volume = 0;
            fixture.Accepted = new PairStatisticsAnalyzer().Analyze(fixture.First, fixture.Second,
                new PairStatisticsOptions { MinCoveragePercent = 0 }, CancellationToken.None);
            PairResearchResult result = Analyze(fixture, 2);
            Equal(3, result.Samples.Count);
            Equal(Start.AddMinutes(2), result.Samples[1].Time);
            Equal<decimal?>(null, result.Samples[1].Sma);
            Equal<decimal?>(5, result.Samples[2].Sma);
        }

        private static void ResearchCancellationAndCatalogCancellation()
        {
            Fixture fixture = Create(new decimal[] { 1, 2 });
            Equal(true, Throws(() => new PairResearchAnalyzer().Analyze(fixture.Accepted, fixture.First, fixture.Second,
                new PairResearchOptions(), new CancellationToken(true))) is OperationCanceledException);
            Equal(true, Throws(() => new StatisticsPairCatalog().BuildPairs(new StatisticsInstrument[0], "SR", "SP",
                new CancellationToken(true))) is OperationCanceledException);
        }

        private static void ResearchCancellationDuringInputValidation()
        {
            Fixture fixture = Create(new decimal[] { 1, 2, 3 });
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                IReadOnlyList<StatisticsCandle> source = new CancellingCandles(fixture.First, cancellation);
                Equal(true, Throws(() => new PairResearchAnalyzer().Analyze(fixture.Accepted, source, fixture.Second,
                    new PairResearchOptions { SmaPeriod = 2 }, cancellation.Token)) is OperationCanceledException);
            }
        }

        private sealed class CancellingCandles : IReadOnlyList<StatisticsCandle>
        {
            private readonly List<StatisticsCandle> _candles;
            private readonly CancellationTokenSource _cancellation;
            public CancellingCandles(List<StatisticsCandle> candles, CancellationTokenSource cancellation)
            {
                _candles = candles;
                _cancellation = cancellation;
            }
            public int Count { get { return _candles.Count; } }
            public StatisticsCandle this[int index]
            {
                get { if (index == 1) _cancellation.Cancel(); return _candles[index]; }
            }
            public IEnumerator<StatisticsCandle> GetEnumerator() { return _candles.GetEnumerator(); }
            IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
        }

        #endregion
    }
}
