/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using OsEngine.Entity;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace OsEngine.OsData.OrderFlow
{
    internal sealed class OrderFlowCanonicalHash : IDisposable
    {
        private IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private bool _completed;

        public void Add(params object[] values)
        {
            if (_completed)
            {
                throw new InvalidOperationException("The canonical hash has already been completed.");
            }

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append('|');
                }

                object value = values[i];
                if (value == null)
                {
                    builder.Append("null");
                }
                else if (value is decimal)
                {
                    builder.Append(((decimal)value).ToString("G29", CultureInfo.InvariantCulture));
                }
                else if (value is DateTime)
                {
                    builder.Append(((DateTime)value).Ticks.ToString(CultureInfo.InvariantCulture));
                }
                else if (value is bool)
                {
                    builder.Append((bool)value ? "1" : "0");
                }
                else if (value is IFormattable)
                {
                    builder.Append(((IFormattable)value).ToString(null, CultureInfo.InvariantCulture));
                }
                else
                {
                    builder.Append(value.ToString());
                }
            }

            builder.Append('\n');
            byte[] bytes = Encoding.UTF8.GetBytes(builder.ToString());
            _hash.AppendData(bytes);
        }

        public string Complete()
        {
            if (_completed)
            {
                throw new InvalidOperationException("The canonical hash has already been completed.");
            }

            _completed = true;
            return Convert.ToHexString(_hash.GetHashAndReset()).ToLowerInvariant();
        }

        public void Dispose()
        {
            if (_hash != null)
            {
                _hash.Dispose();
                _hash = null;
            }
        }

        public static string Calculate(string value)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
                return Convert.ToHexString(sha256.ComputeHash(bytes)).ToLowerInvariant();
            }
        }
    }

    /// <summary>
    /// Maintains a bounded causal deal window and derives one detached feature
    /// snapshot only after the current timestamp bucket has closed.
    /// </summary>
    /// <remarks>
    /// Uses only trade prices, source sides and volumes from the trailing time window.
    /// Contract: ORDER-FLOW-DATA-001.
    /// </remarks>
    internal sealed class OrderFlowFeatureWindow
    {
        private readonly Queue<OrderFlowDeal> _deals = new Queue<OrderFlowDeal>();
        private decimal _buyVolume;
        private decimal _sellVolume;

        /// <summary>
        /// Adds one closed deal bucket, evicts records strictly older than
        /// <c>T - FeatureWindowSeconds</c> and builds the causal feature DTO.
        /// </summary>
        /// <param name="bucket">Closed bucket containing validated positive Buy/Sell deals.</param>
        /// <param name="request">Validated run-scoped formulas and thresholds.</param>
        /// <returns>A detached feature DTO, or <c>null</c> when the bounded window is empty.</returns>
        /// <remarks>Reference prices are per-timestamp VWAP values; published DTOs are mutable by type and treated as read-only by convention.</remarks>
        public OrderFlowFeatureSnapshot Build(OrderFlowBucket bucket, OrderFlowResearchRequest request)
        {
            for (int i = 0; i < bucket.Deals.Count; i++)
            {
                OrderFlowDeal deal = bucket.Deals[i];
                if (deal.Price <= 0 || deal.Volume <= 0 ||
                    (deal.Side != Side.Buy && deal.Side != Side.Sell))
                {
                    continue;
                }

                _deals.Enqueue(deal);
                if (deal.Side == Side.Buy)
                {
                    _buyVolume += deal.Volume;
                }
                else
                {
                    _sellVolume += deal.Volume;
                }
            }

            DateTime cutoff = bucket.Time.AddSeconds(-request.FeatureWindowSeconds);
            while (_deals.Count > 0 && _deals.Peek().Time < cutoff)
            {
                OrderFlowDeal expired = _deals.Dequeue();
                if (expired.Side == Side.Buy)
                {
                    _buyVolume -= expired.Volume;
                }
                else
                {
                    _sellVolume -= expired.Volume;
                }
            }

            if (_deals.Count == 0)
            {
                return null;
            }

            DateTime firstTime = _deals.Peek().Time;
            decimal firstPrice = CalculateBucketVwap(_deals, firstTime);
            decimal currentPrice = CalculateBucketVwap(bucket.Deals, bucket.Time);

            OrderFlowFeatureSnapshot snapshot = new OrderFlowFeatureSnapshot();
            snapshot.SnapshotId = "S-" + bucket.Time.ToString("yyyyMMddHHmmssffffff", CultureInfo.InvariantCulture) +
                "-" + bucket.BucketSequence.ToString("D10", CultureInfo.InvariantCulture);
            snapshot.BucketSequence = bucket.BucketSequence;
            snapshot.Time = bucket.Time;
            snapshot.ReferencePrice = currentPrice;
            snapshot.BuyVolume = _buyVolume;
            snapshot.SellVolume = _sellVolume;
            snapshot.Delta = _buyVolume - _sellVolume;
            snapshot.TradeCount = _deals.Count;
            snapshot.PriceChange = currentPrice - firstPrice;
            snapshot.PriceResponse = snapshot.Delta == 0
                ? 0
                : snapshot.PriceChange / Math.Abs(snapshot.Delta);

            snapshot.DataQualityCode = "OK";
            return snapshot;
        }

        private static decimal CalculateBucketVwap(IEnumerable<OrderFlowDeal> deals, DateTime bucketTime)
        {
            decimal notional = 0;
            decimal volume = 0;

            foreach (OrderFlowDeal deal in deals)
            {
                if (deal.Time > bucketTime) { break; }
                if (deal.Time != bucketTime || deal.Price <= 0 || deal.Volume <= 0)
                {
                    continue;
                }

                notional += deal.Price * deal.Volume;
                volume += deal.Volume;
            }

            if (volume <= 0)
            {
                throw new InvalidOperationException("A closed deal bucket has no positive volume for VWAP.");
            }

            return notional / volume;
        }

    }

    internal sealed class OrderFlowDisplayBarAggregator
    {
        private readonly Dictionary<OrderFlowDisplayTimeFrame, List<OrderFlowDisplayBar>> _bars
            = new Dictionary<OrderFlowDisplayTimeFrame, List<OrderFlowDisplayBar>>();
        private readonly Dictionary<OrderFlowDisplayTimeFrame, OrderFlowDisplayBar> _current
            = new Dictionary<OrderFlowDisplayTimeFrame, OrderFlowDisplayBar>();

        public OrderFlowDisplayBarAggregator()
        {
            _bars[OrderFlowDisplayTimeFrame.Sec15] = new List<OrderFlowDisplayBar>();
            _bars[OrderFlowDisplayTimeFrame.Sec30] = new List<OrderFlowDisplayBar>();
            _bars[OrderFlowDisplayTimeFrame.Min1] = new List<OrderFlowDisplayBar>();
        }

        public void Add(OrderFlowBucket bucket, OrderFlowFeatureSnapshot snapshot, bool preserveResponse = false)
        {
            if (bucket.Deals.Count == 0)
            {
                return;
            }

            Add(bucket, snapshot, OrderFlowDisplayTimeFrame.Sec15, TimeSpan.FromSeconds(15), preserveResponse);
            Add(bucket, snapshot, OrderFlowDisplayTimeFrame.Sec30, TimeSpan.FromSeconds(30), preserveResponse);
            Add(bucket, snapshot, OrderFlowDisplayTimeFrame.Min1, TimeSpan.FromMinutes(1), preserveResponse);
        }

        private void Add(OrderFlowBucket bucket, OrderFlowFeatureSnapshot snapshot,
            OrderFlowDisplayTimeFrame timeFrame, TimeSpan duration, bool preserveResponse)
        {
            DateTime start = new DateTime(bucket.Time.Ticks - bucket.Time.Ticks % duration.Ticks, bucket.Time.Kind);
            OrderFlowDisplayBar bar;

            if (_current.TryGetValue(timeFrame, out bar) == false || bar.TimeStart != start)
            {
                if (bar != null)
                {
                    _bars[timeFrame].Add(bar);
                }

                bar = new OrderFlowDisplayBar();
                bar.TimeFrame = timeFrame;
                bar.TimeStart = start;
                bar.TimeEnd = start.Add(duration);
                _current[timeFrame] = bar;
            }

            for (int i = 0; i < bucket.Deals.Count; i++)
            {
                OrderFlowDeal deal = bucket.Deals[i];
                if (deal.Price <= 0 || deal.Volume <= 0 ||
                    (deal.Side != Side.Buy && deal.Side != Side.Sell))
                {
                    continue;
                }

                if (bar.HasTrades == false)
                {
                    bar.HasTrades = true;
                    bar.Open = deal.Price;
                    bar.High = deal.Price;
                    bar.Low = deal.Price;
                    bar.Close = deal.Price;
                }
                else
                {
                    if (deal.Price > bar.High)
                    {
                        bar.High = deal.Price;
                    }

                    if (deal.Price < bar.Low)
                    {
                        bar.Low = deal.Price;
                    }

                    bar.Close = deal.Price;
                }

                bar.Volume += deal.Volume;
                bar.Delta += deal.Side == Side.Buy ? deal.Volume : -deal.Volume;
            }

            if (!preserveResponse || snapshot != null) { bar.PriceResponse = snapshot?.PriceResponse ?? 0; }
        }

        /// <summary>Builds a detached prefix view, overlaying the uncommitted timestamp bucket exactly once.</summary>
        /// <remarks>Closed bars are shared read-only; mutable bars are copied. Preview never finalizes live state or computes features; response stays at the last closed bucket of that bar (zero for a new bar).</remarks>
        internal Dictionary<OrderFlowDisplayTimeFrame, List<OrderFlowDisplayBar>> Snapshot(OrderFlowBucket pending)
        {
            OrderFlowDisplayBarAggregator preview = new OrderFlowDisplayBarAggregator();
            foreach (KeyValuePair<OrderFlowDisplayTimeFrame, List<OrderFlowDisplayBar>> pair in _bars)
            {
                preview._bars[pair.Key].AddRange(pair.Value);
            }
            foreach (KeyValuePair<OrderFlowDisplayTimeFrame, OrderFlowDisplayBar> pair in _current)
            {
                preview._current[pair.Key] = pair.Value.Copy();
            }
            if (pending != null) { preview.Add(pending, null, true); }
            return preview.Complete();
        }

        public Dictionary<OrderFlowDisplayTimeFrame, List<OrderFlowDisplayBar>> Complete()
        {
            foreach (KeyValuePair<OrderFlowDisplayTimeFrame, OrderFlowDisplayBar> pair in _current)
            {
                if (pair.Value != null)
                {
                    _bars[pair.Key].Add(pair.Value);
                }
            }

            _current.Clear();
            return _bars;
        }
    }

    /// <summary>
    /// Computes future market-path labels without exposing them to the feature
    /// engine or candidate detector.
    /// </summary>
    /// <remarks>
    /// One replay thread owns the instance from construction through ordered
    /// <see cref="AddCandidate"/>, <see cref="Advance"/> and final
    /// <see cref="Complete"/>; the class provides no synchronization and must
    /// not be shared across runs. It records research-only future price paths
    /// and creates no order, fill, position, execution result or PnL.
    /// Contract: ORDER-FLOW-RESEARCH-001 and ORDER-FLOW-DATA-001.
    /// </remarks>
    internal sealed class OrderFlowMarketPathLabeler
    {
        private readonly List<OrderFlowLabelTracker> _active = new List<OrderFlowLabelTracker>();
        private readonly List<OrderFlowMarketPathLabel> _labels;
        private readonly List<OrderFlowJournalEntry> _journal;
        private readonly decimal _priceStep;
        private readonly int _targetTicks;
        private readonly int _invalidationTicks;

        /// <summary>
        /// Creates the offline future-label accumulator for one instrument run.
        /// </summary>
        /// <remarks>
        /// The caller retains lifecycle ownership of both destination lists and
        /// must supply positive, already validated tick settings. Construction
        /// performs no replay, execution or filesystem work.
        /// </remarks>
        /// <param name="labels">Destination list owned by the result.</param>
        /// <param name="journal">Destination audit journal owned by the result.</param>
        /// <param name="priceStep">Positive price distance represented by one tick.</param>
        /// <param name="targetTicks">Positive favorable barrier in ticks.</param>
        /// <param name="invalidationTicks">Positive adverse barrier in ticks.</param>
        public OrderFlowMarketPathLabeler(List<OrderFlowMarketPathLabel> labels,
            List<OrderFlowJournalEntry> journal, decimal priceStep, int targetTicks, int invalidationTicks)
        {
            _labels = labels;
            _journal = journal;
            _priceStep = priceStep;
            _targetTicks = targetTicks;
            _invalidationTicks = invalidationTicks;
        }

        /// <summary>
        /// Starts one tracker per configured future horizon for a broad candidate.
        /// </summary>
        /// <remarks>
        /// The owning replay thread calls this only after advancing labels with
        /// the candidate bucket, so that bucket is excluded. Horizons belong to
        /// research labels and never authorize execution.
        /// </remarks>
        /// <param name="candidate">Candidate already published from a closed causal bucket.</param>
        /// <param name="horizonsSeconds">Positive, sorted horizon lengths in seconds.</param>
        public void AddCandidate(OrderFlowCandidate candidate, List<int> horizonsSeconds)
        {
            for (int i = 0; i < horizonsSeconds.Count; i++)
            {
                OrderFlowLabelTracker tracker = new OrderFlowLabelTracker();
                tracker.Candidate = candidate;
                tracker.HorizonSeconds = horizonsSeconds[i];
                tracker.HorizonTime = candidate.Time.AddSeconds(horizonsSeconds[i]);
                _active.Add(tracker);
            }
        }

        /// <summary>
        /// Applies all valid deals of one later closed timestamp bucket and
        /// finalizes horizons that end at or before that bucket.
        /// </summary>
        /// <remarks>
        /// The owning replay thread must call buckets in nondecreasing source
        /// time. MFE, MAE and barriers inspect individual deals; signed return
        /// uses bucket VWAP, and equal-time opposite barriers are ambiguous.
        /// </remarks>
        /// <param name="bucketTime">Common source time of the closed bucket.</param>
        /// <param name="deals">Validated positive deals from that bucket in stable source-file order.</param>
        public void Advance(DateTime bucketTime, List<OrderFlowDeal> deals)
        {
            FinalizeDue(bucketTime, false);

            decimal closingNotional = 0;
            decimal closingVolume = 0;
            DateTime closingTime = DateTime.MinValue;

            for (int dealIndex = 0; dealIndex < deals.Count; dealIndex++)
            {
                OrderFlowDeal deal = deals[dealIndex];
                if (deal.Price <= 0 || deal.Time <= DateTime.MinValue)
                {
                    continue;
                }

                closingNotional += deal.Price * deal.Volume;
                closingVolume += deal.Volume;
                closingTime = deal.Time;

                for (int trackerIndex = 0; trackerIndex < _active.Count; trackerIndex++)
                {
                    OrderFlowLabelTracker tracker = _active[trackerIndex];
                    if (deal.Time <= tracker.Candidate.Time || deal.Time > tracker.HorizonTime)
                    {
                        continue;
                    }

                    tracker.Apply(deal, _priceStep * _targetTicks, _priceStep * _invalidationTicks);
                }
            }

            if (closingVolume > 0)
            {
                decimal closingPrice = closingNotional / closingVolume;
                for (int trackerIndex = 0; trackerIndex < _active.Count; trackerIndex++)
                {
                    OrderFlowLabelTracker tracker = _active[trackerIndex];
                    if (closingTime > tracker.Candidate.Time && closingTime <= tracker.HorizonTime)
                    {
                        tracker.ApplyClosingPrice(closingPrice);
                    }
                }
            }

            FinalizeDue(bucketTime, true);
        }

        /// <summary>
        /// Finalizes every remaining tracker at end of replay.
        /// </summary>
        /// <remarks>
        /// The owning replay thread calls this once after the final bucket. A
        /// horizon beyond <paramref name="lastEventTime"/> remains incomplete;
        /// completion does not imply executable or profitable behavior.
        /// </remarks>
        /// <param name="lastEventTime">Last selected tick bucket time; excluded dates provide no label evidence.</param>
        public void Complete(DateTime lastEventTime)
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                OrderFlowLabelTracker tracker = _active[i];
                bool isComplete = tracker.HorizonTime <= lastEventTime;
                AddFinalLabel(tracker, isComplete);
                _active.RemoveAt(i);
            }
        }

        private void FinalizeDue(DateTime bucketTime, bool includeEqual)
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                OrderFlowLabelTracker tracker = _active[i];
                bool isDue = includeEqual
                    ? tracker.HorizonTime <= bucketTime
                    : tracker.HorizonTime < bucketTime;

                if (isDue == false)
                {
                    continue;
                }

                AddFinalLabel(tracker, true);
                _active.RemoveAt(i);
            }
        }

        private void AddFinalLabel(OrderFlowLabelTracker tracker, bool isComplete)
        {
            OrderFlowMarketPathLabel label = tracker.CreateLabel(isComplete);
            _labels.Add(label);

            OrderFlowJournalEntry entry = new OrderFlowJournalEntry();
            entry.Time = label.HorizonTime;
            entry.Kind = OrderFlowJournalKind.LabelFinalized;
            entry.CorrelationId = label.CandidateId;
            entry.ReasonCode = label.Outcome.ToString();
            entry.Message = "Market-path label " + label.HorizonSeconds.ToString(CultureInfo.InvariantCulture) +
                "s finalized. This is not an execution or PnL result.";
            _journal.Add(entry);
        }
    }

    internal sealed class OrderFlowLabelTracker
    {
        public OrderFlowCandidate Candidate { get; set; }

        public int HorizonSeconds { get; set; }

        public DateTime HorizonTime { get; set; }

        private decimal _lastSignedReturn;
        private decimal _mfe;
        private decimal _mae;
        private long _timeToMfe = -1;
        private long _timeToMae = -1;
        private long _timeToTarget = -1;
        private long _timeToInvalidation = -1;
        private int _futureTradeCount;

        public void Apply(OrderFlowDeal deal, decimal targetDistance, decimal invalidationDistance)
        {
            decimal direction = Candidate.Direction == OrderFlowDirection.Long ? 1 : -1;
            decimal signedReturn = (deal.Price - Candidate.ReferencePrice) * direction;
            long elapsed = (deal.Time.Ticks - Candidate.Time.Ticks) / 10;

            _futureTradeCount++;
            if (signedReturn > _mfe)
            {
                _mfe = signedReturn;
                _timeToMfe = elapsed;
            }

            if (signedReturn < _mae)
            {
                _mae = signedReturn;
                _timeToMae = elapsed;
            }

            if (_timeToTarget < 0 && signedReturn >= targetDistance)
            {
                _timeToTarget = elapsed;
            }

            if (_timeToInvalidation < 0 && signedReturn <= -invalidationDistance)
            {
                _timeToInvalidation = elapsed;
            }
        }

        public void ApplyClosingPrice(decimal closingPrice)
        {
            decimal direction = Candidate.Direction == OrderFlowDirection.Long ? 1 : -1;
            _lastSignedReturn = (closingPrice - Candidate.ReferencePrice) * direction;
        }

        public OrderFlowMarketPathLabel CreateLabel(bool isComplete)
        {
            OrderFlowMarketPathLabel label = new OrderFlowMarketPathLabel();
            label.CandidateId = Candidate.CandidateId;
            label.HorizonSeconds = HorizonSeconds;
            label.CandidateTime = Candidate.Time;
            label.HorizonTime = HorizonTime;
            label.IsComplete = isComplete;
            label.SignedReturn = _lastSignedReturn;
            label.MaximumFavorableExcursion = _mfe;
            label.MaximumAdverseExcursion = _mae;
            label.TimeToMfeMicroseconds = _timeToMfe;
            label.TimeToMaeMicroseconds = _timeToMae;
            label.TimeToTargetMicroseconds = _timeToTarget;
            label.TimeToInvalidationMicroseconds = _timeToInvalidation;
            label.FutureTradeCount = _futureTradeCount;

            if (isComplete == false)
            {
                label.Outcome = OrderFlowBarrierOutcome.Incomplete;
            }
            else if (_futureTradeCount == 0)
            {
                label.Outcome = OrderFlowBarrierOutcome.NoFutureTrade;
            }
            else if (_timeToTarget >= 0 && _timeToTarget == _timeToInvalidation)
            {
                label.Outcome = OrderFlowBarrierOutcome.AmbiguousSameTimestamp;
            }
            else if (_timeToTarget >= 0 && (_timeToInvalidation < 0 || _timeToTarget < _timeToInvalidation))
            {
                label.Outcome = OrderFlowBarrierOutcome.TargetFirst;
            }
            else if (_timeToInvalidation >= 0)
            {
                label.Outcome = OrderFlowBarrierOutcome.InvalidationFirst;
            }
            else
            {
                label.Outcome = OrderFlowBarrierOutcome.Neither;
            }

            return label;
        }
    }
}
