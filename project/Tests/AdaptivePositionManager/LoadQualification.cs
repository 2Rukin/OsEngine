using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using OsEngine.OsTrader.AdaptivePositionManager;

namespace OsEngine.AdaptivePositionManager.Tests
{
    /// <summary>
    /// Explicit offline synthetic pipeline load qualification, --load NEW_OUTPUT_DIRECTORY.
    /// Measures a real generated tick file and actual feature/controller/durable audit path with fixture fills.
    /// No native server, UI or connector is launched; results cannot qualify native/live throughput or owner history.
    /// </summary>
    internal sealed class LoadQualification
    {
        private readonly BlockingCollection<(ApmTick Tick, long Queued)> _queue = new BlockingCollection<(ApmTick, long)>(512);
        private readonly List<long> _core = new List<long>();
        private readonly List<long> _waiting = new List<long>();
        private readonly List<long> _total = new List<long>();
        private readonly List<long> _memory = new List<long>();
        private readonly List<object> _trace = new List<object>();
        private ApmExecutionController _controller;
        private ApmCampaign _campaign;
        private ApmFeatures _features;
        private Exception _failure;
        private int _processed;
        private int _peakQueue;
        private int _sends;
        private int _fills;
        private int _orders;

        internal static int Run(string output)
        {
            output = Path.GetFullPath(output);
            if (Directory.Exists(output)) throw new ArgumentException("Load evidence directory must be new.");
            Directory.CreateDirectory(output);
            string file = Path.Combine(output, "synthetic-burst.txt");
            using (StreamWriter writer = new StreamWriter(file))
                for (int second = 0; second < 100; second++)
                    for (int tick = 0; tick < 200; tick++)
                    {
                        decimal price = second < 35 || second % 2 == 0 ? 100 : 104;
                        writer.WriteLine(CoreTests.Start.AddSeconds(second).ToString("yyyyMMdd,HHmmss", CultureInfo.InvariantCulture)
                            + "," + price.ToString(CultureInfo.InvariantCulture) + ",1,Buy," + (tick * 5000) + "," + (second * 200 + tick));
                    }
            ApmTick[] input;
            using (StreamReader reader = new StreamReader(file)) input = ApmTickReader.Read(reader, "SyntheticLoad", "APM_SYNTH").ToArray();
            int measuredPeak = input.GroupBy(t => t.Time).Max(g => g.Count());
            LoadQualification plain = new LoadQualification();
            object off = plain.Execute(input, Path.Combine(output, "plain"), false, measuredPeak);
            LoadQualification detailed = new LoadQualification();
            object on = detailed.Execute(input, Path.Combine(output, "detailed"), true, measuredPeak);
            Program.Equal(ApmTickReader.HashJson(plain._trace), ApmTickReader.HashJson(detailed._trace), "diagnostics on/off exact execution trace under load");
            File.WriteAllText(Path.Combine(output, "load.json"), JsonSerializer.Serialize(new { SchemaVersion = "APM-Load-v1",
                Status = "PASS_SYNTHETIC_COMPONENT_ONLY", DatasetHash = ApmTickReader.HashFile(file), InputEvents = input.Length,
                MeasuredPeakPerSecond = measuredPeak, Machine = Environment.MachineName, Cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),
                LogicalProcessors = Environment.ProcessorCount, Runtime = RuntimeInformation.FrameworkDescription,
                OS = RuntimeInformation.OSDescription, BuildHash = ApmTickReader.HashFile(typeof(ApmCampaign).Assembly.Location),
                Plain = off, Detailed = on, Limitations = "Fixture full fills, bounded test queue; native/UI/real-data peak throughput NOT_RUN" },
                new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("LOAD_COMPONENT_PASS " + output); return 0;
        }

        private object Execute(ApmTick[] input, string directory, bool detailed, int measuredPeak)
        {
            ApmCampaignSpec spec = CoreTests.Spec() with { EntryTime = CoreTests.Start.AddSeconds(35), SessionExitTime = CoreTests.Start.AddSeconds(95) };
            ApmPolicy policy = new ApmPolicy();
            ApmOperationalLimits limits = new ApmOperationalLimits(32, 33);
            _campaign = new ApmCampaign(spec, policy, limits);
            _features = new ApmFeatures(spec, policy);
            ApmArtifacts artifacts = new ApmArtifacts(directory);
            using (_controller = new ApmExecutionController(_campaign, new Gateway(this), artifacts))
            {
                _controller.DetailedDiagnostics = detailed;
                Thread consumer = new Thread(Consume) { IsBackground = true, Name = "APM synthetic load consumer" };
                Stopwatch elapsed = Stopwatch.StartNew(); consumer.Start();
                foreach (ApmTick tick in input)
                {
                    long queued = Stopwatch.GetTimestamp();
                    if (!_queue.TryAdd((tick, queued), 5000)) throw new TimeoutException("Bounded load queue made no progress.");
                    _peakQueue = Math.Max(_peakQueue, _queue.Count);
                }
                _queue.CompleteAdding();
                if (!consumer.Join(60000)) throw new TimeoutException("Synthetic load terminal timeout.");
                elapsed.Stop();
                if (_failure != null) throw new InvalidOperationException("Load consumer failed.", _failure);
                Program.Equal(input.Length, _processed, "no lost price events");
                Program.Equal(_sends, _fills, "no lost fixture full fills"); Program.Equal(_sends, _orders, "no lost fixture order events");
                Program.Check(_peakQueue <= 512 && _controller.Recent.Length <= 2000, "bounded queues and diagnostics");
                Program.Check(_campaign.Intents.Length <= limits.OrdinaryIntents + limits.ExitIntents, "bounded retained native-model history");
                Program.Equal(ApmState.Completed, _controller.Snapshot.State, "load campaign completed including resource/cutoff protection");
                double rate = input.Length / elapsed.Elapsed.TotalSeconds;
                Program.Check(rate >= measuredPeak * 2, "throughput at least twice measured synthetic input peak");
                Program.Check(_memory.Count >= 3 && _memory.Last() - _memory[1] < 16 * 1024 * 1024, "finite post-warmup managed memory growth below16MiB in this run");
                ApmCampaign restored = ApmArtifacts.LoadCheckpoint(Path.Combine(directory, "checkpoint.json"));
                Program.Check(restored.Reconcile(0, true, true), "load terminal full ledger recovers and reconciles");
                Program.Equal(0m, restored.Snapshot.FilledVolume, "recovery retains flat actual ledger");
                artifacts.Dispose();
                int decisionRows = File.ReadLines(Path.Combine(directory, "events.jsonl")).Count(line => line.Contains("\"Kind\":\"Decision\""));
                Program.Equal(input.Length, decisionRows, "every input price has durable decision evidence");
                return new { ElapsedSeconds = elapsed.Elapsed.TotalSeconds, EventsPerSecond = rate, ReplayFactor = rate / measuredPeak,
                    ProcessedPrice = _processed, Sends = _sends, Fills = _fills, Orders = _orders, PeakQueue = _peakQueue,
                    RetainedIntents = _campaign.Intents.Length, RecentRows = _controller.Recent.Length,
                    CoreMilliseconds = Percentiles(_core), QueueWaitMilliseconds = Percentiles(_waiting), EndToEndMilliseconds = Percentiles(_total),
                    ManagedBytesAfterForcedGC = _memory, Final = _controller.Snapshot,
                    DecisionRows = decisionRows };
            }
        }

        private void Consume()
        {
            try
            {
                foreach ((ApmTick Tick, long Queued) item in _queue.GetConsumingEnumerable())
                {
                    long started = Stopwatch.GetTimestamp();
                    _controller.Process(_features.OnTick(item.Tick));
                    long finished = Stopwatch.GetTimestamp();
                    _processed++;
                    if (_processed > 2000)
                    { _core.Add(finished - started); _waiting.Add(started - item.Queued); _total.Add(finished - item.Queued); }
                    if (_processed % 4000 == 0) _memory.Add(GC.GetTotalMemory(true));
                }
            }
            catch (Exception error) { _failure = error; }
        }

        private static object Percentiles(List<long> values)
        {
            long[] sorted = values.OrderBy(v => v).ToArray();
            double Milliseconds(double p) => sorted[(int)Math.Ceiling((sorted.Length - 1) * p)] * 1000d / Stopwatch.Frequency;
            return new { P50 = Milliseconds(0.50), P95 = Milliseconds(0.95), P99 = Milliseconds(0.99), Samples = sorted.Length };
        }

        private sealed class Gateway : IApmOrderGateway
        {
            private readonly LoadQualification _owner;
            internal Gateway(LoadQualification owner) { _owner = owner; }
            public void Send(ApmIntent intent)
            {
                _owner._sends++;
                _owner._controller.ApplyFill(new ApmFill("load-" + intent.Id, intent.Id, intent.Time,
                    _owner._controller.Snapshot.Market.Price, intent.Volume, 0)); _owner._fills++;
                _owner._controller.ApplyOrder(intent.Id, ApmOrderState.Filled, intent.Volume, "load-" + intent.Id); _owner._orders++;
                _owner._trace.Add(new { intent.Id, intent.Action, intent.Volume, Quantity = _owner._controller.Snapshot.FilledVolume });
            }
            public void Cancel(ApmIntent intent) => _owner._controller.ApplyOrder(intent.Id, ApmOrderState.Canceled, intent.Filled, "load-" + intent.Id);
        }
    }
}
