/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;

namespace OsEngine.OsData.OrderFlow.Explorer
{
    /// <summary>Explicit raw anchor and causal visibility boundary. EOF-only manual anchors use long.MaxValue as KnownSequence.</summary>
    internal sealed record ExplorerAnchor(string Id, long FirstSequence, long KnownSequence, DateTime Date)
    {
        /// <summary>Rejects a stale selection from another catalog or episode configuration before opening source data.</summary>
        internal void Validate(ExplorerRunSpec spec)
        {
            bool catalog = Id != null && Id.StartsWith(spec.CatalogSpecHash + "/", StringComparison.Ordinal);
            bool episode = spec.Episodes.Enabled && Id != null && Id.StartsWith(spec.EpisodeSpecHash + "/", StringComparison.Ordinal);
            if ((!catalog && !episode) || FirstSequence < 1 || KnownSequence < FirstSequence)
            { throw new InvalidDataException("VWAP-якорь не принадлежит текущему результату. Выберите Cloud или эпизод заново."); }
        }
    }
    internal sealed record ExplorerVwapSample(DateTime Time, long Sequence, decimal Price, decimal Vwap, decimal Sigma);
    internal sealed record ExplorerFrame(long Sequence, DateTime Time, bool Complete, ImmutableArray<ExplorerCloud> Clouds,
        ExplorerEpisode Episode, ImmutableArray<ExplorerPivot> Pivots, ExplorerPivot Provisional, ImmutableArray<ExplorerObservation> Observations,
        ImmutableArray<ExplorerLabel> Labels, ImmutableArray<ExplorerVwapSample> Vwap, ImmutableArray<ExplorerWatch> Watches,
        ImmutableArray<ExplorerTrigger> Triggers, ImmutableArray<ExplorerDiagnostic> Diagnostics, ImmutableArray<ExplorerBar> Bars, ImmutableArray<ExplorerEpisode> Episodes);

    /// <summary>Bounded selected-anchor line. Each sample uses all preceding raw volume, even when older drawing points are compacted.</summary>
    internal sealed class ExplorerAnchorSeries
    {
        private readonly ExplorerAnchor _anchor;
        private readonly ExplorerVwap _moments = new ExplorerVwap();
        private readonly List<ExplorerVwapSample> _samples = new List<ExplorerVwapSample>();
        private long _rows, _stride = 1;
        internal ExplorerAnchorSeries(ExplorerAnchor anchor) { _anchor = anchor; }
        internal void Add(OrderFlowDeal tick)
        {
            if (_anchor == null || tick.SourceSequence < _anchor.FirstSequence || tick.Time.Date != _anchor.Date) { return; }
            _moments.Add(tick.Price, tick.Volume); _rows++;
            ExplorerVwapSample sample = new ExplorerVwapSample(tick.Time, tick.SourceSequence, tick.Price, _moments.Mean, _moments.Sigma);
            if (_rows % _stride == 0 || _samples.Count == 0) { _samples.Add(sample); }
            else if (_samples.Count > 1) { _samples[_samples.Count - 1] = sample; }
            if (_samples.Count > 2048)
            {
                for (int i = _samples.Count - 2; i > 0; i -= 2) { _samples.RemoveAt(i); }
                _stride *= 2;
            }
        }
        internal ImmutableArray<ExplorerVwapSample> Snapshot(long sequence, bool eof = false) =>
            _anchor != null && (sequence >= _anchor.KnownSequence || (eof && _anchor.KnownSequence == long.MaxValue)) ? _samples.ToImmutableArray() : ImmutableArray<ExplorerVwapSample>.Empty;
    }

    /// <summary>Disposable synchronous replay cursor for one background worker, using the saved immutable run specification.</summary>
    /// <remarks>
    /// SHA-256 is checked on a protected source handle before stepping. One Step consumes exactly one selected raw row;
    /// EOF is a distinct finalization step. Frames detach bounded prefixes and never use historical final entities.
    /// UI owns pacing/pause, cancels its worker on disposal and must not share this cursor between threads.
    /// </remarks>
    internal sealed class ExplorerReplayCursor : IDisposable
    {
        private readonly ExplorerRun _run;
        private readonly OrderFlowTickReader _reader;
        private readonly ExplorerCatalog _catalog;
        private readonly ExplorerEpisodes _episodes;
        private readonly ExplorerStudyEngine _study;
        private readonly ExplorerVolumeBaseline _baseline;
        private readonly ExplorerVolumeBaseline _episodeBaseline;
        private readonly ExplorerAnchorSeries _vwap;
        private readonly CancellationToken _cancellation;
        private readonly Queue<ExplorerCloud> _clouds = new Queue<ExplorerCloud>();
        private readonly Queue<ExplorerPivot> _pivots = new Queue<ExplorerPivot>();
        private readonly Queue<ExplorerObservation> _observations = new Queue<ExplorerObservation>();
        private readonly Queue<ExplorerLabel> _labels = new Queue<ExplorerLabel>();
        private readonly Queue<ExplorerTrigger> _triggers = new Queue<ExplorerTrigger>();
        private readonly Queue<ExplorerDiagnostic> _diagnostics = new Queue<ExplorerDiagnostic>();
        private readonly Queue<ExplorerBar> _bars = new Queue<ExplorerBar>();
        private readonly Queue<ExplorerEpisode> _finishedEpisodes = new Queue<ExplorerEpisode>();
        private readonly ExplorerBars _barBuilder;
        private OrderFlowDeal _last;
        private bool _complete;
        private int _dateOrdinal;
        private DateTime _date;
        #region Source processing

