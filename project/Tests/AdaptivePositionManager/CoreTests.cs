using System;
using System.Collections.Generic;
using System.Linq;
using OsEngine.OsTrader.AdaptivePositionManager;

namespace OsEngine.AdaptivePositionManager.Tests
{
    /// <summary>Offline golden, property and fault-injection evidence; fills are explicit synthetic facts.</summary>
    internal static class CoreTests
    {
        internal static readonly DateTime Start = new DateTime(2026, 9, 25, 10, 0, 0);

        internal static ApmCampaignSpec Spec(ApmDirection direction = ApmDirection.Long)
        {
            return new ApmCampaignSpec("synthetic", "entry-1", "APM_SYNTH", "fixture", direction,
                Start, Start.AddHours(4), 100, 10, 20,
                direction == ApmDirection.Long ? 90 : 110, direction == ApmDirection.Long ? 110 : 90,
                1000, 1, 1, 1, "SYN", "SYN", "UTC");
        }

        internal static ApmMarket Market(decimal price, int index, bool ready = true, decimal speed = 0)
            => new ApmMarket(Start.AddMinutes(index), index + 1, price, ready, 0, 0, speed);

        internal static void Run()
        {
            Golden();
            Properties();
            Protection();
            Faults();
            TerminalMissingFills();
            Regimes();
            Recovery();
        }

        private static void Golden()
        {
            decimal[][] paths = { new decimal[] { 100, 102, 104, 102, 100, 104, 102 },
                new decimal[] { 100, 98, 96, 98, 96, 98 } };
            decimal[][] expected = { new decimal[] { 10, 8, 6, 8, 10, 6, 8 },
                new decimal[] { 10, 14, 18, 14, 18, 14 } };
            foreach (ApmDirection direction in new[] { ApmDirection.Long, ApmDirection.Short })
            {
                for (int scenario = 0; scenario < paths.Length; scenario++)
                {
                    ApmCampaign campaign = new ApmCampaign(Spec(direction), new ApmPolicy());
                    for (int i = 0; i < paths[scenario].Length; i++)
                    {
                        decimal price = direction == ApmDirection.Long ? paths[scenario][i] : 200 - paths[scenario][i];
                        ApmDecision decision = campaign.OnMarket(Market(price, i));
                        FillDecision(campaign, decision);
                        Program.Equal(expected[scenario][i], campaign.Snapshot.FilledVolume, "S0" + (scenario + 1) + " q " + i);
                        Near(campaign.CashEquity(price), campaign.Equity(price), "cash identity every event");
                        if (scenario == 0 && i == 4) Near(8, campaign.Equity(price), "S01 independent equity oracle");
                    }
                    Near(scenario == 0 ? 36 : -4, campaign.Equity(direction == ApmDirection.Long
                        ? paths[scenario].Last() : 200 - paths[scenario].Last()), "independent final equity");
                }
            }
            ApmCampaign repeated = Enter();
            for (int i = 1; i <= 100; i++)
            {
                ApmDecision decision = repeated.OnMarket(Market(i % 2 == 1 ? 104 : 100, i));
                FillDecision(repeated, decision);
                Program.Equal(i % 2 == 1 ? 6m : 10m, repeated.Snapshot.FilledVolume, "S03 reversible cycle");
            }
        }

        private static void Properties()
        {
            Random random = new Random(67123);
            for (int i = 0; i < 1500; i++)
            {
                decimal p = 90 + random.Next(1, 1999) / 100m;
                decimal add = random.Next(1, 100);
                decimal reduce = random.Next(1, 100);
                decimal q = ApmMathematics.Curve(Spec(), 100, 10, p, add, reduce);
                decimal mirror = ApmMathematics.Curve(Spec(ApmDirection.Short), 100, 10, 200 - p, add, reduce);
                Program.Equal(q, mirror, "seeded mirrored curve " + i);
                Program.Check(q >= 0 && q <= 20, "seeded bounds");
                Program.Check(ApmMathematics.Curve(Spec(), 100, 10, p + 0.01m, add, reduce) <= q, "seeded monotonicity");
                decimal k = random.Next(0, 100) / 10m;
                decimal reference = random.Next(0, 10) / 10m;
                Program.Check(ApmMathematics.Target(q, 20, reference, k + 1)
                    <= ApmMathematics.Target(q, 20, reference, k), "frozen normalization penalty monotonicity");
                Near(q, ApmMathematics.Target(q, 20, k, k), "activation normalization");
            }
            Program.Equal(2m, ApmMathematics.Quantize(2.5m, 1), "lower risk tie");
            Program.Equal(3m, ApmMathematics.Quantize(3.5m, 1), "not ToEven");
            Program.Equal(3m, ApmMathematics.Quantize(2.51m, 1), "nearest above tie");
            ApmCampaign campaign = Enter();
            Program.Equal(101m, campaign.Preview(ApmAction.Reduce).Value, "preview first reduction");
            string before = campaign.ExportCheckpoint();
            campaign.Preview(ApmAction.Add);
            Program.Equal(before, campaign.ExportCheckpoint(), "preview has no side effects");
            Program.Equal(ApmAction.Reduce, campaign.OnMarket(Market(101, 1)).Action, "preview actual agreement");
        }

