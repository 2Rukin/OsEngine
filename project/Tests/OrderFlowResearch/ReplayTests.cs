/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.Entity;
using OsEngine.OsData.OrderFlow;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Xml.Linq;

namespace OsEngine.OrderFlowResearch.Tests
{
    internal static partial class Program
    {
        private static void TestReplayClock(string root)
        {
            double wall = 0;
            OrderFlowReplayClock clock = new OrderFlowReplayClock(1, false, () => wall);
            AssertTrue(clock.TryAdvanceTo(Start), "First tick immediate");
            AssertFalse(clock.TryAdvanceTo(Start.AddSeconds(1)), "Next tick waits");
            wall = 0.5;
            clock.SetSpeed(2);
            wall = 0.75;
            AssertTrue(clock.TryAdvanceTo(Start.AddSeconds(1)), "Speed change preserves old half-second then accrues new speed");
            clock.SetPaused(true);
            wall = 100;
            AssertFalse(clock.TryAdvanceTo(Start.AddSeconds(1)), "Pause also blocks equal timestamps");
            clock.Step();
            AssertTrue(clock.TryAdvanceTo(Start.AddSeconds(1)), "One equal-time source tick consumed");
            AssertFalse(clock.TryAdvanceTo(Start.AddSeconds(1)), "Step does not consume whole same-time group");
            clock.Step();
            AssertTrue(clock.TryAdvanceTo(Start.AddDays(1)), "Step jumps directly to next trade");
            clock.SetPaused(false);
            AssertFalse(clock.TryAdvanceTo(Start.AddDays(1).AddSeconds(1)), "Pause duration adds no virtual time");
            wall += 0.5;
            AssertTrue(clock.TryAdvanceTo(Start.AddDays(1).AddSeconds(1)), "Resume keeps speed");
            clock.SetSkipGaps(true);
            AssertTrue(clock.TryAdvanceTo(Start.AddDays(2)), "Optional long-gap skipping");
            AssertFalse(clock.TryAdvanceTo(Start.AddDays(2).AddSeconds(60)), "Exactly sixty seconds is not skipped");
            clock.SetPaused(true);
            AssertFalse(clock.TryAdvanceTo(Start.AddDays(3)), "Gap skip cannot bypass pause");
            Expect<ArgumentOutOfRangeException>(() => clock.SetSpeed(double.NaN));
            Expect<ArgumentOutOfRangeException>(() => clock.SetSpeed(1001));
            Expect<ArgumentOutOfRangeException>(() => clock.SetSpeed(0.1));
        }

        private sealed class RecordingReplay : IOrderFlowReplayObserver
        {
            public readonly List<OrderFlowReplayFrame> Frames = new List<OrderFlowReplayFrame>();
            public readonly List<string> Frozen = new List<string>();
            public void InputReady(OrderFlowTickInput input) { AssertTrue(input.Sha256 != null, "Hash precedes first tick"); }
            public void BeforeTick(DateTime time, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); }
            public void TickProcessed(OrderFlowRunContext context, OrderFlowBucket pending, OrderFlowDeal tick)
            {
                OrderFlowReplayFrame frame = OrderFlowReplayFrame.Capture(context, pending, tick);
                Frames.Add(frame);
                Frozen.Add(JsonSerializer.Serialize(frame));
            }
        }

