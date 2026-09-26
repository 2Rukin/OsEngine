/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using System;

namespace OsEngine.OsTrader.AdaptivePositionManager
{
    /// <summary>Campaign direction. Quantity is always an unsigned number of contracts.</summary>
    public enum ApmDirection { Long = 1, Short = -1 }

    /// <summary>Lifecycle independent of market regime; Closing never permits another increase.</summary>
    public enum ApmState { Preflight, Entering, Active, PausedNoIncrease, Reconciling, Closing, FaultedClosing, Completed }

    /// <summary>Asymmetric ordinary-action permissions, never protection permissions.</summary>
    public enum ApmRegime { Normal, FastFavorable, FastAdverse }

    /// <summary>One current action; Cancel targets one intent and does not release its reservation.</summary>
    public enum ApmAction { Wait, InitialEntry, Add, Reduce, Exit, Cancel, Query }

    /// <summary>Unknown and CancelPending remain potentially executable until reconciled.</summary>
    public enum ApmOrderState { Prepared, Working, CancelPending, Unknown, Filled, Canceled, Rejected }

    /// <summary>Proven source capabilities; a trade stream never supplies book OFI.</summary>
    public enum ApmDataProfile { TradeOnly, BookTopOnly, SampledBook, SynchronizedTradesBook }

    /// <summary>
    /// Immutable operator risk locks for one signal on one dedicated tab. Prices and money are decimal.
    /// Times use one explicitly named source timezone; linear valuation requires matching currencies.
    /// </summary>
    /// <remarks>
    /// Validate before sending; boundaries never move with fills. This models reserved stop loss,
    /// not a guarantee of attainable exit price. Contract: APM-PRODUCT-001, APM-MATH-001.
    /// </remarks>
    public sealed record ApmCampaignSpec(
        string CampaignId, string EntrySignalId, string Instrument, string Account,
        ApmDirection Direction, DateTime EntryTime, DateTime SessionExitTime,
        decimal EntryReference, decimal InitialVolume, decimal MaxVolume,
        decimal HardStopPrice, decimal FinalTargetPrice, decimal RiskBudgetCurrency,
        decimal PriceStep, decimal VolumeStep, decimal PriceStepCost,
        string RiskCurrency, string ValuationCurrency, string Timezone,
        decimal StopSlippageReserveTicks = 0, decimal FeePerContract = 0,
        decimal EntrySlippageReserveTicks = 0, string SchemaVersion = "APM-Schema-v1")
    {
        /// <summary>Money per price unit per contract; no implicit FX conversion.</summary>
        public decimal MoneyPerPriceUnit => PriceStepCost / PriceStep;

        /// <summary>Reject invalid locks, unsupported schemas and non-grid quantities before any intent.</summary>
        public void Validate()
        {
            if (SchemaVersion != "APM-Schema-v1" || string.IsNullOrWhiteSpace(CampaignId)
                || string.IsNullOrWhiteSpace(EntrySignalId) || string.IsNullOrWhiteSpace(Instrument)
                || string.IsNullOrWhiteSpace(Account) || string.IsNullOrWhiteSpace(Timezone)
                || string.IsNullOrWhiteSpace(RiskCurrency) || RiskCurrency != ValuationCurrency
                || (Direction != ApmDirection.Long && Direction != ApmDirection.Short)
                || EntryTime == default || SessionExitTime <= EntryTime || SessionExitTime.Date != EntryTime.Date
                || PriceStep <= 0 || VolumeStep <= 0 || PriceStepCost <= 0 || RiskBudgetCurrency <= 0
                || InitialVolume < VolumeStep || MaxVolume < InitialVolume
                || InitialVolume % VolumeStep != 0 || MaxVolume % VolumeStep != 0
                || (int)Direction * (EntryReference - HardStopPrice) <= 0
                || (int)Direction * (FinalTargetPrice - EntryReference) <= 0
                || StopSlippageReserveTicks < 0 || EntrySlippageReserveTicks < 0 || FeePerContract < 0)
                throw new ArgumentException("Invalid APM campaign locks, currency, time or schema.");
        }
    }

