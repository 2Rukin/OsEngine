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
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;

namespace OsEngine.OrderFlowResearch.Tests
{
    internal static partial class Program
    {
        private static void RegisterExplorerAcceptance(string root)
        {
            Run("V2AllModulesPrefixOnlyImmutableFrames", root, TestV2AllPrefixes);
            Run("V2IndependentLayersAndDisabledModules", root, TestV2Modules);
            Run("V2WatchExpiryUnknownAndDateCutoff", root, TestV2WatchTerminals);
            Run("V2MidReadCancellationAllLevelsAndWorker", root, TestV2MidReadCancel);
            Run("V2FormationEqualityCutoffAndStableIds", root, TestV2Boundaries);
            Run("V2EpisodeZoneDurationMembershipAndRelations", root, TestV2EpisodeBoundaries);
            Run("V2VersionedIdentityAndImmutableBundles", root, TestV2Identity);
            Run("V2SamplingAndOutcomeBoundaries", root, TestV2Sampling);
            Run("V2BoundedDisplayBars", root, TestV2DisplayBars);
            Run("V2PreviousDateSameOrdinalBackground", root, TestV2DateBaseline);
            Run("V2RowWriterFailedOpenAndDispose", root, TestV2WriterFailures);
            Run("V2RelativeStudyConfigurationAndTrigger", root, TestV2RelativeStudy);
            Run("V2AnchorRunTransitionAndProvenance", root, TestV2AnchorTransition);
        }

