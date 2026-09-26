using System;
using System.IO;
using OsEngine.OsTrader.AdaptivePositionManager;

namespace OsEngine.AdaptivePositionManager.Tests
{
    /// <summary>Offline parser, causal statistics and persistence fault tests; synthetic records only.</summary>
    internal static class DataTests
    {
        internal static void Run()
        {
            ApmTick tick = ApmTickReader.Parse("20260925,100000,-10.125,2,None,0,9007199254740993", "fixture", "APM_SYNTH", 1);
            Program.Equal(-10.125m, tick.Price, "valid negative instrument price");
            Program.Equal(0, tick.Side, "Unknown Side is not Sell");
            Program.Equal("9007199254740993", tick.Id, "string ID exact");
            Throws(() => ApmTickReader.Parse("20260925,100000,100,-2,Buy,0,id", "f", "s", 1), "negative volume rejected");
            Throws(() => ApmTickReader.Parse("20260925,100000,100,2,Buy,1000000,id", "f", "s", 1), "micros out of range");
            Throws(() => ApmTickReader.Parse("20260925,100000,NaN,2,Buy,0,id", "f", "s", 1), "nonfinite price rejected");
            ApmTickIdentityGuard guard = new ApmTickIdentityGuard(true, 2);
            Program.Equal("ACCEPT", guard.Inspect(tick), "first certified ID");
            Program.Equal("DUPLICATE", guard.Inspect(tick with { ReceiveSequence = 2 }), "exact duplicate ID");
            Program.Equal("CONFLICT", guard.Inspect(tick with { Price = 11 }), "conflicting identity");
            Program.Equal("ACCEPT", guard.Inspect(tick with { Id = "two" }), "second identity");
            Program.Equal("CAPACITY", guard.Inspect(tick with { Id = "three" }), "bounded identity storage fails closed");
            Program.Equal("ID_UNCERTIFIED", new ApmTickIdentityGuard(false).Inspect(tick), "no guessed deduplication");

            ApmPolicy policy = new ApmPolicy { WarmupSeconds = 2, MinSamples = 2, SpeedWindowSeconds = 2, MaxPriceAgeSeconds = 2 };
            ApmFeatures features = new ApmFeatures(CoreTests.Spec(), policy);
            ApmMarket first = features.OnTick(Tick(100, 0));
            Program.Check(!first.Ready, "warmup missing is not zero volatility readiness");
            features.OnTick(Tick(101, 1));
            ApmMarket third = features.OnTick(Tick(102, 2));
            Program.Check(third.Ready, "fresh grid warmup complete");
            Program.Equal(1m, third.SlowVariance, "independent warmup variance");
            Program.Check(!features.OnTimer(CoreTests.Start.AddSeconds(10), 4).Ready, "stale timer resets warmup");
            Program.Check(!features.OnTick(Tick(103, 11, 5)).Ready, "gap rewarms");
            Program.Check(!features.OnTick(Tick(105, 10, 6)).Ready, "late event rejected");
            features.Reset();
            Program.Check(!features.OnTick(Tick(100, 0)).Ready, "session reset");
            ApmFeatures prefix = new ApmFeatures(CoreTests.Spec(), policy);
            ApmFeatures whole = new ApmFeatures(CoreTests.Spec(), policy);
            for (int i = 0; i < 50; i++)
                Program.Equal(prefix.OnTick(Tick(100 + i % 3, i)), whole.OnTick(Tick(100 + i % 3, i)), "prefix causal feature equality");
            whole.OnTick(Tick(999, 51));
            Program.Check(prefix.OnTick(Tick(101, 50)).Price == 101, "future suffix cannot change prefix");
            ApmFeatures between = new ApmFeatures(CoreTests.Spec(), policy);
            between.OnTick(Tick(100, 0));
            ApmTick fractional = Tick(200, 2) with { Time = CoreTests.Start.AddMilliseconds(2500) };
            ApmMarket fractionalSnapshot = between.OnTick(fractional);
            Program.Equal(0m, fractionalSnapshot.SlowVariance, "later tick cannot enter earlier grid boundaries");
            ApmFeatures clocked = new ApmFeatures(CoreTests.Spec(), policy with { MaxPriceAgeSeconds = 10 });
            clocked.OnTick(Tick(100, 0));
            clocked.OnTick(Tick(101, 1));
            clocked.OnTick(Tick(102, 2));
            Program.Check(clocked.OnTimer(CoreTests.Start.AddSeconds(4), 4).Ready, "timer advances valid causal features");
            ApmMarket late = clocked.OnTick(Tick(103, 3, 5));
            Program.Equal("OUT_OF_ORDER", late.Quality, "APM-CORE-002 tick behind processed timer rejected");
            Program.Check(!late.Ready, "APM-CORE-002 future grid cannot be exposed as ready");
            Program.Equal("OUT_OF_ORDER", clocked.OnTimer(CoreTests.Start.AddSeconds(5), 6).Quality,
                "APM-CORE-002 rejection remains latched for timers");
            Program.Check(!clocked.OnTick(Tick(104, 6, 7)).Ready, "APM-CORE-002 rejection requires explicit reset");
            clocked.Reset();
            clocked.OnTick(Tick(100, 0)); clocked.OnTick(Tick(101, 1));
            Program.Check(clocked.OnTick(Tick(102, 2)).Ready, "APM-CORE-002 explicit reset restores feature stream");
            clocked.Reset();
            clocked.OnTimer(CoreTests.Start.AddSeconds(4), 1);
            Program.Equal("OUT_OF_ORDER", clocked.OnTick(Tick(100, 3, 2)).Quality, "clock before first trade is causal");

            string directory = Path.Combine(Path.GetTempPath(), "APM-artifacts-" + Guid.NewGuid().ToString("N"));
            using (ApmArtifacts artifacts = new ApmArtifacts(directory, 2))
            {
                ApmCampaign campaign = CoreTests.Enter();
                campaign.RequestExit("OWNER_EXIT");
                artifacts.SaveCheckpoint(campaign);
                ApmCampaign restored = ApmArtifacts.LoadCheckpoint(Path.Combine(directory, "checkpoint.json"));
                Program.Check(restored.Snapshot.ExitLatch, "checksummed terminal recovery");
                artifacts.Append(new ApmAuditRow(1, CoreTests.Start, "s", "Intent", "ADD", 100, 2, 10, 12, 12, "TEST", "i"));
                Program.Equal(1, artifacts.Recent().Length, "bounded audit view");
                ApmArtifacts.ExportCsv(Path.Combine(directory, "export.csv"), artifacts.Recent());
                Program.Check(File.ReadAllText(Path.Combine(directory, "export.csv")).Contains("\"100\",\"2\""), "numeric export units");
                File.WriteAllText(Path.Combine(directory, "checkpoint.json"), "{}");
                Throws(() => ApmArtifacts.LoadCheckpoint(Path.Combine(directory, "checkpoint.json")), "bad recovery envelope");
            }
            // Evidence is retained under the uniquely named temporary test directory.
        }

        private static ApmTick Tick(decimal price, int seconds, long sequence = 0)
            => new ApmTick("fixture", "APM_SYNTH", "20260925", CoreTests.Start.AddSeconds(seconds),
                sequence > 0 ? sequence : seconds + 1, seconds.ToString(), price, 1, 1, 0);

        internal static void Throws(Action action, string name)
        {
            bool threw = false;
            try { action(); } catch (ArgumentException) { threw = true; }
            catch (FormatException) { threw = true; } catch (InvalidDataException) { threw = true; }
            Program.Check(threw, name);
        }
    }
}
