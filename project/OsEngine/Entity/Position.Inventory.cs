using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace OsEngine.Entity
{
    /// <summary>Remaining ownership basis and cumulative execution result, in price times quantity.</summary>
    public sealed class PositionInventoryValue
    {
        /// <summary>Remaining managed native quantity; zero means released or closed.</summary>
        public decimal Quantity { get; set; }
        /// <summary>Remaining signed price times quantity, excluding ExitCarry and fees.</summary>
        public decimal Cost { get; set; }
        /// <summary>Cumulative entered or registered quantity; release does not reduce it.</summary>
        public decimal Entered { get; set; }
        /// <summary>Cumulative realized price times quantity; basis edits preserve past results.</summary>
        public decimal Realized { get; set; }
        /// <summary>Defined reporting basis retained when quantity reaches zero.</summary>
        public decimal LastBasis { get; set; }
        /// <summary>Explicit external execution fees in account money.</summary>
        public decimal ExternalFees { get; set; }
        /// <summary>Mode-clock completion time, or default while inventory remains.</summary>
        public DateTime ClosedAt { get; set; }
    }

    /// <summary>One identified native execution observed after inventory accounting was enabled.</summary>
    public sealed class PositionInventoryFill
    {
        /// <summary>Native order, trade and signed trading-date deduplication key.</summary>
        public string Key { get; set; } = "";
        /// <summary>True adds inventory; false applies a real close against remaining basis.</summary>
        public bool Entry { get; set; }
        /// <summary>Positive identified native execution quantity.</summary>
        public decimal Quantity { get; set; }
        /// <summary>Literal signed execution price, including zero.</summary>
        public decimal Price { get; set; }
        /// <summary>Native execution time used when inventory becomes flat.</summary>
        public DateTime At { get; set; }
    }

    /// <summary>Durable no-send adjustment at an explicit native execution boundary.</summary>
    /// <remarks>Before/After exclude later real fills. Replay preserves those fills in receipt order.
    /// The owner must persist this request before applying it, then reconcile both journals.</remarks>
    public sealed class PositionInventoryAdjustment
    {
        /// <summary>Stable unique operation identity within this native position.</summary>
        public string Id { get; set; } = "";
        /// <summary>Required prior manual operation; empty for the first adjustment.</summary>
        public string PreviousId { get; set; } = "";
        /// <summary>Captured projection before this operation and before later real fills.</summary>
        public PositionInventoryValue Before { get; set; }
        /// <summary>Validated projection after this operation, excluding later real fills.</summary>
        public PositionInventoryValue After { get; set; }
        /// <summary>Real executions already included at the captured boundary.</summary>
        public HashSet<string> KnownKeys { get; set; } = new HashSet<string>();
    }

    /// <summary>Opt-in inventory ledger; actual orders and MyTrade remain unmodified sources of execution.</summary>
    public sealed class PositionInventory
    {
        /// <summary>Inventory envelope version; unknown versions fail during load.</summary>
        public int Version { get; set; } = 1;
        /// <summary>Frozen native server routing identity; not a credential.</summary>
        public string ServerName { get; set; } = "";
        /// <summary>Frozen native account identity; not inferred from net quantity.</summary>
        public string PortfolioName { get; set; } = "";
        /// <summary>Positive independent price-unit denominator for native percentage display.</summary>
        public decimal PercentBase { get; set; }
        /// <summary>Frozen native reporting lot multiplier; campaign monetary conversion is separate.</summary>
        public decimal LotMultiplier { get; set; } = 1;
        /// <summary>Mode-clock creation time for registered-only positions.</summary>
        public DateTime CreatedAt { get; set; }
        /// <summary>Current remaining basis and cumulative economic projection.</summary>
        public PositionInventoryValue Value { get; set; } = new PositionInventoryValue();
        /// <summary>Most recently applied manual operation identity.</summary>
        public string LastId { get; set; } = "";
        /// <summary>Operation ID to exact serialized payload, retained for replay conflict detection.</summary>
        public Dictionary<string, string> Applied { get; set; } = new Dictionary<string, string>();
        /// <summary>All native executions reflected in this projection.</summary>
        public HashSet<string> KnownKeys { get; set; } = new HashSet<string>();
        /// <summary>Receipt-ordered real executions accepted since inventory accounting was enabled.</summary>
        public List<PositionInventoryFill> Fills { get; set; } = new List<PositionInventoryFill>();
        /// <summary>Last explicitly supplied liquidation quote; absent until observed.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public decimal? LastMark { get; set; }
        /// <summary>Unprojectable real execution remains in native orders and blocks ownership adjustments.</summary>
        public string Fault { get; set; } = "";

        internal static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value));

        internal static void Execute(PositionInventoryValue value, PositionInventoryFill fill, Side direction)
        {
            checked
            {
                if (fill.Quantity <= 0) throw new InvalidOperationException("Invalid inventory fill quantity.");
                if (fill.Entry)
                {
                    value.Quantity += fill.Quantity; value.Entered += fill.Quantity;
                    value.Cost += fill.Price * fill.Quantity;
                    value.LastBasis = value.Cost / value.Quantity;
                    value.ClosedAt = default;
                }
                else
                {
                    if (fill.Quantity > value.Quantity) throw new InvalidOperationException("Native close exceeds managed inventory; reconciliation required.");
                    decimal basis = value.Cost / value.Quantity;
                    value.Realized += (fill.Price - basis) * fill.Quantity * (direction == Side.Buy ? 1 : -1);
                    value.Quantity -= fill.Quantity;
                    value.Cost = basis * value.Quantity;
                    value.LastBasis = basis;
                    if (value.Quantity == 0) value.ClosedAt = fill.At;
                }
            }
        }
    }

    public partial class Position
    {
        /// <summary>Explicit ownership ledger, absent for legacy positions. Mutation requires Journal serialization.</summary>
        public PositionInventory Inventory { get; private set; }

        /// <summary>Freezes current identified native accounting without creating orders, fills or quantity.</summary>
        /// <remarks>Caller holds the Journal mutation lock. Native execution details and frozen account/server
        /// identity must agree. Incomplete facts fail before enabling the projection. THG-INVENTORY-009.</remarks>
        public void EnableInventory(string server, string portfolio, decimal percentBase, DateTime at)
        {
            if (Inventory != null)
            {
                if (Inventory.ServerName != server || Inventory.PortfolioName != portfolio)
                    throw new InvalidOperationException("Inventory owner metadata changed.");
                return;
            }
            if (percentBase <= 0 || at == default) throw new ArgumentException("Inventory requires a positive percentage base and explicit time.");
            Order[] orders = InventoryOrders().ToArray();
            if (orders.Length > 0 && (ServerName != server || PortfolioName != portfolio))
                throw new InvalidOperationException("Inventory metadata does not match the native opening owner.");
            if (orders.Any(o => o.VolumeExecute != (o.MyTrades?.Sum(t => t.Volume) ?? 0)))
                throw new InvalidOperationException("Native execution details are incomplete.");
            decimal basis = EntryPrice;
            decimal remaining = OpenVolume;
            if (remaining < 0) throw new InvalidOperationException("Cannot register a surplus close.");
            decimal closed = CloseOrders?.Sum(o => o.MyTrades?.Sum(t => t.Volume) ?? 0) ?? 0;
            decimal closeCost = CloseOrders?.Sum(o => o.MyTrades?.Sum(t => t.Volume * t.Price) ?? 0) ?? 0;
            Inventory = new PositionInventory
            {
                ServerName = server, PortfolioName = portfolio, PercentBase = percentBase,
                LotMultiplier = IsLotServer() && Lots > 0 ? Lots : 1,
                CreatedAt = TimeCreate == default ? at : TimeCreate,
                Value = new PositionInventoryValue { Quantity = remaining, Cost = remaining * basis,
                    Entered = MaxVolume, LastBasis = basis,
                    Realized = (closeCost - closed * basis) * (Direction == Side.Buy ? 1 : -1),
                    ClosedAt = remaining == 0 ? at : default },
                KnownKeys = orders.SelectMany(o => (o.MyTrades ?? new List<MyTrade>()).Select(t => InventoryKey(o, t))).ToHashSet()
            };
        }

        /// <summary>Captures an immutable operation boundary; caller supplies the separately validated economic result.</summary>
        /// <remarks>Caller serializes with Journal mutations and persists the returned request before applying it.
        /// This method does not verify broker ownership or available account funds. THG-INVENTORY-009.</remarks>
        public PositionInventoryAdjustment PrepareInventory(string id, PositionInventoryValue after)
        {
            if (Inventory == null || Inventory.Fault.Length > 0 || string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("Healthy inventory ledger/id required.");
            CheckInventoryValue(after);
            return new PositionInventoryAdjustment { Id = id, PreviousId = Inventory.LastId,
                Before = PositionInventory.Copy(Inventory.Value), After = PositionInventory.Copy(after),
                KnownKeys = new HashSet<string>(Inventory.KnownKeys) };
        }

        /// <summary>Applies once, preserving later identified executions; conflicting provenance fails before mutation.</summary>
        /// <remarks>Caller holds the Journal lock and has durably saved the request. Missing ordered execution
        /// provenance blocks replay; Committed strategy state is never reconstructed here. THG-INVENTORY-009.</remarks>
        public void ApplyInventory(PositionInventoryAdjustment adjustment)
        {
            if (Inventory == null || Inventory.Fault.Length > 0 || adjustment == null || string.IsNullOrWhiteSpace(adjustment.Id))
                throw new InvalidOperationException("Missing native inventory operation.");
            string payload = JsonSerializer.Serialize(adjustment);
            if (Inventory.Applied.TryGetValue(adjustment.Id, out string previous))
            {
                if (previous != payload) throw new InvalidOperationException("Inventory operation ID payload conflict.");
                return;
            }
            if (Inventory.LastId != adjustment.PreviousId || !adjustment.KnownKeys.IsSubsetOf(Inventory.KnownKeys))
                throw new InvalidOperationException("Native inventory history does not match prepared operation.");
            CheckInventoryValue(adjustment.Before); CheckInventoryValue(adjustment.After);
            PositionInventoryValue before = PositionInventory.Copy(adjustment.Before);
            PositionInventoryValue after = PositionInventory.Copy(adjustment.After);
            foreach (PositionInventoryFill fill in Inventory.Fills.Where(f => !adjustment.KnownKeys.Contains(f.Key)))
            {
                PositionInventory.Execute(before, fill, Direction);
                PositionInventory.Execute(after, fill, Direction);
            }
            if (JsonSerializer.Serialize(before) != JsonSerializer.Serialize(Inventory.Value))
                throw new InvalidOperationException("Native inventory pre-state changed.");
            Inventory.Value = after;
            Inventory.Applied.Add(adjustment.Id, payload); Inventory.LastId = adjustment.Id;
            State = after.Quantity == 0 ? PositionStateType.Done : PositionStateType.Open;
            bool marked = Inventory.LastMark.HasValue;
            SetSignedBidAsk(marked, Inventory.LastMark ?? 0, marked, Inventory.LastMark ?? 0);
        }

        private IEnumerable<Order> InventoryOrders() => (OpenOrders ?? new List<Order>()).Concat(CloseOrders ?? new List<Order>()).Where(o => o != null);
        private static string InventoryKey(Order order, MyTrade trade) => order.NumberUser + ":" + trade.NumberTrade + ":"
            + (trade.SignedIdentity == null ? "" : trade.Time.Date.Ticks.ToString());

        private void ProjectInventoryTrade(Order order, bool entry)
        {
            if (Inventory == null || order.MyTrades == null) return;
            foreach (MyTrade trade in order.MyTrades)
            {
                string key = InventoryKey(order, trade);
                if (Inventory.KnownKeys.Contains(key)) continue;
                PositionInventoryFill fill = new PositionInventoryFill { Key = key, Entry = entry,
                    Quantity = trade.Volume, Price = trade.Price, At = trade.Time };
                PositionInventoryValue next = PositionInventory.Copy(Inventory.Value);
                try { PositionInventory.Execute(next, fill, Direction); }
                catch (Exception error) when (error is InvalidOperationException || error is OverflowException)
                {
                    // Preserve the real native execution for reconciliation even when local ownership cannot absorb it.
                    Inventory.Fault = error.Message; State = PositionStateType.ClosingSurplus; return;
                }
                Inventory.Value = next; Inventory.KnownKeys.Add(key); Inventory.Fills.Add(fill);
            }
        }

        private static void CheckInventoryValue(PositionInventoryValue value)
        {
            if (value == null || value.Quantity < 0 || value.Entered < value.Quantity || value.Entered < 0
                || value.Quantity == 0 && value.Cost != 0 || value.ExternalFees < 0)
                throw new InvalidOperationException("Invalid inventory projection.");
        }

        private void CheckInventoryNativeFacts()
        {
            if (Inventory == null || Inventory.Fault.Length > 0) return;
            HashSet<string> actual = InventoryOrders().SelectMany(o => (o.MyTrades ?? new List<MyTrade>())
                .Select(t => InventoryKey(o, t))).ToHashSet();
            if (!Inventory.KnownKeys.SetEquals(actual) || Inventory.Fills.Any(f => !actual.Contains(f.Key)))
                throw new InvalidOperationException("Inventory/native execution snapshot mismatch.");
        }

        private sealed class InventoryEnvelope
        {
            public string Native { get; set; } = "";
            public PositionInventory Inventory { get; set; }
        }

        private StringBuilder WrapInventory(StringBuilder native) => Inventory == null ? native
            : new StringBuilder("INV1:").Append(Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(
                new InventoryEnvelope { Native = native.ToString(), Inventory = Inventory })));

        private string ReadInventory(string saved)
        {
            if (!saved.StartsWith("INV", StringComparison.Ordinal)) { Inventory = null; return saved; }
            if (!saved.StartsWith("INV1:", StringComparison.Ordinal)) throw new InvalidOperationException("Unknown inventory position version.");
            InventoryEnvelope envelope = JsonSerializer.Deserialize<InventoryEnvelope>(Convert.FromBase64String(saved.Substring(5)));
            PositionInventory ledger = envelope?.Inventory;
            if (ledger == null || ledger.Version != 1 || ledger.PercentBase <= 0 || ledger.LotMultiplier <= 0
                || ledger.CreatedAt == default || ledger.Applied == null || ledger.KnownKeys == null || ledger.Fills == null
                || ledger.LastId.Length > 0 && !ledger.Applied.ContainsKey(ledger.LastId))
                throw new InvalidOperationException("Invalid inventory position envelope.");
            CheckInventoryValue(ledger.Value);
            Inventory = ledger;
            return envelope.Native;
        }
    }
}
