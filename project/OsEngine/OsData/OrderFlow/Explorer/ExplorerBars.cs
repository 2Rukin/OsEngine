/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace OsEngine.OsData.OrderFlow.Explorer
{
    internal sealed record ExplorerBar(DateTime Start, DateTime End, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume);

    /// <summary>Fifteen-second raw OHLC stream; detached aggregation changes display only and skips empty intervals.</summary>
    internal sealed class ExplorerBars
    {
        private readonly Action<ExplorerBar> _output;
        internal ExplorerBar Current { get; private set; }
        internal ExplorerBars(Action<ExplorerBar> output) { _output = output; }
        #region Raw OHLC stream

        internal void Add(OrderFlowDeal tick)
        {
            DateTime start = new DateTime(tick.Time.Ticks - tick.Time.Ticks % (15 * TimeSpan.TicksPerSecond));
            if (Current == null || Current.Start != start)
            {
                Complete(); Current = new ExplorerBar(start, start.AddSeconds(15), tick.Price, tick.Price, tick.Price, tick.Price, tick.Volume);
            }
            else { Current = Current with { High = Math.Max(Current.High, tick.Price), Low = Math.Min(Current.Low, tick.Price), Close = tick.Price, Volume = OrderFlowVolumeComparison.AddExact(Current.Volume, tick.Volume) }; }
        }
        internal void Complete() { if (Current != null) { _output(Current); Current = null; } }

        #endregion

        #region Bounded display aggregation

        internal static IReadOnlyList<ExplorerBar> ReadRange(string directory, DateTime from, DateTime to, OrderFlowDisplayTimeFrame timeFrame, CancellationToken cancellation)
        {
            long count = new FileInfo(Path.Combine(directory, "bars.idx")).Length / 8;
            if (count == 0) { return Array.Empty<ExplorerBar>(); }
            long left = 0, right = count;
            while (left < right)
            { long middle = left + (right - left) / 2; ExplorerBar bar = ExplorerStorage.ReadAt<ExplorerBar>(directory, "bars", middle); if (bar.End < from) { left = middle + 1; } else { right = middle; } }
            return Aggregate(ExplorerStorage.ReadRows<ExplorerBar>(directory, "bars", left).TakeWhile(b => b.Start <= to), timeFrame, cancellation);
        }

        /// <summary>Display-only aggregation with bounded dyadic compaction of long ranges; raw stored bars remain exact.</summary>
        internal static IReadOnlyList<ExplorerBar> Aggregate(IEnumerable<ExplorerBar> bars, OrderFlowDisplayTimeFrame timeFrame, CancellationToken cancellation)
        {
            List<ExplorerBar> output = new List<ExplorerBar>(); ExplorerBar current = null, compact = null; int factor = 1, pending = 0;
            void Append(ExplorerBar bar)
            {
                compact = compact == null ? bar : Merge(compact, bar);
                if (++pending < factor) { return; }
                output.Add(compact); compact = null; pending = 0;
                if (output.Count < 4000) { return; }
                for (int i = 0; i < 2000; i++) { output[i] = Merge(output[i * 2], output[i * 2 + 1]); }
                output.RemoveRange(2000, 2000); factor = checked(factor * 2);
            }
            foreach (ExplorerBar bar in bars)
            {
                cancellation.ThrowIfCancellationRequested();
                DateTime start = timeFrame == OrderFlowDisplayTimeFrame.Month1 ? new DateTime(bar.Start.Year, bar.Start.Month, 1) :
                    new DateTime(bar.Start.Ticks - bar.Start.Ticks % OrderFlowChartTimeFrames.GetDuration(timeFrame).Ticks);
                DateTime end = timeFrame == OrderFlowDisplayTimeFrame.Month1 ? start.AddMonths(1) : start + OrderFlowChartTimeFrames.GetDuration(timeFrame);
                if (current == null || current.Start != start)
                {
                    if (current != null) { Append(current); }
                    current = bar with { Start = start, End = end };
                }
                else { current = current with { High = Math.Max(current.High, bar.High), Low = Math.Min(current.Low, bar.Low), Close = bar.Close, Volume = OrderFlowVolumeComparison.AddExact(current.Volume, bar.Volume) }; }
            }
            if (current != null) { Append(current); }
            if (compact != null) { output.Add(compact); } return output;
        }
        private static ExplorerBar Merge(ExplorerBar first, ExplorerBar last) => first with
        { End = last.End, High = Math.Max(first.High, last.High), Low = Math.Min(first.Low, last.Low), Close = last.Close, Volume = OrderFlowVolumeComparison.AddExact(first.Volume, last.Volume) };
        #endregion
    }
}
