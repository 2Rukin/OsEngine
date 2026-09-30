/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Generic;
using System.Linq;

namespace OsEngine.OsTrader.Grids.Futures2
{
    /// <summary>Local knowledge of a native order; timeout is not cancellation or rejection.</summary>
    public enum Futures2IntentState { Recorded, SubmitPending, Working, Partial, CancelPending, Filled, Canceled, Rejected, Unknown }

    /// <summary>Deterministic allocation of one native order across campaign levels.</summary>
    public sealed class Futures2Allocation
    {
        /// <summary>Level ordinal in the intent's plan.</summary>
        public int LevelId { get; set; }
        /// <summary>Entry lot identity for a reduction; assigned from entry intent for increases.</summary>
        public string LotId { get; set; } = "";
        /// <summary>Requested allocated quantity.</summary>
        public decimal Quantity { get; set; }
        /// <summary>Quantity covered by identified native fills, monotonic.</summary>
        public decimal Filled { get; set; }
        /// <summary>Signed sum of identified fill price times allocated quantity.</summary>
        public decimal FilledCost { get; set; }
        /// <summary>Per-unit exit basis carried only by this replacement entry allocation.</summary>
        public decimal ExitCarry { get; set; }
    }

    /// <summary>Durable strategy-to-native intent link, recorded before invoking a native order API.</summary>
    /// <remarks>It is not an independent broker order. Only native journal fills may update its execution projection. THG-EXECUTION-001.</remarks>
    public sealed class Futures2Intent
    {
        /// <summary>Stable submit identity used in native signal tags and recovery.</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        /// <summary>Accepted plan identity.</summary>
        public string PlanId { get; set; } = "";
        /// <summary>Instrument endpoint index; old/new rollover tabs remain distinct.</summary>
        public int Endpoint { get; set; }
        /// <summary>True for an inventory increase, false for a reduction.</summary>
        public bool Entry { get; set; }
        /// <summary>Exit belongs to the current liquidation/reduction pass, not ordinary TP.</summary>
        public bool Protective { get; set; }
        /// <summary>Reduction generation; obsolete protective orders drain before a revised reduction.</summary>
        public string ReductionGeneration { get; set; } = "";
        /// <summary>Optional coordinated transfer correlation, fixed before submission.</summary>
        public string TransferId { get; set; } = "";
        /// <summary>Part of this entry's collateral explicitly funded by its inbound transfer.</summary>
        public decimal TransferBudget { get; set; }
        /// <summary>Per-transfer funding shares; one native entry may spend several confirmed credits.</summary>
        public Dictionary<string, decimal> TransferBudgets { get; set; } = new Dictionary<string, decimal>();
        /// <summary>Per-unit monetary fee fixed before submission for deterministic late-fill projection.</summary>
        public decimal FeePerUnit { get; set; }
        /// <summary>Confirmed terminal report retained while its execution details are pending.</summary>
        public Futures2IntentState? TerminalReport { get; set; }
        /// <summary>Explicit order type; a zero limit does not mean market.</summary>
        public bool Market { get; set; }
        /// <summary>Immutable requested native quantity.</summary>
        public decimal Quantity { get; set; }
        /// <summary>Signed tick-aligned requested price.</summary>
        public decimal Price { get; set; }
        /// <summary>Native position number after binding.</summary>
        public int PositionNumber { get; set; }
        /// <summary>Native user order number after binding.</summary>
        public int OrderNumber { get; set; }
        /// <summary>Venue identity when acknowledged.</summary>
        public string MarketNumber { get; set; } = "";
        /// <summary>Opaque opt-in transport reference, persisted before sending; empty on older checkpoints.</summary>
        public string ClientKey { get; set; } = "";
        /// <summary>Serial decision time of recording.</summary>
        public DateTime Created { get; set; }
        /// <summary>Last cancellation request time; null until requested.</summary>
        public DateTime? CancelRequested { get; set; }
        /// <summary>Current knowledge of this intent.</summary>
        public Futures2IntentState State { get; set; } = Futures2IntentState.Recorded;
        /// <summary>Monotonic reported execution, checked against identified fills.</summary>
        public decimal ReportedExecuted { get; set; }
        /// <summary>Ordered volume allocations; preserved across restart.</summary>
        public List<Futures2Allocation> Allocations { get; set; } = new List<Futures2Allocation>();
        /// <summary>Identified fill quantity projected from the native journal.</summary>
        public decimal Filled => Allocations.Sum(a => a.Filled);
        /// <summary>Whether the remaining requested quantity can still execute.</summary>
        public bool CanFill => State != Futures2IntentState.Filled && State != Futures2IntentState.Canceled && State != Futures2IntentState.Rejected;
        /// <summary>Reservation including Unknown and CancelPending outcomes.</summary>
        public decimal Remaining => CanFill ? Math.Max(0, Quantity - Filled) : 0;
    }

