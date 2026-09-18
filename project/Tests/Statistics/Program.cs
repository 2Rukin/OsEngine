/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using OsEngine.OsData.Statistics;

namespace OsEngine.Statistics.Tests
{
    internal static class Program
    {
        private static readonly DateTime FirstDay = new DateTime(2026, 2, 17, 9, 0, 0);
        private static int _passed;

        #region Runner

        private static int Main()
        {
            Action[] tests =
            {
                FormulaAndPopulationSummary, ReversingLegsReversesSign, ConstantAndSinglePoint,
                ExactTimestampJoinDoesNotFill, CoverageUsesActiveUnion, ZeroVolumeIsExcluded,
                InclusiveThresholds, EachLegMustPass, MinimumActiveBarsAppliedToEachLeg,
                NoCommonData, NoAcceptedDays, NegativePricesAreValid,
                CalendarGapSplitsIntervals, RejectedDaySplitsIntervals, AdjacentDaysShareInterval,
                SummaryIsWeightedBySamplesNotDays, DateRangeIsInclusive,
                InvalidOptionsAreRejected, UnsortedAndDuplicateSeriesAreRejected,
                AnalyzerCancellation, ReaderParsesSevenAndEightColumns,
                ReaderIsCultureIndependent, ReaderFiltersDates,
                ReaderRejectsBadRowsWithLocation, ReaderRejectsDuplicateAndUnsortedTimes,
                ReaderCancellation, CatalogCurrentAndLegacy,
                CatalogUsesActualFilesNotEnabledFlags, CatalogDoesNotWrite,
                CatalogRejectsUnsafeNamesAndCollisions, CatalogRejectsMissingSettings,
                LargePricePrecision, RepresentableMeanDoesNotOverflow,
                CancellationDuringIteration, CandleLimitIsEnforced, FileIsReleasedAfterReadFailure
            };

            try
            {
                foreach (Action test in tests)
                {
                    test();
                    _passed++;
                    Console.WriteLine("PASS " + test.Method.Name);
                }

                Console.WriteLine("Passed " + _passed + "/" + tests.Length);
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("FAIL after " + _passed + " tests. " + error);
                return 1;
            }
        }