    /// <summary>Versioned behavior settings; risk locks belong exclusively to the campaign schedule.</summary>
    public sealed record ApmPolicy
    {
        /// <summary>APM target formula version recorded in every run.</summary>
        public string ModelVersion { get; init; } = "APM-Target-v0.1";
        /// <summary>B0 holds initial inventory until a protection or terminal trigger.</summary>
        public bool ConstantInventory { get; init; }
        /// <summary>Use fixed price scales instead of volatility scales.</summary>
        public bool FixedScales { get; init; } = true;
        /// <summary>Fixed adverse scale in price units.</summary>
        public decimal AddScale { get; init; } = 5;
        /// <summary>Fixed favorable scale in price units.</summary>
        public decimal ReduceScale { get; init; } = 10;
        /// <summary>Minimum adaptive scale in ticks.</summary>
        public decimal MinimumScaleTicks { get; init; } = 1;
        /// <summary>Adverse volatility scale coefficient.</summary>
        public decimal AddVolatilityFactor { get; init; } = 2;
        /// <summary>Favorable volatility scale coefficient.</summary>
        public decimal ReduceVolatilityFactor { get; init; } = 2;
        /// <summary>Directional-speed scale multiplier coefficient.</summary>
        public decimal SpeedScaleFactor { get; init; } = 0.25m;
        /// <summary>Maximum dimensionless speed multiplier.</summary>
        public decimal MaximumScaleMultiplier { get; init; } = 3;
        /// <summary>Dimensionless inventory penalty gamma.</summary>
        public decimal InventoryPenalty { get; init; }
        /// <summary>Risk horizon in seconds, capped by the remaining campaign time.</summary>
        public int RiskHorizonSeconds { get; init; } = 300;
        /// <summary>Minimum ordinary inventory, in contracts, before final target.</summary>
        public decimal MinActiveVolume { get; init; } = 1;
        /// <summary>Minimum accepted target change, in contracts.</summary>
        public decimal MinRebalanceVolume { get; init; } = 1;
        /// <summary>Additional target deadband, in contracts.</summary>
        public decimal VolumeDeadband { get; init; }
        /// <summary>Maximum ordinary child order; protective closes bypass this limit.</summary>
        public decimal MaxChildVolume { get; init; } = 20;
        /// <summary>No-increase zone as a fraction of the fixed anchor-to-stop distance.</summary>
        public decimal NoAddFraction { get; init; } = 0.3m;
        /// <summary>Minimum opposite-action rearm distance, in ticks.</summary>
        public decimal RearmMinTicks { get; init; } = 1;
        /// <summary>Volatility contribution to rearm distance.</summary>
        public decimal RearmVolatilityFactor { get; init; }
        /// <summary>Minimum time from the latest actual action fill, in seconds.</summary>
        public int MinActionIntervalSeconds { get; init; }
        /// <summary>Enables asymmetric speed regimes.</summary>
        public bool FastEnabled { get; init; }
        /// <summary>Favorable entry threshold in normalized displacement units.</summary>
        public decimal FavorableOn { get; init; } = 3;
        /// <summary>Favorable exit threshold, strictly below entry.</summary>
        public decimal FavorableOff { get; init; } = 1;
        /// <summary>Adverse entry threshold magnitude.</summary>
        public decimal AdverseOn { get; init; } = 3;
        /// <summary>Adverse exit threshold magnitude, strictly below entry.</summary>
        public decimal AdverseOff { get; init; } = 1;
        /// <summary>Minimum regime dwell time in seconds.</summary>
        public int MinRegimeHoldSeconds { get; init; } = 5;
        /// <summary>Bounded favorable defer from first suppressed reduction, in seconds.</summary>
        public int MaxFavorableDeferSeconds { get; init; } = 30;
        /// <summary>Event-time sampling interval in seconds.</summary>
        public int GridSeconds { get; init; } = 1;
        /// <summary>Minimum duration of fresh samples before readiness.</summary>
        public int WarmupSeconds { get; init; } = 30;
        /// <summary>Minimum number of price increments for variance initialization.</summary>
        public int MinSamples { get; init; } = 10;
        /// <summary>Maximum last-price age in seconds; stale gaps restart warmup.</summary>
        public int MaxPriceAgeSeconds { get; init; } = 10;
        /// <summary>Fast variance half-life in seconds.</summary>
        public int FastHalfLifeSeconds { get; init; } = 10;
        /// <summary>Slow variance half-life in seconds.</summary>
        public int SlowHalfLifeSeconds { get; init; } = 60;
        /// <summary>Directional displacement window in seconds.</summary>
        public int SpeedWindowSeconds { get; init; } = 10;
        /// <summary>Positive sigma denominator floor, in price per square-root second.</summary>
        public decimal SigmaFloor { get; init; } = 0.000001m;
        /// <summary>Working order expiry in event seconds; cancel does not imply finality.</summary>
        public int OrderLifetimeSeconds { get; init; } = 30;
        /// <summary>Market or one bounded limit child; protection always requests market.</summary>
        public bool UseLimitOrders { get; init; }
        /// <summary>Optional ResearchOnly discrete-AC ordinary pacing. Null (default) keeps plain execution; risk locks and target are unchanged.</summary>
        public ApmAcSettings ResearchExecution { get; init; }

