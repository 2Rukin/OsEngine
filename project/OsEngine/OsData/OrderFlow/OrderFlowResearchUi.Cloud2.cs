/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.Entity;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace OsEngine.OsData.OrderFlow
{
    public partial class OrderFlowResearchUi
    {
        #region Second Cloud controls

        private void InitializeCloud2Controls()
        {
            TabItemCloud2Settings.Header = TabItemClouds2.Header = "Cloud 2";
            CheckBoxCalculateCloud2.Content = L("Calculate Cloud 2", "Рассчитать Cloud 2");
            CheckBoxShowCloud2.Content = L("Show Cloud 2", "Показать Cloud 2");
            CheckBoxCloud2SingleTicks.Content = L("Single ticks", "Одиночные тики");
            LabelCloud2MinTick.Content = L("Min tick volume", "Мин. объём тика");
            LabelCloud2Sum.Content = L("Min chain volume", "Мин. сумма цепочки");
            LabelCloud2Gap.Content = L("Gap, ms", "Пауза, мс");
            LabelCloud2Range.Content = L("Range, ticks", "Диапазон, тики");
            TextBlockCloud2Help.Text = L(
                "Single ticks: each trade at or above the volume threshold creates its own Cloud immediately. Chain parameters are ignored. Uncheck for a second independent chain calculation.",
                "Одиночные тики: каждая сделка с объёмом не ниже порога сразу создаёт отдельный Cloud. Параметры цепочки не учитываются. Снимите флажок для второго независимого расчёта цепочек.");
            LabelCloud2Scale.Content = LabelChartCloud2Scale.Content = L("Cloud 2 size", "Размер Cloud 2");
            LabelCloud2Contrast.Content = LabelChartCloud2Contrast.Content = L("Cloud 2 contrast", "Контраст Cloud 2");
            CheckBoxCloud2Volumes.Content = L("Cloud 2 volumes", "Объёмы Cloud 2");
            OrderFlowChartHost.BindVisualSlider(SliderChartCloud2Scale, SliderCloud2Scale, TextBlockChartCloud2Scale, "{0:F1}×");
            OrderFlowChartHost.BindVisualSlider(SliderChartCloud2Contrast, SliderCloud2Contrast, TextBlockChartCloud2Contrast, "{0:F1}");
            BindCloudVisibility(CheckBoxChartShowCloud, CheckBoxShowCloud);
            BindCloudVisibility(CheckBoxChartShowCloud2, CheckBoxShowCloud2);
            BindingOperations.SetBinding(TextBlockRightPadding, TextBlock.TextProperty,
                new Binding(nameof(Slider.Value)) { Source = SliderRightPadding, StringFormat = "{0:F0}%" });
            CheckBoxCalculateCloud2.Click += CalculationMode_Click;
            CheckBoxCloud2SingleTicks.Click += CalculationMode_Click;
            CheckBoxShowCloud2.Click += ChartLayers_Click;
            CheckBoxChartShowCloud.Click += ChartLayers_Click;
            CheckBoxChartShowCloud2.Click += ChartLayers_Click;
            SliderCloud2Scale.ValueChanged += SliderCloud2Scale_ValueChanged;
            SliderCloud2Contrast.ValueChanged += SliderCloud2Contrast_ValueChanged;
            CheckBoxCloud2Volumes.Click += CheckBoxCloud2Volumes_Click;
            DataGridClouds2.MouseDoubleClick += DataGridClouds_MouseDoubleClick;
        }

        private static void BindCloudVisibility(CheckBox target, CheckBox source)
        {
            BindingOperations.SetBinding(target, ToggleButton.IsCheckedProperty,
                new Binding(nameof(CheckBox.IsChecked)) { Source = source, Mode = BindingMode.TwoWay });
        }

        private void UpdateCloud2Controls()
        {
            GridCloud2Settings.IsEnabled = CheckBoxCalculateCloud2.IsEnabled && CheckBoxCalculateCloud2.IsChecked == true;
            bool chain = CheckBoxCloud2SingleTicks.IsChecked != true;
            TextBoxCloud2Sum.IsEnabled = LabelCloud2Sum.IsEnabled = chain;
            TextBoxCloud2Gap.IsEnabled = LabelCloud2Gap.IsEnabled = chain;
            TextBoxCloud2Range.IsEnabled = LabelCloud2Range.IsEnabled = chain;
        }

        private void BuildCloud2Request(OrderFlowResearchRequest request)
        {
            request.CalculateCloud2 = CheckBoxCalculateCloud2.IsChecked == true;
            if (!request.CalculateCloud2) { return; }
            request.Cloud2 = new OrderFlowCloudSettings
            {
                SingleTicks = CheckBoxCloud2SingleTicks.IsChecked == true,
                MinimumTickVolume = TextBoxCloud2MinTick.Text.ToDecimal()
            };
            if (request.Cloud2.SingleTicks) { return; }
            request.Cloud2.MinimumSumVolume = TextBoxCloud2Sum.Text.ToDecimal();
            request.Cloud2.MaximumGapMilliseconds = ParseNonNegativeInt(TextBoxCloud2Gap.Text, "Cloud 2 gap");
            request.Cloud2.MaximumRangeTicks = ParseNonNegativeInt(TextBoxCloud2Range.Text, "Cloud 2 range");
        }

        private void DisposeCloud2Controls()
        {
            CheckBoxCalculateCloud2.Click -= CalculationMode_Click;
            CheckBoxCloud2SingleTicks.Click -= CalculationMode_Click;
            CheckBoxShowCloud2.Click -= ChartLayers_Click;
            CheckBoxChartShowCloud.Click -= ChartLayers_Click;
            CheckBoxChartShowCloud2.Click -= ChartLayers_Click;
            SliderCloud2Scale.ValueChanged -= SliderCloud2Scale_ValueChanged;
            SliderCloud2Contrast.ValueChanged -= SliderCloud2Contrast_ValueChanged;
            CheckBoxCloud2Volumes.Click -= CheckBoxCloud2Volumes_Click;
            DataGridClouds2.MouseDoubleClick -= DataGridClouds_MouseDoubleClick;
        }

        #endregion

        #region Second Cloud appearance

        private void SliderCloud2Scale_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try { _chart.SetCloudScale(e.NewValue, true); }
            catch (Exception error) { ShowError(error); }
        }

        private void SliderCloud2Contrast_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try { _chart.SetCloudContrast(e.NewValue, true); }
            catch (Exception error) { ShowError(error); }
        }

        private void CheckBoxCloud2Volumes_Click(object sender, RoutedEventArgs e)
        {
            try { _chart.SetCloudVolumeLabels(CheckBoxCloud2Volumes.IsChecked == true, true); }
            catch (Exception error) { ShowError(error); }
        }

        #endregion
    }
}
