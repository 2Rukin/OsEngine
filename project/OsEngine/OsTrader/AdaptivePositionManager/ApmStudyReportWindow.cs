using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OsEngine.Logging;
using OsEngine.Market;

namespace OsEngine.OsTrader.AdaptivePositionManager
{
    /// <summary>Typed per-trial display; complete observed metrics include an open campaign even in Partial runs. Missing evidence remains null.</summary>
    public sealed record ApmStudyReportRow(string Phase, string Status, string PolicyHash, string MetricsStatus,
        decimal AddScale, decimal ReduceScale, decimal RearmVolatilityFactor,
        decimal InventoryGamma, bool FixedScales, bool Fast, decimal? Net, decimal? WorstCampaign,
        decimal? MaximumDrawdown, decimal? AverageVolume, decimal? MaximumVolume, decimal? Turnover, decimal? Fees);

    /// <summary>Ownerless read-only experiment table with numeric sorting, status filter and CSV export. Never creates optimizer passes.</summary>
    public sealed class ApmStudyReportWindow : Window
    {
        private readonly DataGrid _grid = new DataGrid { IsReadOnly = true, AutoGenerateColumns = true,
            CanUserAddRows = false, EnableRowVirtualization = true };
        private readonly TextBox _filter = new TextBox { Width = 200, Margin = new Thickness(5) };
        private readonly Button _export = new Button { Content = "Экспорт CSV", Margin = new Thickness(5), Padding = new Thickness(8) };
        private readonly ApmStudyReportRow[] _rows;

        /// <summary>Open a declared all-trials artifact (maximum 32 MiB); no guessed path or live account access.</summary>
        public ApmStudyReportWindow(string path)
        {
            _rows = Read(path);
            Title = "APM — эксперимент ResearchOnly"; Width = 1100; Height = 600;
            Width = Math.Min(Width, SystemParameters.WorkArea.Width); Height = Math.Min(Height, SystemParameters.WorkArea.Height);
            DockPanel root = new DockPanel { Margin = new Thickness(8) };
            TextBlock notice = new TextBlock { Text = "ResearchOnly · Net — вся кампания с открытой equity. Пусто — нет evidence. "
                + "Native fills не моделируют очередь. Исторический untouched test и economic GO не подтверждены.", TextWrapping = TextWrapping.Wrap };
            DockPanel.SetDock(notice, Dock.Top); root.Children.Add(notice);
            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(new TextBlock { Text = "Фаза / статус", VerticalAlignment = VerticalAlignment.Center });
            buttons.Children.Add(_filter); buttons.Children.Add(_export);
            DockPanel.SetDock(buttons, Dock.Top); root.Children.Add(buttons); root.Children.Add(_grid);
            Content = root; _grid.ItemsSource = _rows;
            _filter.TextChanged += Filter_Changed; _export.Click += Export_Click; Closed += Window_Closed;
        }

