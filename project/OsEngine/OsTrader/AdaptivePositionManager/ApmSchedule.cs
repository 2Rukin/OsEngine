using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace OsEngine.OsTrader.AdaptivePositionManager
{
    /// <summary>Versioned saved input. Risk locks remain outside the optimizer's searchable behavior parameters.</summary>
    public sealed record ApmScheduleDocument(string SchemaVersion, string DatasetName, string DatasetHash,
        ApmDataProfile Profile, ApmCampaignSpec[] Campaigns);

    /// <summary>
    /// Validated immutable schedule for one dedicated instrument/account. Campaign intervals must be disjoint
    /// in their original order; loading never sorts, changes locks, chooses a historical period or sends orders.
    /// </summary>
    /// <remarks>TradeOnly is the only implemented profile. Contract: APM-DATA-001, APM-RESEARCH-001.</remarks>
    public sealed class ApmSchedule
    {
        private readonly ApmCampaignSpec[] _campaigns;

        /// <summary>Immutable campaign records in explicit input order.</summary>
        public ReadOnlyCollection<ApmCampaignSpec> Campaigns { get; }
        /// <summary>SHA256 of the declared input dataset; verified separately against the actual selected file.</summary>
        public string DatasetHash { get; }
        /// <summary>Dataset label only; it is never opened as a path implicitly.</summary>
        public string DatasetName { get; }
        /// <summary>Canonical validated schedule hash on the current schema/runtime.</summary>
        public string Hash { get; }

        /// <summary>Reject unknown schema, unsupported profile, duplicates, overlaps and inconsistent metadata.</summary>
        public ApmSchedule(ApmScheduleDocument document)
        {
            if (document == null || document.SchemaVersion != "APM-Schedule-v1"
                || document.Profile != ApmDataProfile.TradeOnly || string.IsNullOrWhiteSpace(document.DatasetName)
                || document.DatasetHash == null || document.DatasetHash.Length != 64
                || !document.DatasetHash.All(Uri.IsHexDigit) || document.Campaigns == null
                || document.Campaigns.Length < 1 || document.Campaigns.Length > 1000)
                throw new ArgumentException("Invalid APM schedule schema, dataset hash, profile or campaign count.");
            _campaigns = (ApmCampaignSpec[])document.Campaigns.Clone();
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> signals = new HashSet<string>(StringComparer.Ordinal);
            ApmCampaignSpec first = _campaigns[0] ?? throw new ArgumentException("Missing campaign.");
            ApmCampaignSpec previous = null;
            foreach (ApmCampaignSpec spec in _campaigns)
            {
                if (spec == null) throw new ArgumentException("Missing campaign.");
                spec.Validate();
                if (!ids.Add(spec.CampaignId) || !signals.Add(spec.EntrySignalId)
                    || spec.Instrument != first.Instrument || spec.Account != first.Account || spec.Timezone != first.Timezone
                    || spec.RiskCurrency != first.RiskCurrency || spec.PriceStep != first.PriceStep
                    || spec.PriceStepCost != first.PriceStepCost || spec.VolumeStep != first.VolumeStep
                    || (previous != null && spec.EntryTime <= previous.SessionExitTime))
                    throw new ArgumentException("Schedule contains duplicate signals, overlapping campaigns or inconsistent metadata.");
                previous = spec;
            }
            Campaigns = Array.AsReadOnly(_campaigns);
            DatasetHash = document.DatasetHash.ToUpperInvariant();
            DatasetName = document.DatasetName;
            Hash = ApmTickReader.HashJson(Document());
        }

        /// <summary>Load only an explicitly selected small schedule file. Missing JSON fields never receive implicit locks.</summary>
        public static ApmSchedule Load(string path)
        {
            if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("APM schedule exceeds 4 MiB.");
            string json = File.ReadAllText(path);
            using JsonDocument source = JsonDocument.Parse(json);
            if (!source.RootElement.TryGetProperty("Campaigns", out JsonElement campaigns) || campaigns.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Missing APM campaign array.");
            ParameterInfo[] inputs = typeof(ApmCampaignSpec).GetConstructors().Single().GetParameters();
            foreach (JsonElement campaign in campaigns.EnumerateArray())
                foreach (ParameterInfo input in inputs)
                    if (campaign.ValueKind != JsonValueKind.Object || !campaign.TryGetProperty(input.Name, out _))
                        throw new InvalidDataException("Missing explicit APM campaign input: " + input.Name);
            return new ApmSchedule(JsonSerializer.Deserialize<ApmScheduleDocument>(json,
                new JsonSerializerOptions { RespectRequiredConstructorParameters = true }));
        }

        /// <summary>Save a new schedule artifact; an existing owner's file is not overwritten.</summary>
        public void Save(string path)
        {
            using FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(stream, Document(), new JsonSerializerOptions { WriteIndented = true });
            stream.Flush(true);
        }

        /// <summary>Reject dataset drift before research. This does not prove source liquidity, completeness or time precision.</summary>
        public void VerifyDataset(string selectedPath)
        {
            if (ApmTickReader.HashFile(selectedPath) != DatasetHash)
                throw new InvalidDataException("Selected dataset SHA256 differs from the saved schedule.");
        }

        /// <summary>
        /// Select whole campaigns inside the actual loaded Optimizer interval. Reject boundary cuts,
        /// missing warmup and empty phases; never infer a historical holdout or silently shift a lock.
        /// </summary>
        public ApmSchedule SelectPhase(DateTime start, DateTime end, int warmupSeconds)
        {
            if (start >= end || warmupSeconds < 0) throw new ArgumentException("Invalid phase bounds.");
            List<ApmCampaignSpec> selected = new List<ApmCampaignSpec>();
            foreach (ApmCampaignSpec campaign in _campaigns)
            {
                if (campaign.SessionExitTime < start || campaign.EntryTime > end) continue;
                if (campaign.EntryTime <= start.AddSeconds(warmupSeconds) || campaign.SessionExitTime >= end)
                    throw new ArgumentException("Phase cuts a campaign, its warmup or final fill interval.");
                selected.Add(campaign);
            }
            return new ApmSchedule(new ApmScheduleDocument("APM-Schedule-v1", DatasetName, DatasetHash,
                ApmDataProfile.TradeOnly, selected.ToArray()));
        }

        private ApmScheduleDocument Document() => new ApmScheduleDocument("APM-Schedule-v1", DatasetName,
            DatasetHash, ApmDataProfile.TradeOnly, (ApmCampaignSpec[])_campaigns.Clone());
    }

    /// <summary>Stream validation evidence. Counts describe the inspected file, not a claim about the underlying venue.</summary>
    public sealed record ApmDatasetReport(string Hash, long Rows, long UnknownSide, long CertifiedDuplicates,
        long ConflictingIds, long OutOfOrder, DateTime FirstTime, DateTime LastTime, bool IdCapacityReached,
        bool IdUniquenessCertified, string Status);

    /// <summary>Bounded offline validation in source order; no sorting, silent repair or guessed deduplication.</summary>
    public static class ApmDatasetValidation
    {
        /// <summary>
        /// Inspect an explicitly selected TradeOnly text file. Identity capacity exhaustion and conflicting/late
        /// records invalidate the input. Unknown Side is reported and is usable only by policies not using trade delta.
        /// </summary>
        public static ApmDatasetReport Inspect(string path, string source, string instrument,
            bool idsCertified, int identityCapacity = 250000)
        {
            ApmTickIdentityGuard identities = new ApmTickIdentityGuard(idsCertified, identityCapacity);
            string before = ApmTickReader.HashFile(path);
            long rows = 0, unknown = 0, duplicates = 0, conflicts = 0, late = 0;
            DateTime first = default, previous = default, last = default;
            bool capacity = false;
            using StreamReader reader = File.OpenText(path);
            foreach (ApmTick tick in ApmTickReader.Read(reader, source, instrument))
            {
                if (rows++ == 0) first = tick.Time;
                if (tick.Side == 0) unknown++;
                if (previous != default && tick.Time < previous) late++;
                previous = tick.Time; last = tick.Time;
                string quality = identities.Inspect(tick);
                if (quality == "DUPLICATE") duplicates++;
                if (quality == "CONFLICT") conflicts++;
                if (quality == "CAPACITY") capacity = true;
            }
            string after = ApmTickReader.HashFile(path);
            if (after != before) throw new InvalidDataException("Dataset changed during validation.");
            return new ApmDatasetReport(after, rows, unknown, duplicates, conflicts, late, first, last,
                capacity, idsCertified, rows == 0 || conflicts > 0 || late > 0 || capacity ? "InvalidForPerformance" : "ValidatedTradeOnly");
        }
    }
}
