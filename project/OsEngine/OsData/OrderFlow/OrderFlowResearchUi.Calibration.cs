/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.OsData.OrderFlow.Calibration;
using OsEngine.Themes;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace OsEngine.OsData.OrderFlow
{
    public partial class OrderFlowResearchUi
    {
        private readonly CalibrationWindowSet _calibrationWindows = new CalibrationWindowSet();
        private CalibrationChartData _savedCalibrationChart;
        private CalibrationLayerPlayback _calibrationPlayback;
        private DispatcherTimer _calibrationReplayTimer;

        private void InitializeCalibration()
        {
            ButtonCalibration.Content = L("Cloud calibration", "Подбор Cloud");
            ButtonCalibration.Click += CalibrationClick;
            _chart.CalibrationSelected += CalibrationSelected;
            ThemeManager.ThemeChangedEvent += CalibrationThemeChanged;
            _calibrationReplayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _calibrationReplayTimer.Tick += CalibrationReplayTick;
        }
        private void CalibrationClick(object sender, RoutedEventArgs e)
        {
            try { _calibrationWindows.Open("calibration", () => new CloudCalibrationWindow(CreateExplorerInput, ShowCalibrationChart)); }
            catch (Exception error) { ShowError(error); }
        }
        private void ShowCalibrationChart(CalibrationChartData data, CalibrationMarker marker)
        {
            if (_chart.IsReplaying) { throw new InvalidOperationException(L("Return from replay before changing layers", "Вернитесь из replay перед сменой слоёв")); }
            if (_displayedResult == null)
            {
                _chart.SetResult(data.Prices); _chart.SetLayers(false, false, false);
                ComboBoxTimeFrame.SelectedValue = data.TimeFrame; _chart.SetTimeFrame(data.TimeFrame);
                TextBlockChartLegend.Text = L("Saved calibration layers. For tick replay, run Order Flow on the same input and dates.",
                    "Сохранённые слои calibration. Для tick replay выполните расчёт Order Flow по тому же файлу и датам.");
            }
            else if (_displayedResult.InputHash != data.Prices.InputHash)
            { throw new InvalidOperationException(L("The Order Flow chart uses another input; calculate the matching file first", "График Order Flow использует другой input; сначала рассчитайте соответствующий файл")); }
            if (_displayedRequest != null && data.Layers.Any(l => l.Rule.Provenance.FromDate != _displayedRequest.FromDate || l.Rule.Provenance.ToDate != _displayedRequest.ToDate || l.Rule.Provenance.PriceStep != _displayedRequest.PriceStep))
            { throw new InvalidOperationException(L("Calibration dates and PriceStep must match the main research", "Даты и PriceStep calibration должны совпадать с основным исследованием")); }
            _savedCalibrationChart = data; _chart.SetCalibrationLayers(data.Layers);
            if (marker != null) { _chart.SelectCalibration(marker); }
            ShowChartView();
        }
        private void RestoreCalibrationLayers()
        {
            if (_savedCalibrationChart != null && _displayedResult?.InputHash == _savedCalibrationChart.Prices.InputHash &&
                _savedCalibrationChart.Layers.All(l => l.Rule.Provenance.FromDate == _displayedRequest?.FromDate && l.Rule.Provenance.ToDate == _displayedRequest?.ToDate && l.Rule.Provenance.PriceStep == _displayedRequest?.PriceStep))
            { _chart.SetCalibrationLayers(_savedCalibrationChart.Layers); }
        }
        private void StartCalibrationReplay()
        {
            StopCalibrationReplay();
            if (_savedCalibrationChart == null || _savedCalibrationChart.Layers.IsDefaultOrEmpty || _displayedResult?.InputHash != _savedCalibrationChart.Prices.InputHash) { return; }
            ImmutableArray<CloudRule> rules = _savedCalibrationChart.Layers.Select(l => l.Rule).Where(r => r.Provenance.FromDate == _displayedRequest.FromDate &&
                r.Provenance.ToDate == _displayedRequest.ToDate && r.Provenance.PriceStep == _displayedRequest.PriceStep).ToImmutableArray();
            _calibrationPlayback = new CalibrationLayerPlayback(rules); _calibrationReplayTimer.Start();
        }
        private void StopCalibrationReplay() { _calibrationReplayTimer?.Stop(); _calibrationPlayback?.Dispose(); _calibrationPlayback = null; }
        private void CalibrationReplayTick(object sender, EventArgs e)
        {
            try
            {
                if (_calibrationPlayback?.Take(out CalibrationReplayFrame frame, out Exception error) != true) { return; }
                if (error != null) { StopCalibrationReplay(); throw error; }
                if (frame != null) { _chart.SetCalibrationPlaybackLayers(frame.Layers); if (frame.Complete) { StopCalibrationReplay(); } }
            }
            catch (Exception error) { ShowError(error); }
        }
        private void CalibrationSelected(CalibrationMarker marker)
        {
            try { _calibrationWindows.Open("anatomy-" + marker.CloudId, () => new CloudAnatomyWindow(marker)); }
            catch (Exception error) { ShowError(error); }
        }
        private void CalibrationThemeChanged()
        {
            try { if (_isClosing) { return; } if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(CalibrationThemeChanged)); return; } _chart.InvalidateVisual(); }
            catch (Exception error) { ShowError(error); }
        }
        private void DisposeCalibration()
        {
            StopCalibrationReplay(); _calibrationReplayTimer.Tick -= CalibrationReplayTick;
            ButtonCalibration.Click -= CalibrationClick; _chart.CalibrationSelected -= CalibrationSelected;
            ThemeManager.ThemeChangedEvent -= CalibrationThemeChanged;
            _chart.SetCalibrationLayers(ImmutableArray<CalibrationLayer>.Empty); _savedCalibrationChart = null; _calibrationWindows.Dispose();
        }
    }
}
