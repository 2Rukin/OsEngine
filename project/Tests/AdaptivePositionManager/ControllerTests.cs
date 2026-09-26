using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OsEngine.OsTrader.AdaptivePositionManager;

namespace OsEngine.AdaptivePositionManager.Tests
{
    /// <summary>Explicit offline transport faults, crash boundaries and synchronous callbacks; never calls a native connector.</summary>
    internal static class ControllerTests
    {
        internal static void Run()
        {
            Gateway gateway = new Gateway { Immediate = true };
            using (ApmExecutionController controller = new ApmExecutionController(new ApmCampaign(CoreTests.Spec(), new ApmPolicy()), gateway, null))
            {
                gateway.Controller = controller;
                controller.Process(CoreTests.Market(100, 0));
                controller.Process(CoreTests.Market(104, 1));
                controller.Process(CoreTests.Market(102, 2));
                Program.Equal(8m, controller.Snapshot.FilledVolume, "controller synchronous restore");
                Program.Equal(3, gateway.Sends.Count, "one ordinary order per snapshot");
                controller.Close("EMERGENCY_EXIT");
                Program.Equal(ApmState.Completed, controller.Snapshot.State, "synchronous terminal fill");
                controller.Process(CoreTests.Market(100, 3));
                Program.Equal(4, gateway.Sends.Count, "completed signal never repeats");
                Program.Check(controller.Recent.Any(r => r.Kind == "Fill" && r.Snapshot != null), "stored fill snapshot");
            }

            gateway = new Gateway();
            using (ApmExecutionController controller = new ApmExecutionController(new ApmCampaign(CoreTests.Spec(), new ApmPolicy()), gateway, null))
            {
                gateway.Controller = controller;
                controller.Process(CoreTests.Market(100, 0));
                ApmIntent initial = gateway.Sends[0];
                controller.ApplyFill(new ApmFill("partial", initial.Id, CoreTests.Start, 100, 2, 0));
                controller.Process(CoreTests.Market(90, 1));
                Program.Check(gateway.Cancels.Contains(initial.Id), "partial initial canceled at stop");
                Program.Equal(2m, gateway.Sends.Last().Volume, "partial initial immediately protected");
                controller.ApplyFill(new ApmFill("close-part", gateway.Sends.Last().Id, CoreTests.Start.AddMinutes(1), 90, 2, 0));
                Program.Check(controller.Snapshot.State != ApmState.Completed, "cancel pending prevents false flat");
                controller.ApplyFill(new ApmFill("late-initial", initial.Id, CoreTests.Start.AddMinutes(1), 90, 8, 0));
                Program.Equal(ApmAction.Exit, gateway.Sends.Last().Action, "late initial protective action");
                Program.Equal(8m, gateway.Sends.Last().Volume, "late initial protected exact volume");
            }

            gateway = new Gateway { ThrowAfterSend = true };
            using (ApmExecutionController controller = new ApmExecutionController(new ApmCampaign(CoreTests.Spec(), new ApmPolicy()), gateway, null))
            {
                gateway.Controller = controller;
                bool failed = false;
                try { controller.Process(CoreTests.Market(100, 0)); } catch (ApmExecutionUncertainException) { failed = true; }
                Program.Check(failed, "uncertain send visible to caller");
                controller.Process(CoreTests.Market(100, 1));
                Program.Equal(1, gateway.Sends.Count, "unknown never resends");
                Program.Equal(10m, controller.Snapshot.PendingIncrease, "unknown initial remains reserved");
            }

            string directory = Path.Combine(Path.GetTempPath(), "APM-durable-test-" + Guid.NewGuid().ToString("N"));
            gateway = new Gateway { CheckpointPath = Path.Combine(directory, "checkpoint.json") };
            using (ApmExecutionController controller = new ApmExecutionController(new ApmCampaign(CoreTests.Spec(), new ApmPolicy()), gateway,
                new ApmArtifacts(directory)))
            {
                gateway.Controller = controller;
                controller.Process(CoreTests.Market(100, 0));
                Program.Check(gateway.SawPreparedCheckpoint, "durable reservation exists before send");
                ApmCampaign recovered = ApmArtifacts.LoadCheckpoint(gateway.CheckpointPath);
                Program.Equal(10m, recovered.Snapshot.PendingIncrease, "crash after send before ack retains risk");
                Program.Equal(ApmState.Reconciling, recovered.Snapshot.State, "crash cannot reactivate blindly");
            }
            CancelFailure();
            RecoveryClock();
            PersistenceFailure();
            ReentrantPersistenceFailure();
            IndependentPausePermissions();
        }