        private static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException("Expected " + expected + "; actual " + actual);
            }
        }

        private static void Near(double expected, double actual)
        {
            if (!double.IsFinite(actual) || Math.Abs(expected - actual) > 0.00000001)
            {
                throw new InvalidOperationException("Expected " + expected + "; actual " + actual);
            }
        }

        private static Exception Throws(Action action)
        {
            try
            {
                action();
            }
            catch (Exception error)
            {
                return error;
            }

            throw new InvalidOperationException("Expected exception was not thrown.");
        }

        private static void Cancelled(Action action)
        {
            Exception error = Throws(action);
            Equal(true, error is OperationCanceledException);
        }

        #endregion

        #region Analysis

        private static StatisticsCandle Bar(int minute, decimal close, decimal volume = 1, int day = 0)
        {
            return new StatisticsCandle
            {
                Time = FirstDay.AddDays(day).AddMinutes(minute),
                Open = close, High = close, Low = close, Close = close, Volume = volume
            };
        }

        private static PairStatisticsResult Analyze(StatisticsCandle[] first, StatisticsCandle[] second,
            PairStatisticsOptions options = null)
        {
            return new PairStatisticsAnalyzer().Analyze(first, second,
                options ?? new PairStatisticsOptions(), CancellationToken.None);
        }

        private static void FormulaAndPopulationSummary()
        {
            PairStatisticsResult result = Analyze(
                new[] { Bar(0, 10), Bar(1, 20), Bar(2, 30) },
                new[] { Bar(0, 13), Bar(1, 19), Bar(2, 25) });
            Equal(3, result.Summary.Count);
            Equal(-3m, result.Summary.Minimum);
            Equal(5m, result.Summary.Maximum);
            Equal(8m, result.Summary.Range);
            Equal(1m, result.Summary.Mean);
            Near(Math.Sqrt(32.0 / 3), result.Summary.StandardDeviation);
            Equal(FirstDay, result.Summary.Start);
            Equal(FirstDay.AddMinutes(2), result.Summary.End);
            Equal(1, result.Intervals.Count);
        }

        private static void ReversingLegsReversesSign()
        {
            StatisticsCandle[] first = { Bar(0, 10), Bar(1, 20) };
            StatisticsCandle[] second = { Bar(0, 12), Bar(1, 15) };
            PairStatisticsSummary forward = Analyze(first, second).Summary;
            PairStatisticsSummary reverse = Analyze(second, first).Summary;
            Equal(-forward.Maximum, reverse.Minimum);
            Equal(-forward.Minimum, reverse.Maximum);
            Equal(forward.Range, reverse.Range);
            Equal(-forward.Mean, reverse.Mean);
        }

        private static void ConstantAndSinglePoint()
        {
            PairStatisticsSummary single = Analyze(new[] { Bar(0, 4) }, new[] { Bar(0, 7) }).Summary;
            Equal(-3m, single.Mean);
            Equal(0m, single.Range);
            Near(0, single.StandardDeviation);
            PairStatisticsSummary constant = Analyze(new[] { Bar(0, 4), Bar(1, 5) },
                new[] { Bar(0, 7), Bar(1, 8) }).Summary;
            Near(0, constant.StandardDeviation);
        }

        private static void ExactTimestampJoinDoesNotFill()
        {
            PairStatisticsResult result = Analyze(new[] { Bar(0, 10), Bar(2, 14) },
                new[] { Bar(1, 100), Bar(2, 11) }, new PairStatisticsOptions { MinCoveragePercent = 0 });
            Equal(1, result.Points.Count);
            Equal(3m, result.Points[0].Value);
            Equal(FirstDay.AddMinutes(2), result.Points[0].Time);
            Equal(1, result.Days[0].MatchedBars);
        }

        private static void CoverageUsesActiveUnion()
        {
            PairStatisticsResult result = Analyze(new[] { Bar(0, 10, 7), Bar(1, 11, 3) },
                new[] { Bar(1, 5, 2), Bar(2, 6, 4) }, new PairStatisticsOptions { MinCoveragePercent = 34 });
            PairStatisticsDay day = result.Days[0];
            Equal(10m, day.VolumeA);
            Equal(6m, day.VolumeB);
            Equal(2, day.ActiveBarsA);
            Equal(2, day.ActiveBarsB);
            Near(100.0 / 3, (double)day.CoveragePercent);
            Equal(false, day.Accepted);
            Equal<PairStatisticsSummary>(null, result.Summary);
        }

        private static void ZeroVolumeIsExcluded()
        {
            PairStatisticsResult result = Analyze(new[] { Bar(0, 5000, 0), Bar(1, 10) },
                new[] { Bar(0, 1), Bar(1, 8) }, new PairStatisticsOptions { MinCoveragePercent = 50 });
            Equal(1, result.Points.Count);
            Equal(2m, result.Summary.Minimum);
            Equal(50m, result.Days[0].CoveragePercent);
            Equal(1, result.Days[0].ActiveBarsA);
        }

        private static void InclusiveThresholds()
        {
            PairStatisticsResult result = Analyze(new[] { Bar(0, 10, 5), Bar(1, 20, 5) },
                new[] { Bar(0, 9, 7) }, new PairStatisticsOptions
                {
                    MinVolumeA = 10, MinVolumeB = 7, MinActiveBarsA = 2,
                    MinActiveBarsB = 1, MinCoveragePercent = 50
                });
            Equal(true, result.Days[0].Accepted);
        }

        private static void EachLegMustPass()
        {
            StatisticsCandle[] first = { Bar(0, 10, 100) };
            StatisticsCandle[] second = { Bar(0, 8, 2) };
            Equal(false, Analyze(first, second, new PairStatisticsOptions { MinVolumeB = 3 }).Days[0].Accepted);
            Equal(false, Analyze(first, second, new PairStatisticsOptions { MinVolumeA = 101 }).Days[0].Accepted);
        }

        private static void MinimumActiveBarsAppliedToEachLeg()
        {
            StatisticsCandle[] first = { Bar(0, 10), Bar(1, 20) };
            StatisticsCandle[] second = { Bar(0, 8) };
            Equal(false, Analyze(first, second, new PairStatisticsOptions
                { MinActiveBarsB = 2, MinCoveragePercent = 0 }).Days[0].Accepted);
            Equal(false, Analyze(first, second, new PairStatisticsOptions
                { MinActiveBarsA = 3, MinCoveragePercent = 0 }).Days[0].Accepted);
        }

        private static void NoCommonData()
        {
            PairStatisticsResult result = Analyze(new[] { Bar(0, 10) }, new[] { Bar(1, 5) },
                new PairStatisticsOptions { MinCoveragePercent = 0 });
            Equal<PairStatisticsSummary>(null, result.Summary);
            Equal(0, result.Points.Count);
            Equal(0, result.Intervals.Count);
            Equal(0m, result.Days[0].CoveragePercent);
            Equal(false, result.Days[0].Accepted);
            Equal<PairStatisticsSummary>(null, Analyze(Array.Empty<StatisticsCandle>(),
                Array.Empty<StatisticsCandle>()).Summary);
        }

        private static void NoAcceptedDays()
        {
            PairStatisticsResult result = Analyze(new[] { Bar(0, 10, 0) }, new[] { Bar(0, 5, 0) });
            Equal(1, result.Days.Count);
            Equal(false, result.Days[0].Accepted);
            Equal<PairStatisticsSummary>(null, result.Days[0].Summary);
            Equal<PairStatisticsSummary>(null, result.Summary);
        }

        private static void NegativePricesAreValid()
        {
            Equal(-15m, Analyze(new[] { Bar(0, -20) }, new[] { Bar(0, -5) }).Summary.Mean);
        }

        private static void CalendarGapSplitsIntervals()
        {
            PairStatisticsResult result = Analyze(new[] { Bar(0, 10), Bar(0, 11, day: 3) },
                new[] { Bar(0, 5), Bar(0, 4, day: 3) });
            Equal(2, result.Intervals.Count);
            Equal(1, result.Intervals[0].Days);
        }

        private static void RejectedDaySplitsIntervals()
        {
            PairStatisticsResult result = Analyze(new[] { Bar(0, 10), Bar(0, 11, day: 1), Bar(0, 12, day: 2) },
                new[] { Bar(0, 5), Bar(0, 6, volume: 0, day: 1), Bar(0, 7, day: 2) });
            Equal(3, result.Days.Count);
            Equal(2, result.Intervals.Count);
            Equal(2, result.Points.Count);
        }

        private static void AdjacentDaysShareInterval()
        {
            PairStatisticsResult result = Analyze(new[] { Bar(0, 10), Bar(0, 20, day: 1) },
                new[] { Bar(0, 6), Bar(0, 10, day: 1) });
            Equal(1, result.Intervals.Count);
            Equal(2, result.Intervals[0].Days);
            Equal(6m, result.Intervals[0].Summary.Range);
        }

        private static void SummaryIsWeightedBySamplesNotDays()
        {
            PairStatisticsResult result = Analyze(new[] { Bar(0, 10), Bar(0, 20, day: 1), Bar(1, 20, day: 1) },
                new[] { Bar(0, 10), Bar(0, 10, day: 1), Bar(1, 10, day: 1) });
            Near(20.0 / 3, (double)result.Summary.Mean);
            Equal(10m, result.Summary.Range);
            Near(Math.Sqrt(200.0 / 9), result.Summary.StandardDeviation);
        }

        private static void DateRangeIsInclusive()
        {
            StatisticsCandle[] first = { Bar(0, 10), Bar(0, 20, day: 1), Bar(0, 30, day: 2) };
            StatisticsCandle[] second = { Bar(0, 1), Bar(0, 1, day: 1), Bar(0, 1, day: 2) };
            PairStatisticsResult result = Analyze(first, second, new PairStatisticsOptions
                { From = FirstDay.Date.AddDays(1), To = FirstDay.Date.AddDays(1) });
            Equal(1, result.Points.Count);
            Equal(19m, result.Summary.Mean);
        }

        private static void InvalidOptionsAreRejected()
        {
            StatisticsCandle[] data = { Bar(0, 10) };
            PairStatisticsOptions[] invalid =
            {
                new PairStatisticsOptions { MinVolumeA = -1 },
                new PairStatisticsOptions { MinVolumeB = -1 },
                new PairStatisticsOptions { MinActiveBarsA = 0 },
                new PairStatisticsOptions { MinActiveBarsB = 0 },
                new PairStatisticsOptions { MinCoveragePercent = -1 },
                new PairStatisticsOptions { MinCoveragePercent = 101 },
                new PairStatisticsOptions { From = FirstDay.AddDays(1), To = FirstDay }
            };
            foreach (PairStatisticsOptions options in invalid)
            {
                Throws(() => Analyze(data, data, options));
            }
        }

        private static void UnsortedAndDuplicateSeriesAreRejected()
        {
            StatisticsCandle[] other = { Bar(0, 1) };
            Throws(() => Analyze(new[] { Bar(0, 2), Bar(0, 3) }, other));
            Throws(() => Analyze(new[] { Bar(1, 2), Bar(0, 3) }, other));
            Throws(() => Analyze(new[] { Bar(0, 2, -1) }, other));
        }

        private static void AnalyzerCancellation()
        {
            CancellationToken token = new CancellationToken(true);
            Cancelled(() => new PairStatisticsAnalyzer().Analyze(new[] { Bar(0, 2) },
                new[] { Bar(0, 1) }, new PairStatisticsOptions(), token));
        }

        private static void LargePricePrecision()
        {
            decimal large = 100000000000000000000m;
            PairStatisticsSummary summary = Analyze(new[] { Bar(0, large), Bar(1, large + 2) },
                new[] { Bar(0, 0), Bar(1, 0) }).Summary;
            Equal(2m, summary.Range);
            Equal(large + 1, summary.Mean);
            Near(1, summary.StandardDeviation);
        }

        private static void RepresentableMeanDoesNotOverflow()
        {
            PairStatisticsSummary summary = Analyze(
                new[] { Bar(0, 0), Bar(1, decimal.MaxValue), Bar(2, decimal.MaxValue) },
                new[] { Bar(0, 0), Bar(1, 0), Bar(2, 0) }).Summary;
            Equal(decimal.MaxValue, summary.Range);
            Equal(true, summary.Mean > decimal.MaxValue / 2 && summary.Mean < decimal.MaxValue);
            Equal(true, double.IsFinite(summary.StandardDeviation));
        }

        private static void CancellationDuringIteration()
        {
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                GeneratedCandles candles = new GeneratedCandles(10, cancellation);
                Cancelled(() => new PairStatisticsAnalyzer().Analyze(candles,
                    Array.Empty<StatisticsCandle>(), new PairStatisticsOptions(), cancellation.Token));
                Equal(true, candles.Reads > 1);
            }
        }

        private static void CandleLimitIsEnforced()
        {
            GeneratedCandles candles = new GeneratedCandles(StatisticsDatasetReader.MaximumCandles + 1);
            Exception error = Throws(() => new PairStatisticsAnalyzer().Analyze(candles,
                Array.Empty<StatisticsCandle>(), new PairStatisticsOptions(), CancellationToken.None));
            Equal(true, error is InvalidDataException);
            Equal(true, error.Message.Contains("2,000,000", StringComparison.Ordinal));
            Equal(StatisticsDatasetReader.MaximumCandles + 1, candles.Reads);
        }

        private sealed class GeneratedCandles : IReadOnlyList<StatisticsCandle>
        {
            private readonly CancellationTokenSource _cancellation;
            public int Count { get; }
            public int Reads { get; private set; }

            public GeneratedCandles(int count, CancellationTokenSource cancellation = null)
            {
                Count = count;
                _cancellation = cancellation;
            }

            public StatisticsCandle this[int index]
            {
                get
                {
                    Reads++;
                    if (Reads == 3)
                    {
                        _cancellation?.Cancel();
                    }
                    return Bar(index, 1);
                }
            }

            public IEnumerator<StatisticsCandle> GetEnumerator()
            {
                for (int index = 0; index < Count; index++)
                {
                    yield return this[index];
                }
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        #endregion

        #region Dataset reader

        private static void InDataset(Action<string> action)
        {
            string folder = Path.Combine(Path.GetTempPath(), "OsEngineStatisticsTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                action(folder);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        private static string WriteCandles(string folder, string content)
        {
            string path = Path.Combine(folder, "candles.txt");
            File.WriteAllText(path, content);
            return path;
        }

        private static List<StatisticsCandle> Read(string path, DateTime? from = null, DateTime? to = null)
        {
            return new StatisticsDatasetReader().ReadCandles(path, from, to, CancellationToken.None);
        }

        private static void ReaderParsesSevenAndEightColumns()
        {
            InDataset(folder =>
            {
                List<StatisticsCandle> candles = Read(WriteCandles(folder,
                    "20260217,090000,4.347,4.350,4.340,4.348,10\n" +
                    "20260217,090100,-5,-3,-6,-4,2,0\n"));
                Equal(2, candles.Count);
                Equal(4.348m, candles[0].Close);
                Equal(10m, candles[0].Volume);
                Equal(-4m, candles[1].Close);
            });
        }

        private static void ReaderIsCultureIndependent()
        {
            CultureInfo previous = CultureInfo.CurrentCulture;
            try
            {
                foreach (string culture in new[] { "ru-RU", "en-US" })
                {
                    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                    InDataset(folder => Equal(4.347m, Read(WriteCandles(folder,
                        "20260217,090000,4.347,4.347,4.347,4.347,1,0\n"))[0].Close));
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        private static void ReaderFiltersDates()
        {
            InDataset(folder =>
            {
                string path = WriteCandles(folder,
                    "20260217,090000,1,1,1,1,1\n20260218,235959,2,2,2,2,1\n20260219,000000,3,3,3,3,1\n");
                List<StatisticsCandle> candles = Read(path, FirstDay.Date.AddDays(1), FirstDay.Date.AddDays(1));
                Equal(1, candles.Count);
                Equal(2m, candles[0].Close);
                Throws(() => Read(path, FirstDay.AddDays(1), FirstDay));
            });
        }

        private static void ReaderRejectsBadRowsWithLocation()
        {
            string[] malformed =
            {
                "20260230,090000,1,1,1,1,1", "20260217,250000,1,1,1,1,1",
                "20260217,090000,1,1,1,1,-1", "20260217,090000,1,1,1,1,no-volume",
                "20260217,090000,5,4,1,3,1", "20260217,090000,1,5,2,3,1",
                "20260217,090000,1,2,1,3,1", "20260217,090000,1,1,1,1",
                "20260217,090000,1,1,1,1,1,bad-oi", "20260217,090000,NaN,1,1,1,1",
                "202602170,90000,1,1,1,1,1"
            };
            foreach (string row in malformed)
            {
                InDataset(folder =>
                {
                    string path = WriteCandles(folder, row + "\n");
                    Exception error = Throws(() => Read(path));
                    Equal(true, error.Message.Contains("candles.txt", StringComparison.Ordinal));
                    Equal(true, error.Message.Contains("1", StringComparison.Ordinal));
                });
            }
        }

        private static void ReaderRejectsDuplicateAndUnsortedTimes()
        {
            InDataset(folder =>
            {
                string same = "20260217,090000,1,1,1,1,1\n";
                Throws(() => Read(WriteCandles(folder, same + same)));
                Throws(() => Read(WriteCandles(folder, "20260217,090100,1,1,1,1,1\n" + same)));
            });
        }

        private static void ReaderCancellation()
        {
            InDataset(folder =>
            {
                string path = WriteCandles(folder, "20260217,090000,1,1,1,1,1\n");
                Cancelled(() => new StatisticsDatasetReader().ReadCandles(path, null, null,
                    new CancellationToken(true)));
            });
        }

        private const string BaseSettings = "On%False%False%False%False%False%False%False%True%False%False%False%False%False%False%False%False%False%False%MoexDataServer%01/01/2024 00:00:00%09/22/2026 00:00:00%5%False%False%MoexDataServer%False%";

        private static string CurrentSecurity(string name, string fullName)
        {
            return name + "~" + name + "#futures#forts#RFUD~Фьючерсы~False~~Set_Test~0~0~" + BaseSettings + "~" + fullName;
        }

        private static void CreateSecurityFile(string folder, string name, string timeframe = "Min1")
        {
            string directory = Path.Combine(folder, name, timeframe);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, name + ".txt"), "20260217,090000,1,1,1,1,1\n");
        }

        private static void CatalogCurrentAndLegacy()
        {
            InDataset(folder =>
            {
                File.WriteAllText(Path.Combine(folder, "Settings.txt"), BaseSettings + "\n" +
                    CurrentSecurity("SRH6", "SR-3.26") + "\n" +
                    "SPH6~SPH6#futures#forts#RFUD~Фьючерсы~False~~Set_Test~" + BaseSettings + "~SP-3.26\n");
                CreateSecurityFile(folder, "SRH6");
                CreateSecurityFile(folder, "SPH6");
                List<StatisticsInstrument> items = new StatisticsDatasetReader().ReadCatalog(folder, CancellationToken.None);
                Equal(2, items.Count);
                StatisticsInstrument ordinary = items.Single(item => item.Name == "SRH6");
                StatisticsInstrument preferred = items.Single(item => item.Name == "SPH6");
                Equal("SR-3.26", ordinary.FullName);
                Equal("SP-3.26", preferred.FullName);
                Equal(true, File.Exists(ordinary.CandleFiles["Min1"]));
                Equal(true, ordinary.DisplayName.Contains("SRH6", StringComparison.Ordinal));
            });
        }

        private static void CatalogUsesActualFilesNotEnabledFlags()
        {
            InDataset(folder =>
            {
                File.WriteAllText(Path.Combine(folder, "Settings.txt"), BaseSettings + "\n" + CurrentSecurity("SRH6", "SR-3.26"));
                CreateSecurityFile(folder, "SRH6", "Min5");
                CreateSecurityFile(folder, "SRH6", "Tick");
                CreateSecurityFile(folder, "SRH6", "MarketDepth");
                CreateSecurityFile(folder, "SRH6", "Temp");
                StatisticsInstrument item = new StatisticsDatasetReader().ReadCatalog(folder, CancellationToken.None)[0];
                Equal(1, item.CandleFiles.Count);
                Equal(true, item.CandleFiles.ContainsKey("Min5"));
            });
        }

        private static void CatalogDoesNotWrite()
        {
            InDataset(folder =>
            {
                File.WriteAllText(Path.Combine(folder, "Settings.txt"), BaseSettings + "\n" + CurrentSecurity("SRH6", "SR-3.26"));
                string[] beforeFiles = Directory.GetFiles(folder, "*", SearchOption.AllDirectories);
                string[] beforeDirectories = Directory.GetDirectories(folder, "*", SearchOption.AllDirectories);
                string settingsBefore = File.ReadAllText(beforeFiles[0]);
                new StatisticsDatasetReader().ReadCatalog(folder, CancellationToken.None);
                Equal(beforeFiles.Length, Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Length);
                Equal(beforeDirectories.Length, Directory.GetDirectories(folder, "*", SearchOption.AllDirectories).Length);
                Equal(settingsBefore, File.ReadAllText(beforeFiles[0]));
            });
        }

        private static void CatalogRejectsUnsafeNamesAndCollisions()
        {
            InDataset(folder =>
            {
                string path = Path.Combine(folder, "Settings.txt");
                File.WriteAllText(path, BaseSettings + "\n" + CurrentSecurity("..", "bad"));
                Throws(() => new StatisticsDatasetReader().ReadCatalog(folder, CancellationToken.None));
                File.WriteAllText(path, BaseSettings + "\n" + CurrentSecurity("SRH6", "one") + "\n" + CurrentSecurity("SRH6", "two"));
                Throws(() => new StatisticsDatasetReader().ReadCatalog(folder, CancellationToken.None));
            });
        }

        private static void CatalogRejectsMissingSettings()
        {
            InDataset(folder => Throws(() => new StatisticsDatasetReader().ReadCatalog(folder, CancellationToken.None)));
        }

        private static void FileIsReleasedAfterReadFailure()
        {
            InDataset(folder =>
            {
                string path = WriteCandles(folder, "malformed\n");
                Throws(() => Read(path));
                using (FileStream exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    Equal(true, exclusive.CanWrite);
                }
            });
        }

        #endregion
    }
}
