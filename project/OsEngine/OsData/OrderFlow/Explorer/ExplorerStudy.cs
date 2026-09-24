/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace OsEngine.OsData.OrderFlow.Explorer
{
    /// <summary>Future market-path label, stored separately from causal observations. Returns are price units; excursions are ATR units.</summary>
    internal sealed record ExplorerLabel(string ObservationId, DateTime BreakoutTime, long BreakoutSequence, DateTime KnownAt, long KnownSequence,
        string Group, string Split, int Hour, decimal? Atr, int HorizonMinutes, string Outcome, string FirstHit, string Selection,
        decimal? SignedReturn, decimal? MfeAtr, decimal? MaeAtr);

    /// <summary>Source-ordered future horizons with no overnight continuation. EOF alone cannot certify an unfinished horizon.</summary>
    internal sealed class ExplorerLabels
    {
        private sealed class Pending
        {
            internal ExplorerObservation Observation;
            internal int Horizon;
            internal DateTime End;
            internal string Split, Selection, Hit;
            internal bool Any;
            internal decimal Last, Best, Worst;
        }
        private readonly ExplorerRunSpec _spec;
        private readonly Action<ExplorerLabel> _output;
        private readonly List<Pending> _pending = new List<Pending>();
        private DateTime _nextAllowed = DateTime.MinValue;
        private readonly DateTime? _testDate;
        internal ExplorerLabels(ExplorerRunSpec spec, DateTime[] dates, Action<ExplorerLabel> output)
        {
            _spec = spec; _output = output;
            if (dates.Length > 1) { _testDate = dates[Math.Clamp((int)Math.Floor(.7m * dates.Length), 1, dates.Length - 1)]; }
        }
        #region Pending horizons

        internal void Add(ExplorerObservation observation)
        {
            if (observation.Status != "Breakout") { return; }
            string selection = observation.Time <= _nextAllowed ? "OverlapExcluded" : "Included";
            if (selection == "Included") { _nextAllowed = observation.Time.AddMinutes(_spec.Study.HorizonsMinutes.Max()); }
            string split = !_testDate.HasValue ? "Fit/NoTest" : observation.Time.Date < _testDate ? "Fit" : "Test";
            if (_testDate.HasValue && ((split == "Test" && observation.HistoryStart.Date < _testDate) ||
                (split == "Fit" && observation.Time.AddMinutes(_spec.Study.HorizonsMinutes.Max()) >= _testDate))) { selection = "Purged"; }
            foreach (int horizon in _spec.Study.HorizonsMinutes.OrderBy(v => v))
            {
                Pending pending = new Pending { Observation = observation, Horizon = horizon, End = observation.Time.AddMinutes(horizon), Split = split, Selection = selection };
                if (!observation.Atr.HasValue || observation.Atr <= 0) { Emit(pending, observation.Time, observation.Sequence, "NoAtrLabel"); }
                else if (_spec.Study.TargetAtr * observation.Atr <= 0 || _spec.Study.AdverseAtr * observation.Atr <= 0)
                { throw new InvalidDataException("Explorer ATR label barrier is not representable as a positive decimal."); }
                else if (pending.End.Date != observation.Time.Date) { Emit(pending, observation.Time, observation.Sequence, "Incomplete/DateCutoff"); }
                else
                {
                    if (_pending.Count >= _spec.MaximumBufferItems) { throw new InvalidDataException("Explorer future label buffer limit exceeded."); }
                    _pending.Add(pending);
                }
            }
        }
        internal void Tick(OrderFlowDeal tick)
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                Pending pending = _pending[i]; ExplorerObservation observation = pending.Observation;
                if (tick.SourceSequence <= observation.Sequence) { continue; }
                if (tick.Time.Date != observation.Time.Date || tick.Time > pending.End)
                { Finish(i, tick.Time, tick.SourceSequence, true); continue; }
                decimal signed = (observation.Direction == "Long" ? 1 : -1) * (tick.Price - observation.Price);
                pending.Any = true; pending.Last = signed; pending.Best = Math.Max(pending.Best, signed); pending.Worst = Math.Max(pending.Worst, -signed);
                if (pending.Hit == null)
                {
                    if (signed >= _spec.Study.TargetAtr * observation.Atr.Value) { pending.Hit = "TargetFirst"; }
                    else if (signed <= -_spec.Study.AdverseAtr * observation.Atr.Value) { pending.Hit = "AdverseFirst"; }
                }
                // Keep equal-time rows until a later timestamp or EOF; source ties are still future observations.
            }
        }
        internal void Complete(OrderFlowDeal last)
        {
            if (last == null) { return; }
            for (int i = _pending.Count - 1; i >= 0; i--) { Finish(i, last.Time, last.SourceSequence, last.Time >= _pending[i].End); }
        }
        #endregion

        #region Future outcome publication

        private void Finish(int index, DateTime time, long sequence, bool complete)
        {
            Pending pending = _pending[index];
            Emit(pending, time, sequence, !complete ? "Incomplete" : pending.Hit ?? (pending.Any ? "Timeout" : "NoFutureTrade"));
            _pending.RemoveAt(index);
        }
        private void Emit(Pending pending, DateTime time, long sequence, string outcome)
        {
            ExplorerObservation o = pending.Observation;
            _output(new ExplorerLabel(o.Id, o.Time, o.Sequence, time, sequence, o.Group, pending.Split, o.Time.Hour, o.Atr,
                pending.Horizon, outcome, pending.Hit, pending.Selection, pending.Any ? pending.Last : null,
                pending.Any ? pending.Best / o.Atr.Value : null, pending.Any ? pending.Worst / o.Atr.Value : null));
        }
        #endregion
    }

    /// <summary>Shared study/replay kernel consuming catalog prefixes and raw ticks. All published entities are immutable snapshots.</summary>
    internal sealed class ExplorerStudyEngine
    {
        private readonly ExplorerRunSpec _spec;
        private readonly ExplorerRawMetrics _metrics;
        private readonly ExplorerStructure _structure;
        private readonly ExplorerLabels _labels;
        private readonly Action<ExplorerTrigger> _trigger;
        private string _cloudId, _episodeId;
        private bool _cloudTriggered, _episodeTriggered;
        private readonly List<ExplorerTrigger> _tickTriggers = new List<ExplorerTrigger>();
        private OrderFlowDeal _last;
        private readonly string _hash;
        internal ExplorerPivot Provisional => _structure.Provisional;
        internal IReadOnlyList<ExplorerWatch> Watches => _structure.Active;
        internal ExplorerRawMetrics Metrics => _metrics;
        internal ExplorerStudyEngine(ExplorerRunSpec spec, DateTime[] dates, Action<ExplorerTrigger> trigger, Action<ExplorerPivot> pivot,
            Action<ExplorerObservation> observation, Action<ExplorerLabel> label, Action<ExplorerDiagnostic> diagnostic, Action<ExplorerWatchVwap> vwapSample = null)
        {
            _spec = spec; _hash = spec.StudyHash; _metrics = new ExplorerRawMetrics(spec); _trigger = trigger;
            _labels = new ExplorerLabels(spec, dates, label);
            _structure = new ExplorerStructure(spec, pivot, o => { observation(o); _labels.Add(o); }, diagnostic, vwapSample);
        }
        internal void Prefix(ExplorerPrefix prefix)
        {
            if (!_spec.Study.Enabled || prefix.Profile != _spec.Study.Profile || prefix.Completed || _spec.Study.EpisodeTrigger) { return; }
            if (_cloudId != prefix.Id) { _cloudId = prefix.Id; _cloudTriggered = false; }
            decimal? threshold = _spec.Study.RelativeTrigger ? prefix.RelativeThreshold : _spec.Study.TriggerVolume;
            decimal volume = OrderFlowVolumeComparison.AddExact(prefix.Buy, prefix.Sell);
            if (_cloudTriggered || !threshold.HasValue || volume < threshold) { return; }
            _cloudTriggered = true;
            ExplorerTrigger trigger = new ExplorerTrigger(_hash + "/trigger/" + prefix.FirstSequence, prefix.Id, prefix.Profile, "Cloud", prefix.Time,
                prefix.Sequence, threshold.Value, volume, prefix.Buy, prefix.Sell, prefix.Price, prefix.Low, prefix.High, prefix.Vwap, 0);
            _trigger(trigger); _tickTriggers.Add(trigger);
        }
        internal void Episode(ExplorerEpisodePrefix prefix)
        {
            if (!_spec.Study.Enabled || !_spec.Study.EpisodeTrigger || prefix.Completed) { return; }
            if (_episodeId != prefix.Id) { _episodeId = prefix.Id; _episodeTriggered = false; }
            decimal? threshold = _spec.Study.RelativeTrigger ? prefix.Threshold : _spec.Study.TriggerVolume;
            decimal volume = OrderFlowVolumeComparison.AddExact(prefix.Buy, prefix.Sell);
            if (_episodeTriggered || !threshold.HasValue || volume < threshold) { return; }
            _episodeTriggered = true;
            ExplorerTrigger trigger = new ExplorerTrigger(_hash + "/episode-trigger/" + prefix.FirstSequence, prefix.Id, _spec.Study.Profile, "Episode",
                prefix.Time, prefix.Sequence, threshold.Value, volume, prefix.Buy, prefix.Sell, prefix.Price, prefix.Low, prefix.High, 0, prefix.ChildCount);
            _trigger(trigger); _tickTriggers.Add(trigger);
        }
        internal void Tick(OrderFlowDeal tick, bool relativeKnown)
        {
            _metrics.Before(tick); _labels.Tick(tick);
            if (_spec.Study.SwingsEnabled || _spec.Study.Enabled) { _structure.Add(tick, _metrics, _tickTriggers, relativeKnown); }
            _tickTriggers.Clear(); _metrics.After(tick); _last = tick;
        }
        internal void Complete() { _structure.Complete(_last, _metrics.Atr); _labels.Complete(_last); }
    }

    /// <summary>Separate structure study built from the saved prefix index, without reconstructing or changing the catalog.</summary>
    internal static class ExplorerStudyRunner
    {
        internal static ExplorerRun Run(ExplorerRun run, CancellationToken cancellation, Action<ExplorerProgress> progress = null)
        {
            ExplorerRunSpec spec = run.Spec;
            if (spec.StudyHash == null) { return run; }
            string destination = Path.Combine(spec.OutputRootPath, "cloud-structure-study-" + spec.StudyHash);
            if (Directory.Exists(destination))
            { ExplorerStorage.Verify(destination, ExplorerRunSpec.StudyVersion, spec.StudyHash, cancellation); return run with { StudyPath = destination }; }
            string staging = ExplorerStorage.Stage(spec.OutputRootPath, "cloud-structure-study");
            try
            {
                OrderFlowTickInput metadata = new OrderFlowTickInput(); Stopwatch watch = Stopwatch.StartNew(); long selected = 0, observationsCount = 0;
                using (OrderFlowTickReader reader = new OrderFlowTickReader(spec.InputPath, metadata, cancellation))
                using (IEnumerator<ExplorerPrefix> prefixes = ExplorerStorage.Prefixes(run.CatalogPath, spec).GetEnumerator())
                using (IEnumerator<ExplorerCloud> clouds = ExplorerStorage.ReadRows<ExplorerCloud>(run.CatalogPath, "catalog").GetEnumerator())
                using (IEnumerator<ExplorerEpisodePrefix> episodes = (run.EpisodePath == null ? Enumerable.Empty<ExplorerEpisodePrefix>() :
                    ExplorerStorage.ReadRows<ExplorerEpisodePrefix>(run.EpisodePath, "episode-prefix")).GetEnumerator())
                using (IEnumerator<ExplorerEpisode> finishedEpisodes = (run.EpisodePath == null ? Enumerable.Empty<ExplorerEpisode>() :
                    ExplorerStorage.ReadRows<ExplorerEpisode>(run.EpisodePath, "episodes")).GetEnumerator())
                using (ExplorerStorage.RowWriter<ExplorerTrigger> triggers = new ExplorerStorage.RowWriter<ExplorerTrigger>(staging, "triggers"))
                using (ExplorerStorage.RowWriter<ExplorerPivot> pivots = new ExplorerStorage.RowWriter<ExplorerPivot>(staging, "pivots"))
                using (ExplorerStorage.RowWriter<ExplorerObservation> observations = new ExplorerStorage.RowWriter<ExplorerObservation>(staging, "observations"))
                using (ExplorerStorage.RowWriter<ExplorerLabel> labels = new ExplorerStorage.RowWriter<ExplorerLabel>(staging, "future-labels"))
                using (ExplorerStorage.RowWriter<ExplorerDiagnostic> diagnostics = new ExplorerStorage.RowWriter<ExplorerDiagnostic>(staging, "diagnostics"))
                using (ExplorerStorage.RowWriter<ExplorerWatchVwap> vwap = new ExplorerStorage.RowWriter<ExplorerWatchVwap>(staging, "watch-vwap-samples"))
                {
                    if (metadata.Sha256 != spec.InputSha256) { throw new InvalidDataException("Explorer source SHA-256 changed."); }
                    ExplorerStudyEngine engine = new ExplorerStudyEngine(spec, run.Catalog.Dates, triggers.Add, pivots.Add,
                        observation => { observations.Add(observation); observationsCount++; }, labels.Add, diagnostics.Add, vwap.Add);
                    ExplorerVolumeBaseline baseline = new ExplorerVolumeBaseline(spec.StudyProfile, spec.MaximumBufferItems);
                    ExplorerVolumeBaseline episodeBaseline = new ExplorerVolumeBaseline(spec.StudyProfile with { TimeOfDayVolume = false }, spec.MaximumBufferItems);
                    bool havePrefix = prefixes.MoveNext(), haveEpisode = episodes.MoveNext(), haveCloud = clouds.MoveNext();
                    bool haveFinishedEpisode = finishedEpisodes.MoveNext();
                    int dateOrdinal = 0; DateTime date = default;
                    while (reader.TryRead(out OrderFlowDeal tick))
                    {
                        if (!spec.Includes(tick.Time)) { continue; }
                        if (date != tick.Time.Date) { date = tick.Time.Date; dateOrdinal++; }
                        baseline.Advance(dateOrdinal);
                        episodeBaseline.Advance(dateOrdinal);
                        while (haveFinishedEpisode && finishedEpisodes.Current.KnownSequence <= tick.SourceSequence)
                        {
                            episodeBaseline.Add(finishedEpisodes.Current.Baseline(), finishedEpisodes.Current.StartTime.Date == date ? dateOrdinal : dateOrdinal - 1);
                            haveFinishedEpisode = finishedEpisodes.MoveNext();
                        }
                        while (haveCloud && clouds.Current.KnownSequence.HasValue && clouds.Current.KnownSequence <= tick.SourceSequence)
                        {
                            if (clouds.Current.Profile == spec.Study.Profile) { baseline.Add(clouds.Current, clouds.Current.StartTime.Date == date ? dateOrdinal : dateOrdinal - 1); }
                            haveCloud = clouds.MoveNext();
                        }
                        bool relativeKnown = spec.StudyProfile.TimeOfDayVolume ? baseline.TimeOfDay(tick.Time, tick.SourceSequence).HasValue : baseline.Rolling(tick.SourceSequence).HasValue;
                        while (havePrefix && prefixes.Current.Sequence <= tick.SourceSequence)
                        { engine.Prefix(prefixes.Current); havePrefix = prefixes.MoveNext(); }
                        while (haveEpisode && episodes.Current.Sequence <= tick.SourceSequence)
                        { engine.Episode(episodes.Current); haveEpisode = episodes.MoveNext(); }
                        engine.Tick(tick, spec.Study.EpisodeTrigger ? episodeBaseline.Rolling(tick.SourceSequence).HasValue : relativeKnown); selected++;
                        if (selected % 8192 == 0) { ExplorerStorage.CheckMemory(spec); progress?.Invoke(new ExplorerProgress("Study", selected, observationsCount, date, GC.GetTotalMemory(false), watch.Elapsed.TotalSeconds)); }
                    }
                    engine.Complete();
                }
                WriteComparison(staging, spec, run.Catalog.Dates, cancellation);
                File.WriteAllText(Path.Combine(staging, "run-spec.json"), JsonSerializer.Serialize(spec with { InputPath = "", OutputRootPath = "" }, ExplorerStorage.Json), new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(staging, "README.txt"), "Offline source-ordered structure research. Observations are causal; future-labels are separate. No fills, orders, costs or PnL. Fit-only ATR tertiles; common non-overlap sampling. Unknown/Incomplete are not successes. Changing a hypothesis after Test requires a new untouched interval.", new UTF8Encoding(false));
                ExplorerManifest manifest = new ExplorerManifest(ExplorerRunSpec.StudyVersion, spec.StudyHash, spec.InputSha256,
                    JsonSerializer.SerializeToElement(new { spec.CatalogSpecHash, spec.EpisodeSpecHash, spec.Study }), metadata.RecordCount, selected,
                    observationsCount, run.Catalog.SecondsOnly, run.Catalog.Dates, null);
                ExplorerStorage.Publish(staging, destination, manifest, cancellation); staging = null;
                return run with { StudyPath = destination };
            }
            finally { if (staging != null && Directory.Exists(staging)) { Directory.Delete(staging, true); } }
        }

        private static void WriteComparison(string directory, ExplorerRunSpec spec, DateTime[] dates, CancellationToken cancellation)
        {
            ExplorerDistribution fit = new ExplorerDistribution();
            foreach (ExplorerLabel label in ExplorerStorage.ReadRows<ExplorerLabel>(directory, "future-labels"))
            { cancellation.ThrowIfCancellationRequested(); if (label.HorizonMinutes == spec.Study.HorizonsMinutes.Min() && label.Split.StartsWith("Fit", StringComparison.Ordinal) && label.Selection == "Included" && label.Atr > 0) { fit.Add(label.Atr.Value); } }
            decimal? low = fit.Count == 0 ? null : fit.Select((fit.Count + 2) / 3 - 1), high = fit.Count == 0 ? null : fit.Select((2 * fit.Count + 2) / 3 - 1);
            Dictionary<string, long[]> cells = new Dictionary<string, long[]>();
            using (StreamWriter csv = new StreamWriter(Path.Combine(directory, "future-labels.csv"), false, new UTF8Encoding(false)))
            {
                csv.WriteLine("ObservationId,KnownAt,KnownSequence,Group,Split,Hour,Atr,HorizonMinutes,Outcome,FirstHit,Selection,SignedReturn,MfeAtr,MaeAtr");
                foreach (ExplorerLabel label in ExplorerStorage.ReadRows<ExplorerLabel>(directory, "future-labels"))
                {
                    cancellation.ThrowIfCancellationRequested();
                    csv.WriteLine(string.Join(",", label.ObservationId, label.KnownAt.ToString("O", CultureInfo.InvariantCulture), label.KnownSequence, label.Group,
                        label.Split, label.Hour, N(label.Atr), label.HorizonMinutes, label.Outcome, label.FirstHit, label.Selection, N(label.SignedReturn), N(label.MfeAtr), N(label.MaeAtr)));
                    string band = !label.Atr.HasValue || !low.HasValue ? "Unknown" : label.Atr <= low ? "Low" : label.Atr <= high ? "Middle" : "High";
                    string key = string.Join(",", label.Split, label.Group, label.Hour, band, label.HorizonMinutes);
                    if (!cells.TryGetValue(key, out long[] counts)) { counts = new long[6]; cells.Add(key, counts); }
                    counts[0]++; if (label.Selection != "Included") { counts[5]++; continue; }
                    if (label.Outcome == "TargetFirst") { counts[1]++; }
                    else if (label.Outcome == "AdverseFirst") { counts[2]++; }
                    else if (label.Outcome.StartsWith("Incomplete", StringComparison.Ordinal)) { counts[3]++; }
                    else if (label.Outcome == "NoAtrLabel" || label.Outcome == "NoFutureTrade") { counts[4]++; }
                }
            }
            using (StreamWriter csv = new StreamWriter(Path.Combine(directory, "comparison.csv"), false, new UTF8Encoding(false)))
            {
                csv.WriteLine("Split,Group,Hour,AtrBand,HorizonMinutes,Total,TargetFirst,AdverseFirst,Incomplete,Unknown,Excluded,IncompleteFraction,UnknownFraction,Status");
                foreach (string split in new[] { "Fit", "Fit/NoTest", "Test" })
                { foreach (string group in new[] { "VolumeArmed", "Control" })
                { for (int hour = 0; hour < 24; hour++)
                { foreach (string band in new[] { "Low", "Middle", "High", "Unknown" })
                { foreach (int horizon in spec.Study.HorizonsMinutes)
                {
                    string key = string.Join(",", split, group, hour, band, horizon); cells.TryGetValue(key, out long[] counts); counts ??= new long[6];
                    csv.WriteLine(key + "," + string.Join(",", counts) + "," + N(counts[0] == 0 ? null : (decimal)counts[3] / counts[0]) + "," + N(counts[0] == 0 ? null : (decimal)counts[4] / counts[0]) + "," + (counts[0] == 0 ? "InsufficientSample" : "DescriptiveOnly"));
                } } } } }
            }
            Dictionary<string, (long Total, long Breakout)> watches = new Dictionary<string, (long, long)>();
            DateTime? testDate = dates.Length > 1 ? dates[Math.Clamp((int)Math.Floor(.7m * dates.Length), 1, dates.Length - 1)] : null;
            foreach (ExplorerObservation observation in ExplorerStorage.ReadRows<ExplorerObservation>(directory, "observations"))
            {
                cancellation.ThrowIfCancellationRequested(); string split = !testDate.HasValue ? "Fit/NoTest" : observation.WatchStart.Date < testDate ? "Fit" : "Test";
                string key = split + "/" + observation.Group; watches.TryGetValue(key, out (long Total, long Breakout) counts);
                watches[key] = (counts.Total + 1, counts.Breakout + (observation.Status == "Breakout" ? 1 : 0));
            }
            File.WriteAllText(Path.Combine(directory, "study-summary.json"), JsonSerializer.Serialize(new { FitLowAtr = low, FitHighAtr = high,
                Watches = watches.Select(p => new { Group = p.Key, Terminal = p.Value.Total, Breakout = p.Value.Breakout,
                    BreakoutFraction = p.Value.Total == 0 ? 0 : (decimal)p.Value.Breakout / p.Value.Total }) }, ExplorerStorage.Json));
        }
        private static string N(decimal? value) => value?.ToString("G29", CultureInfo.InvariantCulture) ?? "";
    }
}
