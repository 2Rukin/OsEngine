using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using OsEngine.Logging;
using OsEngine.Market;

namespace OsEngine.OsTrader.AdaptivePositionManager
{
    /// <summary>Independent typed audit or run-summary table. Sorting/filter/export never mutate canonical state.</summary>
    public partial class ApmTableWindow : Window
    {
        private readonly ApmExecutionController _controller;
        private readonly string _kind;
        private readonly Func<ApmRunDiagnosticView> _runDiagnostics;
        private ApmAuditRow[] _filteredAudit = Array.Empty<ApmAuditRow>();
        private ApmCampaignSummaryRow[] _filteredCampaigns = Array.Empty<ApmCampaignSummaryRow>();
        private ApmReportSummaryRow[] _filteredReport = Array.Empty<ApmReportSummaryRow>();

        /// <summary>Selected stored snapshot for a diagnostic chart; never a trading command.</summary>
        public event Action<ApmAuditRow> SelectedEvent;

        /// <summary>Create an ownerless nonmodal window for one current campaign.</summary>
        public ApmTableWindow(ApmExecutionController controller, string kind)
            : this(controller, kind, () => ApmDiagnosticProjection.Capture(controller)) { }

        /// <summary>Create an ownerless nonmodal window with completed and active run-level projections.</summary>
        public ApmTableWindow(ApmExecutionController controller, string kind,
            Func<ApmRunDiagnosticView> runDiagnostics)
        {
            InitializeComponent();
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _kind = kind ?? throw new ArgumentNullException(nameof(kind));
            _runDiagnostics = runDiagnostics ?? throw new ArgumentNullException(nameof(runDiagnostics));
            Title = "APM — " + kind;
            Width = Math.Min(Width, SystemParameters.WorkArea.Width);
            Height = Math.Min(Height, SystemParameters.WorkArea.Height);
            TextBlockContext.Text = "ResearchOnly · " + _controller.ExecutionModel
                + " · последние 2000 audit rows. Полный canonical журнал — events.jsonl. "
                + "Source различает tick/timer/callback. NativeTester не моделирует очередь и частичную ликвидность.";
            if (_kind == "Кампании" || _kind == "Отчёт")
            {
                DataGridRows.Columns.Clear();
                DataGridRows.AutoGenerateColumns = true;
                ButtonSelect.IsEnabled = false;
            }
            if (_kind == "Отчёт") { TextBoxFrom.IsEnabled = false; TextBoxTo.IsEnabled = false; }
            ButtonRefresh.Click += ButtonRefresh_Click;
            ButtonSelect.Click += ButtonSelect_Click;
            ButtonExport.Click += ButtonExport_Click;
            Closed += Window_Closed;
            try { Refresh(); } catch (Exception error) { Log(error); }
        }

        private void Refresh()
        {
            if (_kind == "Кампании") RefreshCampaigns();
            else if (_kind == "Отчёт") RefreshReport();
            else RefreshAudit();
        }

        private void RefreshAudit()
        {
            IEnumerable<ApmAuditRow> rows = _controller.Recent;
            if (_kind == "Решения") rows = rows.Where(row => row.Kind == "Decision");
            else if (_kind == "Заявки и исполнения") rows = rows.Where(row => row.Kind != "Decision");
            else if (_kind == "Качество данных") rows = rows.Where(row => row.Kind == "Fault"
                || (row.Kind == "Decision" && (row.Snapshot?.Market?.Ready != true || row.Snapshot.Market.Quality != "OK")));
            rows = rows.Where(row => Matches(row.CampaignId, TextBoxCampaign.Text)
                && Matches(row.Action, TextBoxAction.Text) && Matches(row.Reasons, TextBoxReason.Text));
            DateTime? from = ReadTime(TextBoxFrom.Text);
            DateTime? to = ReadTime(TextBoxTo.Text);
            if (from.HasValue) rows = rows.Where(row => row.Time >= from.Value);
            if (to.HasValue) rows = rows.Where(row => row.Time <= to.Value);
            _filteredAudit = rows.ToArray();
            _filteredCampaigns = Array.Empty<ApmCampaignSummaryRow>();
            _filteredReport = Array.Empty<ApmReportSummaryRow>();
            DataGridRows.ItemsSource = _filteredAudit;
            TextBlockCount.Text = "Строк " + _filteredAudit.Length;
            if (_kind == "Качество данных") TextBlockContext.Text = "TradeOnly · исходный порядок равных секунд; MicroSeconds — отдельные метаданные. "
                + "Уникальность native trade ID не сертифицирована; агрессор Unknown не превращается в Sell. "
                + "Source сохраняет tick/timer/callback; canonical events.jsonl не фильтруется.";
        }

