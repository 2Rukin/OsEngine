using System;

namespace OsEngine.OsTrader.AdaptivePositionManager
{
    /// <summary>Whole-campaign event-time metrics. Average exposure includes flat time between first fill and the latest observed event.</summary>
    public sealed record ApmCampaignMetrics(decimal NetEquity, decimal MaximumDrawdown, decimal Fees, decimal Turnover,
        decimal MaximumVolume, decimal AverageVolume, decimal SecondsInPosition, decimal ObservedSeconds)
    {
        /// <summary>First actual increasing fill time, when observed by the current implementation.</summary>
        public DateTime? EntryTime { get; init; }
        /// <summary>Latest actual fill that flattened a latched campaign; null when no such fill was observed.</summary>
        public DateTime? ExitTime { get; init; }
        /// <summary>Entry action associated with the first actual increasing fill.</summary>
        public string EntryReason { get; init; } = "";
        /// <summary>Latched exit reason captured on completion.</summary>
        public string ExitReason { get; init; } = "";
    }

    /// <summary>Constant-memory left-continuous integration in serialized callback order, using market time rather than retrospective fill timestamps.</summary>
    internal sealed class ApmMetricsAccumulator
    {
        private DateTime _start;
        private DateTime _last;
        private decimal _quantity;
        private decimal _quantitySeconds;
        private decimal _positionSeconds;
        private ApmSnapshot _snapshot;
        private DateTime? _entryTime;
        private DateTime? _exitTime;
        private string _entryReason = "";
        private string _exitReason = "";

        internal void Add(ApmAuditRow row)
        {
            if (row.Snapshot == null) return;
            DateTime time = row.Snapshot.Market?.Time ?? row.Time;
            if (!_entryTime.HasValue && row.Kind == "Fill" && row.Intent?.Increases == true
                && row.Snapshot.FilledVolume > 0)
            {
                _entryTime = row.Time;
                _entryReason = row.Intent.Action.ToString();
            }
            if (row.Kind == "Fill" && row.Snapshot.ExitLatch && _entryTime.HasValue)
            {
                if (_quantity > 0 && row.Snapshot.FilledVolume == 0)
                {
                    _exitTime = row.Time;
                    _exitReason = row.Snapshot.ExitReason;
                }
                else if (row.Snapshot.FilledVolume > 0)
                {
                    // A late increasing fill reopens exposure; only its later flattening fill may restore ExitTime.
                    _exitTime = null;
                    _exitReason = "";
                }
            }
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
                    seconds == 0 ? 0 : _quantitySeconds / seconds, _positionSeconds, seconds)
                    { EntryTime = _entryTime, ExitTime = _exitTime, EntryReason = _entryReason, ExitReason = _exitReason };
            }
        }
    }
}
