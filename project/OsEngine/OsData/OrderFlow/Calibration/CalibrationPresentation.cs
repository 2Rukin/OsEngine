/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.OsData.OrderFlow.Explorer;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace OsEngine.OsData.OrderFlow.Calibration
{
    internal sealed record CalibrationMarker(CalibrationRun Run, CloudRule Rule, CalibrationEvent Event)
    {
        internal string CloudId => Event.CloudId(Rule);
        internal bool Known(long sequence, bool complete) => Event.Evidence.KnownSequence.HasValue ? Event.Evidence.KnownSequence <= sequence : complete;
    }
    internal sealed record CalibrationLayer(CloudRule Rule, long PassedCount, ImmutableArray<CalibrationMarker> Markers);
    internal sealed record CalibrationChartData(OrderFlowResearchResult Prices, ImmutableArray<CalibrationLayer> Layers, OrderFlowDisplayTimeFrame TimeFrame);
    internal sealed record PinnedCandidate(string BundlePath, string TimeRangeId, string ProfileName, FormationSpec Formation, string FormationHash,
        EventSummary Summary, decimal NeighborSensitivity);
    internal sealed record CalibrationWorkspace(ImmutableArray<TimeRangeProfile> CustomProfiles, ImmutableArray<PinnedCandidate> Pins,
        ImmutableArray<decimal> TickThresholds);

    /// <summary>Bounded historical presentation loaded on a worker; full catalogs remain available through streamed tables.</summary>
    internal static class CalibrationPresentation
    {
        internal const int MarkersPerLayer = 2000;
        internal static CalibrationChartData Load(CalibrationRun run, IEnumerable<CloudRule> rules, OrderFlowDisplayTimeFrame frame, CancellationToken cancellation)
        {
            Dictionary<string, CalibrationRun> opened = new Dictionary<string, CalibrationRun>(StringComparer.OrdinalIgnoreCase) { [run.Directory] = run };
            List<CalibrationLayer> layers = new List<CalibrationLayer>();
            foreach (CloudRule rule in rules)
            {
                cancellation.ThrowIfCancellationRequested();
                if (rule.Provenance.InputSha256 != run.Spec.InputSha256 || rule.Provenance.PriceStep != run.Spec.PriceStep ||
                    rule.Provenance.FromDate != run.Spec.FromDate || rule.Provenance.ToDate != run.Spec.ToDate || !rule.Enabled) { continue; }
                if (layers.Count >= 32) { throw new System.IO.InvalidDataException("Одновременно можно показать не более 32 Cloud layers; отключите лишние правила."); }
                if (!opened.TryGetValue(rule.BundlePath, out CalibrationRun source)) { source = CalibrationStorage.Open(rule.BundlePath, cancellation); opened.Add(rule.BundlePath, source); }
                if (source.Spec.Hash != rule.Provenance.Hash) { throw new System.IO.InvalidDataException("Provenance сохранённого правила не совпадает с bundle."); }
                Queue<CalibrationMarker> markers = new Queue<CalibrationMarker>(); long passed = 0;
                foreach (CalibrationEvent item in CalibrationStorage.Events(source, rule.Formation, cancellation))
                {
                    if (!CalibrationFilter.Passes(item, source.Spec.PriceStep, rule.Kind, rule.Filters, out _)) { continue; }
                    passed++; markers.Enqueue(new CalibrationMarker(source, rule, item)); if (markers.Count > MarkersPerLayer) { markers.Dequeue(); }
                }
                layers.Add(new CalibrationLayer(rule, passed, markers.ToImmutableArray()));
            }
            OrderFlowResearchResult prices = new OrderFlowResearchResult { InputHash = run.Spec.InputSha256, DeltaCalculated = false,
                Input = new OrderFlowTickInput { Sha256 = run.Spec.InputSha256 }, ResearchSpecHash = run.Spec.Hash };
            // A transferred chart retains the main selector. Aggregate each supported timeframe directly from stored bars;
            // compacted Min1 display bars cannot serve as exact input for a second aggregation.
            foreach (OrderFlowDisplayTimeFrame timeFrame in OrderFlowChartTimeFrames.GetMenuValues())
            {
                cancellation.ThrowIfCancellationRequested();
                IReadOnlyList<ExplorerBar> bars = ExplorerBars.ReadRange(run.Directory, run.Manifest.Quality.First.Value, run.Manifest.Quality.Last.Value, timeFrame, cancellation);
                prices.Bars[timeFrame] = bars.Select(b => new OrderFlowDisplayBar { TimeFrame = timeFrame, TimeStart = b.Start, TimeEnd = b.End,
                    HasTrades = true, Open = b.Open, High = b.High, Low = b.Low, Close = b.Close, Volume = b.Volume }).ToList();
            }
            return new CalibrationChartData(prices, layers.ToImmutableArray(), frame);
        }
    }
}