    /// <summary>Remaining inventory cost from identified native fills and separately persisted ownership operations.</summary>
    public sealed class Futures2Lot
    {
        /// <summary>Entry intent/allocation identity or explicit registration operation and level identity.</summary>
        public string Id { get; set; } = "";
        /// <summary>Original immutable plan.</summary>
        public string PlanId { get; set; } = "";
        /// <summary>Original level ordinal.</summary>
        public int LevelId { get; set; }
        /// <summary>Native position number, never inferred from price.</summary>
        public int PositionNumber { get; set; }
        /// <summary>Instrument endpoint.</summary>
        public int Endpoint { get; set; }
        /// <summary>Confirmed remaining quantity.</summary>
        public decimal Quantity { get; set; }
        /// <summary>Remaining raw entry cost in price times quantity; may be negative or zero.</summary>
        public decimal Cost { get; set; }
        /// <summary>Per-unit carried exit basis of remaining replacement inventory; fresh ordinary entries use zero.</summary>
        public decimal ExitCarry { get; set; }
        /// <summary>First native entry receipt, local registration time, or declared external increase time; retained on basis/release edits.</summary>
        public DateTime Opened { get; set; }
        /// <summary>Average of remaining quantity; absent when empty.</summary>
        public decimal? Average => Quantity > 0 ? Cost / Quantity : null;
    }

    /// <summary>Persistent projection and reservation book owned by a single serialized decision stream.</summary>
    /// <remarks>
    /// Native Journal remains the source of fills and order identities. This projection does not
    /// send orders or invent fills. Unknown outcomes retain reservations; only confirmed cancel
    /// releases a remainder. Public methods require the campaign owner's lock. THG-EXECUTION-001.
    /// </remarks>
    public sealed class Futures2Book
    {
        /// <summary>Monotone economic projection revision, including real fills and local ownership operations.</summary>
        public long InventoryRevision { get; set; }
        /// <summary>Native intent links, retained for late callbacks and recovery.</summary>
        public List<Futures2Intent> Intents { get; set; } = new List<Futures2Intent>();
        /// <summary>Remaining cost projections, including old-plan and rollover inventory.</summary>
        public List<Futures2Lot> Lots { get; set; } = new List<Futures2Lot>();
        /// <summary>Scoped native fill keys already projected; checkpoint dedup evidence.</summary>
        public HashSet<string> FillKeys { get; set; } = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>Realized money PnL, after explicitly configured per-unit fees.</summary>
        public decimal Realized { get; set; }
        /// <summary>Positive entry collateral consumed today; exits credit only when configured.</summary>
        public decimal DaySpent { get; set; }
        /// <summary>Date of the daily counter in the decision clock.</summary>
        public DateTime Day { get; set; }
        /// <summary>Count of confirmed native rejections, not timeouts.</summary>
        public int Rejections { get; set; }

        /// <summary>Creates a reservation after checking allocation totals and free held quantity.</summary>
        public void Record(Futures2Intent intent)
        {
            if (Intents.Any(i => i.Id == intent.Id) || intent.Quantity <= 0 || intent.Allocations.Count == 0
                || intent.Allocations.Any(a => a.Quantity <= 0 || a.Filled != 0) || intent.Allocations.Sum(a => a.Quantity) != intent.Quantity)
                throw new InvalidOperationException("Invalid or duplicate intent allocation.");
            if (!intent.Entry)
            {
                foreach (IGrouping<string, Futures2Allocation> group in intent.Allocations.GroupBy(a => a.LotId))
                {
                    Futures2Lot lot = Lots.Single(l => l.Id == group.Key);
                    if (group.Sum(a => a.Quantity) > Available(lot)) throw new InvalidOperationException("Reduction exceeds unreserved inventory.");
                }
            }
            Intents.Add(intent);
        }

