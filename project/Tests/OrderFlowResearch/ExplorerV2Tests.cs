/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.Entity;
using OsEngine.OsData.OrderFlow;
using OsEngine.OsData.OrderFlow.Explorer;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace OsEngine.OrderFlowResearch.Tests
{
    internal static partial class Program
    {
        /// <summary>Offline V2 acceptance alongside the unchanged legacy suite; it creates unshown Explorer Window objects but starts no Application, shown OS window, connector, credentials or orders.</summary>
        private static void RegisterExplorerV2(string root)
        {
            Run("V2FullCatalogAndNoIoFilters", root, TestV2FullCatalog);
            Run("V2SingleTicksAndFractionalVolumes", root, TestV2Single);
            Run("V2IndependentDeltaAndDiagonal", root, TestV2Delta);
            Run("V2DistributionRanksAndRemoval", root, TestV2Distribution);
            Run("V2FrozenAdaptiveParameters", root, TestV2Adaptive);
            Run("V2CausalAtrWarmupAndGapReset", root, TestV2Atr);
            Run("V2RollingAndPreviousDateBackground", root, TestV2Baseline);
            Run("V2EpisodesKeepChildrenAndRawVolume", root, TestV2Episodes);
            Run("V2WeightedVwapAndKnownAt", root, TestV2Vwap);
            Run("V2PivotsConfirmedLaterAndEqualExtrema", root, TestV2Swing);
            Run("V2LongShortStructureAndNoRetroactiveTrigger", root, TestV2Structure);
            Run("V2EqualLowAndExpiry", root, TestV2InvalidStructure);
            Run("V2FutureLabelsOrdinalHorizonAndCutoff", root, TestV2Labels);
            Run("V2HashDependenciesAndImmutableReuse", root, TestV2Hashes);
            Run("V2StudyPrefixIndexAndReplayParity", root, TestV2Replay);
            Run("V2SamePrefixIndependentRecalculation", root, TestV2Prefixes);
            Run("V2InvalidExcludedSuffixNoPublication", root, TestV2Invalid);
            Run("V2CancellationAndMemoryLimit", root, TestV2Cancellation);
            Run("V2CorruptBundleRejected", root, TestV2Corrupt);
            Run("V2LegacyExportIsolation", root, TestV2Legacy);
            Run("V2GuidedThemeAndHelpMarkup", root, TestV2GuidedThemeAndHelpMarkup);
            Run("V2UiComponentAndChartRendering", root, TestV2Ui);
            RegisterExplorerAcceptance(root);
        }
        private static ExplorerRunSpec V2Spec(string root, params string[] rows)
        {
            OrderFlowResearchRequest source = Request(root, rows);
            return new ExplorerRunSpec { InputPath = source.TicksFilePath, OutputRootPath = Path.Combine(root, "explorer"), PriceStep = 1,
                Profiles = ImmutableArray.Create(new ExplorerProfile { MaximumGapMilliseconds = 1000, MaximumRangeTicks = 5 }),
                Study = new ExplorerStudySpec { SwingsEnabled = false } };
        }
        private static ExplorerRun V2Run(ExplorerRunSpec spec) => ExplorerRunner.Catalog(spec, CancellationToken.None);
        private static ExplorerCloud[] V2Clouds(ExplorerRun run) => ExplorerStorage.ReadRows<ExplorerCloud>(run.CatalogPath, "catalog").ToArray();
        private static OrderFlowDeal V2Tick(long sequence, decimal price, decimal volume = 1, DateTime? time = null) => new OrderFlowDeal
        { SourceSequence = sequence, Time = time ?? Start.AddSeconds(sequence - 1), Price = price, Volume = volume, Side = Side.Buy };

        private static void TestV2FullCatalog(string root)
        {
            ExplorerRunSpec spec = V2Spec(root, new[] { 400m, 400m, 400m, 1200m }.Select((volume, i) => Row(Start.AddSeconds(i * 2), 100, volume, Side.Buy)).ToArray());
            ExplorerRun run = V2Run(spec); ExplorerCloud[] clouds = V2Clouds(run);
            AssertEqual(4, clouds.Length, "All chains survive"); AssertEqual("OpenAtEnd", clouds[3].Reason, "EOF distinct");
            string hash = run.Spec.CatalogSpecHash; File.Delete(spec.InputPath);
            ExplorerView high = new ExplorerView { MinimumVolume = 900 }, low = high with { MinimumVolume = 300 };
            AssertEqual(1L, ExplorerStorage.Summarize(run.CatalogPath, run.Spec, high, CancellationToken.None)[0].Passed, "900");
            AssertEqual(4L, ExplorerStorage.Summarize(run.CatalogPath, run.Spec, low, CancellationToken.None)[0].Passed, "300 without raw file");
            AssertEqual(1L, ExplorerStorage.Summarize(run.CatalogPath, run.Spec, high, CancellationToken.None)[0].Passed, "900 restored");
            AssertEqual(hash, run.Spec.CatalogSpecHash, "Filter identity invariant");
            AssertEqual(clouds[3].Id, clouds.Single(c => high.Passes(c, 1)).Id, "Same exact ID");
        }
        private static void TestV2Single(string root)
        {
            ExplorerRunSpec spec = V2Spec(root, Row(Start, 100, .25m, Side.Buy), Row(Start, 100, .25m, Side.Buy), Row(Start, 101, 1, Side.Sell));
            spec = spec with { Profiles = ImmutableArray.Create(new ExplorerProfile { SingleTicks = true, AllTicks = true }) };
            ExplorerCloud[] clouds = V2Clouds(V2Run(spec)); AssertEqual(3, clouds.Length, "Physical rows retained");
            AssertTrue(clouds.All(c => c.Count == 1 && c.Reason == "SingleTick" && c.KnownSequence == c.FirstSequence), "Immediate finals");
            AssertEqual(3, clouds.Select(c => c.Id).Distinct().Count(), "Stable source identity");
        }
        private static void TestV2Delta(string root)
        {
            ExplorerRun run = V2Run(V2Spec(root, Row(Start, 100, 2, Side.Buy), Row(Start, 100, 1, Side.Sell)));
            ExplorerCloud cloud = V2Clouds(run).Single();
            AssertTrue(new ExplorerView { MinimumVolume = null, MinimumDelta = 33.333333333333333333333333333m }.Passes(cloud, 1), "Exact lower delta");
            AssertTrue(!new ExplorerView { MinimumVolume = null, MinimumDelta = 33.333333333333333333333333334m }.Passes(cloud, 1), "No rounded PASS");
            AssertTrue(new ExplorerView { MinimumVolume = null, MinimumContextDelta = 30 }.Passes(cloud, 1), "Context independent of diagonal Off");
        }
        private static void TestV2Distribution(string root)
        {
            ExplorerDistribution distribution = new ExplorerDistribution(); Random random = new Random(45); List<decimal> values = new List<decimal>();
            for (int i = 0; i < 4000; i++)
            {
                decimal value = random.Next(1000); distribution.Add(value); values.Add(value);
                if (i % 3 == 0) { distribution.Remove(values[0]); values.RemoveAt(0); }
                decimal[] sorted = values.OrderBy(v => v).ToArray();
                if (sorted.Length == 0) { AssertEqual(0, distribution.Count, "Empty distribution"); continue; }
                AssertEqual(sorted[(int)Math.Ceiling(.95 * sorted.Length) - 1], distribution.Quantile(.95m), "AVL nearest rank");
                AssertEqual(sorted.Count(v => v <= 400), distribution.AtMost(400), "AVL rank");
            }
        }
        private static void TestV2Adaptive(string root)
        {
            ExplorerRunSpec spec = V2Spec(root, Row(Start, 100, 2, Side.Buy), Row(Start.AddSeconds(2), 100, 4, Side.Buy),
                Row(Start.AddSeconds(4), 100, 6, Side.Buy), Row(Start.AddSeconds(4), 100, 1, Side.Buy), Row(Start.AddSeconds(4), 100, 6, Side.Buy));
            spec = spec with { Profiles = ImmutableArray.Create(new ExplorerProfile { AdaptTick = true, TickMinimum = 2, TickWindow = 3, TickPercentile = .75m }) };
            ExplorerCloud[] clouds = V2Clouds(V2Run(spec));
            AssertEqual(3, clouds.Length, "Chains unaffected by tiny raw row"); AssertEqual(4m, clouds[2].Effective.TickVolume, "Previous ticks p75");
            AssertEqual(2, clouds[2].Count, "Frozen threshold excludes tiny row"); AssertEqual(12m, clouds[2].Volume, "Frozen own conditions");
        }
        private static void TestV2Atr(string root)
        {
            ExplorerRawMetrics metrics = new ExplorerRawMetrics(new ExplorerRunSpec());
            for (int i = 0; i < 21; i++)
            {
                OrderFlowDeal first = V2Tick(i * 2 + 1, 100, time: Start.AddMinutes(i)); metrics.Before(first); metrics.After(first);
                OrderFlowDeal last = V2Tick(i * 2 + 2, 102, time: Start.AddMinutes(i).AddSeconds(30)); metrics.Before(last); metrics.After(last);
            }
            AssertTrue(!metrics.Atr.HasValue, "Current M1 excluded");
            OrderFlowDeal next = V2Tick(43, 999, time: Start.AddMinutes(21)); metrics.Before(next);
            AssertEqual(2m, metrics.Atr.Value, "Seed close plus 20 closed TR before huge current price"); metrics.After(next);
            metrics.Before(V2Tick(44, 100, time: Start.AddMinutes(27))); AssertTrue(!metrics.Atr.HasValue, "Gap resets warmup");
        }
        private static ExplorerCloud BaselineCloud(int sequence, decimal volume, DateTime start) => new ExplorerCloud { Id = "test/" + sequence,
            FirstSequence = sequence, LastSequence = sequence, StartTime = start, Time = start, KnownAt = start, KnownSequence = sequence,
            Buy = volume, Count = 1, Reason = "SingleTick", Effective = new ExplorerEffective(1, 1, 1, null, null, null, null, "") };
        private static void TestV2Baseline(string root)
        {
            ExplorerVolumeBaseline baseline = new ExplorerVolumeBaseline(new ExplorerProfile { VolumeMinimum = 2, VolumeWindow = 3, TimeOfDayVolume = true }, 1000);
            baseline.Advance(1); baseline.Add(BaselineCloud(1, 10, Start), 1); baseline.Add(BaselineCloud(2, 20, Start), 1);
            AssertTrue(!baseline.Rolling(2).HasValue, "Same-ordinal finish excluded"); AssertEqual(20m, baseline.Rolling(3).Value, "Previous chain distribution");
            AssertTrue(!baseline.TimeOfDay(Start).HasValue, "Current date excluded"); baseline.Advance(2);
            AssertEqual(20m, baseline.TimeOfDay(Start.AddDays(1)).Value, "Previous date included");
            AssertTrue(!baseline.TimeOfDay(Start.AddHours(2)).HasValue, "Time slice exact");
        }
        private static void TestV2Episodes(string root)
        {
            ExplorerRunSpec spec = V2Spec(root, Row(Start, 100, 400, Side.Buy), Row(Start.AddSeconds(1), 100, 50, Side.Sell),
                Row(Start.AddSeconds(2), 100, 400, Side.Buy), Row(Start.AddSeconds(3), 100, 50, Side.Sell), Row(Start.AddSeconds(4), 100, 400, Side.Buy),
                Row(Start.AddSeconds(20), 100, 400, Side.Buy));
            spec = spec with { Profiles = ImmutableArray.Create(new ExplorerProfile { MinimumTickVolume = 100, SingleTicks = true }), Episodes = new ExplorerEpisodeSpec { Enabled = true } };
            ExplorerRun run = ExplorerEpisodeRunner.Run(V2Run(spec), CancellationToken.None);
            ExplorerEpisode[] episodes = ExplorerStorage.ReadRows<ExplorerEpisode>(run.EpisodePath, "episodes").ToArray();
            AssertEqual(2, episodes.Length, "Pause breaks episode"); AssertEqual(3, episodes[0].ChildCount, "Three children retained");
            AssertEqual(1200m, episodes[0].Volume, "Cloud sum"); AssertEqual(1300m, episodes[0].RawVolume, "Small intervening raw volume included");
            AssertEqual("EpisodeOpenAtEnd", episodes[1].Reason, "EOF remains incomplete");
        }
        private static void TestV2Vwap(string root)
        {
            ExplorerVwap vwap = new ExplorerVwap(); vwap.Add(100, 1); vwap.Add(200, 3);
            AssertEqual(175m, vwap.Mean, "Weighted mean"); AssertTrue(Math.Abs(vwap.Sigma - 43.3012701892219m) < .000000001m, "Weighted sigma");
            ExplorerAnchorSeries series = new ExplorerAnchorSeries(new ExplorerAnchor("a", 1, 3, Start.Date));
            series.Add(V2Tick(1, 100)); series.Add(V2Tick(2, 200, 3)); AssertEqual(0, series.Snapshot(2).Length, "Not yet recognized");
            series.Add(V2Tick(3, 175)); AssertEqual(3, series.Snapshot(3).Length, "Visible only at known ordinal");
            series.Add(V2Tick(4, 1000, time: Start.AddDays(1))); AssertEqual(3, series.Snapshot(4).Length, "No overnight anchor");
        }
        private static void TestV2Swing(string root)
        {
            ExplorerSwing swing = new ExplorerSwing(new ExplorerRunSpec { Study = new ExplorerStudySpec { AdaptSwing = false, SwingReversalTicks = 5 } });
            AssertTrue(swing.Add(V2Tick(1, 100), null) == null, "Undefined first direction");
            ExplorerPivot low = swing.Add(V2Tick(2, 105), null); AssertEqual(1L, low.ObservedSequence, "First low time"); AssertEqual(2L, low.KnownSequence.Value, "Later knowledge");
            swing.Add(V2Tick(3, 105), null); ExplorerPivot high = swing.Add(V2Tick(4, 100), null);
            AssertEqual(2L, high.ObservedSequence, "First equal high retained"); AssertEqual(4L, high.KnownSequence.Value, "High confirmation later");
        }
        private static List<ExplorerObservation> StructureSequence(decimal[] prices, bool mirror, long triggerSequence, int watchMinutes = 60)
        {
            ExplorerRunSpec spec = new ExplorerRunSpec { InputSha256 = "synthetic", Study = new ExplorerStudySpec { Enabled = true, AdaptSwing = false, SwingReversalTicks = 2, WatchMinutes = watchMinutes } };
            ExplorerRawMetrics metrics = new ExplorerRawMetrics(spec); List<ExplorerObservation> observations = new List<ExplorerObservation>();
            ExplorerStructure structure = new ExplorerStructure(spec, p => { }, observations.Add, d => { });
            for (int i = 0; i < prices.Length; i++)
            {
                OrderFlowDeal tick = V2Tick(i + 1, mirror ? 220 - prices[i] : prices[i]); metrics.Before(tick);
                ExplorerTrigger[] triggers = tick.SourceSequence == triggerSequence ? new[] { new ExplorerTrigger("trigger", "cloud", "Cloud1/Base", "Cloud", tick.Time, tick.SourceSequence, 900, 1000, 1000, 0, tick.Price, tick.Price, tick.Price, tick.Price, 0) } : Array.Empty<ExplorerTrigger>();
                structure.Add(tick, metrics, triggers, true); metrics.After(tick);
            }
            return observations;
        }
        private static readonly decimal[] StructurePrices = { 110, 108, 100, 103, 107, 104, 98, 101, 105, 102, 100, 102, 104, 106 };
        private static void TestV2Structure(string root)
        {
            ExplorerObservation armed = StructureSequence(StructurePrices, false, 9).Single(o => o.Status == "Breakout");
            AssertEqual("Long", armed.Direction, "Long shape"); AssertEqual("VolumeArmed", armed.Group, "Armed before breakout");
            AssertEqual(14L, armed.Sequence, "Strictly subsequent breakout"); AssertEqual(99m, armed.DiagnosticStop.Value, "Diagnostic only");
            AssertEqual("Short", StructureSequence(StructurePrices, true, 9).Single(o => o.Status == "Breakout").Direction, "Mirrored Short");
            AssertEqual("Control", StructureSequence(StructurePrices, false, 2).Single(o => o.Status == "Breakout").Group, "No retroactive early trigger");
            AssertEqual("Control", StructureSequence(StructurePrices, false, 14).Single(o => o.Status == "Breakout").Group, "Same-row trigger cannot arm breakout");
        }
        private static void TestV2InvalidStructure(string root)
        {
            decimal[] equal = (decimal[])StructurePrices.Clone(); equal[10] = 98;
            AssertTrue(!StructureSequence(equal, false, 9).Any(o => o.Status == "Breakout"), "Equal low not higher low");
            decimal[] lower = (decimal[])StructurePrices.Clone(); lower[10] = 97;
            AssertTrue(!StructureSequence(lower, false, 9).Any(o => o.Status == "Breakout"), "Lower low invalidates");
        }
        private static ExplorerObservation LabelObservation(DateTime time, long sequence = 1) => new ExplorerObservation("o" + sequence, "Long", time.AddMinutes(-1), sequence - 1,
            time, sequence, "Control", "Breakout", null, null, null, null, null, null, null, 100, 95, 2, 100, time.AddMinutes(-1), 30);
        private static void TestV2Labels(string root)
        {
            ExplorerRunSpec spec = new ExplorerRunSpec { Study = new ExplorerStudySpec { HorizonsMinutes = ImmutableArray.Create(1) } }; List<ExplorerLabel> output = new List<ExplorerLabel>();
            ExplorerLabels labels = new ExplorerLabels(spec, new[] { Start.Date }, output.Add); labels.Add(LabelObservation(Start));
            labels.Tick(V2Tick(2, 103, time: Start)); labels.Tick(V2Tick(3, 97, time: Start)); labels.Tick(V2Tick(4, 101, time: Start.AddMinutes(1).AddSeconds(1)));
            AssertEqual("TargetFirst", output[0].Outcome, "Physical ordinal resolves same-time first hit"); AssertEqual(1.5m, output[0].MaeAtr.Value, "Full horizon excursion even after first hit");
            labels.Add(LabelObservation(Start.Date.AddHours(23).AddMinutes(59).AddSeconds(30), 10));
            AssertEqual("Incomplete/DateCutoff", output[1].Outcome, "No midnight continuation");
            labels.Add(LabelObservation(Start.AddHours(1), 20)); labels.Complete(V2Tick(21, 100, time: Start.AddHours(1).AddSeconds(10)));
            AssertEqual("Incomplete", output[2].Outcome, "EOF cannot invent horizon coverage");
        }
        private static void TestV2Hashes(string root)
        {
            ExplorerRun run = V2Run(V2Spec(root, BasicTicks())); ExplorerRunSpec spec = run.Spec;
            ExplorerRunSpec study = spec with { Study = new ExplorerStudySpec { Enabled = true, TriggerVolume = 300 } };
            AssertEqual(spec.CatalogSpecHash, study.CatalogSpecHash, "Independent study identity");
            AssertTrue(spec.StudyHash != study.StudyHash, "Versioned study");
            ExplorerRunSpec episode = spec with { Episodes = new ExplorerEpisodeSpec { Enabled = true } };
            AssertEqual(spec.CatalogSpecHash, episode.CatalogSpecHash, "Independent episode identity");
            AssertEqual(run.CatalogPath, V2Run(spec).CatalogPath, "Exact bundle reused");
        }
        private static void TestV2Replay(string root)
        {
            ExplorerRunSpec spec = V2Spec(root, StructurePrices.Select((p, i) => Row(Start.AddSeconds(i), p, i == 8 ? 1000 : 10, Side.Buy)).ToArray()) with
            { Profiles = ImmutableArray.Create(new ExplorerProfile { SingleTicks = true }), Study = new ExplorerStudySpec { Enabled = true, AdaptSwing = false, SwingReversalTicks = 2 } };
            ExplorerRun run = ExplorerStudyRunner.Run(V2Run(spec), CancellationToken.None);
            ExplorerObservation[] historical = ExplorerStorage.ReadRows<ExplorerObservation>(run.StudyPath, "observations").ToArray();
            using ExplorerReplayCursor replay = new ExplorerReplayCursor(run, null, CancellationToken.None); ExplorerFrame frame;
            do { frame = replay.Step(); } while (!frame.Complete);
            AssertEqual(JsonSerializer.Serialize(historical), JsonSerializer.Serialize(frame.Observations), "Same shared study kernel");
            AssertEqual(StructurePrices.Length, frame.Clouds.Length, "All single ticks available");
        }
        private static void TestV2Prefixes(string root)
        {
            ExplorerRunSpec spec = V2Spec(root, Row(Start, 100, 400, Side.Buy), Row(Start, 100, 400, Side.Buy), Row(Start, 100, 400, Side.Buy), Row(Start.AddSeconds(3), 105, 1200, Side.Sell));
            ExplorerRun run = V2Run(spec); using ExplorerReplayCursor replay = new ExplorerReplayCursor(run, null, CancellationToken.None);
            List<OrderFlowDeal> ticks = new List<OrderFlowDeal> { V2Tick(1, 100, 400, Start), V2Tick(2, 100, 400, Start), V2Tick(3, 100, 400, Start), V2Tick(4, 105, 1200, Start.AddSeconds(3)) };
            ticks[3].Side = Side.Sell;
            for (int count = 1; count <= ticks.Count; count++)
            {
                ExplorerFrame frame = replay.Step(); List<ExplorerCloud> completed = new List<ExplorerCloud>(); ExplorerCatalog independent = new ExplorerCatalog(run.Spec, completed.Add, p => { });
                foreach (OrderFlowDeal tick in ticks.Take(count)) { independent.Add(tick); }
                AssertEqual(JsonSerializer.Serialize(completed.Concat(independent.Forming)), JsonSerializer.Serialize(frame.Clouds), "Prefix N recomputed without suffix");
                AssertEqual((long)count, frame.Sequence, "One step one physical row");
            }
        }
        private static void TestV2Invalid(string root)
        {
            ExplorerRunSpec spec = V2Spec(root, Row(Start, 100, 1, Side.Buy), "broken") with { FromDate = Start.Date, ToDate = Start.Date };
            bool rejected = false; try { V2Run(spec); } catch (InvalidDataException) { rejected = true; }
            AssertTrue(rejected, "Invalid suffix rejects full input"); AssertEqual(0, Directory.GetDirectories(spec.OutputRootPath).Length, "No partial bundle");
        }
        private static void TestV2Cancellation(string root)
        {
            ExplorerRunSpec spec = V2Spec(root, BasicTicks()); using CancellationTokenSource cancel = new CancellationTokenSource(); cancel.Cancel();
            bool cancelled = false; try { ExplorerRunner.Catalog(spec, cancel.Token); } catch (OperationCanceledException) { cancelled = true; }
            AssertTrue(cancelled, "Cancelled before publish"); using FileStream exclusive = new FileStream(spec.InputPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            ExplorerRunSpec limited = spec with { MaximumBufferItems = 1000 }; ExplorerRawMetrics metrics = new ExplorerRawMetrics(limited);
            bool rejected = false; try { for (int i = 0; i < 1001; i++) { OrderFlowDeal tick = V2Tick(i + 1, 100, time: Start); metrics.Before(tick); metrics.After(tick); } } catch (InvalidDataException) { rejected = true; }
            AssertTrue(rejected, "Explicit bound rejects dense same-time window");
        }
        private static void TestV2Corrupt(string root)
        {
            ExplorerRun run = V2Run(V2Spec(root, BasicTicks())); File.AppendAllText(Path.Combine(run.CatalogPath, "quality.json"), "x");
            bool rejected = false; try { V2Run(run.Spec); } catch (InvalidDataException) { rejected = true; } AssertTrue(rejected, "Checksum rejection");
        }
        private static void TestV2Legacy(string root)
        {
            OrderFlowResearchRequest request = TwoCloudRequest(root);
            OrderFlowResearchResult before = Export(request); string snapshot = JsonSerializer.Serialize(before);
            Dictionary<string, string> files = Directory.GetFiles(request.OutputRootPath, "*", SearchOption.AllDirectories).ToDictionary(p => p, OrderFlowFileHash.Calculate);
            // Frozen against the unchanged legacy engine at a6cca6b; these are not Explorer exports.
            AssertEqual("0588725a8c2767feedf4f0c7383d7c89936c10129d12901d982b795b6798cbc7", before.ResearchSpecHash, "Legacy frozen research identity");
            AssertEqual("0cecb9540e0b77aeff3d6c389a1ecdb7ac124ae2d5c9baa9e1b5f12c281b839b", before.CloudHash, "Legacy frozen Cloud1 hash");
            AssertEqual("7f593b4502f6ed259f47ac676953fa5bf1ced64770b47a99b724ba5dea36ddf9", before.Cloud2Hash, "Legacy frozen Cloud2 hash");
            AssertEqual("09873acfa15400af73f0f351f8536e13610174426e85381c280e0367e7168e90", files.Single(p => Path.GetFileName(p.Key) == "clouds.csv").Value, "Legacy frozen Cloud1 CSV bytes");
            AssertEqual("ab3b0551907990c490f66ea34a2d1851bb5ca73de0b2c4c920a45c03a820ca7f", files.Single(p => Path.GetFileName(p.Key) == "clouds2.csv").Value, "Legacy frozen Cloud2 CSV bytes");
            ExplorerRun run = V2Run(new ExplorerRunSpec { InputPath = request.TicksFilePath, OutputRootPath = request.OutputRootPath, Study = new ExplorerStudySpec { Enabled = true } });
            ExplorerStudyRunner.Run(run, CancellationToken.None);
            AssertEqual(snapshot, JsonSerializer.Serialize(before), "Original result untouched");
            foreach (KeyValuePair<string, string> file in files) { AssertEqual(file.Value, OrderFlowFileHash.Calculate(file.Key), "Legacy artifact untouched"); }
            OrderFlowResearchResult after = Export(request); AssertEqual(before.CloudHash, after.CloudHash, "Legacy cloud golden identity"); AssertEqual(before.FeatureHash, after.FeatureHash, "Legacy features");
            AssertEqual(before.Cloud2Hash, after.Cloud2Hash, "Second legacy layer unchanged");
        }
        private static void TestV2GuidedThemeAndHelpMarkup(string root)
        {
            XDocument ui;
            using (Stream stream = typeof(Program).Assembly.GetManifestResourceStream("Research.Explorer.Ui.xaml")) { ui = XDocument.Load(stream); }
            XElement userControl = ui.Root;
            AssertEqual("{DynamicResource WindowBackgroundGradientBrush}", (string)userControl.Attribute("Background"), "Explorer surface follows active Window background");
            foreach (string step in new[] { "TextBlockStep1", "TextBlockStep2", "TextBlockStep3", "TextBlockStep4" })
            {
                XElement marker = ui.Descendants().Single(element => (string)element.Attribute("Name") == step);
                AssertTrue(((string)marker.Attribute("Text")).StartsWith(step[^1] + ".", StringComparison.Ordinal), "Visible numbered workflow " + step);
            }
            string[] describedTypes = { "TextBox", "ComboBox", "CheckBox", "Button", "TabItem", "DataGrid" };
            foreach (XElement element in ui.Descendants().Where(element => describedTypes.Contains(element.Name.LocalName) &&
                (element.Name.LocalName == "TabItem" || element.Attribute("Name") != null)))
            {
                string help = (string)element.Attribute("ToolTip");
                AssertTrue(help != null && help.Length >= 25 && help.Any(character => character >= '\u0410' && character <= '\u044f'),
                    "Russian hover help " + element.Name.LocalName + " " + ((string)element.Attribute("Name") ?? (string)element.Attribute("Header")));
            }
            XElement[] grids = ui.Descendants().Where(element => element.Name.LocalName == "DataGrid").ToArray();
            AssertTrue(grids.Length >= 11, "All Explorer option and result grids inspected");
            foreach (XElement grid in grids)
            { AssertEqual("{DynamicResource DataGridStyle}", (string)grid.Attribute("Style"), "Shared themed DataGrid style " + (string)grid.Attribute("Name")); }
            foreach (string name in new[] { "DataGridFormation", "DataGridView", "DataGridAdaptation", "DataGridModules", "DataGridStudy" })
            {
                XElement grid = grids.Single(element => (string)element.Attribute("Name") == name);
                AssertEqual("{StaticResource ExplorerOptionRowStyle}", (string)grid.Attribute("RowStyle"), "Editable option row hover help " + name);
                AssertTrue(grid.Descendants().Any(element => ((string)element.Attribute("Header"))?.Contains("наведении", StringComparison.Ordinal) == true), "Visible help column " + name);
            }
            foreach (string resource in new[] { "Research.Explorer.Window.xaml", "Research.Explorer.ChartWindow.xaml" })
            {
                XDocument window;
                using (Stream stream = typeof(Program).Assembly.GetManifestResourceStream(resource)) { window = XDocument.Load(stream); }
                AssertEqual("{DynamicResource WindowStyleCanResize}", (string)window.Root.Attribute("Style"), "Explorer Window uses application chrome " + resource);
                XElement content = window.Descendants().Single(element => element.Name.LocalName == "ContentControl");
                AssertEqual("{DynamicResource WindowBackgroundGradientBrush}", (string)content.Attribute("Background"), "Explorer Window content follows theme " + resource);
            }
        }

        private static void TestV2Ui(string root)
        {
            using CloudExplorerControl control = new CloudExplorerControl(); control.Measure(new Size(1280, 720)); control.Arrange(new Rect(0, 0, 1280, 720)); control.UpdateLayout();
            RenderTargetBitmap bitmap = new RenderTargetBitmap(1280, 720, 96, 96, PixelFormats.Pbgra32); bitmap.Render(control);
            AssertTrue(control.DesiredSize.Width > 0, "Control rendered without application");
            AssertTrue(control.FindName("CheckBoxScales") != null, "Inline multi-scale checkbox restored");
            AssertEqual(6, control.ComboBoxProfile.Items.Count, "All editable scale profiles remain in the original selector");

            ExplorerRunSpec ownerSpec = V2Spec(root, Row(Start, 100, 1, Side.Buy));
            TabControl results = new TabControl(); TabItem summary = new TabItem(); TabItem explorerTab = new TabItem(); TabItem journal = new TabItem();
            results.Items.Add(summary); results.Items.Add(explorerTab); results.Items.Add(journal); results.SelectedItem = summary;
            List<CloudExplorerWindow> windows = new List<CloudExplorerWindow>();
            CloudExplorerWindow CreateWindow() => new CloudExplorerWindow(() => ownerSpec, () => "owner-input");
            void CaptureWindow(CloudExplorerWindow window) { windows.Add(window); }
            void Fail(Exception error) { throw new InvalidOperationException("Cloud Explorer launcher failed.", error); }
            using (CloudExplorerTabLauncher launcher = new CloudExplorerTabLauncher(results, explorerTab, summary, CreateWindow, CaptureWindow, Fail))
            {
                results.SelectedItem = explorerTab;
                AssertTrue(ReferenceEquals(summary, results.SelectedItem), "Launcher returns to the previous result tab");
                AssertEqual(1, windows.Count, "First selection creates one separate Explorer Window");
                AssertTrue(ReferenceEquals(windows[0].Explorer, windows[0].ContentControlExplorer.Content), "Window hosts the complete Explorer control");
                results.SelectedItem = journal; results.SelectedItem = explorerTab;
                AssertTrue(ReferenceEquals(journal, results.SelectedItem), "Repeated selection retains the new previous tab");
                AssertEqual(1, windows.Count, "Repeated selection reuses the existing Window");
                windows[0].Dispose();
                results.SelectedItem = explorerTab;
                AssertEqual(2, windows.Count, "Selection after close creates a fresh Explorer Window");
            }
            AssertTrue(windows.All(window => window.ContentControlExplorer.Content == null), "Launcher disposal releases every hosted Explorer control");
            ExplorerChart chart = new ExplorerChart(); ExplorerCloud cloud = BaselineCloud(1, 400, Start) with { Profile = "Cloud1/Base", Low = 100, High = 100, Price = 100 };
            chart.Set(new[] { cloud }, new ExplorerView { MinimumVolume = 900, ShowFiltered = true }, 1, Array.Empty<ExplorerVwapSample>());
            chart.Measure(new Size(1000, 500)); chart.Arrange(new Rect(0, 0, 1000, 500)); bitmap.Render(chart);
        }

        /// <summary>Explicit owner-file offline load run. It writes separate output only and never starts OsEngine or a trading stand.</summary>
        private static int RunExplorerOwner(string[] args)
        {
            try
            {
                if (args.Length < 3) { throw new ArgumentException("--explorer-input <tick path> <output path> [from yyyy-MM-dd] [to yyyy-MM-dd]"); }
                bool cancelLoad = args.Length > 3 && args[3] == "--cancel";
                ExplorerRunSpec spec = new ExplorerRunSpec { InputPath = args[1], OutputRootPath = args[2], PriceStep = 1,
                    FromDate = args.Length > 3 && !cancelLoad ? DateTime.ParseExact(args[3], "yyyy-MM-dd", CultureInfo.InvariantCulture) : null,
                    ToDate = args.Length > 4 ? DateTime.ParseExact(args[4], "yyyy-MM-dd", CultureInfo.InvariantCulture) : null,
                    Profiles = ImmutableArray.Create(new ExplorerProfile { RelativeVolume = true }, new ExplorerProfile { Layer = "Cloud2", SingleTicks = true, MinimumTickVolume = 100 }),
                    Episodes = new ExplorerEpisodeSpec { Enabled = true }, Study = new ExplorerStudySpec { Enabled = true } };
                Stopwatch watch = Stopwatch.StartNew(); double last = 0;
                if (cancelLoad)
                {
                    using CancellationTokenSource cancel = new CancellationTokenSource(); bool cancelled = false;
                    try { ExplorerRunner.Catalog(spec, cancel.Token, p => { if (p.Rows >= 8192) { cancel.Cancel(); } }); }
                    catch (OperationCanceledException) { cancelled = true; }
                    AssertTrue(cancelled, "Owner-file mid-read cancellation"); AssertEqual(0, Directory.GetDirectories(spec.OutputRootPath).Length, "No partial owner bundle");
                    using FileStream exclusive = new FileStream(spec.InputPath, FileMode.Open, FileAccess.Read, FileShare.None);
                    Console.WriteLine("Owner cancellation PASS; source handle released; seconds=" + watch.Elapsed.TotalSeconds); return 0;
                }
                Action<ExplorerProgress> progress = p => { if (watch.Elapsed.TotalSeconds - last >= 5) { last = watch.Elapsed.TotalSeconds; Console.WriteLine(p.Phase + " rows=" + p.Rows + " clouds=" + p.Clouds + " MiB=" + p.MemoryBytes / 1048576); } };
                ExplorerRun run = ExplorerRunner.Catalog(spec, CancellationToken.None, progress); run = ExplorerEpisodeRunner.Run(run, CancellationToken.None, progress); run = ExplorerStudyRunner.Run(run, CancellationToken.None, progress);
                if (Path.GetFileName(spec.InputPath).Equals("SRU6.txt", StringComparison.OrdinalIgnoreCase))
                {
                    AssertEqual("435ea400ffcc45cd3215be0806f660368a024d1c2942b8eed8aa8e3d2fed1f7b", run.Spec.InputSha256, "Reference owner checksum");
                    AssertEqual(3130667L, run.Catalog.Rows, "Full reference input validated");
                    if (!spec.FromDate.HasValue) { AssertEqual(174, run.Catalog.Dates.Length, "Observed dates"); AssertEqual(3130667L, run.Catalog.SelectedRows, "All source rows selected"); }
                }
                IReadOnlyList<ExplorerSummary> summary = ExplorerStorage.Summarize(run.CatalogPath, run.Spec, new ExplorerView(), CancellationToken.None);
                Stopwatch pageWatch = Stopwatch.StartNew(); ExplorerPage page = ExplorerStorage.Page(run.CatalogPath, new ExplorerView { ShowFiltered = true }, 1, 0, 250, CancellationToken.None);
                ExplorerChart chart = new ExplorerChart(); chart.Set(page.Rows, new ExplorerView { ShowFiltered = true }, 1, null);
                chart.Measure(new Size(1280, 720)); chart.Arrange(new Rect(0, 0, 1280, 720));
                RenderTargetBitmap bitmap = new RenderTargetBitmap(1280, 720, 96, 96, PixelFormats.Pbgra32); bitmap.Render(chart); pageWatch.Stop();
                PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (FileStream picture = File.Create(Path.Combine(spec.OutputRootPath, "component-page.png"))) { encoder.Save(picture); }
                Console.WriteLine(JsonSerializer.Serialize(new { run.Spec.InputSha256, run.Catalog.Rows, run.Catalog.SelectedRows, Dates = run.Catalog.Dates.Length, run.Catalog.Clouds,
                    Seconds = watch.Elapsed.TotalSeconds, PeakWorkingSet = Process.GetCurrentProcess().PeakWorkingSet64,
                    ArtifactBytes = new[] { run.CatalogPath, run.EpisodePath, run.StudyPath }.Sum(p => Directory.GetFiles(p).Sum(f => new FileInfo(f).Length)), PageRows = page.Rows.Count, ComponentPageMilliseconds = pageWatch.Elapsed.TotalMilliseconds, Summary = summary }));
                return 0;
            }
            catch (Exception error) { Console.WriteLine(error); return 1; }
        }
    }
}
