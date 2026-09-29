/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace OsEngine.OsData.OrderFlow.Context
{
    /// <summary>Display-only selection. Changing filters never removes calculated regions, events or labels.</summary>
    internal sealed class FlowContextView
    {
        public bool Enabled { get; set; } = true;
        public bool Regions { get; set; } = true;
        public bool Vwap { get; set; } = true;
        public bool Twap { get; set; } = true;
        public bool Bands { get; set; } = true;
        public bool Events { get; set; } = true;
        public bool Swings { get; set; } = true;
        public bool AllRegions { get; set; }
        public int Scale { get; set; } = -1;
        public string EventKind { get; set; } = "Все";
        public string SelectedRegionId { get; set; }
        public string SelectedEventId { get; set; }
        public decimal SigmaMultiplier { get; set; } = 1;
        public decimal MinimumEventVolume { get; set; }
        public bool WarmedUpOnly { get; set; }
        public bool Matches(FlowContextEvent item) => (Scale < 0 || item.Coordinates.Any(coordinate => coordinate.Scale == Scale)) && (EventKind == "Все" || item.Kind == EventKind) && item.Volume >= MinimumEventVolume && (!WarmedUpOnly || item.WarmedUp);
    }
    internal sealed class FlowContextStatisticsRow
    {
        public string Событие { get; set; }
        public string Контекст { get; set; }
        public string Направление { get; set; }
        public string Горизонт { get; set; }
        public int Наблюдений { get; set; }
        public int Завершено { get; set; }
        public int Неполных { get; set; }
        public int Дней { get; set; }
        public int Семейств { get; set; }
        public decimal? СреднееШагов { get; set; }
        public decimal? МедианаШагов { get; set; }
        public decimal? СреднееMFE { get; set; }
        public decimal? СреднееMAE { get; set; }
        public decimal? ЦельПервойПроцент { get; set; }
    }
    internal static class FlowContextPresentation
    {
        internal static string Describe(FlowContextEvent item, FlowContextResult result, bool replay)
        {
            StringBuilder text = new StringBuilder();
            text.Append(item.Kind).Append(" · известно ").Append(item.Time.ToString("dd.MM HH:mm:ss.ffffff")).Append(" · цена ").Append(item.Price.ToString("G29"));
            if (item.ObservedAt != item.Time) { text.Append(" · исходное событие ").Append(item.ObservedAt.ToString("HH:mm:ss.ffffff")); }
            text.Append("\nОбъём ").Append(item.Volume).Append(" · Δ ").Append(item.Delta).Append(" · сделок ").Append(item.Trades).Append(" · движение ").Append(item.ProgressTicks.ToString("0.##")).Append(" шагов · TPS ×").Append(item.TpsRatio.ToString("0.##"));
            foreach (FlowContextCoordinate coordinate in item.Coordinates.OrderByDescending(value => value.Scale))
            {
                text.Append("\n").Append(result.Settings.Scales[coordinate.Scale].Name).Append(" [").Append(coordinate.RegionId).Append("] · VWAP ").Append(Signed(coordinate.VwapTicks)).Append(" шаг.")
                    .Append(" · TWAP ").Append(coordinate.TwapTicks.HasValue ? Signed(coordinate.TwapTicks.Value) : "нет времени").Append(" · z ").Append(coordinate.SigmaDistance?.ToString("0.##") ?? "σ=0")
                    .Append(" · ").Append(coordinate.Position > 0 ? "выше области" : coordinate.Position < 0 ? "ниже области" : "в области / у границы")
                    .Append(" · возврат №").Append(coordinate.Retests).Append(" · возраст ").Append((coordinate.AgeSeconds / 60).ToString("0.#")).Append(" мин");
            }
            if (!replay)
            {
                foreach (FlowContextOutcome outcome in result.Outcomes.Where(value => value.EventId == item.Id))
                { text.Append("\n").Append(outcome.Horizon).Append(" · ").Append(outcome.Status).Append(" · результат ").Append(outcome.ChangeTicks?.ToString("0.##") ?? "—").Append(" · MFE/MAE ").Append(outcome.MfeTicks.ToString("0.##")).Append('/').Append(outcome.MaeTicks.ToString("0.##")).Append(" · ").Append(outcome.Barrier); }
            }
            if (!item.WarmedUp) { text.Append("\nПериод прогрева: событие сохранено, в статистику исходов не включено."); }
            return text.ToString();
        }
        private static string Signed(decimal value) => value.ToString("+0.##;-0.##;0", CultureInfo.CurrentCulture);
        internal static List<FlowContextStatisticsRow> Statistics(FlowContextResult result, FlowContextView view)
        {
            Dictionary<string, FlowContextEvent> events = result.Events.Where(view.Matches).ToDictionary(item => item.Id);
            return result.Outcomes.Where(outcome => events.ContainsKey(outcome.EventId)).GroupBy(outcome => (events[outcome.EventId].Kind, events[outcome.EventId].Direction, Context(events[outcome.EventId]), outcome.Horizon)).Select(group =>
            {
                FlowContextOutcome[] complete = group.Where(outcome => outcome.Status == "Complete").ToArray();
                decimal[] changes = complete.Select(outcome => outcome.ChangeTicks.Value).OrderBy(value => value).ToArray();
                return new FlowContextStatisticsRow { Событие = group.Key.Kind, Направление = group.Key.Direction > 0 ? "Вверх" : "Вниз", Контекст = group.Key.Item3, Горизонт = group.Key.Horizon, Наблюдений = group.Count(), Завершено = complete.Length,
                    Неполных = group.Count() - complete.Length, Дней = group.Select(outcome => events[outcome.EventId].Time.Date).Distinct().Count(),
                    Семейств = group.Select(outcome => events[outcome.EventId].Coordinates.OrderByDescending(value => value.Scale).FirstOrDefault()?.RegionId ?? events[outcome.EventId].Time.Date.ToString("yyyyMMdd")).Distinct().Count(),
                    СреднееШагов = changes.Length == 0 ? null : changes.Average(), МедианаШагов = changes.Length == 0 ? null : (changes[(changes.Length - 1) / 2] + changes[changes.Length / 2]) / 2,
                    СреднееMFE = complete.Length == 0 ? null : complete.Average(outcome => outcome.MfeTicks), СреднееMAE = complete.Length == 0 ? null : complete.Average(outcome => outcome.MaeTicks),
                    ЦельПервойПроцент = complete.Length == 0 ? null : 100m * complete.Count(outcome => outcome.Barrier == "TargetFirst") / complete.Length };
            }).OrderByDescending(row => row.Наблюдений).ToList();
        }
        private static string Context(FlowContextEvent item)
        {
            return string.Join("; ", Enumerable.Range(0, 3).Select(scale =>
            {
                FlowContextCoordinate value = item.Coordinates.FirstOrDefault(coordinate => coordinate.Scale == scale);
                return new[] { "Лок", "Раб", "Ст" }[scale] + ": " + (value == null ? "нет области" : (value.VwapTicks >= 0 ? "выше V" : "ниже V") + (value.TwapTicks.HasValue ? value.TwapTicks >= 0 ? "/выше T" : "/ниже T" : "/T нет") + (value.Position > 0 ? "/над диапазоном" : value.Position < 0 ? "/под диапазоном" : "/в диапазоне"));
            }));
        }
    }
}
