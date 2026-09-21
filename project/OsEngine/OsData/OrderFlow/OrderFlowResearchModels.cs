/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using OsEngine.Entity;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OsEngine.OsData.OrderFlow
{
    internal static class OrderFlowResearchSchema
    {
        public const string ParserVersion = "qsh-v4-paired-2";
        public const string NormalizerVersion = "closed-bucket-1";
        public const string FeatureSchemaVersion = "order-flow-features-1";
        public const string CandidateDetectorVersion = "flow-price-resilience-1";
        public const string LabelSchemaVersion = "market-path-1";
        public const string ArtifactSchemaVersion = "order-flow-research-artifacts-2";
    }

    internal enum OrderFlowDirection
    {
        None,
        Long,
        Short
    }

    internal enum OrderFlowObservationType
    {
        Candidate,
        Background,
        DataQuality
    }

    internal enum OrderFlowBarrierOutcome
    {
        Incomplete,
        TargetFirst,
        InvalidationFirst,
        AmbiguousSameTimestamp,
        Neither,
        NoFutureTrade
    }

    internal enum OrderFlowJournalKind
    {
        Information,
        QualityWarning,
        QualityRejection,
        CandidateCreated,
        CandidateSuppressed,
        LabelFinalized
    }

    internal enum OrderFlowDisplayTimeFrame
    {
        Sec15,
        Sec30,
        Min1,
        Min5,
        Min10,
        Min15,
        Min30,
        Min60,
        Hour4
    }

    /// <summary>
    /// Mutable, run-scoped input DTO for one offline paired-QSH research run.
    /// </summary>
    /// <remarks>
    /// <see cref="Validate"/> normalizes the horizon list. The caller must stop
    /// mutating this instance before passing it to the single-consumer engine.
    /// Values define causal feature and future market-path label semantics; they
    /// do not define orders, fills, PnL or a production trading policy.
    /// Contract: ORDER-FLOW-DATA-001 and ORDER-FLOW-RESEARCH-001.
    /// </remarks>
    internal sealed class OrderFlowResearchRequest
    {
        public string DealsFilePath { get; set; }

        public string QuotesFilePath { get; set; }

        public string OutputRootPath { get; set; }

        public int FeatureWindowSeconds { get; set; }

        public decimal MinimumAbsoluteDelta { get; set; }

        public int MinimumPriceChangeTicks { get; set; }

        public int TopBookLevels { get; set; }

        public int MaximumBookAgeMilliseconds { get; set; }

        public int CandidateCooldownMilliseconds { get; set; }

        public int BackgroundSampleSeconds { get; set; }

        public List<int> LabelHorizonsSeconds { get; set; }

        public int TargetTicks { get; set; }

        public int InvalidationTicks { get; set; }

        public decimal PriceStepOverride { get; set; }

        public decimal VolumeStepOverride { get; set; }

        /// <summary>
        /// Validates required path text and numeric settings and canonicalizes label horizons.
        /// File availability is checked by the engine so input failures retain an audit bundle.
        /// </summary>
        /// <exception cref="ArgumentException">Required text or research settings are invalid.</exception>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(DealsFilePath))
            {
                throw new ArgumentException("Deals QSH path is required.", nameof(DealsFilePath));
            }

            if (string.IsNullOrWhiteSpace(QuotesFilePath))
            {
                throw new ArgumentException("Quotes QSH path is required.", nameof(QuotesFilePath));
            }

            if (string.IsNullOrWhiteSpace(OutputRootPath))
            {
                throw new ArgumentException("Output folder is required.", nameof(OutputRootPath));
            }

            if (FeatureWindowSeconds <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(FeatureWindowSeconds));
            }

            if (MinimumAbsoluteDelta <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MinimumAbsoluteDelta));
            }

            if (MinimumPriceChangeTicks < 0 || TopBookLevels <= 0 || MaximumBookAgeMilliseconds <= 0 ||
                CandidateCooldownMilliseconds < 0 || BackgroundSampleSeconds <= 0 || TargetTicks <= 0 ||
                InvalidationTicks <= 0)
            {
                throw new ArgumentOutOfRangeException("Research settings contain an invalid zero or negative value.");
            }

            if (PriceStepOverride < 0 || VolumeStepOverride < 0)
            {
                throw new ArgumentOutOfRangeException("QSH step overrides cannot be negative.");
            }

            if (LabelHorizonsSeconds == null || LabelHorizonsSeconds.Count == 0)
            {
                throw new ArgumentException("At least one future label horizon is required.", nameof(LabelHorizonsSeconds));
            }

            for (int i = 0; i < LabelHorizonsSeconds.Count; i++)
            {
                if (LabelHorizonsSeconds[i] <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(LabelHorizonsSeconds));
                }
            }

            LabelHorizonsSeconds = LabelHorizonsSeconds.Distinct().OrderBy(value => value).ToList();
        }

        public string GetCanonicalValue()
        {
            string horizons = string.Join(",", LabelHorizonsSeconds.OrderBy(value => value));

            return string.Join("|", new string[]
            {
                OrderFlowResearchSchema.ParserVersion,
                OrderFlowResearchSchema.NormalizerVersion,
                OrderFlowResearchSchema.FeatureSchemaVersion,
                OrderFlowResearchSchema.CandidateDetectorVersion,
                OrderFlowResearchSchema.LabelSchemaVersion,
                FeatureWindowSeconds.ToString(CultureInfo.InvariantCulture),
                MinimumAbsoluteDelta.ToString("G29", CultureInfo.InvariantCulture),
                MinimumPriceChangeTicks.ToString(CultureInfo.InvariantCulture),
                TopBookLevels.ToString(CultureInfo.InvariantCulture),
                MaximumBookAgeMilliseconds.ToString(CultureInfo.InvariantCulture),
                CandidateCooldownMilliseconds.ToString(CultureInfo.InvariantCulture),
                BackgroundSampleSeconds.ToString(CultureInfo.InvariantCulture),
                horizons,
                TargetTicks.ToString(CultureInfo.InvariantCulture),
                InvalidationTicks.ToString(CultureInfo.InvariantCulture),
                PriceStepOverride.ToString("G29", CultureInfo.InvariantCulture),
                VolumeStepOverride.ToString("G29", CultureInfo.InvariantCulture)
            });
        }
    }

    internal sealed class OrderFlowQshHeader
    {
        public string FileName { get; set; }

        public string FileType { get; set; }

        public string ApplicationName { get; set; }

        public string Comment { get; set; }

        public string InstrumentHeader { get; set; }

        public string HeaderInstrument { get; set; }

        public string FileInstrument { get; set; }

        public DateTime TradingDate { get; set; }

        public DateTime HeaderTime { get; set; }

        public decimal HeaderPriceStep { get; set; }

        public decimal HeaderVolumeStep { get; set; }

        public decimal EffectivePriceStep { get; set; }

        public decimal EffectiveVolumeStep { get; set; }

        public bool PriceStepOverridden { get; set; }

        public bool VolumeStepOverridden { get; set; }

        public string Sha256 { get; set; }

        /// <summary>Stored byte count, or null when the file could not be opened.</summary>
        public long? FileSize { get; set; }

        /// <summary>True only after all role-specific header checks succeed.</summary>
        public bool HeaderComplete { get; set; }

        /// <summary>Stable input preparation failure code; null after successful header decoding.</summary>
        public string FailureReasonCode { get; set; }
    }

    internal sealed class OrderFlowDeal
    {
        public long SourceSequence { get; set; }

        public DateTime Time { get; set; }

        public DateTime FrameTime { get; set; }

        public decimal Price { get; set; }

        public decimal Volume { get; set; }

        public long PriceTicks { get; set; }

        public long VolumeSteps { get; set; }

        public Side Side { get; set; }

        public string SourceId { get; set; }
    }

    internal sealed class OrderFlowBookLevel
    {
        public decimal Price { get; set; }

        public decimal Volume { get; set; }
    }

    internal sealed class OrderFlowBookSnapshot
    {
        public long SourceSequence { get; set; }

        public DateTime Time { get; set; }

        public List<OrderFlowBookLevel> Bids { get; set; } = new List<OrderFlowBookLevel>();

        public List<OrderFlowBookLevel> Asks { get; set; } = new List<OrderFlowBookLevel>();

        public bool IsValid { get; set; }

        public string QualityCode { get; set; }

        public decimal BestBid
        {
            get { return Bids.Count == 0 ? 0 : Bids[0].Price; }
        }

        public decimal BestAsk
        {
            get { return Asks.Count == 0 ? 0 : Asks[0].Price; }
        }
    }

    internal sealed class OrderFlowBucket
    {
        public long BucketSequence { get; set; }

        public DateTime Time { get; set; }

        public List<OrderFlowDeal> Deals { get; set; } = new List<OrderFlowDeal>();

        public List<OrderFlowBookSnapshot> Quotes { get; set; } = new List<OrderFlowBookSnapshot>();

        public OrderFlowBookSnapshot FinalValidQuote { get; set; }
    }

    internal sealed class OrderFlowFeatureSnapshot
    {
        public string SnapshotId { get; set; }

        public long BucketSequence { get; set; }

        public DateTime Time { get; set; }

        public decimal ReferencePrice { get; set; }

        public decimal BuyVolume { get; set; }

        public decimal SellVolume { get; set; }

        public decimal Delta { get; set; }

        public int TradeCount { get; set; }

        public decimal PriceChange { get; set; }

        public decimal PriceResponse { get; set; }

        public bool BookAvailable { get; set; }

        public bool BookStale { get; set; }

        public DateTime? BookTime { get; set; }

        public long BookAgeMilliseconds { get; set; }

        public decimal Spread { get; set; }

        public decimal BookImbalance { get; set; }

        public string DataQualityCode { get; set; }
    }

    internal sealed class OrderFlowObservation
    {
        public string ObservationKey { get; set; }

        public OrderFlowObservationType ObservationType { get; set; }

        public string CandidateId { get; set; }

        public OrderFlowDirection Direction { get; set; }

        public OrderFlowFeatureSnapshot Features { get; set; }
    }

    internal sealed class OrderFlowCandidate
    {
        public string CandidateId { get; set; }

        public string ObservationKey { get; set; }

        public DateTime Time { get; set; }

        public OrderFlowDirection Direction { get; set; }

        public string ReasonCode { get; set; }

        public string DataQualityCode { get; set; }

        public decimal ReferencePrice { get; set; }

        public string SnapshotId { get; set; }
    }

    internal sealed class OrderFlowMarketPathLabel
    {
        public string CandidateId { get; set; }

        public int HorizonSeconds { get; set; }

        public DateTime CandidateTime { get; set; }

        public DateTime HorizonTime { get; set; }

        public bool IsComplete { get; set; }

        public OrderFlowBarrierOutcome Outcome { get; set; }

        public decimal SignedReturn { get; set; }

        public decimal MaximumFavorableExcursion { get; set; }

        public decimal MaximumAdverseExcursion { get; set; }

        public long TimeToMfeMilliseconds { get; set; }

        public long TimeToMaeMilliseconds { get; set; }

        public long TimeToTargetMilliseconds { get; set; }

        public long TimeToInvalidationMilliseconds { get; set; }

        public int FutureTradeCount { get; set; }
    }

    internal sealed class OrderFlowDisplayBar
    {
        public OrderFlowDisplayTimeFrame TimeFrame { get; set; }

        public DateTime TimeStart { get; set; }

        public DateTime TimeEnd { get; set; }

        public bool HasTrades { get; set; }

        public decimal Open { get; set; }

        public decimal High { get; set; }

        public decimal Low { get; set; }

        public decimal Close { get; set; }

        public decimal Volume { get; set; }

        public decimal Delta { get; set; }

        public decimal PriceResponse { get; set; }

        public decimal BookImbalance { get; set; }

        public long BookAgeMilliseconds { get; set; }

        public bool BookAvailable { get; set; }

        public bool BookStale { get; set; }
    }

    internal sealed class OrderFlowJournalEntry
    {
        public DateTime Time { get; set; }

        public OrderFlowJournalKind Kind { get; set; }

        public string CorrelationId { get; set; }

        public string ReasonCode { get; set; }

        public string Message { get; set; }
    }

    internal sealed class OrderFlowQualityIssue
    {
        public DateTime? Time { get; set; }

        public string ReasonCode { get; set; }

        public bool IsRejection { get; set; }

        public string Message { get; set; }
    }

    internal sealed class OrderFlowQualityReport
    {
        public bool ResearchAccepted { get; set; }

        public bool ExecutionMetadataComplete { get; set; }

        public long DealCount { get; set; }

        public long QuoteCount { get; set; }

        public long ValidBookCount { get; set; }

        public long BucketCount { get; set; }

        public long UnknownSideCount { get; set; }

        public long InvalidDealCount { get; set; }

        public long InvalidBookCount { get; set; }

        public long EmptyBookCount { get; set; }

        public long CrossedBookCount { get; set; }

        public long RegressiveDealTimeCount { get; set; }

        public long RegressiveQuoteTimeCount { get; set; }

        public long DuplicateDealTimestampCount { get; set; }

        public long DuplicateQuoteTimestampCount { get; set; }

        public long MissingBookFeatureCount { get; set; }

        public long StaleBookFeatureCount { get; set; }

        public long BookAgeUpTo100MillisecondsCount { get; set; }

        public long BookAgeUpTo500MillisecondsCount { get; set; }

        public long BookAgeUpTo1000MillisecondsCount { get; set; }

        public long BookAgeAbove1000MillisecondsCount { get; set; }

        public long RejectionIssueCount { get; set; }

        public long WarningIssueCount { get; set; }

        public long SuppressedIssueDetailCount { get; set; }

        public DateTime? FirstEventTime { get; set; }

        public DateTime? LastEventTime { get; set; }

        public DateTime? FirstDealTime { get; set; }

        public DateTime? LastDealTime { get; set; }

        public DateTime? FirstQuoteTime { get; set; }

        public DateTime? LastQuoteTime { get; set; }

        public List<OrderFlowQualityIssue> Issues { get; set; } = new List<OrderFlowQualityIssue>();

        public void AddIssue(DateTime? time, string reasonCode, string message, bool isRejection)
        {
            OrderFlowQualityIssue issue = new OrderFlowQualityIssue();
            issue.Time = time;
            issue.ReasonCode = reasonCode;
            issue.Message = message;
            issue.IsRejection = isRejection;
            Issues.Add(issue);
        }
    }

    /// <summary>
    /// Result of one deterministic offline research run.
    /// </summary>
    /// <remarks>
    /// Market-path labels are held separately from causal observations. This
    /// object contains no order, fill, position or profitability result.
    /// Contract: ORDER-FLOW-RESEARCH-001.
    /// </remarks>
    internal sealed class OrderFlowResearchResult
    {
        public OrderFlowQshHeader DealsHeader { get; set; }

        public OrderFlowQshHeader QuotesHeader { get; set; }

        public OrderFlowQualityReport Quality { get; set; } = new OrderFlowQualityReport();

        public string InputHash { get; set; }

        public string ResearchSpecHash { get; set; }

        public string NormalizedEventHash { get; set; }

        public string FeatureHash { get; set; }

        public string CandidateHash { get; set; }

        public string ArtifactDirectory { get; set; }

        public List<OrderFlowObservation> Observations { get; set; } = new List<OrderFlowObservation>();

        public List<OrderFlowCandidate> Candidates { get; set; } = new List<OrderFlowCandidate>();

        public List<OrderFlowMarketPathLabel> Labels { get; set; } = new List<OrderFlowMarketPathLabel>();

        public List<OrderFlowJournalEntry> Journal { get; set; } = new List<OrderFlowJournalEntry>();

        public Dictionary<OrderFlowDisplayTimeFrame, List<OrderFlowDisplayBar>> Bars { get; set; }
            = new Dictionary<OrderFlowDisplayTimeFrame, List<OrderFlowDisplayBar>>();
    }
}
