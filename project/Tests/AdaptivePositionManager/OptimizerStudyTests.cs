using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using OsEngine.Entity;
using OsEngine.Market.Servers.Optimizer;
using OsEngine.Market.Servers.Tester;
using OsEngine.OsTrader.AdaptivePositionManager;

namespace OsEngine.AdaptivePositionManager.Tests
{
    /// <summary>Independent bounds, fixed-value and actual native metadata roundtrip regressions; no server threads.</summary>
    internal static class OptimizerStudyTests
    {
        internal static void Run()
        {
            ApmCampaignSpec first = CoreTests.Spec();
            ApmCampaignSpec second = first with { CampaignId = "second", EntrySignalId = "second",
                EntryTime = first.EntryTime.AddDays(1), SessionExitTime = first.SessionExitTime.AddDays(1) };
            ApmSchedule schedule = new ApmSchedule(new ApmScheduleDocument("APM-Schedule-v1", "synthetic", new string('A', 64),
                ApmDataProfile.TradeOnly, new[] { first, second }));
            ApmStudyPhase[] phases = { new ApmStudyPhase("IS", first.EntryTime.AddSeconds(-35), first.SessionExitTime.AddSeconds(5), false),
                new ApmStudyPhase("OOS", second.EntryTime.AddSeconds(-35), second.SessionExitTime.AddSeconds(5), true) };
            Program.Equal("second", schedule.SelectPhase(phases[1].Start, phases[1].End, 30).Campaigns.Single().CampaignId, "phase selects OOS only");
            DataTests.Throws(() => schedule.SelectPhase(first.EntryTime, first.SessionExitTime, 30), "phase cannot cut warmup or final fill");
            List<IIStrategyParameter> parameters = Parameters();
            List<bool> flags = parameters.Select(p => false).ToList();
            ((StrategyParameterDecimal)parameters[1]).ValueDecimal = 8;
            ApmOptimizerStudy study = new ApmOptimizerStudy(schedule, phases, parameters, flags);
            Program.Equal(2, study.Trials.Count, "single-pass study has one trial per phase");
            Program.Equal(8m, ((StrategyParameterDecimal)parameters[1]).ValueDecimalDefolt, "fixed current normalized to native fixed column");
            Program.Check(flags[0], "singleton checked native axis");
            Program.Equal(5m, ((StrategyParameterDecimal)parameters[0]).ValueDecimalStop, "singleton native stop");
            parameters = Parameters(); flags = parameters.Select(p => false).ToList();
            parameters[0] = new StrategyParameterDecimal("Add scale price", 5, 3, 7, 2); flags[0] = true;
            study = new ApmOptimizerStudy(schedule, phases, parameters, flags);
            Program.Equal(6, study.Trials.Count, "three combinations retained across phases");
            Program.Equal("3,5,7", string.Join(",", study.Trials.Where(t => t.Phase == "IS").Select(t => t.Policy.AddScale)), "exact frozen candidates");
            parameters[0] = new StrategyParameterDecimal("Add scale price", 5, 1, 10, 1);
            DataTests.Throws(() => new ApmOptimizerStudy(schedule, phases, parameters, flags), "reject more than five values");
            parameters[0] = new StrategyParameterDecimal("Add scale price", 5, 0, 2, 1);
            DataTests.Throws(() => new ApmOptimizerStudy(schedule, phases, parameters, flags), "invalid policy rejected before native run");
            parameters[0] = new StrategyParameterDecimal("RiskBudget", 5, 3, 7, 2);
            DataTests.Throws(() => new ApmOptimizerStudy(schedule, phases, parameters, flags), "locks never searchable");
            Metadata();
            PartialReport();
            RollingPhases(first);
        }

