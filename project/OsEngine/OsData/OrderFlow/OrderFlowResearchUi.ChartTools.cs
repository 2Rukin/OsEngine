/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.Entity;
using OsEngine.Logging;
using OsEngine.Market;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OsEngine.OsData.OrderFlow
{
    public partial class OrderFlowResearchUi
    {
        #region Setup and lifetime

        private OrderFlowChartWindow _chartWindow;
        private OrderFlowChartHost _chartHost;
        private bool _updatingDrawingTools;

        private void InitializeChartTools()
        {
            _chartHost = new OrderFlowChartHost(ContentControlChartHome);
            OrderFlowChartHost.BindSettings(ComboBoxChartTimeFrame, ComboBoxTimeFrame, SliderChartCloudScale,
                SliderCloudScale, TextBlockChartCloudScale);
            OrderFlowChartHost.BindVisualSlider(SliderChartCloudContrast, SliderCloudContrast, TextBlockChartCloudContrast, "{0:F1}");
            LabelCloudContrast.Content = LabelChartCloudContrast.Content = L("Cloud contrast", "Контраст Cloud");
            CheckBoxCloudVolumes.Content = L("Cloud volumes", "Объёмы Cloud");
            LabelRightPadding.Content = L("Right space %", "Поле справа, %");
            SliderRightPadding.ValueChanged += SliderRightPadding_ValueChanged;
            ComboBoxDrawingTool.ItemsSource = new List<KeyValuePair<OrderFlowDrawingTool, string>>
            {
                new KeyValuePair<OrderFlowDrawingTool, string>(OrderFlowDrawingTool.Select, L("Select / pan", "Выбор / прокрутка")),
                new KeyValuePair<OrderFlowDrawingTool, string>(OrderFlowDrawingTool.Horizontal, L("Horizontal line", "Горизонтальная")),
                new KeyValuePair<OrderFlowDrawingTool, string>(OrderFlowDrawingTool.Trend, L("Trend line", "Наклонная"))
            };
            ComboBoxPriceDisplay.ItemsSource = new List<KeyValuePair<OrderFlowPriceDisplay, string>>
            {
                new KeyValuePair<OrderFlowPriceDisplay, string>(OrderFlowPriceDisplay.Candles, L("Candles", "Свечи")),
                new KeyValuePair<OrderFlowPriceDisplay, string>(OrderFlowPriceDisplay.Bars, L("OHLC bars", "Бары OHLC")),
                new KeyValuePair<OrderFlowPriceDisplay, string>(OrderFlowPriceDisplay.HighLow, L("High/Low lines", "Линии High/Low")),
                new KeyValuePair<OrderFlowPriceDisplay, string>(OrderFlowPriceDisplay.MutedHighLow, L("Grey High/Low", "Серые High/Low"))
            };
            ComboBoxPriceDisplay.SelectedValue = OrderFlowPriceDisplay.Candles;
            LabelPriceDisplay.Content = L("View", "Вид");
            CheckBoxCloudPriceMode.Content = L("Chart by Cloud", "График по Cloud");
            CheckBoxCloudPriceMode.ToolTip = L("Connect Cloud prices in source order over a muted price background. Requires calculated Clouds.",
                "Соединяет цены Cloud по порядку поверх приглушённого ценового фона. Нужен выполненный расчёт Cloud.");
            ComboBoxDrawingThickness.ItemsSource = new double[] { 1, 2, 3, 4, 5, 6 };
            LabelDrawingTool.Content = L("Drawing", "Рисование");
            TextBlockDrawingColor.Text = L("Color", "Цвет");
            LabelDrawingThickness.Content = L("Width", "Толщина");
            CheckBoxDrawingLeft.Content = L("Extend left", "Продолжить влево");
            CheckBoxDrawingRight.Content = L("Extend right", "Продолжить вправо");
            ButtonDrawingDelete.Content = L("Delete line", "Удалить линию");
            ButtonDrawingClear.Content = L("Clear lines", "Очистить линии");
            TextBlockDetachedChart.Text = L("The chart is open in a separate window.", "График открыт в отдельном окне.");
            ButtonActivateChartWindow.Content = L("Show chart window", "Показать окно графика");
            UpdateChartWindowState();
            UpdateDrawingTools();
            SliderCloudContrast.ValueChanged += SliderCloudContrast_ValueChanged;
            CheckBoxCloudVolumes.Click += CheckBoxCloudVolumes_Click;
            CheckBoxCloudPriceMode.Click += CheckBoxCloudPriceMode_Click;
            ComboBoxPriceDisplay.SelectionChanged += ComboBoxPriceDisplay_SelectionChanged;
            ButtonChartPopOut.Click += ButtonChartPopOut_Click;
            ButtonActivateChartWindow.Click += ButtonActivateChartWindow_Click;
            ComboBoxDrawingTool.SelectionChanged += ComboBoxDrawingTool_SelectionChanged;
            ComboBoxDrawingThickness.SelectionChanged += ComboBoxDrawingThickness_SelectionChanged;
            CheckBoxDrawingLeft.Click += DrawingExtension_Click;
            CheckBoxDrawingRight.Click += DrawingExtension_Click;
            ButtonDrawingColor.Click += ButtonDrawingColor_Click;
            ButtonDrawingDelete.Click += ButtonDrawingDelete_Click;
            ButtonDrawingClear.Click += ButtonDrawingClear_Click;
            _chart.DrawingChanged += Chart_DrawingChanged;
        }

        private void DisposeChartTools()
        {
            _chart.DrawingChanged -= Chart_DrawingChanged;
            SliderRightPadding.ValueChanged -= SliderRightPadding_ValueChanged;
            SliderCloudContrast.ValueChanged -= SliderCloudContrast_ValueChanged;
            CheckBoxCloudVolumes.Click -= CheckBoxCloudVolumes_Click;
            CheckBoxCloudPriceMode.Click -= CheckBoxCloudPriceMode_Click;
            ComboBoxPriceDisplay.SelectionChanged -= ComboBoxPriceDisplay_SelectionChanged;
            ButtonChartPopOut.Click -= ButtonChartPopOut_Click;
            ButtonActivateChartWindow.Click -= ButtonActivateChartWindow_Click;
            ComboBoxDrawingTool.SelectionChanged -= ComboBoxDrawingTool_SelectionChanged;
            ComboBoxDrawingThickness.SelectionChanged -= ComboBoxDrawingThickness_SelectionChanged;
            CheckBoxDrawingLeft.Click -= DrawingExtension_Click;
            CheckBoxDrawingRight.Click -= DrawingExtension_Click;
            ButtonDrawingColor.Click -= ButtonDrawingColor_Click;
            ButtonDrawingDelete.Click -= ButtonDrawingDelete_Click;
            ButtonDrawingClear.Click -= ButtonDrawingClear_Click;
            _chart.CancelDrawingGesture();
            if (_chartWindow != null)
            {
                _chartWindow.Closed -= ChartWindow_Closed;
                _chartWindow.Close();
                _chartWindow = null;
            }
            _chartHost?.Dispose();
            _chartHost = null;
        }

        #endregion

        #region Separate window

        private void ButtonChartPopOut_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_chartWindow != null) { _chartWindow.Close(); }
                else { OpenChartWindow(); }
            }
            catch (Exception error) { ShowError(error); }
        }

        private void ButtonActivateChartWindow_Click(object sender, RoutedEventArgs e)
        {
            try { if (_chartWindow == null) { OpenChartWindow(); } else { ShowChartView(); } }
            catch (Exception error) { ShowError(error); }
        }

        private void OpenChartWindow()
        {
            if (_isClosing || _chartWindow != null) { return; }
            _chart.CancelDrawingGesture();
            OrderFlowChartWindow window = new OrderFlowChartWindow();
            _chartWindow = window;
            window.Closed += ChartWindow_Closed;
            try
            {
                _chartHost.MoveTo(window.SurfaceHost);
                UpdateChartWindowState();
                window.Show();
                _chart.Focus();
            }
            catch
            {
                window.Closed -= ChartWindow_Closed;
                _chartWindow = null;
                _chartHost.Restore();
                window.Close();
                UpdateChartWindowState();
                throw;
            }
        }

        private void ChartWindow_Closed(object sender, EventArgs e)
        {
            try
            {
                OrderFlowChartWindow window = (OrderFlowChartWindow)sender;
                window.Closed -= ChartWindow_Closed;
                _chartWindow = null;
                if (_isClosing) { _chartHost?.Dispose(); return; }
                _chart.CancelDrawingGesture();
                _chartHost.Restore();
                UpdateChartWindowState();
                TabControlResults.SelectedItem = TabItemChart;
                _chart.Focus();
            }
            catch (Exception error) { ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error); }
        }

        private void UpdateChartWindowState()
        {
            StackPanelDetachedChart.Visibility = _chartWindow == null ? Visibility.Collapsed : Visibility.Visible;
            ContentControlChartHome.Visibility = _chartWindow == null ? Visibility.Visible : Visibility.Collapsed;
            ButtonChartPopOut.Content = _chartWindow == null ? L("Separate window", "Отдельное окно")
                : L("Return to main window", "Вернуть в основное окно");
        }

        private void ShowChartView()
        {
            TabControlResults.SelectedItem = TabItemChart;
            if (_chartWindow == null) { Activate(); _chart.Focus(); return; }
            if (_chartWindow.WindowState == WindowState.Minimized) { _chartWindow.WindowState = WindowState.Normal; }
            _chartWindow.Activate();
            _chart.Focus();
        }

        #endregion

        #region Drawing controls

        private void UpdateDrawingTools()
        {
            _updatingDrawingTools = true;
            try
            {
                ComboBoxDrawingTool.SelectedValue = _chart.DrawingTool;
                ComboBoxDrawingThickness.SelectedItem = _chart.DrawingThickness;
                BorderDrawingColor.Background = new SolidColorBrush(_chart.DrawingColor);
                CheckBoxDrawingLeft.IsChecked = _chart.DrawingExtendLeft;
                CheckBoxDrawingRight.IsChecked = _chart.DrawingExtendRight;
                ButtonDrawingDelete.IsEnabled = _chart.SelectedDrawing != null;
                ButtonDrawingClear.IsEnabled = _chart.Drawings.Count > 0;
                TextBlockDrawingHelp.Text = _chart.DrawingTool != OrderFlowDrawingTool.Select
                    ? (_chart.HasPendingAnchor ? L("Click the second point. Esc cancels.", "Укажите вторую точку. Esc — отмена.")
                        : L("Click two points; the tool stays active. Drag an existing line to move it. Esc cancels.", "Укажите две точки; инструмент останется активным. Линию можно перетаскивать целиком. Esc — отмена."))
                    : _chart.SelectedDrawing != null
                        ? L("Line selected. Drag its body to move it, or endpoints to reshape it; Delete removes it.", "Линия выбрана. Тяните за середину для переноса, за концы — для изменения формы; Delete — удалить.")
                        : L("Select a line to edit it. Empty space pans the chart. Annotations reset on a new result.",
                            "Выберите линию для изменения. Пустая область прокручивает график. Новый результат очищает рисунки.");
            }
            finally { _updatingDrawingTools = false; }
        }

        private void Chart_DrawingChanged(object sender, EventArgs e)
        {
            try { if (!_isClosing) { UpdateDrawingTools(); } }
            catch (Exception error) { ShowError(error); }
        }

        private void ComboBoxDrawingTool_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (!_updatingDrawingTools && ComboBoxDrawingTool.SelectedValue is OrderFlowDrawingTool tool)
                { _chart.SetDrawingTool(tool); _chart.Focus(); }
            }
            catch (Exception error) { ShowError(error); }
        }

        private void SliderRightPadding_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try { _chart.SetRightPadding(e.NewValue); }
            catch (Exception error) { ShowError(error); }
        }

        private void SliderCloudContrast_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try { _chart.SetCloudContrast(e.NewValue); }
            catch (Exception error) { ShowError(error); }
        }

        private void CheckBoxCloudVolumes_Click(object sender, RoutedEventArgs e)
        {
            try { _chart.SetCloudVolumeLabels(CheckBoxCloudVolumes.IsChecked == true); }
            catch (Exception error) { ShowError(error); }
        }

        private void CheckBoxCloudPriceMode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                bool enabled = CheckBoxCloudPriceMode.IsChecked == true;
                ComboBoxPriceDisplay.IsEnabled = !enabled;
                _chart.SetCloudPriceMode(enabled);
            }
            catch (Exception error) { ShowError(error); }
        }

        private void ComboBoxPriceDisplay_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try { if (ComboBoxPriceDisplay.SelectedValue is OrderFlowPriceDisplay display) { _chart.SetPriceDisplay(display); } }
            catch (Exception error) { ShowError(error); }
        }

        private void ApplyDrawingStyle()
        {
            if (_updatingDrawingTools || ComboBoxDrawingThickness.SelectedItem is not double thickness) { return; }
            _chart.SetDrawingStyle(_chart.DrawingColor, thickness, CheckBoxDrawingLeft.IsChecked == true, CheckBoxDrawingRight.IsChecked == true);
        }

        private void ComboBoxDrawingThickness_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try { ApplyDrawingStyle(); }
            catch (Exception error) { ShowError(error); }
        }

        private void DrawingExtension_Click(object sender, RoutedEventArgs e)
        {
            try { ApplyDrawingStyle(); }
            catch (Exception error) { ShowError(error); }
        }

        private void ButtonDrawingColor_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Color color = _chart.DrawingColor;
                ColorCustomDialog dialog = new ColorCustomDialog { Owner = (Window)_chartWindow ?? this,
                    Color = System.Drawing.Color.FromArgb(color.R, color.G, color.B) };
                dialog.ShowDialog();
                System.Drawing.Color selected = dialog.Color;
                _chart.SetDrawingStyle(Color.FromRgb(selected.R, selected.G, selected.B), _chart.DrawingThickness,
                    _chart.DrawingExtendLeft, _chart.DrawingExtendRight);
            }
            catch (Exception error) { ShowError(error); }
        }

        private void ButtonDrawingDelete_Click(object sender, RoutedEventArgs e)
        {
            try { _chart.DeleteSelectedDrawing(); }
            catch (Exception error) { ShowError(error); }
        }

        private void ButtonDrawingClear_Click(object sender, RoutedEventArgs e)
        {
            try { _chart.ClearDrawings(); }
            catch (Exception error) { ShowError(error); }
        }

        #endregion
    }
}
