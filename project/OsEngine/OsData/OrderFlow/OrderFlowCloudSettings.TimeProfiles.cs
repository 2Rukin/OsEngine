/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.Indicators;
using System;
using System.Globalization;
using System.Text.Json.Serialization;

namespace OsEngine.OsData.OrderFlow
{
    internal sealed partial class OrderFlowCloudSettings
    {
        private ThresholdTimeProfiles _timeProfiles;

        /// <summary>Optional source-clock thresholds; Cloud never resets at interval boundaries. Imported reset flags are normalized to false.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ThresholdTimeProfiles TimeProfiles { get => _timeProfiles; set => _timeProfiles = WithoutReset(value); }

        /// <summary>Adapts a shared immutable schedule to Cloud semantics without changing cumulative consumers' schedules.</summary>
        internal static ThresholdTimeProfiles WithoutReset(ThresholdTimeProfiles profiles) =>
            profiles?.ResetOnStart == true ? profiles with { ResetOnStart = false } : profiles;
        /// <summary>Upper physical tick volume; zero disables the upper bound.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public decimal MaximumTickVolume { get; set; }
        /// <summary>Upper anchored Cloud volume; zero disables the upper bound.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public decimal MaximumSumVolume { get; set; }
        /// <summary>Inclusive minimum absolute anchored delta.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public decimal MinimumAbsoluteDelta { get; set; }
        /// <summary>Inclusive maximum absolute anchored delta; zero disables the upper bound.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public decimal MaximumAbsoluteDelta { get; set; }
        /// <summary>Inclusive minimum number of physical included trades.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int MinimumTradeCount { get; set; }
        /// <summary>Inclusive maximum number of physical included trades; zero disables the upper bound.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int MaximumTradeCount { get; set; }

        /// <summary>Editor alias for the base diagonal side-volume floor; serialized by Imbalance only.</summary>
        [JsonIgnore] public decimal ImbalanceMinimumDominantVolume { get => Imbalance.MinimumDominantVolume; set => Imbalance.MinimumDominantVolume = value; }
        /// <summary>Editor alias for the base diagonal absolute difference floor.</summary>
        [JsonIgnore] public decimal ImbalanceMinimumDifference { get => Imbalance.MinimumDifference; set => Imbalance.MinimumDifference = value; }
        /// <summary>Editor alias for the base directional profile delta percent, zero through 100.</summary>
        [JsonIgnore] public decimal ImbalanceMinimumDeltaPercent { get => Imbalance.MinimumDeltaPercent; set => Imbalance.MinimumDeltaPercent = value; }
        /// <summary>Editor alias for the base dominant/opposite volume ratio percent, at least 100.</summary>
        [JsonIgnore] public decimal ImbalanceMinimumRatioPercent { get => Imbalance.MinimumRatioPercent; set => Imbalance.MinimumRatioPercent = value; }

        /// <summary>Resolves opt-in diagonal thresholds without changing the base floors cached by the accumulation profiles.</summary>
        internal OrderFlowImbalanceSettings ScheduledImbalance(ThresholdSelection selection)
        {
            if (Imbalance.Source == OrderFlowImbalanceSource.Off || selection.Period == null ||
                !(selection.Period.Values.ContainsKey("ImbalanceMinimumDominantVolume") || selection.Period.Values.ContainsKey("ImbalanceMinimumDifference") ||
                  selection.Period.Values.ContainsKey("ImbalanceMinimumDeltaPercent") || selection.Period.Values.ContainsKey("ImbalanceMinimumRatioPercent"))) { return null; }
            return new OrderFlowImbalanceSettings { Source = Imbalance.Source, Direction = Imbalance.Direction, ContextSeconds = Imbalance.ContextSeconds,
                MinimumDominantVolume = selection.Value("ImbalanceMinimumDominantVolume", Imbalance.MinimumDominantVolume),
                MinimumDifference = selection.Value("ImbalanceMinimumDifference", Imbalance.MinimumDifference),
                MinimumDeltaPercent = selection.Value("ImbalanceMinimumDeltaPercent", Imbalance.MinimumDeltaPercent),
                MinimumRatioPercent = selection.Value("ImbalanceMinimumRatioPercent", Imbalance.MinimumRatioPercent) };
        }

        private void ValidateTimeProfiles()
        {
            TimeProfiles?.Validate(OrderFlowTimeThresholds.Cloud);
            if (TimeProfiles?.Enabled != true) { TimeProfiles = null; }
            ValidateSelection(new ThresholdSelection(true, false, null));
            if (TimeProfiles != null)
            { foreach (ThresholdTimePeriod period in TimeProfiles.Periods) { ValidateSelection(new ThresholdSelection(true, false, period)); } }
        }
        private void ValidateSelection(ThresholdSelection selection)
        {
            OrderFlowTimeThresholds.ValidateRange(selection.Value("MinimumTickVolume", MinimumTickVolume), selection.Value("MaximumTickVolume", MaximumTickVolume));
            OrderFlowTimeThresholds.ValidateRange(selection.Value(SingleTicks ? "MinimumTickVolume" : "MinimumSumVolume", SingleTicks ? MinimumTickVolume : MinimumSumVolume), selection.Value("MaximumSumVolume", MaximumSumVolume));
            OrderFlowTimeThresholds.ValidateRange(selection.Value("MinimumAbsoluteDelta", MinimumAbsoluteDelta), selection.Value("MaximumAbsoluteDelta", MaximumAbsoluteDelta));
            OrderFlowTimeThresholds.ValidateRange(selection.Value("MinimumTradeCount", MinimumTradeCount), selection.Value("MaximumTradeCount", MaximumTradeCount));
        }
        internal bool PassesThresholds(OrderFlowCloud cloud, ThresholdSelection selection, decimal minimumSum) =>
            OrderFlowTimeThresholds.Range(cloud.Volume, minimumSum, selection.Value("MaximumSumVolume", MaximumSumVolume)) &&
            OrderFlowTimeThresholds.Range(Math.Abs(cloud.Delta), selection.Value("MinimumAbsoluteDelta", MinimumAbsoluteDelta), selection.Value("MaximumAbsoluteDelta", MaximumAbsoluteDelta)) &&
            OrderFlowTimeThresholds.Range(cloud.TradeCount, selection.Value("MinimumTradeCount", MinimumTradeCount), selection.Value("MaximumTradeCount", MaximumTradeCount));

        internal string TimeProfileCanonicalValue()
        {
            string profiles = TimeProfiles?.CanonicalValue() ?? "";
            if (profiles.Length == 0 && MaximumTickVolume == 0 && MaximumSumVolume == 0 && MinimumAbsoluteDelta == 0 && MaximumAbsoluteDelta == 0 && MinimumTradeCount == 0 && MaximumTradeCount == 0)
            { return ""; }
            return "|cloud-thresholds-1|" + string.Join("|", MaximumTickVolume.ToString("G29", CultureInfo.InvariantCulture),
                MaximumSumVolume.ToString("G29", CultureInfo.InvariantCulture), MinimumAbsoluteDelta.ToString("G29", CultureInfo.InvariantCulture),
                MaximumAbsoluteDelta.ToString("G29", CultureInfo.InvariantCulture), MinimumTradeCount.ToString(CultureInfo.InvariantCulture),
                MaximumTradeCount.ToString(CultureInfo.InvariantCulture), profiles);
        }
    }
}