        private static void RollingPhases(ApmCampaignSpec first)
        {
            ApmCampaignSpec[] campaigns = Enumerable.Range(0, 4).Select(day => first with {
                CampaignId = "rolling" + day, EntrySignalId = "rolling" + day,
                EntryTime = first.EntryTime.AddDays(day), SessionExitTime = first.SessionExitTime.AddDays(day) }).ToArray();
            ApmSchedule schedule = new ApmSchedule(new ApmScheduleDocument("APM-Schedule-v1", "rolling-synthetic", new string('A', 64),
                ApmDataProfile.TradeOnly, campaigns));
            ApmStudyPhase[] phases = {
                new ApmStudyPhase("IS1", campaigns[0].EntryTime.AddSeconds(-35), campaigns[0].SessionExitTime.AddSeconds(5), false),
                new ApmStudyPhase("OOS1", campaigns[1].EntryTime.AddSeconds(-35), campaigns[1].SessionExitTime.AddSeconds(5), true),
                new ApmStudyPhase("IS2", campaigns[0].EntryTime.AddSeconds(-35), campaigns[2].SessionExitTime.AddSeconds(5), false),
                new ApmStudyPhase("OOS2", campaigns[3].EntryTime.AddSeconds(-35), campaigns[3].SessionExitTime.AddSeconds(5), true) };
            List<IIStrategyParameter> parameters = Parameters();
            List<bool> flags = parameters.Select(p => false).ToList();
            Program.Equal(4, new ApmOptimizerStudy(schedule, phases, parameters, flags).Trials.Count,
                "advancing WFO permits training reuse of past OOS, not future OOS");
            Program.Equal(3, schedule.SelectPhase(phases[2].Start, phases[2].End, 30).Campaigns.Count,
                "rolling training contains three complete past campaigns");
            ApmStudyPhase[] overlap = (ApmStudyPhase[])phases.Clone();
            overlap[3] = phases[1] with { Name = "OOS2" };
            DataTests.Throws(() => new ApmOptimizerStudy(schedule, overlap, parameters, flags), "repeated OOS interval rejected");
            ApmStudyPhase[] future = (ApmStudyPhase[])phases.Clone();
            future[2] = phases[2] with { End = phases[3].End };
            DataTests.Throws(() => new ApmOptimizerStudy(schedule, future, parameters, flags), "training cannot contain future OOS");
        }

        private static List<IIStrategyParameter> Parameters() => new List<IIStrategyParameter> {
            new StrategyParameterDecimal("Add scale price", 5, 1, 9, 2), new StrategyParameterDecimal("Reduce scale price", 10, 6, 14, 2),
            new StrategyParameterDecimal("Inventory gamma", 0, 0, 1, 0.25m), new StrategyParameterDecimal("Rearm volatility factor", 0, 0, 1, 0.25m),
            new StrategyParameterBool("B0 constant inventory", false), new StrategyParameterBool("Volatility scales", false),
            new StrategyParameterBool("FAST enabled", false) };

