/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OsEngine.OsTrader.AdaptivePositionManager
{
    /// <summary>
    /// Run provenance record. Callers must treat the RiskLocks array as read-only; record copies are shallow.
    /// Unknown native data hashes or fill assumptions are explicit strings, never inferred from a strategy
    /// name. Risk locks and behavioral values are stored separately.
    /// </summary>
    public sealed record ApmRunManifest(string SchemaVersion, string ModelVersion, string CommitSha,
        string RunId, string DatasetHash, string ScheduleHash, string Timezone, string FillModel,
        string CommissionModel, string SlippageModel, string EventOrderingPolicy, ApmDataProfile Profile,
        ApmPolicy Parameters, ApmCampaignSpec[] RiskLocks, int Seed, string CompletionStatus,
        string TrainBoundary = "NOT_SELECTED", string ValidationBoundary = "NOT_SELECTED", string TestBoundary = "NOT_SELECTED",
        string BuildHash = "UNVERIFIED", ApmOperationalLimits NativeLimits = null);

    /// <summary>Serializable audit row kept separate from trading state. Typed values support numeric sorting.</summary>
    public sealed record ApmAuditRow(long Sequence, DateTime Time, string CampaignId, string Kind,
        string Action, decimal Price, decimal Volume, decimal Filled, decimal Raw, decimal Allowed,
        string Reasons, string IntentId, ApmSnapshot Snapshot = null, ApmIntent Intent = null,
        string Side = "", decimal? Slippage = null, string PreviewState = null)
    {
        /// <summary>Audit-only origin such as tick, timer or callback; it never affects decisions.</summary>
        public string Source { get; init; } = "component";
    }

    /// <summary>Normalizes operator-entered paths before any file API and rejects hidden control characters.</summary>
    public static class ApmPathInput
    {
        /// <summary>Return one existing absolute file path or an error naming the affected parameter.</summary>
        public static string ExistingFile(string parameterName, string value)
        {
            string path = Normalize(parameterName, value);
            if (!File.Exists(path)) throw new FileNotFoundException(parameterName + ": file does not exist: '" + path + "'.", path);
            return path;
        }

        /// <summary>Return one absolute directory root. The caller may create it after all validation succeeds.</summary>
        public static string DirectoryRoot(string parameterName, string value) => Normalize(parameterName, value);

        private static string Normalize(string parameterName, string value)
        {
            string path = (value ?? "").Trim();
            if (path.Length == 0) throw new InvalidDataException(parameterName + ": path is empty.");
            if (path.Any(char.IsControl)) throw new InvalidDataException(parameterName + ": normalized path contains control characters.");
            try { return Path.GetFullPath(path); }
            catch (Exception error) when (error is ArgumentException || error is NotSupportedException || error is PathTooLongException)
            {
                throw new InvalidDataException(parameterName + ": invalid normalized path '" + path + "'.", error);
            }
        }
    }

    /// <summary>
    /// Single-owner append-only event journal and replaceable checksummed checkpoint.
    /// Prepared intents are flushed to disk before transport submission. Files contain no credentials.
    /// </summary>
    /// <remarks>
    /// IO failure propagates to the caller, who must suppress send and freeze reconciliation.
    /// File replacement cannot promise broker exactly-once or recovery without order-query evidence.
    /// Contract: APM-EXECUTION-001, APM-IMPLEMENTATION-001.
    /// </remarks>
    public sealed class ApmArtifacts : IDisposable
    {
        private readonly string _directory;
        private readonly FileStream _journal;
        private readonly Queue<ApmAuditRow> _recent = new Queue<ApmAuditRow>();
        private readonly int _recentLimit;
        private bool _disposed;
        private readonly ApmMetricsAccumulator _metrics = new ApmMetricsAccumulator();

        /// <summary>Whole-campaign metrics accumulated from every durable audit row, independent of bounded UI history.</summary>
        public ApmCampaignMetrics Metrics => _metrics.Snapshot;

        /// <summary>Create an isolated output directory; existing journals are never overwritten by a new run.</summary>
        public ApmArtifacts(string directory, int recentLimit = 2000)
        {
            if (recentLimit < 1 || recentLimit > 100000) throw new ArgumentOutOfRangeException(nameof(recentLimit));
            _directory = Path.GetFullPath(directory);
            Directory.CreateDirectory(_directory);
            _journal = new FileStream(Path.Combine(_directory, "events.jsonl"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            _recentLimit = recentLimit;
        }

        /// <summary>Write exact manifest values without modifying the selected schedule or native settings.</summary>
        public void WriteManifest(ApmRunManifest manifest) => WriteReplace("manifest.json", JsonSerializer.Serialize(manifest));

        /// <summary>Append and flush the current execution audit before any send/cancel side effect.</summary>
        public void Append(ApmAuditRow row)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(row) + "\n");
            _journal.Write(bytes);
            if (row.Kind != "Decision") _journal.Flush(true);
            _metrics.Add(row);
            _recent.Enqueue(row);
            while (_recent.Count > _recentLimit) _recent.Dequeue();
        }

        /// <summary>Detached bounded UI history; full execution evidence remains in the append-only journal.</summary>
        public ApmAuditRow[] Recent() => _recent.ToArray();

        /// <summary>Flush a versioned checkpoint to a sibling temp file, then replace its previous snapshot.</summary>
        public void SaveCheckpoint(ApmCampaign campaign)
        {
            string payload = campaign.ExportCheckpoint();
            string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
            WriteReplace("checkpoint.json", JsonSerializer.Serialize(new ApmCheckpointEnvelope("APM-Envelope-v1", digest, payload)));
        }

        /// <summary>Verify checksum and schema, then return a campaign frozen for broker reconciliation.</summary>
        public static ApmCampaign LoadCheckpoint(string path)
        {
            if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidDataException("APM checkpoint envelope exceeds 32 MiB.");
            ApmCheckpointEnvelope envelope = JsonSerializer.Deserialize<ApmCheckpointEnvelope>(File.ReadAllText(path));
            if (envelope == null || envelope.Version != "APM-Envelope-v1" || envelope.Payload == null
                || envelope.Hash != Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(envelope.Payload))))
                throw new InvalidDataException("APM checkpoint checksum or schema mismatch.");
            return ApmCampaign.Recover(envelope.Payload);
        }

        /// <summary>Write a filter-selected UI export without changing canonical event order or trading state.</summary>
        public static void ExportCsv(string path, IEnumerable<ApmAuditRow> rows)
        {
            using StreamWriter writer = new StreamWriter(path, false, new UTF8Encoding(true));
            writer.WriteLine("Sequence,Time,CampaignId,Source,Kind,Action,Price,Volume,Filled,Raw,Allowed,Reasons,IntentId,Curve,Kappa,Policy,PendingIncrease,PendingReduce,Regime,Equity,Drawdown,Fees,MaximumVolume,Turnover,ExitReason,Ready,Quality,BrokerId,Side,IsLimit,OrderState,OrderFilled,OrderRemaining,SlippagePrice");
            foreach (ApmAuditRow row in rows)
            {
                object[] fields = { row.Sequence, row.Time.ToString("O"), row.CampaignId, row.Source, row.Kind, row.Action,
                    row.Price, row.Volume, row.Filled, row.Raw, row.Allowed, row.Reasons, row.IntentId,
                    row.Snapshot?.Decision?.CurveTarget, row.Snapshot?.Decision?.Kappa, row.Snapshot?.Decision?.PolicyTarget,
                    row.Snapshot?.PendingIncrease, row.Snapshot?.PendingReduce, row.Snapshot?.Regime,
                    row.Snapshot?.Equity, row.Snapshot?.MaximumDrawdown, row.Snapshot?.Fees, row.Snapshot?.MaximumVolume,
                    row.Snapshot?.Turnover, row.Snapshot?.ExitReason, row.Snapshot?.Market?.Ready, row.Snapshot?.Market?.Quality,
                    row.Intent?.BrokerId, row.Side, row.Intent?.IsLimit, row.Intent?.State, row.Intent?.Filled,
                    row.Intent?.Remaining, row.Slippage };
                for (int i = 0; i < fields.Length; i++)
                {
                    if (i > 0) writer.Write(',');
                    string field = Convert.ToString(fields[i], System.Globalization.CultureInfo.InvariantCulture) ?? "";
                    writer.Write('"'); writer.Write(field.Replace("\"", "\"\"")); writer.Write('"');
                }
                writer.WriteLine();
            }
        }

        private void WriteReplace(string name, string json)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            string path = Path.Combine(_directory, name);
            string temp = path + ".tmp";
            using (FileStream stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                stream.Write(bytes);
                stream.Flush(true);
            }
            File.Move(temp, path, true);
        }

        /// <summary>Close only audit resources. Disposing a view or artifact store never closes a position.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _journal.Dispose(); _disposed = true;
        }

        private sealed record ApmCheckpointEnvelope(string Version, string Hash, string Payload);
    }
}
