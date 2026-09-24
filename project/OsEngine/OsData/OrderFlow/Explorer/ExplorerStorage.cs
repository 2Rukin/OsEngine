/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace OsEngine.OsData.OrderFlow.Explorer
{
    /// <summary>Progress without raw input contents. Consumers may coalesce updates; terminal errors retain a diagnostic reason.</summary>
    internal sealed record ExplorerProgress(string Phase, long Rows, long Clouds, DateTime? Date, long MemoryBytes, double Seconds);
    internal sealed record ExplorerManifest(string Version, string Hash, string InputSha256, JsonElement Parameters,
        long Rows, long SelectedRows, long Clouds, bool SecondsOnly, DateTime[] Dates, Dictionary<string, string> Checksums)
    {
        public string Formulas { get; init; }
    }
    internal sealed record ExplorerRun(ExplorerRunSpec Spec, string CatalogPath, string EpisodePath, string StudyPath, ExplorerManifest Catalog);
    internal sealed record ExplorerPage(IReadOnlyList<ExplorerCloud> Rows, long Total, long Passed, long Unknown, long NextOffset);
    internal sealed record ExplorerSummary(string Profile, long Total, long Passed, long Single, long OpenAtEnd,
        decimal? P25, decimal? Median, decimal? P75, decimal? P90, decimal? P95, decimal? P99, long AtLeastVolume, long InTimeSlice)
    {
        public long Accumulated => Total - Single;
        public decimal? VolumeFraction => Total == 0 ? null : (decimal)AtLeastVolume / Total;
    }

    /// <summary>Seekable immutable catalog storage. Readers own their handles; pages never load the full catalog or prefix index.</summary>
    /// <remarks>Checksums and schema are verified before reopening. Display filtering uses only catalog files, never the input reader.</remarks>
    internal static class ExplorerStorage
    {
        internal static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = false };

        #region Catalog and prefix streams

        internal sealed class RowWriter<T> : IDisposable
        {
            private readonly BinaryWriter _data, _index;
            internal RowWriter(string directory, string name)
            {
                _data = new BinaryWriter(File.Create(Path.Combine(directory, name + ".bin")), Encoding.UTF8);
                try { _index = new BinaryWriter(File.Create(Path.Combine(directory, name + ".idx")), Encoding.UTF8); }
                catch (Exception failure)
                {
                    try { _data.Dispose(); }
                    catch (Exception cleanup) { throw new AggregateException("Explorer index creation and data rollback failed.", failure, cleanup); }
                    throw;
                }
            }
            /// <summary>Owns both supplied writers, including failure-safe disposal; used by deterministic storage fault fixtures.</summary>
            internal RowWriter(BinaryWriter data, BinaryWriter index) { _data = data; _index = index; }
            internal void Add(T row)
            {
                byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(row, Json);
                _index.Write(_data.BaseStream.Position); _data.Write(bytes.Length); _data.Write(bytes);
            }
            /// <summary>Attempts to close both streams; retains the primary error and aggregates a second close failure.</summary>
            public void Dispose()
            {
                Exception failure = null;
                try { _data.Dispose(); } catch (Exception error) { failure = error; }
                try { _index.Dispose(); }
                catch (Exception error)
                {
                    if (failure != null) { throw new AggregateException("Both Explorer writers failed to close.", failure, error); }
                    throw;
                }
                if (failure != null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw(); }
            }
        }
        internal static IEnumerable<T> ReadRows<T>(string directory, string name, long start = 0)
        {
            using BinaryReader data = new BinaryReader(File.OpenRead(Path.Combine(directory, name + ".bin")), Encoding.UTF8);
            using BinaryReader index = new BinaryReader(File.OpenRead(Path.Combine(directory, name + ".idx")), Encoding.UTF8);
            if (start < 0 || start > index.BaseStream.Length / sizeof(long)) { throw new ArgumentOutOfRangeException(nameof(start)); }
            if (start == index.BaseStream.Length / sizeof(long)) { yield break; }
            index.BaseStream.Position = start * sizeof(long); data.BaseStream.Position = index.ReadInt64();
            while (data.BaseStream.Position < data.BaseStream.Length)
            {
                int size = data.ReadInt32();
                if (size < 0 || size > 64 * 1024 * 1024 || size > data.BaseStream.Length - data.BaseStream.Position) { throw new InvalidDataException("Invalid Explorer record length."); }
                yield return JsonSerializer.Deserialize<T>(data.ReadBytes(size), Json) ?? throw new InvalidDataException("Missing Explorer record.");
            }
        }

        internal static void WritePrefix(BinaryWriter writer, ExplorerPrefix prefix, ExplorerRunSpec spec)
        {
            int profileIndex = 0;
            while (profileIndex < spec.Profiles.Length && spec.Profiles[profileIndex].Key != prefix.Profile) { profileIndex++; }
            if (profileIndex == spec.Profiles.Length) { throw new InvalidDataException("Unknown Explorer profile."); }
            writer.Write((byte)profileIndex);
            writer.Write(prefix.FirstSequence); writer.Write(prefix.Sequence); writer.Write(prefix.Time.Ticks);
            writer.Write(prefix.Price); writer.Write(prefix.Low); writer.Write(prefix.High); writer.Write(prefix.Buy); writer.Write(prefix.Sell);
            writer.Write(prefix.Vwap); writer.Write(prefix.Count); writer.Write(prefix.RelativeThreshold.HasValue);
            if (prefix.RelativeThreshold.HasValue) { writer.Write(prefix.RelativeThreshold.Value); }
            writer.Write(prefix.Completed); writer.Write(prefix.Reason);
        }
        internal static IEnumerable<ExplorerPrefix> Prefixes(string directory, ExplorerRunSpec spec)
        {
            using BinaryReader reader = new BinaryReader(File.OpenRead(Path.Combine(directory, "prefix-index.bin")), Encoding.UTF8);
            string hash = spec.CatalogSpecHash;
            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                int profileIndex = reader.ReadByte();
                if (profileIndex >= spec.Profiles.Length) { throw new InvalidDataException("Unknown Explorer profile index."); }
                string profile = spec.Profiles[profileIndex].Key;
                long first = reader.ReadInt64(), sequence = reader.ReadInt64(); DateTime time = new DateTime(reader.ReadInt64());
                decimal price = reader.ReadDecimal(), low = reader.ReadDecimal(), high = reader.ReadDecimal(), buy = reader.ReadDecimal(), sell = reader.ReadDecimal(), vwap = reader.ReadDecimal();
                int count = reader.ReadInt32(); decimal? relative = reader.ReadBoolean() ? reader.ReadDecimal() : null;
                yield return new ExplorerPrefix(hash + "/" + profile + "/" + first, profile, first, sequence, time,
                    price, low, high, buy, sell, vwap, count, relative, reader.ReadBoolean(), reader.ReadString());
            }
        }

        internal static ExplorerPage Page(string directory, ExplorerView view, decimal step, long start, int pageSize, CancellationToken cancellation)
        {
            List<ExplorerCloud> rows = new List<ExplorerCloud>(); long total = 0, passed = 0, unknown = 0, cursor = start;
            foreach (ExplorerCloud cloud in ReadRows<ExplorerCloud>(directory, "catalog", start))
            {
                cancellation.ThrowIfCancellationRequested(); cursor++;
                if (!string.IsNullOrEmpty(view.Profile) && cloud.Profile != view.Profile) { continue; }
                if (!string.IsNullOrEmpty(view.IdContains) && !cloud.Id.Contains(view.IdContains, StringComparison.Ordinal)) { continue; }
                rows.Add(cloud); total++; if (view.Passes(cloud, step)) { passed++; }
                if (cloud.RelativeStatus == "Unknown") { unknown++; }
                if (rows.Count == pageSize) { break; }
            }
            return new ExplorerPage(rows, total, passed, unknown, cursor);
        }

        internal static IReadOnlyList<ExplorerSummary> Summarize(string directory, ExplorerRunSpec spec, ExplorerView view, CancellationToken cancellation)
        {
            Dictionary<string, ExplorerDistribution> distributions = new Dictionary<string, ExplorerDistribution>();
            Dictionary<string, long[]> counts = new Dictionary<string, long[]>();
            foreach (ExplorerProfile profile in spec.Profiles) { distributions.Add(profile.Key, new ExplorerDistribution()); counts.Add(profile.Key, new long[6]); }
            foreach (ExplorerCloud cloud in ReadRows<ExplorerCloud>(directory, "catalog"))
            {
                cancellation.ThrowIfCancellationRequested();
                ExplorerDistribution distribution = distributions[cloud.Profile]; distribution.Add(cloud.Volume);
                long[] count = counts[cloud.Profile]; count[0]++; if (view.Passes(cloud, spec.PriceStep)) { count[1]++; }
                if (cloud.Count == 1) { count[2]++; } if (cloud.Reason == "OpenAtEnd") { count[3]++; }
                ExplorerView layer = view.ForProfile(cloud.Profile);
                if (!layer.MinimumVolume.HasValue || cloud.Volume >= layer.MinimumVolume.Value) { count[4]++; }
                if (cloud.StartTime.Hour >= layer.StartHour && cloud.StartTime.Hour < layer.EndHour) { count[5]++; }
                if (count[0] % 4096 == 0) { CheckMemory(spec); }
            }
            return distributions.Select(pair => { ExplorerDistribution d = pair.Value; long[] c = counts[pair.Key];
                return new ExplorerSummary(pair.Key, c[0], c[1], c[2], c[3], Q(d, .25m), Q(d, .5m), Q(d, .75m), Q(d, .9m), Q(d, .95m), Q(d, .99m), c[4], c[5]); }).ToArray();
        }
        private static decimal? Q(ExplorerDistribution distribution, decimal p) => distribution.Count == 0 ? null : distribution.Quantile(p);

        internal static ExplorerView ResolveRelations(ExplorerRun run, ExplorerView view, CancellationToken cancellation)
        {
            if (view.Layers.Count > 0)
            {
                return view with { Layers = view.Layers.ToImmutableDictionary(p => p.Key, p => ResolveRelations(run, p.Value, cancellation)) };
            }
            if (string.IsNullOrEmpty(view.RelatedId)) { return view with { RelatedCloudIds = ImmutableHashSet<string>.Empty }; }
            string volumeId = view.RelatedId; int children = int.MaxValue;
            if (run.StudyPath != null)
            {
                foreach (ExplorerObservation observation in ReadRows<ExplorerObservation>(run.StudyPath, "observations"))
                {
                    cancellation.ThrowIfCancellationRequested(); if (observation.Id != view.RelatedId) { continue; }
                    if (observation.Trigger == null) { return view with { RelatedCloudIds = ImmutableHashSet<string>.Empty }; }
                    volumeId = observation.Trigger.VolumeId;
                    if (observation.Trigger.Kind == "Cloud") { return view with { RelatedCloudIds = ImmutableHashSet.Create(volumeId) }; }
                    children = observation.Trigger.Children; break;
                }
            }
            if (run.EpisodePath != null)
            {
                foreach (ExplorerEpisode episode in ReadRows<ExplorerEpisode>(run.EpisodePath, "episodes"))
                { cancellation.ThrowIfCancellationRequested(); if (episode.Id == volumeId) { return view with { RelatedCloudIds = episode.ChildIds.Take(children).ToImmutableHashSet() }; } }
            }
            return view with { RelatedCloudIds = ImmutableHashSet<string>.Empty };
        }

        internal static T ReadAt<T>(string directory, string name, long index) => ReadRows<T>(directory, name, index).First();
        internal static T FindOrdered<T>(string directory, string name, long sequence, Func<T, long> key) where T : class
        {
            long count = new FileInfo(Path.Combine(directory, name + ".idx")).Length / 8, left = 0, right = count;
            while (left < right)
            { long middle = left + (right - left) / 2; T row = ReadAt<T>(directory, name, middle); long value = key(row); if (value == sequence) { return row; } if (value < sequence) { left = middle + 1; } else { right = middle; } }
            return null;
        }

        internal static void WriteCatalogSummary(string directory, ExplorerRunSpec spec, CancellationToken cancellation)
        {
            File.WriteAllText(Path.Combine(directory, "summary.json"), JsonSerializer.Serialize(Summarize(directory, spec, new ExplorerView { MinimumVolume = null }, cancellation), Json));
            Dictionary<string, (long Total, long Single, long Open)> slices = new Dictionary<string, (long, long, long)>();
            foreach (ExplorerCloud cloud in ReadRows<ExplorerCloud>(directory, "catalog"))
            {
                cancellation.ThrowIfCancellationRequested(); string key = cloud.Profile + "," + cloud.StartTime.ToString("yyyy-MM-dd,HH", CultureInfo.InvariantCulture);
                slices.TryGetValue(key, out (long Total, long Single, long Open) counts);
                slices[key] = (counts.Total + 1, counts.Single + (cloud.Count == 1 ? 1 : 0), counts.Open + (cloud.Reason == "OpenAtEnd" ? 1 : 0));
            }
            using StreamWriter csv = new StreamWriter(Path.Combine(directory, "catalog-slices.csv"), false, new UTF8Encoding(false));
            csv.WriteLine("Profile,SourceDate,SourceHour,Total,Single,Accumulated,OpenAtEnd");
            foreach (KeyValuePair<string, (long Total, long Single, long Open)> slice in slices.OrderBy(p => p.Key, StringComparer.Ordinal))
            { csv.WriteLine(slice.Key + "," + slice.Value.Total + "," + slice.Value.Single + "," + (slice.Value.Total - slice.Value.Single) + "," + slice.Value.Open); }
        }

        #endregion

        #region Publication and verification

        internal static string Stage(string root, string kind)
        {
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "." + kind + "-staging-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path); return path;
        }
        internal static ExplorerManifest Verify(string directory, string version, string hash, CancellationToken cancellation)
        {
            ExplorerManifest manifest = JsonSerializer.Deserialize<ExplorerManifest>(File.ReadAllText(Path.Combine(directory, "manifest.json")), Json);
            string[] required = version switch
            {
                ExplorerRunSpec.CatalogVersion => new[] { "catalog.bin", "catalog.idx", "prefix-index.bin", "bars.bin", "bars.idx", "quality.json", "summary.json", "catalog-slices.csv", "catalog-spec.json" },
                ExplorerRunSpec.EpisodeVersion => new[] { "episodes.bin", "episodes.idx", "episode-prefix.bin", "episode-prefix.idx", "children.bin", "children.idx", "membership.bin", "membership.idx" },
                ExplorerRunSpec.StudyVersion => new[] { "run-spec.json", "triggers.bin", "triggers.idx", "pivots.bin", "pivots.idx", "observations.bin", "observations.idx", "future-labels.bin", "future-labels.idx", "future-labels.csv", "watch-vwap-samples.bin", "watch-vwap-samples.idx", "diagnostics.bin", "diagnostics.idx", "comparison.csv", "study-summary.json", "README.txt" },
                _ => throw new InvalidDataException("Unknown Explorer bundle version.")
            };
            if (manifest == null || manifest.Version != version || manifest.Hash != hash || manifest.Formulas != FormulaVersion(version) ||
                manifest.Checksums == null || required.Any(f => !manifest.Checksums.ContainsKey(f)))
            { throw new InvalidDataException("Unknown or mismatched Explorer bundle schema/identity."); }
            foreach (KeyValuePair<string, string> file in manifest.Checksums)
            {
                cancellation.ThrowIfCancellationRequested();
                if (Path.GetFileName(file.Key) != file.Key || HashFile(Path.Combine(directory, file.Key), cancellation) != file.Value)
                { throw new InvalidDataException("Explorer artifact checksum mismatch: " + Path.GetFileName(file.Key)); }
            }
            return manifest;
        }
        internal static ExplorerManifest Publish(string staging, string destination, ExplorerManifest manifest, CancellationToken cancellation)
        {
            Dictionary<string, string> sums = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string file in Directory.GetFiles(staging).OrderBy(p => p, StringComparer.Ordinal))
            { sums.Add(Path.GetFileName(file), HashFile(file, cancellation)); }
            manifest = manifest with { Checksums = sums, Formulas = FormulaVersion(manifest.Version) };
            File.WriteAllText(Path.Combine(staging, "manifest.json"), JsonSerializer.Serialize(manifest, Json), new UTF8Encoding(false));
            cancellation.ThrowIfCancellationRequested();
            if (Directory.Exists(destination))
            {
                ExplorerManifest previous = Verify(destination, manifest.Version, manifest.Hash, cancellation);
                if (previous.Checksums.Count != sums.Count || sums.Any(p => !previous.Checksums.TryGetValue(p.Key, out string value) || value != p.Value))
                { throw new InvalidDataException("Existing Explorer bundle differs; it was preserved."); }
                Directory.Delete(staging, true); return previous;
            }
            Directory.Move(staging, destination); return manifest;
        }
        private static string FormulaVersion(string version) => version switch
        {
            ExplorerRunSpec.CatalogVersion => "raw-atr20-previous-close-1;nearest-rank-1;frozen-profile-1;exact-diagonal-1",
            ExplorerRunSpec.EpisodeVersion => "completed-children-1;raw-interval-1;previous-episode-p95-1",
            ExplorerRunSpec.StudyVersion => "raw-atr20-previous-close-1;directional-change-1;watch-gates-1;raw-weighted-vwap-1;ordinal-labels-1;fit-test-purging-1",
            _ => throw new InvalidDataException("Unknown Explorer formula version.")
        };
        internal static string HashFile(string path, CancellationToken cancellation)
        {
            using FileStream input = File.OpenRead(path);
            using System.Security.Cryptography.IncrementalHash hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
            byte[] buffer = new byte[65536]; int count;
            while ((count = input.Read(buffer)) != 0) { cancellation.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, count); }
            return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }
        internal static void SaveView(ExplorerRun run, ExplorerView view)
        {
            string directory = Path.Combine(run.Spec.OutputRootPath, "cloud-explorer-view"); Directory.CreateDirectory(directory);
            string destination = Path.Combine(directory, run.Spec.CatalogSpecHash + ".json"), temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temporary, JsonSerializer.Serialize(view, Json), new UTF8Encoding(false)); File.Move(temporary, destination, true); }
            finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
        }
        internal static ExplorerView LoadView(ExplorerRun run)
        {
            string path = Path.Combine(run.Spec.OutputRootPath, "cloud-explorer-view", run.Spec.CatalogSpecHash + ".json");
            if (!File.Exists(path)) { return new ExplorerView(); }
            ExplorerView view = JsonSerializer.Deserialize<ExplorerView>(File.ReadAllText(path), Json);
            if (view?.Schema != "cloud-explorer-view-1") { throw new InvalidDataException("Unknown Explorer view schema."); }
            return view;
        }
        internal static void CheckMemory(ExplorerRunSpec spec)
        {
            if (GC.GetTotalMemory(false) > spec.MaximumMemoryMegabytes * 1024L * 1024)
            { throw new InvalidDataException("Explorer managed-memory budget exceeded; reduce profiles/windows or raise the explicit memory limit."); }
        }

        #endregion
    }

    /// <summary>Offline catalog transaction. Source reader validates every row, including excluded dates, before atomic publication.</summary>
    /// <remarks>Caller owns worker thread and cancellation. No UI, Application, connector or trading state is accessed.</remarks>
    internal static class ExplorerRunner
    {
        internal static ExplorerRun Catalog(ExplorerRunSpec request, CancellationToken cancellation, Action<ExplorerProgress> progress = null)
        {
            request.Validate(); Stopwatch clock = Stopwatch.StartNew();
            OrderFlowTickInput metadata = new OrderFlowTickInput(); string staging = null;
            try
            {
                using OrderFlowTickReader reader = new OrderFlowTickReader(request.InputPath, metadata, cancellation);
                if (request.InputSha256 != null && request.InputSha256 != metadata.Sha256) { throw new InvalidDataException("Explorer source SHA-256 changed."); }
                ExplorerRunSpec spec = request with { InputSha256 = metadata.Sha256, FromDate = request.FromDate?.Date, ToDate = request.ToDate?.Date };
                string destination = Path.Combine(spec.OutputRootPath, "cloud-catalog-" + spec.CatalogSpecHash);
                if (Directory.Exists(destination))
                {
                    ExplorerManifest reused = ExplorerStorage.Verify(destination, ExplorerRunSpec.CatalogVersion, spec.CatalogSpecHash, cancellation);
                    return new ExplorerRun(spec, destination, null, null, reused);
                }
                staging = ExplorerStorage.Stage(spec.OutputRootPath, "cloud-catalog");
                long selected = 0; bool secondsOnly = true; List<DateTime> dates = new List<DateTime>();
                Dictionary<string, long> slices = new Dictionary<string, long>();
                long cloudCount;
                using (ExplorerStorage.RowWriter<ExplorerCloud> rows = new ExplorerStorage.RowWriter<ExplorerCloud>(staging, "catalog"))
                using (ExplorerStorage.RowWriter<ExplorerBar> bars = new ExplorerStorage.RowWriter<ExplorerBar>(staging, "bars"))
                using (BinaryWriter prefixes = new BinaryWriter(File.Create(Path.Combine(staging, "prefix-index.bin")), Encoding.UTF8))
                {
                    ExplorerCatalog catalog = new ExplorerCatalog(spec, rows.Add, prefix => ExplorerStorage.WritePrefix(prefixes, prefix, spec));
                    ExplorerBars barBuilder = new ExplorerBars(bars.Add);
                    while (reader.TryRead(out OrderFlowDeal tick))
                    {
                        if (!spec.Includes(tick.Time)) { continue; }
                        if (dates.Count == 0 || dates[dates.Count - 1] != tick.Time.Date) { dates.Add(tick.Time.Date); }
                        selected++; secondsOnly &= tick.Time.Ticks % TimeSpan.TicksPerSecond == 0;
                        string slice = tick.Time.ToString("yyyy-MM-dd HH", CultureInfo.InvariantCulture);
                        slices.TryGetValue(slice, out long count); slices[slice] = count + 1;
                        catalog.Add(tick, cancellation);
                        barBuilder.Add(tick);
                        if (selected % 8192 == 0)
                        {
                            ExplorerStorage.CheckMemory(spec);
                            progress?.Invoke(new ExplorerProgress("Catalog", selected, catalog.CompletedCount, tick.Time.Date, GC.GetTotalMemory(false), clock.Elapsed.TotalSeconds));
                        }
                    }
                    catalog.Complete(); barBuilder.Complete(); cloudCount = catalog.CompletedCount;
                }
                cancellation.ThrowIfCancellationRequested();
                ExplorerStorage.WriteCatalogSummary(staging, spec, cancellation);
                File.WriteAllText(Path.Combine(staging, "quality.json"), JsonSerializer.Serialize(new { metadata.RecordCount, ReadComplete = metadata.ReadComplete,
                    SelectedRows = selected, SecondsOnly = secondsOnly, Clock = "Unspecified source clock; equal times ordered only within this file", Slices = slices }, ExplorerStorage.Json));
                File.WriteAllText(Path.Combine(staging, "catalog-spec.json"), JsonSerializer.Serialize(spec with { InputPath = "", OutputRootPath = "", Episodes = new ExplorerEpisodeSpec(), Study = new ExplorerStudySpec { Enabled = false, SwingsEnabled = false } }, ExplorerStorage.Json));
                ExplorerManifest manifest = new ExplorerManifest(ExplorerRunSpec.CatalogVersion, spec.CatalogSpecHash, spec.InputSha256,
                    JsonSerializer.SerializeToElement(new { spec.PriceStep, spec.FromDate, spec.ToDate, spec.Profiles, Parser = OrderFlowResearchSchema.ParserVersion }),
                    metadata.RecordCount, selected, cloudCount, secondsOnly, dates.ToArray(), null);
                manifest = ExplorerStorage.Publish(staging, destination, manifest, cancellation); staging = null;
                progress?.Invoke(new ExplorerProgress("Catalog complete", selected, cloudCount, dates.LastOrDefault(), GC.GetTotalMemory(false), clock.Elapsed.TotalSeconds));
                return new ExplorerRun(spec, destination, null, null, manifest);
            }
            finally { if (staging != null && Directory.Exists(staging)) { Directory.Delete(staging, true); } }
        }
    }
}
