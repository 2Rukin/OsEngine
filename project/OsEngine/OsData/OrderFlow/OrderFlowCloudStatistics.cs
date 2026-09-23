/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;

namespace OsEngine.OsData.OrderFlow
{
    /// <summary>Fixed, run-owned definition of the exploratory Cloud reaction study; no trading or execution model.</summary>
    internal sealed class OrderFlowCloudStatisticsSettings
    {
        public const string Version = "cloud-reaction-study-1";
        public int AtrPeriod { get; set; } = 20;
        public decimal TargetAtr { get; set; } = 1.5m;
        public decimal AdverseAtr { get; set; } = 1;
        public int[] HorizonsMinutes { get; set; } = new[] { 3, 9, 18 };
        public int FitPercent { get; set; } = 70;
        public int MinimumSamples { get; set; } = 30;

        public OrderFlowCloudStatisticsSettings CopyValidated()
        {
            if (AtrPeriod < 1 || AtrPeriod > 1000 || TargetAtr <= 0 || AdverseAtr <= 0 || FitPercent < 10 || FitPercent > 90 || MinimumSamples < 1 ||
                HorizonsMinutes == null || HorizonsMinutes.Length == 0 || HorizonsMinutes.Length > 8 || HorizonsMinutes.Any(value => value < 1 || value > 1440))
            { throw new ArgumentException("Statistics require ATR period1..1000, positive barriers, 1..8 horizons of1..1440 minutes, fit10..90%, minimum samples>=1."); }
            return new OrderFlowCloudStatisticsSettings { AtrPeriod = AtrPeriod, TargetAtr = TargetAtr, AdverseAtr = AdverseAtr,
                HorizonsMinutes = HorizonsMinutes.Distinct().OrderBy(value => value).ToArray(), FitPercent = FitPercent, MinimumSamples = MinimumSamples };
        }

        public string CanonicalValue() => string.Join("|", Version, AtrPeriod, TargetAtr.ToString("G29", CultureInfo.InvariantCulture),
            AdverseAtr.ToString("G29", CultureInfo.InvariantCulture), string.Join(",", HorizonsMinutes), FitPercent, MinimumSamples);
    }

    internal enum OrderFlowCloudSamplePart { Fit, Test, Purged }
    internal enum OrderFlowCloudReactionOutcome { Timeout, TargetFirst, AdverseFirst }

    /// <summary>A completed Cloud event with causal ATR and source ordinal; outcomes remain separate from its known-at-completion features.</summary>
    internal sealed class OrderFlowCloudStudyEvent
    {
        [JsonIgnore] public OrderFlowCloud Cloud { get; init; }
        public string CloudId => Cloud.CloudId;
        public int Layer { get; init; }
        public DateTime Time => Cloud.CompletedAt.Value;
        public long SourceSequence => Cloud.CompletionSourceSequence.Value;
        public decimal ReferencePrice { get; set; }
        public decimal Atr { get; init; }
        public DateTime FeatureStart { get; init; }
        public OrderFlowCloudSamplePart Part { get; init; }
        public bool Sampled { get; init; }
        public int ContinuationSide => Cloud.Delta > 0 ? 1 : -1;
        public List<OrderFlowCloudReaction> Reactions { get; } = new List<OrderFlowCloudReaction>();
        public bool Complete => Sampled && Reactions.Count > 0 && Reactions.All(item => item.IsComplete && item.FutureTrades > 0);
    }

    /// <summary>Observed tick-price path after completion, not a position/fill result. First-touch ordinal resolves equal timestamps.</summary>
    internal sealed class OrderFlowCloudReaction
    {
        public int HorizonMinutes { get; init; }
        public bool Reversal { get; init; }
        public bool IsComplete { get; set; }
        public long FutureTrades { get; set; }
        public OrderFlowCloudReactionOutcome Outcome { get; set; }
        public long? FirstTouchSequence { get; set; }
        public decimal? FirstTouchSeconds { get; set; }
        public decimal MfeAtr { get; set; }
        public decimal MaeAtr { get; set; }
        public decimal EndReturnAtr { get; set; }
    }

