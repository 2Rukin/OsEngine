/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.Entity;
using OsEngine.Language;
using OsEngine.Logging;
using OsEngine.Market;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace OsEngine.Indicators
{
    /// <summary>Dispatcher-owned editor of a detached threshold schedule; Apply validates all rows and Cancel leaves the caller unchanged.</summary>
    public partial class ThresholdTimeProfilesUi : Window
    {
        private sealed class PeriodEditor
        {
            internal Border Container;
            internal TextBox From, To;
            internal Button Remove;
            internal readonly Dictionary<string, TextBox> Values = new Dictionary<string, TextBox>();
        }
        private readonly IReadOnlyList<ThresholdDefinition> _definitions;
        private readonly bool _supportsReset;
        private readonly List<PeriodEditor> _editors = new List<PeriodEditor>();
        internal ThresholdTimeProfiles Result { get; private set; }

        internal ThresholdTimeProfilesUi(ThresholdTimeProfiles profiles, IReadOnlyList<ThresholdDefinition> definitions, bool supportsReset = true)
        {
            InitializeComponent();
            _definitions = definitions;
            _supportsReset = supportsReset;
            profiles ??= new ThresholdTimeProfiles();
            Title = L("Threshold time profiles", "Временные пороги");
            CheckBoxEnabled.Content = L("Use time profiles", "Использовать интервалы");
            CheckBoxBaseOutside.Content = L("Base thresholds outside intervals", "Базовые пороги вне интервалов");
            CheckBoxReset.Content = L("Reset at interval start", "Сброс при начале интервала");
            CheckBoxEnabled.IsChecked = profiles.Enabled;
            CheckBoxBaseOutside.IsChecked = profiles.UseBaseOutside;
            CheckBoxReset.IsChecked = supportsReset && profiles.ResetOnStart;
            CheckBoxReset.Visibility = supportsReset ? Visibility.Visible : Visibility.Collapsed;
            TextBlockHelp.Text = L("Source clock, HH.mm entered with a colon. Both whole boundary minutes are included. Overnight intervals are supported. Empty thresholds use base values. Intervals must not overlap.",
                "Время источника, часы и минуты через двоеточие. Обе граничные минуты включены целиком. Поддерживается переход через полночь. Пустые пороги используют базовые значения. Интервалы не должны пересекаться.");
            ButtonAdd.Content = L("Add interval", "Добавить интервал");
            ButtonAccept.Content = L("Apply", "Применить"); ButtonCancel.Content = L("Cancel", "Отмена");
            foreach (ThresholdTimePeriod period in profiles.Periods) { AddEditor(period); }
            ButtonAdd.Click += AddClick; ButtonAccept.Click += AcceptClick; Closed += WindowClosed;
        }

        #region Interval editing

        private void AddEditor(ThresholdTimePeriod period)
        {
            PeriodEditor editor = new PeriodEditor();
            StackPanel panel = new StackPanel();
            editor.Container = new Border { Child = panel, BorderThickness = new Thickness(1), Padding = new Thickness(6), Margin = new Thickness(4) };
            editor.Container.SetResourceReference(Border.BorderBrushProperty, "ControlBorderBrush");
            WrapPanel time = new WrapPanel(); panel.Children.Add(time);
            time.Children.Add(Label(L("From", "С")));
            editor.From = Input(ThresholdTimeProfiles.FormatMinute(period.FromMinute)); time.Children.Add(editor.From);
            time.Children.Add(Label(L("Through inclusive", "До включительно")));
            editor.To = Input(ThresholdTimeProfiles.FormatMinute(period.ToMinute)); time.Children.Add(editor.To);
            editor.Remove = new Button { Content = L("Remove", "Удалить"), Tag = editor, Margin = new Thickness(4), Padding = new Thickness(8, 4, 8, 4), MinHeight = 28 };
            editor.Remove.Click += RemoveClick; time.Children.Add(editor.Remove);
            foreach (ThresholdDefinition definition in _definitions)
            {
                Grid row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(Label(definition.Label));
                TextBox value = Input(period.Values.TryGetValue(definition.Key, out decimal threshold) ? threshold.ToString("G29", CultureInfo.InvariantCulture) : "");
                value.Width = 155; Grid.SetColumn(value, 1); row.Children.Add(value); panel.Children.Add(row); editor.Values.Add(definition.Key, value);
            }
            _editors.Add(editor); StackPanelPeriods.Children.Add(editor.Container);
        }
        private static TextBox Input(string value) => new TextBox { Text = value, Width = 95, Margin = new Thickness(4), VerticalContentAlignment = VerticalAlignment.Center };
        private static TextBlock Label(string value)
        {
            TextBlock label = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4), VerticalAlignment = VerticalAlignment.Center };
            label.SetResourceReference(TextBlock.ForegroundProperty, "ControlForegroundWhite"); return label;
        }
        private void AddClick(object sender, RoutedEventArgs e)
        { try { AddEditor(new ThresholdTimePeriod()); } catch (Exception error) { Report(error); } }
        private void RemoveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                PeriodEditor editor = (PeriodEditor)((Button)sender).Tag;
                editor.Remove.Click -= RemoveClick; StackPanelPeriods.Children.Remove(editor.Container); _editors.Remove(editor);
            }
            catch (Exception error) { Report(error); }
        }
        private void AcceptClick(object sender, RoutedEventArgs e)
        {
            try
            {
                ImmutableArray<ThresholdTimePeriod>.Builder periods = ImmutableArray.CreateBuilder<ThresholdTimePeriod>();
                foreach (PeriodEditor editor in _editors)
                {
                    ImmutableDictionary<string, decimal>.Builder values = ImmutableDictionary.CreateBuilder<string, decimal>();
                    foreach (KeyValuePair<string, TextBox> pair in editor.Values)
                    { if (!string.IsNullOrWhiteSpace(pair.Value.Text)) { values.Add(pair.Key, ThresholdTimeProfiles.ParseThreshold(pair.Value.Text)); } }
                    periods.Add(new ThresholdTimePeriod { FromMinute = ThresholdTimeProfiles.ParseMinute(editor.From.Text), ToMinute = ThresholdTimeProfiles.ParseMinute(editor.To.Text), Values = values.ToImmutable() });
                }
                ThresholdTimeProfiles profiles = new ThresholdTimeProfiles { Enabled = CheckBoxEnabled.IsChecked == true,
                    UseBaseOutside = CheckBoxBaseOutside.IsChecked == true, ResetOnStart = _supportsReset && CheckBoxReset.IsChecked == true, Periods = periods.ToImmutable() };
                profiles.Validate(_definitions); Result = profiles; DialogResult = true;
            }
            catch (Exception error) { Report(error); }
        }

        #endregion

        private void Report(Exception error) { TextBlockStatus.Text = error.Message; ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error); }
        private static string L(string en, string ru) => OsLocalization.ConvertToLocString("Eng:" + en + "_Ru:" + ru + "_");
        private void WindowClosed(object sender, EventArgs e)
        {
            try
            {
                ButtonAdd.Click -= AddClick; ButtonAccept.Click -= AcceptClick; Closed -= WindowClosed;
                foreach (PeriodEditor editor in _editors) { editor.Remove.Click -= RemoveClick; } _editors.Clear();
            }
            catch (Exception error) { ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error); }
        }
    }
}
