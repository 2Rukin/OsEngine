using System;
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
    }
}
