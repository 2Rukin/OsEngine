using System;
using OsEngine.OsTrader.AdaptivePositionManager;

namespace OsEngine.AdaptivePositionManager.Tests
{
    /// <summary>Independent published-formula oracles, symmetry, numerical limits and no-future-data research boundaries.</summary>
    internal static class ResearchModelTests
    {
        internal static void Run()
        {
            Book(); Calibration(); Execution();
        }

        private static void Book()
        {
            DateTime time = CoreTests.Start;
            ApmBookQuote previous = new ApmBookQuote(time, 1, 100, 10, 102, 12);
            ApmBookQuote[] next = { previous with { Sequence = 2, BidSize = 13, AskSize = 9 },
                previous with { Sequence = 2, Bid = 101, BidSize = 7 }, previous with { Sequence = 2, Bid = 99, BidSize = 8 },
                previous with { Sequence = 2, Ask = 101, AskSize = 8 }, previous with { Sequence = 2, Ask = 103, AskSize = 8 } };
            decimal[] expected = { 6, 7, -10, -8, 12 };
            for (int i = 0; i < next.Length; i++)
            {
                Program.Equal(expected[i], ApmOrderBookResearch.EventOfi(previous, next[i]), "independent four-indicator OFI case " + i);
                Program.Equal(-expected[i], ApmOrderBookResearch.EventOfi(Mirror(previous), Mirror(next[i])), "OFI bid/ask mirror " + i);
            }
            ApmOrderBookResearch book = new ApmOrderBookResearch(ApmDataProfile.SampledBook, 2, TimeSpan.FromSeconds(2));
            Program.Check(!book.Observe(previous).Ready, "first quote only baseline");
            ApmBookFeature feature = book.Observe(next[0]);
            Program.Equal("SAMPLED_PROXY", feature.Quality, "sampled data never full-event OFI");
            Program.Equal(11m, feature.MeanDepth, "explicit average top depth units");
            Program.Equal(6m / 11m, feature.NormalizedOfi, "depth normalization exactly once");
            Program.Check(!book.Observe(next[0]).Ready, "duplicate/out-of-order invalidates continuity");
            Program.Check(!book.Observe(previous with { Sequence = 3, Bid = 103 }).Ready, "crossed book rejected");
            Program.Check(!book.Observe(previous with { Sequence = 4, AskSize = 0 }).Ready, "empty level rejected");
            book.Observe(previous with { Sequence = 5 });
            Program.Equal("STALE_GAP", book.Observe(previous with { Sequence = 6, Time = time.AddSeconds(3) }).Quality, "stale gap resets");
            Program.Equal(0m, book.Observe(previous with { Sequence = 7, Time = time.AddSeconds(4) }).EventOfi, "post-gap no invented impulse");
            book.Reset(); Program.Check(!book.Observe(previous).Ready, "reconnect resets baseline");
            DataTests.Throws(() => new ApmOrderBookResearch(ApmDataProfile.TradeOnly, 2, TimeSpan.FromSeconds(1)), "TradeOnly cannot invent book");
        }

        private static ApmBookQuote Mirror(ApmBookQuote quote) => quote with
            { Bid = 202 - quote.Ask, Ask = 202 - quote.Bid, BidSize = quote.AskSize, AskSize = quote.BidSize };

        private static void Calibration()
        {
            DateTime time = CoreTests.Start;
            ApmCausalCalibration impact = new ApmCausalCalibration(ApmCalibrationKind.ContemporaneousImpact, "quote-price/book-volume", time.AddSeconds(20));
            ApmMaturedObservation sample = new ApmMaturedObservation(time, time.AddSeconds(1), time, time.AddSeconds(1), time.AddSeconds(1), 2, 1);
            Program.Check(impact.Snapshot.Coefficient == null, "unidentified fit absent, not zero");
            DataTests.Throws(() => impact.Add(sample, time), "future label forbidden");
            impact.Add(sample, time.AddSeconds(1));
            Program.Equal(0.5m, impact.Snapshot.Coefficient.Value, "contemporaneous coefficient units");
            DataTests.Throws(() => impact.Add(sample, time.AddSeconds(3)), "duplicate/overlapping calibration window forbidden");
            ApmCausalCalibration predictive = new ApmCausalCalibration(ApmCalibrationKind.LaggedPrediction, "ticks/book-volume", time.AddSeconds(20));
            DataTests.Throws(() => predictive.Add(sample, time.AddSeconds(2)), "contemporaneous effect is not predictive alpha");
            ApmMaturedObservation lagged = sample with { ResponseStart = time.AddSeconds(2), ResponseEnd = time.AddSeconds(3), ObservedAt = time.AddSeconds(3) };
            predictive.Add(lagged, time.AddSeconds(3));
            Program.Equal(time.AddSeconds(3), predictive.Snapshot.TrainingCutoff, "last matured response is actual cutoff");
            DataTests.Throws(() => predictive.Add(lagged with { ResponseStart = time.AddSeconds(21), ResponseEnd = time.AddSeconds(22), ObservedAt = time.AddSeconds(22) }, time.AddSeconds(22)), "no implicit OOS model update");
            ApmDemandCalibration demand = new ApmDemandCalibration(time.AddSeconds(10));
            Program.Check(demand.Predict(1) == null, "uncalibrated demand absent");
            demand.Add(time.AddSeconds(1), time.AddSeconds(1), time.AddSeconds(1), true, 1, 1);
            demand.Add(time.AddSeconds(2), time.AddSeconds(2), time.AddSeconds(2), true, 3, 3);
            Program.Equal(3m, demand.Predict(1).Value, "joint E[cp] oracle; product-of-means would wrongly give2");
            DataTests.Throws(() => demand.Add(time.AddSeconds(3), time.AddSeconds(4), time.AddSeconds(3), true, 1, 1), "future demand unavailable");
            Program.Equal(3m, demand.Predict(1).Value, "rejected update leaves previous model unchanged");
            Program.Equal(-1m, ApmResearchExecution.LinearDemand(true, 1, 1, 2), "negative linear reference is not silently clipped");
            Program.Equal(0m, ApmResearchExecution.LinearDemand(false, 1, 1, 2), "no arrival no demand");
        }

