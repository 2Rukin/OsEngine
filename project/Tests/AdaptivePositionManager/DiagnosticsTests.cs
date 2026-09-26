using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using OsEngine.OsTrader.AdaptivePositionManager;

namespace OsEngine.AdaptivePositionManager.Tests
{
    /// <summary>Recorded preview causality and diagnostics toggles independent of GUI execution.</summary>
    internal static class DiagnosticsTests
    {
        internal static void Run()
        {
            ApmCampaign campaign = CoreTests.Enter();
            string recorded = campaign.ExportPreviewState();
            Program.Equal(101m, ApmCampaign.PreviewRecorded(recorded, ApmAction.Reduce).Value, "recorded preview agrees with known first reduction");
            Program.Equal(campaign.Preview(ApmAction.Add), ApmCampaign.PreviewRecorded(recorded, ApmAction.Add), "compact preview retains whole decision permissions");
            CoreTests.FillDecision(campaign, campaign.OnMarket(CoreTests.Market(104, 1)));
            Program.Equal(101m, ApmCampaign.PreviewRecorded(recorded, ApmAction.Reduce).Value, "future fills cannot repaint old preview");
            DataTests.Throws(() => ApmCampaign.Recover(recorded), "diagnostic projection cannot become execution recovery");
            string saved = campaign.ExportCheckpoint();
            for (int i = 0; i < 5; i++)
                ApmCampaign.PreviewRecorded(campaign.ExportPreviewState(), ApmAction.Add);
            Program.Equal(saved, campaign.ExportCheckpoint(), "diagnostic reads cannot change canonical trading state");
            string reference = null;
            foreach (bool detailed in new[] { false, true })
            {
                ImmediateGateway gateway = new ImmediateGateway();
                using ApmExecutionController controller = new ApmExecutionController(new ApmCampaign(CoreTests.Spec(), new ApmPolicy()), gateway, null);
                gateway.Controller = controller;
                controller.DetailedDiagnostics = detailed;
                decimal[] path = { 100, 102, 104, 102, 100, 104, 102, 110 };
                for (int i = 0; i < path.Length; i++) controller.Process(CoreTests.Market(path[i], i));
                string actual = JsonSerializer.Serialize(controller.Recent.Select(row => row with { PreviewState = null }));
                if (reference == null) reference = actual;
                else Program.Equal(reference, actual, "detailed diagnostics on/off canonical decision/fill trace parity");
                Program.Equal(detailed, controller.Recent.Any(row => row.PreviewState != null), "preview payload only with explicit detailed mode");
                Program.Check(controller.Recent.Where(row => row.Kind == "Fill").All(row => row.Slippage == 0), "actual fill price versus original decision price");
            }
            ImmediateGateway pausedGateway = new ImmediateGateway();
            using ApmExecutionController paused = new ApmExecutionController(new ApmCampaign(CoreTests.Spec(), new ApmPolicy()), pausedGateway, null);
            pausedGateway.Controller = paused;
            paused.DetailedDiagnostics = true;
            paused.Process(CoreTests.Market(100, 0));
            paused.Pause(true);
            ApmDiagnosticView view = paused.CaptureView();
            Program.Equal(ApmState.PausedNoIncrease, view.Snapshot.State, "atomic UI view includes latest operator pause");
            Program.Equal<decimal?>(null, ApmCampaign.PreviewRecorded(view.PreviewState, ApmAction.Add), "current preview cannot use rows recorded before pause");
            Program.Check(view.Metrics.EntryTime.HasValue, "diagnostic metrics retain actual first fill time");
            Program.Check(view.Rows.Any(row => row.Kind == "Fill" && row.Source == "callback"),
                "callback audit source is explicit");

            ApmCampaignSpec secondSpec = paused.Spec with { CampaignId = "second", EntrySignalId = "signal-two" };
            ApmCampaignSummaryRow first = ApmDiagnosticProjection.Campaign(paused.Spec, view.Snapshot, view.Metrics);
            ApmCampaignSummaryRow second = ApmDiagnosticProjection.Campaign(secondSpec,
                view.Snapshot with { CampaignId = "second" }, view.Metrics);
            Program.Equal("second", second.CampaignId, "run table projection keeps campaigns as separate typed rows");
            ApmReportSummaryRow report = ApmDiagnosticProjection.Report(paused.Spec, view.Snapshot, view.Metrics, paused.ExecutionModel);
            Program.Equal("NOT_RUN", report.Baseline, "report does not turn missing baseline into success");
            Program.Equal("NOT_RUN", report.OutOfSample, "report exposes missing OOS");
            Program.Equal("NOT_RUN", report.CostStress, "report exposes missing cost stress");
            string directory = Path.Combine(Path.GetTempPath(), "APM-diagnostics-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            ApmDiagnosticProjection.ExportCsv(Path.Combine(directory, "campaigns.csv"), new[] { first, second });
            ApmDiagnosticProjection.ExportCsv(Path.Combine(directory, "report.csv"), new[] { report });
            Program.Equal(3, File.ReadAllLines(Path.Combine(directory, "campaigns.csv")).Length,
                "campaign CSV has one row per campaign");
            Program.Check(File.ReadAllText(Path.Combine(directory, "report.csv")).Contains("NOT_RUN"),
                "report CSV preserves explicit research gates");
            string[] names = new[] { "Решения", "Заявки и исполнения", "Кампании", "Качество данных", "Отчёт" }
                .Select(ApmDiagnosticProjection.DefaultCsvName).ToArray();
            Program.Equal(5, names.Distinct().Count(), "every diagnostics table has a distinct default CSV name");
            ExitTimeUsesActualFlatteningFill();
            CompletedWithoutFillHasNoActualExitTime();
        }

        private static void ExitTimeUsesActualFlatteningFill()
        {
            TimestampGateway gateway = new TimestampGateway();
            using ApmExecutionController controller = new ApmExecutionController(
                new ApmCampaign(CoreTests.Spec(), new ApmPolicy()), gateway, null);
            gateway.Controller = controller;
            controller.Process(CoreTests.Market(100, 0), "tick");
            controller.Process(CoreTests.Market(104, 10), "tick");
            Program.Check(gateway.ReduceFill != null, "ordinary reduction exists before terminal close");
            controller.Close();
            Program.Check(gateway.ExitIntent != null, "manual exit creates one captured closing intent");
            controller.Process(CoreTests.Market(100, 20), "timer");
            DateTime actualFillTime = CoreTests.Start.AddSeconds(15);
            controller.ApplyFill(new ApmFill("actual-exit", gateway.ExitIntent.Id, actualFillTime,
                gateway.ExitIntent.PriceBound, gateway.ExitIntent.Volume, 0));
            Program.Equal(ApmState.Completed, controller.Snapshot.State,
                "actual full flattening fill completes the generic campaign");
            controller.ApplyOrder(gateway.ExitIntent.Id, ApmOrderState.Filled, gateway.ExitIntent.Volume, "exit-order");
            controller.Process(CoreTests.Market(100, 30), "timer");
            controller.ApplyFill(gateway.EntryFill);
            controller.ApplyFill(gateway.ReduceFill);
            ApmDiagnosticView view = controller.CaptureView();
            Program.Equal(actualFillTime, view.Metrics.ExitTime,
                "later order timer and duplicate old fills cannot overwrite actual flattening fill time");
            Program.Equal(actualFillTime,
                ApmDiagnosticProjection.Campaign(controller.Spec, view.Snapshot, view.Metrics).ExitTime,
                "campaign projection exposes the stable actual flattening fill time");
        }

        private static void CompletedWithoutFillHasNoActualExitTime()
        {
            NoFillGateway gateway = new NoFillGateway();
            using ApmExecutionController controller = new ApmExecutionController(
                new ApmCampaign(CoreTests.Spec(), new ApmPolicy()), gateway, null);
            gateway.Controller = controller;
            controller.Process(CoreTests.Market(100, 0), "tick");
            controller.Close();
            ApmDiagnosticView view = controller.CaptureView();
            Program.Equal(ApmState.Completed, view.Snapshot.State, "canceled unfilled entry can complete flat");
            Program.Equal<DateTime?>(null, view.Metrics.ExitTime, "campaign without fills has no invented actual exit time");
            Program.Equal<DateTime?>(null,
                ApmDiagnosticProjection.Campaign(controller.Spec, view.Snapshot, view.Metrics).ExitTime,
                "campaign projection keeps no-fill exit time unobserved");
        }

        private sealed class ImmediateGateway : IApmOrderGateway
        {
            internal ApmExecutionController Controller;
            public void Send(ApmIntent intent)
            {
                Controller.ApplyFill(new ApmFill("actual-" + intent.Id, intent.Id, intent.Time, intent.PriceBound, intent.Volume, 0));
                Controller.ApplyOrder(intent.Id, ApmOrderState.Filled, intent.Volume, "native-" + intent.Id);
            }
            public void Cancel(ApmIntent intent) => Controller.ApplyOrder(intent.Id, ApmOrderState.Canceled, intent.Filled, "native-" + intent.Id);
        }

        private sealed class TimestampGateway : IApmOrderGateway
        {
            internal ApmExecutionController Controller;
            internal ApmIntent ExitIntent;
            internal ApmFill EntryFill;
            internal ApmFill ReduceFill;

            public void Send(ApmIntent intent)
            {
                if (intent.Action != ApmAction.Exit)
                {
                    ApmFill fill = new ApmFill("fill-" + intent.Id, intent.Id, intent.Time,
                        intent.PriceBound, intent.Volume, 0);
                    if (intent.Action == ApmAction.InitialEntry) EntryFill = fill;
                    else if (intent.Action == ApmAction.Reduce) ReduceFill = fill;
                    Controller.ApplyFill(fill);
                    Controller.ApplyOrder(intent.Id, ApmOrderState.Filled, intent.Volume, "entry-order");
                }
                else
                {
                    ExitIntent = intent;
                    Controller.ApplyOrder(intent.Id, ApmOrderState.Working, 0, "exit-order");
                }
            }

            public void Cancel(ApmIntent intent) => Controller.ApplyOrder(intent.Id,
                ApmOrderState.Canceled, intent.Filled, "cancel-order");
        }

        private sealed class NoFillGateway : IApmOrderGateway
        {
            internal ApmExecutionController Controller;
            public void Send(ApmIntent intent) { }
            public void Cancel(ApmIntent intent) => Controller.ApplyOrder(intent.Id,
                ApmOrderState.Canceled, intent.Filled, "cancel-unfilled");
        }
    }
}
