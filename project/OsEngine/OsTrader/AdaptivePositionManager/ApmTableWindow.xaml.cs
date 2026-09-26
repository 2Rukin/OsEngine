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
    /// <summary>Independent typed audit table. Sorting/filter/export never mutate canonical event order or the controller.</summary>
    public partial class ApmTableWindow : Window
    {
        private readonly ApmExecutionController _controller;
        private readonly string _kind;
        private ApmAuditRow[] _filtered = Array.Empty<ApmAuditRow>();

        /// <summary>Selected stored snapshot for a diagnostic chart; never a trading command.</summary>
        public event Action<ApmAuditRow> SelectedEvent;

        /// <summary>Create an ownerless nonmodal window on the dispatcher; read only bounded audit copies.</summary>
        public ApmTableWindow(ApmExecutionController controller, string kind)
        {
            InitializeComponent();
            _controller = controller; _kind = kind; Title = "APM — " + kind;
            Width = Math.Min(Width, SystemParameters.WorkArea.Width);
            Height = Math.Min(Height, SystemParameters.WorkArea.Height);
            TextBlockContext.Text = "ResearchOnly · " + _controller.ExecutionModel + " · последние 2000 audit rows. Полный журнал — events.jsonl. "
                + "NativeTester не моделирует очередь и частичную ликвидность. Live и экономическое преимущество не квалифицированы.";
            ButtonRefresh.Click += ButtonRefresh_Click;
            ButtonSelect.Click += ButtonSelect_Click;
            ButtonExport.Click += ButtonExport_Click;
            Closed += Window_Closed;
            try { Refresh(); } catch (Exception error) { Log(error); }
        }

        private void Refresh()
        {
            IEnumerable<ApmAuditRow> rows = _controller.Recent;
            if (_kind == "Решения") rows = rows.Where(r => r.Kind == "Decision");
            else if (_kind == "Заявки и исполнения") rows = rows.Where(r => r.Kind != "Decision");
            else if (_kind == "Кампании" || _kind == "Отчёт") rows = rows.TakeLast(1);
            else if (_kind == "Качество данных") rows = rows.Where(r => r.Kind == "Fault"
                || (r.Kind == "Decision" && (r.Snapshot?.Market?.Ready != true || r.Snapshot.Market.Quality != "OK")));
            rows = rows.Where(r => Matches(r.CampaignId, TextBoxCampaign.Text) && Matches(r.Action, TextBoxAction.Text)
                && Matches(r.Reasons, TextBoxReason.Text));
            if (!string.IsNullOrWhiteSpace(TextBoxFrom.Text))
            {
                DateTime from = DateTime.ParseExact(TextBoxFrom.Text.Trim(), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                rows = rows.Where(r => r.Time >= from);
            }
            if (!string.IsNullOrWhiteSpace(TextBoxTo.Text))
            {
                DateTime to = DateTime.ParseExact(TextBoxTo.Text.Trim(), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                rows = rows.Where(r => r.Time <= to);
            }
            _filtered = rows.ToArray();
            DataGridRows.ItemsSource = _filtered;
            TextBlockCount.Text = "Строк " + _filtered.Length;
            if (_kind == "Отчёт") TextBlockContext.Text = "ResearchOnly · текущая кампания: net equity включает открытый остаток. "
                + "Baseline comparison / OOS / cost stress: NOT_RUN. Полный журнал: events.jsonl. Gate evidence — implementation.md. "
                + "Начало сценария " + _controller.Spec.EntryTime.ToString("O") + "; cutoff " + _controller.Spec.SessionExitTime.ToString("O");
            if (_kind == "Качество данных") TextBlockContext.Text = "TradeOnly · исходный порядок равных секунд; MicroSeconds — отдельные метаданные. "
                + "Уникальность native trade ID не сертифицирована; агрессор Unknown не превращается в Sell. "
                + "Показаны recorded readiness/quality; полный файл проверяется ApmDatasetValidation отдельно.";
        }

        private static bool Matches(string text, string filter) => string.IsNullOrWhiteSpace(filter)
            || (text ?? "").Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase);

        private void ButtonRefresh_Click(object sender, RoutedEventArgs args) { try { Refresh(); } catch (Exception error) { Log(error); } }
        private void ButtonSelect_Click(object sender, RoutedEventArgs args)
        {
            try { if (DataGridRows.SelectedItem is ApmAuditRow row) SelectedEvent?.Invoke(row); }
            catch (Exception error) { Log(error); }
        }
        private void ButtonExport_Click(object sender, RoutedEventArgs args)
        {
            try
            {
                SaveFileDialog dialog = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "apm-audit.csv" };
                if (dialog.ShowDialog() == true) ApmArtifacts.ExportCsv(dialog.FileName, _filtered);
            }
            catch (Exception error) { Log(error); }
        }
        private void Window_Closed(object sender, EventArgs args)
        {
            try
            {
                ButtonRefresh.Click -= ButtonRefresh_Click; ButtonSelect.Click -= ButtonSelect_Click;
                ButtonExport.Click -= ButtonExport_Click; Closed -= Window_Closed;
                DataGridRows.ItemsSource = null; _filtered = Array.Empty<ApmAuditRow>();
                SelectedEvent = null;
            }
            catch (Exception error) { Log(error); }
        }
        private static void Log(Exception error) => ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error);
    }
}
