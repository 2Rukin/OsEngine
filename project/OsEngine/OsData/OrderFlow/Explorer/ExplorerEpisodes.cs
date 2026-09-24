/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace OsEngine.OsData.OrderFlow.Explorer
{
    /// <summary>Immutable episode prefix/final evidence; raw interval volume includes trades excluded from child Clouds.</summary>
    internal sealed record ExplorerEpisode(string Id, string Profile, long FirstSequence, long LastSequence, DateTime StartTime,
        DateTime Time, DateTime KnownAt, long KnownSequence, decimal Low, decimal High, decimal Buy, decimal Sell,
        decimal RawVolume, int ZoneTicks, decimal? RelativeThreshold, ImmutableArray<string> ChildIds, string Reason)
    {
        public decimal Volume => OrderFlowVolumeComparison.AddExact(Buy, Sell);
        public int ChildCount => ChildIds.Length;
        internal ExplorerCloud Baseline() => new ExplorerCloud { Id = Id, Profile = Profile, FirstSequence = FirstSequence, LastSequence = LastSequence,
            StartTime = StartTime, Time = Time, KnownAt = KnownAt, KnownSequence = KnownSequence, Buy = Buy, Sell = Sell,
            Reason = Reason == "EpisodeOpenAtEnd" ? "OpenAtEnd" : Reason };
    }
    /// <summary>One completed child admission; earlier children are recovered from the separately streamed children table.</summary>
    internal sealed record ExplorerEpisodePrefix(string Id, string CloudId, long FirstSequence, long Sequence, DateTime Time,
        decimal Buy, decimal Sell, decimal Low, decimal High, decimal Price, decimal? Threshold, int ChildCount, bool Completed, string Reason);
    internal sealed record ExplorerEpisodeChild(string EpisodeId, int Index, ExplorerCloud Cloud);
    internal sealed record ExplorerMembership(string CloudId, long FirstSequence, string EpisodeId, int ChildCount);

    /// <summary>Single-consumer grouping of finished neighboring Clouds from one selected profile; never merges scales.</summary>
    /// <remarks>Boundary equality is accepted. A new completed child proves closure; EOF remains explicitly incomplete.</remarks>
    internal sealed class ExplorerEpisodes
    {
        private readonly ExplorerRunSpec _spec;
        private readonly Action<ExplorerEpisode> _completed;
        private readonly Action<ExplorerEpisodePrefix> _prefix;
        private readonly Action<ExplorerEpisodeChild> _child;
        private readonly Queue<(decimal Volume, int Date, long Sequence)> _previous = new Queue<(decimal, int, long)>();
        private readonly List<string> _children = new List<string>();
        private ExplorerEpisode _current;
        private decimal _rawBefore;
        private int _date;
        private readonly string _hash;
        internal ExplorerEpisode Current => _current == null ? null : _current with { ChildIds = _children.ToImmutableArray() };

        internal ExplorerEpisodes(ExplorerRunSpec spec, Action<ExplorerEpisode> completed, Action<ExplorerEpisodePrefix> prefix, Action<ExplorerEpisodeChild> child)
        { _spec = spec; _completed = completed; _prefix = prefix; _child = child; _hash = spec.EpisodeSpecHash; }

        internal void Add(ExplorerCloud cloud, int dateOrdinal)
        {
            if (cloud.Profile != _spec.Episodes.Profile || cloud.Reason == "OpenAtEnd") { return; }
            ExplorerEpisodeSpec rules = _spec.Episodes;
            if (_current != null && (cloud.StartTime.Date != _current.StartTime.Date ||
                cloud.StartTime - _current.Time > TimeSpan.FromSeconds(rules.MaximumPauseSeconds) ||
                cloud.Time - _current.StartTime > TimeSpan.FromSeconds(rules.MaximumDurationSeconds) ||
                (Math.Max(cloud.High, _current.High) - Math.Min(cloud.Low, _current.Low)) / _spec.PriceStep > _current.ZoneTicks))
            { Finish(cloud.KnownAt.Value, cloud.KnownSequence.Value, "Boundary"); }
            if (_current == null)
            {
                _date = dateOrdinal;
                ExplorerProfile profile = _spec.Profiles.First(p => p.Key == rules.Profile);
                while (_previous.Count > 0 && _previous.Peek().Date < dateOrdinal - profile.VolumeDates + 1) { _previous.Dequeue(); }
                decimal[] baseline = _previous.Where(e => e.Sequence < cloud.FirstSequence).TakeLast(profile.VolumeWindow).Select(e => e.Volume).OrderBy(v => v).ToArray();
                decimal? threshold = baseline.Length >= profile.VolumeMinimum ? baseline[(int)decimal.Ceiling(profile.VolumePercentile * baseline.Length) - 1] : null;
                int zone = rules.AdaptZone && cloud.Effective.Atr.HasValue ? (int)Math.Clamp(decimal.Ceiling(rules.AtrFactor * cloud.Effective.Atr.Value / _spec.PriceStep), rules.ZoneMinimum, rules.ZoneMaximum) : rules.MaximumZoneTicks;
                _rawBefore = cloud.RawVolumeBefore; _children.Clear();
                _current = new ExplorerEpisode(_hash + "/" + cloud.FirstSequence, cloud.Profile, cloud.FirstSequence, cloud.LastSequence,
                    cloud.StartTime, cloud.Time, cloud.KnownAt.Value, cloud.KnownSequence.Value, cloud.Low, cloud.High, 0, 0, 0,
                    zone, threshold, ImmutableArray<string>.Empty, "Forming");
            }
            if (_children.Count >= _spec.MaximumBufferItems) { throw new InvalidDataException("Explorer episode child limit exceeded."); }
            _children.Add(cloud.Id);
            _current = _current with { LastSequence = cloud.LastSequence, Time = cloud.Time, KnownAt = cloud.KnownAt.Value,
                KnownSequence = cloud.KnownSequence.Value, Low = Math.Min(_current.Low, cloud.Low), High = Math.Max(_current.High, cloud.High),
                Buy = OrderFlowVolumeComparison.AddExact(_current.Buy, cloud.Buy), Sell = OrderFlowVolumeComparison.AddExact(_current.Sell, cloud.Sell),
                RawVolume = OrderFlowVolumeComparison.AddExact(cloud.RawVolumeThrough, -_rawBefore) };
            _child(new ExplorerEpisodeChild(_current.Id, _children.Count - 1, cloud));
            _prefix(new ExplorerEpisodePrefix(_current.Id, cloud.Id, _current.FirstSequence, _current.KnownSequence, _current.KnownAt,
                _current.Buy, _current.Sell, _current.Low, _current.High, cloud.Price, _current.RelativeThreshold, _children.Count, false, "Child"));
        }
        internal void DateCutoff(OrderFlowDeal tick)
        { if (_current != null && _current.StartTime.Date != tick.Time.Date) { Finish(tick.Time, tick.SourceSequence, "DateCutoff"); } }
        internal void Complete(DateTime time, long sequence) { Finish(time, sequence, "EpisodeOpenAtEnd"); }
        private void Finish(DateTime time, long sequence, string reason)
        {
            if (_current == null) { return; }
            ExplorerEpisode final = _current with { KnownAt = time, KnownSequence = sequence, ChildIds = _children.ToImmutableArray(), Reason = reason };
            _completed(final);
            _prefix(new ExplorerEpisodePrefix(final.Id, null, final.FirstSequence, sequence, time, final.Buy, final.Sell,
                final.Low, final.High, 0, final.RelativeThreshold, _children.Count, true, reason));
            if (reason != "EpisodeOpenAtEnd")
            {
                _previous.Enqueue((final.Volume, _date, sequence));
                int count = _spec.Profiles.First(p => p.Key == _spec.Episodes.Profile).VolumeWindow;
                while (_previous.Count > count + 1) { _previous.Dequeue(); }
            }
            _current = null; _children.Clear();
        }
    }

    internal static class ExplorerEpisodeRunner
    {
        /// <summary>Reuses the immutable catalog and validates the original source in a separate causal pass before publishing episodes.</summary>
        internal static ExplorerRun Run(ExplorerRun run, CancellationToken cancellation, Action<ExplorerProgress> progress = null)
        {
            ExplorerRunSpec spec = run.Spec;
            if (!spec.Episodes.Enabled) { return run; }
            string destination = Path.Combine(spec.OutputRootPath, "cloud-episodes-" + spec.EpisodeSpecHash);
            if (Directory.Exists(destination))
            { ExplorerStorage.Verify(destination, ExplorerRunSpec.EpisodeVersion, spec.EpisodeSpecHash, cancellation); return run with { EpisodePath = destination }; }
            string staging = ExplorerStorage.Stage(spec.OutputRootPath, "cloud-episodes");
            try
            {
                long count = 0, selected = 0; OrderFlowTickInput metadata = new OrderFlowTickInput(); Stopwatch watch = Stopwatch.StartNew();
                using (OrderFlowTickReader reader = new OrderFlowTickReader(spec.InputPath, metadata, cancellation))
                using (IEnumerator<ExplorerCloud> clouds = ExplorerStorage.ReadRows<ExplorerCloud>(run.CatalogPath, "catalog").GetEnumerator())
                using (ExplorerStorage.RowWriter<ExplorerEpisode> episodes = new ExplorerStorage.RowWriter<ExplorerEpisode>(staging, "episodes"))
                using (ExplorerStorage.RowWriter<ExplorerEpisodePrefix> prefixes = new ExplorerStorage.RowWriter<ExplorerEpisodePrefix>(staging, "episode-prefix"))
                using (ExplorerStorage.RowWriter<ExplorerEpisodeChild> children = new ExplorerStorage.RowWriter<ExplorerEpisodeChild>(staging, "children"))
                using (ExplorerStorage.RowWriter<ExplorerMembership> membership = new ExplorerStorage.RowWriter<ExplorerMembership>(staging, "membership"))
                {
                    if (metadata.Sha256 != spec.InputSha256) { throw new InvalidDataException("Explorer source SHA-256 changed."); }
                    ExplorerEpisodes engine = new ExplorerEpisodes(spec, episode =>
                    {
                        episodes.Add(episode); count++;
                        foreach (string childId in episode.ChildIds) { membership.Add(new ExplorerMembership(childId, long.Parse(childId.Substring(childId.LastIndexOf('/') + 1), System.Globalization.CultureInfo.InvariantCulture), episode.Id, episode.ChildCount)); }
                    }, prefixes.Add, children.Add);
                    bool next = clouds.MoveNext(); DateTime date = default, last = default; int dateOrdinal = 0; long sequence = 0;
                    while (reader.TryRead(out OrderFlowDeal tick))
                    {
                        if (!spec.Includes(tick.Time)) { continue; }
                        if (date != tick.Time.Date) { date = tick.Time.Date; dateOrdinal++; }
                        while (next && clouds.Current.KnownSequence.HasValue && clouds.Current.KnownSequence <= tick.SourceSequence)
                        { engine.Add(clouds.Current, clouds.Current.StartTime.Date == date ? dateOrdinal : dateOrdinal - 1); next = clouds.MoveNext(); }
                        engine.DateCutoff(tick); last = tick.Time; sequence = tick.SourceSequence; selected++;
                        if (selected % 8192 == 0) { ExplorerStorage.CheckMemory(spec); progress?.Invoke(new ExplorerProgress("Episodes", selected, count, date, GC.GetTotalMemory(false), watch.Elapsed.TotalSeconds)); }
                    }
                    engine.Complete(last, sequence);
                }
                ExplorerManifest manifest = new ExplorerManifest(ExplorerRunSpec.EpisodeVersion, spec.EpisodeSpecHash, spec.InputSha256,
                    JsonSerializer.SerializeToElement(new { spec.CatalogSpecHash, spec.Episodes }), metadata.RecordCount, selected, count,
                    run.Catalog.SecondsOnly, run.Catalog.Dates, null);
                ExplorerStorage.Publish(staging, destination, manifest, cancellation); staging = null;
                return run with { EpisodePath = destination };
            }
            finally { if (staging != null && Directory.Exists(staging)) { Directory.Delete(staging, true); } }
        }
    }
}
