using System;
using System.Linq;
using OsEngine.OsTrader.AdaptivePositionManager;

namespace OsEngine.AdaptivePositionManager.Tests
{
    /// <summary>Broker contradictions and permissions changing between proposal and reservation.</summary>
    internal static class LifecycleTests
    {
        internal static void Run()
        {
            ReservationRaces();
            BrokerContradictions();
            EntryAndCosts();
            PauseAndRearm();
            ScheduledWarmup();
        }

        private static void ScheduledWarmup()
        {
            foreach (decimal price in new[] { 80m, 120m })
            {
                ApmCampaign campaign = new ApmCampaign(CoreTests.Spec(), new ApmPolicy());
                campaign.OnMarket(new ApmMarket(CoreTests.Start.AddSeconds(-1), 1, price, true));
                Program.Check(!campaign.Snapshot.ExitLatch, "pre-entry warmup cannot terminate a future empty campaign");
                Program.Equal(ApmAction.InitialEntry, campaign.OnMarket(new ApmMarket(CoreTests.Start, 2, 100, true)).Action,
                    "scheduled signal remains available after earlier S/U crossing");
                ApmCampaign atEntry = new ApmCampaign(CoreTests.Spec(), new ApmPolicy());
                atEntry.OnMarket(new ApmMarket(CoreTests.Start, 1, price, true));
                Program.Check(atEntry.Snapshot.ExitLatch, "S/U at scheduled entry still terminates campaign");
            }
        }

        private static void ReservationRaces()
        {
            foreach (string change in new[] { "pause", "reconcile", "exit", "newer", "already-reserved" })
            {
                ApmCampaign campaign = CoreTests.Enter();
                ApmDecision add = campaign.OnMarket(CoreTests.Market(98, 1));
                if (change == "pause") campaign.Pause(true);
                if (change == "reconcile") campaign.RequireReconciliation("OWNER_CHECK");
                if (change == "exit") campaign.RequestExit("MANUAL_EXIT");
                if (change == "newer") campaign.OnMarket(CoreTests.Market(99, 2));
                if (change == "already-reserved") campaign.Reserve(add);
                int count = campaign.Intents.Length;
                decimal pending = campaign.Snapshot.PendingIncrease;
                bool rejected = false;
                try { campaign.Reserve(add); } catch (InvalidOperationException) { rejected = true; }
                Program.Check(rejected, "stale or disallowed send rejected: " + change);
                Program.Equal(count, campaign.Intents.Length, "failed reservation adds no intent");
                Program.Equal(pending, campaign.Snapshot.PendingIncrease, "failed reservation changes no pending risk");
            }
        }

        private static void BrokerContradictions()
        {
            foreach (decimal total in new[] { -1m, 5m })
            {
                ApmCampaign campaign = CoreTests.Enter();
                ApmIntent intent = campaign.Reserve(campaign.OnMarket(CoreTests.Market(98, 1)));
                campaign.ApplyOrder(intent.Id, ApmOrderState.Working, total);
                Program.Equal(ApmState.Reconciling, campaign.Snapshot.State, "invalid cumulative order total freezes");
                Program.Equal(4m, campaign.Snapshot.PendingIncrease, "invalid callback cannot free reservation");
            }
            foreach (ApmOrderState state in new[] { ApmOrderState.Canceled, ApmOrderState.Filled })
            {
                ApmCampaign campaign = CoreTests.Enter();
                ApmIntent intent = campaign.Reserve(campaign.OnMarket(CoreTests.Market(98, 1)));
                campaign.ApplyFill(new ApmFill("partial", intent.Id, CoreTests.Start, 98, 2, 0));
                campaign.ApplyOrder(intent.Id, state, 1);
                Program.Equal(ApmOrderState.Unknown, campaign.Intents.Last().State, "terminal contradicting own fills is unknown");
                Program.Equal(2m, campaign.Snapshot.PendingIncrease, "contradictory finality reserves full unfilled remainder");
            }
            ApmCampaign pending = CoreTests.Enter();
            ApmIntent add = pending.Reserve(pending.OnMarket(CoreTests.Market(98, 1)));
            pending.MarkCancelPending(add.Id);
            pending.ApplyOrder(add.Id, ApmOrderState.Working, 0);
            Program.Equal(ApmOrderState.CancelPending, pending.Intents.Last().State, "late ack cannot undo pending cancellation");
            pending.ApplyOrder(add.Id, ApmOrderState.Canceled, 0);
            pending.ApplyOrder(add.Id, ApmOrderState.Working, 0);
            pending.ApplyOrder(add.Id, ApmOrderState.Prepared, 0);
            Program.Equal(ApmOrderState.Canceled, pending.Intents.Last().State, "stale ack cannot revive final order");
            foreach (string corruption in new[] { "identity", "foreign", "excess" })
            {
                ApmCampaign campaign = CoreTests.Enter();
                ApmIntent entry = campaign.Intents[0];
                ApmFill fill = new ApmFill("fill-" + entry.Id, entry.Id, CoreTests.Start, 999, 10, 0);
                if (corruption == "foreign") fill = fill with { Id = "foreign", IntentId = "other" };
                if (corruption == "excess") fill = fill with { Id = "excess", Volume = 1 };
                decimal cash = campaign.CashEquity(100);
                campaign.ApplyFill(fill);
                Program.Equal(ApmState.Reconciling, campaign.Snapshot.State, "invalid own fill freezes: " + corruption);
                Program.Equal(10m, campaign.Snapshot.FilledVolume, "contradiction does not invent quantity");
                Program.Equal(cash, campaign.CashEquity(100), "contradiction does not invent cash");
            }
            ApmCampaign closing = CoreTests.Enter();
            ApmIntent exit = closing.Reserve(closing.OnMarket(CoreTests.Market(90, 1)));
            closing.ApplyOrder(exit.Id, ApmOrderState.Rejected, 0);
            Program.Equal(ApmState.FaultedClosing, closing.Snapshot.State, "rejected exit exposes still-open fault");
            Program.Equal(ApmAction.Query, closing.OnMarket(CoreTests.Market(100, 2)).Action, "rejected exit requires reconciliation before retry");
        }

