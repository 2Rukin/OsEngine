using System;
using System.Linq;
using System.Text.Json.Nodes;
using OsEngine.OsTrader.AdaptivePositionManager;

namespace OsEngine.AdaptivePositionManager.Tests
{
    /// <summary>Invalid locks, estimator units and independently corrupted persistence evidence.</summary>
    internal static class ContractTests
    {
        internal static void Run()
        {
            ApmCampaignSpec good = CoreTests.Spec();
            ApmCampaignSpec[] invalid = {
                good with { SchemaVersion = "unknown" }, good with { CampaignId = "" }, good with { EntrySignalId = "" },
                good with { Instrument = "" }, good with { Account = "" }, good with { Timezone = "" },
                good with { RiskCurrency = "" }, good with { ValuationCurrency = "USD" }, good with { Direction = 0 },
                good with { EntryTime = default }, good with { SessionExitTime = good.EntryTime },
                good with { SessionExitTime = good.EntryTime.AddDays(1) }, good with { PriceStep = 0 },
                good with { VolumeStep = 0 }, good with { PriceStepCost = 0 }, good with { RiskBudgetCurrency = 0 },
                good with { InitialVolume = 0 }, good with { MaxVolume = 9 }, good with { InitialVolume = 10.5m },
                good with { MaxVolume = 20.5m }, good with { HardStopPrice = 100 }, good with { FinalTargetPrice = 100 },
                good with { StopSlippageReserveTicks = -1 }, good with { EntrySlippageReserveTicks = -1 }, good with { FeePerContract = -1 }
            };
            foreach (ApmCampaignSpec spec in invalid) DataTests.Throws(spec.Validate, "invalid operator lock rejected");
            ApmPolicy p = new ApmPolicy();
            ApmPolicy[] policies = {
                p with { ModelVersion = "unknown" }, p with { AddScale = 0 }, p with { ReduceScale = 0 },
                p with { MinimumScaleTicks = 0 }, p with { AddVolatilityFactor = 0 }, p with { ReduceVolatilityFactor = 0 },
                p with { SpeedScaleFactor = -1 }, p with { MaximumScaleMultiplier = 0.9m }, p with { InventoryPenalty = -1 },
                p with { RiskHorizonSeconds = 0 }, p with { MinActiveVolume = 0 }, p with { MinActiveVolume = 11 },
                p with { MinActiveVolume = 1.5m }, p with { MinRebalanceVolume = 0 }, p with { MinRebalanceVolume = 1.5m },
                p with { VolumeDeadband = -1 }, p with { MaxChildVolume = 0 }, p with { MaxChildVolume = 1.5m },
                p with { NoAddFraction = 0 }, p with { NoAddFraction = 1 }, p with { RearmMinTicks = -1 },
                p with { RearmVolatilityFactor = -1 }, p with { MinActionIntervalSeconds = -1 }, p with { FavorableOn = 1 },
                p with { FavorableOff = -1 }, p with { AdverseOn = 1 }, p with { AdverseOff = -1 },
                p with { MinRegimeHoldSeconds = -1 }, p with { MaxFavorableDeferSeconds = 0 }, p with { GridSeconds = 0 },
                p with { WarmupSeconds = 0 }, p with { MinSamples = 0 }, p with { MinSamples = 100001 },
                p with { MaxPriceAgeSeconds = 0 }, p with { FastHalfLifeSeconds = 0 }, p with { SlowHalfLifeSeconds = 10 },
                p with { SpeedWindowSeconds = 0 }, p with { GridSeconds = 3, SpeedWindowSeconds = 10 },
                p with { SpeedWindowSeconds = 100001 }, p with { SigmaFloor = 0 }, p with { OrderLifetimeSeconds = 0 }
            };
            foreach (ApmPolicy policy in policies) DataTests.Throws(() => policy.Validate(good), "invalid optimizer combination rejected");
            EstimatorUnits();
            RecoveryIntegrity();
        }