        private static ExplorerFrame RecalculateV2Prefix(ExplorerRun run, ExplorerAnchor anchor, IReadOnlyList<OrderFlowDeal> ticks)
        {
            ExplorerRunSpec spec = run.Spec;
            List<ExplorerCloud> clouds = new List<ExplorerCloud>(); List<ExplorerEpisode> episodes = new List<ExplorerEpisode>();
            List<ExplorerPivot> pivots = new List<ExplorerPivot>(); List<ExplorerObservation> observations = new List<ExplorerObservation>();
            List<ExplorerLabel> labels = new List<ExplorerLabel>(); List<ExplorerTrigger> triggers = new List<ExplorerTrigger>();
            List<ExplorerDiagnostic> diagnostics = new List<ExplorerDiagnostic>(); List<ExplorerBar> bars = new List<ExplorerBar>();
            ExplorerStudyEngine study = new ExplorerStudyEngine(spec, run.Catalog.Dates, triggers.Add, pivots.Add, observations.Add, labels.Add, diagnostics.Add);
            ExplorerVolumeBaseline baseline = new ExplorerVolumeBaseline(spec.StudyProfile, spec.MaximumBufferItems);
            ExplorerVolumeBaseline episodeBaseline = new ExplorerVolumeBaseline(spec.StudyProfile with { TimeOfDayVolume = false }, spec.MaximumBufferItems);
            DateTime date = default; int ordinal = 0;
            ExplorerEpisodes episode = new ExplorerEpisodes(spec, e => { episodes.Add(e); episodeBaseline.Add(e.Baseline(), e.StartTime.Date == date ? ordinal : ordinal - 1); }, study.Episode, c => { });
            ExplorerCatalog catalog = new ExplorerCatalog(spec, c =>
            {
                clouds.Add(c); int cloudDate = c.StartTime.Date == date ? ordinal : ordinal - 1;
                if (c.Profile == spec.Study.Profile) { baseline.Add(c, cloudDate); }
                episode.Add(c, cloudDate);
            }, study.Prefix);
            ExplorerAnchorSeries vwap = new ExplorerAnchorSeries(anchor); ExplorerBars barBuilder = new ExplorerBars(bars.Add);
            foreach (OrderFlowDeal tick in ticks)
            {
                if (date != tick.Time.Date) { date = tick.Time.Date; ordinal++; }
                baseline.Advance(ordinal); episodeBaseline.Advance(ordinal);
                catalog.Add(tick); episode.DateCutoff(tick);
                bool known = spec.StudyProfile.TimeOfDayVolume ? baseline.TimeOfDay(tick.Time, tick.SourceSequence).HasValue : baseline.Rolling(tick.SourceSequence).HasValue;
                study.Tick(tick, spec.Study.EpisodeTrigger ? episodeBaseline.Rolling(tick.SourceSequence).HasValue : known);
                vwap.Add(tick); barBuilder.Add(tick);
            }
            OrderFlowDeal last = ticks.Last();
            return new ExplorerFrame(last.SourceSequence, last.Time, false, clouds.Concat(catalog.Forming).ToImmutableArray(), episode.Current,
                pivots.ToImmutableArray(), study.Provisional, observations.ToImmutableArray(), labels.ToImmutableArray(), vwap.Snapshot(last.SourceSequence),
                study.Watches.ToImmutableArray(), triggers.ToImmutableArray(), diagnostics.ToImmutableArray(),
                bars.Concat(barBuilder.Current == null ? Array.Empty<ExplorerBar>() : new[] { barBuilder.Current }).ToImmutableArray(),
                episodes.Concat(episode.Current == null ? Array.Empty<ExplorerEpisode>() : new[] { episode.Current }).ToImmutableArray());
        }
        private static void TestV2AllPrefixes(string root)
        {
            List<OrderFlowDeal> ticks = new List<OrderFlowDeal>();
            for (int i = 0; i < 21; i++)
            {
                ticks.Add(V2Tick(ticks.Count + 1, 100, time: Start.AddMinutes(i)));
                ticks.Add(V2Tick(ticks.Count + 1, 102, time: Start.AddMinutes(i).AddSeconds(30)));
            }
            for (int i = 0; i < StructurePrices.Length; i++) { ticks.Add(V2Tick(ticks.Count + 1, StructurePrices[i], i == 8 ? 1000 : 10, Start.AddMinutes(22).AddSeconds(i))); }
            ticks.Add(V2Tick(ticks.Count + 1, 110, time: ticks.Last().Time));
            ticks.Add(V2Tick(ticks.Count + 1, 99, time: ticks.Last().Time));
            ticks.Add(V2Tick(ticks.Count + 1, 104, time: Start.AddMinutes(26)));
            ticks.Add(V2Tick(ticks.Count + 1, 120, time: Start.AddDays(1)));
            ExplorerRunSpec spec = V2Spec(root, ticks.Select(t => Row(t.Time, t.Price, t.Volume, t.Side)).ToArray()) with
            { Episodes = new ExplorerEpisodeSpec { Enabled = true }, Study = new ExplorerStudySpec { Enabled = true, AdaptSwing = false, SwingReversalTicks = 2, HorizonsMinutes = ImmutableArray.Create(1) } };
            ExplorerRun run = ExplorerStudyRunner.Run(ExplorerEpisodeRunner.Run(V2Run(spec), CancellationToken.None), CancellationToken.None);
            ExplorerAnchor anchor = new ExplorerAnchor(run.Spec.CatalogSpecHash + "/Cloud1/Base/43", 43, 46, Start.Date);
            using ExplorerReplayCursor replay = new ExplorerReplayCursor(run, anchor, CancellationToken.None);
            List<(ExplorerFrame Frame, string Json)> snapshots = new List<(ExplorerFrame, string)>();
            for (int count = 1; count <= ticks.Count; count++)
            {
                ExplorerFrame frame = replay.Step(); string json = JsonSerializer.Serialize(frame);
                AssertEqual(JsonSerializer.Serialize(RecalculateV2Prefix(run, anchor, ticks.Take(count).ToArray())), json, "All modules equal independent prefix " + count);
                snapshots.Add((frame, json));
            }
            AssertTrue(snapshots.Any(s => s.Frame.Observations.Any(o => o.Status == "Breakout")), "Fixture reaches breakout");
            AssertTrue(snapshots.Any(s => s.Frame.Labels.Length > 0), "Fixture reaches future label");
            AssertTrue(snapshots.Any(s => s.Frame.Watches.Length > 0), "Fixture reaches active watch");
            AssertTrue(snapshots.Any(s => s.Frame.Triggers.Length > 0), "Fixture crosses volume threshold");
            ExplorerFrame final = replay.Step(); AssertTrue(final.Complete, "Separate EOF step");
            foreach ((ExplorerFrame Frame, string Json) snapshot in snapshots) { AssertEqual(snapshot.Json, JsonSerializer.Serialize(snapshot.Frame), "Published prefix remains immutable"); }
            AssertEqual(JsonSerializer.Serialize(ExplorerStorage.ReadRows<ExplorerObservation>(run.StudyPath, "observations")), JsonSerializer.Serialize(final.Observations), "Historical observation parity");
            AssertEqual(JsonSerializer.Serialize(ExplorerStorage.ReadRows<ExplorerLabel>(run.StudyPath, "future-labels")), JsonSerializer.Serialize(final.Labels), "Historical labels parity");
        }
        private static void TestV2Modules(string root)
        {
            ExplorerRunSpec spec = V2Spec(root, StructurePrices.Select((p, i) => Row(Start.AddSeconds(i), p, 1000, Side.Buy)).ToArray()) with
            { Profiles = ImmutableArray.Create(new ExplorerProfile { Layer = "Cloud2", SingleTicks = true }) };
            ExplorerRun disabled = V2Run(spec);
            using (ExplorerReplayCursor replay = new ExplorerReplayCursor(disabled, null, CancellationToken.None))
            {
                ExplorerFrame frame; do { frame = replay.Step(); } while (!frame.Complete);
                AssertEqual(0, frame.Triggers.Length, "Disabled study has no triggers"); AssertEqual(0, frame.Pivots.Length, "Disabled swing has no pivots");
                AssertEqual(0, frame.Observations.Length, "Disabled study has no observations");
            }
            ExplorerRun swings = ExplorerStudyRunner.Run(V2Run(spec with { Study = spec.Study with { SwingsEnabled = true } }), CancellationToken.None);
            AssertEqual(disabled.CatalogPath, swings.CatalogPath, "Cloud2-only catalog reused for swings-only mode");
            AssertTrue(ExplorerStorage.ReadRows<ExplorerPivot>(swings.StudyPath, "pivots").Any(), "Swings independently enabled");
            AssertTrue(!ExplorerStorage.ReadRows<ExplorerTrigger>(swings.StudyPath, "triggers").Any(), "Swings do not enable volume triggers");
        }
        private static void TestV2WatchTerminals(string root)
        {
            foreach (string mode in new[] { "Expired", "DateCutoff", "UnknownActivity", "UnknownVolumeBaseline", "Repeated" })
            {
                ExplorerRunSpec spec = new ExplorerRunSpec { InputSha256 = "fixture", Study = new ExplorerStudySpec { Enabled = true, AdaptSwing = false, SwingReversalTicks = 2,
                    WatchMinutes = 1, ActivityGate = mode == "UnknownActivity", RelativeTrigger = mode == "UnknownVolumeBaseline" } };
                ExplorerRawMetrics metrics = new ExplorerRawMetrics(spec); List<ExplorerObservation> observations = new List<ExplorerObservation>();
                List<ExplorerDiagnostic> diagnostics = new List<ExplorerDiagnostic>(); ExplorerStructure structure = new ExplorerStructure(spec, p => { }, observations.Add, diagnostics.Add);
                for (int i = 0; i < StructurePrices.Length; i++)
                {
                    DateTime time = Start.AddSeconds(i);
                    if (i == StructurePrices.Length - 1 && mode == "Expired") { time = Start.AddSeconds(7).AddMinutes(1); }
                    if (i == StructurePrices.Length - 1 && mode == "DateCutoff") { time = Start.AddDays(1); }
                    OrderFlowDeal tick = V2Tick(i + 1, StructurePrices[i], time: time); metrics.Before(tick);
                    ExplorerTrigger[] triggers = i == 8 || (mode == "Repeated" && i == 9) ? new[] { new ExplorerTrigger("trigger/" + i, "cloud/" + i, "Cloud1/Base", "Cloud", time, i + 1, 900, 1000, 1000, 0, tick.Price, tick.Price, tick.Price, tick.Price, 0) } : Array.Empty<ExplorerTrigger>();
                    structure.Add(tick, metrics, triggers, mode != "UnknownVolumeBaseline"); metrics.After(tick);
                }
                if (mode == "Repeated")
                { AssertEqual(9L, observations.Single(o => o.Status == "Breakout").Trigger.Sequence, "First trigger remains frozen"); AssertTrue(diagnostics.Any(d => d.Status == "AdditionalEvidence"), "Further trigger diagnostic"); }
                else
                {
                    AssertTrue(observations.Any(o => o.Status == mode), "Explicit terminal " + mode);
                    AssertTrue(!observations.Any(o => o.Status == "Breakout"), "Terminal cannot become breakout " + mode);
                    if (mode.StartsWith("Unknown", StringComparison.Ordinal)) { AssertTrue(observations.All(o => o.Group == "Excluded"), "Unknown is excluded from both groups"); }
                }
            }
        }
        private static void TestV2MidReadCancel(string root)
        {
            ExplorerRunSpec spec = V2Spec(root, Enumerable.Range(0, 8193).Select(i => Row(Start.AddSeconds(i), 100, 1, Side.Buy)).ToArray()) with
            { Profiles = ImmutableArray.Create(new ExplorerProfile { SingleTicks = true }), Episodes = new ExplorerEpisodeSpec { Enabled = true }, Study = new ExplorerStudySpec { Enabled = true } };
            foreach (string phase in new[] { "Catalog", "Episodes", "Study" })
            {
                ExplorerRun run = phase == "Catalog" ? null : V2Run(spec);
                if (phase == "Study") { run = ExplorerEpisodeRunner.Run(run, CancellationToken.None); }
                using CancellationTokenSource cancel = new CancellationTokenSource(); bool cancelled = false;
                Action<ExplorerProgress> progress = p => { if (p.Rows >= 8192) { cancel.Cancel(); } };
                try
                {
                    if (phase == "Catalog") { ExplorerRunner.Catalog(spec, cancel.Token, progress); }
                    else if (phase == "Episodes") { ExplorerEpisodeRunner.Run(run, cancel.Token, progress); }
                    else { ExplorerStudyRunner.Run(run, cancel.Token, progress); }
                }
                catch (OperationCanceledException) { cancelled = true; }
                AssertTrue(cancelled, "Mid-read cancellation " + phase);
                AssertTrue(!Directory.GetDirectories(spec.OutputRootPath).Any(p => Path.GetFileName(p).Contains("staging", StringComparison.Ordinal)), "Staging cleaned " + phase);
                using FileStream exclusive = new FileStream(spec.InputPath, FileMode.Open, FileAccess.Read, FileShare.None);
                if (run != null) { ExplorerStorage.Verify(run.CatalogPath, ExplorerRunSpec.CatalogVersion, run.Spec.CatalogSpecHash, CancellationToken.None); }
            }
            ExplorerRun replayRun = V2Run(spec);
            using (CancellationTokenSource cancel = new CancellationTokenSource())
            using (ExplorerReplayCursor replay = new ExplorerReplayCursor(replayRun, null, cancel.Token))
            {
                replay.Step(); cancel.Cancel(); bool cancelled = false;
                try { replay.Step(); } catch (OperationCanceledException) { cancelled = true; }
                AssertTrue(cancelled, "Replay cancellation after first tick");
            }
            using ManualResetEventSlim entered = new ManualResetEventSlim(); using ManualResetEventSlim release = new ManualResetEventSlim();
            using ExplorerJob job = new ExplorerJob(token => { entered.Set(); release.Wait(); return "late result"; });
            AssertTrue(entered.Wait(5000), "Worker entered"); job.Dispose(); release.Set();
            AssertTrue(SpinWait.SpinUntil(() => job.TryResult(out object ignored, out Exception error), 5000), "Disposed worker exits");
            job.TryResult(out object result, out Exception failure); AssertTrue(result == null && failure == null, "Disposed worker publishes no late result");
        }
        private static void TestV2Boundaries(string root)
        {
            ExplorerRunSpec spec = V2Spec(root, Row(Start, 100, 400, Side.Buy), Row(Start.AddSeconds(1), 105, 400, Side.Buy),
                Row(Start.AddSeconds(2).AddTicks(10), 200, 400, Side.Sell), Row(Start.AddDays(1), 200, 400, Side.Sell));
            ExplorerRun run = V2Run(spec); ExplorerCloud[] clouds = V2Clouds(run);
            AssertEqual(2, clouds[0].Count, "Gap and range equality admitted"); AssertEqual("Gap", clouds[0].Reason, "Gap wins simultaneous range break");
            AssertEqual("DateCutoff", clouds[1].Reason, "Date boundary explicit"); AssertEqual("OpenAtEnd", clouds[2].Reason, "EOF incomplete");
            ExplorerPrefix[] prefixes = ExplorerStorage.Prefixes(run.CatalogPath, run.Spec).ToArray();
            foreach (ExplorerCloud cloud in clouds) { AssertTrue(prefixes.Any(p => p.Id == cloud.Id && !p.Completed), "Stable prefix to final ID"); }
        }
        private static void TestV2EpisodeBoundaries(string root)
        {
            ExplorerRunSpec spec = V2Spec(root, Row(Start, 100, 400, Side.Buy), Row(Start.AddSeconds(1), 102, 400, Side.Buy),
                Row(Start.AddSeconds(2), 104, 400, Side.Buy), Row(Start.AddSeconds(3), 105, 400, Side.Buy)) with
            { Profiles = ImmutableArray.Create(new ExplorerProfile { SingleTicks = true }), Episodes = new ExplorerEpisodeSpec { Enabled = true, MaximumDurationSeconds = 2, MaximumZoneTicks = 4 },
                Study = new ExplorerStudySpec { Enabled = true, EpisodeTrigger = true } };
            ExplorerRun run = ExplorerStudyRunner.Run(ExplorerEpisodeRunner.Run(V2Run(spec), CancellationToken.None), CancellationToken.None);
            ExplorerEpisode first = ExplorerStorage.ReadRows<ExplorerEpisode>(run.EpisodePath, "episodes").First();
            AssertEqual(3, first.ChildCount, "Duration and zone equality admitted");
            ExplorerMembership member = ExplorerStorage.FindOrdered<ExplorerMembership>(run.EpisodePath, "membership", 2, m => m.FirstSequence);
            AssertEqual(first.Id, member.EpisodeId, "Indexed membership"); AssertEqual(3, member.ChildCount, "Final child count");
            ExplorerTrigger trigger = ExplorerStorage.ReadRows<ExplorerTrigger>(run.StudyPath, "triggers").Single();
            AssertEqual(3, trigger.Children, "Threshold crossing freezes child prefix");
            ExplorerView view = ExplorerStorage.ResolveRelations(run, new ExplorerView { RelatedId = first.Id, MinimumVolume = null }, CancellationToken.None);
            AssertEqual(3, V2Clouds(run).Count(c => view.Passes(c, 1)), "Related episode filter matches children only");
        }
        private static void TestV2Identity(string root)
        {
            ExplorerRun run = V2Run(V2Spec(root, BasicTicks())); ExplorerRunSpec spec = run.Spec;
            Dictionary<string, string> before = Directory.GetFiles(run.CatalogPath).ToDictionary(Path.GetFileName, f => ExplorerStorage.HashFile(f, CancellationToken.None));
            ExplorerRunSpec downstream = spec with { Episodes = new ExplorerEpisodeSpec { Enabled = true }, Study = new ExplorerStudySpec { Enabled = true } };
            ExplorerRun study = ExplorerStudyRunner.Run(ExplorerEpisodeRunner.Run(V2Run(downstream), CancellationToken.None), CancellationToken.None);
            foreach (KeyValuePair<string, string> file in before) { AssertEqual(file.Value, ExplorerStorage.HashFile(Path.Combine(run.CatalogPath, file.Key), CancellationToken.None), "Byte immutable catalog " + file.Key); }
            AssertTrue(spec.CatalogSpecHash != (spec with { PriceStep = 2 }).CatalogSpecHash, "Step identity");
            AssertTrue(spec.CatalogSpecHash != (spec with { FromDate = Start.Date, ToDate = Start.Date }).CatalogSpecHash, "Date identity");
            AssertTrue(spec.CatalogSpecHash != (spec with { InputSha256 = "changed" }).CatalogSpecHash, "Source identity");
            AssertTrue(spec.CatalogSpecHash != (spec with { Profiles = ImmutableArray.Create(spec.Profiles[0] with { MaximumRangeTicks = 6 }) }).CatalogSpecHash, "Formation identity");
            ExplorerRunSpec changedEpisodes = downstream with { Episodes = downstream.Episodes with { MaximumZoneTicks = 20 } };
            AssertEqual(downstream.StudyHash, changedEpisodes.StudyHash, "Cloud study independent of episodes");
            AssertTrue((downstream with { Study = downstream.Study with { EpisodeTrigger = true } }).StudyHash !=
                (changedEpisodes with { Study = changedEpisodes.Study with { EpisodeTrigger = true } }).StudyHash, "Episode study depends on episode rules");
            ExplorerStorage.SaveView(study, new ExplorerView { MinimumVolume = 300 }); AssertEqual(300m, ExplorerStorage.LoadView(study).MinimumVolume.Value, "Separate saved display profile");
        }
        private static void TestV2Sampling(string root)
        {
            ExplorerRunSpec spec = new ExplorerRunSpec { Study = new ExplorerStudySpec { HorizonsMinutes = ImmutableArray.Create(1) } };
            List<ExplorerLabel> output = new List<ExplorerLabel>(); ExplorerLabels labels = new ExplorerLabels(spec, new[] { Start.Date, Start.Date.AddDays(1), Start.Date.AddDays(2) }, output.Add);
            labels.Add(LabelObservation(Start)); labels.Add(LabelObservation(Start.AddSeconds(30), 2) with { Group = "VolumeArmed" });
            labels.Tick(V2Tick(3, 101, time: Start.AddMinutes(1))); labels.Tick(V2Tick(4, 101, time: Start.AddMinutes(2)));
            AssertTrue(output.Any(l => l.Selection == "OverlapExcluded" && l.Group == "VolumeArmed"), "Common non-overlap sequence before groups");
            AssertTrue(output.All(l => l.Outcome == "Timeout" && l.SignedReturn == 1 && l.MfeAtr == .5m), "Price return versus ATR excursion units");
            labels.Add(LabelObservation(Start.AddDays(2), 10) with { HistoryStart = Start.AddDays(1) });
            labels.Tick(V2Tick(11, 100, time: Start.AddDays(2).AddMinutes(2)));
            AssertEqual("Test", output.Last().Split, "Chronological 70 percent split"); AssertEqual("Purged", output.Last().Selection, "Test prehistory purge");
            AssertEqual("NoFutureTrade", output.Last().Outcome, "No future tick within full horizon");
            labels.Add(LabelObservation(Start.AddDays(2).AddHours(1), 20) with { Atr = null });
            AssertEqual("NoAtrLabel", output.Last().Outcome, "Unknown ATR remains unlabelled");
        }
        private static void TestV2DisplayBars(string root)
        {
            ExplorerBar[] raw = Enumerable.Range(0, 9000).Select(i => new ExplorerBar(Start.AddSeconds(i * 15), Start.AddSeconds((i + 1) * 15), i + 1, i + 2, i + 1, i + 2, 1)).ToArray();
            IReadOnlyList<ExplorerBar> display = ExplorerBars.Aggregate(raw, OrderFlowDisplayTimeFrame.Sec15, CancellationToken.None);
            AssertTrue(display.Count <= 4000, "Bounded display compaction"); AssertEqual(9000m, display.Sum(b => b.Volume), "Compaction preserves volume");
            AssertEqual(raw[0].Open, display[0].Open, "First open preserved"); AssertEqual(raw.Last().Close, display.Last().Close, "Last close preserved");
        }
        private static void TestV2DateBaseline(string root)
        {
            ExplorerRunSpec spec = V2Spec(root, Row(Start, 100, 1, Side.Buy), Row(Start.AddSeconds(2), 100, 2, Side.Buy),
                Row(Start.AddSeconds(3), 100, 1000, Side.Buy), Row(Start.AddDays(1), 100, 5, Side.Buy), Row(Start.AddDays(1).AddSeconds(2), 100, 5, Side.Buy)) with
            { Profiles = ImmutableArray.Create(new ExplorerProfile { TimeOfDayVolume = true, VolumeMinimum = 1, VolumeWindow = 2 }) };
            ExplorerCloud[] clouds = V2Clouds(V2Run(spec));
            AssertEqual(1m, clouds[2].Effective.TimeOfDayThreshold.Value, "Same-ordinal date-cutoff cloud excluded");
            AssertEqual(1002m, clouds[3].Effective.TimeOfDayThreshold.Value, "Prior-date completed cloud available on later ordinal");
            ExplorerVolumeBaseline baseline = new ExplorerVolumeBaseline(spec.Profiles[0], 1000);
            baseline.Advance(1); baseline.Add(BaselineCloud(1, 1, Start), 1); baseline.Advance(2);
            baseline.Add(BaselineCloud(2, 1002, Start.AddSeconds(2)) with { KnownSequence = 4 }, 1);
            AssertEqual(1m, baseline.TimeOfDay(Start.AddDays(1), 4).Value, "Study advance-before-completion ordering excludes tie");
            AssertEqual(1002m, baseline.TimeOfDay(Start.AddDays(1), 5).Value, "Late previous-date admission visible without extra date lag");
        }
        private sealed class ExplorerFailingStream : MemoryStream
        {
            internal bool WasDisposed;
            internal bool Fail;
            protected override void Dispose(bool disposing)
            {
                WasDisposed = true; base.Dispose(disposing);
                if (Fail) { throw new IOException("Injected close failure"); }
            }
        }
        private static void TestV2WriterFailures(string root)
        {
            Directory.CreateDirectory(Path.Combine(root, "rows.idx")); bool failed = false;
            try { using ExplorerStorage.RowWriter<int> writer = new ExplorerStorage.RowWriter<int>(root, "rows"); }
            catch (UnauthorizedAccessException) { failed = true; }
            catch (IOException) { failed = true; }
            AssertTrue(failed, "Second open fails deterministically");
            using (FileStream exclusive = new FileStream(Path.Combine(root, "rows.bin"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            { AssertTrue(exclusive.CanWrite, "First handle rolled back immediately"); }
            foreach (bool both in new[] { false, true })
            {
                ExplorerFailingStream data = new ExplorerFailingStream { Fail = true }, index = new ExplorerFailingStream { Fail = both };
                ExplorerStorage.RowWriter<int> writer = new ExplorerStorage.RowWriter<int>(new BinaryWriter(data), new BinaryWriter(index));
                Exception failure = null; try { writer.Dispose(); } catch (Exception error) { failure = error; }
                AssertTrue(data.WasDisposed && index.WasDisposed, "Both closes attempted despite first failure");
                if (both) { AssertEqual(2, ((AggregateException)failure).InnerExceptions.Count, "Both failure reasons retained"); }
                else { AssertTrue(failure is IOException && failure.Message == "Injected close failure", "Primary I/O error retained"); }
            }
        }
        private static void TestV2RelativeStudy(string root)
        {
            ExplorerRunSpec spec = V2Spec(root, StructurePrices.Select((p, i) => Row(Start.AddSeconds(i), p, i == 8 ? 1000 : 10, Side.Buy)).ToArray()) with
            { Profiles = ImmutableArray.Create(new ExplorerProfile { SingleTicks = true, VolumeMinimum = 1 }),
                Study = new ExplorerStudySpec { Enabled = true, RelativeTrigger = true, AdaptSwing = false, SwingReversalTicks = 2 } };
            bool rejected = false; try { V2Run(spec); } catch (ArgumentException error) { rejected = error.Message.Contains("Относительный объём", StringComparison.Ordinal); }
            AssertTrue(rejected, "Incompatible hypothesis rejected with actionable reason");
            AssertTrue(!Directory.Exists(spec.OutputRootPath), "Rejected before any bundle is created");
            ExplorerRun run = ExplorerStudyRunner.Run(V2Run(spec with { Profiles = ImmutableArray.Create(spec.Profiles[0] with { RelativeVolume = true }) }), CancellationToken.None);
            ExplorerTrigger trigger = ExplorerStorage.ReadRows<ExplorerTrigger>(run.StudyPath, "triggers").Single(t => t.Sequence == 9);
            AssertEqual(10m, trigger.Threshold, "Frozen causal baseline used"); AssertEqual(1000m, trigger.Volume, "Relative trigger emitted");
            AssertEqual("VolumeArmed", ExplorerStorage.ReadRows<ExplorerObservation>(run.StudyPath, "observations").Single(o => o.Status == "Breakout").Group, "Relative study can arm watch");
            (spec with { Episodes = new ExplorerEpisodeSpec { Enabled = true }, Study = spec.Study with { EpisodeTrigger = true } }).Validate();
        }
        private static void TestV2AnchorTransition(string root)
        {
            ExplorerRun a = ExplorerEpisodeRunner.Run(V2Run(V2Spec(root, Row(Start, 100, 400, Side.Buy)) with
            { Profiles = ImmutableArray.Create(new ExplorerProfile { SingleTicks = true }), Episodes = new ExplorerEpisodeSpec { Enabled = true } }), CancellationToken.None);
            ExplorerEpisode episode = ExplorerStorage.ReadRows<ExplorerEpisode>(a.EpisodePath, "episodes").Single();
            ExplorerRun b = V2Run(V2Spec(root, Row(Start, 200, 400, Side.Buy)));
            using CloudExplorerControl control = new CloudExplorerControl();
            MethodInfo install = typeof(CloudExplorerControl).GetMethod("InstallRun", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo selection = typeof(CloudExplorerControl).GetField("_selectedAnchor", BindingFlags.Instance | BindingFlags.NonPublic);
            install.Invoke(control, new object[] { a }); control.DataGridEpisodes.ItemsSource = new[] { episode }; control.DataGridEpisodes.SelectedItem = episode;
            ExplorerAnchor old = (ExplorerAnchor)selection.GetValue(control); AssertEqual(episode.Id, old.Id, "Actual selection handler stores anchor");
            control.DataGridEpisodes.SelectedItem = null; AssertTrue(selection.GetValue(control) == null, "Deselection clears pending anchor");
            control.DataGridEpisodes.SelectedItem = episode; install.Invoke(control, new object[] { b });
            AssertTrue(selection.GetValue(control) == null, "New run atomically clears old selected anchor");
            bool rejected = false; try { ExplorerAnchorRunner.Run(b, old, CancellationToken.None); } catch (InvalidDataException) { rejected = true; }
            AssertTrue(rejected, "Foreign anchor rejected before raw read");
            rejected = false; try { using ExplorerReplayCursor replay = new ExplorerReplayCursor(b, old, CancellationToken.None); } catch (InvalidDataException) { rejected = true; }
            AssertTrue(rejected, "Foreign anchor cannot enter replay");
            ExplorerCloud cloud = V2Clouds(b).Single();
            ExplorerAnchor valid = new ExplorerAnchor(cloud.Id, cloud.FirstSequence, long.MaxValue, cloud.StartTime.Date);
            AssertEqual(200m, ExplorerAnchorRunner.Run(b, valid, CancellationToken.None).Single().Vwap, "Current run explicit EOF anchor remains supported");
        }
    }
}
