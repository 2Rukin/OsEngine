/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace OsEngine.OsData.OrderFlow.Explorer
{
    /// <summary>Frozen offline discovery plan. Changes produce a new identity; display choices never enter it.</summary>
    internal sealed record ExplorerPatternSpec
    {
        internal const string Version = "cloud-pattern-search-1";
        internal const string Grammar = "causal-conditions-1";
        public string Direction { get; init; } = "Both";
        public string Context { get; init; } = "Both";
        public decimal TargetAtr { get; init; } = 1.5m;
        public decimal AdverseAtr { get; init; } = 1;
        public decimal AnchorVolume { get; init; } = 20;
        public ImmutableArray<int> HorizonsMinutes { get; init; } = ImmutableArray.Create(15, 60);
        public int ControlMinutes { get; init; } = 5;
        public int CandidateBudget { get; init; } = 128;
        public int MaximumCards { get; init; } = 12;
        public int MinimumDates { get; init; } = 3;
        public int MinimumWeeks { get; init; } = 2;
        public int MinimumTestDates { get; init; } = 2;
        public int Seed { get; init; } = 1701;
        public string ProfilePreset { get; init; } = "three-causal-scales-1";
        public string Overlap { get; init; } = "earliest-known-global-per-horizon-1";
        public string DayBoundary { get; init; } = "source-date-no-session-inference-1";
        public string Hash(ExplorerRunSpec run) => ExplorerRunSpec.Hash(new { Version, Grammar, run.CatalogSpecHash, run.EpisodeSpecHash,
            run.Study, Plan = this });
        internal void Validate()
        {
            ExplorerValidation.Require(new[] { "Long", "Short", "Both" }.Contains(Direction), "Pattern.Direction", "Цель поиска: Long (рост), Short (снижение) или Both (оба).");
            ExplorerValidation.Require(new[] { "Day", "Week", "Both" }.Contains(Context), "Pattern.Context", "Контекст поиска: Day (сегодня), Week (неделя) или Both (оба).");
            ExplorerValidation.Require(TargetAtr > 0 && AdverseAtr > 0 && AnchorVolume > 0, "Pattern.TargetAtr", "Порог цели, неблагоприятный барьер и объём якоря должны быть положительными.");
            ExplorerValidation.Require(!HorizonsMinutes.IsDefaultOrEmpty && HorizonsMinutes.Length <= 4 && HorizonsMinutes.All(h => h > 0 && h <= 1440) && HorizonsMinutes.Distinct().Count() == HorizonsMinutes.Length,
                "Pattern.HorizonsMinutes", "Для поиска укажите 1–4 разных целых горизонта от 1 до 1440 минут через ;. Конец дня добавляется отдельно.");
            ExplorerValidation.Require(ControlMinutes >= 1 && ControlMinutes <= 60, "Pattern.ControlMinutes", "Шаг контрольных якорей: от 1 до 60 минут.");
            ExplorerValidation.Require(CandidateBudget >= 1 && CandidateBudget <= 512 && MaximumCards >= 1 && MaximumCards <= 32, "Pattern.CandidateBudget", "Бюджет: 1–512 правил; карточек: 1–32.");
            ExplorerValidation.Require(MinimumDates >= 2 && MinimumWeeks >= 1 && MinimumDates <= 1000 && MinimumWeeks <= 100 && MinimumTestDates >= 2 && MinimumTestDates <= 1000,
                "Pattern.MinimumDates", "Поддержка: 2–1000 дат, 1–100 недель; Test: минимум 2–1000 дат.");
            ExplorerValidation.Require(ProfilePreset == "three-causal-scales-1" || ProfilePreset == "expert-1", "Pattern.ProfilePreset", "Неизвестная версия набора профилей поиска.");
            ExplorerValidation.Require(Overlap == "earliest-known-global-per-horizon-1" && DayBoundary == "source-date-no-session-inference-1", "Pattern.Overlap", "Неизвестная политика времени или перекрытий.");
        }
        internal static ExplorerRunSpec Preset(ExplorerRunSpec input) => input with
        {
            Profiles = new[] { ("Narrow", 2, .1m), ("Base", 5, .2m), ("Wide", 10, .4m) }.Select(s => new ExplorerProfile
            { Layer = "Cloud1", Scale = s.Item1, MaximumRangeTicks = s.Item2, AtrFactor = s.Item3, AdaptRange = true, RelativeVolume = true }).ToImmutableArray(),
            Episodes = new ExplorerEpisodeSpec { Enabled = true }, Study = new ExplorerStudySpec { Enabled = false, SwingsEnabled = false }
        };
        internal static DateTime Week(DateTime date) => date.Date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
    }

    /// <summary>One observed source date. Missing calendar dates are absent, never zero-filled sessions.</summary>
    internal sealed record ExplorerPatternDay(DateTime Date, decimal Buy, decimal Sell, decimal Open, decimal Close,
        decimal High, decimal Low, long Ticks, long Clouds, long Episodes);

    /// <summary>Immutable causal anchor. Every value is frozen at KnownSequence; no future label is stored here.</summary>
    internal sealed record ExplorerPatternSnapshot
    {
        public string Id { get; init; }
        public string EventId { get; init; }
        public string Kind { get; init; }
        public string Profile { get; init; }
        public DateTime ObservedAt { get; init; }
        public DateTime KnownAt { get; init; }
        public long KnownSequence { get; init; }
        public DateTime HistoryStart { get; init; }
        public decimal Price { get; init; }
        public decimal? Atr { get; init; }
        public int Activity { get; init; }
        public decimal DayDelta { get; init; }
        public decimal WeekDelta { get; init; }
        public decimal CloudDelta { get; init; }
        public decimal CloudVolume { get; init; }
        public decimal? RelativeVolume { get; init; }
        public decimal? VwapDistance { get; init; }
        public decimal? LowDistance { get; init; }
        public decimal? HighDistance { get; init; }
        public string WeekStatus { get; init; }
        public ImmutableArray<ExplorerPatternDay> WeekDays { get; init; }
        public long DayClouds { get; init; }
        public long DayEpisodes { get; init; }
        public long LastBuySequence { get; init; }
        public long LastSellSequence { get; init; }
        public long LastLowSequence { get; init; }
        public long LastHighSequence { get; init; }
        public DateTime? LastBuyTime { get; init; }
        public DateTime? LastSellTime { get; init; }
        public DateTime? LastLowTime { get; init; }
        public DateTime? LastHighTime { get; init; }
    }
    /// <summary>Same-source-date future path, emitted only when the horizon closes or incompleteness is proven.</summary>
    internal sealed record ExplorerPatternLabel(string SnapshotId, string Direction, int Minutes, DateTime End,
        long EndSequence, string Status, string FirstHit, DateTime? FirstHitAt, long? FirstHitSequence,
        decimal? MfeAtr, decimal? MaeAtr, decimal? Return, string Sampling);
    internal sealed record ExplorerPatternRow(ExplorerPatternSnapshot Snapshot, ExplorerPatternLabel Label);
    internal sealed record ExplorerPatternRun(ExplorerRun Run, ExplorerPatternSpec Plan, string Directory, ExplorerManifest Manifest);
    internal sealed record ExplorerPatternCondition(string Feature, decimal Threshold, int Sign = 1, int WindowMinutes = 0);
    internal sealed record ExplorerPatternRule(string Id, ImmutableArray<ExplorerPatternCondition> Conditions, string Text);
    internal sealed record ExplorerPatternMetrics(long Eligible, long Success, long Control, long ControlSuccess, int Dates, int Weeks,
        decimal? Rate, decimal? ControlRate, decimal? Difference, decimal? WeeklyMin, decimal? WeeklyMax, long Unknown, long Incomplete,
        string SuccessId, string FailureId, string ControlId)
    {
        public int ControlDates { get; init; }
        public int ControlWeeks { get; init; }
        public long BeforeMatchingCases { get; init; }
        public long BeforeMatchingControls { get; init; }
        public long Unmatched { get; init; }
        public long BalanceExcluded { get; init; }
        public long Purged { get; init; }
    }
    internal sealed record ExplorerPatternMembership(string RuleId, string SnapshotId, string Partition, string Direction, int Minutes, string Group, string Status);
    internal sealed record ExplorerPatternCard(string Id, ExplorerPatternRule Rule, string Direction, int Minutes, int FitRank,
        ExplorerPatternMetrics Fit, ExplorerPatternMetrics Test, string Status);
    internal sealed record ExplorerPatternQuality(int Days, int Weeks, long Clouds, long Episodes, long Anchors, long CompleteLabels,
        long CandidateGrid, long Considered, long BudgetExcluded, int Supported, int Tested, DateTime? TestStart,
        Dictionary<string, long> Reasons, string Warning);
}
