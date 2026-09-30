/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OsEngine.OsTrader.Grids.Futures2
{
    /// <summary>Versioned local checkpoint envelope with integrity detection, not authentication.</summary>
    public sealed class Futures2Envelope
    {
        /// <summary>SHA256 of the exact UTF8 payload.</summary>
        public string Hash { get; set; } = "";
        /// <summary>Serialized campaign projection; never contains credentials.</summary>
        public string Payload { get; set; } = "";
    }

    /// <summary>Flush-before-replace persistence used before every native side effect.</summary>
    /// <remarks>One owner per path. Corrupt primary data blocks recovery; backup is never silently treated
    /// as current execution knowledge. Caller must reconcile native facts after every restart.</remarks>
    public sealed class Futures2Store
    {
        private readonly string _path;
        /// <summary>Creates a store at a caller-owned dedicated file; does not access it yet.</summary>
        public Futures2Store(string path) { _path = Path.GetFullPath(path); }
        /// <summary>Derives a traversal-safe filename from a native robot identity.</summary>
        public static string Name(string identity) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))) + ".json";
        /// <summary>Reads a complete checkpoint, or returns null only when neither primary nor backup exists.</summary>
        public Futures2Checkpoint Load()
        {
            if (!File.Exists(_path))
            {
                if (File.Exists(_path + ".bak") || File.Exists(_path + ".tmp")) throw new IOException("Incomplete checkpoint installation: reconciliation required.");
                return null;
            }
            Futures2Envelope envelope = JsonSerializer.Deserialize<Futures2Envelope>(File.ReadAllText(_path, Encoding.UTF8));
            if (envelope == null || envelope.Hash != Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(envelope.Payload))))
                throw new InvalidDataException("Futures2 checkpoint integrity failure.");
            Futures2Checkpoint data = JsonSerializer.Deserialize<Futures2Checkpoint>(envelope.Payload);
            Validate(data);
            return data;
        }
        /// <summary>Writes and flushes a new checkpoint before atomically replacing the primary.</summary>
        public void Save(Futures2Checkpoint data)
        {
            Validate(data);
            string payload = JsonSerializer.Serialize(data);
            Futures2Envelope envelope = new Futures2Envelope { Payload = payload,
                Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))) };
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            using (FileStream stream = new FileStream(_path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { stream.Write(bytes); stream.Flush(true); }
            if (File.Exists(_path)) File.Replace(_path + ".tmp", _path, _path + ".bak");
            else File.Move(_path + ".tmp", _path);
        }
        /// <summary>Rejects older schemas for durable hedge, selected-level commands or execution policy fields.</summary>
        public static void ValidateSchema(Futures2Checkpoint data)
        {
            if (data == null || data.Schema < 1 || data.Schema > 5 || data.Plans == null || data.SelectedCancels == null)
                throw new InvalidDataException("Unsupported Futures2 checkpoint schema.");
            if (data.Schema < 3 && (data.Plans.Values.Any(p => p.Input.IsHedge)
                || data.PendingPlan?.Input.IsHedge == true || data.Rollover?.Plan?.Input.IsHedge == true))
                throw new InvalidDataException("Hedge plans require checkpoint schema 3 or later.");
            if (data.Schema < 4 && (data.EntryBatch != null || data.LevelEdit?.Levels != null || data.SelectedCancels.Count > 0))
                throw new InvalidDataException("Selected-level commands require checkpoint schema 4.");
            if (data.Schema < 5 && (data.Policy?.AscendingLevelPriority == true || data.Policy?.QuoteOrdinaryLimits == true
                || data.PendingPolicy?.AscendingLevelPriority == true || data.PendingPolicy?.QuoteOrdinaryLimits == true))
                throw new InvalidDataException("Execution policy options require checkpoint schema 5.");
        }

        /// <summary>Checks referential/quantity consistency without changing recovered state.</summary>
        public static void Validate(Futures2Checkpoint data)
        {
            ValidateSchema(data);
            if (string.IsNullOrWhiteSpace(data.Campaign)
                || data.Book == null || data.Plans == null || data.Policy == null)
                throw new InvalidDataException("Invalid campaign schema.");
            data.Policy.Validate();
            if (data.PendingPlan != null) Futures2Controller.ValidateHedgePolicy(data.PendingPlan, data.PendingPolicy);
            if (data.Rollover?.Plan != null && data.Rollover.State != "Canceled" && data.Rollover.State != "Applied")
                Futures2Controller.ValidateHedgePolicy(data.Rollover.Plan, data.Policy);
            foreach (Futures2Plan plan in data.Plans.Values.Where(p => p.Id == data.ActivePlan
                || data.Book.Lots.Any(l => l.PlanId == p.Id && l.Quantity > 0)))
                Futures2Controller.ValidateHedgePolicy(plan, data.Policy);
            if (data.ActivePlan.Length > 0 && (!data.Plans.ContainsKey(data.ActivePlan) || data.Capital < 0 || data.Capital == 0 && !data.Book.LocallyEmpty))
                throw new InvalidDataException("Missing active plan/capital.");
            if (data.Book.Intents.Select(i => i.Id).Distinct().Count() != data.Book.Intents.Count
                || data.Book.Lots.Select(l => l.Id).Distinct().Count() != data.Book.Lots.Count)
                throw new InvalidDataException("Duplicate ownership identity.");
            if (data.Book.Intents.Any(i => i.ClientKey == null)
                || data.Book.Intents.Where(i => i.ClientKey.Length > 0).Select(i => i.ClientKey).Distinct().Count()
                    != data.Book.Intents.Count(i => i.ClientKey.Length > 0))
                throw new InvalidDataException("Duplicate or missing signed broker correlation.");
            foreach (Futures2Intent intent in data.Book.Intents)
            {
                if (intent.ClientKey.Length > 0 && intent.ClientKey != "F2" + intent.Id)
                    throw new InvalidDataException("Signed broker correlation does not match durable intent.");
                if (!data.Plans.ContainsKey(intent.PlanId) || intent.Endpoint < 0 || intent.Endpoint > 1 || intent.Quantity <= 0
                    || intent.Allocations.Count == 0 || intent.Allocations.Sum(a => a.Quantity) != intent.Quantity
                    || intent.Allocations.Any(a => a.Quantity <= 0 || a.Filled < 0 || a.Filled > a.Quantity))
                    throw new InvalidDataException("Invalid native intent projection.");
            }
            foreach (Futures2Lot lot in data.Book.Lots)
                if (!data.Plans.ContainsKey(lot.PlanId) || lot.Quantity < 0 || (lot.Quantity == 0 && lot.Cost != 0))
                    throw new InvalidDataException("Invalid native inventory projection.");
            Futures2InventoryValidation.Validate(data);
            Futures2Commands.ValidateCommands(data);
        }
    }
}
