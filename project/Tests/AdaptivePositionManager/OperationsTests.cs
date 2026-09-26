using System;
using System.Collections.Generic;
using System.Linq;
using OsEngine.OsTrader.AdaptivePositionManager;

namespace OsEngine.AdaptivePositionManager.Tests
{
    /// <summary>Finite native lifetime budgets, late-fill identity, repeated recovery and separate monotonic replay-health checks.</summary>
    internal static class OperationsTests
    {
        internal static void Run()
        {
            ApmCampaign campaign = new ApmCampaign(CoreTests.Spec(), new ApmPolicy(), new ApmOperationalLimits(2, 3));
            CoreTests.FillDecision(campaign, campaign.OnMarket(CoreTests.Market(100, 0)));
            CoreTests.FillDecision(campaign, campaign.OnMarket(CoreTests.Market(104, 1)));
            ApmDecision close = campaign.OnMarket(CoreTests.Market(102, 2));
            Program.Equal(ApmAction.Exit, close.Action, "ordinary budget preserves protective exit reserve");
            Program.Equal("RESOURCE_LIMIT", campaign.Snapshot.ExitReason, "resource exhaustion latches exit");
            ApmIntent exit = campaign.Reserve(close);
            campaign.ApplyOrder(exit.Id, ApmOrderState.Canceled, 0);
            for (int i = 3; i <= 4; i++)
            {
                ApmIntent retry = campaign.Reserve(campaign.OnMarket(CoreTests.Market(102, i)));
                campaign.ApplyOrder(retry.Id, ApmOrderState.Canceled, 0);
            }
            ApmDecision exhausted = campaign.OnMarket(CoreTests.Market(102, 5));
            Program.Equal(ApmAction.Query, exhausted.Action, "finite zero-fill cancel retry budget");
            Program.Equal(ApmState.FaultedClosing, campaign.Snapshot.State, "retry exhaustion does not fabricate flat");
            Program.Equal(6m, campaign.Snapshot.FilledVolume, "retry exhaustion preserves open exposure");
            Program.Equal(5, campaign.Intents.Length, "ordinary plus protective intents bounded without eviction");
            string saved = campaign.ExportCheckpoint();
            DataTests.Throws(() => ApmCampaign.Recover(saved.Replace("\"OrdinaryIntents\":2", "\"OrdinaryIntents\":1")),
                "recovery rejects valid ledger exceeding declared native intent budget");
            ApmCampaign recovered = ApmCampaign.Recover(saved);
            Program.Equal(new ApmOperationalLimits(2, 3), recovered.NativeLimits, "checkpoint preserves operational budget");
            Program.Check(recovered.Snapshot.ExitLatch, "recovery keeps resource exit");
            for (int i = 6; i < 10; i++)
            {
                recovered.Reconcile(6, true, true);
                Program.Equal(ApmAction.Query, recovered.OnMarket(CoreTests.Market(102, i)).Action, "repeat reconcile cannot reset retry budget");
            }
            ApmCampaign late = new ApmCampaign(CoreTests.Spec(), new ApmPolicy(), new ApmOperationalLimits(1, 2));
            ApmIntent entry = late.Reserve(late.OnMarket(CoreTests.Market(100, 0)));
            late.OnMarket(CoreTests.Market(100, 1));
            late.ApplyOrder(entry.Id, ApmOrderState.Canceled, 0);
            ApmFill fill = new ApmFill("late-native", entry.Id, CoreTests.Start.AddMinutes(2), 100, 10, 0);
            late.ApplyFill(fill); late.ApplyFill(fill);
            Program.Equal(10m, late.Snapshot.FilledVolume, "late native fill retained and duplicate ignored after resource exit");
            CoreTests.FillDecision(late, late.OnMarket(CoreTests.Market(100, 3)));
            Program.Equal(ApmState.Completed, late.Snapshot.State, "late native fill closed from reserved budget");
            Program.Equal(2, late.Intents.Length, "terminal IDs preserved for recovery");
            ApmCampaign repeat = ApmCampaign.Recover(late.ExportCheckpoint());
            Program.Check(repeat.Reconcile(0, true, true), "terminal recovery authoritative flat reconcile");
            repeat.ApplyFill(fill);
            Program.Equal(0m, repeat.Snapshot.FilledVolume, "recovered terminal duplicate never reopens inventory");
            Program.Equal(ApmAction.Wait, repeat.OnMarket(CoreTests.Market(100, 4)).Action, "recovered ExitLatch never reenters");
            long ticks = 0;
            ApmReplayWatchdog watchdog = new ApmReplayWatchdog(() => ticks, 1000);
            ticks = 1000; watchdog.Observe(); ticks = 11000;
            Program.Check(watchdog.Read(TimeSpan.FromSeconds(10)).Stalled, "independent wall-clock progress timeout");
            Program.Equal(1L, watchdog.Read(TimeSpan.FromSeconds(10)).Callbacks, "watchdog observes exact callback count");
            Program.Equal(ApmState.Completed, repeat.Snapshot.State, "wall-clock watchdog does not trade");
            ResearchController();
            LateFillWithWorkingLastExit();
            NativeCapabilityViolations();
        }