        private static void TestReplayPrefixes(string root)
        {
            OrderFlowResearchRequest request = Request(root,
                Row(Start, 100, 40, Side.Buy), Row(Start, 101, 60, Side.Sell),
                Row(Start, 102, 200, Side.Sell), Row(Start.AddSeconds(1), 104, 110, Side.Buy),
                Row(Start.AddSeconds(7), 104, 20, Side.Buy), Row(Start.AddMinutes(2), 110, 30, Side.Buy));
            request.CalculateCloud = true;
            request.Cloud.MinimumSumVolume = 100;
            request.Cloud.MaximumRangeTicks = 5;
            RecordingReplay observer = new RecordingReplay();
            OrderFlowResearchResult result = new OrderFlowResearchEngine().Run(request, CancellationToken.None, observer);
            OrderFlowResearchResult normal = new OrderFlowResearchEngine().Run(request, CancellationToken.None);
            AssertTrue(result.Quality.ResearchAccepted, "Replay accepted");
            AssertEqual(JsonSerializer.Serialize(normal), JsonSerializer.Serialize(result), "Observer does not alter any calculation/hash/order");
            AssertEqual(6, observer.Frames.Count, "Every source tick has its own prefix");
            AssertEqual(0, observer.Frames[0].Result.Clouds.Count, "Subthreshold chain hidden");
            OrderFlowCloud forming = observer.Frames[1].Result.Clouds.Single();
            AssertEqual(100m, forming.Volume, "Cloud appears at threshold");
            AssertEqual("Forming", forming.CompletionReason, "No invented EOF/completion");
            AssertEqual(1, forming.BuyCount, "Prefix Buy count");
            AssertEqual(1, forming.SellCount, "Prefix Sell count");
            AssertEqual(101m, forming.Price, "No final chain price lookahead");
            AssertEqual(300m, observer.Frames[2].Result.Clouds.Single().Volume, "Same-time next tick grows forming Cloud");
            AssertEqual(100m, forming.Qualified.Volume, "Qualification remains frozen");
            AssertEqual(0, observer.Frames[2].Result.Candidates.Count, "Open timestamp bucket has no candidate");
            AssertTrue(observer.Frames[3].Result.Candidates.Count > 0, "Candidate computed after previous bucket closes");
            AssertEqual(0, observer.Frames[3].Result.Labels.Count, "No future outcome leaked");
            AssertTrue(observer.Frames.Last().Result.Labels.Count > 0, "Elapsed horizons eventually publish");
            AssertEqual(40m, observer.Frames[0].Result.Bars[OrderFlowDisplayTimeFrame.Min1].Single().Volume, "First forming candle only first tick");
            AssertEqual(300m, observer.Frames[2].Result.Bars[OrderFlowDisplayTimeFrame.Min1].Single().Volume, "Pending bucket added exactly once");
            AssertEqual(430m, observer.Frames[4].Result.Bars[OrderFlowDisplayTimeFrame.Min1].Single().Volume, "Committed plus pending buckets without duplication");
            for (int index = 0; index < observer.Frames.Count; index++)
            {
                AssertEqual(observer.Frozen[index], JsonSerializer.Serialize(observer.Frames[index]), "Published prefix never changes after later ticks/EOF");
                AssertTrue(observer.Frames[index].Result.Clouds.All(cloud => cloud.LastSourceSequence <= observer.Frames[index].SourceSequence), "No future Cloud vertices");
            }
        }

        private static void TestReplayChartTimeFrames(string root)
        {
            OrderFlowResearchRequest request = CloudRequest(root,
                Row(Start, 100, 500, Side.Buy), Row(Start.AddMinutes(6), 101, 1000, Side.Sell),
                Row(Start.AddMinutes(12), 102, 2000, Side.Buy));
            request.Cloud.MinimumSumVolume = 100;
            RecordingReplay observer = new RecordingReplay();
            OrderFlowResearchResult result = new OrderFlowResearchEngine().Run(request, CancellationToken.None, observer);
            string original = JsonSerializer.Serialize(result);
            OrderFlowResearchChart chart = new OrderFlowResearchChart(); chart.SetResult(result);
            foreach (OrderFlowDisplayTimeFrame timeFrame in OrderFlowChartTimeFrames.GetMenuValues())
            {
                chart.SetTimeFrame(timeFrame);
                int barsBefore = chart.TotalBars;
                decimal referenceBefore = chart.CloudReferenceVolume;
                chart.BeginReplay(100);
                AssertEqual(0, chart.TotalBars, "Full result hidden before first frame");
                chart.ApplyReplayFrame(observer.Frames[0].Result);
                AssertEqual(1, chart.TotalBars, "First tick forms one selected-timeframe candle " + timeFrame);
                chart.ApplyReplayFrame(observer.Frames[1].Result);
                chart.SetCloudPriceMode(true);
                RenderDrawingChart(chart);
                AssertEqual(100m, chart.CloudReferenceVolume, "Reference known before replay, never future median");
                AssertEqual(original, JsonSerializer.Serialize(result), "Display does not mutate full result");
                chart.EndReplay();
                AssertEqual(barsBefore, chart.TotalBars, "Full result restored on same timeframe");
                AssertEqual(referenceBefore, chart.CloudReferenceVolume, "Full historical reference restored");
            }
            chart.BeginReplay(100);
            chart.ApplyReplayFrame(observer.Frames[1].Result);
            chart.SetTimeFrame(OrderFlowDisplayTimeFrame.Min5);
            AssertEqual(2, chart.TotalBars, "Timeframe can change during playback");
            chart.SetTimeFrame(OrderFlowDisplayTimeFrame.Min15);
            AssertEqual(1, chart.TotalBars, "Current prefix reaggregates, not whole future");
            chart.ApplyReplayFrame(observer.Frames[2].Result);
            AssertEqual(1, chart.TotalBars, "Next tick still forms same M15 bar");
            RenderDrawingChart(chart);
            chart.EndReplay();
        }

