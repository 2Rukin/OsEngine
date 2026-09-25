using OsEngine.Language;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace OsEngine.OsData.OrderFlow.Calibration
{
    /// <summary>Display-only localization. Column keys, enum values, saved identities and numeric values remain unchanged.</summary>
    internal sealed class CalibrationText : IValueConverter
    {
        internal static string Label(string key)
        {
            if (key == null) { return ""; }
            foreach (string suffix in new[] { " P50", " P95", " P99", "P50", "P95", "P99" })
            { if (key.EndsWith(suffix, StringComparison.Ordinal)) { return Label(key.Substring(0, key.Length - suffix.Length)) + " " + suffix.Trim(); } }
            string ru = key switch
            {
                "All" => "Все", "Any" => "Любое", "Buy" => "Покупка", "Sell" => "Продажа", "Neutral" => "Нейтральное",
                "Single" => "Одна сделка", "Chain" => "Цепочка", "Standard" => "Обычное", "Diagonal" => "Диагональное",
                "Inside" => "Внутри", "Context" => "Контекст", "Passed rule" => "Прошедшие правило",
                "Count" or "Events" => "События", "Volume" or "TotalVolume" => "Объём", "BuyVolume" => "Объём покупок", "SellVolume" => "Объём продаж",
                "Delta" => "Дельта", "AbsoluteDelta" or "abs(Delta)" => "Модуль дельты", "DeltaPercent" => "Дельта, %", "AbsoluteDeltaPercent" => "Модуль дельты, %",
                "DiagonalDelta" => "Диагональная дельта", "AbsoluteDiagonalDelta" or "abs(DiagonalDelta)" => "Модуль диагональной дельты",
                "DiagonalDeltaPercent" => "Диагональная дельта, %", "TradeCount" => "Число сделок", "Duration" or "DurationMilliseconds" => "Длительность, мс",
                "Range" or "RangeTicks" => "Размах, ticks", "LargestTick" => "Крупнейшая сделка", "LargestTickShare" => "Доля крупнейшей сделки, %",
                "LevelCount" or "PriceLevels" => "Число уровней", "MaximumLevelShare" or "TopLevelShare" => "Макс. доля уровня, %", "StackLength" => "Длина стека",
                "MinimumTickVolume" => "Мин. объём сделки", "Gap" or "GapMilliseconds" => "Пауза, мс", "NeighborSensitivity" => "Чувствительность к соседям, %",
                "Chains / active day" or "ChainsPerDay" => "Цепочек / активный день", "Singles / active day" or "SinglesPerDay" => "Одиночных / активный день",
                "ActiveDays" => "Активные дни", "Profile" => "Профиль", "Name" => "Имя", "Enabled" => "Включено", "Visible" => "Видимо",
                "FormationMode" => "Формирование", "RuleKind" => "Тип правила", "Direction" or "Side" => "Направление",
                "Time" => "Время", "StartTime" => "Начало", "LastIncludedTime" => "Последний включённый тик", "KnownAt" => "Известен с",
                "CompletionReason" => "Причина завершения", "SourceSequence" => "Номер исходной строки", "FirstSourceSequence" => "Первая строка",
                "LastSourceSequence" => "Последняя строка", "Ordinal" => "Порядковый номер", "Price" => "Цена", "FirstPrice" => "Первая цена",
                "LastPrice" => "Последняя цена", "Low" => "Минимум цены", "High" => "Максимум цены", "DeviationTicks" => "Отклонение, ticks",
                "SharePercent" => "Доля объёма, %", "LowerPrice" => "Нижняя цена", "UpperPrice" => "Верхняя цена", "SellLower" => "Продажи снизу",
                "BuyUpper" => "Покупки сверху", "PairDiagonalDelta" => "Дельта пары", "BuyRatioPercent" => "Buy ratio, %", "SellRatioPercent" => "Sell ratio, %",
                "StackId" => "Номер стека", "StackOrdinal" => "Позиция в стеке", "BuyStack" => "Стек покупок", "SellStack" => "Стек продаж",
                "InsideDiagonalDelta" => "Inside дельта", "InsideDiagonalDeltaPercent" => "Inside дельта, %", "ContextDiagonalDelta" => "Context дельта",
                "ContextDiagonalDeltaPercent" => "Context дельта, %", "TimeRangeId" => "ID диапазона", "CloudId" => "ID Cloud", "RuleId" => "ID правила",
                "EventId" => "ID события", "FormationHash" => "Hash формирования", "InputSha256" => "SHA-256 файла",
                _ => key
            };
            return OsLocalization.ConvertToLocString("Eng:" + key + "_Ru:" + ru + "_");
        }

        internal static void LocalizeItems(ComboBox combo)
        {
            FrameworkElementFactory text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new Binding { Converter = new CalibrationText() });
            combo.ItemTemplate = new DataTemplate { VisualTree = text };
        }
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Label(value?.ToString());
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
