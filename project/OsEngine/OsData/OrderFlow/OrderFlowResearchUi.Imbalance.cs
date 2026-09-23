/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.Entity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace OsEngine.OsData.OrderFlow
{
    public partial class OrderFlowResearchUi
    {
        #region Settings and lifetime

        private T ImbalanceControl<T>(string prefix, string name) where T : FrameworkElement
        { return (T)FindName(typeof(T).Name + prefix + name); }

        private void InitializeCloudImbalanceControls()
        {
            foreach (string prefix in new[] { "Cloud", "Cloud2" })
            {
                ComboBox source = ImbalanceControl<ComboBox>(prefix, "ImbalanceSource");
                source.ItemsSource = new List<KeyValuePair<OrderFlowImbalanceSource, string>>
                {
                    new KeyValuePair<OrderFlowImbalanceSource, string>(OrderFlowImbalanceSource.Off, L("Off", "Выключен")),
                    new KeyValuePair<OrderFlowImbalanceSource, string>(OrderFlowImbalanceSource.Inside, L("Inside Cloud", "Внутри Cloud")),
                    new KeyValuePair<OrderFlowImbalanceSource, string>(OrderFlowImbalanceSource.Context, L("Surrounding flow", "Окружающий поток")),
                    new KeyValuePair<OrderFlowImbalanceSource, string>(OrderFlowImbalanceSource.Both, L("Both, same side", "Оба, одна сторона"))
                };
                source.SelectedValue = OrderFlowImbalanceSource.Off;
                source.SelectionChanged += ImbalanceSource_SelectionChanged;
                ComboBox direction = ImbalanceControl<ComboBox>(prefix, "ImbalanceDirection");
                direction.ItemsSource = new List<KeyValuePair<OrderFlowImbalanceDirection, string>>
                {
                    new KeyValuePair<OrderFlowImbalanceDirection, string>(OrderFlowImbalanceDirection.Any, L("Any", "Любое")),
                    new KeyValuePair<OrderFlowImbalanceDirection, string>(OrderFlowImbalanceDirection.Buy, L("Buy", "Покупки")),
                    new KeyValuePair<OrderFlowImbalanceDirection, string>(OrderFlowImbalanceDirection.Sell, L("Sell", "Продажи"))
                };
                direction.SelectedValue = OrderFlowImbalanceDirection.Any;
                ImbalanceControl<Label>(prefix, "ImbalanceSource").Content = L("Filter", "Фильтр");
                ImbalanceControl<Label>(prefix, "ContextSeconds").Content = L("Context, sec", "Окружение, сек");
                ImbalanceControl<Label>(prefix, "ImbalanceDirection").Content = L("Direction", "Направление");
                ImbalanceControl<Label>(prefix, "ImbalanceRatio").Content = L("Ratio, %", "Соотношение, %");
                ImbalanceControl<Label>(prefix, "ImbalanceVolume").Content = L("Min side volume", "Мин. объём стороны");
                ImbalanceControl<Label>(prefix, "ImbalanceDifference").Content = L("Min difference", "Мин. разность");
                ImbalanceControl<Label>(prefix, "ImbalanceDelta").Content = L("Min delta, %", "Мин. дельта, %");
                ImbalanceControl<TextBlock>(prefix, "ImbalanceHelp").Text = L(
                    "Both statistics are calculated. 300% = 3:1 across adjacent prices. Zero/missing opposites do not compare. Min delta=0 disables the extra volume-delta condition. Apply filters without recalculation; all rows stay in the tables. Tick-count bounds refer to the whole Cloud; 0 disables that bound. Context includes all selected-period ticks, even below Tick Limit.",
                    "Оба показателя рассчитываются всегда. 300% = 3:1 на соседних ценах. Нули и отсутствующие соседние объёмы не сравниваются. Мин. дельта=0 отключает дополнительное условие по объёмной дельте. Примените фильтры без пересчёта; все Cloud остаются в таблицах. Количество тиков — во всём Cloud, 0 отключает соответствующую границу. Окружение включает все тики периода, в том числе ниже порога Cloud.");
                ImbalanceControl<Label>(prefix, "MinCount").Content = L("Min ticks", "Мин. тиков");
                ImbalanceControl<Label>(prefix, "MaxCount").Content = L("Max ticks (0=any)", "Макс. тиков (0=любое)");
                ImbalanceControl<Button>(prefix, "FilterApply").Content = L("Apply filters", "Применить фильтры");
                ImbalanceControl<Button>(prefix, "FilterApply").Click += ApplyCloudFilters_Click;
                UpdateImbalanceThresholds(prefix);
            }
            CheckBoxCloudRejected.Content = L("Cloud 1 filtered out", "Отсечённые Cloud 1");
            CheckBoxCloud2Rejected.Content = L("Cloud 2 filtered out", "Отсечённые Cloud 2");
            CheckBoxCloudRejected.ToolTip = CheckBoxCloud2Rejected.ToolTip = L("Show failed Clouds in gray without recalculation; includes them in the Cloud path.",
                "Показать не прошедшие фильтр Cloud серым без пересчёта; включить их в линию по Cloud.");
            CheckBoxCloudRejected.Click += ShowRejectedClouds_Click;
            CheckBoxCloud2Rejected.Click += ShowRejectedClouds_Click;
            Dictionary<string, string> headers = new Dictionary<string, string>
            {
                ["SidePercent"] = L("Count delta %", "Перевес числа, %"),
                ["ImbalancePassed"] = L("View filters PASS", "Прошёл фильтры показа"),
                ["DeltaPercent"] = L("Volume delta %", "Дельта объёма, %"),
                ["InsideImbalance.BuyRatioPercent"] = L("Inside Buy %", "Внутри Buy, %"),
                ["InsideImbalance.SellRatioPercent"] = L("Inside Sell %", "Внутри Sell, %"),
                ["ContextImbalance.DeltaPercent"] = L("Context delta %", "Окруж. дельта, %"),
                ["ContextImbalance.BuyRatioPercent"] = L("Context Buy %", "Окруж. Buy, %"),
                ["ContextImbalance.SellRatioPercent"] = L("Context Sell %", "Окруж. Sell, %")
            };
            foreach (DataGrid grid in new[] { DataGridClouds, DataGridClouds2 })
            foreach (DataGridColumn column in grid.Columns)
            {
                if (column is DataGridBoundColumn bound && bound.Binding is Binding binding && headers.TryGetValue(binding.Path.Path, out string header))
                { column.Header = header; column.MinWidth = 110; }
            }
        }

        private OrderFlowImbalanceSettings ReadImbalanceSettings(string prefix, bool displayFilter = false)
        {
            OrderFlowImbalanceSettings settings = new OrderFlowImbalanceSettings
            {
                ContextSeconds = displayFilter ? (prefix == "Cloud2" ? _displayedRequest?.Cloud2?.Imbalance?.ContextSeconds : _displayedRequest?.Cloud?.Imbalance?.ContextSeconds) ?? 30
                    : ParseInt(ImbalanceControl<TextBox>(prefix, "ContextSeconds").Text, "Cloud context seconds"),
                Source = displayFilter ? (OrderFlowImbalanceSource)ImbalanceControl<ComboBox>(prefix, "ImbalanceSource").SelectedValue : OrderFlowImbalanceSource.Off
            };
            if (settings.Source == OrderFlowImbalanceSource.Off) { return settings; }
            settings.Direction = (OrderFlowImbalanceDirection)ImbalanceControl<ComboBox>(prefix, "ImbalanceDirection").SelectedValue;
            settings.MinimumRatioPercent = ImbalanceControl<TextBox>(prefix, "ImbalanceRatio").Text.ToDecimal();
            settings.MinimumDominantVolume = ImbalanceControl<TextBox>(prefix, "ImbalanceVolume").Text.ToDecimal();
            settings.MinimumDifference = ImbalanceControl<TextBox>(prefix, "ImbalanceDifference").Text.ToDecimal();
            settings.MinimumDeltaPercent = ImbalanceControl<TextBox>(prefix, "ImbalanceDelta").Text.ToDecimal();
            return settings;
        }

        private void DisposeCloudImbalanceControls()
        {
            ButtonCloudFilterApply.Click -= ApplyCloudFilters_Click;
            ButtonCloud2FilterApply.Click -= ApplyCloudFilters_Click;
            ComboBoxCloudImbalanceSource.SelectionChanged -= ImbalanceSource_SelectionChanged;
            ComboBoxCloud2ImbalanceSource.SelectionChanged -= ImbalanceSource_SelectionChanged;
            CheckBoxCloudRejected.Click -= ShowRejectedClouds_Click;
            CheckBoxCloud2Rejected.Click -= ShowRejectedClouds_Click;
        }

        #endregion

        #region Interaction

        private void UpdateImbalanceThresholds(string prefix)
        {
            ((WrapPanel)FindName("Panel" + prefix + "ImbalanceThresholds")).IsEnabled =
                ImbalanceControl<ComboBox>(prefix, "ImbalanceSource").SelectedValue is OrderFlowImbalanceSource source && source != OrderFlowImbalanceSource.Off;
        }

        private void ImbalanceSource_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try { UpdateImbalanceThresholds(ReferenceEquals(sender, ComboBoxCloud2ImbalanceSource) ? "Cloud2" : "Cloud"); }
            catch (Exception error) { ShowError(error); }
        }

        private OrderFlowCloudFilter ReadCloudFilter(string prefix)
        {
            return new OrderFlowCloudFilter(ReadImbalanceSettings(prefix, true),
                ParseNonNegativeInt(ImbalanceControl<TextBox>(prefix, "MinCount").Text, "Minimum Cloud ticks"),
                ParseNonNegativeInt(ImbalanceControl<TextBox>(prefix, "MaxCount").Text, "Maximum Cloud ticks"));
        }

        private void ApplyCloudFilters_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_displayedResult == null) { return; }
                bool second = ReferenceEquals(sender, ButtonCloud2FilterApply);
                if (second ? !_displayedResult.Cloud2Calculated : !_displayedResult.CloudCalculated) { return; }
                _chart.SetCloudFilter(ReadCloudFilter(second ? "Cloud2" : "Cloud"), second);
                RefreshCloudViewRows();
            }
            catch (Exception error) { ShowError(error); }
        }

        private void ApplyCloudViewFilters()
        {
            if (_displayedResult == null) { return; }
            OrderFlowCloudFilter first = _displayedResult.CloudCalculated ? ReadCloudFilter("Cloud") : null;
            OrderFlowCloudFilter second = _displayedResult.Cloud2Calculated ? ReadCloudFilter("Cloud2") : null;
            _chart.SetCloudFilters(first, second);
            RefreshCloudViewRows();
        }

        private void RefreshCloudViewRows()
        {
            DataGridClouds.ItemsSource = _chart.CloudViewRows(false);
            DataGridClouds2.ItemsSource = _chart.CloudViewRows(true);
            foreach (string prefix in new[] { "Cloud", "Cloud2" })
            {
                bool second = prefix == "Cloud2";
                List<OrderFlowCloud> rows = _chart.CloudViewRows(second);
                int seconds = (second ? _displayedRequest?.Cloud2?.Imbalance?.ContextSeconds : _displayedRequest?.Cloud?.Imbalance?.ContextSeconds) ?? 30;
                ImbalanceControl<TextBlock>(prefix, "FilterStatus").Text = L("Pass ", "Прошло ") + rows.Count(cloud => cloud.ImbalancePassed)
                    + " / " + rows.Count + L(" · calculated context ", " · рассчитанное окружение ") + seconds + L(" sec. Display filters do not change exported results.", " сек. Фильтры показа не меняют экспорт расчёта.");
            }
        }

        private void ShowRejectedClouds_Click(object sender, RoutedEventArgs e)
        {
            try { _chart.SetShowRejectedClouds(CheckBoxCloudRejected.IsChecked == true, CheckBoxCloud2Rejected.IsChecked == true); }
            catch (Exception error) { ShowError(error); }
        }

        #endregion
    }
}
