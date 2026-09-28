/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.Indicators;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace OsEngine.OsData.OrderFlow
{
    /// <summary>One explicitly named additional Cloud calculation; stable instance identity is independent of equal settings in other layers.</summary>
    internal sealed class OrderFlowCloudLayer
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "Cloud";
        public bool Enabled { get; set; } = true;
        public bool Visible { get; set; } = true;
        public string Color { get; set; } = "#FF5DADE2";
        public OrderFlowCloudSettings Settings { get; set; } = new OrderFlowCloudSettings();
        internal OrderFlowCloudLayer Copy() => JsonSerializer.Deserialize<OrderFlowCloudLayer>(JsonSerializer.Serialize(this));
        internal void Validate()
        {
            if (!Guid.TryParseExact(Id, "N", out _) || string.IsNullOrWhiteSpace(Name) || Settings == null ||
                Color == null || Color.Length != 9 || Color[0] != '#' || !uint.TryParse(Color.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
            { throw new ArgumentException("Invalid Cloud instance name, identity or ARGB color."); }
            Settings.Validate();
        }
    }

    /// <summary>Run-owned output of one Cloud instance. Replay copies the collection and forming evidence.</summary>
    internal sealed class OrderFlowCloudLayerResult
    {
        public OrderFlowCloudLayer Layer { get; set; }
        public List<OrderFlowCloud> Clouds { get; set; } = new List<OrderFlowCloud>();
        public string Hash { get; set; }
    }

    /// <summary>Supported thresholds share the generic schedule contract; Order Flow deliberately does not declare unavailable OI.</summary>
    internal static class OrderFlowTimeThresholds
    {
        internal static readonly ImmutableArray<ThresholdDefinition> Cloud = ImmutableArray.Create(
            new ThresholdDefinition("MinimumTickVolume", "Мин. объём тика / Min tick volume", ThresholdMetric.Volume, 0.0000000000000000000000000001m),
            new ThresholdDefinition("MaximumTickVolume", "Макс. объём тика, 0 без предела / Max tick", ThresholdMetric.Volume),
            new ThresholdDefinition("MinimumSumVolume", "Мин. объём Cloud / Min Cloud volume", ThresholdMetric.Volume, 0.0000000000000000000000000001m),
            new ThresholdDefinition("MaximumSumVolume", "Макс. объём Cloud, 0 без предела / Max Cloud", ThresholdMetric.Volume),
            new ThresholdDefinition("MinimumAbsoluteDelta", "Мин. абсолютная дельта / Min absolute delta", ThresholdMetric.Delta),
            new ThresholdDefinition("MaximumAbsoluteDelta", "Макс. абсолютная дельта, 0 без предела / Max delta", ThresholdMetric.Delta),
            new ThresholdDefinition("MinimumTradeCount", "Мин. число сделок / Min trades", ThresholdMetric.Trades, 0, int.MaxValue, true),
            new ThresholdDefinition("MaximumTradeCount", "Макс. число сделок, 0 без предела / Max trades", ThresholdMetric.Trades, 0, int.MaxValue, true),
            new ThresholdDefinition("ImbalanceMinimumDominantVolume", "Диагональ: мин. объём стороны / Min side volume", ThresholdMetric.Volume),
            new ThresholdDefinition("ImbalanceMinimumDifference", "Диагональ: мин. разность / Min difference", ThresholdMetric.Delta),
            new ThresholdDefinition("ImbalanceMinimumDeltaPercent", "Диагональ: мин. дельта профиля % / Min delta %", ThresholdMetric.Delta, 0, 100),
            new ThresholdDefinition("ImbalanceMinimumRatioPercent", "Диагональ: мин. отношение % / Min ratio %", ThresholdMetric.Volume, 100));
        internal static readonly ImmutableArray<ThresholdDefinition> Delta = ImmutableArray.Create(
            new ThresholdDefinition("MinimumAbsoluteDelta", "Мин. абсолютная дельта / Min absolute delta", ThresholdMetric.Delta, 0.0000000000000000000000000001m),
            new ThresholdDefinition("MaximumAbsoluteDelta", "Макс. абсолютная дельта, 0 без предела / Max delta", ThresholdMetric.Delta),
            new ThresholdDefinition("MinimumVolume", "Мин. объём окна / Min window volume", ThresholdMetric.Volume),
            new ThresholdDefinition("MaximumVolume", "Макс. объём окна, 0 без предела / Max volume", ThresholdMetric.Volume),
            new ThresholdDefinition("MinimumTradeCount", "Мин. число сделок / Min trades", ThresholdMetric.Trades, 0, int.MaxValue, true),
            new ThresholdDefinition("MaximumTradeCount", "Макс. число сделок, 0 без предела / Max trades", ThresholdMetric.Trades, 0, int.MaxValue, true));

        internal static bool Range(decimal value, decimal minimum, decimal maximum) => value >= minimum && (maximum == 0 || value <= maximum);
        internal static bool HasDiagonalOverrides(ThresholdTimeProfiles profiles) => profiles?.Enabled == true &&
            profiles.Periods.Any(p => p.Values.Keys.Any(k => k.StartsWith("Imbalance", StringComparison.Ordinal)));
        internal static void ValidateRange(decimal minimum, decimal maximum)
        { if (minimum < 0 || maximum < 0 || maximum != 0 && maximum < minimum) { throw new ArgumentException("Threshold range is inverted; maximum zero means unlimited."); } }
    }

    internal sealed partial class OrderFlowResearchRequest
    {
        /// <summary>Additional independent Cloud instances. Empty preserves legacy request and artifact identity.</summary>
        public List<OrderFlowCloudLayer> CloudLayers { get; set; } = new List<OrderFlowCloudLayer>();
        /// <summary>Optional source-clock thresholds for the Delta feature window; reset leaves previously created labels alive.</summary>
        public ThresholdTimeProfiles DeltaTimeProfiles { get; set; }

        private void ValidateTimeProfiles()
        {
            if (CloudLayers == null || CloudLayers.Any(l => l == null) || CloudLayers.Select(l => l.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != CloudLayers.Count)
            { throw new ArgumentException("Cloud instances require distinct identities."); }
            foreach (OrderFlowCloudLayer layer in CloudLayers) { layer.Validate(); }
            DeltaTimeProfiles?.Validate(OrderFlowTimeThresholds.Delta);
            if (!CalculateDelta || DeltaTimeProfiles?.Enabled != true) { DeltaTimeProfiles = null; }
            if (DeltaTimeProfiles != null)
            {
                foreach (ThresholdTimePeriod period in DeltaTimeProfiles.Periods)
                {
                    ThresholdSelection selection = new ThresholdSelection(true, false, period);
                    OrderFlowTimeThresholds.ValidateRange(selection.Value("MinimumAbsoluteDelta", MinimumAbsoluteDelta), selection.Value("MaximumAbsoluteDelta", 0));
                    OrderFlowTimeThresholds.ValidateRange(selection.Value("MinimumVolume", 0), selection.Value("MaximumVolume", 0));
                    OrderFlowTimeThresholds.ValidateRange(selection.Value("MinimumTradeCount", 0), selection.Value("MaximumTradeCount", 0));
                }
            }
        }
        internal string TimeProfileCanonicalValue()
        {
            string delta = DeltaTimeProfiles?.CanonicalValue() ?? "";
            string layers = string.Join(";", CloudLayers.Where(l => l.Enabled).OrderBy(l => l.Id, StringComparer.Ordinal).Select(l => l.Id + "=" + l.Settings.CanonicalValue()));
            return delta.Length == 0 && layers.Length == 0 ? "" : "|time-layers-1|" + delta + "|" + layers;
        }
    }

    internal sealed partial class OrderFlowResearchResult
    {
        /// <summary>All enabled extra instances, with independent historical or replay-prefix collections.</summary>
        public List<OrderFlowCloudLayerResult> CloudLayers { get; set; } = new List<OrderFlowCloudLayerResult>();
    }
}