        private void RefreshCampaigns()
        {
            IEnumerable<ApmCampaignSummaryRow> rows = _runDiagnostics().Campaigns;
            rows = rows.Where(row => Matches(row.CampaignId, TextBoxCampaign.Text)
                && (Matches(row.Direction.ToString(), TextBoxAction.Text) || Matches(row.State.ToString(), TextBoxAction.Text))
                && (Matches(row.EntryReason, TextBoxReason.Text) || Matches(row.ExitReason, TextBoxReason.Text)));
            DateTime? from = ReadTime(TextBoxFrom.Text);
            DateTime? to = ReadTime(TextBoxTo.Text);
            if (from.HasValue) rows = rows.Where(row => row.EntryTime.HasValue && row.EntryTime.Value >= from.Value);
            if (to.HasValue) rows = rows.Where(row => (row.ExitTime ?? row.EntryTime).HasValue
                && (row.ExitTime ?? row.EntryTime).Value <= to.Value);
            _filteredCampaigns = rows.ToArray();
            _filteredAudit = Array.Empty<ApmAuditRow>();
            _filteredReport = Array.Empty<ApmReportSummaryRow>();
            DataGridRows.ItemsSource = _filteredCampaigns;
            TextBlockCount.Text = "Кампаний " + _filteredCampaigns.Length;
            TextBlockContext.Text = "Одна строка — одна завершённая или активная кампания текущего ResearchOnly replay. "
                + "Entry/exit — фактические fill times; пустой exit означает активную кампанию или завершение без наблюдавшегося closing fill.";
        }

        private void RefreshReport()
        {
            IEnumerable<ApmReportSummaryRow> rows = _runDiagnostics().Report;
            rows = rows.Where(row => Matches(row.CampaignId, TextBoxCampaign.Text)
                && (Matches(row.State.ToString(), TextBoxAction.Text) || Matches(row.ExecutionModel, TextBoxAction.Text))
                && (Matches(row.CostAssumptions, TextBoxReason.Text) || Matches(row.OpenGates, TextBoxReason.Text)));
            _filteredReport = rows.ToArray();
            _filteredAudit = Array.Empty<ApmAuditRow>();
            _filteredCampaigns = Array.Empty<ApmCampaignSummaryRow>();
            DataGridRows.ItemsSource = _filteredReport;
            TextBlockCount.Text = "Отчётов " + _filteredReport.Length;
            TextBlockContext.Text = "ResearchOnly · campaign result включает открытый остаток текущей кампании. "
                + "Комиссии и slippage reserves показаны явно. Baseline / OOS / cost stress остаются NOT_RUN, "
                + "пока их нет в этом run evidence; это не live или profitability qualification.";
        }

        private static DateTime? ReadTime(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            return DateTime.ParseExact(text.Trim(), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private static bool Matches(string text, string filter) => string.IsNullOrWhiteSpace(filter)
            || (text ?? "").Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase);

        private void ButtonRefresh_Click(object sender, RoutedEventArgs args)
        { try { Refresh(); } catch (Exception error) { Log(error); } }

        private void ButtonSelect_Click(object sender, RoutedEventArgs args)
        {
            try { if (DataGridRows.SelectedItem is ApmAuditRow row) SelectedEvent?.Invoke(row); }
            catch (Exception error) { Log(error); }
        }

        private void ButtonExport_Click(object sender, RoutedEventArgs args)
        {
            try
            {
                SaveFileDialog dialog = new SaveFileDialog
                    { Filter = "CSV (*.csv)|*.csv", FileName = ApmDiagnosticProjection.DefaultCsvName(_kind) };
                if (dialog.ShowDialog() != true) return;
                if (_kind == "Кампании") ApmDiagnosticProjection.ExportCsv(dialog.FileName, _filteredCampaigns);
                else if (_kind == "Отчёт") ApmDiagnosticProjection.ExportCsv(dialog.FileName, _filteredReport);
                else ApmArtifacts.ExportCsv(dialog.FileName, _filteredAudit);
            }
            catch (Exception error) { Log(error); }
        }

        private void Window_Closed(object sender, EventArgs args)
        {
            try
            {
                ButtonRefresh.Click -= ButtonRefresh_Click; ButtonSelect.Click -= ButtonSelect_Click;
                ButtonExport.Click -= ButtonExport_Click; Closed -= Window_Closed;
                DataGridRows.ItemsSource = null;
                _filteredAudit = Array.Empty<ApmAuditRow>();
                _filteredCampaigns = Array.Empty<ApmCampaignSummaryRow>();
                _filteredReport = Array.Empty<ApmReportSummaryRow>();
                SelectedEvent = null;
            }
            catch (Exception error) { Log(error); }
        }

        private static void Log(Exception error) => ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error);
    }
}
