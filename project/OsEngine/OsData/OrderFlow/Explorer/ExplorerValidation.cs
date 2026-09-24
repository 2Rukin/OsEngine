/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.Entity;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OsEngine.OsData.OrderFlow.Explorer
{
    /// <summary>Expected input rejection with a stable field key for focus routing; never represents an unexpected worker failure.</summary>
    internal sealed class ExplorerInputException : ArgumentException
    {
        internal string Field { get; }
        internal string Profile { get; }
        internal ExplorerInputException(string field, string message, string profile = null, Exception inner = null)
            : base(message, field, inner) { Field = field; Profile = profile; }
    }

    /// <summary>Deterministic, field-addressed validation shared by UI and workers; source/output access probes are explicit UI preflight only.</summary>
    internal static class ExplorerValidation
    {
        internal static void Require(bool valid, string field, string message, string profile = null)
        { if (!valid) { throw new ExplorerInputException(field, message, profile); } }

        internal static decimal Number(string text, string field)
        {
            const NumberStyles style = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
            string value = text?.Trim() ?? "";
            bool valid = decimal.TryParse(value, style, CultureInfo.InvariantCulture, out decimal parsed) ||
                decimal.TryParse(value, style, CultureInfo.GetCultureInfo("ru-RU"), out parsed);
            Require(valid, field, $"Поле «{ExplorerOptions.NameOf(field)}»: введите число в допустимом диапазоне; дробную часть отделяйте точкой или запятой.");
            return value.ToDecimal();
        }
        internal static int Integer(string text, string field)
        {
            decimal number = Number(text, field);
            Require(number == decimal.Truncate(number) && number >= int.MinValue && number <= int.MaxValue,
                field, $"Поле «{ExplorerOptions.NameOf(field)}»: введите целое число от {int.MinValue} до {int.MaxValue}.");
            return (int)number;
        }
        private static void Minimum(decimal value, decimal minimum, string field, string profile = null)
        { Require(value >= minimum, field, $"Поле «{ExplorerOptions.NameOf(field)}»: значение должно быть не меньше {minimum}.", profile); }
        private static void Positive(decimal value, string field, string profile = null)
        { Require(value > 0, field, $"Поле «{ExplorerOptions.NameOf(field)}»: значение должно быть положительным.", profile); }
        private static void Interval(decimal value, decimal low, decimal high, string field, string profile = null)
        { Require(value >= low && value <= high, field, $"Поле «{ExplorerOptions.NameOf(field)}»: допустимы значения от {low} до {high}.", profile); }
        internal static void Profile(ExplorerProfile p)
        {
            Require(p != null, "Profiles", "Профиль не задан. Выберите слой и масштаб.");
            string key = p.Key;
            Require(p.Layer == "Cloud1" || p.Layer == "Cloud2", "Layer", "Выберите слой Cloud1 или Cloud2.", key);
            Require(!string.IsNullOrWhiteSpace(p.Scale) && p.Scale.All(char.IsLetterOrDigit), "Scale", "Название масштаба должно состоять из букв и цифр.", key);
            Positive(p.MinimumTickVolume, "MinimumTickVolume", key);
            Minimum(p.MaximumGapMilliseconds, 0, "MaximumGapMilliseconds", key); Minimum(p.MaximumRangeTicks, 0, "MaximumRangeTicks", key);
            Minimum(p.ContextSeconds, 1, "ContextSeconds", key); Minimum(p.TickMinimum, 1, "TickMinimum", key);
            Interval(p.TickWindow, p.TickMinimum, 100000, "TickWindow", key); Positive(p.TickPercentile, "TickPercentile", key);
            Interval(p.TickPercentile, 0, 1, "TickPercentile", key); Minimum(p.PaceSeconds, 1, "PaceSeconds", key);
            Minimum(p.PaceMinimum, 1, "PaceMinimum", key); Positive(p.PaceFactor, "PaceFactor", key);
            Minimum(p.GapMinimum, 0, "GapMinimum", key); Minimum(p.GapMaximum, p.GapMinimum, "GapMaximum", key);
            Positive(p.AtrFactor, "AtrFactor", key); Minimum(p.RangeMinimum, 1, "RangeMinimum", key);
            Minimum(p.RangeMaximum, p.RangeMinimum, "RangeMaximum", key); Minimum(p.VolumeMinimum, 1, "VolumeMinimum", key);
            Interval(p.VolumeWindow, p.VolumeMinimum, 100000, "VolumeWindow", key); Minimum(p.VolumeDates, 1, "VolumeDates", key);
            Positive(p.VolumePercentile, "VolumePercentile", key); Interval(p.VolumePercentile, 0, 1, "VolumePercentile", key);
            Interval(p.TimeOfDayDates, 1, 100, "TimeOfDayDates", key); Interval(p.TimeOfDayMinutes, 0, 720, "TimeOfDayMinutes", key);
        }
        internal static void Run(ExplorerRunSpec s)
        {
            Require(!string.IsNullOrWhiteSpace(s.InputPath), "InputPath", "Укажите существующий файл сделок в основном окне Order Flow.");
            Require(!string.IsNullOrWhiteSpace(s.OutputRootPath), "OutputRootPath", "Укажите папку результатов в основном окне Order Flow.");
            Positive(s.PriceStep, "PriceStep");
            Require(s.FromDate.HasValue == s.ToDate.HasValue, "FromDate", "Заполните обе даты либо оставьте обе пустыми.");
            Require(!(s.FromDate > s.ToDate), "FromDate", "Начальная дата позже конечной. Поменяйте даты местами или выберите другой период.");
            Require(!s.Profiles.IsDefaultOrEmpty, "Profiles", "Включите хотя бы один слой Cloud.");
            Require(s.Profiles.Length <= 6 && s.Profiles.All(p => p != null), "Profiles", "Допустимы от одного до шести непустых профилей Cloud.");
            Require(s.Profiles.Select(p => p.Key).Distinct().Count() == s.Profiles.Length, "Profiles", "Профили слоя и масштаба не должны повторяться.");
            Require(s.Profiles.GroupBy(p => p.Layer).All(g => g.Count() <= 3), "Profiles", "На один слой допускается не больше трёх масштабов.");
            Minimum(s.MaximumBufferItems, 1000, "MaximumBufferItems"); Minimum(s.MaximumMemoryMegabytes, 64, "MaximumMemoryMegabytes");
            foreach (ExplorerProfile profile in s.Profiles) { Profile(profile); }
            ExplorerEpisodeSpec e = s.Episodes; ExplorerStudySpec t = s.Study;
            Require(e != null, "Episodes", "Задайте параметры модуля эпизодов."); Require(t != null, "Study", "Задайте параметры исследования.");
            try
            {
            Minimum(e.MaximumPauseSeconds, 0, "MaximumPauseSeconds"); Minimum(e.MaximumDurationSeconds, 0, "MaximumDurationSeconds");
            Minimum(e.MaximumZoneTicks, 0, "MaximumZoneTicks"); Positive(e.AtrFactor, "AtrFactor");
            Minimum(e.ZoneMinimum, 1, "ZoneMinimum"); Minimum(e.ZoneMaximum, e.ZoneMinimum, "ZoneMaximum");
            Require(!e.Enabled || s.Profiles.Any(p => p.Key == e.Profile), "Episodes.Profile", "Выбранный профиль эпизодов не участвует в расчёте. Включите его слой/масштаб или смените профиль.");
            }
            catch (ExplorerInputException error) { throw Scoped(error, "Episodes"); }
            try
            {
            Require(!t.Enabled || s.Profiles.Any(p => p.Key == t.Profile), "Study.Profile", "Выбранный профиль исследования не участвует в расчёте.");
            Require(!t.Enabled || !t.EpisodeTrigger || (e.Enabled && t.Profile == e.Profile), "EpisodeTrigger", "Для триггера по эпизоду включите эпизоды и выберите одинаковый профиль эпизодов и исследования.");
            Positive(t.TriggerVolume, "TriggerVolume"); Minimum(t.SwingReversalTicks, 1, "SwingReversalTicks"); Positive(t.SwingAtrFactor, "SwingAtrFactor");
            Interval(t.WatchMinutes, 1, 1440, "WatchMinutes"); Minimum(t.ActivitySeconds, 1, "ActivitySeconds"); Minimum(t.ActivityMinimum, 1, "ActivityMinimum");
            Minimum(t.ActivityMaximumPauseSeconds, 0, "ActivityMaximumPauseSeconds"); Hours(t.StartHour, t.EndHour);
            Require(!t.HorizonsMinutes.IsDefaultOrEmpty && t.HorizonsMinutes.Length <= 12 && t.HorizonsMinutes.All(h => h >= 1 && h <= 1440) &&
                t.HorizonsMinutes.Distinct().Count() == t.HorizonsMinutes.Length, "HorizonsMinutes", "Поле «Горизонты»: укажите от 1 до 12 разных целых минут от 1 до 1440 через точку с запятой.");
            Positive(t.TargetAtr, "TargetAtr"); Positive(t.AdverseAtr, "AdverseAtr");
            Require(!t.Enabled || !t.RelativeTrigger || t.EpisodeTrigger || s.StudyProfile.RelativeVolume || s.StudyProfile.TimeOfDayVolume,
                "RelativeTrigger", "Для относительного триггера включите «Относительный объём» или «Фон того же времени прежних дат» выбранного профиля и пересчитайте каталог.");
            }
            catch (ExplorerInputException error) { throw Scoped(error, "Study"); }
        }
        internal static ExplorerInputException Scoped(ExplorerInputException error, string scope) => new ExplorerInputException(
            error.Field.StartsWith(scope + ".", StringComparison.Ordinal) ? error.Field : scope + "." + error.Field, UserMessage(error, ""), error.Profile, error);
        private static void Hours(int start, int end)
        { Interval(start, 0, 23, "StartHour"); Interval(end, start + 1, 24, "EndHour"); }
        internal static void View(ExplorerView view)
        {
            Hours(view.StartHour, view.EndHour);
            Require(view.Kind == null || view.Kind == "SingleTrade" || view.Kind == "Accumulated", "Kind", "Тип Cloud: оставьте пусто либо укажите SingleTrade или Accumulated.");
            Require(view.RelativeStatus == null || new[] { "Pass", "Fail", "Unknown" }.Contains(view.RelativeStatus), "RelativeStatus", "Статус: оставьте пусто либо укажите Pass, Fail или Unknown.");
            foreach (string suffix in new[] { "Volume", "Count", "Duration", "Range", "Delta", "ContextDelta", "Percentile" })
            {
                object min = typeof(ExplorerView).GetProperty("Minimum" + suffix).GetValue(view), max = typeof(ExplorerView).GetProperty("Maximum" + suffix).GetValue(view);
                decimal? low = min == null ? null : Convert.ToDecimal(min), high = max == null ? null : Convert.ToDecimal(max);
                Require(!low.HasValue || !high.HasValue || low <= high, "Minimum" + suffix, $"Поле «{ExplorerOptions.NameOf("Minimum" + suffix)}»: минимум не должен превышать максимум.");
                decimal lower = suffix == "Delta" || suffix == "ContextDelta" ? -100 : 0;
                decimal upper = suffix == "Delta" || suffix == "ContextDelta" || suffix == "Percentile" ? 100 : decimal.MaxValue;
                if (low.HasValue) { Interval(low.Value, lower, upper, "Minimum" + suffix); }
                if (high.HasValue) { Interval(high.Value, lower, upper, "Maximum" + suffix); }
            }
            Require(Enum.IsDefined(view.DiagonalSource), "DiagonalSource", "Выберите допустимый источник диагонального перевеса.");
            Require(Enum.IsDefined(view.DiagonalDirection), "DiagonalDirection", "Выберите допустимое направление диагонального перевеса.");
            Minimum(view.MinimumRatio, 0, "MinimumRatio"); Minimum(view.MinimumDominant, 0, "MinimumDominant"); Minimum(view.MinimumDifference, 0, "MinimumDifference");
        }
        internal static void Preflight(ExplorerRunSpec spec)
        {
            spec.Validate();
            try { using FileStream input = new FileStream(spec.InputPath, FileMode.Open, FileAccess.Read, FileShare.Read); }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException || error is NotSupportedException)
            { throw new ExplorerInputException("InputPath", "Не удалось открыть файл сделок. Проверьте существование файла, путь и права чтения.", inner: error); }
            try
            {
                Directory.CreateDirectory(spec.OutputRootPath);
                string probe = Path.Combine(spec.OutputRootPath, ".explorer-write-" + Guid.NewGuid().ToString("N"));
                using FileStream output = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException || error is NotSupportedException)
            { throw new ExplorerInputException("OutputRootPath", "Не удалось создать папку результатов или записать в неё. Проверьте путь, свободное место и права.", inner: error); }
        }
        internal static string UserMessage(Exception error, string attempt)
        {
            if (error is ExplorerInputException) { return error.Message.Split(" (Parameter", StringSplitOptions.None)[0]; }
            if (error is InvalidDataException && error.Message.StartsWith("Line ", StringComparison.Ordinal))
            {
                int separator = error.Message.IndexOf(':');
                string reason = separator < 0 ? "Нарушен формат строки." : error.Message.Substring(separator + 1).Trim() switch
                {
                    "Expected seven comma-separated fields." => "Ожидаются семь полей, разделённых запятыми.",
                    "Date/Time must be yyyyMMdd,HHmmss." => "Дата и время должны иметь формат yyyyMMdd,HHmmss.",
                    "MicroSeconds must be an integer from 0 to 999999." => "MicroSeconds: целое число от 0 до 999999.",
                    "Tick time moves backwards; file order is not repaired." => "Время сделки меньше предыдущего. Исправьте порядок в источнике; автоматической перестановки нет.",
                    "Price and Volume must be positive decimal numbers with a dot separator." => "Цена и объём должны быть положительными числами с точкой.",
                    "Side must be Buy or Sell; it is never inferred from price." => "Сторона сделки: Buy или Sell; по цене она не угадывается.",
                    "Line exceeds 4096 characters." => "Строка длиннее допустимых 4096 символов.", _ => "Нарушен формат тиков. Подробности в журнале, ID " + attempt
                };
                return "Строка " + (separator < 0 ? "?" : error.Message.Substring(5, separator - 5)) + ": " + reason;
            }
            if (error is OperationCanceledException) { return "Расчёт отменён; незаконченный результат не опубликован."; }
            if (error is OutOfMemoryException || error.Message.Contains("memory", StringComparison.OrdinalIgnoreCase)) { return "Не хватает памяти для текущего лимита. Уменьшите окна/число профилей или увеличьте лимит памяти."; }
            if (error.Message.Contains("limit exceeded", StringComparison.OrdinalIgnoreCase)) { return "Превышен лимит элементов буфера. Уменьшите окна/число профилей или осознанно увеличьте лимит элементов в 2.1."; }
            if (error is OverflowException || error.Message.Contains("representable", StringComparison.OrdinalIgnoreCase)) { return "Числа или вычисленный порог выходят за точность decimal. Проверьте шаг цены, объёмы и коэффициенты; результат не опубликован."; }
            if (error.Message.Contains("SHA", StringComparison.OrdinalIgnoreCase)) { return "Исходный файл изменён. Для открытия/реплея выберите исходный файл с тем же SHA; иначе выполните новый расчёт."; }
            if (error.Message.Contains("checksum", StringComparison.OrdinalIgnoreCase)) { return "Сохранённый результат повреждён: контрольная сумма файла не совпадает. Выберите неповреждённый результат или пересчитайте его."; }
            if (error is FileNotFoundException || error is DirectoryNotFoundException) { return "Файл или папка не найдены. Проверьте выбранный путь и наличие всех файлов результата."; }
            if (error is UnauthorizedAccessException) { return "Нет доступа к файлу или папке. Проверьте права чтения и записи."; }
            if (error is JsonException || (error is InvalidDataException && (error.Message.Contains("version", StringComparison.OrdinalIgnoreCase) || error.Message.Contains("schema", StringComparison.OrdinalIgnoreCase))))
            { return "Результат повреждён или имеет неподдерживаемую версию. Выберите совместимый manifest либо пересчитайте данные."; }
            if (error is IOException && error is not InvalidDataException) { return "Ошибка чтения или записи. Проверьте диск, свободное место и доступ к файлам. Диагностика: " + attempt; }
            if ((error is ArgumentException || error is InvalidOperationException || error is InvalidDataException) && error.Message.Any(c => c >= 'А' && c <= 'я')) { return error.Message; }
            return "Не удалось завершить операцию. Сохраните ID попытки «" + attempt + "» и проверьте журнал ошибок.";
        }
        internal static bool NeedsDiagnosticFile(Exception error) => error.InnerException != null ||
            (error is not ExplorerInputException && !(error is InvalidOperationException && error.Message.Any(c => c >= 'А' && c <= 'я')));

        internal static string Status(string code) => code switch
        {
            "Unknown" => "Неизвестно", "NoAtr" => "Неизвестно: недостаточно ATR", "Incomplete" => "Неполный горизонт", "DateCutoff" => "Горизонт пересекает границу даты",
            "NoFutureTrade" => "Нет будущих сделок", "Overlap" => "Перекрытие будущих окон", "Complete" => "Полный горизонт", "Target" => "Цель первой", "Adverse" => "Неблагоприятный барьер первым",
            "Timeout" => "Барьер не достигнут", "IncompleteWeek" => "Неполная неделя: нет предыстории", "WeekEmpty" => "Прежних дней недели пока нет", "Available" => "Контекст доступен",
            "EOF" => "Конец файла", "OpenAtEnd" => "Не завершён на конце файла", "EpisodeOpenAtEnd" => "Эпизод не завершён на конце файла",
            "Pass" => "Прошёл", "Fail" => "Не прошёл", "Known" => "Известно", "Unavailable" => "Недоступно", "Forming" => "Формируется",
            "Gap" => "Пауза", "Range" => "Граница цены", "SingleTick" => "Одиночная сделка", "Boundary" => "Граница группировки", "Breakout" => "Пробой", "Control" => "Контроль", "VolumeArmed" => "Объёмный триггер",
            "Catalog" => "Каталог", "Catalog complete" => "Каталог готов", "Episodes" => "Эпизоды", "Study" => "Исследование", _ => code
        };
    }
}
