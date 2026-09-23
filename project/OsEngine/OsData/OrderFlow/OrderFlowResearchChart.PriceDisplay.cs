/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace OsEngine.OsData.OrderFlow
{
    internal enum OrderFlowPriceDisplay { Candles, Bars, HighLow, MutedHighLow }

    internal sealed partial class OrderFlowResearchChart
    {
        #region Display settings

        private OrderFlowPriceDisplay _priceDisplay;
        private bool _cloudPriceMode;

        /// <summary>Changes only the price renderer; all views share bars, viewport, annotations and research data.</summary>
        public void SetPriceDisplay(OrderFlowPriceDisplay display)
        {
            if (!Enum.IsDefined(display)) { throw new ArgumentOutOfRangeException(nameof(display)); }
            _priceDisplay = display;
            InvalidateVisual();
        }

        /// <summary>
        /// Draws a Cloud Time/Price polyline over muted timeframe High/Low lines. Cloud vertices are never aggregated by timeframe.
        /// This historical display uses completed result anchors, not a new causal signal or calculation.
        /// </summary>
        public void SetCloudPriceMode(bool enabled)
        {
            _cloudPriceMode = enabled;
            InvalidateVisual();
        }

        #endregion

        #region Price rendering

        private void DrawPriceDisplay(DrawingContext context, List<OrderFlowDisplayBar> bars, double left, double slot,
            double top, double bottom, decimal min, decimal max)
        {
            if (_cloudPriceMode || _priceDisplay == OrderFlowPriceDisplay.MutedHighLow)
            {
                Brush muted = new SolidColorBrush(Color.FromArgb(105, 145, 145, 145));
                DrawHighLow(context, bars, left, slot, top, bottom, min, max, muted, muted);
                if (_cloudPriceMode) { DrawCloudPricePath(context, new Rect(left, top, slot * bars.Count, bottom - top), min, max); }
                return;
            }
            if (_priceDisplay == OrderFlowPriceDisplay.Candles)
            { DrawPriceBars(context, bars, left, slot, top, bottom, min, max); return; }
            if (_priceDisplay == OrderFlowPriceDisplay.HighLow)
            { DrawHighLow(context, bars, left, slot, top, bottom, min, max, Brushes.MediumSeaGreen, Brushes.Coral); return; }
            for (int i = 0; i < bars.Count; i++)
            {
                OrderFlowDisplayBar bar = bars[i];
                double x = left + slot * (i + 0.5);
                double width = Math.Max(1, Math.Min(7, slot * 0.3));
                Pen pen = new Pen(bar.Close >= bar.Open ? Brushes.SeaGreen : Brushes.IndianRed, 1.2);
                context.DrawLine(pen, new Point(x, Scale(bar.High, min, max, bottom, top)), new Point(x, Scale(bar.Low, min, max, bottom, top)));
                double open = Scale(bar.Open, min, max, bottom, top);
                double close = Scale(bar.Close, min, max, bottom, top);
                context.DrawLine(pen, new Point(x - width, open), new Point(x, open));
                context.DrawLine(pen, new Point(x, close), new Point(x + width, close));
            }
        }

        private static void DrawHighLow(DrawingContext context, List<OrderFlowDisplayBar> bars, double left, double slot,
            double top, double bottom, decimal min, decimal max, Brush highBrush, Brush lowBrush)
        {
            for (int side = 0; side < 2; side++)
            {
                Pen pen = new Pen(side == 0 ? highBrush : lowBrush, 1.3);
                StreamGeometry geometry = new StreamGeometry();
                using (StreamGeometryContext path = geometry.Open())
                {
                    for (int i = 0; i < bars.Count; i++)
                    {
                        Point point = new Point(left + slot * (i + 0.5), Scale(side == 0 ? bars[i].High : bars[i].Low, min, max, bottom, top));
                        if (i == 0) { path.BeginFigure(point, false, false); }
                        else { path.LineTo(point, true, false); }
                        if (bars.Count == 1) { context.DrawLine(pen, point - new Vector(3, 0), point + new Vector(3, 0)); }
                    }
                }
                geometry.Freeze();
                context.DrawGeometry(null, pen, geometry);
            }
        }

        #endregion

        #region Cloud path

        /// <summary>Finds visible Cloud anchors plus the nearest outside neighbors so connecting segments cross viewport gaps.</summary>
        internal static (int Start, int End) CloudPathRange(IReadOnlyList<OrderFlowCloud> clouds, DateTime from, DateTime to)
        {
            int low = 0;
            int high = clouds.Count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (clouds[middle].Time < from) { low = middle + 1; }
                else { high = middle; }
            }
            int start = Math.Max(0, low - 1);
            while (low < clouds.Count && clouds[low].Time < to) { low++; }
            return (start, Math.Min(clouds.Count, low + 1));
        }

        private void DrawCloudPricePath(DrawingContext context, Rect plot, decimal min, decimal max)
        {
            List<OrderFlowCloud> firstClouds = DisplayClouds(false);
            List<OrderFlowCloud> secondClouds = DisplayClouds(true);
            bool first = _showCloud && _result.CloudCalculated && firstClouds.Count > 0;
            bool second = _showCloud2 && _result.Cloud2Calculated && secondClouds.Count > 0;
            if (!first && !second)
            {
                DrawText(context, L("No visible calculated Clouds — select a calculated layer or run research.", "Нет видимых рассчитанных Cloud — покажите рассчитанный слой или выполните расчёт."),
                    new Point(plot.Left + 8, plot.Top + 8), Brushes.Goldenrod, 12);
                return;
            }
            if (first) { DrawCloudPriceLayer(context, plot, min, max, firstClouds, Brushes.DeepSkyBlue); }
            if (second) { DrawCloudPriceLayer(context, plot, min, max, secondClouds, Brushes.MediumOrchid); }
        }

        private void DrawCloudPriceLayer(DrawingContext context, Rect plot, decimal min, decimal max,
            List<OrderFlowCloud> clouds, Brush brush)
        {
            List<OrderFlowDisplayBar> bars = CurrentBars;
            (int start, int end) = CloudPathRange(clouds, bars[_startIndex].TimeStart, bars[_startIndex + VisibleCount - 1].TimeEnd);
            Pen pen = new Pen(brush, 2);
            Point? previous = null;
            context.PushClip(new RectangleGeometry(plot));
            for (int i = start; i < end; i++)
            {
                OrderFlowCloud cloud = clouds[i];
                Point point = OrderFlowDrawingGeometry.Project(bars, _startIndex, VisibleCount, plot, min, max,
                    new OrderFlowDrawingAnchor(cloud.Time, cloud.Price));
                if (previous.HasValue && OrderFlowDrawingGeometry.Clip(previous.Value, point, false, false, plot, out Point first, out Point second))
                { context.DrawLine(pen, first, second); }
                if (plot.Contains(point)) { context.DrawEllipse(brush, null, point, 2, 2); }
                previous = point;
            }
            context.Pop();
        }

        #endregion
    }
}
