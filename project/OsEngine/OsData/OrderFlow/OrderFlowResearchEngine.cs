/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using OsEngine.Entity;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace OsEngine.OsData.OrderFlow
{
    /// <summary>
    /// Runs a deterministic, closed-bucket paired-QSH research replay.
    /// </summary>
    /// <remarks>
    /// The engine is single-consumer and offline. It publishes candidates and
    /// future market-path labels but never creates orders, fills, positions or
    /// PnL. A quote from the current timestamp bucket becomes available only to
    /// later buckets. Contract: ORDER-FLOW-DATA-001 and ORDER-FLOW-RESEARCH-001.
    /// </remarks>
    internal sealed class OrderFlowResearchEngine
    {
        /// <summary>
        /// Validates and replays one local Deals/Quotes pair into causal
        /// observations and separately computed future market-path labels.
        /// </summary>
        /// <param name="request">Run-scoped request that must no longer be mutated by its caller.</param>
        /// <param name="cancellationToken">Cancellation observed between closed buckets.</param>
        /// <returns>A deterministic research result; malformed QSH is represented by rejection reason codes where decoding can be entered safely.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="request"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException">Paths or numeric settings fail request validation.</exception>
        /// <exception cref="OperationCanceledException">Cancellation is requested.</exception>
        /// <exception cref="IOException">Input hashing fails before guarded decoding starts.</exception>
        /// <remarks>No order, fill, position, execution simulation or PnL is created.</remarks>
        public OrderFlowResearchResult Run(OrderFlowResearchRequest request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            request.Validate();

            OrderFlowResearchResult result = new OrderFlowResearchResult();
            result.ResearchSpecHash = OrderFlowCanonicalHash.Calculate(request.GetCanonicalValue());
            string dealsFileHash = OrderFlowFileHash.Calculate(request.DealsFilePath);
            string quotesFileHash = OrderFlowFileHash.Calculate(request.QuotesFilePath);
            string dealsFileName = Path.GetFileName(request.DealsFilePath).ToUpperInvariant();
            string quotesFileName = Path.GetFileName(request.QuotesFilePath).ToUpperInvariant();
            result.InputHash = OrderFlowCanonicalHash.Calculate(
                "DEALS|" + dealsFileName + "|" + dealsFileHash +
                "|QUOTES|" + quotesFileName + "|" + quotesFileHash);

            using (OrderFlowCanonicalHash eventHash = new OrderFlowCanonicalHash())
            using (OrderFlowCanonicalHash featureHash = new OrderFlowCanonicalHash())
            using (OrderFlowCanonicalHash candidateHash = new OrderFlowCanonicalHash())
            {
                try
                {
                    using (OrderFlowDealsQshReader dealsReader = new OrderFlowDealsQshReader(
                        request.DealsFilePath, request.PriceStepOverride, request.VolumeStepOverride,
                        dealsFileHash))
                    using (OrderFlowQuotesQshReader quotesReader = new OrderFlowQuotesQshReader(
                        request.QuotesFilePath, request.PriceStepOverride, request.VolumeStepOverride,
                        quotesFileHash))
                    {
                        result.DealsHeader = dealsReader.Header;
                        result.QuotesHeader = quotesReader.Header;
                        ValidatePair(result);

                        if (HasRejection(result.Quality) == false)
                        {
                            ProcessPair(dealsReader, quotesReader, request, result,
                                eventHash, featureHash, candidateHash, cancellationToken);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (InvalidDataException error)
                {
                    AddQualityIssue(result, null, "QSH_INVALID", error.Message, true);
                }
                catch (IOException error)
                {
                    AddQualityIssue(result, null, "QSH_IO_ERROR", error.Message, true);
                }
                catch (OverflowException error)
                {
                    AddQualityIssue(result, null, "QSH_NUMERIC_OVERFLOW", error.Message, true);
                }

                result.NormalizedEventHash = eventHash.Complete();
                result.FeatureHash = featureHash.Complete();
                result.CandidateHash = candidateHash.Complete();
            }

            FinalizeQuality(result);
            SortResult(result);
            return result;
        }

        private static void ValidatePair(OrderFlowResearchResult result)
        {
            OrderFlowQshHeader deals = result.DealsHeader;
            OrderFlowQshHeader quotes = result.QuotesHeader;

            if (string.Equals(deals.FileInstrument, quotes.FileInstrument,
                StringComparison.OrdinalIgnoreCase) == false)
            {
                AddQualityIssue(result, null, "PAIR_FILE_INSTRUMENT_MISMATCH",
                    "Deals and Quotes file names identify different instruments.", true);
            }

            if (deals.TradingDate != quotes.TradingDate)
            {
                AddQualityIssue(result, null, "PAIR_TRADING_DATE_MISMATCH",
                    "Deals and Quotes file names identify different trading dates.", true);
            }

            if (string.Equals(deals.InstrumentHeader, quotes.InstrumentHeader,
                StringComparison.Ordinal) == false)
            {
                AddQualityIssue(result, null, "PAIR_HEADER_INSTRUMENT_MISMATCH",
                    "Deals and Quotes QSH headers identify different instruments.", true);
            }

            if (string.IsNullOrWhiteSpace(deals.HeaderInstrument) ||
                string.IsNullOrWhiteSpace(quotes.HeaderInstrument) ||
                string.Equals(deals.FileInstrument, deals.HeaderInstrument,
                    StringComparison.OrdinalIgnoreCase) == false ||
                string.Equals(quotes.FileInstrument, quotes.HeaderInstrument,
                    StringComparison.OrdinalIgnoreCase) == false)
            {
                AddQualityIssue(result, null, "PAIR_FILE_HEADER_INSTRUMENT_MISMATCH",
                    "A QSH file name instrument does not match the instrument encoded in its header.", true);
            }

            if (deals.EffectivePriceStep != quotes.EffectivePriceStep ||
                deals.EffectiveVolumeStep != quotes.EffectiveVolumeStep)
            {
                AddQualityIssue(result, null, "PAIR_STEP_MISMATCH",
                    "Deals and Quotes use different effective price or volume steps.", true);
            }

            if (deals.PriceStepOverridden || quotes.PriceStepOverridden ||
                deals.VolumeStepOverridden || quotes.VolumeStepOverridden)
            {
                AddQualityIssue(result, null, "QSH_STEP_OVERRIDE",
                    "At least one QSH price or volume step is supplied by the user override and must be independently verified.", false);
            }

            result.Quality.ExecutionMetadataComplete = false;
            AddQualityIssue(result, null, "EXECUTION_METADATA_NOT_COLLECTED",
                "The research MVP does not collect lot, price-step cost, commission, session or execution profiles. No PnL claim is available.", false);
        }

        private static void ProcessPair(OrderFlowDealsQshReader dealsReader, OrderFlowQuotesQshReader quotesReader,
            OrderFlowResearchRequest request, OrderFlowResearchResult result, OrderFlowCanonicalHash eventHash,
            OrderFlowCanonicalHash featureHash, OrderFlowCanonicalHash candidateHash,
            CancellationToken cancellationToken)
        {
            OrderFlowRunContext context = new OrderFlowRunContext(request, result, eventHash,
                featureHash, candidateHash, dealsReader.Header.EffectivePriceStep);

            OrderFlowDeal nextDeal;
            OrderFlowBookSnapshot nextQuote;
            bool hasDeal = dealsReader.TryRead(out nextDeal);
            bool hasQuote = quotesReader.TryRead(out nextQuote);
            DateTime lastDealReadTime = DateTime.MinValue;
            DateTime lastQuoteReadTime = DateTime.MinValue;
            bool fatalOrderingError = false;

            while ((hasDeal || hasQuote) && fatalOrderingError == false)
            {
                cancellationToken.ThrowIfCancellationRequested();

                DateTime bucketTime;
                if (hasDeal && hasQuote)
                {
                    bucketTime = nextDeal.Time <= nextQuote.Time ? nextDeal.Time : nextQuote.Time;
                }
                else
                {
                    bucketTime = hasDeal ? nextDeal.Time : nextQuote.Time;
                }

                OrderFlowBucket bucket = new OrderFlowBucket();
                bucket.BucketSequence = ++context.BucketSequence;
                bucket.Time = bucketTime;

                while (hasDeal && nextDeal.Time == bucketTime)
                {
                    bucket.Deals.Add(nextDeal);
                    result.Quality.DealCount++;
                    UpdateDealTimeRange(result.Quality, nextDeal.Time);

                    if (lastDealReadTime == nextDeal.Time)
                    {
                        result.Quality.DuplicateDealTimestampCount++;
                    }

                    lastDealReadTime = nextDeal.Time;
                    OrderFlowDeal consumedDeal = nextDeal;
                    hasDeal = dealsReader.TryRead(out nextDeal);

                    if (hasDeal && nextDeal.Time < consumedDeal.Time)
                    {
                        result.Quality.RegressiveDealTimeCount++;
                        AddQualityIssue(result, nextDeal.Time, "DEAL_TIME_REGRESSION",
                            "Deals source time moves backwards in file order.", true);
                        fatalOrderingError = true;
                        break;
                    }
                }

                if (fatalOrderingError)
                {
                    break;
                }

                while (hasQuote && nextQuote.Time == bucketTime)
                {
                    bucket.Quotes.Add(nextQuote);
                    result.Quality.QuoteCount++;
                    UpdateQuoteTimeRange(result.Quality, nextQuote.Time);

                    if (lastQuoteReadTime == nextQuote.Time)
                    {
                        result.Quality.DuplicateQuoteTimestampCount++;
                    }

                    lastQuoteReadTime = nextQuote.Time;
                    if (nextQuote.IsValid)
                    {
                        result.Quality.ValidBookCount++;
                        bucket.FinalValidQuote = nextQuote;
                    }

                    OrderFlowBookSnapshot consumedQuote = nextQuote;
                    hasQuote = quotesReader.TryRead(out nextQuote);

                    if (hasQuote && nextQuote.Time < consumedQuote.Time)
                    {
                        result.Quality.RegressiveQuoteTimeCount++;
                        AddQualityIssue(result, nextQuote.Time, "QUOTE_TIME_REGRESSION",
                            "Quotes source time moves backwards in file order.", true);
                        fatalOrderingError = true;
                        break;
                    }
                }

                if (fatalOrderingError)
                {
                    break;
                }

                ProcessBucket(context, bucket);
            }

            DateTime lastEventTime = result.Quality.LastEventTime ?? DateTime.MinValue;
            context.Labeler.Complete(lastEventTime);
            result.Bars = context.BarAggregator.Complete();
        }

        private static void ProcessBucket(OrderFlowRunContext context, OrderFlowBucket bucket)
        {
            OrderFlowResearchResult result = context.Result;
            result.Quality.BucketCount++;
            UpdateTimeRange(result.Quality, bucket.Time);
            HashBucket(context.EventHash, bucket);

            List<OrderFlowDeal> validDeals = new List<OrderFlowDeal>();
            for (int i = 0; i < bucket.Deals.Count; i++)
            {
                OrderFlowDeal deal = bucket.Deals[i];
                if (deal.Side != Side.Buy && deal.Side != Side.Sell)
                {
                    result.Quality.UnknownSideCount++;
                    result.Quality.InvalidDealCount++;
                    AddQualityIssue(result, deal.Time, "DEAL_SIDE_UNKNOWN",
                        "A deal has no source Buy/Sell side and is excluded from causal features.", true);
                    continue;
                }

                if (deal.Price <= 0 || deal.Volume <= 0)
                {
                    result.Quality.InvalidDealCount++;
                    AddQualityIssue(result, deal.Time, "DEAL_VALUE_INVALID",
                        "A deal has a non-positive price or volume and is excluded from causal features.", true);
                    continue;
                }

                validDeals.Add(deal);
            }

            for (int i = 0; i < bucket.Quotes.Count; i++)
            {
                OrderFlowBookSnapshot quote = bucket.Quotes[i];
                if (quote.IsValid)
                {
                    continue;
                }

                result.Quality.InvalidBookCount++;
                if (quote.QualityCode == "BOOK_EMPTY")
                {
                    result.Quality.EmptyBookCount++;
                }
                else if (quote.QualityCode == "BOOK_CROSSED_OR_LOCKED")
                {
                    result.Quality.CrossedBookCount++;
                }

                AddQualityIssue(result, quote.Time, quote.QualityCode,
                    "An invalid quote snapshot is ignored; the last valid earlier book is not silently refreshed.", false);
            }

            context.Labeler.Advance(bucket.Time, validDeals);

            OrderFlowFeatureSnapshot snapshot = null;
            if (validDeals.Count > 0)
            {
                OrderFlowBucket validBucket = new OrderFlowBucket();
                validBucket.BucketSequence = bucket.BucketSequence;
                validBucket.Time = bucket.Time;
                validBucket.Deals = validDeals;
                snapshot = context.FeatureWindow.Build(validBucket, context.PreviousClosedBook, context.Request);

                if (snapshot != null)
                {
                    UpdateBookQualityCounters(result.Quality, snapshot);
                    HashFeature(context.FeatureHash, snapshot);
                    context.BarAggregator.Add(validBucket, snapshot);
                    ProcessObservation(context, snapshot);
                }
            }

            if (bucket.FinalValidQuote != null)
            {
                context.PreviousClosedBook = bucket.FinalValidQuote;
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

                while (nextTime <= snapshot.Time)
                {
                    nextTime = nextTime.AddSeconds(context.Request.BackgroundSampleSeconds);
                }

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
            string candidateId = "C-" + snapshot.Time.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture) +
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
            string inputPrefix = string.IsNullOrEmpty(context.Result.InputHash)
                ? "no-input-hash"
                : context.Result.InputHash.Substring(0, 16);

            return inputPrefix + "|" + snapshot.Time.ToString("yyyyMMdd", CultureInfo.InvariantCulture) +
                "|" + snapshot.BucketSequence.ToString(CultureInfo.InvariantCulture) + "|" + type + "|" + direction;
        }

        private static void HashBucket(OrderFlowCanonicalHash hash, OrderFlowBucket bucket)
        {
            hash.Add("BUCKET", bucket.BucketSequence, bucket.Time, bucket.Deals.Count, bucket.Quotes.Count);

            for (int i = 0; i < bucket.Deals.Count; i++)
            {
                OrderFlowDeal deal = bucket.Deals[i];
                hash.Add("DEAL", deal.SourceSequence, deal.Time, deal.FrameTime, deal.PriceTicks,
                    deal.VolumeSteps, deal.Side, deal.SourceId);
            }

            for (int i = 0; i < bucket.Quotes.Count; i++)
            {
                OrderFlowBookSnapshot quote = bucket.Quotes[i];
                hash.Add("QUOTE", quote.SourceSequence, quote.Time, quote.IsValid, quote.QualityCode,
                    quote.Bids.Count, quote.Asks.Count);

                for (int levelIndex = 0; levelIndex < quote.Bids.Count; levelIndex++)
                {
                    OrderFlowBookLevel level = quote.Bids[levelIndex];
                    hash.Add("BID", levelIndex, level.Price, level.Volume);
                }

                for (int levelIndex = 0; levelIndex < quote.Asks.Count; levelIndex++)
                {
                    OrderFlowBookLevel level = quote.Asks[levelIndex];
                    hash.Add("ASK", levelIndex, level.Price, level.Volume);
                }
            }
        }

        private static void HashFeature(OrderFlowCanonicalHash hash, OrderFlowFeatureSnapshot snapshot)
        {
            hash.Add(snapshot.SnapshotId, snapshot.BucketSequence, snapshot.Time, snapshot.ReferencePrice,
                snapshot.BuyVolume, snapshot.SellVolume, snapshot.Delta, snapshot.TradeCount,
                snapshot.PriceChange, snapshot.PriceResponse, snapshot.BookAvailable, snapshot.BookStale,
                snapshot.BookTime, snapshot.BookAgeMilliseconds, snapshot.Spread,
                snapshot.BookImbalance, snapshot.DataQualityCode);
        }

        private static void HashCandidate(OrderFlowCanonicalHash hash, OrderFlowCandidate candidate,
            OrderFlowFeatureSnapshot snapshot)
        {
            hash.Add(candidate.CandidateId, candidate.ObservationKey, candidate.Time, candidate.Direction,
                candidate.ReasonCode, candidate.DataQualityCode, candidate.ReferencePrice,
                snapshot.Delta, snapshot.PriceChange, snapshot.PriceResponse, snapshot.Spread,
                snapshot.BookImbalance, snapshot.BookAgeMilliseconds);
        }

        private static void UpdateBookQualityCounters(OrderFlowQualityReport quality,
            OrderFlowFeatureSnapshot snapshot)
        {
            if (snapshot.BookAvailable == false)
            {
                quality.MissingBookFeatureCount++;
                return;
            }

            if (snapshot.BookStale)
            {
                quality.StaleBookFeatureCount++;
            }

            if (snapshot.BookAgeMilliseconds <= 100)
            {
                quality.BookAgeUpTo100MillisecondsCount++;
            }
            else if (snapshot.BookAgeMilliseconds <= 500)
            {
                quality.BookAgeUpTo500MillisecondsCount++;
            }
            else if (snapshot.BookAgeMilliseconds <= 1000)
            {
                quality.BookAgeUpTo1000MillisecondsCount++;
            }
            else
            {
                quality.BookAgeAbove1000MillisecondsCount++;
            }
        }

        private static void UpdateTimeRange(OrderFlowQualityReport quality, DateTime time)
        {
            if (quality.FirstEventTime.HasValue == false || time < quality.FirstEventTime.Value)
            {
                quality.FirstEventTime = time;
            }

            if (quality.LastEventTime.HasValue == false || time > quality.LastEventTime.Value)
            {
                quality.LastEventTime = time;
            }
        }

        private static void UpdateDealTimeRange(OrderFlowQualityReport quality, DateTime time)
        {
            if (quality.FirstDealTime.HasValue == false || time < quality.FirstDealTime.Value)
            {
                quality.FirstDealTime = time;
            }

            if (quality.LastDealTime.HasValue == false || time > quality.LastDealTime.Value)
            {
                quality.LastDealTime = time;
            }
        }

        private static void UpdateQuoteTimeRange(OrderFlowQualityReport quality, DateTime time)
        {
            if (quality.FirstQuoteTime.HasValue == false || time < quality.FirstQuoteTime.Value)
            {
                quality.FirstQuoteTime = time;
            }

            if (quality.LastQuoteTime.HasValue == false || time > quality.LastQuoteTime.Value)
            {
                quality.LastQuoteTime = time;
            }
        }

        private static void FinalizeQuality(OrderFlowResearchResult result)
        {
            if (result.Quality.DealCount == 0)
            {
                AddQualityIssue(result, null, "DEALS_EMPTY", "No Deals records were decoded.", true);
            }

            if (result.Quality.QuoteCount == 0)
            {
                AddQualityIssue(result, null, "QUOTES_EMPTY", "No Quotes records were decoded.", true);
            }

            if (result.Quality.QuoteCount > 0 && result.Quality.ValidBookCount == 0)
            {
                AddQualityIssue(result, null, "QUOTES_NO_VALID_BOOK",
                    "No valid two-sided quote snapshot was decoded.", true);
            }

            if (result.Quality.FirstDealTime.HasValue && result.Quality.LastDealTime.HasValue &&
                result.Quality.FirstQuoteTime.HasValue && result.Quality.LastQuoteTime.HasValue &&
                (result.Quality.LastDealTime.Value < result.Quality.FirstQuoteTime.Value ||
                 result.Quality.LastQuoteTime.Value < result.Quality.FirstDealTime.Value))
            {
                AddQualityIssue(result, null, "PAIR_TIME_RANGES_DISJOINT",
                    "Deals and Quotes source time ranges do not overlap; session metadata is not available to reconcile them.", true);
            }

            result.Quality.ResearchAccepted = HasRejection(result.Quality) == false;

            OrderFlowJournalEntry finalEntry = new OrderFlowJournalEntry();
            finalEntry.Time = result.Quality.LastEventTime ?? DateTime.MinValue;
            finalEntry.Kind = OrderFlowJournalKind.Information;
            finalEntry.CorrelationId = result.InputHash;
            finalEntry.ReasonCode = result.Quality.ResearchAccepted ? "RESEARCH_ACCEPTED" : "RESEARCH_REJECTED";
            finalEntry.Message = result.Quality.ResearchAccepted
                ? "Paired causal research replay completed. No execution or profitability qualification was performed."
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
            FeatureWindow = new OrderFlowFeatureWindow();
            BarAggregator = new OrderFlowDisplayBarAggregator();
            Labeler = new OrderFlowMarketPathLabeler(result.Labels, result.Journal,
                priceStep, request.TargetTicks, request.InvalidationTicks);
        }

        public OrderFlowResearchRequest Request { get; private set; }

        public OrderFlowResearchResult Result { get; private set; }

        public OrderFlowCanonicalHash EventHash { get; private set; }

        public OrderFlowCanonicalHash FeatureHash { get; private set; }

        public OrderFlowCanonicalHash CandidateHash { get; private set; }

        public decimal PriceStep { get; private set; }

        public OrderFlowFeatureWindow FeatureWindow { get; private set; }

        public OrderFlowDisplayBarAggregator BarAggregator { get; private set; }

        public OrderFlowMarketPathLabeler Labeler { get; private set; }

        public OrderFlowBookSnapshot PreviousClosedBook { get; set; }

        public long BucketSequence { get; set; }

        public DateTime LastLongCandidateTime { get; set; }

        public DateTime LastShortCandidateTime { get; set; }

        public DateTime NextBackgroundTime { get; set; }
    }
}
