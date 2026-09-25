/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.Entity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace OsEngine.OsData.OrderFlow.Calibration
{
    internal sealed record AnatomyTick(long SourceSequence, DateTime Time, decimal Price, decimal Volume, string Side,
        long Ordinal, decimal GapMilliseconds, decimal DeviationTicks, string TimeRangeId);
    internal sealed record AnatomyLevel(decimal Price, decimal BuyVolume, decimal SellVolume, decimal Delta, long TradeCount, decimal SharePercent, string TimeRangeId)
    {
        public string Direction => Delta > 0 ? "Buy" : Delta < 0 ? "Sell" : "Neutral";
    }

    /// <summary>Exact source-bound anatomy using compact normalized cache, never rereading raw text or inferring membership from timestamps alone.</summary>
    internal static class CalibrationAnatomy
    {
        internal static IEnumerable<AnatomyTick> Ticks(CalibrationRun run, FormationSpec formation, CalibrationEvent item,
            bool context, CancellationToken cancellation)
        {
            long ordinal = 0; DateTime? previous = null;
            foreach (OrderFlowDeal tick in CalibrationCache.Read(run.Directory, cancellation, context ? 0 : item.Evidence.FirstSequence, item.Evidence.LastSequence))
            {
                if (tick.Time.Date != item.Evidence.Time.Date || !run.Spec.Range.Includes(tick.Time)) { continue; }
                if (context)
                { if (tick.Time < item.Evidence.Time.AddSeconds(-formation.ContextSeconds)) { continue; } }
                else if (tick.Volume < formation.MinimumTickVolume) { continue; }
                yield return new AnatomyTick(tick.SourceSequence, tick.Time, tick.Price, tick.Volume, tick.Side.ToString(), ++ordinal,
                    previous.HasValue ? (tick.Time.Ticks - previous.Value.Ticks) / (decimal)TimeSpan.TicksPerMillisecond : 0,
                    (tick.Price - item.Evidence.FirstPrice) / run.Spec.PriceStep, run.Spec.Range.Id);
                previous = tick.Time;
            }
        }

        internal static IEnumerable<AnatomyLevel> Levels(CalibrationRun run, FormationSpec formation, CalibrationEvent item, CancellationToken cancellation)
        {
            SortedDictionary<decimal, AnatomyLevel> levels = new SortedDictionary<decimal, AnatomyLevel>();
            foreach (AnatomyTick tick in Ticks(run, formation, item, false, cancellation))
            {
                if (!levels.TryGetValue(tick.Price, out AnatomyLevel level)) { level = new AnatomyLevel(tick.Price, 0, 0, 0, 0, 0, run.Spec.Range.Id); }
                decimal buy = tick.Side == "Buy" ? OrderFlowVolumeComparison.AddExact(level.BuyVolume, tick.Volume) : level.BuyVolume;
                decimal sell = tick.Side == "Sell" ? OrderFlowVolumeComparison.AddExact(level.SellVolume, tick.Volume) : level.SellVolume;
                levels[tick.Price] = level with { BuyVolume = buy, SellVolume = sell, Delta = buy - sell, TradeCount = level.TradeCount + 1,
                    SharePercent = OrderFlowVolumeComparison.AddExact(buy, sell) / item.Volume * 100 };
                if (levels.Count > run.Spec.MaximumBufferItems) { throw new System.IO.InvalidDataException("Превышен лимит anatomy levels."); }
            }
            foreach (AnatomyLevel level in levels.Values) { cancellation.ThrowIfCancellationRequested(); yield return level; }
        }

        internal static IEnumerable<DiagonalPairRow> Pairs(CalibrationRun run, CalibrationEvent item, DiagonalSettings settings, CancellationToken cancellation)
        {
            List<DiagonalPairRow> rows = new List<DiagonalPairRow>();
            DiagonalMetrics.Calculate(settings.Source == DiagonalSource.Inside ? item.Evidence.Inside : item.Evidence.Context,
                run.Spec.PriceStep, settings, rows, cancellation);
            return rows.Select(row => row with { TimeRangeId = run.Spec.Range.Id });
        }
    }

    internal sealed record CalibrationEventRow(string EventId, string CloudId, string TimeRangeId, string Direction,
        DateTime StartTime, DateTime LastIncludedTime, DateTime? KnownAt, string CompletionReason,
        long FirstSourceSequence, long LastSourceSequence, decimal FirstPrice, decimal LastPrice, decimal Low, decimal High,
        decimal RangeTicks, decimal DurationMilliseconds, int TradeCount, decimal TotalVolume, decimal BuyVolume, decimal SellVolume,
        decimal Delta, decimal DeltaPercent, decimal LargestTick, decimal VWAP, decimal InsideDiagonalDelta,
        decimal InsideDiagonalDeltaPercent, decimal ContextDiagonalDelta, decimal ContextDiagonalDeltaPercent, int BuyStack, int SellStack)
    {
        internal static CalibrationEventRow Create(CalibrationEvent item, CalibrationSpec spec, CloudRule rule = null)
        {
            DiagonalSettings settings = rule?.Filters.Diagonal ?? spec.Diagonal;
            DiagonalMetrics inside = DiagonalMetrics.Calculate(item.Evidence.Inside, spec.PriceStep, settings);
            DiagonalMetrics context = DiagonalMetrics.Calculate(item.Evidence.Context, spec.PriceStep, settings);
            return new CalibrationEventRow(item.EventId, rule == null ? "" : item.CloudId(rule), item.TimeRangeId,
                item.Delta > 0 ? "Buy" : item.Delta < 0 ? "Sell" : "Neutral", item.Evidence.StartTime, item.Evidence.Time,
                item.Evidence.KnownAt, item.Evidence.Reason, item.Evidence.FirstSequence, item.Evidence.LastSequence,
                item.Evidence.FirstPrice, item.Evidence.Price, item.Evidence.Low, item.Evidence.High,
                (item.Evidence.High - item.Evidence.Low) / spec.PriceStep, item.Evidence.DurationMilliseconds, item.Evidence.Count,
                item.Volume, item.Evidence.Buy, item.Evidence.Sell, item.Delta, item.DeltaPercent, item.LargestTick, item.Evidence.Vwap,
                inside.Delta, inside.DeltaPercent, context.Delta, context.DeltaPercent,
                settings.Source == DiagonalSource.Inside ? inside.BuyStack : context.BuyStack,
                settings.Source == DiagonalSource.Inside ? inside.SellStack : context.SellStack);
        }
    }
}