        private static void Metadata()
        {
            CultureInfo savedCulture = CultureInfo.CurrentCulture;
            try
            {
                foreach (string culture in new[] { "ru-RU", "en-US" })
                {
                    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                    string folder = Path.Combine(Path.GetTempPath(), "APM-metadata-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(folder);
                    string file = Path.Combine(folder, "SecuritiesSettings.txt");
                    string neighbor = "OTHER$1$1$1$1$2$12.5$09/26/2026 00:00:00$0.5$0.25$Futures$future-tail";
                    File.WriteAllLines(file, new[] { neighbor, "OLD5$1$1$1$1", "OLD6$1$1$1$1$0", "OLD7$1$1$1$1$0$12.5" });
                    Security sample = new Security { Name = "SYNTH", NameClass = "TestClass", Lot = 1, PriceStep = 1,
                        PriceStepCost = 1, DecimalsVolume = 2, MarginSell = 12.5m, Expiration = new DateTime(2026, 9, 26),
                        VolumeStep = 0.25m, MinTradeAmount = 0.5m, SecurityType = SecurityType.Futures };
                    OptimizerDataStorage storage = Storage(folder, new[] { sample });
                    storage.SaveSecurityDopSettings(sample);
                    Program.Check(File.ReadAllLines(file).Contains(neighbor), "unrelated future metadata tail preserved " + culture);
                    Security[] fresh = new[] { "SYNTH", "OTHER", "OLD5", "OLD6", "OLD7" }
                        .Select(name => new Security { Name = name, NameClass = "TestClass" }).ToArray();
                    storage = Storage(folder, fresh);
                    typeof(OptimizerDataStorage).GetMethod("SetToSecuritiesDopSettings", BindingFlags.NonPublic | BindingFlags.Instance)
                        .Invoke(storage, new object[] { folder });
                    foreach (Security security in fresh.Take(2))
                    {
                        Program.Equal(0.25m, security.VolumeStep, "reloaded explicit step " + culture);
                        Program.Equal(0.5m, security.MinTradeAmount, "reloaded explicit minimum " + culture);
                        Program.Equal(12.5m, security.MarginSell, "fractional sell margin " + culture);
                        Program.Equal(new DateTime(2026, 9, 26), security.Expiration, "invariant expiration " + culture);
                        Program.Equal(SecurityType.Futures, security.SecurityType, "security type " + culture);
                    }
                    Program.Equal(0.25m, storage.SecuritiesTester[0].Security.VolumeStep, "replay metadata copy");
                    foreach (Security security in fresh.Skip(2)) Program.Equal(0m, security.VolumeStep, "legacy row does not invent volume step");
                    Program.Equal(12.5m, fresh[4].MarginSell, "legacy fractional margin");
                }
            }
            finally { CultureInfo.CurrentCulture = savedCulture; }
        }

        private static OptimizerDataStorage Storage(string folder, Security[] securities)
        {
            OptimizerDataStorage storage = (OptimizerDataStorage)RuntimeHelpers.GetUninitializedObject(typeof(OptimizerDataStorage));
            storage.PathToFolder = folder;
            typeof(OptimizerDataStorage).GetField("_sourceDataType", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(storage, TesterSourceDataType.Folder);
            storage.Securities = securities.ToList();
            storage.SecuritiesTester = securities.Select(s => new SecurityTester { Security = new Security { Name = s.Name, NameClass = s.NameClass } }).ToList();
            return storage;
        }

        private static void PartialReport()
        {
            string folder = Path.Combine(Path.GetTempPath(), "APM-partial-report-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            ApmSnapshot closed = CoreTests.Enter().Snapshot with { CampaignId = "closed", State = ApmState.Completed, Equity = 100, FilledVolume = 0 };
            ApmSnapshot open = closed with { CampaignId = "open", State = ApmState.Active, Equity = -200, FilledVolume = 10 };
            ApmCampaignMetrics gain = new ApmCampaignMetrics(100, 1, 2, 20, 10, 5, 10, 20);
            ApmCampaignMetrics loss = new ApmCampaignMetrics(-200, 201, 3, 10, 10, 10, 20, 20);
            object[] trials = {
                Trial("mixed", new ApmPolicy(), new[] { closed }, new[] { gain }, open, loss),
                Trial("single-open", new ApmPolicy(), Array.Empty<ApmSnapshot>(), Array.Empty<ApmCampaignMetrics>(), open, loss),
                Trial("closed", new ApmPolicy(), new[] { closed }, new[] { gain }, closed, gain),
                Trial("missing", new ApmPolicy(), new[] { closed }, new[] { gain }, open, null),
                Trial("rearm", new ApmPolicy { RearmVolatilityFactor = 0.5m }, new[] { closed }, new[] { gain }, closed, null) };
            string file = Path.Combine(folder, "all-trials.json");
            File.WriteAllText(file, JsonSerializer.Serialize(new { SchemaVersion = "APM-AllTrials-v1", Results = trials }));
            ApmStudyReportRow[] rows = ApmStudyReportWindow.Read(file);
            Program.Equal(-100m, rows[0].Net.Value, "APM-T08-001 completed gain + open loss included");
            Program.Equal(201m, rows[0].MaximumDrawdown.Value, "APM-T08-001 open drawdown included");
            Program.Equal(-200m, rows[1].Net.Value, "APM-T08-001 single open metric retained");
            Program.Equal(100m, rows[2].Net.Value, "APM-T08-001 archived current never double counted");
            Program.Check(rows[3].Net == null, "APM-T08-001 missing open metric not completed-only profit");
            Program.Equal("IncompleteOpenMetrics", rows[3].MetricsStatus, "APM-T08-001 missing completeness visible");
            Program.Equal(0.5m, rows[4].RearmVolatilityFactor, "APM-T08-002 rearm axis retained");
            Program.Check(rows[4].PolicyHash != rows[2].PolicyHash, "APM-T08-002 stable trial identity distinguishes rearm");
            string csv = Path.Combine(folder, "report.csv"); ApmStudyReportWindow.ExportCsv(csv, rows);
            string exported = File.ReadAllText(csv);
            Program.Check(exported.Contains("RearmVolatilityFactor") && exported.Contains(rows[4].PolicyHash)
                && exported.Contains("\"0.5\""), "APM-T08-002 CSV preserves configuration and hash");
        }

        private static object Trial(string phase, ApmPolicy policy, ApmSnapshot[] campaigns, ApmCampaignMetrics[] metrics,
            ApmSnapshot current, ApmCampaignMetrics currentMetrics) => new { Phase = phase, Status = "Partial",
                PolicyHash = ApmTickReader.HashJson(policy), Policy = policy,
                Runs = new[] { new { Campaigns = campaigns, Metrics = metrics, Current = current,
                    CurrentMetrics = currentMetrics, Completed = campaigns.Length } } };
    }
}
