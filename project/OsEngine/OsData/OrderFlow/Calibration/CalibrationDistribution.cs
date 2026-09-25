/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;

namespace OsEngine.OsData.OrderFlow.Calibration
{
    /// <summary>Worker-owned scratch transaction for exact statistics; never writes into an immutable study.</summary>
    /// <remarks>ORDER-FLOW-CLOUD-CALIBRATION-001. Dispose removes only this uniquely created temporary directory.
    /// Scratch plus study artifacts share the disk budget; cancellation, memory and disk failures propagate to the caller.
    /// Values are bounded in RAM independently of observation cardinality. No events are retained here.</remarks>
    internal sealed class CalibrationStatisticsWorkspace : IDisposable
    {
        private readonly CalibrationSpec _spec;
        private readonly string _artifacts;
        private readonly CancellationToken _cancellation;
        private long _bytes;
        internal string DirectoryPath { get; }
        internal long ScratchBytes => _bytes;
        internal CalibrationStatisticsWorkspace(CalibrationSpec spec, string artifacts, CancellationToken cancellation)
        {
            _spec = spec; _artifacts = artifacts; _cancellation = cancellation;
            DirectoryPath = Path.Combine(Path.GetTempPath(), "OsEngine-calibration-statistics-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
        }
        internal void Check()
        {
            _cancellation.ThrowIfCancellationRequested();
            CalibrationEngine.CheckMemory(_spec);
            long artifacts = Directory.Exists(_artifacts) ? Directory.EnumerateFiles(_artifacts).Sum(p => new FileInfo(p).Length) : 0;
            if (_bytes > _spec.MaximumCacheBytes - artifacts) { throw new InvalidDataException("Превышен лимит artifacts и временной статистики calibration."); }
        }
        internal void ReserveRecord()
        {
            _bytes = checked(_bytes + 24);
            if (_bytes > _spec.MaximumCacheBytes) { throw new InvalidDataException("Превышен лимит временной статистики calibration."); }
        }
        internal void Delete(string path)
        {
            long bytes = new FileInfo(path).Length;
            File.Delete(path); _bytes -= bytes;
        }
        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath)) { Directory.Delete(DirectoryPath, true); }
            _bytes = 0;
        }
    }

    /// <summary>Exact decimal nearest-rank and histogram using fixed-size sorted buffers and disk-backed counted runs.</summary>
    /// <remarks>One owner thread; Snapshot seals the collector. Binary compaction has at most 64 run slots and two input streams,
    /// not a tree node per distinct value. Equal decimals are folded without rounding; histogram groups the same consecutive
    /// distinct values as the original implementation. Dispose releases the buffer and all owned scratch runs.</remarks>
    internal sealed class CalibrationDistribution : IDisposable
    {
        private readonly CalibrationStatisticsWorkspace _workspace;
        private readonly string _prefix;
        private readonly string[] _runs = new string[64];
        private decimal[] _buffer;
        private int _used, _serial;
        private long _count;
        private DistributionSummary _snapshot;
        internal int BufferCapacity => _buffer?.Length ?? 0;

        internal CalibrationDistribution(CalibrationStatisticsWorkspace workspace, int capacity)
        {
            _workspace = workspace; _buffer = new decimal[Math.Max(1, Math.Min(4096, capacity))];
            _prefix = Path.Combine(workspace.DirectoryPath, Guid.NewGuid().ToString("N"));
        }
        internal void Add(decimal value)
        {
            if (_buffer == null || _snapshot != null) { throw new InvalidOperationException("Statistics collector is sealed or disposed."); }
            _count = checked(_count + 1); _buffer[_used++] = value;
            if (_used == _buffer.Length) { Flush(); }
        }
        private string NewPath() => _prefix + "-" + checked(++_serial) + ".run";
        private void Write(BinaryWriter writer, decimal value, long count)
        {
            _workspace.ReserveRecord(); writer.Write(value); writer.Write(count);
        }
        private void Flush()
        {
            if (_used == 0) { return; }
            _workspace.Check(); Array.Sort(_buffer, 0, _used);
            string path = NewPath();
            using (BinaryWriter writer = new BinaryWriter(File.Create(path)))
            {
                for (int i = 0; i < _used;)
                {
                    decimal value = _buffer[i]; int start = i++;
                    while (i < _used && _buffer[i] == value) { i++; }
                    Write(writer, value, i - start);
                }
            }
            _used = 0;
            int level = 0;
            while (level < _runs.Length && _runs[level] != null)
            {
                path = Merge(_runs[level], path); _runs[level++] = null;
            }
            if (level == _runs.Length) { throw new InvalidDataException("Statistics count exceeds run capacity."); }
            _runs[level] = path; _workspace.Check();
        }
        private string Merge(string leftPath, string rightPath)
        {
            _workspace.Check();
            string path = NewPath();
            using (BinaryReader left = new BinaryReader(File.OpenRead(leftPath)))
            using (BinaryReader right = new BinaryReader(File.OpenRead(rightPath)))
            using (BinaryWriter output = new BinaryWriter(File.Create(path)))
            {
                bool hasLeft = Next(left, out decimal lv, out long lc), hasRight = Next(right, out decimal rv, out long rc);
                long written = 0;
                while (hasLeft || hasRight)
                {
                    if (!hasRight || hasLeft && lv < rv)
                    { Write(output, lv, lc); hasLeft = Next(left, out lv, out lc); }
                    else if (!hasLeft || rv < lv)
                    { Write(output, rv, rc); hasRight = Next(right, out rv, out rc); }
                    else
                    { Write(output, lv, checked(lc + rc)); hasLeft = Next(left, out lv, out lc); hasRight = Next(right, out rv, out rc); }
                    if (++written % 4096 == 0) { _workspace.Check(); }
                }
            }
            _workspace.Delete(leftPath); _workspace.Delete(rightPath); _workspace.Check(); return path;
        }
        private static bool Next(BinaryReader reader, out decimal value, out long count)
        {
            value = 0; count = 0;
            if (reader.BaseStream.Position == reader.BaseStream.Length) { return false; }
            value = reader.ReadDecimal(); count = reader.ReadInt64(); return true;
        }
        internal DistributionSummary Snapshot()
        {
            if (_snapshot != null) { return _snapshot; }
            if (_buffer == null) { throw new ObjectDisposedException(nameof(CalibrationDistribution)); }
            Flush(); string sorted = null;
            for (int i = 0; i < _runs.Length; i++)
            {
                if (_runs[i] == null) { continue; }
                sorted = sorted == null ? _runs[i] : Merge(_runs[i], sorted); _runs[i] = null;
            }
            List<DistributionPoint> histogram = new List<DistributionPoint>();
            decimal?[] quantiles = new decimal?[8];
            if (sorted != null)
            {
                using (BinaryReader reader = new BinaryReader(File.OpenRead(sorted)))
                {
                    long distinct = reader.BaseStream.Length / 24, group = Math.Max(1, (distinct + 39) / 40);
                    decimal[] percentiles = { .5m, .75m, .9m, .95m, .99m, .995m, .999m, 1m };
                    long[] ranks = percentiles.Select(p => (long)decimal.Ceiling(p * _count)).ToArray();
                    long observed = 0, index = 0, bucketCount = 0;
                    int rank = 0; decimal anchor = 0;
                    while (Next(reader, out decimal value, out long count))
                    {
                        if (index % group == 0)
                        {
                            if (bucketCount > 0) { histogram.Add(new DistributionPoint(anchor, bucketCount)); }
                            anchor = value; bucketCount = 0;
                        }
                        observed = checked(observed + count); bucketCount += count;
                        while (rank < ranks.Length && observed >= ranks[rank]) { quantiles[rank++] = value; }
                        if (++index % 4096 == 0) { _workspace.Check(); }
                    }
                    if (bucketCount > 0) { histogram.Add(new DistributionPoint(anchor, bucketCount)); }
                    if (observed != _count) { throw new InvalidDataException("Statistics count mismatch."); }
                }
                _workspace.Delete(sorted);
            }
            _workspace.Check(); _buffer = null;
            _snapshot = new DistributionSummary(_count, quantiles[0], quantiles[1], quantiles[2], quantiles[3],
                quantiles[4], quantiles[5], quantiles[6], quantiles[7], histogram.ToImmutableArray());
            return _snapshot;
        }
        public void Dispose()
        {
            _buffer = null;
            foreach (string path in Directory.EnumerateFiles(_workspace.DirectoryPath, Path.GetFileName(_prefix) + "-*.run"))
            { _workspace.Delete(path); }
            Array.Clear(_runs); _used = 0;
        }
    }
}
