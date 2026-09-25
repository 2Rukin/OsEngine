/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace OsEngine.OsData.OrderFlow
{
    public partial class OrderFlowResearchUi
    {
        #region Playback lifetime

        private OrderFlowReplaySession _replay;
        private DispatcherTimer _replayTimer;
        private OrderFlowResearchResult _displayedResult;
        private OrderFlowResearchRequest _displayedRequest;
        private string _beforeReplayLegend;
        private string _beforeReplayDetails;

        private void InitializeReplay()
        {
            ButtonReplayPlay.Content = L("Replay", "Реплей");
            ButtonReplayStep.Content = L("One tick", "Один тик");
            ButtonReplayStep.ToolTip = L("Available after the worker has paused. Press Pause first.", "Доступно после остановки потока. Сначала нажмите «Пауза».");
            ButtonReplayReturn.Content = L("Full chart", "Полный график");
            LabelReplaySpeed.Content = L("Speed", "Скорость");
            CheckBoxReplaySkipGaps.Content = L("Skip gaps > 60 s", "Пропуск пауз > 60 с");
            ComboBoxReplaySpeed.ItemsSource = new double[] { 0.25, 0.5, 1, 2, 5, 10, 25, 50, 100, 250, 500, 1000 }
                .Select(speed => new KeyValuePair<double, string>(speed, speed.ToString("0.##", CultureInfo.InvariantCulture) + "×")).ToList();
            ComboBoxReplaySpeed.SelectedValue = 1d;
            _replayTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(100) };
            _replayTimer.Tick += ReplayTimer_Tick;
            ButtonReplayPlay.Click += ButtonReplayPlay_Click;
            ButtonReplayStep.Click += ButtonReplayStep_Click;
            ButtonReplayReturn.Click += ButtonReplayReturn_Click;
            ComboBoxReplaySpeed.SelectionChanged += ComboBoxReplaySpeed_SelectionChanged;
            CheckBoxReplaySkipGaps.Click += CheckBoxReplaySkipGaps_Click;
            TextBlockReplayStatus.Text = L("Run research, then replay its selected period.", "Выполните расчёт, затем запустите реплей выбранного периода.");
        }

        private void StartReplay()
        {
            if (_displayedResult?.Quality.ResearchAccepted != true || _displayedRequest == null || _analysisThread != null || _statisticsJob != null) { return; }
            _replay = new OrderFlowReplaySession(_displayedRequest, _displayedResult.Input.Sha256,
                (double)ComboBoxReplaySpeed.SelectedValue, CheckBoxReplaySkipGaps.IsChecked == true);
            _beforeReplayLegend = TextBlockChartLegend.Text;
            _beforeReplayDetails = TextBlockCandidateDetails.Text;
            _chart.BeginReplay(_replay.CloudReferenceVolume, _replay.Cloud2ReferenceVolume);
            StartCalibrationReplay();
            SetRunningState(true);
            ButtonCancel.IsEnabled = ButtonOpenArtifacts.IsEnabled = ButtonChartSelected.IsEnabled = false;
            TabItemSummary.IsEnabled = TabItemCandidates.IsEnabled = TabItemClouds.IsEnabled = TabItemClouds2.IsEnabled = TabItemJournal.IsEnabled = false;
            TabControlResults.SelectedItem = TabItemChart;
            TextBlockCandidateDetails.Text = string.Empty;
            TextBlockChartLegend.Text = L(
                "Tick replay on the selected timeframe. Forming candles and qualified Clouds use consumed ticks only. Delta candidates appear after a timestamp bucket closes; outcome colors appear after their horizons. Each Cloud size reference = its configured chain sum or single-tick threshold. Full-result tables are available after return.",
                "Реплей тиков на выбранном таймфрейме. Свечи и прошедшие порог Cloud формируются по поступившим тикам. Кандидаты дельты появляются после закрытия группы timestamp; цвет исхода — после горизонта. Опорный объём каждого Cloud = заданная сумма цепочки или порог одиночного тика. Таблицы полного результата доступны после возврата.");
            TextBlockReplayStatus.Text = L("Preparing: verifying the tick file…", "Подготовка: проверка файла тиков…");
            ButtonReplayPlay.IsEnabled = ButtonReplayReturn.IsEnabled = true;
            ButtonReplayStep.IsEnabled = false;
            ButtonReplayPlay.Content = L("Pause", "Пауза");
            _replay.Start();
            _replayTimer.Start();
            ShowChartView();
        }

        private void ReturnFromReplay()
        {
            if (_replay == null) { return; }
            _replayTimer.Stop();
            StopCalibrationReplay();
            _replay.Dispose();
            _replay = null;
            _chart.EndReplay();
            RefreshCloudViewRows();
            SetRunningState(false);
            TabItemSummary.IsEnabled = TabItemCandidates.IsEnabled = TabItemClouds.IsEnabled = TabItemClouds2.IsEnabled = TabItemJournal.IsEnabled = true;
            ButtonOpenArtifacts.IsEnabled = System.IO.Directory.Exists(_displayedResult.ArtifactDirectory);
            ButtonChartSelected.IsEnabled = true;
            TextBlockChartLegend.Text = _beforeReplayLegend;
            TextBlockCandidateDetails.Text = _beforeReplayDetails;
            ButtonReplayStep.IsEnabled = ButtonReplayReturn.IsEnabled = false;
            ButtonReplayPlay.Content = L("Replay", "Реплей");
            TextBlockReplayStatus.Text = L("Full historical result restored.", "Восстановлен полный исторический результат.");
        }

        private void DisposeReplay()
        {
            _replayTimer.Stop();
            _replayTimer.Tick -= ReplayTimer_Tick;
            _replay?.Dispose();
            _replay = null;
            ButtonReplayPlay.Click -= ButtonReplayPlay_Click;
            ButtonReplayStep.Click -= ButtonReplayStep_Click;
            ButtonReplayReturn.Click -= ButtonReplayReturn_Click;
            ComboBoxReplaySpeed.SelectionChanged -= ComboBoxReplaySpeed_SelectionChanged;
            CheckBoxReplaySkipGaps.Click -= CheckBoxReplaySkipGaps_Click;
        }

        #endregion

        #region Playback controls

        private void ButtonReplayPlay_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_replay?.Finished == true) { ReturnFromReplay(); }
                if (_replay == null) { StartReplay(); return; }
                _replay.SetPaused(!_replay.Clock.Paused);
                ButtonReplayStep.IsEnabled = false;
                ButtonReplayPlay.Content = _replay.Clock.Paused ? L("Continue", "Продолжить") : L("Pause", "Пауза");
            }
            catch (Exception error) { ReturnFromReplay(); ShowError(error); }
        }

        private void ButtonReplayStep_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_replay?.TryStep() == true) { ButtonReplayStep.IsEnabled = false; }
            }
            catch (Exception error) { ShowError(error); }
        }

        private void ButtonReplayReturn_Click(object sender, RoutedEventArgs e)
        {
            try { ReturnFromReplay(); }
            catch (Exception error) { ShowError(error); }
        }

        private void ComboBoxReplaySpeed_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try { if (ComboBoxReplaySpeed.SelectedValue is double speed) { _replay?.Clock.SetSpeed(speed); } }
            catch (Exception error) { ShowError(error); }
        }

        private void CheckBoxReplaySkipGaps_Click(object sender, RoutedEventArgs e)
        {
            try { _replay?.Clock.SetSkipGaps(CheckBoxReplaySkipGaps.IsChecked == true); }
            catch (Exception error) { ShowError(error); }
        }

        private void ReplayTimer_Tick(object sender, EventArgs e)
        {
            if (_isClosing || _replay == null) { return; }
            try
            {
                // Read terminal state first: a later finish leaves the timer alive to collect its final frame.
                bool finished = _replay.Finished;
                OrderFlowReplayFrame frame = _replay.TakeFrame(out bool pausedAtBoundary);
                if (_replay.Error != null)
                {
                    Exception error = _replay.Error;
                    ReturnFromReplay();
                    ShowError(error);
                    return;
                }
                if (frame != null)
                {
                    _chart.ApplyReplayFrame(frame.Result);
                    _chart.SetCalibrationReplay(frame.SourceSequence, frame.Complete);
                    _calibrationPlayback?.Request(frame.SourceSequence, frame.Complete);
                    RefreshCloudViewRows();
                    TextBlockReplayStatus.Text = frame.Time.ToString("dd.MM.yyyy HH:mm:ss.ffffff", CultureInfo.InvariantCulture)
                        + L(" · ticks ", " · тики ") + frame.TickCount + " / " + _displayedResult.Quality.DealCount;
                }
                ButtonReplayStep.IsEnabled = pausedAtBoundary && !finished;
                if (finished)
                {
                    _replayTimer.Stop();
                    TextBlockReplayStatus.Text += L(" · complete", " · завершён");
                    ButtonReplayPlay.Content = L("Replay again", "Заново");
                    ButtonReplayStep.IsEnabled = false;
                }
            }
            catch (Exception error) { ReturnFromReplay(); ShowError(error); }
        }

        #endregion
    }
}
