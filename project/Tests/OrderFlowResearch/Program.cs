/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.Entity;
using OsEngine.OsData.OrderFlow;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace OsEngine.OrderFlowResearch.Tests
{
    /// <summary>Offline synthetic tick research tests and WPF component rendering without starting the application.</summary>
    /// <remarks>
    /// Command from project/: dotnet run --project Tests/OrderFlowResearch/OsEngine.OrderFlowResearch.Tests.csproj.
    /// Uses temporary local fixtures removed in finally; no credentials, network or owner data required.
    /// Caller owns timeout policy. ORDER-FLOW-QUALIFICATION-001 defines scope: this does not prove
    /// execution, profitability, owner-file performance or Tester/live parity.
    /// </remarks>
    internal static partial class Program
    {
        private static int _passed;
        private static int _failed;
        private static readonly DateTime Start = new DateTime(2026, 9, 18, 10, 0, 0);

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--explorer-input") { return RunExplorerOwner(args); }
            if (args.Length > 0 && args[0] == "--pattern-input") { return RunPatternOwner(args); }
            string root = Path.Combine(Path.GetTempPath(), "OsEngine-TickResearch-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                Run("strict schema and fractional microseconds", root, TestReader);
                Run("malformed fields and line evidence", root, TestInvalidRows);
                Run("regressive microseconds rejected", root, TestRegressiveTime);
                Run("repeated IDs and identical rows retained", root, TestRepeatedIds);
                Run("hash and replay on pinned handle", root, TestPinnedInput);
                Run("missing and locked input audit", root, TestUnavailable);
                Run("missing parent and missing file reuse identical rejection", root, TestMissingIdentity);
                Run("cancellation releases input", root, TestCancellation);
                Run("manual decimal price step", root, TestPriceStep);
                Run("long and mirrored short", root, TestDirections);
                Run("same-time permutation causal features", root, TestPermutation);
                Run("future suffix isolation", root, TestFutureSuffix);
                Run("same-time barrier ambiguity", root, TestBarrierTie);
                Run("microsecond barrier order", root, TestMicrosecondBarriers);
                Run("incomplete horizons and gaps", root, TestIncompleteAndGap);
                Run("exact window formulas and boundary", root, TestFeatureFormulas);
                Run("inclusive dates without outside evidence", root, TestDateRange);
                Run("excluded invalid suffix rejects file", root, TestExcludedInvalid);
                Run("empty selected range", root, TestEmptyRange);
                Run("date validation and normalization", root, TestDateValidation);
                Run("invalid typed date never means all dates or old selection", root, TestDateEntry);
                Run("decimal presentation retains small price differences", root, TestDecimalPresentation);
                Run("continuous window across midnight", root, TestMidnight);
                Run("deterministic immutable artifacts", root, TestArtifacts);
                Run("step and dates enter spec identity", root, TestSpecIdentity);
                Run("single-input microsecond artifact schema", root, TestSchema);
                Run("summary layout and bilingual values", root, TestSummaryLayout);
                Run("whole chart navigation", root, TestChartNavigation);
                Run("larger OHLC bars and response", root, TestLargerChartBars);
                Run("twenty-one timeframes render immutably", root, TestChartTimeFrames);
                Run("daily and weekly calendar boundaries", root, TestCalendarChartBars);
                Run("pointer zoom and source time axis", root, TestTimeAxis);
                Run("Cloud gap range and completion boundaries", root, TestCloudBoundaries);
                Run("Cloud filtered ticks and thresholds", root, TestCloudFilteredTicks);
                Run("Cloud mixed sides and single-trade classification", root, TestCloudModes);
                Run("owner Cloud example and causal qualification snapshot", root, TestCloudQualification);
                Run("Cloud selected period and decimal ranges", root, TestCloudPeriodAndDecimals);
                Run("independent Delta Cloud and combined replay", root, TestIndependentCalculations);
                Run("technical IDs never enter event or Cloud calculation", root, TestTechnicalIdsIgnored);
                Run("Cloud immutable artifacts and spec", root, TestCloudArtifacts);
                Run("shared Cloud chart and settings tabs", root, TestCloudChart);
                Run("calendar month aggregation", root, TestMonthlyChartBars);
                Run("visible timeframe and circle controls stay synchronized", root, TestChartControlBindings);
                Run("Cloud visual coefficient and hit geometry", root, TestCloudVisualScale);
                Run("drawing rays clip in both time directions", root, TestDrawingClipping);
                Run("drawing source anchors and compressed time", root, TestDrawingCoordinates);
                Run("drawing interaction and immutable results", root, TestDrawingInteraction);
                Run("pending drawing does not block axis drag", root, TestDrawingDuringAxisDrag);
                Run("chart surface transfer and stable bindings", root, TestChartSurfaceTransfer);
                Run("drawing toolbar moves with chart", root, TestDrawingToolbarMarkup);
                Run("candles bars and high-low price views", root, TestPriceDisplayModes);
                Run("Cloud path visible neighbors and equal times", root, TestCloudPricePathRange);
                Run("Cloud price path across all timeframes", root, TestCloudPriceRendering);
                Run("Cloud median contrast and decimal extremes", root, TestCloudVolumeContrast);
                Run("Cloud exact volume labels and fit", root, TestCloudVolumeLabels);
                Run("Cloud contrast controls survive chart transfer", root, TestCloudContrastBindings);
                Run("replay clock speed pause step and gap policy", root, TestReplayClock);
                Run("replay detached tick prefixes and calculation parity", root, TestReplayPrefixes);
                Run("replay forming candles on all selected timeframes", root, TestReplayChartTimeFrames);
                Run("replay worker step cancellation identity and EOF", root, TestReplayWorker);
                Run("replay toolbar travels with chart", root, TestReplayToolbar);
                Run("Delta-only replay uses fixed safe reference", root, TestReplayDeltaOnly);
                Run("replay return preserves source range after TF change", root, TestReplayReturnRange);
                Run("single tick waits for acknowledged worker pause", root, TestReplayStepBoundary);
                Run("SecondCloudTicks", root, TestSecondCloudTicks);
                Run("SecondCloudCombinations", root, TestSecondCloudCombinations);
                Run("SecondCloudArtifacts", root, TestSecondCloudArtifacts);
                Run("SecondCloudReplay", root, TestSecondCloudReplay);
                Run("SecondCloudDisplay", root, TestSecondCloudDisplay);
                Run("WorkspacePadding", root, TestWorkspacePadding);
                Run("WorkspaceGrid", root, TestWorkspaceGrid);
                Run("DrawingBodyTranslation", root, TestDrawingBodyTranslation);
                Run("DrawingGapTranslation", root, TestDrawingGapTranslation);
                Run("WorkspaceMarkup", root, TestWorkspaceMarkup);
                Run("ImbalanceFormulas", root, TestImbalanceFormulas);
                Run("ImbalanceExactComparison", root, TestImbalanceExactComparison);
                Run("ImbalancePrecisionThresholds", root, TestImbalancePrecisionThresholds);
                Run("ImbalancePrecisionWindow", root, TestImbalancePrecisionWindow);
                Run("ImbalancePrecisionRejectedResult", root, TestImbalancePrecisionRejectedResult);
                Run("ImbalanceMissingNeighbors", root, TestImbalanceMissingNeighbors);
                Run("ImbalanceVolumeFloors", root, TestImbalanceVolumeFloors);
                Run("ImbalanceBothDirections", root, TestImbalanceBothDirections);
                Run("ImbalanceIndexUpdates", root, TestImbalanceIndexUpdates);
                Run("ImbalanceContextCausality", root, TestImbalanceContextCausality);
                Run("ImbalanceWindowBoundary", root, TestImbalanceWindowBoundary);
                Run("ImbalanceSingleTickReplay", root, TestImbalanceSingleTickReplay);
                Run("ImbalanceFilterIndependence", root, TestImbalanceFilterIndependence);
                Run("ImbalanceArtifactSchema", root, TestImbalanceArtifactSchema);
                Run("ImbalanceDisplayAndMarkup", root, TestImbalanceDisplayAndMarkup);
                Run("CloudSavedPairFloors", root, TestCloudSavedPairFloors);
                Run("CloudFinalCountFilter", root, TestCloudFinalCountFilter);
                Run("CloudPostfilterNoIo", root, TestCloudPostfilterNoIo);
                Run("CloudPairArtifacts", root, TestCloudPairArtifacts);
                Run("CloudSquareHitsAndFocus", root, TestCloudSquareHitsAndFocus);
                Run("CloudExplorerMarkupAndHelp", root, TestCloudExplorerMarkupAndHelp);
                Run("StudyAtrCausality", root, TestStudyAtrCausality);
                Run("StudyCompletionReference", root, TestStudyCompletionReference);
                Run("StudySameTimeOrder", root, TestStudySameTimeOrder);
                Run("StudyHorizonCoverage", root, TestStudyHorizonCoverage);
                Run("StudyNeutralAndUnfinished", root, TestStudyNeutralAndUnfinished);
                Run("StudyFixedSampling", root, TestStudyFixedSampling);
                Run("StudySplitPurging", root, TestStudySplitPurging);
                Run("StudyHeldOutIsolation", root, TestStudyHeldOutIsolation);
                Run("StudyDateAndSettings", root, TestStudyDateAndSettings);
                Run("StudyIdentityAndCancellation", root, TestStudyIdentityAndCancellation);
                Run("StudyArtifactPublication", root, TestStudyArtifactPublication);
                Run("StudyWorkerLifetime", root, TestStudyWorkerLifetime);
                Run("StudyExactThirds", root, TestStudyExactThirds);
                Run("StudyBarrierUnderflow", root, TestStudyBarrierUnderflow);
                RegisterExplorerV2(root);
                RegisterPatternSearch(root);
            }
            finally { Directory.Delete(root, true); }
            Console.WriteLine("Order Flow Research tests: " + _passed + " passed, " + _failed + " failed.");
            return _failed == 0 ? 0 : 1;
        }

        private static void Run(string name, string root, Action<string> test)
        {
            string directory = Path.Combine(root, _passed + "-" + _failed);
            Directory.CreateDirectory(directory);
            try { test(directory); _passed++; Console.WriteLine("PASS " + name); }
            catch (Exception error) { _failed++; Console.WriteLine("FAIL " + name + Environment.NewLine + error); }
        }

        private static string Row(DateTime time, decimal price, decimal volume, Side side, string id = "opaque")
        {
            return string.Join(",", time.ToString("yyyyMMdd,HHmmss", CultureInfo.InvariantCulture),
                price.ToString(CultureInfo.InvariantCulture), volume.ToString(CultureInfo.InvariantCulture), side,
                ((time.Ticks % TimeSpan.TicksPerSecond) / 10).ToString(CultureInfo.InvariantCulture), id);
        }

        private static string[] BasicTicks(Side side = Side.Sell, decimal future = 101)
        {
            return new[] { Row(Start, 100, 10, Side.Buy), Row(Start.AddSeconds(1), 100, 200, side),
                Row(Start.AddSeconds(2), future, 1, Side.Buy), Row(Start.AddSeconds(7), future, 1, Side.Buy) };
        }

        private static OrderFlowResearchRequest Request(string root, params string[] rows)
        {
            string path = Path.Combine(root, "TEST.txt");
            File.WriteAllText(path, string.Join("\r\n", rows) + "\r\n", new UTF8Encoding(false));
            return new OrderFlowResearchRequest { TicksFilePath = path, OutputRootPath = Path.Combine(root, "output"),
                FeatureWindowSeconds = 10, MinimumAbsoluteDelta = 100, MinimumPriceChangeTicks = 0,
                CandidateCooldownMilliseconds = 10000, BackgroundSampleSeconds = 30,
                LabelHorizonsSeconds = new List<int> { 5 }, TargetTicks = 1, InvalidationTicks = 1, PriceStep = 1 };
        }

        private static OrderFlowResearchResult Replay(OrderFlowResearchRequest request)
        { return new OrderFlowResearchEngine().Run(request, CancellationToken.None); }

        private static OrderFlowResearchResult Export(OrderFlowResearchRequest request)
        { return new OrderFlowResearchRunner().RunAndExport(request, CancellationToken.None); }

        private static void TestReader(string root)
        {
            OrderFlowResearchRequest request = Request(root, "\uFEFFDate,Time,Price,Volume,Side,MicroSeconds,Id", "",
                "20260918,100000,0.00001,0.25,Buy,1,", "20260918,100000,2.5,1.5,sell,999999,id");
            OrderFlowTickInput metadata = new OrderFlowTickInput();
            using (OrderFlowTickReader reader = new OrderFlowTickReader(request.TicksFilePath, metadata, CancellationToken.None))
            {
                AssertTrue(reader.TryRead(out OrderFlowDeal first), "First tick");
                AssertEqual(Start.AddTicks(10), first.Time, "One microsecond");
                AssertEqual(DateTimeKind.Unspecified, first.Time.Kind, "No invented timezone");
                AssertEqual(0.00001m, first.Price, "Decimal price");
                AssertEqual(0.25m, first.Volume, "Unscaled volume");
                AssertTrue(typeof(OrderFlowDeal).GetProperty("SourceId") == null, "Technical ID is not stored in decoded events");
                AssertTrue(reader.TryRead(out OrderFlowDeal second), "Second tick");
                AssertEqual(Start.AddTicks(9999990), second.Time, "Fractional second");
                AssertEqual(Side.Sell, second.Side, "Case insensitive side");
                AssertFalse(reader.TryRead(out OrderFlowDeal end), "EOF");
            }
            AssertTrue(metadata.ReadComplete, "Full validation");
            AssertEqual(2L, metadata.RecordCount, "No header or blanks in count");
            AssertEqual(StoredHash(request.TicksFilePath), metadata.Sha256, "All raw bytes hashed");
        }

        private static void TestInvalidRows(string root)
        {
            string valid = "20260918,100000,100,1,Buy,0,id";
            string[] bad = { "202609180,90000,100,1,Buy,0,id", "2026091,8100000,100,1,Buy,0,id",
                valid.Replace("20260918", "20260230"), valid.Replace("100000", "240000"),
                valid.Replace(",100,", ",0,"), valid.Replace(",100,", ",-1,"), valid.Replace(",1,", ",0,"),
                valid.Replace(",1,", ",-1,"), valid.Replace("Buy", "None"), valid.Replace("Buy", "1"),
                valid.Replace(",0,id", ",1000000,id"), valid.Replace(",0,id", ",-1,id"), valid.Replace(",0,id", ",1.5,id"),
                valid + ",extra", "20260918,100000,1,1,Buy,0", valid.Replace(",100,", ",1e3,"),
                valid.Replace(",100,", ",79228162514264337593543950336,"), valid.Replace(",id", "," + new string('x', 4100)) };
            foreach (string row in bad)
            {
                OrderFlowResearchResult result = Replay(Request(root, row));
                AssertRejected(result, "TICK_INVALID");
                AssertTrue(result.Quality.Issues.Any(issue => issue.Message.StartsWith("Line 1:")), "Line evidence");
                AssertEqual(0, result.Candidates.Count, "No candidate from invalid first row");
            }
            OrderFlowResearchRequest binary = Request(root, valid);
            File.WriteAllBytes(binary.TicksFilePath, new byte[] { 0xff, 0xff });
            AssertRejected(Replay(binary), "TICK_INVALID");
        }

        private static void TestRegressiveTime(string root)
        {
            AssertRejected(Replay(Request(root, Row(Start.AddTicks(20), 100, 1, Side.Buy),
                Row(Start.AddTicks(10), 100, 1, Side.Buy))), "TICK_INVALID");
        }

        private static void TestRepeatedIds(string root)
        {
            string same = Row(Start, 100, 10, Side.Buy, "9");
            OrderFlowResearchResult result = Replay(Request(root, same, same, Row(Start, 101, 20, Side.Sell, "9"),
                Row(Start.AddSeconds(1), 102, 1, Side.Buy, "1")));
            AssertTrue(result.Quality.ResearchAccepted, "Repeated opaque IDs accepted");
            AssertEqual(4L, result.Quality.DealCount, "No deduplication");
            AssertEqual(2L, result.Quality.DuplicateDealTimestampCount, "Equal time rows retained");
            AssertEqual(20m, result.Observations[0].Features.BuyVolume, "Identical rows counted twice");
            AssertEqual(20m, result.Observations[0].Features.SellVolume, "Different row sharing ID retained");
        }

        private static void TestPinnedInput(string root)
        {
            OrderFlowResearchRequest request = Request(root, BasicTicks());
            OrderFlowTickInput metadata = new OrderFlowTickInput();
            using (OrderFlowTickReader reader = new OrderFlowTickReader(request.TicksFilePath, metadata, CancellationToken.None))
            {
                Expect<IOException>(() => { using FileStream writer = new FileStream(request.TicksFilePath, FileMode.Open, FileAccess.Write); });
                Expect<IOException>(() => File.Move(request.TicksFilePath, request.TicksFilePath + ".moved"));
                while (reader.TryRead(out OrderFlowDeal tick)) { }
                AssertEqual(StoredHash(request.TicksFilePath), metadata.Sha256, "Hash matches replay handle");
            }
            using FileStream released = new FileStream(request.TicksFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }

        private static void TestUnavailable(string root)
        {
            OrderFlowResearchRequest request = Request(root, BasicTicks());
            using (FileStream locked = new FileStream(request.TicksFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                OrderFlowResearchResult result = Export(request);
                AssertRejected(result, "TICK_IO_ERROR");
                AssertTrue(File.Exists(Path.Combine(result.ArtifactDirectory, "quality.json")), "Locked input bundle");
            }
            File.Delete(request.TicksFilePath);
            OrderFlowResearchResult missing = Export(request);
            AssertRejected(missing, "TICK_FILE_MISSING");
            AssertTrue(File.Exists(Path.Combine(missing.ArtifactDirectory, "manifest.json")), "Missing input bundle");
        }

        private static void TestCancellation(string root)
        {
            OrderFlowResearchRequest request = Request(root, BasicTicks());
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            using (OrderFlowTickReader reader = new OrderFlowTickReader(request.TicksFilePath, new OrderFlowTickInput(), cancellation.Token))
            {
                cancellation.Cancel();
                Expect<OperationCanceledException>(() => reader.TryRead(out OrderFlowDeal tick));
            }
            Expect<OperationCanceledException>(() => new OrderFlowResearchRunner().RunAndExport(request, cancellation.Token));
            AssertFalse(Directory.Exists(request.OutputRootPath), "No publication on cancellation");
            using FileStream released = new FileStream(request.TicksFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }

        private static void TestMissingIdentity(string root)
        {
            OrderFlowResearchRequest request = Request(root, BasicTicks());
            string parent = Path.Combine(root, "absent");
            request.TicksFilePath = Path.Combine(parent, "missing.txt");
            OrderFlowResearchResult missingParent = Export(request);
            AssertRejected(missingParent, "TICK_FILE_MISSING");
            Dictionary<string, string> hashes = Directory.GetFiles(missingParent.ArtifactDirectory).ToDictionary(Path.GetFileName, StoredHash);
            Directory.CreateDirectory(parent);
            OrderFlowResearchResult missingFile = Export(request);
            AssertRejected(missingFile, "TICK_FILE_MISSING");
            AssertEqual(missingParent.ArtifactDirectory, missingFile.ArtifactDirectory, "One deterministic missing-input identity");
            AssertEqual(missingFile.ArtifactDirectory, Export(request).ArtifactDirectory, "Further retry reuses bundle");
            foreach (string path in Directory.GetFiles(missingFile.ArtifactDirectory))
            { AssertEqual(hashes[Path.GetFileName(path)], StoredHash(path), "Missing-parent and missing-file audit bytes identical"); }
        }

        private static void TestPriceStep(string root)
        {
            AssertEqual(0.00001m, OrderFlowResearchUi.ParsePriceStep("0,00001"), "Comma decimal");
            AssertEqual(0.00001m, OrderFlowResearchUi.ParsePriceStep("0.00001"), "Dot decimal");
            AssertEqual(5.25m, OrderFlowResearchUi.ParsePriceStep("5,25"), "Step above one");
            foreach (string text in new[] { "", "0", "-1", "1,2.3", "1e-5", "x" })
            { Expect<ArgumentException>(() => OrderFlowResearchUi.ParsePriceStep(text)); }
            foreach (decimal step in new[] { 0.00001m, 0.00000001m, 5m })
            {
                OrderFlowResearchRequest request = Request(root, Row(Start, 10, 10, Side.Buy),
                    Row(Start.AddSeconds(1), 10 + step, 200, Side.Sell), Row(Start.AddSeconds(2), 10 + step * 2, 1, Side.Buy),
                    Row(Start.AddSeconds(7), 10 + step * 2, 1, Side.Buy));
                request.PriceStep = step; request.MinimumPriceChangeTicks = 1;
                OrderFlowResearchResult result = Replay(request);
                AssertEqual(1, result.Candidates.Count, "Exact one-tick threshold");
                AssertEqual(step, result.Labels[0].MaximumFavorableExcursion, "Exact decimal barrier");
                AssertEqual(OrderFlowBarrierOutcome.TargetFirst, result.Labels[0].Outcome, "Target hit");
                request.PriceStep = 0; Expect<ArgumentException>(() => Replay(request));
            }
        }

        private static void TestDirections(string root)
        {
            foreach (Side side in new[] { Side.Sell, Side.Buy })
            {
                OrderFlowResearchResult result = Replay(Request(root, BasicTicks(side, side == Side.Sell ? 101 : 99)));
                AssertTrue(result.Quality.ResearchAccepted, "Valid input");
                AssertEqual(1, result.Candidates.Count, "Cooldown retains one candidate");
                AssertEqual(side == Side.Sell ? OrderFlowDirection.Long : OrderFlowDirection.Short, result.Candidates[0].Direction, "Mirror");
                AssertEqual(1, result.Labels[0].FutureTradeCount, "Only later tick within horizon");
                AssertEqual(OrderFlowBarrierOutcome.TargetFirst, result.Labels[0].Outcome, "Favorable barrier");
                AssertEqual(1000000L, result.Labels[0].TimeToTargetMicroseconds, "Microsecond units");
            }
        }

        private static void TestPermutation(string root)
        {
            string[] rows = { Row(Start, 99, 10, Side.Buy), Row(Start, 101, 10, Side.Buy), Row(Start.AddSeconds(1), 100, 200, Side.Sell) };
            OrderFlowResearchResult a = Replay(Request(root, rows));
            OrderFlowResearchResult b = Replay(Request(root, rows[1], rows[0], rows[2]));
            AssertEqual(a.FeatureHash, b.FeatureHash, "Timestamp VWAP independent of source order");
            AssertEqual(a.Candidates.Count, b.Candidates.Count, "Detection count invariant");
            AssertEqual(a.Candidates[0].Time, b.Candidates[0].Time, "Detection time invariant");
            AssertEqual(a.Candidates[0].Direction, b.Candidates[0].Direction, "Detection direction invariant");
            AssertFalse(a.InputHash == b.InputHash, "Different raw ordering retains distinct provenance");
            AssertEqual(99m, a.Bars[OrderFlowDisplayTimeFrame.Min1][0].Open, "OHLC source order");
            AssertEqual(101m, b.Bars[OrderFlowDisplayTimeFrame.Min1][0].Open, "OHLC not artificially sorted");
        }

        private static void TestFutureSuffix(string root)
        {
            OrderFlowResearchResult up = Replay(Request(root, BasicTicks()));
            OrderFlowResearchResult down = Replay(Request(root, BasicTicks(Side.Sell, 99)));
            AssertEqual(JsonSerializer.Serialize(up.Observations.Single(o => o.CandidateId != null).Features),
                JsonSerializer.Serialize(down.Observations.Single(o => o.CandidateId != null).Features), "Prior snapshot causal");
            AssertEqual(OrderFlowBarrierOutcome.TargetFirst, up.Labels[0].Outcome, "Up suffix");
            AssertEqual(OrderFlowBarrierOutcome.InvalidationFirst, down.Labels[0].Outcome, "Down suffix");
        }

        private static void TestBarrierTie(string root) { CheckBarriers(root, false); }
        private static void TestMicrosecondBarriers(string root) { CheckBarriers(root, true); }

        private static void CheckBarriers(string root, bool distinct)
        {
            foreach (bool reverse in new[] { false, true })
            {
                OrderFlowResearchResult result = Replay(Request(root, BasicTicks()[0], BasicTicks()[1],
                    Row(Start.AddSeconds(1).AddTicks(10), reverse ? 99 : 101, 1, Side.Buy),
                    Row(Start.AddSeconds(1).AddTicks(distinct ? 20 : 10), reverse ? 101 : 99, 1, Side.Buy),
                    Row(Start.AddSeconds(7), 100, 1, Side.Buy)));
                AssertEqual(distinct ? (reverse ? OrderFlowBarrierOutcome.InvalidationFirst : OrderFlowBarrierOutcome.TargetFirst)
                    : OrderFlowBarrierOutcome.AmbiguousSameTimestamp, result.Labels[0].Outcome, "Only source microseconds order barriers");
                AssertEqual(distinct && reverse ? 2L : 1L, result.Labels[0].TimeToTargetMicroseconds, "Exact delay");
            }
        }

        private static void TestIncompleteAndGap(string root)
        {
            AssertEqual(OrderFlowBarrierOutcome.Incomplete, Replay(Request(root, BasicTicks().Take(3).ToArray())).Labels[0].Outcome, "Truncated horizon");
            AssertEqual(OrderFlowBarrierOutcome.NoFutureTrade, Replay(Request(root, BasicTicks()[0], BasicTicks()[1],
                Row(Start.AddDays(1), 100, 1, Side.Buy))).Labels[0].Outcome, "Elapsed horizon without future ticks");
        }

        private static void TestFeatureFormulas(string root)
        {
            OrderFlowResearchRequest request = Request(root, BasicTicks()); request.FeatureWindowSeconds = 1;
            OrderFlowFeatureWindow window = new OrderFlowFeatureWindow();
            window.Build(Bucket(Start.AddSeconds(1), 100, 10, Side.Buy), request);
            OrderFlowFeatureSnapshot feature = window.Build(Bucket(Start.AddSeconds(2), 102, 30, Side.Sell), request);
            AssertEqual(10m, feature.BuyVolume, "Left boundary included");
            AssertEqual(-20m, feature.Delta, "Buy minus sell");
            AssertEqual(2, feature.TradeCount, "Window count");
            AssertEqual(102m, feature.ReferencePrice, "Latest VWAP");
            AssertEqual(2m, feature.PriceChange, "Latest minus earliest VWAP");
            AssertEqual(0.1m, feature.PriceResponse, "Change / absolute delta");
            OrderFlowFeatureSnapshot next = window.Build(Bucket(Start.AddSeconds(2).AddTicks(10), 104, 30, Side.Buy), request);
            AssertEqual(2, next.TradeCount, "Evict strictly outside window");
            AssertEqual(0m, next.Delta, "Balanced flow");
            AssertEqual(0m, next.PriceResponse, "Zero delta sentinel");
            AssertEqual(10m, feature.BuyVolume, "Published feature detached");
        }

        private static OrderFlowBucket Bucket(DateTime time, decimal price, decimal volume, Side side)
        {
            return new OrderFlowBucket { Time = time, Deals = new List<OrderFlowDeal> {
                new OrderFlowDeal { Time = time, Price = price, Volume = volume, Side = side } } };
        }

        private static void TestDateRange(string root)
        {
            DateTime day = Start.Date;
            OrderFlowResearchRequest request = Request(root, Row(day.AddTicks(-10), 50, 1000, Side.Sell),
                Row(day, 100, 10, Side.Buy), Row(day.AddDays(1).AddTicks(-10), 100, 200, Side.Sell),
                Row(day.AddDays(1), 101, 1, Side.Buy), Row(day.AddDays(1).AddSeconds(10), 101, 1, Side.Buy));
            request.FromDate = day; request.ToDate = day;
            OrderFlowResearchResult result = Export(request);
            AssertTrue(result.Quality.ResearchAccepted, "Inclusive day accepted");
            AssertEqual(5L, result.Input.RecordCount, "Full source validated");
            AssertEqual(2L, result.Quality.DealCount, "Includes first and last microsecond of selected date");
            AssertEqual(10m, result.Observations[0].Features.Delta, "No outside warmup");
            AssertEqual(1, result.Candidates.Count, "End-of-day candidate");
            AssertEqual(0, result.Labels[0].FutureTradeCount, "No outside future ticks");
            AssertEqual(OrderFlowBarrierOutcome.Incomplete, result.Labels[0].Outcome, "Excluded suffix cannot complete horizon");
            AssertEqual(StoredHash(request.TicksFilePath), result.Input.Sha256, "Full provenance");
        }

        private static void TestExcludedInvalid(string root)
        {
            OrderFlowResearchRequest request = Request(root, BasicTicks().Concat(new[] { "invalid suffix" }).ToArray());
            request.FromDate = Start.Date; request.ToDate = Start.Date;
            AssertRejected(Export(request), "TICK_INVALID");
        }

        private static void TestEmptyRange(string root)
        {
            OrderFlowResearchRequest request = Request(root, BasicTicks());
            request.FromDate = Start.Date.AddDays(1); request.ToDate = request.FromDate;
            OrderFlowResearchResult result = Export(request);
            AssertRejected(result, "TICKS_EMPTY");
            AssertTrue(result.Input.ReadComplete, "Full source checked");
            AssertEqual(0, result.Candidates.Count, "No fabricated candidate");
        }

        private static void TestDateValidation(string root)
        {
            OrderFlowResearchRequest request = Request(root, BasicTicks());
            request.FromDate = Start; Expect<ArgumentException>(() => request.Validate());
            request.ToDate = Start.AddDays(-1); Expect<ArgumentException>(() => request.Validate());
            request.ToDate = Start.AddHours(-1); request.Validate();
            AssertEqual(Start.Date, request.FromDate.Value, "Only calendar date");
            AssertEqual(Start.Date, request.ToDate.Value, "Same calendar date accepted");
        }

        private static void TestMidnight(string root)
        {
            DateTime midnight = Start.Date.AddDays(1);
            OrderFlowResearchResult result = Replay(Request(root, Row(midnight.AddSeconds(-1), 100, 10, Side.Buy),
                Row(midnight, 100, 200, Side.Sell), Row(midnight.AddSeconds(6), 101, 1, Side.Buy)));
            AssertEqual(-190m, result.Observations.Single(o => o.CandidateId != null).Features.Delta, "No daily reset");
            AssertEqual(2, result.Bars[OrderFlowDisplayTimeFrame.Min1].Count, "Clock bars across dates");
        }

        private static void TestArtifacts(string root)
        {
            OrderFlowResearchRequest request = Request(root, BasicTicks());
            OrderFlowResearchResult first = Export(request);
            Dictionary<string, string> hashes = Directory.GetFiles(first.ArtifactDirectory).ToDictionary(Path.GetFileName, StoredHash);
            OrderFlowResearchResult repeat = Export(request);
            AssertEqual(first.ArtifactDirectory, repeat.ArtifactDirectory, "Same bundle reused");
            AssertEqual(first.FeatureHash, repeat.FeatureHash, "Features deterministic");
            AssertEqual(first.CandidateHash, repeat.CandidateHash, "Candidates deterministic");
            foreach (string path in Directory.GetFiles(repeat.ArtifactDirectory))
            { AssertEqual(hashes[Path.GetFileName(path)], StoredHash(path), "All bytes identical"); }
            string pathToChange = Path.Combine(first.ArtifactDirectory, "observations.csv");
            File.AppendAllText(pathToChange, "tampered");
            Expect<InvalidOperationException>(() => Export(request));
            AssertTrue(File.ReadAllText(pathToChange).EndsWith("tampered"), "Altered bundle not overwritten");
        }

        private static void TestSpecIdentity(string root)
        {
            OrderFlowResearchRequest request = Request(root, BasicTicks());
            OrderFlowResearchResult original = Replay(request);
            request.PriceStep = 0.00001m;
            OrderFlowResearchResult changed = Replay(request);
            AssertEqual(original.InputHash, changed.InputHash, "Same input bytes");
            AssertEqual(original.FeatureHash, changed.FeatureHash, "Same absolute features");
            AssertFalse(original.ResearchSpecHash == changed.ResearchSpecHash, "Step enters spec");
            AssertFalse(original.Observations[0].ObservationKey == changed.Observations[0].ObservationKey, "Distinct observation identity");
            request.FromDate = Start.Date; request.ToDate = Start.Date;
            OrderFlowResearchResult dated = Replay(request);
            AssertFalse(changed.ResearchSpecHash == dated.ResearchSpecHash, "Dates enter spec");
            AssertEqual(changed.NormalizedEventHash, dated.NormalizedEventHash, "Same selected events");
        }

        private static void TestSchema(string root)
        {
            OrderFlowResearchResult result = Export(Request(root, BasicTicks()));
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(result.ArtifactDirectory, "manifest.json")));
            JsonElement manifest = document.RootElement;
            AssertEqual(result.Input.Sha256, manifest.GetProperty("Input").GetProperty("Sha256").GetString(), "Single raw input");
            AssertEqual(1m, manifest.GetProperty("PriceStep").GetDecimal(), "Manual step persisted");
            AssertTrue(manifest.GetProperty("Input").GetProperty("ReadComplete").GetBoolean(), "Validation complete");
            foreach (string path in Directory.GetFiles(result.ArtifactDirectory))
            {
                string text = File.ReadAllText(path);
                foreach (string old in new[] { "BookImbalance", "book_age", "spread", "VolumeStep", "Quotes", "QSH" })
                { AssertFalse(text.Contains(old, StringComparison.OrdinalIgnoreCase), "Removed field absent: " + old); }
            }
            string labels = File.ReadAllText(Path.Combine(result.ArtifactDirectory, "market-path-labels.csv"));
            AssertTrue(labels.Contains("time_to_target_us") && labels.Contains(".000000"), "Microsecond units and format");
        }

        private static string StoredHash(string path)
        {
            using FileStream file = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();
        }

        private static void AssertRejected(OrderFlowResearchResult result, string code)
        {
            AssertFalse(result.Quality.ResearchAccepted, "Research rejected");
            AssertTrue(result.Quality.Issues.Any(issue => issue.IsRejection && issue.ReasonCode == code), "Rejection " + code);
        }

        private static void Expect<T>(Action action) where T : Exception
        {
            try { action(); } catch (T) { return; }
            throw new InvalidOperationException("Expected " + typeof(T).Name);
        }

        private static void AssertTrue(bool condition, string message)
        { if (!condition) { throw new InvalidOperationException(message); } }
        private static void AssertFalse(bool condition, string message) { AssertTrue(!condition, message); }
        private static void AssertEqual<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            { throw new InvalidOperationException(message + ". Expected " + expected + ", actual " + actual + "."); }
        }
    }
}