        private static void Protection()
        {
            ApmCampaign campaign = Enter();
            Program.Equal(ApmAction.Wait, campaign.OnMarket(Market(92, 1)).Action, "S05 no-add zone");
            ApmDecision stop = campaign.OnMarket(Market(90, 2, false));
            Program.Equal(ApmAction.Exit, stop.Action, "S06 inclusive stop despite stale data");
            FillDecision(campaign, stop, 88);
            Program.Equal(ApmState.Completed, campaign.Snapshot.State, "actual terminal flat");
            Program.Equal(ApmAction.Wait, campaign.OnMarket(Market(100, 3)).Action, "S21 cannot reactivate");
            campaign = Enter();
            Program.Equal(ApmAction.Exit, campaign.OnMarket(Market(110, 1)).Action, "S07 final target");
            campaign = Enter();
            Program.Equal(ApmAction.Exit, campaign.OnMarket(new ApmMarket(Spec().SessionExitTime, 2, 100, false)).Action,
                "S26 explicit timer cutoff without tick");
            campaign = Enter(Spec() with { RiskBudgetCurrency = 110 });
            ApmDecision add = campaign.OnMarket(Market(99, 1));
            Program.Equal(1m, add.Volume, "S14 independent maximum risk add");
            FillDecision(campaign, add);
            Program.Equal(ApmAction.Wait, campaign.OnMarket(Market(99, 2)).Action, "S14 exhausted cap");
            campaign = Enter(null, new ApmPolicy { MaxChildVolume = 3 });
            Program.Equal(3m, campaign.OnMarket(Market(106, 1)).Volume, "S11 one bounded ordinary child");
        }

        private static void Faults()
        {
            ApmCampaign campaign = Enter();
            ApmDecision add = campaign.OnMarket(Market(98, 1));
            ApmIntent intent = campaign.Reserve(add);
            Program.Equal(10m, campaign.Snapshot.FilledVolume, "intent is not execution");
            Program.Equal(4m, campaign.Snapshot.PendingIncrease, "S15 pending reservation");
            Program.Equal(ApmAction.Cancel, campaign.OnMarket(Market(98, 2)).Action, "expired child cancellation proposal");
            campaign.MarkCancelPending(intent.Id);
            Program.Equal(4m, campaign.Snapshot.PendingIncrease, "S15 cancel retains reservation");
            ApmFill fill = new ApmFill("partial", intent.Id, Start.AddMinutes(2), 98, 2, 0);
            campaign.ApplyFill(fill);
            campaign.ApplyFill(fill);
            Program.Equal(12m, campaign.Snapshot.FilledVolume, "S16 partial deduplicated");
            Program.Equal(2m, campaign.Snapshot.PendingIncrease, "partial leaves remainder");
            campaign.ApplyOrder(intent.Id, ApmOrderState.Canceled, 3);
            Program.Equal(1m, campaign.Snapshot.PendingIncrease, "canceled missing fill remains reserved");
            campaign.ApplyFill(new ApmFill("late", intent.Id, Start.AddMinutes(2), 98, 1, 0));
            Program.Equal(0m, campaign.Snapshot.PendingIncrease, "final fill reconciles cancellation");
            Program.Equal(13m, campaign.Snapshot.FilledVolume, "S16 confirmed three of four");

            campaign = Enter();
            intent = campaign.Reserve(campaign.OnMarket(Market(98, 1)));
            campaign.MarkCancelPending(intent.Id);
            ApmDecision stop = campaign.OnMarket(Market(90, 2));
            Program.Equal(10m, stop.Volume, "S17 protect known volume without waiting cancel");
            FillDecision(campaign, stop);
            Program.Check(campaign.Snapshot.State != ApmState.Completed, "pending add prevents false Completed");
            campaign.ApplyFill(new ApmFill("late-stop", intent.Id, Start.AddMinutes(3), 90, 4, 0));
            Program.Equal(ApmAction.Exit, campaign.OnMarket(Market(90, 3)).Action, "late add only protective close");

            campaign = Enter();
            ApmIntent reduce = campaign.Reserve(campaign.OnMarket(Market(104, 1)));
            stop = campaign.OnMarket(Market(90, 2));
            Program.Equal(6m, stop.Volume, "S18 close only unreserved volume");
            ApmIntent exit = campaign.Reserve(stop);
            campaign.ApplyFill(new ApmFill("exit", exit.Id, Start.AddMinutes(2), 90, 6, 0));
            campaign.ApplyFill(new ApmFill("reduce", reduce.Id, Start.AddMinutes(2), 90, 4, 0));
            Program.Equal(0m, campaign.Snapshot.FilledVolume, "S18 no reversal");

            campaign = Enter();
            intent = campaign.Reserve(campaign.OnMarket(Market(98, 1)));
            campaign.MarkUnknown(intent.Id);
            Program.Equal(ApmAction.Query, campaign.OnMarket(Market(98, 2)).Action, "S19 unknown no resend");
            Program.Equal(4m, campaign.Snapshot.PendingIncrease, "unknown reservation");
        }