        internal ExplorerReplayCursor(ExplorerRun run, ExplorerAnchor anchor, CancellationToken cancellation)
        {
            anchor?.Validate(run.Spec);
            _run = run; _cancellation = cancellation;
            OrderFlowTickInput metadata = new OrderFlowTickInput();
            _reader = new OrderFlowTickReader(run.Spec.InputPath, metadata, cancellation);
            if (metadata.Sha256 != run.Spec.InputSha256) { _reader.Dispose(); throw new InvalidDataException("Explorer replay source SHA-256 changed."); }
            _baseline = new ExplorerVolumeBaseline(run.Spec.StudyProfile, run.Spec.MaximumBufferItems);
            _episodeBaseline = new ExplorerVolumeBaseline(run.Spec.StudyProfile with { TimeOfDayVolume = false }, run.Spec.MaximumBufferItems);
            _study = new ExplorerStudyEngine(run.Spec, run.Catalog.Dates, trigger => Remember(_triggers, trigger), p => Remember(_pivots, p), o => Remember(_observations, o), l => Remember(_labels, l), d => Remember(_diagnostics, d));
            _barBuilder = new ExplorerBars(bar => Remember(_bars, bar));
            if (run.Spec.Episodes.Enabled)
            { _episodes = new ExplorerEpisodes(run.Spec, e => { Remember(_finishedEpisodes, e); _episodeBaseline.Add(e.Baseline(), e.StartTime.Date == _date ? _dateOrdinal : _dateOrdinal - 1); }, _study.Episode, c => { }); }
            _catalog = new ExplorerCatalog(run.Spec, CompletedCloud, _study.Prefix);
            _vwap = new ExplorerAnchorSeries(anchor);
        }
        internal ExplorerFrame Step()
        {
            if (_complete) { return Capture(); }
            while (_reader.TryRead(out OrderFlowDeal tick))
            {
                if (!_run.Spec.Includes(tick.Time)) { continue; }
                if (_date != tick.Time.Date) { _date = tick.Time.Date; _dateOrdinal++; }
                _baseline.Advance(_dateOrdinal);
                _episodeBaseline.Advance(_dateOrdinal);
                _catalog.Add(tick, _cancellation); _episodes?.DateCutoff(tick);
                ExplorerProfile profile = _run.Spec.StudyProfile;
                bool known = profile.TimeOfDayVolume ? _baseline.TimeOfDay(tick.Time, tick.SourceSequence).HasValue : _baseline.Rolling(tick.SourceSequence).HasValue;
                _study.Tick(tick, _run.Spec.Study.EpisodeTrigger ? _episodeBaseline.Rolling(tick.SourceSequence).HasValue : known);
                _vwap.Add(tick); _last = tick;
                _barBuilder.Add(tick);
                return Capture();
            }
            _catalog.Complete(); _episodes?.Complete(_last?.Time ?? default, _last?.SourceSequence ?? 0); _study.Complete(); _barBuilder.Complete(); _complete = true;
            return Capture();
        }
        private void CompletedCloud(ExplorerCloud cloud)
        {
            Remember(_clouds, cloud);
            int date = cloud.StartTime.Date == _date ? _dateOrdinal : _dateOrdinal - 1;
            if (cloud.Profile == _run.Spec.Study.Profile) { _baseline.Add(cloud, date); }
            _episodes?.Add(cloud, date);
        }
        #endregion

        #region Detached frames and disposal

        private ExplorerFrame Capture() => new ExplorerFrame(_last?.SourceSequence ?? 0, _last?.Time ?? default, _complete,
            _clouds.Concat(_catalog.Forming).ToImmutableArray(), _episodes?.Current, _pivots.ToImmutableArray(), _study.Provisional,
            _observations.ToImmutableArray(), _labels.ToImmutableArray(), _vwap.Snapshot(_last?.SourceSequence ?? 0, _complete), _study.Watches.ToImmutableArray(),
            _triggers.ToImmutableArray(), _diagnostics.ToImmutableArray(), _bars.Concat(_barBuilder.Current == null ? Array.Empty<ExplorerBar>() : new[] { _barBuilder.Current }).ToImmutableArray(),
            _finishedEpisodes.Concat(_episodes?.Current == null ? Array.Empty<ExplorerEpisode>() : new[] { _episodes.Current }).ToImmutableArray());
        private static void Remember<T>(Queue<T> queue, T value) { queue.Enqueue(value); if (queue.Count > 250) { queue.Dequeue(); } }
        public void Dispose() { _reader.Dispose(); }
        #endregion
    }

    internal static class ExplorerAnchorRunner
    {
        /// <summary>Reads one selected anchor on a worker, validates the entire file and returns at most 2048 drawing samples.</summary>
        internal static ImmutableArray<ExplorerVwapSample> Run(ExplorerRun run, ExplorerAnchor anchor, CancellationToken cancellation)
        {
            anchor.Validate(run.Spec);
            OrderFlowTickInput metadata = new OrderFlowTickInput(); ExplorerAnchorSeries series = new ExplorerAnchorSeries(anchor);
            using OrderFlowTickReader reader = new OrderFlowTickReader(run.Spec.InputPath, metadata, cancellation);
            if (metadata.Sha256 != run.Spec.InputSha256) { throw new InvalidDataException("Explorer VWAP source SHA-256 changed."); }
            long sequence = 0;
            while (reader.TryRead(out OrderFlowDeal tick)) { if (run.Spec.Includes(tick.Time)) { series.Add(tick); sequence = tick.SourceSequence; } }
            return series.Snapshot(sequence, true);
        }
    }
}