    /// <summary>A predeclared single-coordinate rule. The held-out sample is never used to select or retune it.</summary>
    internal sealed class OrderFlowCloudStudyRule
    {
        public string Id { get; init; }
        public OrderFlowImbalanceSource Source { get; init; }
        public decimal Ratio { get; init; } = 100;
        public decimal Volume { get; init; }
        public decimal Difference { get; init; }
        public decimal Delta { get; init; }
        public int MaximumCount { get; init; }
        public OrderFlowCloudFilter CreateFilter() => new OrderFlowCloudFilter(new OrderFlowImbalanceSettings {
            Source = Source, MinimumRatioPercent = Ratio, MinimumDominantVolume = Volume, MinimumDifference = Difference, MinimumDeltaPercent = Delta }, 0, MaximumCount);
    }

    /// <summary>Descriptive rates and excursions on one fixed cohort; Wilson bound is an approximate ranking aid, not post-selection significance.</summary>
    internal sealed class OrderFlowCloudStudyMetrics
    {
        public int Count { get; init; }
        public int Wins { get; init; }
        public double WinPercent => Count == 0 ? 0 : 100d * Wins / Count;
        public double LowerPercent => Count == 0 ? 0 : 100 * WilsonLower(Wins, Count);
        public decimal MeanMfeAtr { get; init; }
        public decimal MeanMaeAtr { get; init; }
        public static double WilsonLower(int wins, int count)
        {
            if (count == 0) { return 0; }
            double p = (double)wins / count; const double z = 1.959963984540054;
            return (p + z * z / (2 * count) - z * Math.Sqrt((p * (1 - p) + z * z / (4 * count)) / count)) / (1 + z * z / count);
        }
    }

    /// <summary>One fit-selected configuration with its untouched chronological check, plus a display-only Cloud selection.</summary>
    internal sealed class OrderFlowCloudRecommendation
    {
        public int Layer { get; init; }
        public string Regime { get; init; }
        public decimal? RegimeLower { get; init; }
        public decimal? RegimeUpper { get; init; }
        public int HorizonMinutes { get; init; }
        public bool Reversal { get; init; }
        public string Reaction => Reversal ? "Reversal / Разворот" : "Continuation / Продолжение";
        public OrderFlowCloudStudyRule Rule { get; init; }
        public string Parameters => Rule.Id;
        public int TriedRules { get; init; }
        public OrderFlowCloudStudyMetrics Fit { get; init; }
        public OrderFlowCloudStudyMetrics Test { get; init; }
        public OrderFlowCloudStudyMetrics FitBaseline { get; init; }
        public OrderFlowCloudStudyMetrics TestBaseline { get; init; }
        public bool SelectedImprovement { get; init; }
        public bool EnoughSamples { get; init; }
        public bool TestImprovement => SelectedImprovement && EnoughSamples && Test.WinPercent > TestBaseline.WinPercent;
        public string Status => !EnoughSamples ? "Small sample / Мало наблюдений" : !SelectedImprovement ? "No fit improvement / Нет прироста на подборе"
            : TestImprovement ? "Check improved / Прирост на проверке" : "Not confirmed / Не подтверждено";
        public List<string> MatchingCloudIds { get; init; } = new List<string>();
        public string FocusCloudId { get; init; }
    }

    internal sealed class OrderFlowCloudStatisticsResult
    {
        public string Version => OrderFlowCloudStatisticsSettings.Version;
        public string StudyHash { get; set; }
        public string InputSha256 { get; set; }
        public string SourceSpecHash { get; set; }
        public OrderFlowCloudStatisticsSettings Settings { get; set; }
        public DateTime? SplitDate { get; set; }
        public int ObservedDays { get; set; }
        public int FitDays { get; set; }
        public int SkippedUnfinished { get; set; }
        public int SkippedNeutral { get; set; }
        public int SkippedAtr { get; set; }
        public int SkippedOverlap { get; set; }
        public int SkippedIncomplete { get; set; }
        public int Purged { get; set; }
        public List<OrderFlowCloudStudyEvent> Events { get; } = new List<OrderFlowCloudStudyEvent>();
        public List<OrderFlowCloudRecommendation> Recommendations { get; } = new List<OrderFlowCloudRecommendation>();
        [JsonIgnore] public string ArtifactDirectory { get; set; }
    }
}
