/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using Microsoft.Win32;
using OsEngine.Indicators;
using OsEngine.OsData.OrderFlow.Calibration;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Windows;

namespace OsEngine.OsData.OrderFlow
{
    internal sealed record OrderFlowTimeWorkspace(string Version, ThresholdTimeProfiles Delta, ThresholdTimeProfiles Cloud1,
        ThresholdTimeProfiles Cloud2, List<OrderFlowCloudLayer> Layers);

    public partial class OrderFlowResearchUi
    {
        private readonly List<OrderFlowCloudLayer> _cloudLayers = new List<OrderFlowCloudLayer>();
        private ThresholdTimeProfiles _deltaTimeProfiles, _cloudTimeProfiles, _cloud2TimeProfiles;
        private sealed record AdditionalCloudRow(string Layer, OrderFlowCloud Cloud);

        #region Main workbench controls

        private void InitializeTimeProfiles()
        {
            ButtonAddCloud.Content = L("Add Cloud", "Добавить Cloud");
            ButtonEditCloudLayer.Content = L("Settings", "Настройки"); ButtonRemoveCloudLayer.Content = L("Remove", "Удалить");
            ButtonCloudLayerVisibility.Content = L("Show / hide", "Показать / скрыть");
            ButtonCloudLayerFilter.Content = L("Apply volume / delta / trades post-filters", "Применить постфильтры объёма / дельты / сделок");
            ButtonTimeProfiles.Content = L("Time profiles", "Временные пороги");
            ButtonSaveTimeProfiles.Content = L("Save profiles and Clouds", "Сохранить профили и Cloud");
            ButtonLoadTimeProfiles.Content = L("Load profiles and Clouds", "Загрузить профили и Cloud");
            TabItemCloudLayersSettings.Header = L("Cloud instances / time", "Экземпляры Cloud / время");
            TabItemAdditionalClouds.Header = L("Additional Clouds", "Добавленные Cloud");
            ComboBoxTimeProfileTarget.ItemsSource = new[] { "Delta", "Cloud 1", "Cloud 2" }; ComboBoxTimeProfileTarget.SelectedIndex = 0;
            ButtonAddCloud.Click += AddCloudClick; ButtonEditCloudLayer.Click += EditCloudClick; ButtonRemoveCloudLayer.Click += RemoveCloudClick;
            ButtonCloudLayerVisibility.Click += CloudVisibilityClick; ButtonCloudLayerFilter.Click += CloudLayerFilterClick;
            ButtonTimeProfiles.Click += TimeProfilesClick; ButtonSaveTimeProfiles.Click += SaveTimeProfilesClick; ButtonLoadTimeProfiles.Click += LoadTimeProfilesClick;
            DataGridAdditionalClouds.MouseDoubleClick += AdditionalCloudDoubleClick;
            _tableWindows.Add(DataGridAdditionalClouds, L("Additional Clouds", "Добавленные Cloud"), TableContext,
                () => TabControlResults.SelectedItem = TabItemAdditionalClouds);
            RefreshCloudLayers();
        }
        private bool CanEditTimeProfiles() => !_chart.IsReplaying && ButtonRun.IsEnabled;
        private void AddCloudClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!CanEditTimeProfiles()) { return; }
                OrderFlowCloudLayer layer = new OrderFlowCloudLayer { Name = "Cloud " + (_cloudLayers.Count + 3), Settings = new OrderFlowCloudSettings
                    { TimeProfiles = new ThresholdTimeProfiles { Enabled = true, UseBaseOutside = false, Periods = ImmutableArray.Create(new ThresholdTimePeriod()) } } };
                OrderFlowCloudLayerUi ui = new OrderFlowCloudLayerUi(layer);
                if (ui.ShowDialog() != true) { return; } _cloudLayers.Add(ui.Result); RefreshCloudLayers(ui.Result.Id);
            }
            catch (Exception error) { ShowError(error); }
        }
        private void EditCloudClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!CanEditTimeProfiles() || ComboBoxCloudLayers.SelectedItem is not OrderFlowCloudLayer layer) { return; }
                OrderFlowCloudLayerUi ui = new OrderFlowCloudLayerUi(layer);
                if (ui.ShowDialog() != true) { return; } _cloudLayers[_cloudLayers.IndexOf(layer)] = ui.Result; RefreshCloudLayers(ui.Result.Id);
                _chart.SetCloudLayerView(ui.Result);
            }
            catch (Exception error) { ShowError(error); }
        }
        private void RemoveCloudClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!CanEditTimeProfiles() || ComboBoxCloudLayers.SelectedItem is not OrderFlowCloudLayer layer) { return; }
                _cloudLayers.Remove(layer); OrderFlowCloudLayer hidden = layer.Copy(); hidden.Visible = false; _chart.SetCloudLayerView(hidden); RefreshCloudLayers();
            }
            catch (Exception error) { ShowError(error); }
        }
        private void CloudVisibilityClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ComboBoxCloudLayers.SelectedItem is not OrderFlowCloudLayer layer) { return; }
                layer.Visible = !layer.Visible; _chart.SetCloudLayerView(layer); RefreshCloudLayers(layer.Id);
            }
            catch (Exception error) { ShowError(error); }
        }
        private void CloudLayerFilterClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ComboBoxCloudLayers.SelectedItem is not OrderFlowCloudLayer layer) { return; }
                _chart.SetCloudLayerPostFilter(layer.Id, layer.Settings); RefreshAdditionalCloudRows();
                TextBlockCloudLayers.Text = L("Saved Cloud volume, absolute delta and trade-count filters applied. Tick/gap/range changes require Run.",
                    "Постфильтры объёма Cloud, абсолютной дельты и числа сделок применены к сохранённым результатам. Изменения тиков, паузы и размаха требуют расчёта.");
            }
            catch (Exception error) { ShowError(error); }
        }
        private void TimeProfilesClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!CanEditTimeProfiles()) { return; }
                int target = ComboBoxTimeProfileTarget.SelectedIndex;
                ThresholdTimeProfiles current = target == 0 ? _deltaTimeProfiles : target == 1 ? _cloudTimeProfiles : _cloud2TimeProfiles;
                ThresholdTimeProfilesUi ui = new ThresholdTimeProfilesUi(current, target == 0 ? OrderFlowTimeThresholds.Delta : OrderFlowTimeThresholds.Cloud, supportsReset: target == 0);
                if (ui.ShowDialog() != true) { return; }
                if (target == 0) { _deltaTimeProfiles = ui.Result; } else if (target == 1) { _cloudTimeProfiles = ui.Result; } else { _cloud2TimeProfiles = ui.Result; }
                RefreshCloudLayers();
            }
            catch (Exception error) { ShowError(error); }
        }
        private void RefreshCloudLayers(string selectedId = null)
        {
            selectedId ??= (ComboBoxCloudLayers.SelectedItem as OrderFlowCloudLayer)?.Id;
            ComboBoxCloudLayers.ItemsSource = _cloudLayers.ToArray();
            ComboBoxCloudLayers.SelectedItem = _cloudLayers.FirstOrDefault(l => l.Id == selectedId) ?? _cloudLayers.FirstOrDefault();
            TextBlockCloudLayers.Text = L("Additional Clouds", "Добавленные Cloud") + " · " + _cloudLayers.Count + "\n" +
                L("Time profiles enabled — Delta / Cloud 1 / Cloud 2", "Временные пороги включены — Delta / Cloud 1 / Cloud 2") + " · " +
                (_deltaTimeProfiles?.Enabled == true) + " / " + (_cloudTimeProfiles?.Enabled == true) + " / " + (_cloud2TimeProfiles?.Enabled == true) + "\n" +
                L("Edit each Cloud to set its activity intervals. Calculation settings apply on Run; visibility and post-filters apply immediately.",
                    "В настройках каждого Cloud задаются его интервалы. Настройки расчёта применяются кнопкой «Рассчитать»; видимость и постфильтры — сразу.");
        }
        private void AttachTimeProfiles(OrderFlowResearchRequest request)
        {
            request.DeltaTimeProfiles = _deltaTimeProfiles;
            if (request.CalculateCloud) { request.Cloud.TimeProfiles = _cloudTimeProfiles; }
            if (request.CalculateCloud2) { request.Cloud2.TimeProfiles = _cloud2TimeProfiles; }
            request.CloudLayers = _cloudLayers.Select(l => l.Copy()).ToList();
        }
        private void RefreshAdditionalCloudRows()
        {
            DataGridAdditionalClouds.ItemsSource = _chart.DisplayedCloudLayers.SelectMany(l => _chart.CloudLayerRows(l).Select(c => new AdditionalCloudRow(l.Layer.Name, c))).ToList();
        }
        private void AdditionalCloudDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                if (DataGridAdditionalClouds.SelectedItem is not AdditionalCloudRow row) { return; }
                OrderFlowCloudLayerResult result = _chart.DisplayedCloudLayers.FirstOrDefault(l => l.Clouds.Any(c => c.CloudId == row.Cloud.CloudId));
                OrderFlowCloudLayer layer = _cloudLayers.FirstOrDefault(l => l.Id == result?.Layer.Id) ?? result?.Layer.Copy();
                if (layer != null) { layer.Visible = true; _chart.SetCloudLayerView(layer); }
                _chart.SelectCloud(row.Cloud.CloudId); ShowChartView();
            }
            catch (Exception error) { ShowError(error); }
        }

        #endregion

        #region Explicit workspace persistence

        private void SaveTimeProfilesClick(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveFileDialog dialog = new SaveFileDialog { Filter = "Order Flow profiles|*.json", FileName = "order-flow-time-profiles.json" };
                if (dialog.ShowDialog() != true) { return; }
                CalibrationStorage.AtomicJson(dialog.FileName, new OrderFlowTimeWorkspace("order-flow-time-1", _deltaTimeProfiles, OrderFlowCloudSettings.WithoutReset(_cloudTimeProfiles),
                    OrderFlowCloudSettings.WithoutReset(_cloud2TimeProfiles), _cloudLayers.Select(l => l.Copy()).ToList()), CancellationToken.None);
            }
            catch (Exception error) { ShowError(error); }
        }
        private void LoadTimeProfilesClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!CanEditTimeProfiles()) { return; }
                OpenFileDialog dialog = new OpenFileDialog { Filter = "Order Flow profiles|*.json" };
                if (dialog.ShowDialog() != true) { return; }
                OrderFlowTimeWorkspace state = JsonSerializer.Deserialize<OrderFlowTimeWorkspace>(File.ReadAllText(dialog.FileName));
                ValidateTimeWorkspace(state);
                foreach (OrderFlowCloudLayer old in _cloudLayers) { OrderFlowCloudLayer hidden = old.Copy(); hidden.Visible = false; _chart.SetCloudLayerView(hidden); }
                _deltaTimeProfiles = state.Delta; _cloudTimeProfiles = OrderFlowCloudSettings.WithoutReset(state.Cloud1); _cloud2TimeProfiles = OrderFlowCloudSettings.WithoutReset(state.Cloud2);
                _cloudLayers.Clear(); _cloudLayers.AddRange(state.Layers); RefreshCloudLayers();
                foreach (OrderFlowCloudLayer layer in _cloudLayers) { _chart.SetCloudLayerView(layer); }
            }
            catch (Exception error) { ShowError(error); }
        }
        internal static void ValidateTimeWorkspace(OrderFlowTimeWorkspace state)
        {
            if (state?.Version != "order-flow-time-1" || state.Layers == null || state.Layers.Any(l => l == null) || state.Layers.Select(l => l.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != state.Layers.Count)
            { throw new InvalidDataException("Invalid Order Flow time-profile workspace."); }
            state.Delta?.Validate(OrderFlowTimeThresholds.Delta); state.Cloud1?.Validate(OrderFlowTimeThresholds.Cloud); state.Cloud2?.Validate(OrderFlowTimeThresholds.Cloud);
            foreach (OrderFlowCloudLayer layer in state.Layers) { layer.Copy().Validate(); }
        }
        private void DisposeTimeProfiles()
        {
            ButtonAddCloud.Click -= AddCloudClick; ButtonEditCloudLayer.Click -= EditCloudClick; ButtonRemoveCloudLayer.Click -= RemoveCloudClick;
            ButtonCloudLayerVisibility.Click -= CloudVisibilityClick; ButtonCloudLayerFilter.Click -= CloudLayerFilterClick;
            ButtonTimeProfiles.Click -= TimeProfilesClick; ButtonSaveTimeProfiles.Click -= SaveTimeProfilesClick; ButtonLoadTimeProfiles.Click -= LoadTimeProfilesClick;
            DataGridAdditionalClouds.MouseDoubleClick -= AdditionalCloudDoubleClick;
        }

        #endregion
    }
}
