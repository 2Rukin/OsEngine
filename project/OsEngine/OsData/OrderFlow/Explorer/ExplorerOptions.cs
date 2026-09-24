/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.Entity;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OsEngine.OsData.OrderFlow.Explorer
{
    /// <summary>UI-owned editable text, converted to a fresh immutable record only when an operation is explicitly requested.</summary>
    internal sealed class ExplorerOption
    {
        internal PropertyInfo Property;
        internal string Initial;
        public string Name { get; init; }
        public string Help { get; init; }
        public string Value { get; set; }
    }
    internal static class ExplorerOptions
    {
        private static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            ["InputPath"] = "Файл сделок", ["OutputRootPath"] = "Папка результатов", ["PriceStep"] = "Шаг цены",
            ["FromDate"] = "Начальная дата", ["ToDate"] = "Конечная дата", ["MaximumBufferItems"] = "Лимит элементов",
            ["MaximumMemoryMegabytes"] = "Лимит памяти, МБ",
            ["AnchorVolume"] = "Объём значимого префикса Cloud", ["ControlMinutes"] = "Шаг контрольных якорей, минуты",
            ["CandidateBudget"] = "Бюджет правил", ["MaximumCards"] = "Максимум карточек", ["MinimumDates"] = "Минимум разных дат Fit",
            ["MinimumWeeks"] = "Минимум разных недель Fit", ["MinimumTestDates"] = "Минимум разных дат Test", ["Seed"] = "Seed воспроизводимого отбора",
            ["SingleTicks"] = "Одиночные сделки", ["AllTicks"] = "Все валидные сделки", ["MinimumTickVolume"] = "Минимальный объём сделки",
            ["MaximumGapMilliseconds"] = "Пауза формирования, мс", ["MaximumRangeTicks"] = "Размах формирования, шаги цены", ["ContextSeconds"] = "Окно окружения, секунды",
            ["AdaptTick"] = "Адаптивный отбор сделок", ["AdaptGap"] = "Адаптивная пауза", ["AdaptRange"] = "Адаптивный размах",
            ["RelativeVolume"] = "Относительный объём", ["TimeOfDayVolume"] = "Фон того же времени прежних дат",
            ["TickWindow"] = "Окно размеров сделок", ["TickMinimum"] = "Минимум истории размеров", ["TickPercentile"] = "Квантиль размера, 0..1",
            ["PaceSeconds"] = "Окно темпа, секунды", ["PaceMinimum"] = "Минимум сделок для темпа", ["PaceFactor"] = "Коэффициент паузы",
            ["GapMinimum"] = "Минимальная адаптивная пауза, мс", ["GapMaximum"] = "Максимальная адаптивная пауза, мс",
            ["AtrFactor"] = "Коэффициент ATR", ["RangeMinimum"] = "Минимальный размах, шаги", ["RangeMaximum"] = "Максимальный размах, шаги",
            ["VolumeWindow"] = "Окно законченных событий", ["VolumeMinimum"] = "Минимум фоновых событий", ["VolumeDates"] = "Число source-дат rolling фона",
            ["VolumePercentile"] = "Квантиль объёма, 0..1", ["TimeOfDayDates"] = "Предыдущие даты часового фона", ["TimeOfDayMinutes"] = "Полуширина часового фона, минуты",
            ["Enabled"] = "Включить модуль", ["Profile"] = "Слой / масштаб", ["MaximumPauseSeconds"] = "Пауза между Cloud, секунды",
            ["MaximumDurationSeconds"] = "Длительность эпизода, секунды", ["MaximumZoneTicks"] = "Зона эпизода, шаги", ["AdaptZone"] = "Зона эпизода по ATR",
            ["ZoneMinimum"] = "Минимальная зона, шаги", ["ZoneMaximum"] = "Максимальная зона, шаги",
            ["SwingsEnabled"] = "Подтверждённые экстремумы", ["EpisodeTrigger"] = "Триггер по эпизоду", ["RelativeTrigger"] = "Триггер по относительному порогу",
            ["TriggerVolume"] = "Фиксированный порог триггера", ["SwingReversalTicks"] = "Ручной разворот swing, шаги", ["AdaptSwing"] = "Разворот swing по ATR",
            ["SwingAtrFactor"] = "Коэффициент ATR swing", ["WatchMinutes"] = "Время наблюдения, минуты", ["ActivityGate"] = "Гейт активности",
            ["ActivitySeconds"] = "Окно активности, секунды", ["ActivityMinimum"] = "Минимум предыдущих сделок", ["ActivityMaximumPauseSeconds"] = "Максимальная пауза активности, секунды",
            ["StartHour"] = "Начальный час источника", ["EndHour"] = "Конечный час, не включён", ["VwapGate"] = "VWAP как условие пробоя",
            ["HorizonsMinutes"] = "Горизонты, минуты через ;", ["TargetAtr"] = "Цель в ATR", ["AdverseAtr"] = "Неблагоприятный барьер в ATR",
            ["MinimumVolume"] = "Минимальная сумма", ["MaximumVolume"] = "Максимальная сумма", ["MinimumCount"] = "Минимальное число сделок", ["MaximumCount"] = "Максимальное число сделок",
            ["Kind"] = "SingleTrade / Accumulated", ["MinimumDuration"] = "Мин. длительность, мс", ["MaximumDuration"] = "Макс. длительность, мс",
            ["MinimumRange"] = "Мин. фактический размах, шаги", ["MaximumRange"] = "Макс. фактический размах, шаги",
            ["MinimumDelta"] = "Мин. дельта Cloud, % объёма", ["MaximumDelta"] = "Макс. дельта Cloud, % объёма", ["AbsoluteDelta"] = "Модуль дельты Cloud",
            ["MinimumContextDelta"] = "Мин. дельта окружения, %", ["MaximumContextDelta"] = "Макс. дельта окружения, %", ["AbsoluteContextDelta"] = "Модуль дельты окружения",
            ["RelativeStatus"] = "Относительный статус", ["MinimumPercentile"] = "Мин. percentile, %", ["MaximumPercentile"] = "Макс. percentile, %",
            ["AdaptationStatus"] = "Причина fallback", ["DiagonalSource"] = "Диагональ Off / Inside / Context / Both", ["DiagonalDirection"] = "Направление Any / Buy / Sell",
            ["MinimumRatio"] = "Мин. диагональное ratio, %", ["MinimumDominant"] = "Мин. доминирующий объём", ["MinimumDifference"] = "Мин. диагональная разность",
            ["ShowFiltered"] = "Показывать отсечённые серым", ["ShowControl"] = "Показывать контрольные маркеры", ["RelatedId"] = "ID эпизода / наблюдения", ["Visible"] = "Показывать этот слой / масштаб"
        };
        internal static string NameOf(string field) => Names.TryGetValue(field, out string name) ? name : field;
        internal static List<ExplorerOption> Create<T>(T record, params string[] excluded)
        {
            List<ExplorerOption> options = new List<ExplorerOption>();
            foreach (PropertyInfo property in typeof(T).GetProperties().Where(p => p.SetMethod != null && !excluded.Contains(p.Name)))
            {
                object value = property.GetValue(record);
                string text = value is ImmutableArray<int> array ? string.Join(";", array) : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
                Type type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                string help = type == typeof(bool) ? "True — включено; False — выключено." : type.IsEnum ? string.Join(" / ", Enum.GetNames(type)) :
                    Nullable.GetUnderlyingType(property.PropertyType) != null ? "Пусто — без ограничения. Границы включительны." : "Изменение вычислительного параметра требует нового расчёта.";
                if (property.Name == "RelativeStatus") { help = "Пусто — все; Pass / Fail / Unknown. Неизвестный фон не считается отказом."; }
                if (property.Name == "Profile") { help = "Cloud1/Base или Cloud2/Base; при нескольких масштабах Narrow / Wide."; }
                options.Add(new ExplorerOption { Property = property, Name = Names.TryGetValue(property.Name, out string name) ? name : property.Name,
                    Value = text, Initial = text, Help = help });
            }
            return options;
        }
        internal static T Read<T>(T defaults, IEnumerable<ExplorerOption> options)
        {
            JsonObject document = JsonSerializer.SerializeToNode(defaults, ExplorerStorage.Json).AsObject();
            foreach (ExplorerOption option in options)
            {
                Type nullable = Nullable.GetUnderlyingType(option.Property.PropertyType), type = nullable ?? option.Property.PropertyType;
                string text = option.Value?.Trim() ?? ""; object value;
                if (text.Length == 0 && nullable != null) { document[option.Property.Name] = null; continue; }
                if (type == typeof(string)) { value = text.Length == 0 ? null : text; }
                else if (type == typeof(bool))
                {
                    ExplorerValidation.Require(bool.TryParse(text, out bool flag), option.Property.Name, "Поле «" + option.Name + "»: укажите True (да) или False (нет).");
                    value = flag;
                }
                else if (type.IsEnum)
                {
                    bool valid = Enum.TryParse(type, text, true, out object parsed) && Enum.IsDefined(type, parsed);
                    ExplorerValidation.Require(valid, option.Property.Name, "Поле «" + option.Name + "»: выберите " + string.Join(" / ", Enum.GetNames(type)) + ".");
                    value = parsed;
                }
                else if (type == typeof(decimal)) { value = ExplorerValidation.Number(text, option.Property.Name); }
                else if (type == typeof(int)) { value = ExplorerValidation.Integer(text, option.Property.Name); }
                else if (type == typeof(ImmutableArray<int>)) { value = text.Split(';').Select(s => ExplorerValidation.Integer(s, option.Property.Name)).ToImmutableArray(); }
                else { throw new InvalidOperationException("Unsupported Explorer option type."); }
                document[option.Property.Name] = JsonSerializer.SerializeToNode(value, option.Property.PropertyType, ExplorerStorage.Json);
            }
            return document.Deserialize<T>(ExplorerStorage.Json);
        }
        internal static T ReadScoped<T>(T defaults, IEnumerable<ExplorerOption> options, string scope)
        { try { return Read(defaults, options); } catch (ExplorerInputException error) { throw ExplorerValidation.Scoped(error, scope); } }
    }
}
