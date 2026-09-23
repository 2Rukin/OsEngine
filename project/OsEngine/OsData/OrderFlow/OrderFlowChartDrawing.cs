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
    internal enum OrderFlowDrawingTool { Select, Horizontal, Trend }

    /// <summary>Source-clock timestamp and unsnapped decimal price of a temporary annotation anchor.</summary>
    internal readonly record struct OrderFlowDrawingAnchor(DateTime Time, decimal Price);

    /// <summary>UI-only annotation, excluded from research results, hashes and artifacts.</summary>
    internal sealed class OrderFlowChartLine
    {
        public OrderFlowDrawingTool Kind { get; set; }
        public OrderFlowDrawingAnchor First { get; set; }
        public OrderFlowDrawingAnchor Second { get; set; }
        public Color Color { get; set; } = Colors.Gold;
        public double Thickness { get; set; } = 2;
        public bool ExtendLeft { get; set; }
        public bool ExtendRight { get; set; }
    }

    /// <summary>
    /// Maps annotation anchors onto the chart's compressed bar axis and clips segments/rays to the price panel.
    /// Missing intervals collapse to the next bar boundary; no artificial bars or source mutations are produced.
    /// </summary>
    internal static class OrderFlowDrawingGeometry
    {
        #region Coordinates

        /// <summary>Returns the fractional bar position, extrapolating outside the series with the nearest bar duration.</summary>
        public static double BarPosition(IReadOnlyList<OrderFlowDisplayBar> bars, DateTime time)
        {
            if (bars.Count == 0) { throw new ArgumentException("Bars are required.", nameof(bars)); }
            int low = 0;
            int high = bars.Count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (bars[middle].TimeEnd <= time) { low = middle + 1; }
                else { high = middle; }
            }
            int index = Math.Min(low, bars.Count - 1);
            OrderFlowDisplayBar bar = bars[index];
            if (index > 0 && time < bar.TimeStart) { return index; }
            return index + (time.Ticks - bar.TimeStart.Ticks) / (double)(bar.TimeEnd.Ticks - bar.TimeStart.Ticks);
        }

        /// <summary>Inverts the compressed bar coordinate, extrapolating outside history with the nearest bar duration.</summary>
        /// <remarks>Preserves source DateTime.Kind; date limits are clamped. Empty historical gaps have no independent coordinate.</remarks>
        public static DateTime TimeAt(IReadOnlyList<OrderFlowDisplayBar> bars, double position)
        {
            if (bars.Count == 0 || !double.IsFinite(position)) { throw new ArgumentException("Finite position and bars are required."); }
            int index = (int)Math.Clamp(Math.Floor(position), 0, bars.Count - 1);
            OrderFlowDisplayBar bar = bars[index];
            decimal ticks = bar.TimeStart.Ticks + (decimal)(position - index) * (bar.TimeEnd.Ticks - bar.TimeStart.Ticks);
            return new DateTime((long)Math.Clamp(decimal.Round(ticks), DateTime.MinValue.Ticks, DateTime.MaxValue.Ticks), bar.TimeStart.Kind);
        }

        /// <summary>Translates both anchors by the same compressed-axis distance and price offset; preserves screen slope on this bar grid.</summary>
        /// <remarks>Limits are applied to the common displacement, never to one endpoint alone. Source anchors remain timeframe independent after the gesture.</remarks>
        public static (OrderFlowDrawingAnchor First, OrderFlowDrawingAnchor Second) Translate(
            IReadOnlyList<OrderFlowDisplayBar> bars, OrderFlowDrawingAnchor first, OrderFlowDrawingAnchor second,
            double barOffset, decimal priceOffset)
        {
            double firstPosition = BarPosition(bars, first.Time);
            double secondPosition = BarPosition(bars, second.Time);
            double lower = BarPosition(bars, DateTime.MinValue);
            double upper = BarPosition(bars, DateTime.MaxValue);
            barOffset = Math.Clamp(barOffset, lower - Math.Min(firstPosition, secondPosition), upper - Math.Max(firstPosition, secondPosition));
            foreach (decimal price in new[] { first.Price, second.Price })
            {
                if (priceOffset > 0 && price > 0) { priceOffset = Math.Min(priceOffset, decimal.MaxValue - price); }
                if (priceOffset < 0 && price < 0) { priceOffset = Math.Max(priceOffset, decimal.MinValue - price); }
            }
            return (new OrderFlowDrawingAnchor(TimeAt(bars, firstPosition + barOffset), first.Price + priceOffset),
                new OrderFlowDrawingAnchor(TimeAt(bars, secondPosition + barOffset), second.Price + priceOffset));
        }

        /// <summary>Converts an in-panel pointer to a source-clock anchor; prices are not snapped to PriceStep.</summary>
        public static OrderFlowDrawingAnchor AnchorAt(IReadOnlyList<OrderFlowDisplayBar> bars, int start, int count,
            Rect plot, decimal min, decimal max, Point point)
        {
            double position = Math.Clamp((point.X - plot.Left) / plot.Width * count, 0, count);
            int index = Math.Min(count - 1, (int)position);
            OrderFlowDisplayBar bar = bars[start + index];
            long span = bar.TimeEnd.Ticks - bar.TimeStart.Ticks;
            long offset = Math.Min(span - 1, Math.Max(0, (long)((position - index) * span)));
            decimal fraction = (decimal)Math.Clamp((plot.Bottom - point.Y) / plot.Height, 0, 1);
            return new OrderFlowDrawingAnchor(bar.TimeStart.AddTicks(offset), min + fraction * (max - min));
        }

        /// <summary>Projects an anchor using current bars and visible OHLC scale, including anchors outside the viewport.</summary>
        public static Point Project(IReadOnlyList<OrderFlowDisplayBar> bars, int start, int count, Rect plot,
            decimal min, decimal max, OrderFlowDrawingAnchor anchor)
        {
            return new Point(plot.Left + (BarPosition(bars, anchor.Time) - start) * plot.Width / count,
                plot.Bottom - (double)((anchor.Price - min) / (max - min)) * plot.Height);
        }

        #endregion

        #region Clipping and selection

        /// <summary>
        /// Clips a segment, ray or infinite line. Left/right mean screen-time direction, regardless of click order.
        /// Coincident X anchors use their finite vertical segment; horizontal extensions are undefined in that case.
        /// </summary>
        public static bool Clip(Point first, Point second, bool extendLeft, bool extendRight, Rect plot,
            out Point from, out Point to)
        {
            from = to = default;
            if (plot.IsEmpty) { return false; }
            if (first.X > second.X) { (first, second) = (second, first); }
            Vector delta = second - first;
            bool vertical = Math.Abs(delta.X) < 0.000001;
            double lower = extendLeft && !vertical ? double.NegativeInfinity : 0;
            double upper = extendRight && !vertical ? double.PositiveInfinity : 1;
            if (!ClipAxis(first.X, delta.X, plot.Left, plot.Right, ref lower, ref upper) ||
                !ClipAxis(first.Y, delta.Y, plot.Top, plot.Bottom, ref lower, ref upper)) { return false; }
            from = first + delta * lower;
            to = first + delta * upper;
            return double.IsFinite(from.X) && double.IsFinite(from.Y) && double.IsFinite(to.X) && double.IsFinite(to.Y);
        }

        private static bool ClipAxis(double origin, double direction, double min, double max, ref double lower, ref double upper)
        {
            if (Math.Abs(direction) < 0.000001) { return origin >= min && origin <= max; }
            double enter = (min - origin) / direction;
            double leave = (max - origin) / direction;
            if (enter > leave) { (enter, leave) = (leave, enter); }
            lower = Math.Max(lower, enter);
            upper = Math.Min(upper, leave);
            return lower <= upper;
        }

        /// <summary>Pixel distance to a clipped finite segment, including a zero-length segment.</summary>
        public static double Distance(Point point, Point first, Point second)
        {
            Vector delta = second - first;
            double fraction = delta.LengthSquared == 0 ? 0 : Math.Clamp(Vector.Multiply(point - first, delta) / delta.LengthSquared, 0, 1);
            return (point - (first + delta * fraction)).Length;
        }

        #endregion
    }
}
