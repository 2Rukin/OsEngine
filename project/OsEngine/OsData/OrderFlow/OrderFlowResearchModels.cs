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
        public const string CloudVersion = "tick-cloud-chain-4";
        public const string ParserVersion = "tick-text-1";
        public const string NormalizerVersion = "tick-closed-bucket-3";
        public const string FeatureSchemaVersion = "tick-flow-features-2";
        public const string CandidateDetectorVersion = "flow-price-resilience-1";
        public const string LabelSchemaVersion = "market-path-microseconds-2";
        public const string ArtifactSchemaVersion = "tick-flow-artifacts-7";
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
        Hour4,
        Day1,
        Week1,
        Min2,
        Min3,
        Min20,
        Min45,
        Hour2,
        Hour3,
        Hour6,
        Hour8,
        Hour12,
        Month1
    }

    /// <summary>
    /// Mutable, run-scoped input DTO for one offline tick-text research run.
    /// </summary>
    /// <remarks>
    /// <see cref="Validate"/> normalizes horizons and clears disabled calculation settings. The caller must stop
    /// mutating this instance before passing it to the single-consumer engine.
    /// Values define causal feature and future market-path label semantics; they
    /// do not define orders, fills, PnL or a production trading policy.
    /// Contract: ORDER-FLOW-DATA-001 and ORDER-FLOW-RESEARCH-001.
    /// </remarks>
    internal sealed class OrderFlowResearchRequest
    {
        public bool CalculateDelta { get; set; } = true;
        public bool CalculateCloud { get; set; }
        public bool CalculateCloud2 { get; set; }
        public OrderFlowCloudSettings Cloud2 { get; set; } = new OrderFlowCloudSettings { SingleTicks = true, MinimumTickVolume = 1000 };
        public OrderFlowCloudSettings Cloud { get; set; } = new OrderFlowCloudSettings();

        public string TicksFilePath { get; set; }

        public DateTime? FromDate { get; set; }

        public DateTime? ToDate { get; set; }

        public string OutputRootPath { get; set; }

        public int FeatureWindowSeconds { get; set; }

        public decimal MinimumAbsoluteDelta { get; set; }

        public int MinimumPriceChangeTicks { get; set; }

        public int CandidateCooldownMilliseconds { get; set; }

        public int BackgroundSampleSeconds { get; set; }

        public List<int> LabelHorizonsSeconds { get; set; }

        public int TargetTicks { get; set; }

        public int InvalidationTicks { get; set; }

        public decimal PriceStep { get; set; }

        /// <summary>
        /// Validates required path text and numeric settings and canonicalizes horizons and clears parameters of disabled calculations.
        /// File availability is checked by the engine so input failures retain an audit bundle.
        /// </summary>
        /// <exception cref="ArgumentException">Required text or research settings are invalid.</exception>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(TicksFilePath))
            {
                throw new ArgumentException("Tick text path is required.", nameof(TicksFilePath));
            }

            if (string.IsNullOrWhiteSpace(OutputRootPath))
            {
                throw new ArgumentException("Output folder is required.", nameof(OutputRootPath));
            }

            if (!CalculateDelta && !CalculateCloud && !CalculateCloud2) { throw new ArgumentException("Select Delta, Cloud 1 or Cloud 2."); }
            if (CalculateCloud2) { (Cloud2 ?? throw new ArgumentException("Cloud 2 settings are required.")).Validate(); }
            else { Cloud2 = null; }
            if (CalculateCloud) { (Cloud ?? throw new ArgumentException("Cloud settings are required.")).Validate(); }
            else { Cloud = null; }
            if (CalculateDelta)
            {
                if (FeatureWindowSeconds <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(FeatureWindowSeconds));
                }

                if (MinimumAbsoluteDelta <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(MinimumAbsoluteDelta));
                }

                if (MinimumPriceChangeTicks < 0 ||
                    CandidateCooldownMilliseconds < 0 || BackgroundSampleSeconds <= 0 || TargetTicks <= 0 ||
                    InvalidationTicks <= 0)
                {
                    throw new ArgumentOutOfRangeException("Research settings contain an invalid zero or negative value.");
                }
            }
            else
            {
                FeatureWindowSeconds = MinimumPriceChangeTicks = CandidateCooldownMilliseconds = 0;
                BackgroundSampleSeconds = TargetTicks = InvalidationTicks = 0;
                MinimumAbsoluteDelta = 0;
                LabelHorizonsSeconds = new List<int>();
            }

            if (PriceStep <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(PriceStep), "A positive manual decimal price step is required.");
            }
            if (FromDate.HasValue != ToDate.HasValue || FromDate?.Date > ToDate?.Date)
            {
                throw new ArgumentException("Select both inclusive dates in chronological order, or leave both empty.");
            }
            FromDate = FromDate?.Date;
            ToDate = ToDate?.Date;

            if (CalculateDelta && (LabelHorizonsSeconds == null || LabelHorizonsSeconds.Count == 0))
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
                OrderFlowResearchSchema.ArtifactSchemaVersion,
                OrderFlowResearchSchema.CloudVersion,
                CalculateDelta ? "DELTA" : "NO_DELTA",
                CalculateCloud ? Cloud.CanonicalValue() : "NO_CLOUD",
                CalculateCloud2 ? "CLOUD2:" + Cloud2.CanonicalValue() : "NO_CLOUD2",
                OrderFlowResearchSchema.ParserVersion,
                OrderFlowResearchSchema.NormalizerVersion,
                OrderFlowResearchSchema.FeatureSchemaVersion,
                OrderFlowResearchSchema.CandidateDetectorVersion,
                OrderFlowResearchSchema.LabelSchemaVersion,
                FeatureWindowSeconds.ToString(CultureInfo.InvariantCulture),
                MinimumAbsoluteDelta.ToString("G29", CultureInfo.InvariantCulture),
                MinimumPriceChangeTicks.ToString(CultureInfo.InvariantCulture),
                CandidateCooldownMilliseconds.ToString(CultureInfo.InvariantCulture),
                BackgroundSampleSeconds.ToString(CultureInfo.InvariantCulture),
                horizons,
                TargetTicks.ToString(CultureInfo.InvariantCulture),
                InvalidationTicks.ToString(CultureInfo.InvariantCulture),
                PriceStep.ToString("G29", CultureInfo.InvariantCulture),
                FromDate?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? "ALL",
                ToDate?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? "ALL"
            });
        }
    }

    /// <summary>Provenance of one pinned local tick input; filename identifies the instrument by user convention only.</summary>
    internal sealed class OrderFlowTickInput
    {
        public string FileName { get; set; }
        public string Instrument { get; set; }
        public string Sha256 { get; set; }
        public long? FileSize { get; set; }
        public long RecordCount { get; set; }
        public DateTime? FirstTime { get; set; }
        public DateTime? LastTime { get; set; }
        public bool ReadComplete { get; set; }
        public string FailureReasonCode { get; set; }
    }

    internal sealed class OrderFlowDeal
    {
        public long SourceSequence { get; set; }

        public DateTime Time { get; set; }

        public decimal Price { get; set; }

        public decimal Volume { get; set; }

        public Side Side { get; set; }

    }

    internal sealed class OrderFlowBucket
    {
        public long BucketSequence { get; set; }

        public DateTime Time { get; set; }

        public List<OrderFlowDeal> Deals { get; set; } = new List<OrderFlowDeal>();

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

        public long TimeToMfeMicroseconds { get; set; }

        public long TimeToMaeMicroseconds { get; set; }

        public long TimeToTargetMicroseconds { get; set; }

        public long TimeToInvalidationMicroseconds { get; set; }

        public int FutureTradeCount { get; set; }
    }

    internal sealed class OrderFlowDisplayBar
    {
        /// <summary>Copies all scalar bar fields for a worker-owned preview without changing the live accumulator.</summary>
        internal OrderFlowDisplayBar Copy() { return (OrderFlowDisplayBar)MemberwiseClone(); }

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

        public long BucketCount { get; set; }

        public long DuplicateDealTimestampCount { get; set; }

        public long RejectionIssueCount { get; set; }

        public long WarningIssueCount { get; set; }

        public long SuppressedIssueDetailCount { get; set; }

        public DateTime? FirstEventTime { get; set; }

        public DateTime? LastEventTime { get; set; }

        public DateTime? FirstDealTime { get; set; }

        public DateTime? LastDealTime { get; set; }

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
        public OrderFlowTickInput Input { get; set; }

        public OrderFlowQualityReport Quality { get; set; } = new OrderFlowQualityReport();

        public string InputHash { get; set; }

        public string ResearchSpecHash { get; set; }

        public string NormalizedEventHash { get; set; }

        public string FeatureHash { get; set; }

        public string CandidateHash { get; set; }

        public bool DeltaCalculated { get; set; } = true;
        public bool CloudCalculated { get; set; }
        public bool Cloud2Calculated { get; set; }
        public string Cloud2Hash { get; set; }
        public List<OrderFlowCloud> Clouds2 { get; set; } = new List<OrderFlowCloud>();
        public string CloudHash { get; set; }
        public List<OrderFlowCloud> Clouds { get; set; } = new List<OrderFlowCloud>();

        public string ArtifactDirectory { get; set; }

        public List<OrderFlowObservation> Observations { get; set; } = new List<OrderFlowObservation>();

        public List<OrderFlowCandidate> Candidates { get; set; } = new List<OrderFlowCandidate>();

        public List<OrderFlowMarketPathLabel> Labels { get; set; } = new List<OrderFlowMarketPathLabel>();

        public List<OrderFlowJournalEntry> Journal { get; set; } = new List<OrderFlowJournalEntry>();

        public Dictionary<OrderFlowDisplayTimeFrame, List<OrderFlowDisplayBar>> Bars { get; set; }
            = new Dictionary<OrderFlowDisplayTimeFrame, List<OrderFlowDisplayBar>>();
    }
}
