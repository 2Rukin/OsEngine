using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using OsEngine.OsTrader.AdaptivePositionManager;

namespace OsEngine.AdaptivePositionManager.Tests
{
    /// <summary>Immutable scenario locks and actual byte-hash/data-quality validation on synthetic files.</summary>
    internal static class ScheduleTests
    {
        internal static void Run()
        {
            string directory = Path.Combine(Path.GetTempPath(), "APM-schedule-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string ticks = Path.Combine(directory, "synthetic.txt");
            File.WriteAllText(ticks, "20260925,100000,100,1,Buy,0,one\n20260925,100001,101,2,Unknown,0,two\n");
            ApmDatasetReport report = ApmDatasetValidation.Inspect(ticks, "fixture", "APM_SYNTH", true);
            Program.Equal("ValidatedTradeOnly", report.Status, "valid source-order synthetic dataset");
            Program.Equal(2L, report.Rows, "exact validation row count");
            Program.Equal(1L, report.UnknownSide, "unknown side retained and reported");
            ApmCampaignSpec[] specs = { CoreTests.Spec() };
            ApmScheduleDocument document = new ApmScheduleDocument("APM-Schedule-v1", "synthetic.txt", report.Hash,
                ApmDataProfile.TradeOnly, specs);
            ApmSchedule schedule = new ApmSchedule(document);
            string hash = schedule.Hash;
            specs[0] = specs[0] with { RiskBudgetCurrency = 1 };
            Program.Equal(1000m, schedule.Campaigns[0].RiskBudgetCurrency, "caller array cannot mutate frozen locks");
            schedule.VerifyDataset(ticks);
            string path = Path.Combine(directory, "schedule.json");
            schedule.Save(path);
            Program.Equal(hash, ApmSchedule.Load(path).Hash, "saved schedule canonical hash stable");
            string saved = File.ReadAllText(path);
            foreach (string missing in new[] { "HardStopPrice", "FinalTargetPrice", "RiskBudgetCurrency", "StopSlippageReserveTicks", "FeePerContract", "EntrySlippageReserveTicks" })
            {
                JsonObject incomplete = JsonNode.Parse(saved).AsObject();
                incomplete["Campaigns"][0].AsObject().Remove(missing);
                File.WriteAllText(path, incomplete.ToJsonString());
                DataTests.Throws(() => ApmSchedule.Load(path), "APM-UI-001 missing explicit lock rejected: " + missing);
            }
            JsonObject missingProfile = JsonNode.Parse(saved).AsObject();
            missingProfile.Remove("Profile");
            File.WriteAllText(path, missingProfile.ToJsonString());
            bool profileRejected = false;
            try { ApmSchedule.Load(path); } catch (JsonException) { profileRejected = true; }
            Program.Check(profileRejected, "APM-UI-001 profile cannot default silently to TradeOnly");
            File.WriteAllText(path, saved);
            Program.Equal(0m, ApmSchedule.Load(path).Campaigns[0].StopSlippageReserveTicks, "explicit valid zero reserve remains valid");
            ApmScheduleDocument valid = document with { Campaigns = new[] { CoreTests.Spec() } };
            foreach (ApmScheduleDocument bad in new[] {
                valid with { SchemaVersion = "unknown" }, valid with { Profile = ApmDataProfile.SampledBook },
                valid with { DatasetHash = "short" }, valid with { DatasetHash = new string('x', 64) },
                valid with { Campaigns = Array.Empty<ApmCampaignSpec>() },
                valid with { Campaigns = new[] { CoreTests.Spec(), CoreTests.Spec() } },
                valid with { Campaigns = new[] { CoreTests.Spec(), CoreTests.Spec() with { CampaignId = "two", EntrySignalId = "two" } } }
            }) DataTests.Throws(() => new ApmSchedule(bad), "invalid saved scenario cannot start");
            File.AppendAllText(ticks, "20260925,100000,100,1,Buy,0,one\n20260925,100002,999,1,Buy,0,two\n");
            DataTests.Throws(() => schedule.VerifyDataset(ticks), "changed source bytes cannot reuse manifest");
            report = ApmDatasetValidation.Inspect(ticks, "fixture", "APM_SYNTH", true);
            Program.Equal("InvalidForPerformance", report.Status, "late/conflicting input rejected for performance");
            Program.Equal(1L, report.CertifiedDuplicates, "certified exact duplicate counted");
            Program.Equal(1L, report.ConflictingIds, "conflicting source identity counted");
            Program.Equal(1L, report.OutOfOrder, "receive-order time regression counted");
            report = ApmDatasetValidation.Inspect(ticks, "fixture", "APM_SYNTH", true, 1);
            Program.Check(report.IdCapacityReached, "identity storage has an explicit bounded failure");
        }
    }
}
