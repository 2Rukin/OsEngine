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
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OsEngine.OsData.OrderFlow
{
    /// <summary>
    /// Offline paired-QSH workbench for inspecting causal Order Flow research
    /// features, candidates and separately computed future market-path labels.
    /// </summary>
    public partial class OrderFlowResearchUi
    {
        private OrderFlowResearchChart _chart;
        private Thread _analysisThread;
        private CancellationTokenSource _cancellation;
        private OrderFlowResearchRequest _pendingRequest;
        private OrderFlowResearchResult _completedResult;
        private Exception _workerError;
        private bool _workerCancelled;
        private volatile bool _isClosing;

        /// <summary>
        /// Creates the research-only workbench. The window does not start a
        /// replay or access external systems until the user selects local files.
        /// </summary>
        public OrderFlowResearchUi()
        {
            InitializeComponent();
            Layout.StickyBorders.Listen(this);
            Layout.StartupLocation.Start_FitHeightToWorkArea(this);

            _chart = new OrderFlowResearchChart();
            ContentControlChart.Content = _chart;
            ComboBoxTimeFrame.ItemsSource = Enum.GetValues(typeof(OrderFlowDisplayTimeFrame));
            ComboBoxTimeFrame.SelectedItem = OrderFlowDisplayTimeFrame.Min1;

            ButtonBrowseDeals.Click += ButtonBrowseDeals_Click;
            ButtonBrowseQuotes.Click += ButtonBrowseQuotes_Click;
            ButtonBrowseOutput.Click += ButtonBrowseOutput_Click;
            ButtonRun.Click += ButtonRun_Click;
            ButtonCancel.Click += ButtonCancel_Click;
            ButtonOpenArtifacts.Click += ButtonOpenArtifacts_Click;
            ComboBoxTimeFrame.SelectionChanged += ComboBoxTimeFrame_SelectionChanged;
            DataGridCandidates.SelectionChanged += DataGridCandidates_SelectionChanged;
            DataGridCandidates.MouseDoubleClick += DataGridCandidates_MouseDoubleClick;
            Closing += OrderFlowResearchUi_Closing;

            ApplyLocalization();
        }

        private void ApplyLocalization()
        {
            Title = OsLocalization.ConvertToLocString("Eng:Order Flow Research_Ru:Исследование Order Flow_");
            LabelDeals.Content = OsLocalization.ConvertToLocString("Eng:Deals QSH_Ru:Сделки QSH_");
            LabelQuotes.Content = OsLocalization.ConvertToLocString("Eng:Quotes QSH_Ru:Стакан QSH_");
            LabelOutput.Content = OsLocalization.ConvertToLocString("Eng:Output folder_Ru:Папка результатов_");
            ButtonBrowseDeals.Content = OsLocalization.ConvertToLocString("Eng:Browse_Ru:Выбрать_");
            ButtonBrowseQuotes.Content = OsLocalization.ConvertToLocString("Eng:Browse_Ru:Выбрать_");
            ButtonBrowseOutput.Content = OsLocalization.ConvertToLocString("Eng:Browse_Ru:Выбрать_");
            LabelWindow.Content = OsLocalization.ConvertToLocString("Eng:Window sec_Ru:Окно сек_");
            LabelDelta.Content = OsLocalization.ConvertToLocString("Eng:Min delta_Ru:Мин дельта_");
            LabelPriceTicks.Content = OsLocalization.ConvertToLocString("Eng:Price ticks_Ru:Тики цены_");
            LabelBookLevels.Content = OsLocalization.ConvertToLocString("Eng:Book levels_Ru:Уровни стакана_");
            LabelBookAge.Content = OsLocalization.ConvertToLocString("Eng:Book age ms_Ru:Возраст стакана мс_");
            LabelCooldown.Content = OsLocalization.ConvertToLocString("Eng:Cooldown ms_Ru:Пауза мс_");
            LabelBackground.Content = OsLocalization.ConvertToLocString("Eng:Background sec_Ru:Фон сек_");
            LabelHorizons.Content = OsLocalization.ConvertToLocString("Eng:Horizons sec_Ru:Горизонты сек_");
            LabelTarget.Content = OsLocalization.ConvertToLocString("Eng:Target ticks_Ru:Цель тики_");
            LabelInvalidation.Content = OsLocalization.ConvertToLocString("Eng:Invalid ticks_Ru:Отмена тики_");
            LabelPriceStep.Content = OsLocalization.ConvertToLocString("Eng:Price step override_Ru:Шаг цены вручную_");
            LabelVolumeStep.Content = OsLocalization.ConvertToLocString("Eng:Volume step override_Ru:Шаг объема вручную_");
            ButtonRun.Content = OsLocalization.ConvertToLocString("Eng:Run research_Ru:Запустить_");
            ButtonCancel.Content = OsLocalization.ConvertToLocString("Eng:Cancel_Ru:Отмена_");
            ButtonOpenArtifacts.Content = OsLocalization.ConvertToLocString("Eng:Open artifacts_Ru:Открыть файлы_");
            TabItemSummary.Header = OsLocalization.ConvertToLocString("Eng:Summary_Ru:Сводка_");
            TabItemCandidates.Header = OsLocalization.ConvertToLocString("Eng:Candidates_Ru:Кандидаты_");
            TabItemChart.Header = OsLocalization.ConvertToLocString("Eng:Chart_Ru:График_");
            TabItemJournal.Header = OsLocalization.ConvertToLocString("Eng:Event journal_Ru:Журнал событий_");
            LabelTimeFrame.Content = OsLocalization.ConvertToLocString("Eng:Display timeframe_Ru:Таймфрейм отображения_");
            TextBlockChartBoundary.Text = OsLocalization.ConvertToLocString(
                "Eng:Visualization only. Signals are not recalculated._Ru:Только визуализация. Сигналы не пересчитываются._");
            TextBlockStatus.Text = OsLocalization.ConvertToLocString("Eng:Ready_Ru:Готово_");
            TextBlockEvidence.Text = OsLocalization.ConvertToLocString(
                "Eng:Research only. No trades or PnL._Ru:Только исследование. Без сделок и PnL._");
        }

        private void ButtonBrowseDeals_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog();
                dialog.Filter = "Deals QSH (*.Deals.qsh)|*.Deals.qsh|QSH files (*.qsh)|*.qsh|All files (*.*)|*.*";
                dialog.CheckFileExists = true;

                if (dialog.ShowDialog() == true)
                {
                    TextBoxDealsPath.Text = dialog.FileName;
                    TryFillPairedPath(dialog.FileName, ".Deals.qsh", ".Quotes.qsh", TextBoxQuotesPath);
                    SetDefaultOutputPath(dialog.FileName);
                }
            }
            catch (Exception error)
            {
                ShowError(error);
            }
        }

        private void ButtonBrowseQuotes_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog();
                dialog.Filter = "Quotes QSH (*.Quotes.qsh)|*.Quotes.qsh|QSH files (*.qsh)|*.qsh|All files (*.*)|*.*";
                dialog.CheckFileExists = true;

                if (dialog.ShowDialog() == true)
                {
                    TextBoxQuotesPath.Text = dialog.FileName;
                    TryFillPairedPath(dialog.FileName, ".Quotes.qsh", ".Deals.qsh", TextBoxDealsPath);
                    SetDefaultOutputPath(dialog.FileName);
                }
            }
            catch (Exception error)
            {
                ShowError(error);
            }
        }

        private void ButtonBrowseOutput_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using (System.Windows.Forms.FolderBrowserDialog dialog = new System.Windows.Forms.FolderBrowserDialog())
                {
                    dialog.SelectedPath = Directory.Exists(TextBoxOutputPath.Text)
                        ? TextBoxOutputPath.Text
                        : string.Empty;

                    if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        TextBoxOutputPath.Text = dialog.SelectedPath;
                    }
                }
            }
            catch (Exception error)
            {
                ShowError(error);
            }
        }

        private void ButtonRun_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_analysisThread != null && _analysisThread.IsAlive)
                {
                    return;
                }

                _pendingRequest = BuildRequest();
                _completedResult = null;
                _workerError = null;
                _workerCancelled = false;
                _cancellation = new CancellationTokenSource();

                SetRunningState(true);
                TextBlockStatus.Text = OsLocalization.ConvertToLocString(
                    "Eng:Reading and replaying the QSH pair_Ru:Чтение и воспроизведение пары QSH_");

                _analysisThread = new Thread(ResearchThreadArea);
                _analysisThread.IsBackground = true;
                _analysisThread.Name = "OrderFlowResearch";
                _analysisThread.Start();
            }
            catch (Exception error)
            {
                SetRunningState(false);
                ShowError(error);
            }
        }

        private void ResearchThreadArea()
        {
            try
            {
                OrderFlowResearchRunner runner = new OrderFlowResearchRunner();
                _completedResult = runner.RunAndExport(_pendingRequest, _cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                _workerCancelled = true;
            }
            catch (Exception error)
            {
                _workerError = error;
                ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error);
            }

            if (_isClosing == false && Dispatcher.HasShutdownStarted == false)
            {
                try
                {
                    Dispatcher.BeginInvoke(new Action(ResearchCompletedOnUiThread));
                }
                catch (InvalidOperationException error)
                {
                    ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error);
                    DisposeCancellationAfterClose();
                }
            }
            else
            {
                DisposeCancellationAfterClose();
            }
        }

        private void DisposeCancellationAfterClose()
        {
            if (_cancellation != null)
            {
                _cancellation.Dispose();
                _cancellation = null;
            }

            _analysisThread = null;
        }

        private void ResearchCompletedOnUiThread()
        {
            if (_isClosing)
            {
                DisposeCancellationAfterClose();
                return;
            }

            try
            {
                SetRunningState(false);

                if (_workerCancelled)
                {
                    TextBlockStatus.Text = OsLocalization.ConvertToLocString(
                        "Eng:Research cancelled_Ru:Исследование отменено_");
                    return;
                }

                if (_workerError != null)
                {
                    ShowError(_workerError);
                    return;
                }

                ApplyResult(_completedResult);
            }
            catch (Exception error)
            {
                ShowError(error);
            }
            finally
            {
                if (_cancellation != null)
                {
                    _cancellation.Dispose();
                    _cancellation = null;
                }

                _analysisThread = null;
            }
        }

        private void ApplyResult(OrderFlowResearchResult result)
        {
            if (result == null)
            {
                return;
            }

            TextBoxSummary.Text = BuildSummary(result);
            List<OrderFlowCandidateView> candidateViews = OrderFlowCandidateView.Create(result);
            DataGridCandidates.ItemsSource = candidateViews;
            DataGridJournal.ItemsSource = result.Journal;
            _chart.SetResult(result);
            ButtonOpenArtifacts.IsEnabled = Directory.Exists(result.ArtifactDirectory);
            TabControlResults.SelectedItem = TabItemSummary;

            if (candidateViews.Count > 0)
            {
                DataGridCandidates.SelectedIndex = 0;
            }

            TextBlockStatus.Text = result.Quality.ResearchAccepted
                ? OsLocalization.ConvertToLocString("Eng:Research replay accepted_Ru:Исследовательский replay принят_")
                : OsLocalization.ConvertToLocString("Eng:Research replay rejected. Inspect quality reasons_Ru:Replay отклонен. Проверьте причины качества_");
        }

        private void ButtonCancel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_cancellation != null)
                {
                    _cancellation.Cancel();
                    TextBlockStatus.Text = OsLocalization.ConvertToLocString(
                        "Eng:Cancelling_Ru:Отмена выполняется_");
                }
            }
            catch (Exception error)
            {
                ShowError(error);
            }
        }

        private void ButtonOpenArtifacts_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_completedResult == null || Directory.Exists(_completedResult.ArtifactDirectory) == false)
                {
                    return;
                }

                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = _completedResult.ArtifactDirectory;
                startInfo.UseShellExecute = true;
                Process.Start(startInfo);
            }
            catch (Exception error)
            {
                ShowError(error);
            }
        }

        private void ComboBoxTimeFrame_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (_chart != null && ComboBoxTimeFrame.SelectedItem is OrderFlowDisplayTimeFrame)
                {
                    _chart.SetTimeFrame((OrderFlowDisplayTimeFrame)ComboBoxTimeFrame.SelectedItem);
                }
            }
            catch (Exception error)
            {
                ShowError(error);
            }
        }

        private void DataGridCandidates_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                OrderFlowCandidateView selected = DataGridCandidates.SelectedItem as OrderFlowCandidateView;
                if (selected == null)
                {
                    return;
                }

                _chart.SelectCandidate(selected.CandidateId);
                TextBlockCandidateDetails.Text = selected.Details;
            }
            catch (Exception error)
            {
                ShowError(error);
            }
        }

        private void DataGridCandidates_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (DataGridCandidates.SelectedItem != null)
                {
                    TabControlResults.SelectedItem = TabItemChart;
                }
            }
            catch (Exception error)
            {
                ShowError(error);
            }
        }

        private OrderFlowResearchRequest BuildRequest()
        {
            OrderFlowResearchRequest request = new OrderFlowResearchRequest();
            request.DealsFilePath = TextBoxDealsPath.Text.Trim();
            request.QuotesFilePath = TextBoxQuotesPath.Text.Trim();
            request.OutputRootPath = TextBoxOutputPath.Text.Trim();
            request.FeatureWindowSeconds = ParseInt(TextBoxWindowSeconds.Text, "Feature window");
            request.MinimumAbsoluteDelta = TextBoxMinimumDelta.Text.ToDecimal();
            request.MinimumPriceChangeTicks = ParseNonNegativeInt(TextBoxMinimumPriceTicks.Text, "Price ticks");
            request.TopBookLevels = ParseInt(TextBoxTopLevels.Text, "Book levels");
            request.MaximumBookAgeMilliseconds = ParseInt(TextBoxBookAge.Text, "Book age");
            request.CandidateCooldownMilliseconds = ParseNonNegativeInt(TextBoxCooldown.Text, "Cooldown");
            request.BackgroundSampleSeconds = ParseInt(TextBoxBackground.Text, "Background interval");
            request.LabelHorizonsSeconds = ParseHorizons(TextBoxHorizons.Text);
            request.TargetTicks = ParseInt(TextBoxTargetTicks.Text, "Target ticks");
            request.InvalidationTicks = ParseInt(TextBoxInvalidationTicks.Text, "Invalidation ticks");
            request.PriceStepOverride = string.IsNullOrWhiteSpace(TextBoxPriceStep.Text)
                ? 0
                : TextBoxPriceStep.Text.ToDecimal();
            request.VolumeStepOverride = string.IsNullOrWhiteSpace(TextBoxVolumeStep.Text)
                ? 0
                : TextBoxVolumeStep.Text.ToDecimal();
            request.Validate();
            return request;
        }

        private static int ParseInt(string value, string fieldName)
        {
            int parsed;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) == false || parsed <= 0)
            {
                throw new ArgumentException(fieldName + " must be a positive integer.");
            }

            return parsed;
        }

        private static int ParseNonNegativeInt(string value, string fieldName)
        {
            int parsed;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) == false || parsed < 0)
            {
                throw new ArgumentException(fieldName + " must be a non-negative integer.");
            }

            return parsed;
        }

        private static List<int> ParseHorizons(string value)
        {
            string[] parts = value.Replace(',', ';').Split(';', StringSplitOptions.RemoveEmptyEntries);
            List<int> horizons = new List<int>();

            for (int i = 0; i < parts.Length; i++)
            {
                horizons.Add(ParseInt(parts[i].Trim(), "Label horizon"));
            }

            return horizons;
        }

        private static void TryFillPairedPath(string selectedPath, string selectedSuffix,
            string pairedSuffix, TextBox target)
        {
            if (selectedPath.EndsWith(selectedSuffix, StringComparison.OrdinalIgnoreCase) == false)
            {
                return;
            }

            string pairedPath = selectedPath.Substring(0, selectedPath.Length - selectedSuffix.Length) + pairedSuffix;
            if (File.Exists(pairedPath))
            {
                target.Text = pairedPath;
            }
        }

        private void SetDefaultOutputPath(string selectedPath)
        {
            if (string.IsNullOrWhiteSpace(TextBoxOutputPath.Text))
            {
                string directory = Path.GetDirectoryName(selectedPath);
                if (string.IsNullOrWhiteSpace(directory) == false)
                {
                    TextBoxOutputPath.Text = Path.Combine(directory, "OrderFlowResearchResults");
                }
            }
        }

        private static string BuildSummary(OrderFlowResearchResult result)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine(result.Quality.ResearchAccepted ? "RESEARCH ACCEPTED" : "RESEARCH REJECTED");
            builder.AppendLine("This result contains no order, fill, execution PnL or profitability claim.");
            builder.AppendLine();
            builder.AppendLine("Deals: " + result.Quality.DealCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("Quotes: " + result.Quality.QuoteCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("Valid books: " + result.Quality.ValidBookCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("Deals range: " + FormatRange(result.Quality.FirstDealTime, result.Quality.LastDealTime));
            builder.AppendLine("Quotes range: " + FormatRange(result.Quality.FirstQuoteTime, result.Quality.LastQuoteTime));
            builder.AppendLine("Closed buckets: " + result.Quality.BucketCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("Observations: " + result.Observations.Count.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("Broad candidates: " + result.Candidates.Count.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("Market path labels: " + result.Labels.Count.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("Invalid deals: " + result.Quality.InvalidDealCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("Invalid books: " + result.Quality.InvalidBookCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("Missing book features: " + result.Quality.MissingBookFeatureCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("Stale book features: " + result.Quality.StaleBookFeatureCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine();
            builder.AppendLine("Input hash: " + result.InputHash);
            builder.AppendLine("ResearchSpec hash: " + result.ResearchSpecHash);
            builder.AppendLine("Normalized event hash: " + result.NormalizedEventHash);
            builder.AppendLine("Feature hash: " + result.FeatureHash);
            builder.AppendLine("Candidate hash: " + result.CandidateHash);
            builder.AppendLine("Artifacts: " + result.ArtifactDirectory);
            builder.AppendLine();
            builder.AppendLine("Quality reasons");

            for (int i = 0; i < result.Quality.Issues.Count; i++)
            {
                OrderFlowQualityIssue issue = result.Quality.Issues[i];
                builder.AppendLine((issue.IsRejection ? "REJECT" : "WARN") + " · " +
                    issue.ReasonCode + " · " + issue.Message);
            }

            return builder.ToString();
        }

        private static string FormatRange(DateTime? first, DateTime? last)
        {
            if (first.HasValue == false || last.HasValue == false)
            {
                return "n/a";
            }

            return first.Value.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) +
                " .. " + last.Value.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        }

        private void SetRunningState(bool isRunning)
        {
            ButtonRun.IsEnabled = isRunning == false;
            ButtonCancel.IsEnabled = isRunning;
            ButtonBrowseDeals.IsEnabled = isRunning == false;
            ButtonBrowseQuotes.IsEnabled = isRunning == false;
            ButtonBrowseOutput.IsEnabled = isRunning == false;
            TextBoxDealsPath.IsEnabled = isRunning == false;
            TextBoxQuotesPath.IsEnabled = isRunning == false;
            TextBoxOutputPath.IsEnabled = isRunning == false;
            TextBoxWindowSeconds.IsEnabled = isRunning == false;
            TextBoxMinimumDelta.IsEnabled = isRunning == false;
            TextBoxMinimumPriceTicks.IsEnabled = isRunning == false;
            TextBoxTopLevels.IsEnabled = isRunning == false;
            TextBoxBookAge.IsEnabled = isRunning == false;
            TextBoxCooldown.IsEnabled = isRunning == false;
            TextBoxBackground.IsEnabled = isRunning == false;
            TextBoxHorizons.IsEnabled = isRunning == false;
            TextBoxTargetTicks.IsEnabled = isRunning == false;
            TextBoxInvalidationTicks.IsEnabled = isRunning == false;
            TextBoxPriceStep.IsEnabled = isRunning == false;
            TextBoxVolumeStep.IsEnabled = isRunning == false;
        }

        private void ShowError(Exception error)
        {
            ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error);
            TextBlockStatus.Text = error.Message;
            CustomMessageBoxUi message = new CustomMessageBoxUi(OsLocalization.ConvertToLocString(
                "Eng:Order Flow research failed. See the log and status line._Ru:Ошибка исследования Order Flow. Проверьте лог и строку состояния._"));
            message.ShowDialog();
        }

        private void OrderFlowResearchUi_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                _isClosing = true;

                if (_cancellation != null)
                {
                    _cancellation.Cancel();
                }

                ButtonBrowseDeals.Click -= ButtonBrowseDeals_Click;
                ButtonBrowseQuotes.Click -= ButtonBrowseQuotes_Click;
                ButtonBrowseOutput.Click -= ButtonBrowseOutput_Click;
                ButtonRun.Click -= ButtonRun_Click;
                ButtonCancel.Click -= ButtonCancel_Click;
                ButtonOpenArtifacts.Click -= ButtonOpenArtifacts_Click;
                ComboBoxTimeFrame.SelectionChanged -= ComboBoxTimeFrame_SelectionChanged;
                DataGridCandidates.SelectionChanged -= DataGridCandidates_SelectionChanged;
                DataGridCandidates.MouseDoubleClick -= DataGridCandidates_MouseDoubleClick;
                Closing -= OrderFlowResearchUi_Closing;

                ContentControlChart.Content = null;
                _chart = null;
            }
            catch (Exception error)
            {
                ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error);
            }
        }
    }
}