        /// <summary>Validate every behavior input without silently correcting invalid optimizer combinations.</summary>
        public void Validate(ApmCampaignSpec spec)
        {
            spec.Validate();
            if (ResearchExecution != null)
            {
                new ApmAcExecutionPlanner(ResearchExecution);
                if (ResearchExecution.TrainingCutoff >= spec.EntryTime)
                    throw new ArgumentException("Research execution calibration must precede every campaign entry.");
            }
            if (ModelVersion != "APM-Target-v0.1" || AddScale <= 0 || ReduceScale <= 0
                || MinimumScaleTicks <= 0 || AddVolatilityFactor <= 0 || ReduceVolatilityFactor <= 0
                || SpeedScaleFactor < 0 || MaximumScaleMultiplier < 1 || InventoryPenalty < 0
                || RiskHorizonSeconds <= 0 || MinActiveVolume < spec.VolumeStep || MinActiveVolume > spec.InitialVolume
                || MinActiveVolume % spec.VolumeStep != 0 || MinRebalanceVolume < spec.VolumeStep
                || MinRebalanceVolume % spec.VolumeStep != 0 || VolumeDeadband < 0
                || MaxChildVolume < spec.VolumeStep || MaxChildVolume % spec.VolumeStep != 0
                || NoAddFraction <= 0 || NoAddFraction >= 1 || RearmMinTicks < 0 || RearmVolatilityFactor < 0
                || MinActionIntervalSeconds < 0 || FavorableOn <= FavorableOff || FavorableOff < 0
                || AdverseOn <= AdverseOff || AdverseOff < 0 || MinRegimeHoldSeconds < 0
                || MaxFavorableDeferSeconds <= 0 || GridSeconds <= 0 || WarmupSeconds < GridSeconds
                || MinSamples < 1 || MinSamples > 100000 || MaxPriceAgeSeconds < GridSeconds
                || FastHalfLifeSeconds <= 0 || SlowHalfLifeSeconds <= FastHalfLifeSeconds
                || SpeedWindowSeconds < GridSeconds || SpeedWindowSeconds % GridSeconds != 0
                || SpeedWindowSeconds / GridSeconds > 100000 || SigmaFloor <= 0 || OrderLifetimeSeconds <= 0)
                throw new ArgumentException("Invalid APM behavior parameters.");
        }
    }

    /// <summary>Immutable causal market event. Variance is price squared per second; speed is dimensionless.</summary>
    public sealed record ApmMarket(DateTime Time, long Sequence, decimal Price, bool Ready,
        decimal SlowVariance = 0, decimal FastVariance = 0, decimal Speed = 0,
        string Quality = "OK", ApmDataProfile Profile = ApmDataProfile.TradeOnly);

    /// <summary>
    /// Policy explanation record. Callers must treat Reasons as read-only; record copies do not clone its array.
    /// Volume is a proposal, never a booked fill.
    /// </summary>
    public sealed record ApmDecision(long Id, DateTime Time, long Sequence, decimal Price,
        decimal CurveTarget, decimal RawTarget, decimal PolicyTarget, decimal RiskAllowedTarget,
        decimal FilledVolume, decimal PendingIncrease, decimal PendingReduce,
        ApmAction Action, decimal Volume, string IntentId, string[] Reasons,
        ApmRegime Regime, decimal Kappa, decimal AddScale, decimal ReduceScale);

    /// <summary>Copy of one reserved child. Remaining stays reserved through cancellation and unknown status.</summary>
    public sealed record ApmIntent(string Id, long DecisionId, ApmAction Action, decimal Volume,
        decimal PriceBound, DateTime Time, bool IsLimit, ApmOrderState State = ApmOrderState.Prepared,
        decimal Filled = 0, decimal FillNotional = 0, decimal ReportedFilled = 0,
        string BrokerId = "", DateTime LastFillTime = default, decimal? DecisionPrice = null)
    {
        /// <summary>True only while a new cancellation can be dispatched; terminal missing fills are accounting uncertainty.</summary>
        public bool CanCancel => State == ApmOrderState.Prepared || State == ApmOrderState.Working;
        /// <summary>True while the order can fill, or its reported fills have not reached the ledger.</summary>
        public bool Unresolved => State == ApmOrderState.Prepared || State == ApmOrderState.Working
            || State == ApmOrderState.CancelPending || State == ApmOrderState.Unknown || ReportedFilled > Filled;
        /// <summary>Worst-case unaccounted volume; terminal missing fills retain their own reservation.</summary>
        public decimal Remaining => Unresolved ? Math.Max(0,
            (State == ApmOrderState.Filled || State == ApmOrderState.Canceled || State == ApmOrderState.Rejected
                ? ReportedFilled : Volume) - Filled) : 0;
        /// <summary>True for initial entry and ordinary additions to the campaign direction.</summary>
        public bool Increases => Action == ApmAction.InitialEntry || Action == ApmAction.Add;
    }

    /// <summary>One authoritative fill keyed by source/session/instrument/order/trade identity, not price/time.</summary>
    public sealed record ApmFill(string Id, string IntentId, DateTime Time, decimal Price, decimal Volume, decimal Fee);

    /// <summary>
    /// Operator snapshot record with shared Decision/Reasons references. Callers must not mutate nested
    /// arrays; this is a shallow read-only usage contract, not a deeply immutable collection boundary.
    /// </summary>
    public sealed record ApmSnapshot(string CampaignId, ApmState State, bool ExitLatch, string ExitReason,
        decimal Anchor, decimal FilledVolume, decimal AverageEntry, decimal RealizedNet, decimal Fees,
        decimal Equity, decimal MaximumDrawdown, decimal MaximumVolume, decimal Turnover,
        decimal PendingIncrease, decimal PendingReduce, ApmRegime Regime, ApmDecision Decision, ApmMarket Market = null);
}
