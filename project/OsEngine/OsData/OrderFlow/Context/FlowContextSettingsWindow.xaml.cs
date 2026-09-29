using OsEngine.Entity;
using OsEngine.Logging;
using OsEngine.Market;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;

namespace OsEngine.OsData.OrderFlow.Context
{
    /// <summary>Edits a private profile copy on the UI dispatcher; acceptance prepares the next calculation without changing an installed study.</summary>
    /// <remarks>The owner reads Settings only after DialogResult is true. Validation failures keep the dialog open; closing detaches handlers.</remarks>
    public partial class FlowContextSettingsWindow : Window
    {
        private readonly List<Editor> _editors = new List<Editor>();
        internal FlowContextSettings Settings { get; private set; }
        internal FlowContextSettingsWindow(FlowContextSettings settings)
        {
            InitializeComponent(); Settings = settings.Copy();
            ItemsControlGeneral.ItemsSource = Editors(Settings, new (string, string)[]
            {
                ("Instrument", "Инструмент / имя профиля"), ("PriceStep", "Шаг цены"), ("WarmupSeconds", "Прогрев после начала данных / паузы, сек"),
                ("WindowSeconds", "Окно локальных событий, сек"), ("BaselineSeconds", "Предшествующий фон TPS, сек"), ("MinimumTrades", "Минимум сделок в событии"),
                ("MinimumEventVolume", "Минимальный объём события"), ("SameSizeShare", "Доля одинаковых размеров, 0…1"), ("TpsMultiplier", "Ускорение TPS относительно фона, ×"),
                ("AggressionShare", "Минимум |дельта| / объём, 0…1"), ("MaximumProgressTicks", "Максимальное продвижение агрессии, шаги"),
                ("ExhaustionRatio", "Объём ослабшей волны / предыдущей, 0…1"), ("EventCooldownSeconds", "Пауза между событиями одного вида, сек"),
                ("MaximumGapSeconds", "Максимальная допустимая пауза данных, сек"), ("SampleSeconds", "Интервал точек VWAP/TWAP на графике, сек"),
                ("EndOfDay", "Считать исход до конца дня"), ("TargetTicks", "Исследовательская цель, шаги"), ("StopTicks", "Исследовательский стоп, шаги"),
                ("RetainedActivePerScale", "Хранить активными N последних областей / масштаб"), ("MaximumRegions", "Лимит всех областей"), ("MaximumEvents", "Лимит всех событий"), ("MaximumSamples", "Лимит точек линий")
            });
            (string, string)[] scaleFields = new (string, string)[]
            {
                ("Name", "Название масштаба"), ("Enabled", "Рассчитывать масштаб"), ("FormationSeconds", "Период формирования области, сек"),
                ("MinimumTickVolume", "Минимальный объём отдельной сделки"), ("CloudVolume", "Минимальный объём исходной цепочки"), ("CloudGapMilliseconds", "Пауза между сделками цепочки, мс"),
                ("CloudRangeTicks", "Диапазон исходной цепочки, шаги"), ("MinimumClouds", "Минимум завершённых цепочек в области"), ("RegionVolume", "Минимум суммарного объёма цепочек"),
                ("RegionRangeTicks", "Базовая ширина области, шаги"), ("CloudPauseSeconds", "Максимальная пауза между цепочками, сек"),
                ("LifetimeSeconds", "Продолжать ориентиры после подтверждения, сек"), ("BreakoutTicks", "Выход за границу / пробой структуры, шаги"),
                ("HoldSeconds", "Подтверждение нового положения, сек"), ("SwingTicks", "Базовый разворот ZigZag, шаги"),
                ("SwingVolatilityFactor", "Разворот × предшествующий диапазон, 0 — выключен"), ("VolatilityLookbackSeconds", "Предшествующее окно волатильности, сек"),
                ("RegionVolatilityFactor", "Ширина области × предшествующий диапазон, 0 — выключен"), ("MaximumAdaptiveRangeTicks", "Предел адаптивной ширины области, шаги")
            };
            ItemsControlLocal.ItemsSource = Editors(Settings.Scales[0], scaleFields); ItemsControlWork.ItemsSource = Editors(Settings.Scales[1], scaleFields); ItemsControlSenior.ItemsSource = Editors(Settings.Scales[2], scaleFields);
            TextBoxSessions.Text = string.Join(";", Settings.Sessions.Select(session => Clock(session.StartMinute) + "-" + Clock(session.EndMinute)));
            TextBoxHorizons.Text = string.Join(";", Settings.HorizonsSeconds.Select(value => (value / 60m).ToString(CultureInfo.InvariantCulture)));
            ButtonApply.Click += ApplyClick; ButtonCancel.Click += CancelClick; Closed += WindowClosed;
        }
        private sealed class Editor
        {
            public string Label { get; set; }
            public string Help { get; set; }
            public string Value { get; set; }
            public bool Checked { get; set; }
            public bool IsBoolean => Property.PropertyType == typeof(bool);
            public bool IsValue => !IsBoolean;
            internal object Target;
            internal PropertyInfo Property;
            internal void Apply()
            {
                Type type = Property.PropertyType;
                object parsed = type == typeof(bool) ? Checked : type == typeof(string) ? Value : type == typeof(int) ? int.Parse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture) : Value.ToDecimal();
                Property.SetValue(Target, parsed);
            }
        }
        private List<Editor> Editors(object target, IEnumerable<(string Property, string Label)> fields)
        {
            List<Editor> editors = new List<Editor>();
            foreach ((string name, string label) in fields)
            {
                PropertyInfo property = target.GetType().GetProperty(name); object value = property.GetValue(target);
                Editor editor = new Editor { Target = target, Property = property, Label = label, Help = label, Value = Convert.ToString(value, CultureInfo.InvariantCulture), Checked = value is bool check && check };
                editors.Add(editor); _editors.Add(editor);
            }
            return editors;
        }
        private static string Clock(int minutes) => (minutes / 60).ToString("D2") + ":" + (minutes % 60).ToString("D2");
        private static int Minutes(string clock)
        {
            string[] parts = clock.Trim().Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int hour) || !int.TryParse(parts[1], out int minute) || hour < 0 || hour > 24 || minute < 0 || minute > 59 || (hour == 24 && minute != 0)) { throw new ArgumentException("Время сессии задаётся как HH:mm."); }
            return hour * 60 + minute;
        }
        private void ApplyClick(object sender, RoutedEventArgs e)
        {
            try
            {
                foreach (Editor editor in _editors) { editor.Apply(); }
                Settings.Sessions = TextBoxSessions.Text.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(range =>
                { string[] pair = range.Split('-'); if (pair.Length != 2) { throw new ArgumentException("Интервал сессии задаётся как HH:mm-HH:mm."); } return new FlowContextSession { StartMinute = Minutes(pair[0]), EndMinute = Minutes(pair[1]) }; }).ToList();
                Settings.HorizonsSeconds = TextBoxHorizons.Text.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(value => checked((int)(value.ToDecimal() * 60))).ToList();
                Settings.Validate(); DialogResult = true;
            }
            catch (Exception error) { TextBlockError.Text = error.Message; ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error); }
        }
        private void CancelClick(object sender, RoutedEventArgs e) { try { DialogResult = false; } catch (Exception error) { ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error); } }
        private void WindowClosed(object sender, EventArgs e) { ButtonApply.Click -= ApplyClick; ButtonCancel.Click -= CancelClick; Closed -= WindowClosed; }
    }
}
