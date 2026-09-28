/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace OsEngine.OsData.OrderFlow
{
    internal sealed partial class OrderFlowResearchChart
    {
        #region Volume sizing

        private double _cloudContrast = 2;
        private decimal _cloudReferenceVolume = 1;
        private bool _showCloudVolumes = true;
        private decimal _cloudReferenceVolume2 = 1;
        private bool _showCloudVolumes2 = true;

        /// <summary>Volume reference: full-result positive median in history, configured minimum Cloud sum during replay; fixed across viewport/timeframe changes.</summary>
        internal decimal CloudReferenceVolume => _cloudReferenceVolume;
        internal decimal Cloud2ReferenceVolume => _cloudReferenceVolume2;

        /// <summary>
        /// Changes relative volume-size contrast for all Clouds, including added instances and calibration overlays. UI-thread-only; no recalculation/export.
        /// The finite exponent ranges from 0.5 through 10 (default 2). Ordinary layers retain their own history/replay references; calibration retains its capped logarithmic sizing.
        /// </summary>
        public void SetCloudContrast(double contrast)
        {
            if (!double.IsFinite(contrast) || contrast < 0.5 || contrast > 10) { throw new ArgumentOutOfRangeException(nameof(contrast)); }
            _cloudContrast = contrast;
            _additionalCloudHits.Clear();
            _calibrationHits.Clear();
            _cloudHits.Clear();
            _cloudHits2.Clear();
            _cloudPlotBounds = Rect.Empty;
            ToolTip = null;
            InvalidateVisual();
        }

        /// <summary>Toggles exact volume text for the selected layer (secondLayer selects Cloud 2) when it fits within the visible circle; hit areas and research data remain unchanged.</summary>
        public void SetCloudVolumeLabels(bool visible, bool secondLayer = false)
        {
            if (secondLayer) { _showCloudVolumes2 = visible; }
            else { _showCloudVolumes = visible; }
            InvalidateVisual();
        }

        private void UpdateCloudReference()
        {
            _cloudReferenceVolume = MedianVolume(_result?.Clouds);
            _cloudReferenceVolume2 = MedianVolume(_result?.Clouds2);
        }

        private static decimal MedianVolume(List<OrderFlowCloud> clouds)
        {
            decimal[] volumes = clouds?.Where(cloud => cloud.Volume > 0).Select(cloud => cloud.Volume).ToArray();
            if (volumes == null || volumes.Length == 0) { return 1; }
            Array.Sort(volumes);
            int middle = volumes.Length / 2;
            return volumes.Length % 2 == 1 ? volumes[middle]
                : volumes[middle - 1] + (volumes[middle] - volumes[middle - 1]) / 2;
        }

        /// <summary>
        /// Smooth log-power radius in WPF units, with no hard volume ceiling. Area is not proportional to volume.
        /// Full-result median normalization makes comparisons stable during pan/zoom/TF changes, not across different results.
        /// </summary>
        internal double CloudVolumeRadius(decimal volume, bool secondLayer = false)
            => CloudVolumeRadius(volume, secondLayer ? _cloudReferenceVolume2 : _cloudReferenceVolume);

        private double CloudVolumeRadius(decimal volume, decimal reference)
        {
            double relative = (double)volume / (double)Math.Max(reference, 0.0000000000000000000000000001m);
            double power = _cloudContrast * Math.Log2(relative);
            // Stable softplus: direct Pow(relative, 10) can overflow for valid decimal volume ratios.
            double logarithm = Math.Max(0, power) + Math.Log2(1 + Math.Pow(2, -Math.Abs(power)));
            return _cloudScale * (4 + 20 * logarithm);
        }

        #endregion

        #region Volume labels

        private void DrawCloudVolumeLabels(DrawingContext context,
            List<(Point Center, double Radius, OrderFlowCloud Cloud)> hits, bool secondLayer)
        {
            if (!(secondLayer ? _showCloudVolumes2 : _showCloudVolumes)) { return; }
            foreach ((Point center, double radius, OrderFlowCloud cloud) in hits)
            {
                FormattedText text = new FormattedText(F(cloud.Volume), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 10, ForegroundBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                if (text.Width + 4 > radius * 1.8 || text.Height + 4 > radius * 1.8 ||
                    text.Width + 4 > _cloudPlotBounds.Width || text.Height + 4 > _cloudPlotBounds.Height) { continue; }
                Point origin = new Point(Math.Clamp(center.X - text.Width / 2, _cloudPlotBounds.Left + 2, _cloudPlotBounds.Right - text.Width - 2),
                    Math.Clamp(center.Y - text.Height / 2, _cloudPlotBounds.Top + 2, _cloudPlotBounds.Bottom - text.Height - 2));
                double farX = Math.Max(Math.Abs(origin.X - center.X), Math.Abs(origin.X + text.Width - center.X));
                double farY = Math.Max(Math.Abs(origin.Y - center.Y), Math.Abs(origin.Y + text.Height - center.Y));
                if (farX * farX + farY * farY > (radius - 1) * (radius - 1)) { continue; }
                context.DrawText(text, origin);
            }
        }

        #endregion
    }
}