        private static void TerminalMissingFills()
        {
            foreach (ApmOrderState terminal in new[] { ApmOrderState.Canceled, ApmOrderState.Rejected, ApmOrderState.Filled })
            {
                ApmCampaign campaign = Enter();
                ApmIntent reduce = campaign.Reserve(campaign.OnMarket(Market(104, 1)));
                decimal reported = terminal == ApmOrderState.Filled ? 4 : 2;
                campaign.ApplyOrder(reduce.Id, terminal, reported);
                Program.Equal(reported, campaign.Snapshot.PendingReduce, "terminal missing fills reserved");
                Program.Equal(ApmAction.Wait, campaign.OnMarket(Market(104, 2)).Action,
                    "APM-CORE-001 expired terminal order cannot be canceled again");
                campaign.MarkCancelPending(reduce.Id);
                Program.Equal(terminal, campaign.Intents.Single(i => i.Id == reduce.Id).State,
                    "APM-CORE-001 direct cancellation cannot reopen terminal state");
                Program.Equal(reported, campaign.Snapshot.PendingReduce, "terminal reservation never expands");
                campaign.ApplyFill(new ApmFill("late-terminal", reduce.Id, Start.AddMinutes(2), 104, reported, 0));
                Program.Equal(0m, campaign.Snapshot.PendingReduce, "late terminal fill clears complete reservation");
                ApmDecision stop = campaign.OnMarket(Market(90, 3));
                Program.Equal(10 - reported, stop.Volume, "APM-CORE-001 stop covers entire actual remainder");
                FillDecision(campaign, stop);
                Program.Equal(0m, campaign.Snapshot.FilledVolume, "APM-CORE-001 protective actual flat");
                Program.Equal(ApmState.Completed, campaign.Snapshot.State, "APM-CORE-001 no phantom close reservation");
            }
        }

        private static void Regimes()
        {
            ApmPolicy policy = new ApmPolicy { FastEnabled = true, MaxFavorableDeferSeconds = 90 };
            ApmCampaign campaign = Enter(null, policy);
            Program.Equal(ApmAction.Wait, campaign.OnMarket(Market(106, 1, true, 4)).Action, "S08 favorable defer");
            Program.Equal(ApmAction.Wait, campaign.OnMarket(Market(106, 2, true, 4)).Action, "same defer timer");
            Program.Equal(ApmAction.Reduce, campaign.OnMarket(Market(106, 3, true, 4)).Action, "S10 bounded defer");
            Program.Equal(ApmAction.Exit, campaign.OnMarket(Market(110, 4, true, 4)).Action, "target overrides fast");
            campaign = Enter(null, policy);
            Program.Equal(ApmAction.Wait, campaign.OnMarket(Market(96, 1, true, -4)).Action, "S09 adverse blocks averaging");
            Program.Equal(4m, campaign.OnMarket(Market(98, 2, true, 0)).Volume, "no skipped-volume debt");
        }

        private static void Recovery()
        {
            ApmCampaign campaign = Enter();
            campaign.RequestExit("MANUAL_EXIT");
            ApmCampaign recovered = ApmCampaign.Recover(campaign.ExportCheckpoint());
            Program.Check(recovered.Snapshot.ExitLatch, "S20 persisted exit latch");
            Program.Check(!recovered.Reconcile(11, true, true), "foreign volume cannot be adopted");
            Program.Check(recovered.Reconcile(10, true, true), "confirmed snapshot clears reconciliation");
            Program.Equal(ApmAction.Exit, recovered.OnMarket(Market(100, 1)).Action, "recovered terminal close");
            Program.Equal(campaign.CashEquity(102), recovered.CashEquity(102), "checkpoint exact decimal ledger");
        }

        internal static ApmCampaign Enter(ApmCampaignSpec spec = null, ApmPolicy policy = null)
        {
            ApmCampaign campaign = new ApmCampaign(spec ?? Spec(), policy ?? new ApmPolicy());
            FillDecision(campaign, campaign.OnMarket(Market(100, 0)));
            return campaign;
        }

        internal static void FillDecision(ApmCampaign campaign, ApmDecision decision, decimal? actualPrice = null)
        {
            Program.Check(decision.Volume > 0, "actionable fixture decision " + string.Join(",", decision.Reasons));
            ApmIntent intent = campaign.Reserve(decision);
            campaign.ApplyFill(new ApmFill("fill-" + intent.Id, intent.Id, decision.Time,
                actualPrice ?? decision.Price, intent.Volume, campaign.Spec.FeePerContract * intent.Volume));
            campaign.ApplyOrder(intent.Id, ApmOrderState.Filled, intent.Volume);
        }

        private static void Near(decimal expected, decimal actual, string name)
            => Program.Check(Math.Abs(expected - actual) < 0.000000000000000001m, name + " " + expected + " != " + actual);
    }
}