        private static void EstimatorUnits()
        {
            ApmCampaignSpec spec = CoreTests.Spec();
            DataTests.Throws(() => ApmMathematics.Quantize(-1, 1), "negative inventory quantization rejected");
            DataTests.Throws(() => ApmMathematics.Quantize(1, 0), "zero lot rejected");
            DataTests.Throws(() => ApmMathematics.Floor(-1, 1), "negative risk capacity rejected");
            DataTests.Throws(() => ApmMathematics.Floor(1, 0), "zero risk lot rejected");
            DataTests.Throws(() => ApmMathematics.Sqrt(-1), "negative variance rejected");
            DataTests.Throws(() => ApmMathematics.Curve(spec, 100, 10, 100, 0, 1), "zero adverse scale rejected");
            DataTests.Throws(() => ApmMathematics.Curve(spec, 100, 10, 100, 1, 0), "zero favorable scale rejected");
            DataTests.Throws(() => ApmMathematics.Curve(spec, 100, 0, 100, 1, 1), "zero initial curve rejected");
            DataTests.Throws(() => ApmMathematics.Curve(spec, 100, 21, 100, 1, 1), "curve exceeds lock rejected");
            DataTests.Throws(() => ApmMathematics.Target(-1, 20, 0, 0), "negative curve rejected");
            DataTests.Throws(() => ApmMathematics.Target(1, 0, 0, 0), "zero minimizer cap rejected");
            DataTests.Throws(() => ApmMathematics.Target(1, 20, -1, 0), "negative initial penalty rejected");
            DataTests.Throws(() => ApmMathematics.Target(1, 20, 0, -1), "negative current penalty rejected");
            ApmPolicy policy = new ApmPolicy { FixedScales = false, SpeedWindowSeconds = 4, InventoryPenalty = 2 };
            ApmMarket market = CoreTests.Market(100, 0) with { SlowVariance = 4, Speed = 2 };
            Program.Equal((8m, 12m), ApmMathematics.Scales(spec, policy, market), "sqrt(price squared/sec * sec) and asymmetric speed");
            Program.Equal((12m, 8m), ApmMathematics.Scales(spec, policy, market with { Speed = -2 }), "mirrored adverse multiplier");
            Program.Equal((8m, 24m), ApmMathematics.Scales(spec, policy, market with { Speed = 100 }), "bounded adaptive multiplier");
            Program.Equal((1m, 1m), ApmMathematics.Scales(spec, policy, market with { SlowVariance = 0, Speed = 0 }), "minimum price tick scale");
            Program.Equal(0.96m, ApmMathematics.Kappa(spec, policy, market), "dimensionless penalty independent arithmetic");
            Program.Equal(0m, ApmMathematics.Kappa(spec, policy, market with { Time = spec.SessionExitTime }), "zero remaining horizon");
            DataTests.Throws(() => ApmMathematics.Kappa(spec, policy, market with { SlowVariance = -1 }), "negative variance kappa rejected");
            ApmCampaign constant = CoreTests.Enter(null, new ApmPolicy { ConstantInventory = true });
            Program.Equal(ApmAction.Wait, constant.OnMarket(CoreTests.Market(106, 1)).Action, "B0 preserves initial quantity before target");
            Program.Equal(ApmAction.Exit, constant.OnMarket(CoreTests.Market(110, 2)).Action, "B0 respects same immutable target");
        }

        private static void RecoveryIntegrity()
        {
            ApmCampaign campaign = CoreTests.Enter(CoreTests.Spec() with { FeePerContract = 0.1m });
            CoreTests.FillDecision(campaign, campaign.OnMarket(CoreTests.Market(104, 1)));
            CoreTests.FillDecision(campaign, campaign.OnMarket(CoreTests.Market(102, 2)));
            Program.Equal(26.4m, campaign.Equity(102), "reduce/readd independent fee-adjusted result");
            string json = campaign.ExportCheckpoint();
            ApmCampaign restored = ApmCampaign.Recover(json);
            Program.Equal(campaign.Snapshot.FilledVolume, restored.Snapshot.FilledVolume, "recovered mixed ledger quantity");
            Program.Equal(campaign.Snapshot.AverageEntry, restored.Snapshot.AverageEntry, "recovered remaining basis");
            Program.Equal(campaign.Equity(102), restored.Equity(102), "recovered mixed ledger equity");
            foreach (string field in new[] { "Quantity", "Average", "Realized", "Fees", "SignedCash" })
            {
                JsonObject corrupted = JsonNode.Parse(json).AsObject();
                corrupted[field] = corrupted[field].GetValue<decimal>() + 1;
                DataTests.Throws(() => ApmCampaign.Recover(corrupted.ToJsonString()), "forged aggregate " + field);
            }
            string intentId = campaign.Intents[0].Id;
            foreach (string field in new[] { "Filled", "FillNotional", "ReportedFilled", "Volume", "State", "Action" })
            {
                JsonObject corrupted = JsonNode.Parse(json).AsObject();
                corrupted["Intents"][intentId][field] = field == "Volume" ? 0 : 9999;
                DataTests.Throws(() => ApmCampaign.Recover(corrupted.ToJsonString()), "forged intent " + field);
            }
            foreach (string field in new[] { "Id", "IntentId" })
            {
                JsonObject corrupted = JsonNode.Parse(json).AsObject();
                JsonNode fill = corrupted["Fills"].AsObject().First().Value;
                fill[field] = "foreign";
                DataTests.Throws(() => ApmCampaign.Recover(corrupted.ToJsonString()), "forged fill " + field);
            }
            DataTests.Throws(() => ApmCampaign.Recover("{}"), "missing schema cannot recover");
            CoreTests.FillDecision(campaign, campaign.OnMarket(CoreTests.Market(110, 3)));
            restored = ApmCampaign.Recover(campaign.ExportCheckpoint());
            Program.Check(restored.Snapshot.ExitLatch && restored.Reconcile(0, true, true), "recovered terminal actual-flat evidence");
            Program.Equal(ApmState.Completed, restored.Snapshot.State, "fully exited ledger recovery");
        }
    }
}
