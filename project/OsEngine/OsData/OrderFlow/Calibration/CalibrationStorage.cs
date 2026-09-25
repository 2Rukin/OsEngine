/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.Entity;
using OsEngine.OsData.OrderFlow.Explorer;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace OsEngine.OsData.OrderFlow.Calibration
{
    internal sealed record CalibrationManifest(string Version, string Formulas, string Hash, CalibrationSpec Spec,
        CalibrationQuality Quality, int ActiveDays, ImmutableArray<ParameterCell> Cells, Dictionary<string, string> Checksums)
    {
        public ImmutableArray<DateTime> RangeDates { get; init; } = ImmutableArray<DateTime>.Empty;
    }
    internal sealed record CalibrationRun(string Directory, CalibrationManifest Manifest)
    {
        // Session-only source selection used to reuse a prepared snapshot; local paths are not exported into the manifest.
        internal string SourcePath { get; init; }
        internal CalibrationSpec Spec => Manifest.Spec;
        internal ParameterCell Cell(FormationSpec formation) => Manifest.Cells.First(c => c.Formation == formation);
    }

    /// <summary>Compact fixed-size normalized disk cache. Iterators own handles; input is never retained as a tick object graph.</summary>
    /// <remarks>One validated source pass writes selected dates. Cache keeps source sequences even when date filtering leaves gaps.</remarks>
    internal static class CalibrationCache
    {
        internal const int RecordBytes = 49;
        internal static void Write(BinaryWriter writer, OrderFlowDeal tick)
        { writer.Write(tick.SourceSequence); writer.Write(tick.Time.Ticks); writer.Write(tick.Price); writer.Write(tick.Volume); writer.Write((byte)(tick.Side == Side.Buy ? 1 : 2)); }

        internal static IEnumerable<OrderFlowDeal> Read(string directory, CancellationToken cancellation, long firstSequence = 0, long lastSequence = long.MaxValue)
        {
            using BinaryReader reader = new BinaryReader(File.OpenRead(Path.Combine(directory, "ticks.cache")), Encoding.UTF8);
            long length = reader.BaseStream.Length;
            if (length % RecordBytes != 0) { throw new InvalidDataException("Повреждён compact tick cache."); }
            if (firstSequence > 0)
            {
                long lo = 0, hi = length / RecordBytes;
                while (lo < hi)
                {
                    cancellation.ThrowIfCancellationRequested(); long mid = lo + (hi - lo) / 2;
                    reader.BaseStream.Position = mid * RecordBytes;
                    if (reader.ReadInt64() < firstSequence) { lo = mid + 1; } else { hi = mid; }
                }
                reader.BaseStream.Position = lo * RecordBytes;
            }
            while (reader.BaseStream.Position < length)
            {
                cancellation.ThrowIfCancellationRequested();
                long sequence = reader.ReadInt64(); if (sequence > lastSequence) { yield break; }
                DateTime time = new DateTime(reader.ReadInt64(), DateTimeKind.Unspecified);
                decimal price = reader.ReadDecimal(), volume = reader.ReadDecimal(); byte side = reader.ReadByte();
                if (side != 1 && side != 2 || price <= 0 || volume <= 0) { throw new InvalidDataException("Повреждена запись tick cache."); }
                yield return new OrderFlowDeal { SourceSequence = sequence, Time = time, Price = price, Volume = volume, Side = side == 1 ? Side.Buy : Side.Sell };
            }
        }
    }

    /// <summary>Checksummed immutable bundles and independent mutable presentation index. Publication is a same-root directory rename.</summary>
    /// <remarks>ORDER-FLOW-CLOUD-CALIBRATION-001. Only owned staging files are removed on failure/cancel; existing differing bundles survive.</remarks>
    internal static class CalibrationStorage
    {
        #region Immutable bundles

        internal static CalibrationRun Publish(string staging, string root, CalibrationManifest manifest, CancellationToken cancellation)
        {
            Dictionary<string, string> checksums = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string path in System.IO.Directory.GetFiles(staging).OrderBy(p => p, StringComparer.Ordinal))
            { checksums.Add(Path.GetFileName(path), ExplorerStorage.HashFile(path, cancellation)); }
            manifest = manifest with { Checksums = checksums };
            File.WriteAllText(Path.Combine(staging, "manifest.json"), JsonSerializer.Serialize(manifest, ExplorerStorage.Json), new UTF8Encoding(false));
            string destination = Path.Combine(root, "cloud-calibration-" + manifest.Hash);
            cancellation.ThrowIfCancellationRequested();
            if (System.IO.Directory.Exists(destination))
            {
                CalibrationRun existing = Open(destination, cancellation);
                if (existing.Manifest.Hash != manifest.Hash || existing.Manifest.Checksums.Count != checksums.Count ||
                    checksums.Any(p => !existing.Manifest.Checksums.TryGetValue(p.Key, out string value) || value != p.Value))
                { throw new InvalidDataException("Существующий bundle отличается; он сохранён без изменений."); }
                System.IO.Directory.Delete(staging, true); return existing;
            }
            System.IO.Directory.Move(staging, destination);
            return new CalibrationRun(destination, manifest);
        }

        internal static CalibrationRun Open(string directory, CancellationToken cancellation)
        {
            CalibrationManifest manifest = JsonSerializer.Deserialize<CalibrationManifest>(File.ReadAllText(Path.Combine(directory, "manifest.json")), ExplorerStorage.Json);
            if (manifest?.Spec == null || manifest.Version != CalibrationSpec.Version || manifest.Formulas != CalibrationSpec.Formulas ||
                manifest.Hash != manifest.Spec.Hash || manifest.Checksums == null || !manifest.Checksums.ContainsKey("ticks.cache") ||
                !manifest.Checksums.ContainsKey("bars.bin") || !manifest.Checksums.ContainsKey("bars.idx") ||
                manifest.Cells.IsDefault || manifest.Spec.TickDistributionOnly != manifest.Cells.IsEmpty || manifest.Quality?.InputSha256 != manifest.Spec.InputSha256)
            { throw new InvalidDataException("Неизвестная или несовместимая схема calibration bundle."); }
            manifest.Spec.Validate();
            HashSet<string> allowed = new HashSet<string>(StringComparer.Ordinal) { "ticks.cache", "bars.bin", "bars.idx" };
            foreach (ParameterCell cell in manifest.Cells)
            {
                if (cell.FormationHash != manifest.Spec.FormationHash(cell.Formation) || cell.FileName != "events-" + cell.FormationHash ||
                    !manifest.Checksums.ContainsKey(cell.FileName + ".bin") || !manifest.Checksums.ContainsKey(cell.FileName + ".idx"))
                { throw new InvalidDataException("Несогласованная formation identity."); }
                allowed.Add(cell.FileName + ".bin"); allowed.Add(cell.FileName + ".idx");
            }
            if (manifest.Checksums.Keys.Any(k => !allowed.Contains(k))) { throw new InvalidDataException("Неизвестный файл в calibration manifest."); }
            foreach (KeyValuePair<string, string> checksum in manifest.Checksums)
            {
                cancellation.ThrowIfCancellationRequested();
                if (Path.GetFileName(checksum.Key) != checksum.Key || ExplorerStorage.HashFile(Path.Combine(directory, checksum.Key), cancellation) != checksum.Value)
                { throw new InvalidDataException("Повреждён calibration artifact: " + Path.GetFileName(checksum.Key)); }
            }
            return new CalibrationRun(Path.GetFullPath(directory), manifest);
        }

        internal static IEnumerable<CalibrationEvent> Events(CalibrationRun run, FormationSpec formation, CancellationToken cancellation)
        {
            ParameterCell cell = run.Cell(formation);
            foreach (CalibrationEvent item in ExplorerStorage.ReadRows<CalibrationEvent>(run.Directory, cell.FileName))
            { cancellation.ThrowIfCancellationRequested(); yield return item; }
        }

        #endregion

        #region Rules and view state

        internal static void SaveRule(string outputRoot, CloudRule rule, CancellationToken cancellation, string previousRuleId = null)
        {
            rule.Validate();
            CalibrationRun run = Open(rule.BundlePath, cancellation);
            if (run.Spec.Hash != rule.Provenance.Hash || !run.Manifest.Cells.Any(c => c.FormationHash == rule.FormationHash))
            { throw new InvalidDataException("Правило не соответствует сохранённому formation catalog."); }
            string directory = Path.Combine(outputRoot, "cloud-calibration-rules"); System.IO.Directory.CreateDirectory(directory);
            using FileStream writerLock = new FileStream(Path.Combine(directory, "rules.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            List<CloudRule> rules = LoadRules(outputRoot, cancellation).ToList();
            if (previousRuleId != null && previousRuleId != rule.RuleId)
            { int old = rules.FindIndex(r => r.RuleId == previousRuleId); if (old >= 0) { rules[old] = rules[old] with { Enabled = false, Visible = false }; } }
            int index = rules.FindIndex(r => r.RuleId == rule.RuleId);
            if (index >= 0) { rules[index] = rule; } else { rules.Add(rule); }
            if (rules.Count > 1024) { throw new InvalidDataException("Превышен лимит 1024 сохранённых правил."); }
            AtomicJson(Path.Combine(directory, "rules.json"), rules, cancellation);
        }

        internal static ImmutableArray<CloudRule> LoadRules(string outputRoot, CancellationToken cancellation)
        {
            string directory = Path.Combine(outputRoot, "cloud-calibration-rules");
            string path = Path.Combine(directory, "rules.json");
            if (!File.Exists(path)) { return ImmutableArray<CloudRule>.Empty; }
            ImmutableArray<CloudRule> rules = JsonSerializer.Deserialize<ImmutableArray<CloudRule>>(File.ReadAllText(path), ExplorerStorage.Json);
            if (rules.IsDefault || rules.Length > 1024) { throw new InvalidDataException("Некорректный индекс сохранённых правил."); }
            HashSet<string> ids = new HashSet<string>();
            foreach (CloudRule rule in rules)
            {
                cancellation.ThrowIfCancellationRequested();
                if (rule == null) { throw new InvalidDataException("Пустой Cloud rule."); } rule.Validate();
                if (!ids.Add(rule.RuleId)) { throw new InvalidDataException("Повтор CloudRuleId в индексе правил."); }
            }
            return rules;
        }

        internal static void AtomicJson<T>(string destination, T value, CancellationToken cancellation)
        {
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(value, ExplorerStorage.Json), new UTF8Encoding(false));
                cancellation.ThrowIfCancellationRequested(); File.Move(temporary, destination, true);
            }
            finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
        }

        #endregion
    }
}