        private static void IndependentPausePermissions()
        {
            Gateway gateway = new Gateway { Immediate = true };
            using ApmExecutionController controller = new ApmExecutionController(new ApmCampaign(CoreTests.Spec(), new ApmPolicy()), gateway, null);
            gateway.Controller = controller;
            controller.Process(CoreTests.Market(100, 0));
            controller.Pause(true);
            controller.SetEnabled(true);
            controller.Process(CoreTests.Market(98, 1));
            Program.Equal(10m, controller.Snapshot.FilledVolume, "shell On never clears UI pause on tick/timer");
            controller.SetEnabled(false);
            controller.Pause(false);
            controller.Process(CoreTests.Market(98, 2));
            Program.Equal(10m, controller.Snapshot.FilledVolume, "UI Resume never overrides shell Off before timer");
            controller.SetEnabled(true);
            controller.Process(CoreTests.Market(98, 3));
            Program.Equal(14m, controller.Snapshot.FilledVolume, "both permissions restore ordinary increases");
            controller.SetEnabled(false);
            controller.Process(CoreTests.Market(90, 4));
            Program.Equal(ApmState.Completed, controller.Snapshot.State, "shell Off never disables protection");
        }

        private static void CancelFailure()
        {
            Gateway gateway = new Gateway { ThrowOnCancel = true };
            using ApmExecutionController controller = new ApmExecutionController(new ApmCampaign(CoreTests.Spec(), new ApmPolicy()), gateway, null);
            gateway.Controller = controller;
            controller.Process(CoreTests.Market(100, 0));
            ApmIntent initial = gateway.Sends[0];
            controller.ApplyFill(new ApmFill("partial", initial.Id, CoreTests.Start, 100, 2, 0));
            bool failed = false;
            try { controller.Process(CoreTests.Market(90, 1)); } catch (ApmExecutionUncertainException) { failed = true; }
            Program.Check(failed, "APM-ADAPTER-003 cancellation uncertainty exposed to normal error logger");
            Program.Equal(8m, controller.Snapshot.PendingIncrease, "failed cancel preserves full initial remainder");
            Program.Equal(2m, gateway.Sends.Last().Volume, "failed cancel does not delay confirmed protective close");
            Program.Equal(ApmAction.Exit, gateway.Sends.Last().Action, "only protection after cancel uncertainty");
            controller.ApplyFill(new ApmFill("first-close", gateway.Sends.Last().Id, CoreTests.Start.AddMinutes(1), 90, 2, 0));
            controller.ApplyFill(new ApmFill("late-entry", initial.Id, CoreTests.Start.AddMinutes(1), 90, 8, 0));
            Program.Equal(8m, gateway.Sends.Last().Volume, "late fill preserves execution-uncertainty protection permission");
            Program.Equal(1, gateway.Cancels.Count, "unknown cancellation never blindly repeated");
            Program.Equal(3, gateway.Sends.Count, "initial plus two exact protective orders only");
        }

        private static void RecoveryClock()
        {
            ApmCampaign original = CoreTests.Enter();
            original.OnMarket(CoreTests.Market(100, 5) with { Sequence = 500 });
            foreach (bool closeFirst in new[] { false, true })
            {
                Gateway gateway = new Gateway();
                using ApmExecutionController controller = new ApmExecutionController(ApmCampaign.Recover(original.ExportCheckpoint()), gateway, null);
                gateway.Controller = controller;
                Program.Check(controller.Reconcile(10, true, true), "recovery snapshot explicitly confirmed");
                if (closeFirst) controller.Close();
                else controller.Process(CoreTests.Market(104, 6));
                Program.Equal(1, gateway.Sends.Count, "APM-ADAPTER-002 recovery first action reaches gateway");
                Program.Equal(closeFirst ? ApmAction.Exit : ApmAction.Reduce, gateway.Sends[0].Action, "recovery resumes only requested valid action");
                Program.Check(controller.Snapshot.Decision.Sequence > 500, "recovery preserves causal monotonicity");
                Program.Equal(closeFirst ? 10m : 4m, gateway.Sends[0].Volume, "recovery quantity stays based on confirmed ledger");
            }
        }