        private static void NativeCapabilityViolations()
        {
            ApmCampaignSpec spec = CoreTests.Spec();
            DataTests.Throws(() => ApmCampaign.Recover(null), "missing checkpoint rejected before parsing");
            DataTests.Throws(() => ApmCampaign.Recover(new string(' ', 16 * 1024 * 1024 + 1)), "oversized checkpoint rejected before parsing");
            ApmPolicy futureFit = new ApmPolicy { ResearchExecution = new ApmAcSettings(3, 6, 0, 1, 1, 0, "future-fit", spec.EntryTime) };
            DataTests.Throws(() => new ApmCampaign(spec, futureFit), "research calibration cutoff cannot touch campaign entry");
            string oversized = new string('x', 129);
            foreach (ApmCampaignSpec invalid in new[] { spec with { CampaignId = oversized }, spec with { EntrySignalId = oversized },
                spec with { Instrument = oversized }, spec with { Account = oversized } })
                DataTests.Throws(() => new ApmCampaign(invalid, new ApmPolicy(), new ApmOperationalLimits()),
                    "native persistent identifier bounded before allocating ledger");
            ApmCampaign campaign = new ApmCampaign(spec, new ApmPolicy(), new ApmOperationalLimits());
            ApmIntent entry = campaign.Reserve(campaign.OnMarket(CoreTests.Market(100, 0)));
            ApmFill fill = new ApmFill("actual-partial", entry.Id, CoreTests.Start, 100, 3, 0);
            foreach (ApmFill invalid in new ApmFill[] { null, fill with { Id = " " }, fill with { Volume = 0 }, fill with { Fee = -1 } })
            {
                DataTests.Throws(() => campaign.ApplyFill(invalid), "invalid fill cannot mutate native accounting");
                Program.Equal(0m, campaign.Snapshot.FilledVolume, "invalid fill leaves actual inventory unchanged");
            }
            campaign.ApplyFill(fill);
            Program.Equal(3m, campaign.Snapshot.FilledVolume, "native full-fill violation still accounts actual partial");
            Program.Equal(7m, campaign.Snapshot.PendingIncrease, "native partial retains unfilled entry reservation");
            Program.Equal(ApmState.Reconciling, campaign.Snapshot.State, "native full-fill capability breach freezes increases");
            Program.Equal(ApmAction.Query, campaign.OnMarket(CoreTests.Market(100, 1)).Action, "no blind reentry after unexpected partial");
            campaign.ApplyFill(fill);
            Program.Equal(3m, campaign.Snapshot.FilledVolume, "capability violation keeps fill deduplication");
        }

        private static void ResearchController()
        {
            ApmPolicy policy = new ApmPolicy { ResearchExecution = new ApmAcSettings(3, 6, 0, 1, 1, 0, "SYNTHETIC-v1", CoreTests.Start.AddDays(-1)) };
            Gateway gateway = new Gateway();
            using ApmExecutionController controller = new ApmExecutionController(new ApmCampaign(CoreTests.Spec(), policy), gateway, null);
            gateway.Controller = controller;
            controller.Process(CoreTests.Market(100, 0));
            Program.Equal(10m, gateway.Sends[0].Volume, "AC does not pace initial entry");
            ApmDecision paced = controller.Process(CoreTests.Market(104, 1));
            Program.Equal(1m, paced.Volume, "controller reserves on-grid AC cap");
            Program.Equal(6m, paced.RiskAllowedTarget, "execution pacing does not alter risk target");
            Program.Equal(9m, controller.Snapshot.FilledVolume, "only actual paced fill changes quantity");
            controller.Process(new ApmMarket(CoreTests.Start.AddMinutes(1).AddSeconds(1), 3, 104, true));
            Program.Equal(2, gateway.Sends.Count, "AC waits for next bucket after actual fill");
            controller.Process(new ApmMarket(CoreTests.Start.AddMinutes(1).AddSeconds(2), 4, 101, true));
            Program.Equal(2, gateway.Sends.Count, "APM-T10-001 changed target equal actual inventory waits");
            controller.Process(new ApmMarket(CoreTests.Start.AddMinutes(1).AddSeconds(5), 5, 104, true));
            Program.Equal(1m, gateway.Sends.Last().Volume, "APM-T10-001 returning target starts new bucket from q9");
            controller.Process(new ApmMarket(CoreTests.Start.AddMinutes(1).AddSeconds(6), 6, 89, true));
            Program.Equal(ApmAction.Exit, gateway.Sends.Last().Action, "hard stop bypasses pending ordinary schedule");
            Program.Equal(8m, gateway.Sends.Last().Volume, "protective whole residual unpaced");
            Program.Equal(ApmState.Completed, controller.Snapshot.State, "research execution same terminal accounting");
        }