        private static void Execution()
        {
            Program.Equal(99.6m, ApmResearchExecution.ReservationPrice(100, 2, 0.1m, 0.2m, 10), "AS quote-price reference");
            Program.Equal(100.4m, ApmResearchExecution.ReservationPrice(100, -2, 0.1m, 0.2m, 10), "AS signed inventory mirror");
            Program.Equal(98m, ApmResearchExecution.ReservationPrice(100, 2, 0.1m, 0.2m, 10, 5), "futures-unit adaptation uses one multiplier");
            double[] inventory = ApmResearchExecution.DiscreteInventory(12, 3, 3, 1, 1, 1, 0);
            Near(12, inventory[0], "AC initial"); Near(4.5, inventory[1], "AC discrete equation16 oracle");
            Near(1.5, inventory[2], "AC discrete equation17 oracle"); Near(0, inventory[3], "AC terminal");
            double[] linear = ApmResearchExecution.DiscreteInventory(12, 3, 3, 0, 1, 1, 0);
            Near(8, linear[1], "AC kappa0 first"); Near(4, linear[2], "AC kappa0 second");
            double[] tiny = ApmResearchExecution.DiscreteInventory(12, 3, 3, 1e-20, 1, 1, 0);
            Near(8, tiny[1], "AC stable near-zero");
            double[] large = ApmResearchExecution.DiscreteInventory(12, 10000, 100, 1e10, 1, 1, 0);
            for (int j = 0; j < large.Length; j++) Program.Check(double.IsFinite(large[j]) && large[j] >= 0 && large[j] <= 12, "AC stable large kappaT");
            double[] extreme = ApmResearchExecution.DiscreteInventory(12, 1e308, 3, 1e-300, 1, 1, 0);
            for (int j = 0; j < extreme.Length; j++) Program.Check(double.IsFinite(extreme[j]) && extreme[j] >= 0 && extreme[j] <= 12,
                "APM-T10-002 finite extreme horizon trajectory before entry");
            new ApmPolicy { ResearchExecution = new ApmAcSettings(3, 1e308, 1e-300, 1, 1, 0, "EXTREME-SYNTHETIC", CoreTests.Start.AddDays(-1)) }.Validate(CoreTests.Spec());
            DataTests.Throws(() => ApmResearchExecution.DiscreteInventory(12, 3, 3, 1, 1, 1, 2), "AC etaTilde positive required");
            DataTests.Throws(() => ApmResearchExecution.DiscreteInventory(12, 3, 3, -1, 1, 1, 0), "negative risk aversion forbidden");
            ApmAcExecutionPlanner planner = new ApmAcExecutionPlanner(new ApmAcSettings(3, 3, 0, 1, 1, 0, "SYNTHETIC-v1", CoreTests.Start.AddSeconds(-1)));
            ApmCampaign campaign = CoreTests.Enter();
            ApmDecision decision = campaign.OnMarket(CoreTests.Market(104, 1));
            Program.Equal(1m, planner.AllowedVolume(decision, campaign.Snapshot, 1), "ordinary4-volume target split into on-grid first bucket");
            Program.Equal(0m, planner.AllowedVolume(decision, campaign.Snapshot with { PendingReduce = 1 }, 1), "pending child deducted");
            campaign.RequestExit("HARD_STOP");
            ApmDecision exit = campaign.OnMarket(CoreTests.Market(89, 2));
            Program.Equal(exit.Volume, planner.AllowedVolume(exit, campaign.Snapshot, 1), "protective exit bypasses schedule");
        }

        private static void Near(double expected, double actual, string name) => Program.Check(Math.Abs(expected - actual) <= 1e-11, name);
    }
}
