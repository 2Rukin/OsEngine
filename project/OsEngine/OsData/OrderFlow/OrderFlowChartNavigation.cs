/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using System;
using System.Collections.Generic;
using System.Globalization;

namespace OsEngine.OsData.OrderFlow
{
    /// <summary>Pure display calculations for pointer-anchored horizontal zoom and readable time ticks.</summary>
    /// <remarks>Bars without trades remain omitted. Tick labels use source times without timezone conversion.</remarks>
    internal static class OrderFlowChartNavigation
    {
        /// <summary>Retains an anchor bar within rounding and available-history bounds.</summary>
        public static int ZoomStart(int start, int oldCount, int newCount, int total, double anchor)
        {
            anchor = Math.Clamp(anchor, 0, 1);
            double requested = start + anchor * (oldCount - newCount);
            return (int)Math.Clamp(Math.Round(requested), 0, Math.Max(0, total - newCount));
        }

        /// <summary>Changes at least one bar for each nonzero wheel event, including at the two-bar minimum.</summary>
        public static int WheelCount(int count, int total, int delta)
        {
            double requested = count * Math.Pow(1.25, -Math.Clamp(delta / 120.0, -8, 8));
            double rounded = delta < 0 ? Math.Ceiling(requested) : Math.Floor(requested);
            return (int)Math.Clamp(rounded, Math.Min(2, Math.Max(1, total)), Math.Max(1, total));
        }

        /// <summary>Places sparse labels at actual displayed bar times, including source date changes.</summary>
        public static List<OrderFlowChartTimeTick> TimeTicks(List<OrderFlowDisplayBar> bars, double width)
        {
            List<OrderFlowChartTimeTick> ticks = new List<OrderFlowChartTimeTick>();
            if (bars.Count == 0) { return ticks; }
            int count = Math.Min(bars.Count, Math.Clamp((int)(width / 125), 1, 12));
            bool seconds = bars[0].TimeEnd - bars[0].TimeStart < TimeSpan.FromMinutes(1);
            for (int i = 0; i < count; i++)
            {
                int index = count == 1 ? 0 : (int)Math.Round(i * (bars.Count - 1.0) / (count - 1));
                ticks.Add(new OrderFlowChartTimeTick { BarIndex = index,
                    Text = bars[index].TimeStart.ToString(seconds ? "dd.MM HH:mm:ss" : "dd.MM HH:mm", CultureInfo.InvariantCulture) });
            }
            return ticks;
        }
    }

    internal sealed class OrderFlowChartTimeTick
    {
        public int BarIndex { get; set; }
        public string Text { get; set; }
    }
}