        private static void LateFillWithWorkingLastExit()
        {
            ApmCampaign campaign = new ApmCampaign(CoreTests.Spec(), new ApmPolicy(), new ApmOperationalLimits(2, 3));
            CoreTests.FillDecision(campaign, campaign.OnMarket(CoreTests.Market(100, 0)));
            ApmIntent add = campaign.Reserve(campaign.OnMarket(CoreTests.Market(98, 1)));
            campaign.ApplyOrder(add.Id, ApmOrderState.Canceled, 0);
            for (int i = 2; i < 4; i++)
            {
                ApmIntent canceled = campaign.Reserve(campaign.OnMarket(CoreTests.Market(98, i)));
                campaign.ApplyOrder(canceled.Id, ApmOrderState.Canceled, 0);
            }
            ApmIntent working = campaign.Reserve(campaign.OnMarket(CoreTests.Market(98, 4)));
            campaign.ApplyOrder(working.Id, ApmOrderState.Working, 0);
            campaign.ApplyFill(new ApmFill("late-full-add", add.Id, CoreTests.Start.AddMinutes(5), 98, add.Volume, 0));
            Program.Equal(14m, campaign.Snapshot.FilledVolume, "APM-T11-001 late full add updates actual q");
            Program.Equal(10m, campaign.Snapshot.PendingReduce, "APM-T11-001 final working exit retains reservation");
            ApmDecision blocked = campaign.OnMarket(CoreTests.Market(98, 5));
            Program.Equal(ApmAction.Query, blocked.Action, "APM-T11-001 no impossible fourth exit proposal");
            Program.Check(blocked.Reasons.Contains("EXIT_BUDGET_EXHAUSTED"), "APM-T11-001 exact exhaustion reason observable");
            Program.Equal(ApmState.FaultedClosing, campaign.Snapshot.State, "APM-T11-001 exhausted budget explicitly faulted despite pending exit");
            ApmCampaign recovered = ApmCampaign.Recover(campaign.ExportCheckpoint());
            recovered.Reconcile(14, true, true);
            Program.Equal(ApmAction.Query, recovered.OnMarket(CoreTests.Market(98, 6)).Action, "APM-T11-001 recovery preserves exhausted budget and reservation");
            Program.Equal(10m, recovered.Snapshot.PendingReduce, "APM-T11-001 recovery keeps working exit reservation");
            campaign.ApplyFill(new ApmFill("last-exit-fill", working.Id, CoreTests.Start.AddMinutes(6), 98, working.Volume, 0));
            Program.Equal(4m, campaign.Snapshot.FilledVolume, "APM-T11-001 remaining exposure never fabricated flat");
        }

        private sealed class Gateway : IApmOrderGateway
        {
            internal ApmExecutionController Controller;
            internal readonly List<ApmIntent> Sends = new List<ApmIntent>();
            public void Send(ApmIntent intent)
            {
                Sends.Add(intent);
                Controller.ApplyFill(new ApmFill("research-" + intent.Id, intent.Id, intent.Time,
                    Controller.Snapshot.Market.Price, intent.Volume, 0));
                Controller.ApplyOrder(intent.Id, ApmOrderState.Filled, intent.Volume, "broker-" + intent.Id);
            }
            public void Cancel(ApmIntent intent) => Controller.ApplyOrder(intent.Id, ApmOrderState.Canceled, intent.Filled, "broker-" + intent.Id);
        }
    }
}