        private static void PersistenceFailure()
        {
            string directory = Path.Combine(Path.GetTempPath(), "APM-disk-fault-" + Guid.NewGuid().ToString("N"));
            Gateway gateway = new Gateway();
            ApmArtifacts artifacts = new ApmArtifacts(directory);
            Directory.CreateDirectory(Path.Combine(directory, "checkpoint.json"));
            using ApmExecutionController controller = new ApmExecutionController(new ApmCampaign(CoreTests.Spec(), new ApmPolicy()), gateway, artifacts);
            gateway.Controller = controller;
            bool failed = false;
            try { controller.Process(CoreTests.Market(100, 0)); }
            catch (ApmPersistenceException) { failed = true; }
            Program.Check(failed, "checkpoint write failure visible");
            Program.Equal(0, gateway.Sends.Count, "persistence failure forbids transport submission");
            Program.Equal(ApmState.Reconciling, controller.Snapshot.State, "persistence failure freezes owner");
        }

        private static void ReentrantPersistenceFailure()
        {
            string directory = Path.Combine(Path.GetTempPath(), "APM-callback-disk-fault-" + Guid.NewGuid().ToString("N"));
            Gateway gateway = new Gateway { LockCheckpointDuringFill = Path.Combine(directory, "checkpoint.json") };
            using ApmExecutionController controller = new ApmExecutionController(new ApmCampaign(CoreTests.Spec(), new ApmPolicy()), gateway,
                new ApmArtifacts(directory));
            gateway.Controller = controller;
            bool failed = false;
            try { controller.Process(CoreTests.Market(100, 0)); }
            catch (ApmPersistenceException) { failed = true; }
            Program.Check(failed, "APM-ADAPTER-003 callback IO keeps its own persistence exception classification");
            Program.Equal(10m, controller.Snapshot.FilledVolume, "own fill remains an actual fact after failed checkpoint");
            Program.Equal(0m, controller.Snapshot.PendingIncrease, "callback IO must not reopen fully filled intent as unknown");
            ApmDecision stop = controller.Process(CoreTests.Market(90, 1));
            Program.Equal(ApmAction.Query, stop.Action, "persistence latch survives after temporary file lock released");
            Program.Equal(1, gateway.Sends.Count, "no subsequent protective send without persistence reconciliation");
        }

        private sealed class Gateway : IApmOrderGateway
        {
            internal ApmExecutionController Controller;
            internal bool Immediate;
            internal bool ThrowAfterSend;
            internal bool ThrowOnCancel;
            internal string CheckpointPath;
            internal string LockCheckpointDuringFill;
            internal bool SawPreparedCheckpoint;
            internal readonly List<ApmIntent> Sends = new List<ApmIntent>();
            internal readonly List<string> Cancels = new List<string>();

            public void Send(ApmIntent intent)
            {
                if (CheckpointPath != null)
                    SawPreparedCheckpoint = ApmArtifacts.LoadCheckpoint(CheckpointPath).Intents
                        .Any(i => i.Id == intent.Id && i.State == ApmOrderState.Prepared);
                Sends.Add(intent);
                if (LockCheckpointDuringFill != null)
                {
                    using FileStream locked = new FileStream(LockCheckpointDuringFill, FileMode.Open, FileAccess.Read, FileShare.None);
                    Controller.ApplyFill(new ApmFill("callback-fill", intent.Id, intent.Time, intent.PriceBound, intent.Volume, 0));
                }
                if (ThrowAfterSend) throw new IOException("Synthetic lost ack");
                if (Immediate)
                {
                    Controller.ApplyFill(new ApmFill("fill-" + intent.Id, intent.Id, intent.Time, intent.PriceBound, intent.Volume, 0));
                    Controller.ApplyOrder(intent.Id, ApmOrderState.Filled, intent.Volume, "broker-" + intent.Id);
                }
            }

            public void Cancel(ApmIntent intent)
            {
                Cancels.Add(intent.Id);
                if (ThrowOnCancel) throw new IOException("Synthetic cancel lost ack");
            }
        }
    }
}
