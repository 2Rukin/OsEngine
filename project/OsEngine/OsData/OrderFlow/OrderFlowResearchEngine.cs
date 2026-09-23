/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace OsEngine.OsData.OrderFlow
{
    /// <summary>Runs independent delta/price and Cloud calculations on one validated tick stream.</summary>
    /// <remarks>
    /// One caller owns this synchronous offline replay. The entire pinned input is validated in source order;
    /// only ticks inside the inclusive requested dates enter buckets, features, labels and charts.
    /// No warm-up or future-label evidence is taken from excluded dates. Time is source clock time,
    /// without a session calendar, timezone conversion or daily reset. No execution or PnL is created.
    /// Contract: ORDER-FLOW-DATA-001 and ORDER-FLOW-RESEARCH-001.
    /// </remarks>
    internal sealed class OrderFlowResearchEngine
    {
        /// <summary>Validates and replays one local tick file, returning rejected audit results for input failures.</summary>
        /// <param name="request">Run-owned settings, including positive manual price step; caller must stop mutating them.</param>
        /// <param name="cancellationToken">Observed during hashing, line reading and closed-bucket processing.</param>
        /// <returns>Research result with single-input provenance and no trading outcome.</returns>
        /// <exception cref="ArgumentException">Paths, dates or numeric settings are invalid.</exception>
        /// <exception cref="OperationCanceledException">The run is cancelled without publishing a rejected result.</exception>
        public OrderFlowResearchResult Run(OrderFlowResearchRequest request, CancellationToken cancellationToken)
        {
            return Run(request, cancellationToken, null);
        }

        /// <summary>Runs the same calculations with an optional synchronous prefix observer for visual playback.</summary>
        /// <remarks>The worker owns the observer; callbacks may pace/cancel but never mutate the context. No artifacts are written.</remarks>
        internal OrderFlowResearchResult Run(OrderFlowResearchRequest request, CancellationToken cancellationToken,
            IOrderFlowReplayObserver replay)
        {
            if (request == null) { throw new ArgumentNullException(nameof(request)); }
            request.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            OrderFlowResearchResult result = new OrderFlowResearchResult();
            result.DeltaCalculated = request.CalculateDelta;
            result.CloudCalculated = request.CalculateCloud;
            result.Cloud2Calculated = request.CalculateCloud2;
            result.ResearchSpecHash = OrderFlowCanonicalHash.Calculate(request.GetCanonicalValue());
            result.Input = new OrderFlowTickInput
            {
                FileName = Path.GetFileName(request.TicksFilePath).ToUpperInvariant(),
                Instrument = Path.GetFileNameWithoutExtension(request.TicksFilePath).ToUpperInvariant()
            };
            using (OrderFlowCanonicalHash eventHash = new OrderFlowCanonicalHash())
            using (OrderFlowCanonicalHash featureHash = new OrderFlowCanonicalHash())
            using (OrderFlowCanonicalHash candidateHash = new OrderFlowCanonicalHash())
            {
                try
                {
                    using OrderFlowTickReader reader = new OrderFlowTickReader(request.TicksFilePath, result.Input, cancellationToken);
                    replay?.InputReady(result.Input);
                    result.InputHash = InputIdentity(result.Input);
                    AddQualityIssue(result, null, "EXECUTION_METADATA_NOT_COLLECTED",
                        "Manual price step and recorded tick volumes define research units; execution costs and fills are not modelled.", false);
                    ProcessTicks(reader, request, result, eventHash, featureHash, candidateHash, cancellationToken, replay);
                }
                catch (OperationCanceledException) { throw; }
                catch (FileNotFoundException) { RejectInput(result, "TICK_FILE_MISSING", "The tick file does not exist."); }
                catch (DirectoryNotFoundException) { RejectInput(result, "TICK_FILE_MISSING", "The tick file does not exist."); }
                catch (UnauthorizedAccessException) { RejectInput(result, "TICK_ACCESS_DENIED", "Read access to the tick file was denied."); }
                catch (InvalidDataException error) { RejectInput(result, "TICK_INVALID", error.Message); }
                catch (DecoderFallbackException) { RejectInput(result, "TICK_INVALID", "The tick file is not valid UTF-8 text."); }
                catch (IOException) { RejectInput(result, "TICK_IO_ERROR", "The tick file could not be opened or read."); }
                catch (OverflowException) { RejectInput(result, "TICK_NUMERIC_OVERFLOW", "A tick or derived calculation exceeds the decimal range."); }
                catch (ArgumentOutOfRangeException) { RejectInput(result, "TICK_TIME_RANGE", "A research window or horizon exceeds the supported time range."); }
                result.InputHash ??= InputIdentity(result.Input);
                result.NormalizedEventHash = eventHash.Complete();
                result.FeatureHash = featureHash.Complete();
                result.CandidateHash = candidateHash.Complete();
            }
            cancellationToken.ThrowIfCancellationRequested();
            result.CloudHash = HashClouds(result.Clouds, cancellationToken);
            result.Cloud2Hash = HashClouds(result.Clouds2, cancellationToken);
            FinalizeQuality(result);
            SortResult(result);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }

        private static string HashClouds(List<OrderFlowCloud> clouds, CancellationToken cancellationToken)
        {
            using (OrderFlowCanonicalHash cloudHash = new OrderFlowCanonicalHash())
            {
                foreach (OrderFlowCloud cloud in clouds)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    cloudHash.Add(cloud.CloudId, cloud.StartTime, cloud.Time, cloud.CompletedAt, cloud.LastSourceSequence,
                        cloud.CompletionSourceSequence, cloud.CompletionReason, cloud.Price, cloud.Low, cloud.High,
                        cloud.BuyVolume, cloud.SellVolume, cloud.BuyCount, cloud.SellCount, cloud.LargestTick,
                        cloud.FirstSourceSequence, cloud.FirstPrice, cloud.Notional, cloud.PriceStep);
                    cloudHash.Add(cloud.ImbalancePassed, cloud.ImbalanceSource);
                    HashImbalance(cloudHash, cloud.InsideImbalance, cancellationToken);
                    HashImbalance(cloudHash, cloud.ContextImbalance, cancellationToken);
                    cloudHash.Add(cloud.Qualified.Time, cloud.Qualified.SourceSequence, cloud.Qualified.Price,
                        cloud.Qualified.Low, cloud.Qualified.High, cloud.Qualified.Volume, cloud.Qualified.BuyVolume,
                        cloud.Qualified.SellVolume, cloud.Qualified.TradeCount, cloud.Qualified.Vwap);
                }
                return cloudHash.Complete();
            }
        }

        private static void HashImbalance(OrderFlowCanonicalHash hash, OrderFlowImbalanceSnapshot snapshot, CancellationToken cancellationToken)
        {
            hash.Add(snapshot.BuyVolume, snapshot.SellVolume, snapshot.ComparablePairs);
            foreach (OrderFlowDiagonalPair pair in new[] { snapshot.BestBuy, snapshot.BestSell, snapshot.EligibleBuy, snapshot.EligibleSell })
            { hash.Add(pair?.LowerPrice, pair?.Buy, pair?.Sell); }
            foreach (OrderFlowDiagonalPair pair in snapshot.Pairs.Values)
            { cancellationToken.ThrowIfCancellationRequested(); hash.Add(pair.LowerPrice, pair.Buy, pair.Sell); }
        }

        private static string InputIdentity(OrderFlowTickInput input)
        {
            return OrderFlowCanonicalHash.Calculate(OrderFlowResearchSchema.ParserVersion + "|" +
                input.FileName + "|" + (input.Sha256 ?? input.FailureReasonCode));
        }

        private static void RejectInput(OrderFlowResearchResult result, string code, string message)
        {
            result.Input.FailureReasonCode = code;
            AddQualityIssue(result, null, code, message, true);
        }

        private static void ProcessTicks(OrderFlowTickReader reader, OrderFlowResearchRequest request,
            OrderFlowResearchResult result, OrderFlowCanonicalHash eventHash, OrderFlowCanonicalHash featureHash,
            OrderFlowCanonicalHash candidateHash, CancellationToken cancellationToken, IOrderFlowReplayObserver replay)
        {
            OrderFlowRunContext context = new OrderFlowRunContext(request, result, eventHash,
                featureHash, candidateHash, request.PriceStep);
            OrderFlowBucket bucket = null;
            while (reader.TryRead(out OrderFlowDeal tick))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (request.FromDate.HasValue && (tick.Time.Date < request.FromDate.Value || tick.Time.Date > request.ToDate.Value))
                {
                    continue;
                }
                replay?.BeforeTick(tick.Time, cancellationToken);
                context.CloudAccumulator?.Add(tick, cancellationToken);
                context.CloudAccumulator2?.Add(tick, cancellationToken);
                if (bucket == null || bucket.Time != tick.Time)
                {
                    if (bucket != null) { ProcessBucket(context, bucket); }
                    bucket = new OrderFlowBucket { Time = tick.Time, BucketSequence = ++context.BucketSequence };
                }
                else { result.Quality.DuplicateDealTimestampCount++; }
                bucket.Deals.Add(tick);
                result.Quality.DealCount++;
                result.Quality.FirstDealTime ??= tick.Time;
                result.Quality.LastDealTime = tick.Time;
                replay?.TickProcessed(context, bucket, tick);
            }
            if (bucket != null) { ProcessBucket(context, bucket); }
            context.CloudAccumulator?.Complete();
            context.CloudAccumulator2?.Complete();
            context.Labeler?.Complete(result.Quality.LastEventTime ?? DateTime.MinValue);
            result.Bars = context.BarAggregator.Complete();
        }

        private static void ProcessBucket(OrderFlowRunContext context, OrderFlowBucket bucket)
        {
            context.Result.Quality.BucketCount++;
            context.Result.Quality.FirstEventTime ??= bucket.Time;
            context.Result.Quality.LastEventTime = bucket.Time;
            HashBucket(context.EventHash, bucket);
            context.Labeler?.Advance(bucket.Time, bucket.Deals);
            OrderFlowFeatureSnapshot snapshot = context.FeatureWindow?.Build(bucket, context.Request);
            context.BarAggregator.Add(bucket, snapshot);
            if (snapshot != null)
            {
                HashFeature(context.FeatureHash, snapshot);
                ProcessObservation(context, snapshot);
            }
        }

        private static void ProcessObservation(OrderFlowRunContext context, OrderFlowFeatureSnapshot snapshot)
        {
            OrderFlowDirection direction = DetectDirection(snapshot, context.Request,
                context.PriceStep);
            bool createdCandidate = false;

            if (direction != OrderFlowDirection.None)
            {
                DateTime lastCandidateTime = direction == OrderFlowDirection.Long
                    ? context.LastLongCandidateTime
                    : context.LastShortCandidateTime;

                if (lastCandidateTime != DateTime.MinValue &&
                    snapshot.Time < lastCandidateTime.AddMilliseconds(context.Request.CandidateCooldownMilliseconds))
                {
                    OrderFlowJournalEntry suppressed = new OrderFlowJournalEntry();
                    suppressed.Time = snapshot.Time;
                    suppressed.Kind = OrderFlowJournalKind.CandidateSuppressed;
                    suppressed.CorrelationId = snapshot.SnapshotId;
                    suppressed.ReasonCode = "CANDIDATE_COOLDOWN";
                    suppressed.Message = direction + " broad candidate was suppressed by the frozen sampling cooldown.";
                    context.Result.Journal.Add(suppressed);
                }
                else
                {
                    CreateCandidate(context, snapshot, direction);
                    createdCandidate = true;

                    if (direction == OrderFlowDirection.Long)
                    {
                        context.LastLongCandidateTime = snapshot.Time;
                    }
                    else
                    {
                        context.LastShortCandidateTime = snapshot.Time;
                    }
                }
            }

            bool backgroundDue = context.NextBackgroundTime == DateTime.MinValue ||
                snapshot.Time >= context.NextBackgroundTime;

            if (backgroundDue)
            {
                DateTime nextTime = context.NextBackgroundTime == DateTime.MinValue
                    ? snapshot.Time
                    : context.NextBackgroundTime;

                long interval = TimeSpan.FromSeconds(context.Request.BackgroundSampleSeconds).Ticks;
                long steps = (snapshot.Time.Ticks - nextTime.Ticks) / interval + 1;
                nextTime = nextTime.AddTicks(checked(steps * interval));

                context.NextBackgroundTime = nextTime;

                if (createdCandidate == false)
                {
                    OrderFlowObservation observation = new OrderFlowObservation();
                    observation.ObservationKey = CreateObservationKey(context, snapshot,
                        OrderFlowObservationType.Background, OrderFlowDirection.None);
                    observation.ObservationType = OrderFlowObservationType.Background;
                    observation.Direction = OrderFlowDirection.None;
                    observation.Features = snapshot;
                    context.Result.Observations.Add(observation);
                }
            }
        }

        private static OrderFlowDirection DetectDirection(OrderFlowFeatureSnapshot snapshot,
            OrderFlowResearchRequest request, decimal priceStep)
        {
            decimal minimumPriceChange = request.MinimumPriceChangeTicks * priceStep;

            if (snapshot.Delta <= -request.MinimumAbsoluteDelta &&
                snapshot.PriceChange >= minimumPriceChange)
            {
                return OrderFlowDirection.Long;
            }

            if (snapshot.Delta >= request.MinimumAbsoluteDelta &&
                snapshot.PriceChange <= -minimumPriceChange)
            {
                return OrderFlowDirection.Short;
            }

            return OrderFlowDirection.None;
        }

        private static void CreateCandidate(OrderFlowRunContext context, OrderFlowFeatureSnapshot snapshot,
            OrderFlowDirection direction)
        {
            string sideCode = direction == OrderFlowDirection.Long ? "L" : "S";
            string candidateId = "C-" + snapshot.Time.ToString("yyyyMMddHHmmssffffff", CultureInfo.InvariantCulture) +
                "-" + snapshot.BucketSequence.ToString("D10", CultureInfo.InvariantCulture) + "-" + sideCode;

            OrderFlowCandidate candidate = new OrderFlowCandidate();
            candidate.CandidateId = candidateId;
            candidate.Time = snapshot.Time;
            candidate.Direction = direction;
            candidate.ReasonCode = direction == OrderFlowDirection.Long
                ? "SELL_FLOW_PRICE_RESILIENCE"
                : "BUY_FLOW_PRICE_RESILIENCE";
            candidate.DataQualityCode = snapshot.DataQualityCode;
            candidate.ReferencePrice = snapshot.ReferencePrice;
            candidate.SnapshotId = snapshot.SnapshotId;
            candidate.ObservationKey = CreateObservationKey(context, snapshot,
                OrderFlowObservationType.Candidate, direction);

            OrderFlowObservation observation = new OrderFlowObservation();
            observation.ObservationKey = candidate.ObservationKey;
            observation.ObservationType = OrderFlowObservationType.Candidate;
            observation.CandidateId = candidate.CandidateId;
            observation.Direction = direction;
            observation.Features = snapshot;

            context.Result.Candidates.Add(candidate);
            context.Result.Observations.Add(observation);
            context.Labeler.AddCandidate(candidate, context.Request.LabelHorizonsSeconds);
            HashCandidate(context.CandidateHash, candidate, snapshot);

            OrderFlowJournalEntry entry = new OrderFlowJournalEntry();
            entry.Time = candidate.Time;
            entry.Kind = OrderFlowJournalKind.CandidateCreated;
            entry.CorrelationId = candidate.CandidateId;
            entry.ReasonCode = candidate.ReasonCode;
            entry.Message = direction + " broad candidate created. It is an observation, not a trade signal or order.";
            context.Result.Journal.Add(entry);
        }

        private static string CreateObservationKey(OrderFlowRunContext context,
            OrderFlowFeatureSnapshot snapshot, OrderFlowObservationType type, OrderFlowDirection direction)
        {
            return context.Result.InputHash + "|" + context.Result.ResearchSpecHash + "|" +
                snapshot.Time.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "|" +
                snapshot.BucketSequence.ToString(CultureInfo.InvariantCulture) + "|" + type + "|" +
                OrderFlowResearchSchema.CandidateDetectorVersion + "|" + direction;
        }

        private static void HashBucket(OrderFlowCanonicalHash hash, OrderFlowBucket bucket)
        {
            hash.Add("BUCKET", bucket.BucketSequence, bucket.Time, bucket.Deals.Count);
            foreach (OrderFlowDeal deal in bucket.Deals)
            {
                hash.Add("TICK", deal.SourceSequence, deal.Time, deal.Price, deal.Volume, deal.Side);
            }
        }

        private static void HashFeature(OrderFlowCanonicalHash hash, OrderFlowFeatureSnapshot snapshot)
        {
            hash.Add(snapshot.SnapshotId, snapshot.BucketSequence, snapshot.Time, snapshot.ReferencePrice,
                snapshot.BuyVolume, snapshot.SellVolume, snapshot.Delta, snapshot.TradeCount,
                snapshot.PriceChange, snapshot.PriceResponse, snapshot.DataQualityCode);
        }

        private static void HashCandidate(OrderFlowCanonicalHash hash, OrderFlowCandidate candidate,
            OrderFlowFeatureSnapshot snapshot)
        {
            hash.Add(candidate.CandidateId, candidate.ObservationKey, candidate.Time, candidate.Direction,
                candidate.ReasonCode, candidate.DataQualityCode, candidate.ReferencePrice,
                snapshot.Delta, snapshot.PriceChange, snapshot.PriceResponse);
        }

        private static void FinalizeQuality(OrderFlowResearchResult result)
        {
            if (result.Quality.DealCount == 0)
            {
                AddQualityIssue(result, null, "TICKS_EMPTY", "No ticks occur within the selected date range.", true);
            }

            result.Quality.ResearchAccepted = HasRejection(result.Quality) == false;

            OrderFlowJournalEntry finalEntry = new OrderFlowJournalEntry();
            finalEntry.Time = result.Quality.LastEventTime ?? DateTime.MinValue;
            finalEntry.Kind = OrderFlowJournalKind.Information;
            finalEntry.CorrelationId = result.InputHash;
            finalEntry.ReasonCode = result.Quality.ResearchAccepted ? "RESEARCH_ACCEPTED" : "RESEARCH_REJECTED";
            finalEntry.Message = result.Quality.ResearchAccepted
                ? "Tick-only causal research replay completed. No execution or profitability qualification was performed."
                : "Research replay was rejected. Inspect quality reason codes before interpreting candidates.";
            result.Journal.Add(finalEntry);
        }

        private static bool HasRejection(OrderFlowQualityReport quality)
        {
            return quality.RejectionIssueCount > 0;
        }

        private static void AddQualityIssue(OrderFlowResearchResult result, DateTime? time,
            string reasonCode, string message, bool isRejection)
        {
            if (isRejection)
            {
                result.Quality.RejectionIssueCount++;
            }
            else
            {
                result.Quality.WarningIssueCount++;
            }

            if (result.Quality.Issues.Count < 200)
            {
                result.Quality.AddIssue(time, reasonCode, message, isRejection);
            }
            else
            {
                result.Quality.SuppressedIssueDetailCount++;
            }

            if (result.Journal.Count < 10000)
            {
                OrderFlowJournalEntry entry = new OrderFlowJournalEntry();
                entry.Time = time ?? DateTime.MinValue;
                entry.Kind = isRejection
                    ? OrderFlowJournalKind.QualityRejection
                    : OrderFlowJournalKind.QualityWarning;
                entry.ReasonCode = reasonCode;
                entry.Message = message;
                result.Journal.Add(entry);
            }
        }

        private static void SortResult(OrderFlowResearchResult result)
        {
            result.Labels = result.Labels
                .OrderBy(label => label.CandidateTime)
                .ThenBy(label => label.CandidateId, StringComparer.Ordinal)
                .ThenBy(label => label.HorizonSeconds)
                .ToList();

            result.Journal = result.Journal
                .OrderBy(entry => entry.Time)
                .ThenBy(entry => entry.Kind)
                .ThenBy(entry => entry.CorrelationId, StringComparer.Ordinal)
                .ToList();
        }
    }

    internal sealed class OrderFlowRunContext
    {
        public OrderFlowRunContext(OrderFlowResearchRequest request, OrderFlowResearchResult result,
            OrderFlowCanonicalHash eventHash, OrderFlowCanonicalHash featureHash,
            OrderFlowCanonicalHash candidateHash, decimal priceStep)
        {
            Request = request;
            Result = result;
            EventHash = eventHash;
            FeatureHash = featureHash;
            CandidateHash = candidateHash;
            PriceStep = priceStep;
            FeatureWindow = request.CalculateDelta ? new OrderFlowFeatureWindow() : null;
            CloudAccumulator = request.CalculateCloud ? new OrderFlowCloudAccumulator(request.Cloud, priceStep, result.Clouds) : null;
            CloudAccumulator2 = request.CalculateCloud2 ? new OrderFlowCloudAccumulator(request.Cloud2, priceStep, result.Clouds2, "CL2-") : null;
            BarAggregator = new OrderFlowDisplayBarAggregator();
            Labeler = request.CalculateDelta ? new OrderFlowMarketPathLabeler(result.Labels, result.Journal,
                priceStep, request.TargetTicks, request.InvalidationTicks) : null;
        }

        public OrderFlowResearchRequest Request { get; private set; }

        public OrderFlowResearchResult Result { get; private set; }

        public OrderFlowCanonicalHash EventHash { get; private set; }

        public OrderFlowCanonicalHash FeatureHash { get; private set; }

        public OrderFlowCanonicalHash CandidateHash { get; private set; }

        public decimal PriceStep { get; private set; }

        public OrderFlowCloudAccumulator CloudAccumulator { get; private set; }
        public OrderFlowCloudAccumulator CloudAccumulator2 { get; private set; }

        public OrderFlowFeatureWindow FeatureWindow { get; private set; }

        public OrderFlowDisplayBarAggregator BarAggregator { get; private set; }

        public OrderFlowMarketPathLabeler Labeler { get; private set; }

        public long BucketSequence { get; set; }

        public DateTime LastLongCandidateTime { get; set; }

        public DateTime LastShortCandidateTime { get; set; }

        public DateTime NextBackgroundTime { get; set; }
    }
}
