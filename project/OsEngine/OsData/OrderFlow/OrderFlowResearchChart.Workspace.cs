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
    internal sealed partial class OrderFlowResearchChart
    {
        #region Right workspace

        private double _rightPaddingPercent = 5;
        private Rect _drawingDataPlot = Rect.Empty;
        internal double RightPaddingPercent => _rightPaddingPercent;
        private double DataWidth => Math.Max(1, ActualWidth - 104) * (1 - _rightPaddingPercent / 100);

        /// <summary>Reserves 0–90 percent of the plot width after the visible data; default five. No artificial bars enter results.</summary>
        /// <remarks>UI-thread-only; cancels transient pointer input, retaining the selected tool and source-time annotations.</remarks>
        public void SetRightPadding(double percent)
        {
            if (!double.IsFinite(percent) || percent < 0 || percent > 90) { throw new ArgumentOutOfRangeException(nameof(percent)); }
            CancelDrawingGesture();
            _rightPaddingPercent = percent;
            _cloudHits.Clear();
            _cloudHits2.Clear();
            _cloudPlotBounds = _drawingPlot = _drawingDataPlot = Rect.Empty;
            ScrollTo(_startIndex);
        }

        /// <summary>Returns no candle for the reserved right workspace; circles retain their own clipped hit testing.</summary>
        internal int BarIndexAt(double x)
        {
            double position = (x - 92) / DataWidth;
            return position < 0 || position >= 1 || VisibleCount == 0 ? -1
                : _startIndex + Math.Min(VisibleCount - 1, (int)(position * VisibleCount));
        }

        #endregion

        #region Price grid

        /// <summary>Returns readable decimal prices on a 1/2/5 grid within the visible range; labels use these exact prices.</summary>
        internal static List<decimal> PriceGrid(decimal minimum, decimal maximum, double height)
        {
            List<decimal> prices = new List<decimal>();
            if (maximum <= minimum) { prices.Add(minimum); return prices; }
            decimal desired = (maximum - minimum) / (int)Math.Clamp(Math.Floor(height / 65), 2, 10);
            decimal unit = 1;
            while (unit > desired && unit > 0.0000000000000000000000000001m) { unit /= 10; }
            while (unit <= desired / 10) { unit *= 10; }
            decimal ratio = desired / unit;
            decimal step = unit * (ratio <= 1 ? 1 : ratio <= 2 ? 2 : ratio <= 5 ? 5 : 10);
            decimal remainder = minimum % step;
            decimal first = minimum - remainder;
            if (first < minimum) { first += step; }
            for (decimal price = first; price <= maximum;)
            {
                prices.Add(price);
                if (maximum - price < step) { break; }
                price += step;
            }
            return prices;
        }

        private void DrawPriceGrid(DrawingContext context, decimal minimum, decimal maximum,
            double left, double right, double top, double bottom)
        {
            Pen grid = new Pen(new SolidColorBrush(Color.FromArgb(70, 128, 128, 128)), 0.5);
            foreach (decimal price in PriceGrid(minimum, maximum, bottom - top))
            {
                double y = Scale(price, minimum, maximum, bottom, top);
                context.DrawLine(grid, new Point(left, y), new Point(right, y));
                FormattedText text = new FormattedText(F(price), System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, ForegroundBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                // Fit exact decimal labels into the existing scale gutter without covering price data.
                double factor = Math.Min(1, (left - 8) / Math.Max(1, text.Width));
                context.PushTransform(new TranslateTransform(4, Math.Clamp(y - text.Height / 2, top - 5, bottom - text.Height + 5)));
                context.PushTransform(new ScaleTransform(factor, 1));
                context.DrawText(text, new Point());
                context.Pop(); context.Pop();
            }
        }

        #endregion
    }
}
