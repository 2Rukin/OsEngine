/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using OsEngine.Entity;
using OsEngine.Market.Servers.Entity;
using OsEngine.OsData.BinaryEntity;
using OsEngine.OsData.OrderFlow;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;

namespace OsEngine.OrderFlowResearch.Tests
{
    /// <summary>
    /// Deterministic offline component test stand for the paired-QSH research
    /// path. It creates no network connection, broker order or persistent raw
    /// market fixture and does not prove profitability or live parity.
    /// </summary>
    /// <remarks>
    /// Canonical command from <c>project/</c>:
    /// <c>dotnet run --project Tests/OrderFlowResearch/OsEngine.OrderFlowResearch.Tests.csproj</c>.
    /// The stand has no external service, credential or market-data dependency.
    /// It creates no real order or position and changes no OsEngine setting.
    /// It creates a unique temporary directory and deletes it in <c>finally</c>;
    /// it has no internal timeout, so the invoking build job owns timeout policy.
    /// It covers the synthetic research risks registered by
    /// ORDER-FLOW-QUALIFICATION-001 but does not prove real-QSH, full-day,
    /// execution, profitability or live compatibility.
    /// </remarks>
    internal static class Program
    {
        private static int _passed;
        private static int _failed;

        private static int Main()
        {
            string root = Path.Combine(Path.GetTempPath(),
                "OsEngine-OrderFlowResearch-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            try
            {
                Run("valid causal replay and separated labels", root, TestValidCausalReplay);
                Run("short candidate is mirrored", root, TestShortCandidate);
                Run("deterministic repeat and write-once artifacts", root, TestDeterministicRepeat);
                Run("case variant names reuse canonical artifacts", root, TestCaseVariantIdentity);
                Run("same timestamp quote cannot alter candidate features", root, TestSameTimestampQuoteIsolation);
                Run("same timestamp deal order cannot alter features", root, TestSameTimestampDealOrderIsolation);
                Run("same timestamp barrier order is ambiguous", root, TestSameTimestampBarrierAmbiguity);
                Run("future suffix changes labels but not prior features", root, TestFutureSuffixIsolation);
                Run("mismatched file pair is rejected", root, TestMismatchedPairRejected);
                Run("disjoint source ranges are rejected", root, TestDisjointRangesRejected);
                Run("unknown deal side is rejected", root, TestUnknownSideRejected);
                Run("negative quote change count is rejected", root, TestNegativeQuoteCountRejected);
                Run("gzip QSH pair is supported", root, TestGzipPair);
                Run("deflate QSH pair is supported", root, TestDeflatePair);
                Run("truncated QSH frame is rejected", root, TestTruncatedFrameRejected);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }

            Console.WriteLine("Order Flow Research tests: " + _passed + " passed, " + _failed + " failed.");
            return _failed == 0 ? 0 : 1;
        }

        private static void Run(string name, string root, Action<string> test)
        {
            string testRoot = Path.Combine(root, Sanitize(name));
            Directory.CreateDirectory(testRoot);

            try
            {
                test(testRoot);
                _passed++;
                Console.WriteLine("PASS " + name);
            }
            catch (Exception error)
            {
                _failed++;
                Console.WriteLine("FAIL " + name + Environment.NewLine + error);
            }
        }

        private static void TestValidCausalReplay(string root)
        {
            SyntheticPair pair = SyntheticQshFactory.Create(root, "base", 101, 1000, 10, Side.Sell);
            OrderFlowResearchResult result = RunPair(pair, Path.Combine(root, "output"));

            AssertTrue(result.Quality.ResearchAccepted, "Valid synthetic pair must be accepted.");
            AssertEqual(4L, result.Quality.DealCount, "Deal count");
            AssertEqual(3L, result.Quality.QuoteCount, "Quote count");
            AssertEqual(1, result.Candidates.Count, "Candidate count");

            OrderFlowCandidate candidate = result.Candidates[0];
            AssertEqual(OrderFlowDirection.Long, candidate.Direction, "Candidate direction");
            OrderFlowObservation observation = result.Observations.Find(
                item => item.CandidateId == candidate.CandidateId);
            AssertNotNull(observation, "Candidate observation");
            AssertEqual(pair.StartTime, observation.Features.BookTime.Value,
                "Candidate must use the previous closed quote, not its same-timestamp quote.");
            AssertEqual(0m, observation.Features.BookImbalance, "Previous-book imbalance");

            OrderFlowMarketPathLabel label = result.Labels.Single(
                item => item.CandidateId == candidate.CandidateId && item.HorizonSeconds == 5);
            AssertTrue(label.IsComplete, "Five-second label must be complete.");
            AssertEqual(OrderFlowBarrierOutcome.TargetFirst, label.Outcome, "Future barrier outcome");
            AssertTrue(label.MaximumFavorableExcursion >= 1m, "MFE must include the future price rise.");

            AssertTrue(File.Exists(Path.Combine(result.ArtifactDirectory, "observations.csv")),
                "Causal observations artifact");
            AssertTrue(File.Exists(Path.Combine(result.ArtifactDirectory, "market-path-labels.csv")),
                "Future labels artifact");

            string observationHeader = File.ReadLines(Path.Combine(result.ArtifactDirectory, "observations.csv")).First();
            string labelHeader = File.ReadLines(Path.Combine(result.ArtifactDirectory, "market-path-labels.csv")).First();
            AssertFalse(observationHeader.Contains("outcome", StringComparison.OrdinalIgnoreCase),
                "Observation schema must not contain future outcome.");
            AssertFalse(observationHeader.Contains("mfe", StringComparison.OrdinalIgnoreCase),
                "Observation schema must not contain future MFE.");
            AssertFalse(labelHeader.Contains("book_imbalance", StringComparison.OrdinalIgnoreCase),
                "Label schema must not duplicate causal book features.");
            AssertEqual(3, result.Bars.Count, "Diagnostic timeframe count");
        }

        private static void TestDeterministicRepeat(string root)
        {
            SyntheticPair pair = SyntheticQshFactory.Create(root, "repeat", 101, 1000, 10, Side.Sell);
            string output = Path.Combine(root, "output");
            OrderFlowResearchResult first = RunPair(pair, output);
            OrderFlowResearchResult second = RunPair(pair, output);

            AssertEqual(first.NormalizedEventHash, second.NormalizedEventHash, "Event hash");
            AssertEqual(first.FeatureHash, second.FeatureHash, "Feature hash");
            AssertEqual(first.CandidateHash, second.CandidateHash, "Candidate hash");
            AssertEqual(first.ArtifactDirectory, second.ArtifactDirectory, "Idempotent artifact directory");
        }

        private static void TestCaseVariantIdentity(string root)
        {
            SyntheticPair source = SyntheticQshFactory.Create(root, "case-source", 101, 1000, 10, Side.Sell);
            string caseDirectory = Path.Combine(root, "case-variant");
            Directory.CreateDirectory(caseDirectory);
            string dealsPath = Path.Combine(caseDirectory, "test.2026-09-18.deals.qsh");
            string quotesPath = Path.Combine(caseDirectory, "test.2026-09-18.quotes.qsh");
            File.Copy(source.DealsPath, dealsPath);
            File.Copy(source.QuotesPath, quotesPath);

            SyntheticPair caseVariant = new SyntheticPair();
            caseVariant.DealsPath = dealsPath;
            caseVariant.QuotesPath = quotesPath;
            caseVariant.StartTime = source.StartTime;
            string output = Path.Combine(root, "output");

            OrderFlowResearchResult first = RunPair(source, output);
            OrderFlowResearchResult second = RunPair(caseVariant, output);

            AssertTrue(first.Quality.ResearchAccepted && second.Quality.ResearchAccepted,
                "Both case variants must be accepted.");
            AssertEqual(first.InputHash, second.InputHash, "Canonical case-variant input identity");
            AssertEqual(first.DealsHeader.FileName, second.DealsHeader.FileName,
                "Canonical Deals manifest filename");
            AssertEqual(first.QuotesHeader.FileName, second.QuotesHeader.FileName,
                "Canonical Quotes manifest filename");
            AssertEqual(first.ArtifactDirectory, second.ArtifactDirectory,
                "Case variants must reuse one byte-identical artifact bundle");
        }

        private static void TestShortCandidate(string root)
        {
            SyntheticPair pair = SyntheticQshFactory.Create(root, "short", 99, 1000, 10, Side.Buy);
            OrderFlowResearchResult result = RunPair(pair, Path.Combine(root, "output"));

            AssertTrue(result.Quality.ResearchAccepted, "Valid mirrored pair must be accepted.");
            AssertEqual(1, result.Candidates.Count, "Short candidate count");
            AssertEqual(OrderFlowDirection.Short, result.Candidates[0].Direction, "Mirrored candidate direction");
            OrderFlowMarketPathLabel label = result.Labels.Single(item => item.HorizonSeconds == 5);
            AssertEqual(OrderFlowBarrierOutcome.TargetFirst, label.Outcome, "Short favorable barrier outcome");
        }

        private static void TestSameTimestampQuoteIsolation(string root)
        {
            SyntheticPair positiveBook = SyntheticQshFactory.Create(root, "book-positive", 101, 1000, 10, Side.Sell);
            SyntheticPair negativeBook = SyntheticQshFactory.Create(root, "book-negative", 101, 10, 1000, Side.Sell);
            OrderFlowResearchResult first = RunPair(positiveBook, Path.Combine(root, "output-positive"));
            OrderFlowResearchResult second = RunPair(negativeBook, Path.Combine(root, "output-negative"));

            OrderFlowFeatureSnapshot firstFeature = GetOnlyCandidateFeature(first);
            OrderFlowFeatureSnapshot secondFeature = GetOnlyCandidateFeature(second);
            AssertEqual(firstFeature.Time, secondFeature.Time, "Candidate time");
            AssertEqual(firstFeature.Delta, secondFeature.Delta, "Candidate delta");
            AssertEqual(firstFeature.PriceChange, secondFeature.PriceChange, "Candidate price change");
            AssertEqual(firstFeature.BookTime, secondFeature.BookTime, "Previous closed book time");
            AssertEqual(firstFeature.BookImbalance, secondFeature.BookImbalance,
                "Same-timestamp quote must not alter candidate imbalance.");
            AssertFalse(first.NormalizedEventHash == second.NormalizedEventHash,
                "Different quote payloads must change the normalized event hash.");
        }

        private static void TestFutureSuffixIsolation(string root)
        {
            SyntheticPair favorable = SyntheticQshFactory.Create(root, "future-up", 101, 1000, 10, Side.Sell);
            SyntheticPair adverse = SyntheticQshFactory.Create(root, "future-down", 99, 1000, 10, Side.Sell);
            OrderFlowResearchResult upResult = RunPair(favorable, Path.Combine(root, "output-up"));
            OrderFlowResearchResult downResult = RunPair(adverse, Path.Combine(root, "output-down"));

            OrderFlowFeatureSnapshot upFeature = GetOnlyCandidateFeature(upResult);
            OrderFlowFeatureSnapshot downFeature = GetOnlyCandidateFeature(downResult);
            AssertEqual(upFeature.Time, downFeature.Time, "Prior snapshot time");
            AssertEqual(upFeature.ReferencePrice, downFeature.ReferencePrice, "Prior reference price");
            AssertEqual(upFeature.Delta, downFeature.Delta, "Prior delta");
            AssertEqual(upFeature.PriceChange, downFeature.PriceChange, "Prior price change");
            AssertEqual(upFeature.BookImbalance, downFeature.BookImbalance, "Prior book imbalance");

            OrderFlowMarketPathLabel upLabel = upResult.Labels.Single(item => item.HorizonSeconds == 5);
            OrderFlowMarketPathLabel downLabel = downResult.Labels.Single(item => item.HorizonSeconds == 5);
            AssertEqual(OrderFlowBarrierOutcome.TargetFirst, upLabel.Outcome, "Favorable suffix label");
            AssertEqual(OrderFlowBarrierOutcome.InvalidationFirst, downLabel.Outcome, "Adverse suffix label");
        }

        private static void TestSameTimestampDealOrderIsolation(string root)
        {
            SyntheticPair ascending = SyntheticQshFactory.CreateSameTimestampPermutation(
                root, "deals-ascending", false);
            SyntheticPair descending = SyntheticQshFactory.CreateSameTimestampPermutation(
                root, "deals-descending", true);
            OrderFlowResearchResult first = RunPair(ascending, Path.Combine(root, "output-ascending"));
            OrderFlowResearchResult second = RunPair(descending, Path.Combine(root, "output-descending"));

            OrderFlowFeatureSnapshot firstFeature = GetOnlyCandidateFeature(first);
            OrderFlowFeatureSnapshot secondFeature = GetOnlyCandidateFeature(second);
            AssertEqual(firstFeature.ReferencePrice, secondFeature.ReferencePrice,
                "Bucket VWAP must not depend on same-timestamp deal order.");
            AssertEqual(firstFeature.PriceChange, secondFeature.PriceChange,
                "Price change must not depend on same-timestamp deal order.");
            AssertEqual(first.FeatureHash, second.FeatureHash,
                "Causal feature hash must be invariant to same-timestamp deal permutation.");
            AssertFalse(first.NormalizedEventHash == second.NormalizedEventHash,
                "Normalized event hash must preserve source file order.");
            AssertEqual(99m, first.Bars[OrderFlowDisplayTimeFrame.Sec15][0].Open,
                "Display OHLC must preserve the first source deal.");
            AssertEqual(101m, second.Bars[OrderFlowDisplayTimeFrame.Sec15][0].Open,
                "Display OHLC must reflect the reversed source order.");
        }

        private static void TestSameTimestampBarrierAmbiguity(string root)
        {
            SyntheticPair targetFirst = SyntheticQshFactory.CreateBarrierTie(
                root, "barrier-target-first", false);
            SyntheticPair invalidationFirst = SyntheticQshFactory.CreateBarrierTie(
                root, "barrier-invalidation-first", true);
            OrderFlowResearchResult first = RunPair(targetFirst, Path.Combine(root, "output-target-first"));
            OrderFlowResearchResult second = RunPair(invalidationFirst, Path.Combine(root, "output-invalidation-first"));

            OrderFlowMarketPathLabel firstLabel = first.Labels.Single(item => item.HorizonSeconds == 5);
            OrderFlowMarketPathLabel secondLabel = second.Labels.Single(item => item.HorizonSeconds == 5);
            AssertEqual(OrderFlowBarrierOutcome.AmbiguousSameTimestamp, firstLabel.Outcome,
                "Target-first source order remains epistemically ambiguous at one timestamp.");
            AssertEqual(OrderFlowBarrierOutcome.AmbiguousSameTimestamp, secondLabel.Outcome,
                "Invalidation-first source order remains epistemically ambiguous at one timestamp.");
            AssertEqual(firstLabel.SignedReturn, secondLabel.SignedReturn,
                "Closing bucket VWAP must be order independent.");
        }

        private static void TestMismatchedPairRejected(string root)
        {
            SyntheticPair pair = SyntheticQshFactory.Create(root, "mismatch-source", 101, 1000, 10, Side.Sell);
            string output = Path.Combine(root, "output");
            OrderFlowResearchResult validResult = RunPair(pair, output);
            string mismatchDirectory = Path.Combine(root, "mismatch");
            Directory.CreateDirectory(mismatchDirectory);
            string mismatchedQuotes = Path.Combine(mismatchDirectory, "OTHER.2026-09-18.Quotes.qsh");
            File.Copy(pair.QuotesPath, mismatchedQuotes);

            SyntheticPair mismatchedPair = new SyntheticPair();
            mismatchedPair.DealsPath = pair.DealsPath;
            mismatchedPair.QuotesPath = mismatchedQuotes;
            mismatchedPair.StartTime = pair.StartTime;
            OrderFlowResearchResult result = RunPair(mismatchedPair, output);

            AssertFalse(result.Quality.ResearchAccepted, "Mismatched pair must be rejected.");
            AssertTrue(result.Quality.Issues.Any(issue => issue.ReasonCode == "PAIR_FILE_INSTRUMENT_MISMATCH"),
                "Mismatched filename reason code");
            AssertFalse(validResult.InputHash == result.InputHash,
                "Semantic file names and roles must participate in input identity.");
            AssertFalse(validResult.ArtifactDirectory == result.ArtifactDirectory,
                "Rejected renamed input must not collide with a valid artifact bundle.");
            AssertTrue(Directory.Exists(result.ArtifactDirectory), "Rejected pair audit bundle");
        }

        private static void TestDisjointRangesRejected(string root)
        {
            SyntheticPair pair = SyntheticQshFactory.CreateDisjointRanges(root, "disjoint");
            OrderFlowResearchResult result = RunPair(pair, Path.Combine(root, "output"));

            AssertFalse(result.Quality.ResearchAccepted, "Non-overlapping source ranges must be rejected.");
            AssertTrue(result.Quality.Issues.Any(issue => issue.ReasonCode == "PAIR_TIME_RANGES_DISJOINT"),
                "Disjoint range reason code");
        }

        private static void TestUnknownSideRejected(string root)
        {
            SyntheticPair pair = SyntheticQshFactory.Create(root, "unknown-side", 101, 1000, 10, Side.None);
            OrderFlowResearchResult result = RunPair(pair, Path.Combine(root, "output"));

            AssertFalse(result.Quality.ResearchAccepted, "Unknown side must reject research acceptance.");
            AssertTrue(result.Quality.UnknownSideCount > 0, "Unknown side counter");
            AssertTrue(result.Quality.Issues.Any(issue => issue.ReasonCode == "DEAL_SIDE_UNKNOWN"),
                "Unknown side reason code");
        }

        private static void TestGzipPair(string root)
        {
            SyntheticPair pair = SyntheticQshFactory.Create(root, "gzip-raw", 101, 1000, 10, Side.Sell);
            SyntheticPair gzipPair = SyntheticQshFactory.CompressGzip(root, pair);
            OrderFlowResearchResult result = RunPair(gzipPair, Path.Combine(root, "output"));
            AssertTrue(result.Quality.ResearchAccepted, "GZip QSH pair must be accepted.");
            AssertEqual(1, result.Candidates.Count, "GZip candidate count");
        }

        private static void TestNegativeQuoteCountRejected(string root)
        {
            SyntheticPair pair = SyntheticQshFactory.CreateNegativeQuoteCount(root, "negative-quote-count");
            OrderFlowResearchResult result = RunPair(pair, Path.Combine(root, "output"));

            AssertFalse(result.Quality.ResearchAccepted, "A negative Quotes change count must be rejected.");
            AssertTrue(result.Quality.Issues.Any(issue => issue.ReasonCode == "QSH_INVALID"),
                "Malformed Quotes reason code");
        }

        private static void TestDeflatePair(string root)
        {
            SyntheticPair pair = SyntheticQshFactory.Create(root, "deflate-raw", 101, 1000, 10, Side.Sell);
            SyntheticPair deflatePair = SyntheticQshFactory.CompressDeflate(root, pair);
            OrderFlowResearchResult result = RunPair(deflatePair, Path.Combine(root, "output"));
            AssertTrue(result.Quality.ResearchAccepted, "Deflate QSH pair must be accepted.");
            AssertEqual(1, result.Candidates.Count, "Deflate candidate count");
        }

        private static void TestTruncatedFrameRejected(string root)
        {
            SyntheticPair pair = SyntheticQshFactory.Create(root, "truncated-source", 101, 1000, 10, Side.Sell);
            string directory = Path.Combine(root, "truncated");
            Directory.CreateDirectory(directory);
            string dealsPath = Path.Combine(directory, "TEST.2026-09-18.Deals.qsh");
            string quotesPath = Path.Combine(directory, "TEST.2026-09-18.Quotes.qsh");
            File.Copy(pair.DealsPath, dealsPath);
            File.Copy(pair.QuotesPath, quotesPath);

            using (FileStream stream = new FileStream(dealsPath, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                stream.SetLength(stream.Length - 1);
            }

            SyntheticPair truncatedPair = new SyntheticPair();
            truncatedPair.DealsPath = dealsPath;
            truncatedPair.QuotesPath = quotesPath;
            truncatedPair.StartTime = pair.StartTime;
            OrderFlowResearchResult result = RunPair(truncatedPair, Path.Combine(root, "output"));

            AssertFalse(result.Quality.ResearchAccepted, "A QSH frame truncated after its header must be rejected.");
            AssertTrue(result.Quality.Issues.Any(issue => issue.ReasonCode == "QSH_INVALID"),
                "Truncated frame reason code");
        }

        private static OrderFlowResearchResult RunPair(SyntheticPair pair, string output)
        {
            OrderFlowResearchRequest request = new OrderFlowResearchRequest();
            request.DealsFilePath = pair.DealsPath;
            request.QuotesFilePath = pair.QuotesPath;
            request.OutputRootPath = output;
            request.FeatureWindowSeconds = 10;
            request.MinimumAbsoluteDelta = 100;
            request.MinimumPriceChangeTicks = 0;
            request.TopBookLevels = 5;
            request.MaximumBookAgeMilliseconds = 5000;
            request.CandidateCooldownMilliseconds = 10000;
            request.BackgroundSampleSeconds = 30;
            request.LabelHorizonsSeconds = new List<int>() { 5 };
            request.TargetTicks = 1;
            request.InvalidationTicks = 1;

            OrderFlowResearchRunner runner = new OrderFlowResearchRunner();
            return runner.RunAndExport(request, CancellationToken.None);
        }

        private static OrderFlowFeatureSnapshot GetOnlyCandidateFeature(OrderFlowResearchResult result)
        {
            AssertEqual(1, result.Candidates.Count, "Expected one broad candidate");
            string candidateId = result.Candidates[0].CandidateId;
            OrderFlowObservation observation = result.Observations.Single(item => item.CandidateId == candidateId);
            return observation.Features;
        }

        private static string Sanitize(string name)
        {
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                char value = name[i];
                builder.Append(char.IsLetterOrDigit(value) ? value : '-');
            }

            return builder.ToString();
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (condition == false)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static void AssertFalse(bool condition, string message)
        {
            AssertTrue(condition == false, message);
        }

        private static void AssertNotNull(object value, string message)
        {
            AssertTrue(value != null, message);
        }

        private static void AssertEqual<T>(T expected, T actual, string message)
        {
            if (EqualityComparer<T>.Default.Equals(expected, actual) == false)
            {
                throw new InvalidOperationException(message + ". Expected " + expected + ", actual " + actual + ".");
            }
        }
    }

    internal sealed class SyntheticPair
    {
        public string DealsPath { get; set; }

        public string QuotesPath { get; set; }

        public DateTime StartTime { get; set; }
    }

    internal static class SyntheticQshFactory
    {
        private static readonly byte[] _prefix = Encoding.UTF8.GetBytes("QScalp History Data");

        public static SyntheticPair Create(string root, string name, decimal futurePrice,
            long sameTimestampBidVolume, long sameTimestampAskVolume, Side candidateDealSide)
        {
            string directory = Path.Combine(root, name);
            Directory.CreateDirectory(directory);
            string dealsPath = Path.Combine(directory, "TEST.2026-09-18.Deals.qsh");
            string quotesPath = Path.Combine(directory, "TEST.2026-09-18.Quotes.qsh");
            DateTime start = new DateTime(2026, 9, 18, 10, 0, 0, DateTimeKind.Utc);

            WriteDeals(dealsPath, start, futurePrice, candidateDealSide);
            WriteQuotes(quotesPath, start, sameTimestampBidVolume, sameTimestampAskVolume);

            SyntheticPair pair = new SyntheticPair();
            pair.DealsPath = dealsPath;
            pair.QuotesPath = quotesPath;
            pair.StartTime = start;
            return pair;
        }

        public static SyntheticPair CreateSameTimestampPermutation(string root, string name, bool reverse)
        {
            string directory = Path.Combine(root, name);
            Directory.CreateDirectory(directory);
            string dealsPath = Path.Combine(directory, "TEST.2026-09-18.Deals.qsh");
            string quotesPath = Path.Combine(directory, "TEST.2026-09-18.Quotes.qsh");
            DateTime start = new DateTime(2026, 9, 18, 10, 0, 0, DateTimeKind.Utc);

            WriteSameTimestampDeals(dealsPath, start, reverse);
            WriteQuotes(quotesPath, start, 100, 100);

            SyntheticPair pair = new SyntheticPair();
            pair.DealsPath = dealsPath;
            pair.QuotesPath = quotesPath;
            pair.StartTime = start;
            return pair;
        }

        public static SyntheticPair CreateBarrierTie(string root, string name, bool reverse)
        {
            string directory = Path.Combine(root, name);
            Directory.CreateDirectory(directory);
            string dealsPath = Path.Combine(directory, "TEST.2026-09-18.Deals.qsh");
            string quotesPath = Path.Combine(directory, "TEST.2026-09-18.Quotes.qsh");
            DateTime start = new DateTime(2026, 9, 18, 10, 0, 0, DateTimeKind.Utc);

            WriteBarrierTieDeals(dealsPath, start, reverse);
            WriteQuotes(quotesPath, start, 100, 100);

            SyntheticPair pair = new SyntheticPair();
            pair.DealsPath = dealsPath;
            pair.QuotesPath = quotesPath;
            pair.StartTime = start;
            return pair;
        }

        public static SyntheticPair CreateDisjointRanges(string root, string name)
        {
            string directory = Path.Combine(root, name);
            Directory.CreateDirectory(directory);
            string dealsPath = Path.Combine(directory, "TEST.2026-09-18.Deals.qsh");
            string quotesPath = Path.Combine(directory, "TEST.2026-09-18.Quotes.qsh");
            DateTime start = new DateTime(2026, 9, 18, 10, 0, 0, DateTimeKind.Utc);

            WriteDeals(dealsPath, start, 101, Side.Sell);
            WriteQuotes(quotesPath, start.AddDays(1), 100, 100);

            SyntheticPair pair = new SyntheticPair();
            pair.DealsPath = dealsPath;
            pair.QuotesPath = quotesPath;
            pair.StartTime = start;
            return pair;
        }

        public static SyntheticPair CreateNegativeQuoteCount(string root, string name)
        {
            string directory = Path.Combine(root, name);
            Directory.CreateDirectory(directory);
            string dealsPath = Path.Combine(directory, "TEST.2026-09-18.Deals.qsh");
            string quotesPath = Path.Combine(directory, "TEST.2026-09-18.Quotes.qsh");
            DateTime start = new DateTime(2026, 9, 18, 10, 0, 0, DateTimeKind.Utc);

            WriteDeals(dealsPath, start, 101, Side.Sell);
            WriteQuotesWithNegativeCount(quotesPath, start);

            SyntheticPair pair = new SyntheticPair();
            pair.DealsPath = dealsPath;
            pair.QuotesPath = quotesPath;
            pair.StartTime = start;
            return pair;
        }

        public static SyntheticPair CompressGzip(string root, SyntheticPair source)
        {
            string directory = Path.Combine(root, "gzip");
            Directory.CreateDirectory(directory);
            string dealsPath = Path.Combine(directory, "TEST.2026-09-18.Deals.qsh");
            string quotesPath = Path.Combine(directory, "TEST.2026-09-18.Quotes.qsh");
            CompressFileGzip(source.DealsPath, dealsPath);
            CompressFileGzip(source.QuotesPath, quotesPath);

            SyntheticPair pair = new SyntheticPair();
            pair.DealsPath = dealsPath;
            pair.QuotesPath = quotesPath;
            pair.StartTime = source.StartTime;
            return pair;
        }

        public static SyntheticPair CompressDeflate(string root, SyntheticPair source)
        {
            string directory = Path.Combine(root, "deflate");
            Directory.CreateDirectory(directory);
            string dealsPath = Path.Combine(directory, "TEST.2026-09-18.Deals.qsh");
            string quotesPath = Path.Combine(directory, "TEST.2026-09-18.Quotes.qsh");
            CompressFileDeflate(source.DealsPath, dealsPath);
            CompressFileDeflate(source.QuotesPath, quotesPath);

            SyntheticPair pair = new SyntheticPair();
            pair.DealsPath = dealsPath;
            pair.QuotesPath = quotesPath;
            pair.StartTime = source.StartTime;
            return pair;
        }

        private static void WriteDeals(string path, DateTime start, decimal futurePrice, Side candidateDealSide)
        {
            using (FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (DataBinaryWriter writer = new DataBinaryWriter(stream))
            {
                WriteHeader(writer, start, 0x20);
                long lastFrame = TimeManager.GetTimeStampMillisecondsFromStartTime(start);
                DealsStream dealsStream = new DealsStream();

                WriteDealFrame(writer, dealsStream, start.AddSeconds(1), ref lastFrame,
                    100, 60, candidateDealSide, "1");
                WriteDealFrame(writer, dealsStream, start.AddSeconds(2), ref lastFrame,
                    100, 60, candidateDealSide, "2");
                WriteDealFrame(writer, dealsStream, start.AddSeconds(3), ref lastFrame,
                    futurePrice, 1, Side.Buy, "3");
                WriteDealFrame(writer, dealsStream, start.AddSeconds(4), ref lastFrame,
                    futurePrice, 1, Side.Buy, "4");
            }
        }

        private static void WriteDealFrame(DataBinaryWriter writer, DealsStream dealsStream, DateTime time,
            ref long lastFrame, decimal price, decimal volume, Side side, string id)
        {
            long timestamp = TimeManager.GetTimeStampMillisecondsFromStartTime(time);
            writer.WriteGrowing(timestamp - lastFrame);
            lastFrame = timestamp;

            Trade trade = new Trade();
            trade.Time = time;
            trade.Price = price;
            trade.Volume = volume;
            trade.Side = side;
            trade.Id = id;
            dealsStream.Write(writer, trade, 1, 1);
        }

        private static void WriteSameTimestampDeals(string path, DateTime start, bool reverse)
        {
            using (FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (DataBinaryWriter writer = new DataBinaryWriter(stream))
            {
                WriteHeader(writer, start, 0x20);
                long lastFrame = TimeManager.GetTimeStampMillisecondsFromStartTime(start);
                DealsStream dealsStream = new DealsStream();
                decimal firstPrice = reverse ? 101 : 99;
                decimal secondPrice = reverse ? 99 : 101;

                WriteDealFrame(writer, dealsStream, start.AddSeconds(1), ref lastFrame,
                    firstPrice, 60, Side.Sell, "1");
                WriteDealFrame(writer, dealsStream, start.AddSeconds(1), ref lastFrame,
                    secondPrice, 60, Side.Sell, "2");
                WriteDealFrame(writer, dealsStream, start.AddSeconds(2), ref lastFrame,
                    101, 1, Side.Buy, "3");
                WriteDealFrame(writer, dealsStream, start.AddSeconds(3), ref lastFrame,
                    101, 1, Side.Buy, "4");
            }
        }

        private static void WriteBarrierTieDeals(string path, DateTime start, bool reverse)
        {
            using (FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (DataBinaryWriter writer = new DataBinaryWriter(stream))
            {
                WriteHeader(writer, start, 0x20);
                long lastFrame = TimeManager.GetTimeStampMillisecondsFromStartTime(start);
                DealsStream dealsStream = new DealsStream();
                decimal firstBarrierPrice = reverse ? 99 : 101;
                decimal secondBarrierPrice = reverse ? 101 : 99;

                WriteDealFrame(writer, dealsStream, start.AddSeconds(1), ref lastFrame,
                    100, 60, Side.Sell, "1");
                WriteDealFrame(writer, dealsStream, start.AddSeconds(1), ref lastFrame,
                    100, 60, Side.Sell, "2");
                WriteDealFrame(writer, dealsStream, start.AddSeconds(2), ref lastFrame,
                    firstBarrierPrice, 1, Side.Buy, "3");
                WriteDealFrame(writer, dealsStream, start.AddSeconds(2), ref lastFrame,
                    secondBarrierPrice, 1, Side.Buy, "4");
            }
        }

        private static void WriteQuotes(string path, DateTime start, long bidVolume, long askVolume)
        {
            using (FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (DataBinaryWriter writer = new DataBinaryWriter(stream))
            {
                WriteHeader(writer, start, 0x10);
                long lastFrame = TimeManager.GetTimeStampMillisecondsFromStartTime(start);
                long lastPrice = 0;

                WriteQuoteFrame(writer, start, ref lastFrame, ref lastPrice,
                    new QuoteChange[]
                    {
                        new QuoteChange(99, -100),
                        new QuoteChange(101, 100)
                    });
                WriteQuoteFrame(writer, start.AddSeconds(2), ref lastFrame, ref lastPrice,
                    new QuoteChange[]
                    {
                        new QuoteChange(99, -bidVolume),
                        new QuoteChange(101, askVolume)
                    });
                WriteQuoteFrame(writer, start.AddSeconds(8), ref lastFrame, ref lastPrice,
                    new QuoteChange[0]);
            }
        }

        private static void WriteQuotesWithNegativeCount(string path, DateTime start)
        {
            using (FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (DataBinaryWriter writer = new DataBinaryWriter(stream))
            {
                WriteHeader(writer, start, 0x10);
                long lastFrame = TimeManager.GetTimeStampMillisecondsFromStartTime(start);
                long lastPrice = 0;
                WriteQuoteFrame(writer, start, ref lastFrame, ref lastPrice,
                    new QuoteChange[]
                    {
                        new QuoteChange(99, -100),
                        new QuoteChange(101, 100)
                    });

                long timestamp = TimeManager.GetTimeStampMillisecondsFromStartTime(start.AddSeconds(2));
                writer.WriteGrowing(timestamp - lastFrame);
                writer.WriteLeb128(-1);
            }
        }

        private static void WriteQuoteFrame(DataBinaryWriter writer, DateTime time, ref long lastFrame,
            ref long lastPrice, QuoteChange[] changes)
        {
            long timestamp = TimeManager.GetTimeStampMillisecondsFromStartTime(time);
            writer.WriteGrowing(timestamp - lastFrame);
            lastFrame = timestamp;
            writer.WriteLeb128(changes.Length);

            for (int i = 0; i < changes.Length; i++)
            {
                writer.WriteLeb128(changes[i].PriceTicks - lastPrice);
                lastPrice = changes[i].PriceTicks;
                writer.WriteLeb128(changes[i].SignedVolumeSteps);
            }
        }

        private static void WriteHeader(DataBinaryWriter writer, DateTime start, byte streamType)
        {
            writer.Write(_prefix);
            writer.Write((byte)4);
            writer.Write("OrderFlowSyntheticFixture");
            writer.Write("VolumeStep:1");
            writer.Write(start.Ticks);
            writer.Write((byte)1);
            writer.Write(streamType);
            writer.Write("Synthetic:TEST:Futures:1:1");
        }

        private static void CompressFileGzip(string sourcePath, string targetPath)
        {
            using (FileStream source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (FileStream target = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (GZipStream gzip = new GZipStream(target, CompressionLevel.Optimal))
            {
                source.CopyTo(gzip);
            }
        }

        private static void CompressFileDeflate(string sourcePath, string targetPath)
        {
            using (FileStream source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (FileStream target = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (DeflateStream deflate = new DeflateStream(target, CompressionLevel.Optimal))
            {
                source.CopyTo(deflate);
            }
        }
    }

    internal readonly struct QuoteChange
    {
        public QuoteChange(long priceTicks, long signedVolumeSteps)
        {
            PriceTicks = priceTicks;
            SignedVolumeSteps = signedVolumeSteps;
        }

        public long PriceTicks { get; }

        public long SignedVolumeSteps { get; }
    }
}