        /// <summary>Returns quantity not already reserved for a potentially executable reduction.</summary>
        public decimal Available(Futures2Lot lot)
        {
            decimal reserved = Intents.Where(i => !i.Entry && i.CanFill).SelectMany(i => i.Allocations)
                .Where(a => a.LotId == lot.Id).Sum(a => a.Quantity - a.Filled);
            return Math.Max(0, lot.Quantity - reserved);
        }

        /// <summary>Returns held plus potentially executable increases for a level in one plan.</summary>
        public decimal Occupied(string planId, int level)
        {
            return Lots.Where(l => l.PlanId == planId && l.LevelId == level).Sum(l => l.Quantity)
                + Intents.Where(i => i.Entry && i.CanFill && i.PlanId == planId).SelectMany(i => i.Allocations)
                    .Where(a => a.LevelId == level).Sum(a => a.Quantity - a.Filled);
        }

        /// <summary>Projects one accepted native fill, allocating exactly once in immutable allocation order.</summary>
        /// <remarks>
        /// The caller supplies the original plan and a key scoped by endpoint/user-order/trade identity.
        /// Nonpositive/corrective or excessive fills fail before mutation and require reconciliation.
        /// Terminal cancel does not prevent a late valid fill from being recorded.
        /// </remarks>
        public bool Fill(Futures2Intent intent, Futures2Plan plan, string key, decimal quantity, decimal price,
            decimal feePerUnit, bool creditExits, DateTime time)
        {
            if (FillKeys.Contains(key)) return false;
            if (!Intents.Contains(intent)) throw new InvalidOperationException("Unowned intent.");
            long nextRevision = checked(InventoryRevision + 1);
            Futures2Book staged = System.Text.Json.JsonSerializer.Deserialize<Futures2Book>(System.Text.Json.JsonSerializer.Serialize(this));
            Futures2Intent stagedIntent = staged.Intents.Single(i => i.Id == intent.Id);
            staged.FillInPlace(stagedIntent, plan, key, quantity, price, feePerUnit, creditExits, time);
            Lots = staged.Lots; FillKeys = staged.FillKeys; Realized = staged.Realized;
            Day = staged.Day; DaySpent = staged.DaySpent; Rejections = staged.Rejections;
            intent.Allocations = stagedIntent.Allocations; intent.State = stagedIntent.State;
            InventoryRevision = nextRevision;
            return true;
        }