        private static void TestReplayWorker(string root)
        {
            OrderFlowResearchRequest request = CloudRequest(root, Row(Start, 100, 100, Side.Buy),
                Row(Start, 101, 200, Side.Sell), Row(Start.AddDays(1), 102, 300, Side.Buy));
            OrderFlowResearchResult normal = new OrderFlowResearchEngine().Run(request, CancellationToken.None);
            using (OrderFlowReplaySession session = new OrderFlowReplaySession(request, normal.Input.Sha256, 1000, true))
            {
                session.Clock.SetPaused(true);
                session.Start();
                session.Clock.Step();
                OrderFlowReplayFrame first = null;
                AssertTrue(SpinWait.SpinUntil(() => { first = session.TakeFrame(); return first != null; }, 5000), "Paused worker consumes one requested tick");
                AssertEqual(1L, first.TickCount, "Equal timestamp did not consume second tick");
                AssertFalse(session.Finished, "Worker remains paused");
                session.Clock.Step();
                OrderFlowReplayFrame second = null;
                AssertTrue(SpinWait.SpinUntil(() => { second = session.TakeFrame(); return second != null; }, 5000), "Second step publishes");
                AssertEqual(2L, second.TickCount, "Physical tick order retained");
                session.Clock.SetPaused(false);
                AssertTrue(SpinWait.SpinUntil(() => session.Finished, 5000), "Skip gap completes worker");
                AssertEqual(null, session.Error, "No worker error");
                OrderFlowReplayFrame final = session.TakeFrame();
                AssertTrue(final.Complete, "Final frame survives terminal state");
                AssertEqual(normal.CloudHash, final.Result.CloudHash, "Replay has same Cloud calculation");
                AssertFalse(Directory.Exists(request.OutputRootPath), "Playback never exports");
            }
            using (OrderFlowReplaySession session = new OrderFlowReplaySession(request, normal.Input.Sha256, 0.25, false))
            {
                session.Clock.SetPaused(true);
                session.Start(); session.Dispose();
                AssertTrue(SpinWait.SpinUntil(() => session.Finished, 5000), "Cancellation wakes paused worker");
                AssertEqual(null, session.Error, "Cancellation is not an error");
            }
            using (FileStream access = File.Open(request.TicksFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            using (OrderFlowReplaySession changed = new OrderFlowReplaySession(request, "different", 1, true))
            {
                Expect<InvalidOperationException>(() => changed.InputReady(normal.Input));
            }
        }

        private static void TestReplayToolbar(string root)
        {
            XDocument ui;
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Research.Ui.xaml")) { ui = XDocument.Load(stream); }
            foreach (string name in new[] { "ButtonReplayPlay", "ButtonReplayStep", "ButtonReplayReturn", "ComboBoxReplaySpeed", "CheckBoxReplaySkipGaps", "TextBlockReplayStatus" })
            {
                XElement control = ui.Descendants().Single(element => (string)element.Attribute("Name") == name);
                AssertTrue(control.Ancestors().Any(element => (string)element.Attribute("Name") == "ScrollViewerChartSurface"), "Replay controls travel with single chart: " + name);
            }
        }

        private static void TestReplayDeltaOnly(string root)
        {
            OrderFlowResearchRequest request = Request(root, BasicTicks());
            OrderFlowResearchResult result = new OrderFlowResearchEngine().Run(request, CancellationToken.None);
            AssertTrue(result.Quality.ResearchAccepted, "Delta-only fixture accepted");
            AssertEqual(null, request.Cloud, "Validated Delta-only deliberately has no Cloud settings");
            using (OrderFlowReplaySession session = new OrderFlowReplaySession(request, result.Input.Sha256, 1000, true))
            {
                AssertEqual(1m, session.CloudReferenceVolume, "UI gets safe fixed reference without dereferencing Cloud");
                OrderFlowResearchChart chart = new OrderFlowResearchChart(); chart.SetResult(result);
                chart.BeginReplay(session.CloudReferenceVolume);
                session.Start();
                AssertTrue(SpinWait.SpinUntil(() => session.Finished, 5000), "Delta-only playback completes");
                AssertEqual(null, session.Error, "No null Cloud failure");
                OrderFlowReplayFrame frame = session.TakeFrame();
                chart.ApplyReplayFrame(frame.Result); RenderDrawingChart(chart);
                AssertTrue(frame.Result.DeltaCalculated && !frame.Result.CloudCalculated, "Mode unchanged");
                AssertEqual(result.FeatureHash, frame.Result.FeatureHash, "Delta-only calculation parity");
                chart.EndReplay();
            }
        }

        private static void TestReplayReturnRange(string root)
        {
            OrderFlowResearchResult result = new OrderFlowResearchResult();
            result.Bars[OrderFlowDisplayTimeFrame.Min1] = Enumerable.Range(0, 300)
                .Select(index => ChartMinute(Start.AddMinutes(index), 100, 102, 99, 101, 1, 1, 0)).ToList();
            result.Bars[OrderFlowDisplayTimeFrame.Sec15] = Enumerable.Range(0, 1200).Select(index =>
            {
                OrderFlowDisplayBar bar = ChartMinute(Start.AddSeconds(index * 15), 100, 102, 99, 101, 1, 1, 0);
                bar.TimeFrame = OrderFlowDisplayTimeFrame.Sec15; bar.TimeEnd = bar.TimeStart.AddSeconds(15);
                return bar;
            }).ToList();
            OrderFlowResearchChart chart = new OrderFlowResearchChart(); chart.SetResult(result);
            AssertEqual(180, chart.StartIndex, "Initial M1 range starts at minute180");
            chart.BeginReplay(1); chart.SetTimeFrame(OrderFlowDisplayTimeFrame.Sec15); chart.EndReplay();
            AssertEqual(720, chart.StartIndex, "Return maps source minute180 to Sec15 index720");
            AssertEqual(480, chart.VisibleCount, "Return preserves120 source minutes on Sec15");
            chart.BeginReplay(1); chart.SetTimeFrame(OrderFlowDisplayTimeFrame.Min60); chart.EndReplay();
            AssertEqual(3, chart.StartIndex, "Return maps source minute180 to hour3");
            AssertEqual(2, chart.VisibleCount, "Return preserves last two hours");
            chart.SetTimeFrame(OrderFlowDisplayTimeFrame.Min1); chart.Zoom(17); chart.ScrollTo(101);
            chart.BeginReplay(1); chart.SetTimeFrame(OrderFlowDisplayTimeFrame.Min5); chart.EndReplay();
            AssertEqual(20, chart.StartIndex, "Coarser return includes starting boundary candle");
            AssertEqual(4, chart.VisibleCount, "Coarser return covers101..118 with100..120");
            RenderDrawingChart(chart);
        }

        private sealed class AdmissionReplay : IOrderFlowReplayObserver
        {
            internal OrderFlowReplaySession Session;
            internal ManualResetEventSlim Admitted;
            internal ManualResetEventSlim Release;
            private bool _first = true;
            public void InputReady(OrderFlowTickInput input) { Session.InputReady(input); }
            public void BeforeTick(DateTime time, CancellationToken cancellationToken)
            {
                Session.BeforeTick(time, cancellationToken);
                if (_first) { _first = false; Admitted.Set(); Release.Wait(cancellationToken); }
            }
            public void TickProcessed(OrderFlowRunContext context, OrderFlowBucket pending, OrderFlowDeal tick)
            {
                Session.TickProcessed(context, pending, tick);
            }
        }

        private static void TestReplayStepBoundary(string root)
        {
            OrderFlowResearchRequest request = CloudRequest(root, Row(Start, 100, 100, Side.Buy),
                Row(Start, 101, 200, Side.Sell), Row(Start.AddDays(1), 102, 300, Side.Buy));
            OrderFlowResearchResult result = new OrderFlowResearchEngine().Run(request, CancellationToken.None);
            using (OrderFlowReplaySession session = new OrderFlowReplaySession(request, result.Input.Sha256, 1, true))
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            using (ManualResetEventSlim admitted = new ManualResetEventSlim())
            using (ManualResetEventSlim release = new ManualResetEventSlim())
            {
                Exception workerError = null;
                AdmissionReplay observer = new AdmissionReplay { Session = session, Admitted = admitted, Release = release };
                Thread worker = new Thread(() =>
                {
                    try { new OrderFlowResearchEngine().Run(request, cancellation.Token, observer); }
                    catch (OperationCanceledException) { }
                    catch (Exception error) { workerError = error; }
                }) { IsBackground = true };
                worker.Start();
                try
                {
                    AssertTrue(admitted.Wait(5000), "First tick admitted but not processed");
                    session.SetPaused(true);
                    AssertFalse(session.TryStep(), "Pause flag cannot admit another tick while first is in flight");
                    AssertFalse(session.PausedAtBoundary, "No worker acknowledgment before processing completes");
                    release.Set();
                    AssertTrue(SpinWait.SpinUntil(() => session.PausedAtBoundary, 5000), "Worker publishes prefix then acknowledges pause");
                    OrderFlowReplayFrame frame = session.TakeFrame(out bool paused);
                    AssertTrue(paused, "Acknowledgment and latest frame acquired together");
                    AssertEqual(1L, frame.TickCount, "Admitted tick completed alone");
                    AssertTrue(session.TryStep(), "Confirmed pause accepts one step");
                    AssertTrue(SpinWait.SpinUntil(() => session.PausedAtBoundary, 5000), "Worker stops again after one tick");
                    frame = session.TakeFrame(out paused);
                    AssertEqual(2L, frame.TickCount, "Exactly one additional same-time tick");
                    AssertTrue(paused, "Next tick remains blocked");
                }
                finally
                {
                    cancellation.Cancel(); release.Set();
                    AssertTrue(worker.Join(5000), "Worker cancellation completes");
                }
                AssertEqual(null, workerError, "No concurrency test error");
            }
        }
    }
}
