/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace OsEngine.OsTrader.AdaptivePositionManager
{
    /// <summary>One typed campaign summary row; it is a detached diagnostic projection and never an execution input.</summary>
    public sealed record ApmCampaignSummaryRow(string CampaignId, ApmDirection Direction,
        DateTime? EntryTime, DateTime? ExitTime, string EntryReason, string ExitReason,
        decimal NetResult, decimal MaximumDrawdown, decimal MaximumVolume, decimal Turnover, ApmState State);

    /// <summary>ResearchOnly campaign report with explicit assumptions and unexecuted research gates.</summary>
    public sealed record ApmReportSummaryRow(string CampaignId, ApmState State, decimal CampaignResult,
        decimal MaximumDrawdown, decimal Turnover, decimal Fees, decimal FeePerContract,
        string CostAssumptions, ApmDataProfile Profile, string ExecutionModel, string ResearchStatus,
        string Baseline, string OutOfSample, string CostStress, string OpenGates);

    /// <summary>Detached run-level table data. Completed and active campaigns remain separate rows.</summary>
    public sealed record ApmRunDiagnosticView(ApmCampaignSummaryRow[] Campaigns, ApmReportSummaryRow[] Report);

    /// <summary>Build and export typed run-level diagnostic projections without changing trading state.</summary>
    public static class ApmDiagnosticProjection
    {
        /// <summary>Build the single-campaign fallback used by component diagnostics and UI smoke tests.</summary>
        public static ApmRunDiagnosticView Capture(ApmExecutionController controller)
        {
            if (controller == null) throw new ArgumentNullException(nameof(controller));
            ApmDiagnosticView view = controller.CaptureView();
            ApmCampaignSummaryRow campaign = Campaign(controller.Spec, view.Snapshot, view.Metrics);
            return new ApmRunDiagnosticView(new[] { campaign }, new[] { Report(controller.Spec, view.Snapshot, view.Metrics,
                controller.ExecutionModel) });
        }

        /// <summary>Project one active or completed campaign from its immutable locks and accumulated metrics.</summary>
        public static ApmCampaignSummaryRow Campaign(ApmCampaignSpec spec, ApmSnapshot snapshot, ApmCampaignMetrics metrics)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            metrics ??= new ApmCampaignMetrics(snapshot.Equity, snapshot.MaximumDrawdown, snapshot.Fees,
                snapshot.Turnover, snapshot.MaximumVolume, 0, 0, 0);
            string entryReason = spec.EntrySignalId + (string.IsNullOrWhiteSpace(metrics.EntryReason)
                ? "" : " / " + metrics.EntryReason);
            return new ApmCampaignSummaryRow(spec.CampaignId, spec.Direction, metrics.EntryTime, metrics.ExitTime, entryReason,
                string.IsNullOrWhiteSpace(metrics.ExitReason) ? snapshot.ExitReason : metrics.ExitReason,
                snapshot.Equity, snapshot.MaximumDrawdown, snapshot.MaximumVolume, snapshot.Turnover, snapshot.State);
        }

        /// <summary>Project one explicit ResearchOnly result row; absent qualification is shown as NOT_RUN.</summary>
        public static ApmReportSummaryRow Report(ApmCampaignSpec spec, ApmSnapshot snapshot,
            ApmCampaignMetrics metrics, string executionModel)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            metrics ??= new ApmCampaignMetrics(snapshot.Equity, snapshot.MaximumDrawdown, snapshot.Fees,
                snapshot.Turnover, snapshot.MaximumVolume, 0, 0, 0);
            string assumptions = "fee/contract=" + spec.FeePerContract.ToString(CultureInfo.InvariantCulture)
                + "; entry reserve ticks=" + spec.EntrySlippageReserveTicks.ToString(CultureInfo.InvariantCulture)
                + "; stop reserve ticks=" + spec.StopSlippageReserveTicks.ToString(CultureInfo.InvariantCulture);
            return new ApmReportSummaryRow(spec.CampaignId, snapshot.State, metrics.NetEquity,
                metrics.MaximumDrawdown, metrics.Turnover, metrics.Fees, spec.FeePerContract,
                assumptions, ApmDataProfile.TradeOnly, executionModel ?? "", "ResearchOnly",
                "NOT_RUN", "NOT_RUN", "NOT_RUN",
                "QG06 physical DPI NOT_RUN; historical-data/OOS NOT_RUN; book/recovery NOT_RUN");
        }

        /// <summary>Stable default CSV name for each diagnostics window.</summary>
        public static string DefaultCsvName(string kind)
        {
            return kind switch
            {
                "Решения" => "apm-decisions.csv",
                "Заявки и исполнения" => "apm-orders-fills.csv",
                "Кампании" => "apm-campaigns.csv",
                "Качество данных" => "apm-data-quality.csv",
                "Отчёт" => "apm-report.csv",
                _ => "apm-diagnostics.csv"
            };
        }

        /// <summary>Export campaign summaries with stable typed columns.</summary>
        public static void ExportCsv(string path, IEnumerable<ApmCampaignSummaryRow> rows)
        {
            using StreamWriter writer = Open(path);
            Write(writer, new object[] { "CampaignId", "Direction", "EntryTime", "ExitTime", "EntryReason",
                "ExitReason", "NetResult", "MaximumDrawdown", "MaximumVolume", "Turnover", "State" });
            foreach (ApmCampaignSummaryRow row in rows)
                Write(writer, new object[] { row.CampaignId, row.Direction, Format(row.EntryTime), Format(row.ExitTime),
                    row.EntryReason, row.ExitReason, row.NetResult, row.MaximumDrawdown, row.MaximumVolume,
                    row.Turnover, row.State });
        }

        /// <summary>Export ResearchOnly reports including assumptions and explicit NOT_RUN gates.</summary>
        public static void ExportCsv(string path, IEnumerable<ApmReportSummaryRow> rows)
        {
            using StreamWriter writer = Open(path);
            Write(writer, new object[] { "CampaignId", "State", "CampaignResult", "MaximumDrawdown", "Turnover",
                "Fees", "FeePerContract", "CostAssumptions", "Profile", "ExecutionModel", "ResearchStatus",
                "Baseline", "OutOfSample", "CostStress", "OpenGates" });
            foreach (ApmReportSummaryRow row in rows)
                Write(writer, new object[] { row.CampaignId, row.State, row.CampaignResult, row.MaximumDrawdown,
                    row.Turnover, row.Fees, row.FeePerContract, row.CostAssumptions, row.Profile, row.ExecutionModel,
                    row.ResearchStatus, row.Baseline, row.OutOfSample, row.CostStress, row.OpenGates });
        }

        private static StreamWriter Open(string path) => new StreamWriter(path, false, new UTF8Encoding(true));
        private static string Format(DateTime? value) => value.HasValue ? value.Value.ToString("O") : "";

        private static void Write(StreamWriter writer, object[] fields)
        {
            for (int i = 0; i < fields.Length; i++)
            {
                if (i > 0) writer.Write(',');
                string field = Convert.ToString(fields[i], CultureInfo.InvariantCulture) ?? "";
                writer.Write('"'); writer.Write(field.Replace("\"", "\"\"")); writer.Write('"');
            }
            writer.WriteLine();
        }
    }
}