        private bool FillInPlace(Futures2Intent intent, Futures2Plan plan, string key, decimal quantity, decimal price,
            decimal feePerUnit, bool creditExits, DateTime time)
        {
            if (FillKeys.Contains(key)) return false;
            if (!Intents.Contains(intent) || quantity <= 0 || quantity > intent.Quantity - intent.Filled || feePerUnit < 0)
                throw new InvalidOperationException("Unsupported native fill or quantity mismatch.");
            if (Day.Date < time.Date) { Day = time.Date; DaySpent = 0; }
            // Validate the complete allocation before changing any quantity.
            decimal check = quantity;
            foreach (Futures2Allocation allocation in intent.Allocations)
            {
                decimal take = Math.Min(check, allocation.Quantity - allocation.Filled);
                if (!intent.Entry && take > 0 && Lots.Single(l => l.Id == allocation.LotId).Quantity < take)
                    throw new InvalidOperationException("Native close exceeds projected held quantity.");
                check -= take;
            }
            decimal remaining = quantity;
            checked
            {
                foreach (Futures2Allocation allocation in intent.Allocations)
                {
                    decimal take = Math.Min(remaining, allocation.Quantity - allocation.Filled);
                    if (take == 0) continue;
                    if (intent.Entry)
                    {
                        string id = intent.Id + ":" + allocation.LevelId;
                        Futures2Lot lot = Lots.Find(l => l.Id == id);
                        if (lot == null)
                        {
                            lot = new Futures2Lot { Id = id, PlanId = intent.PlanId, LevelId = allocation.LevelId, PositionNumber = intent.PositionNumber,
                                Endpoint = intent.Endpoint, Opened = time, ExitCarry = allocation.ExitCarry };
                            Lots.Add(lot);
                        }
                        lot.Cost += price * take;
                        lot.Quantity += take;
                        DaySpent += plan.Input.Collateral * take;
                        Realized -= feePerUnit * take;
                    }
                    else
                    {
                        Futures2Lot lot = Lots.Single(l => l.Id == allocation.LotId);
                        if (take > lot.Quantity) throw new InvalidOperationException("Native close exceeds remaining inventory.");
                        decimal average = lot.Cost / lot.Quantity;
                        Realized += (price - average) * take * (plan.IsLongInventory ? 1 : -1)
                            * plan.Input.TickValue / plan.Input.Tick - feePerUnit * take;
                        lot.Cost -= average * take;
                        lot.Quantity -= take;
                        if (lot.Quantity == 0) lot.Cost = 0;
                        if (creditExits) DaySpent = Math.Max(0, DaySpent - plan.Input.Collateral * take);
                    }
                    allocation.Filled += take;
                    allocation.FilledCost += price * take;
                    remaining -= take;
                    if (remaining == 0) break;
                }
            }
            FillKeys.Add(key);
            if (intent.Filled == intent.Quantity) intent.State = Futures2IntentState.Filled;
            else if (intent.TerminalReport.HasValue && intent.ReportedExecuted <= intent.Filled) intent.State = intent.TerminalReport.Value;
            else if (intent.ReportedExecuted > intent.Filled) intent.State = Futures2IntentState.Unknown;
            else if (intent.CanFill && intent.State != Futures2IntentState.CancelPending) intent.State = Futures2IntentState.Partial;
            return true;
        }

        /// <summary>Applies monotonic execution knowledge; terminal status without matching fill evidence remains Unknown.</summary>
        public void Observe(Futures2Intent intent, Futures2IntentState status, decimal reportedExecuted)
        {
            if (reportedExecuted < 0 || reportedExecuted > intent.Quantity) throw new InvalidOperationException("Invalid native execution report.");
            if (status == Futures2IntentState.Canceled || status == Futures2IntentState.Rejected || status == Futures2IntentState.Filled)
            {
                if (status == Futures2IntentState.Rejected && !intent.TerminalReport.HasValue) Rejections++;
                intent.TerminalReport = status;
            }
            intent.ReportedExecuted = Math.Max(intent.ReportedExecuted, status == Futures2IntentState.Filled ? intent.Quantity : reportedExecuted);
            if (intent.ReportedExecuted > intent.Filled)
            {
                intent.State = Futures2IntentState.Unknown;
                return;
            }
            if (intent.Filled == intent.Quantity) { intent.State = Futures2IntentState.Filled; return; }
            if (!intent.CanFill) return;
            if (intent.TerminalReport.HasValue) { intent.State = intent.TerminalReport.Value; return; }
            if (intent.State == Futures2IntentState.CancelPending && (status == Futures2IntentState.Working || status == Futures2IntentState.Partial)) return;
            intent.State = status;
        }

        /// <summary>Computes reserved planning collateral for every held or potentially fillable entry.</summary>
        public decimal Reserved(IReadOnlyDictionary<string, Futures2Plan> plans)
        {
            checked
            {
                return Lots.Sum(l => l.Quantity * plans[l.PlanId].Input.Collateral)
                    + Intents.Where(i => i.Entry).Sum(i => i.Remaining * plans[i.PlanId].Input.Collateral);
            }
        }

        /// <summary>Local empty predicate only; broker reconciliation is an additional condition for FlatConfirmed.</summary>
        public bool LocallyEmpty => Lots.All(l => l.Quantity == 0) && Intents.All(i => !i.CanFill);
    }
}
