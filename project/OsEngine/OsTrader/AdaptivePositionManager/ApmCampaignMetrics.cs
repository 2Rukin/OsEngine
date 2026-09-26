using System;

namespace OsEngine.OsTrader.AdaptivePositionManager
{
    /// <summary>Whole-campaign event-time metrics. Average exposure includes flat time between first fill and the latest observed event.</summary>
    public sealed record ApmCampaignMetrics(decimal NetEquity, decimal MaximumDrawdown, decimal Fees, decimal Turnover,
        decimal MaximumVolume, decimal AverageVolume, decimal SecondsInPosition, decimal ObservedSeconds);

    /// <summary>Constant-memory left-continuous integration in serialized callback order, using market time rather than retrospective fill timestamps.</summary>
    internal sealed class ApmMetricsAccumulator
    {
        private DateTime _start;
        private DateTime _last;
        private decimal _quantity;
        private decimal _quantitySeconds;
        private decimal _positionSeconds;
        private ApmSnapshot _snapshot;

        internal void Add(ApmAuditRow row)
        {
            if (row.Snapshot == null) return;
            DateTime time = row.Snapshot.Market?.Time ?? row.Time;
            if (_start == default && row.Snapshot.FilledVolume > 0) { _start = time; _last = time; }
            if (_start != default)
            {
                if (time < _last) throw new InvalidOperationException("Metric event time regressed.");
                decimal seconds = (decimal)(time - _last).TotalSeconds;
                _quantitySeconds += _quantity * seconds;
                if (_quantity > 0) _positionSeconds += seconds;
                _last = time;
                _quantity = row.Snapshot.FilledVolume;
            }
            _snapshot = row.Snapshot;
        }

        internal ApmCampaignMetrics Snapshot
        {
            get
            {
                decimal seconds = _start == default ? 0 : (decimal)(_last - _start).TotalSeconds;
                return new ApmCampaignMetrics(_snapshot?.Equity ?? 0, _snapshot?.MaximumDrawdown ?? 0,
                    _snapshot?.Fees ?? 0, _snapshot?.Turnover ?? 0, _snapshot?.MaximumVolume ?? 0,
                    seconds == 0 ? 0 : _quantitySeconds / seconds, _positionSeconds, seconds);
            }
        }
    }
}
