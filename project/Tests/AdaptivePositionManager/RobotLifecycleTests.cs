using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using OsEngine.OsTrader.AdaptivePositionManager;
using OsEngine.Robots.MyBots;

namespace OsEngine.AdaptivePositionManager.Tests
{
    /// <summary>
    /// Offline robot terminal callback contract, including the Optimizer signature. Native servers and bot
    /// constructors are deliberately bypassed; these tests do not qualify a real Optimizer pass.
    /// </summary>
    internal static class RobotLifecycleTests
    {
        internal static void Run()
        {
            foreach (string mode in new[] { "complete", "open", "unstarted" })
            {
                string root = Path.Combine(Path.GetTempPath(), "APM-terminal-" + Guid.NewGuid().ToString("N"));
                ApmCampaign campaign = CoreTests.Enter();
                if (mode != "open")
                {
                    campaign.RequestExit("SESSION_EXIT");
                    CoreTests.FillDecision(campaign, campaign.OnMarket(CoreTests.Market(100, 1)));
                }
                using ApmArtifacts artifacts = new ApmArtifacts(Path.Combine(root, "0000"));
                using ApmExecutionController controller = new ApmExecutionController(campaign, new UnusedGateway(), null);
                ApmOsEngineAdapter adapter = (ApmOsEngineAdapter)RuntimeHelpers.GetUninitializedObject(typeof(ApmOsEngineAdapter));
                Set(adapter, "<Controller>k__BackingField", controller);
                AdaptivePositionResearchBot robot = (AdaptivePositionResearchBot)RuntimeHelpers.GetUninitializedObject(typeof(AdaptivePositionResearchBot));
                Set(robot, "_lifecycle", new object()); Set(robot, "_adapter", adapter); Set(robot, "_artifacts", artifacts);
                Set(robot, "_results", new List<ApmSnapshot>()); Set(robot, "_runDirectory", root); Set(robot, "_runId", "callback-contract");
                Set(robot, "_metrics", new List<ApmCampaignMetrics>());
                ApmCampaignSpec spec = CoreTests.Spec();
                ApmCampaignSpec next = spec with { CampaignId = "later", EntrySignalId = "later", EntryTime = spec.SessionExitTime.AddMinutes(1),
                    SessionExitTime = spec.SessionExitTime.AddHours(1) };
                Set(robot, "_schedule", new ApmSchedule(new ApmScheduleDocument("APM-Schedule-v1", "synthetic", new string('A', 64),
                    ApmDataProfile.TradeOnly, mode == "unstarted" ? new[] { spec, next } : new[] { spec })));
                ApmRunManifest manifest = new ApmRunManifest("APM-Schema-v1", "APM-Target-v0.1", "OFFLINE", "callback-contract",
                    new string('A', 64), "schedule", "UTC", "SyntheticFixture", "none", "none", "explicit", ApmDataProfile.TradeOnly,
                    new ApmPolicy(), new[] { spec }, 1, "Running");
                Set(robot, "_manifest", manifest); artifacts.WriteManifest(manifest);
                Invoke(robot, "Optimizer_TestingEndEvent", 1, TimeSpan.Zero);
                string first = File.ReadAllText(Path.Combine(root, "run-summary.json"));
                Invoke(robot, "FinalizeRun");
                Program.Equal(first, File.ReadAllText(Path.Combine(root, "run-summary.json")), "end/delete finalization is idempotent: " + mode);
                using JsonDocument summary = JsonDocument.Parse(first);
                Program.Equal(mode == "complete" ? "CompletedResearchOnly" : "Partial", summary.RootElement.GetProperty("CompletionStatus").GetString(),
                    "optimizer callback records terminal schedule status without another tick: " + mode);
                Program.Equal(mode == "open" ? 0 : 1, summary.RootElement.GetProperty("Completed").GetInt32(), "campaign archived once: " + mode);
                Program.Equal(mode == "unstarted" ? 1 : 0, summary.RootElement.GetProperty("Unstarted").GetInt32(), "omitted schedule rows retained: " + mode);
                using JsonDocument saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "0000", "manifest.json")));
                Program.Equal(mode == "open" ? "Partial" : "CompletedResearchOnly", saved.RootElement.GetProperty("CompletionStatus").GetString(),
                    "individual campaign preserves its actual status: " + mode);
            }
        }

        private static void Set(object target, string name, object value)
            => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
        private static void Invoke(object target, string name, params object[] args)
            => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, args);

        private sealed class UnusedGateway : IApmOrderGateway
        {
            public void Send(ApmIntent intent) => throw new InvalidOperationException("No execution in terminal callback fixture.");
            public void Cancel(ApmIntent intent) => throw new InvalidOperationException("No execution in terminal callback fixture.");
        }
    }
}