        private static void EntryAndCosts()
        {
            ApmCampaign campaign = new ApmCampaign(CoreTests.Spec(), new ApmPolicy());
            Program.Equal(ApmAction.Wait, campaign.OnMarket(CoreTests.Market(100, -1)).Action, "no entry before schedule");
            Program.Equal(ApmAction.Wait, campaign.OnMarket(CoreTests.Market(100, 0, false)).Action, "no entry before readiness");
            ApmIntent entry = campaign.Reserve(campaign.OnMarket(CoreTests.Market(100, 1)));
            campaign.ApplyOrder(entry.Id, ApmOrderState.Rejected, 0);
            Program.Equal(ApmState.Completed, campaign.Snapshot.State, "unfilled rejected signal terminal once");
            campaign = new ApmCampaign(CoreTests.Spec() with { RiskBudgetCurrency = 99 }, new ApmPolicy());
            Program.Check(campaign.OnMarket(CoreTests.Market(100, 0)).Reasons.Contains("INITIAL_RISK_REJECTED"),
                "initial risk refusal never silently scales entry");
            campaign = new ApmCampaign(CoreTests.Spec(), new ApmPolicy());
            entry = campaign.Reserve(campaign.OnMarket(CoreTests.Market(100, 0)));
            campaign.ApplyFill(new ApmFill("partial", entry.Id, CoreTests.Start, 101, 2, 0));
            campaign.ApplyOrder(entry.Id, ApmOrderState.Canceled, 2);
            Program.Equal(101m, campaign.Snapshot.Anchor, "confirmed partial initial fixes actual VWAP anchor");
            Program.Equal(2m, campaign.Snapshot.FilledVolume, "partial initial completed with actual quantity");
            foreach (decimal price in new[] { 90m, 110m })
            {
                campaign = new ApmCampaign(CoreTests.Spec(), new ApmPolicy());
                entry = campaign.Reserve(campaign.OnMarket(CoreTests.Market(100, 0)));
                campaign.ApplyFill(new ApmFill("bad-entry", entry.Id, CoreTests.Start, price, 2, 0));
                Program.Equal("INVALID_FILLED_ANCHOR", campaign.Snapshot.ExitReason, "out-of-bound partial anchor protected immediately");
            }
            campaign = new ApmCampaign(CoreTests.Spec(), new ApmPolicy());
            entry = campaign.Reserve(campaign.OnMarket(CoreTests.Market(100, 0)));
            campaign.ApplyFill(new ApmFill("cost-shock", entry.Id, CoreTests.Start, 100, 10, 1000));
            Program.Equal("MONEY_STOP", campaign.Snapshot.ExitReason, "whole equity money stop includes actual fee shock");
        }

        private static void PauseAndRearm()
        {
            ApmCampaign campaign = CoreTests.Enter();
            campaign.Pause(true);
            Program.Equal(ApmAction.Wait, campaign.OnMarket(CoreTests.Market(98, 1)).Action, "pause blocks additions");
            Program.Equal(ApmAction.Reduce, campaign.OnMarket(CoreTests.Market(104, 2)).Action, "pause permits reduction");
            Program.Equal(ApmAction.Exit, campaign.OnMarket(CoreTests.Market(90, 3)).Action, "pause permits stop");
            campaign = CoreTests.Enter(null, new ApmPolicy { MinActionIntervalSeconds = 120 });
            Program.Check(campaign.OnMarket(CoreTests.Market(104, 1)).Reasons.Contains("REARM_PENDING"), "interval starts at last actual fill");
            Program.Equal(ApmAction.Reduce, campaign.OnMarket(CoreTests.Market(104, 2)).Action, "inclusive interval boundary");
            campaign = CoreTests.Enter(null, new ApmPolicy { RearmMinTicks = 2 });
            CoreTests.FillDecision(campaign, campaign.OnMarket(CoreTests.Market(104, 1)));
            Program.Equal(ApmAction.Wait, campaign.OnMarket(CoreTests.Market(103, 2)).Action, "one-tick reversal below rearm");
            Program.Equal(ApmAction.Add, campaign.OnMarket(CoreTests.Market(102, 3)).Action, "two-tick reversal rearmed");
        }
    }
}