        /// <summary>Parse current all-trials rows; duplicate/absent runs deliberately have no synthesized metrics.</summary>
        public static ApmStudyReportRow[] Read(string path)
        {
            if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidDataException("Study report exceeds 32 MiB.");
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.GetProperty("SchemaVersion").GetString() != "APM-AllTrials-v1") throw new InvalidDataException("Unknown study schema.");
            List<ApmStudyReportRow> rows = new List<ApmStudyReportRow>();
            foreach (JsonElement trial in document.RootElement.GetProperty("Results").EnumerateArray())
            {
                ApmPolicy policy = trial.GetProperty("Policy").Deserialize<ApmPolicy>();
                JsonElement runs = trial.GetProperty("Runs");
                ApmCampaignMetrics[] metrics = null;
                string metricsStatus = "Unavailable";
                if (runs.ValueKind == JsonValueKind.Array && runs.GetArrayLength() == 1
                    && runs[0].TryGetProperty("Metrics", out JsonElement values))
                {
                    metrics = values.Deserialize<ApmCampaignMetrics[]>();
                    ApmSnapshot[] campaigns = runs[0].GetProperty("Campaigns").Deserialize<ApmSnapshot[]>();
                    ApmSnapshot current = runs[0].GetProperty("Current").Deserialize<ApmSnapshot>();
                    if (metrics == null || campaigns == null || metrics.Length != campaigns.Length
                        || metrics.Length != runs[0].GetProperty("Completed").GetInt32()) metrics = null;
                    else
                    {
                        metricsStatus = "ObservedCompletedCampaigns";
                        if (current != null && !campaigns.Any(c => c.CampaignId == current.CampaignId))
                        {
                            if (runs[0].TryGetProperty("CurrentMetrics", out JsonElement open) && open.ValueKind == JsonValueKind.Object)
                            {
                                metrics = metrics.Append(open.Deserialize<ApmCampaignMetrics>()).ToArray();
                                metricsStatus = "IncludesOpenCampaign";
                            }
                            else { metrics = null; metricsStatus = "IncompleteOpenMetrics"; }
                        }
                    }
                }
                bool known = metrics?.Length > 0;
                decimal duration = known ? metrics.Sum(m => m.ObservedSeconds) : 0;
                rows.Add(new ApmStudyReportRow(trial.GetProperty("Phase").GetString(), trial.GetProperty("Status").GetString(),
                    trial.GetProperty("PolicyHash").GetString(), metricsStatus,
                    policy.AddScale, policy.ReduceScale, policy.RearmVolatilityFactor, policy.InventoryPenalty, policy.FixedScales, policy.FastEnabled,
                    known ? metrics.Sum(m => m.NetEquity) : null, known ? metrics.Min(m => m.NetEquity) : null,
                    known ? metrics.Max(m => m.MaximumDrawdown) : null,
                    known && duration > 0 ? metrics.Sum(m => m.AverageVolume * m.ObservedSeconds) / duration : null,
                    known ? metrics.Max(m => m.MaximumVolume) : null, known ? metrics.Sum(m => m.Turnover) : null,
                    known ? metrics.Sum(m => m.Fees) : null));
            }
            return rows.ToArray();
        }

        private void Filter_Changed(object sender, TextChangedEventArgs args) => _grid.ItemsSource = _rows.Where(r =>
            r.Phase.Contains(_filter.Text, StringComparison.OrdinalIgnoreCase) || r.Status.Contains(_filter.Text, StringComparison.OrdinalIgnoreCase)).ToArray();

        private void Export_Click(object sender, RoutedEventArgs args)
        {
            try
            {
                SaveFileDialog dialog = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "apm-all-trials.csv" };
                if (dialog.ShowDialog() != true) return;
                ExportCsv(dialog.FileName, (IEnumerable<ApmStudyReportRow>)_grid.ItemsSource);
            }
            catch (Exception error) { ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error); }
        }

        /// <summary>Export the exact selected trial identities, behavior values and complete observed metrics in invariant numeric units.</summary>
        public static void ExportCsv(string path, IEnumerable<ApmStudyReportRow> rows)
        {
            using StreamWriter writer = new StreamWriter(path);
            writer.WriteLine("Phase,Status,PolicyHash,MetricsStatus,AddScale,ReduceScale,RearmVolatilityFactor,InventoryGamma,FixedScales,Fast,Net,WorstCampaign,MaximumCampaignDrawdown,AverageVolume,MaximumVolume,Turnover,Fees");
            foreach (ApmStudyReportRow row in rows)
            {
                object[] cells = { row.Phase, row.Status, row.PolicyHash, row.MetricsStatus, row.AddScale, row.ReduceScale,
                    row.RearmVolatilityFactor, row.InventoryGamma, row.FixedScales, row.Fast, row.Net, row.WorstCampaign,
                    row.MaximumDrawdown, row.AverageVolume, row.MaximumVolume, row.Turnover, row.Fees };
                writer.WriteLine(string.Join(",", cells.Select(Cell)));
            }
        }

        private static string Cell(object value)
        {
            string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            if (value is string && text.Length > 0 && "=+-@".Contains(text[0])) text = "'" + text;
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }

        private void Window_Closed(object sender, EventArgs args)
        { _filter.TextChanged -= Filter_Changed; _export.Click -= Export_Click; Closed -= Window_Closed; _grid.ItemsSource = null; }
    }
}
