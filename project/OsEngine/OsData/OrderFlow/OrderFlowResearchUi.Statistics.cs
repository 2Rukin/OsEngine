/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.Entity;
using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace OsEngine.OsData.OrderFlow
{
    public partial class OrderFlowResearchUi
    {
        #region Statistics lifetime

        private OrderFlowCloudStatisticsJob _statisticsJob;
        private OrderFlowCloudStatisticsResult _statisticsResult;
        private OrderFlowResearchResult _statisticsSource;
        private DispatcherTimer _statisticsTimer;

        private void InitializeStatistics()
        {
            TabItemStatistics.Header = L("Statistics", "Статистика");
            ButtonStatsCalculate.Content = L("Calculate statistics", "Рассчитать статистику");
            ButtonStatsCancel.Content = L("Cancel statistics", "Отменить статистику");
            ButtonStatsShow.Content = L("Show recommended parameters on chart", "Показать рекомендуемые параметры на графике");
            TextBoxStatisticsSummary.Text = L("Calculate Clouds first. Study evaluates completed events, using ATR and a chronological held-out check. No fills or fees are modeled.",
                "Сначала рассчитайте Cloud. Статистика оценивает завершённые события с учётом ATR и отдельного более позднего участка проверки. Исполнение и издержки не моделируются.");
            _statisticsTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(150) };
            _statisticsTimer.Tick += StatisticsTimer_Tick;
            ButtonStatsCalculate.Click += ButtonStatsCalculate_Click;
            ButtonStatsCancel.Click += ButtonStatsCancel_Click;
            ButtonStatsShow.Click += ButtonStatsShow_Click;
            MenuItem show = new MenuItem { Header = L("Show parameters on chart", "Показать параметры на графике"), ToolTip = L("Apply this row's filters and volatility cohort without recalculation.", "Применить фильтры и группу волатильности этой строки без пересчёта.") };
            show.SetResourceReference(ForegroundProperty, "ControlForegroundWhite");
            show.Click += ButtonStatsShow_Click;
            DataGridRecommendations.ContextMenu = new ContextMenu(); DataGridRecommendations.ContextMenu.Items.Add(show);
            DataGridRecommendations.PreviewMouseRightButtonDown += StatisticsGrid_RightButtonDown;
            DataGridRecommendations.ContextMenuOpening += StatisticsGrid_ContextMenuOpening;
        }

        private void ClearStatistics()
        {
            _statisticsTimer?.Stop(); _statisticsJob?.Dispose(); _statisticsJob = null;
            _statisticsResult = null; _statisticsSource = null;
            DataGridRecommendations.ItemsSource = null;
            ButtonStatsShow.IsEnabled = ButtonStatsCancel.IsEnabled = false;
            TextBoxStatisticsSummary.Text = L("Statistics have not been calculated for this result.", "Статистика для этого результата ещё не рассчитана.");
        }

        private void DisposeStatistics()
        {
            ClearStatistics();
            if (_statisticsTimer != null) { _statisticsTimer.Tick -= StatisticsTimer_Tick; }
            ButtonStatsCalculate.Click -= ButtonStatsCalculate_Click;
            ButtonStatsCancel.Click -= ButtonStatsCancel_Click;
            ButtonStatsShow.Click -= ButtonStatsShow_Click;
            DataGridRecommendations.PreviewMouseRightButtonDown -= StatisticsGrid_RightButtonDown;
            DataGridRecommendations.ContextMenuOpening -= StatisticsGrid_ContextMenuOpening;
            if (DataGridRecommendations.ContextMenu?.Items[0] is MenuItem show) { show.Click -= ButtonStatsShow_Click; }
            DataGridRecommendations.ContextMenu = null;
        }

        private void UpdateStatisticsControls(bool running)
        {
            GridStatisticsSettings.IsEnabled = !running;
            ButtonStatsCalculate.IsEnabled = !running && _displayedResult?.Quality.ResearchAccepted == true &&
                (_displayedResult.CloudCalculated || _displayedResult.Cloud2Calculated);
            ButtonStatsShow.IsEnabled = !running && _statisticsResult?.Recommendations.Count > 0;
        }

        #endregion

        #region Statistics calculation

        private void ButtonStatsCalculate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_statisticsJob != null || _analysisThread != null || _replay != null || _displayedResult == null) { return; }
                OrderFlowCloudStatisticsSettings settings = new OrderFlowCloudStatisticsSettings { AtrPeriod = ParseInt(TextBoxStatsAtrPeriod.Text, "ATR bars"),
                    TargetAtr = TextBoxStatsTargetAtr.Text.ToDecimal(), AdverseAtr = TextBoxStatsStopAtr.Text.ToDecimal(),
                    HorizonsMinutes = ParseHorizons(TextBoxStatsHorizons.Text).ToArray(), FitPercent = ParseInt(TextBoxStatsTrainPercent.Text, "Fit percent"),
                    MinimumSamples = ParseInt(TextBoxStatsMinimumSamples.Text, "Minimum statistics sample") }.CopyValidated();
                ClearStatistics();
                _statisticsJob = new OrderFlowCloudStatisticsJob(_displayedRequest, _displayedResult, settings);
                SetRunningState(true); ButtonStatsCancel.IsEnabled = true;
                TextBoxStatisticsSummary.Text = L("Verifying tick file and measuring reactions. Cloud chains are reused. Cancel is available.",
                    "Проверка файла тиков и расчёт реакций. Используются готовые Cloud. Расчёт можно отменить.");
                _statisticsJob.Start(); _statisticsTimer.Start();
            }
            catch (Exception error) { ClearStatistics(); SetRunningState(false); ShowError(error); }
        }

        private void ButtonStatsCancel_Click(object sender, RoutedEventArgs e)
        {
            try { _statisticsJob?.Cancel(); ButtonStatsCancel.IsEnabled = false; }
            catch (Exception error) { ShowError(error); }
        }

        private void StatisticsTimer_Tick(object sender, EventArgs e)
        {
            if (_isClosing || _statisticsJob?.Finished != true) { return; }
            OrderFlowCloudStatisticsJob job = _statisticsJob;
            try
            {
                _statisticsTimer.Stop();
                if (!ReferenceEquals(job.Source, _displayedResult)) { return; }
                if (job.Error != null) { ShowError(job.Error); TextBoxStatisticsSummary.Text = L("Statistics failed; no recommendations published.", "Ошибка статистики; рекомендации не опубликованы."); }
                else if (job.Cancelled || job.Result == null) { TextBoxStatisticsSummary.Text = L("Statistics cancelled.", "Статистика отменена."); }
                else
                {
                    _statisticsResult = job.Result; _statisticsSource = job.Source;
                    DataGridRecommendations.ItemsSource = _statisticsResult.Recommendations;
                    if (_statisticsResult.Recommendations.Count > 0) { DataGridRecommendations.SelectedIndex = 0; }
                    TextBoxStatisticsSummary.Text = StatisticsSummary(_statisticsResult);
                }
            }
            catch (Exception error) { ShowError(error); }
            finally { job.Dispose(); _statisticsJob = null; SetRunningState(false); ButtonStatsCancel.IsEnabled = false; }
        }

        private static string StatisticsSummary(OrderFlowCloudStatisticsResult result)
        {
            return L("Day split ", "Разделение по датам ") + (result.SplitDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? L("unavailable (one day)", "недоступно (одна дата)"))
                + L(" · fit days ", " · дат подбора ") + result.FitDays + " / " + result.ObservedDays
                + L(" · complete fit/check events ", " · полных случаев подбор/проверка ")
                + result.Events.Count(item => item.Complete && item.Part == OrderFlowCloudSamplePart.Fit) + "/"
                + result.Events.Count(item => item.Complete && item.Part == OrderFlowCloudSamplePart.Test)
                + L("\nExcluded unfinished / neutral / ATR warm-up / overlap / incomplete / split overlap: ", "\nИсключено незавершённых / нейтральных / без ATR / пересекающихся / неполных / на границе выборок: ")
                + string.Join(" / ", result.SkippedUnfinished, result.SkippedNeutral, result.SkippedAtr, result.SkippedOverlap, result.SkippedIncomplete, result.Purged)
                + L("\nRules are selected on fit only. Check improvement is descriptive, not evidence of executable profit. Two layers are evaluated separately.",
                    "\nПараметры выбираются только на участке подбора. Прирост на проверке описывает реакцию цены, не доказывает прибыль с исполнением. Слои оцениваются отдельно.")
                + "\n" + result.ArtifactDirectory;
        }

        #endregion

        #region Recommendation display

        private void ButtonStatsShow_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_statisticsResult == null || !ReferenceEquals(_statisticsSource, _displayedResult) || DataGridRecommendations.SelectedItem is not OrderFlowCloudRecommendation row) { return; }
                if (_replay != null) { ReturnFromReplay(); }
                string prefix = row.Layer == 2 ? "Cloud2" : "Cloud";
                ImbalanceControl<ComboBox>(prefix, "ImbalanceSource").SelectedValue = row.Rule.Source;
                ImbalanceControl<ComboBox>(prefix, "ImbalanceDirection").SelectedValue = OrderFlowImbalanceDirection.Any;
                SetStatisticsField(prefix, "ImbalanceRatio", row.Rule.Ratio); SetStatisticsField(prefix, "ImbalanceVolume", row.Rule.Volume);
                SetStatisticsField(prefix, "ImbalanceDifference", row.Rule.Difference); SetStatisticsField(prefix, "ImbalanceDelta", row.Rule.Delta);
                SetStatisticsField(prefix, "MinCount", 0); SetStatisticsField(prefix, "MaxCount", row.Rule.MaximumCount);
                _chart.SetCloudFilter(row.Rule.CreateFilter().WithAllowedClouds(row.MatchingCloudIds), row.Layer == 2);
                if (row.Layer == 2) { CheckBoxShowCloud2.IsChecked = true; CheckBoxCloud2Rejected.IsChecked = false; }
                else { CheckBoxShowCloud.IsChecked = true; CheckBoxCloudRejected.IsChecked = false; }
                _chart.SetShowRejectedClouds(CheckBoxCloudRejected.IsChecked == true, CheckBoxCloud2Rejected.IsChecked == true);
                _chart.SetLayers(CheckBoxShowDelta.IsChecked == true, CheckBoxShowCloud.IsChecked == true, CheckBoxShowCloud2.IsChecked == true);
                RefreshCloudViewRows();
                ImbalanceControl<TextBlock>(prefix, "FilterStatus").Text += L(" · study volatility cohort ", " · группа волатильности из статистики ") + row.Regime;
                if (row.FocusCloudId != null) { _chart.SelectCloud(row.FocusCloudId); }
                TextBlockCandidateDetails.Text = row.Parameters + " · " + row.Regime + " · " + row.Reaction + " · " + row.Status;
                ShowChartView();
            }
            catch (Exception error) { ShowError(error); }
        }

        private void SetStatisticsField(string prefix, string name, decimal value)
        { ImbalanceControl<TextBox>(prefix, name).Text = value.ToString("G29", CultureInfo.InvariantCulture); }

        private void StatisticsGrid_RightButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                DataGridRow row = ItemsControl.ContainerFromElement(DataGridRecommendations, e.OriginalSource as DependencyObject) as DataGridRow;
                DataGridRecommendations.ContextMenu.DataContext = row?.Item;
                if (row?.Item is OrderFlowCloudRecommendation) { DataGridRecommendations.SelectedItem = row.Item; }
            }
            catch (Exception error) { ShowError(error); }
        }

        private void StatisticsGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            try { e.Handled = DataGridRecommendations.ContextMenu.DataContext is not OrderFlowCloudRecommendation || !ButtonStatsShow.IsEnabled; }
            catch (Exception error) { e.Handled = true; ShowError(error); }
        }

        #endregion
    }
}
