/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.Indicators;
using System;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace OsEngine.OsData.OrderFlow
{
    internal sealed partial class OrderFlowResearchChart
    {
        private readonly Dictionary<string, OrderFlowCloudSettings> _cloudLayerPostFilters = new Dictionary<string, OrderFlowCloudSettings>();
        private readonly Dictionary<string, (List<OrderFlowCloud> Source, List<OrderFlowCloud> Rows)> _cloudLayerRowCache = new Dictionary<string, (List<OrderFlowCloud>, List<OrderFlowCloud>)>();
        private readonly Dictionary<string, OrderFlowCloudLayer> _cloudLayerViews = new Dictionary<string, OrderFlowCloudLayer>();
        private readonly List<(Point Center, double Radius, OrderFlowCloud Cloud)> _additionalCloudHits = new List<(Point, double, OrderFlowCloud)>();

        /// <summary>Current history or consumed replay prefix, shared by the chart and additional-Cloud table.</summary>
        internal IReadOnlyList<OrderFlowCloudLayerResult> DisplayedCloudLayers => (IReadOnlyList<OrderFlowCloudLayerResult>)_result?.CloudLayers ?? Array.Empty<OrderFlowCloudLayerResult>();

        /// <summary>Changes only named-layer appearance on the dispatcher; historical and replay calculations remain frozen.</summary>
        internal void SetCloudLayerView(OrderFlowCloudLayer layer)
        {
            _cloudLayerViews[layer.Id] = layer.Copy(); _additionalCloudHits.Clear(); ToolTip = null; InvalidateVisual();
        }
        private OrderFlowCloudLayer LayerView(OrderFlowCloudLayer layer) => _cloudLayerViews.TryGetValue(layer.Id, out OrderFlowCloudLayer view) ? view : layer;
        /// <summary>Installs a display-only threshold filter evaluated at each saved Cloud anchor; formation and exported evidence stay unchanged.</summary>
        internal void SetCloudLayerPostFilter(string id, OrderFlowCloudSettings settings)
        {
            OrderFlowCloudSettings copy = JsonSerializer.Deserialize<OrderFlowCloudSettings>(JsonSerializer.Serialize(settings));
            copy.Validate(); _cloudLayerPostFilters[id] = copy; _cloudLayerRowCache.Remove(id); _additionalCloudHits.Clear(); ToolTip = null; InvalidateVisual();
        }
        internal List<OrderFlowCloud> CloudLayerRows(OrderFlowCloudLayerResult result)
        {
            if (!_cloudLayerPostFilters.TryGetValue(result.Layer.Id, out OrderFlowCloudSettings settings)) { return result.Clouds; }
            if (_cloudLayerRowCache.TryGetValue(result.Layer.Id, out (List<OrderFlowCloud> Source, List<OrderFlowCloud> Rows) cached) && ReferenceEquals(cached.Source, result.Clouds)) { return cached.Rows; }
            ThresholdTimeCursor cursor = new ThresholdTimeCursor(settings.TimeProfiles); List<OrderFlowCloud> rows = new List<OrderFlowCloud>(result.Clouds.Count);
            foreach (OrderFlowCloud cloud in result.Clouds)
            {
                ThresholdSelection selection = cursor.Select(cloud.Time); OrderFlowCloud view = cloud.Copy();
                decimal minimum = selection.Value(settings.SingleTicks ? "MinimumTickVolume" : "MinimumSumVolume", settings.SingleTicks ? settings.MinimumTickVolume : settings.MinimumSumVolume);
                OrderFlowImbalanceSettings imbalance = settings.ScheduledImbalance(selection) ?? settings.Imbalance;
                view.InsideImbalance = cloud.InsideImbalance?.WithFloors(imbalance);
                view.ContextImbalance = cloud.ContextImbalance?.WithFloors(imbalance);
                view.ImbalanceSource = imbalance.Source;
                view.ImbalancePassed = imbalance.Passes(view.InsideImbalance, view.ContextImbalance);
                view.ThresholdPassed = selection.Active && settings.PassesThresholds(cloud, selection, minimum) && view.ImbalancePassed; rows.Add(view);
            }
            _cloudLayerRowCache[result.Layer.Id] = (result.Clouds, rows); return rows;
        }
        private List<OrderFlowCloud> AdditionalClouds(OrderFlowCloudLayerResult result) => CloudLayerRows(result)
            .Where(c => c.ThresholdPassed && c.ImbalancePassed || c.CloudId == _selectedCloudId).ToList();
        private bool HasAdditionalClouds() => _result?.CloudLayers.Any(r => LayerView(r.Layer).Visible && AdditionalClouds(r).Count != 0) == true;
        private void DrawAdditionalClouds(DrawingContext context, List<OrderFlowDisplayBar> bars, double left, double slot,
            double top, double bottom, decimal min, decimal max)
        {
            foreach (OrderFlowCloudLayerResult result in _result.CloudLayers)
            {
                OrderFlowCloudLayer view = LayerView(result.Layer);
                if (!view.Visible) { continue; }
                DrawClouds(context, bars, left, slot, top, bottom, min, max, false, result, view);
            }
        }
        private void DrawAdditionalCloudPaths(DrawingContext context, Rect plot, decimal min, decimal max)
        {
            foreach (OrderFlowCloudLayerResult result in _result.CloudLayers)
            {
                OrderFlowCloudLayer view = LayerView(result.Layer);
                if (view.Visible) { DrawCloudPriceLayer(context, plot, min, max, AdditionalClouds(result), new SolidColorBrush((Color)ColorConverter.ConvertFromString(view.Color))); }
            }
        }
        private decimal AdditionalCloudReference(OrderFlowCloudLayerResult result) => IsReplaying
            ? (result.Layer.Settings.SingleTicks ? result.Layer.Settings.MinimumTickVolume : result.Layer.Settings.MinimumSumVolume) : MedianVolume(result.Clouds);
        private static double AdditionalCloudRadius(decimal reference, decimal volume)
        {
            double relative = (double)volume / (double)Math.Max(reference, 0.0000000000000000000000000001m);
            double power = 2 * Math.Log2(relative);
            return 4 + 20 * (Math.Max(0, power) + Math.Log2(1 + Math.Pow(2, -Math.Abs(power))));
        }
    }
}
