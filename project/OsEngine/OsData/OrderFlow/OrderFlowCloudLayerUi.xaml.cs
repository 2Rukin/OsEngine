/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.Entity;
using OsEngine.Indicators;
using OsEngine.Language;
using OsEngine.Logging;
using OsEngine.Market;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace OsEngine.OsData.OrderFlow
{
    /// <summary>Dispatcher-owned editor of a copied Cloud instance; accepted settings apply to the next run, without mutating running calculations.</summary>
    public partial class OrderFlowCloudLayerUi : Window
    {
        private readonly OrderFlowCloudLayer _layer;
        private readonly Dictionary<string, TextBox> _thresholds = new Dictionary<string, TextBox>();
        internal OrderFlowCloudLayer Result { get; private set; }

        internal OrderFlowCloudLayerUi(OrderFlowCloudLayer source)
        {
            InitializeComponent(); _layer = source.Copy();
            Title = L("Cloud instance", "Экземпляр Cloud"); LabelName.Content = L("Name", "Название");
            CheckBoxEnabled.Content = L("Calculate", "Рассчитывать"); CheckBoxVisible.Content = L("Show", "Показывать");
            CheckBoxSingle.Content = L("Single trades instead of chains", "Одиночные сделки вместо цепочек");
            LabelGap.Content = L("Maximum gap, ms", "Максимальная пауза, мс"); LabelRange.Content = L("Maximum price range, ticks", "Максимальный размах цены, тики");
            ButtonTimeProfiles.Content = L("Activity intervals and thresholds", "Время работы и пороги");
            ButtonAccept.Content = L("Apply", "Применить"); ButtonCancel.Content = L("Cancel", "Отмена"); ButtonColor.Content = L("Layer color", "Цвет слоя");
            LabelImbalance.Content = L("Diagonal imbalance", "Диагональный перевес"); LabelContext.Content = L("Context, sec", "Окружение, сек");
            ComboBoxImbalance.ItemsSource = Enum.GetValues<OrderFlowImbalanceSource>(); ComboBoxImbalance.SelectedItem = _layer.Settings.Imbalance.Source;
            ComboBoxDirection.ItemsSource = Enum.GetValues<OrderFlowImbalanceDirection>(); ComboBoxDirection.SelectedItem = _layer.Settings.Imbalance.Direction;
            ComboBoxImbalance.ToolTip = L("Off: disabled; Inside: Cloud; Context: surrounding flow; Both: same side in both.", "Off: выключен; Inside: внутри Cloud; Context: окружающий поток; Both: одна сторона в обоих.");
            ComboBoxDirection.ToolTip = L("Any: either side; Buy: buyers; Sell: sellers.", "Any: любая сторона; Buy: покупки; Sell: продажи.");
            TextBoxContext.Text = _layer.Settings.Imbalance.ContextSeconds.ToString(CultureInfo.InvariantCulture);
            TextBoxName.Text = _layer.Name; CheckBoxEnabled.IsChecked = _layer.Enabled; CheckBoxVisible.IsChecked = _layer.Visible;
            CheckBoxSingle.IsChecked = _layer.Settings.SingleTicks; TextBoxGap.Text = _layer.Settings.MaximumGapMilliseconds.ToString(CultureInfo.InvariantCulture);
            TextBoxRange.Text = _layer.Settings.MaximumRangeTicks.ToString(CultureInfo.InvariantCulture);
            foreach (ThresholdDefinition definition in OrderFlowTimeThresholds.Cloud)
            {
                Grid row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                TextBlock label = new TextBlock { Text = definition.Label, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(5), VerticalAlignment = VerticalAlignment.Center };
                label.SetResourceReference(TextBlock.ForegroundProperty, "ControlForegroundWhite"); row.Children.Add(label);
                TextBox input = new TextBox { Text = Convert.ToDecimal(typeof(OrderFlowCloudSettings).GetProperty(definition.Key).GetValue(_layer.Settings), CultureInfo.InvariantCulture).ToString("G29", CultureInfo.InvariantCulture), Width = 140, Margin = new Thickness(4) };
                Grid.SetColumn(input, 1); row.Children.Add(input); StackPanelThresholds.Children.Add(row); _thresholds.Add(definition.Key, input);
            }
            TextBlockHelp.Text = L("Bounds are inclusive; maximum zero means unlimited. Thresholds change the calculation and require Run. Single mode uses the minimum tick volume. Time boundaries never split a Cloud. Gap and price range determine its chains.",
                "Границы включительные; максимум 0 — без ограничения. Пороги меняют расчёт и применяются кнопкой «Рассчитать». В одиночном режиме используется минимум объёма тика. Смена интервала не разделяет Cloud. Цепочки определяются паузой и размахом цены.");
            RefreshSchedule(); ButtonAccept.Click += AcceptClick; ButtonTimeProfiles.Click += TimeProfilesClick; ButtonColor.Click += ColorClick; Closed += WindowClosed;
        }

        #region Settings

        private void RefreshSchedule()
        {
            ThresholdTimeProfiles profiles = _layer.Settings.TimeProfiles;
            TextBlockTimeProfiles.Text = profiles?.Enabled == true ? string.Join("; ", profiles.Periods.Select(p => ThresholdTimeProfiles.FormatMinute(p.FromMinute) + "–" + ThresholdTimeProfiles.FormatMinute(p.ToMinute))) +
                L(" · chains follow gap / price range", " · цепочки по паузе / размаху цены") : L("All source time, base thresholds", "Всё время источника, базовые пороги");
        }
        private void TimeProfilesClick(object sender, RoutedEventArgs e)
        {
            try
            {
                ThresholdTimeProfilesUi ui = new ThresholdTimeProfilesUi(_layer.Settings.TimeProfiles, OrderFlowTimeThresholds.Cloud, supportsReset: false);
                if (ui.ShowDialog() == true) { _layer.Settings.TimeProfiles = ui.Result; RefreshSchedule(); }
            }
            catch (Exception error) { Report(error); }
        }
        private void ColorClick(object sender, RoutedEventArgs e)
        {
            try
            {
                using System.Windows.Forms.ColorDialog dialog = new System.Windows.Forms.ColorDialog();
                dialog.Color = System.Drawing.Color.FromArgb(unchecked((int)uint.Parse(_layer.Color.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) { _layer.Color = "#" + dialog.Color.ToArgb().ToString("X8", CultureInfo.InvariantCulture); }
            }
            catch (Exception error) { Report(error); }
        }
        private void AcceptClick(object sender, RoutedEventArgs e)
        {
            try
            {
                _layer.Name = TextBoxName.Text.Trim(); _layer.Enabled = CheckBoxEnabled.IsChecked == true; _layer.Visible = CheckBoxVisible.IsChecked == true;
                _layer.Settings.SingleTicks = CheckBoxSingle.IsChecked == true;
                _layer.Settings.MaximumGapMilliseconds = int.Parse(TextBoxGap.Text, CultureInfo.InvariantCulture);
                _layer.Settings.MaximumRangeTicks = int.Parse(TextBoxRange.Text, CultureInfo.InvariantCulture);
                _layer.Settings.Imbalance.Source = (OrderFlowImbalanceSource)ComboBoxImbalance.SelectedItem;
                _layer.Settings.Imbalance.Direction = (OrderFlowImbalanceDirection)ComboBoxDirection.SelectedItem;
                _layer.Settings.Imbalance.ContextSeconds = int.Parse(TextBoxContext.Text, CultureInfo.InvariantCulture);
                foreach (ThresholdDefinition definition in OrderFlowTimeThresholds.Cloud)
                {
                    decimal value = ThresholdTimeProfiles.ParseThreshold(_thresholds[definition.Key].Text); definition.Validate(value);
                    PropertyInfo property = typeof(OrderFlowCloudSettings).GetProperty(definition.Key);
                    property.SetValue(_layer.Settings, definition.Integer ? (object)checked((int)value) : value);
                }
                _layer.Copy().Validate(); Result = _layer.Copy(); DialogResult = true;
            }
            catch (Exception error) { Report(error); }
        }

        #endregion

        private void Report(Exception error) { TextBlockStatus.Text = error.Message; ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error); }
        private static string L(string en, string ru) => OsLocalization.ConvertToLocString("Eng:" + en + "_Ru:" + ru + "_");
        private void WindowClosed(object sender, EventArgs e)
        {
            try { ButtonAccept.Click -= AcceptClick; ButtonTimeProfiles.Click -= TimeProfilesClick; ButtonColor.Click -= ColorClick; Closed -= WindowClosed; }
            catch (Exception error) { ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error); }
        }
    }
}
