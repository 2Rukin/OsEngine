using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using OsEngine.Entity;

namespace OsEngine.OsTrader.Grids.Futures2
{
    /// <summary>Local ownership changes are distinct from explicitly attributed external executions.</summary>
    public enum Futures2InventoryKind { RegisterToCapacity, SetQuantity, SetBasis, ExternalIncrease, ExternalDecrease }

    /// <summary>Operator request. Quantity is a per-level target for SetQuantity, an execution delta for External methods.</summary>
    public sealed class Futures2InventoryRequest
    {
        /// <summary>Stable caller-supplied replay identity, at most 64 ASCII letters/digits/dash/underscore.</summary>
        public string Id { get; set; } = "";
        /// <summary>Accepted immutable plan owning the selected levels.</summary>
        public string PlanId { get; set; } = "";
        /// <summary>Native tab ordinal, zero or one.</summary>
        public int Endpoint { get; set; }
        /// <summary>Distinct selected level ordinals, processed in supplied order.</summary>
        public List<int> Levels { get; set; } = new List<int>();
        /// <summary>Ownership operation or separately attributed actual external execution.</summary>
        public Futures2InventoryKind Kind { get; set; }
        /// <summary>Per-level target for SetQuantity, positive execution delta for External methods.</summary>
        public decimal Quantity { get; set; }
        /// <summary>Explicit literal basis/execution price; null means absent, not zero.</summary>
        public decimal? Price { get; set; }
        /// <summary>RegisterToCapacity uses each plan level price instead of Price.</summary>
        public bool UseLevelPrices { get; set; }
        /// <summary>Operator execution provenance unique within this campaign; no cross-robot claim registry.</summary>
        public string ExecutionReference { get; set; } = "";
        /// <summary>Known mode-clock external execution time; absent for ownership operations.</summary>
        public DateTime? ExecutedAt { get; set; }
        /// <summary>Total nonnegative monetary fee of the declared external execution.</summary>
        public decimal Fee { get; set; }
    }

    /// <summary>Native before-image and adjustment, persisted before Journal publication.</summary>
    public sealed class Futures2NativeInventoryChange
    {
        /// <summary>Native before-image including real history, frozen routing metadata and inventory provenance.</summary>
        public string Snapshot { get; set; } = "";
        /// <summary>No-send mutation prepared at the captured native execution boundary.</summary>
        public PositionInventoryAdjustment Adjustment { get; set; }
    }

    /// <summary>Two-store recovery record. Committed Book state must never be replaced by this historical after-image.</summary>
    public sealed class Futures2InventoryOperation
    {
        /// <summary>Detached immutable-by-convention operator request used for replay comparison.</summary>
        public Futures2InventoryRequest Request { get; set; }
        /// <summary>Frozen hash of server/account/instrument/execution profile.</summary>
        public string EndpointIdentity { get; set; } = "";
        /// <summary>Mode-clock operation time, or explicitly declared external execution time.</summary>
        public DateTime At { get; set; }
        /// <summary>True only when the Book after-image is included in the same persisted checkpoint.</summary>
        public bool Committed { get; set; }
        /// <summary>Required Book economic revision before the operation.</summary>
        public long Revision { get; set; }
        /// <summary>Complete captured lot projection before any ownership mutation.</summary>
        public List<Futures2Lot> Before { get; set; }
        /// <summary>Complete lot projection used only when committing Prepared, never on Committed replay.</summary>
        public List<Futures2Lot> After { get; set; }
        /// <summary>Captured campaign realized money used for Prepared conflict checks.</summary>
        public decimal RealizedBefore { get; set; }
        /// <summary>Resulting campaign realized money; unchanged for pure ownership operations.</summary>
        public decimal RealizedAfter { get; set; }
        /// <summary>Captured day execution collateral spend.</summary>
        public decimal DaySpentBefore { get; set; }
        /// <summary>Resulting day spend; ownership changes do not consume or credit it.</summary>
        public decimal DaySpentAfter { get; set; }
        /// <summary>Captured day counter date.</summary>
        public DateTime DayBefore { get; set; }
        /// <summary>Resulting day counter date; external execution uses its declared time.</summary>
        public DateTime DayAfter { get; set; }
        /// <summary>Native position changes persisted before Journal mutation.</summary>
        public List<Futures2NativeInventoryChange> Native { get; set; } = new List<Futures2NativeInventoryChange>();
    }

    public sealed partial class Futures2NativeAdapter
    {
        private static T InventoryCopy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value));
        private bool InventoryPending => Engine.Data.InventoryOperations.Any(o => !o.Committed);

        /// <summary>Registers, releases or reprices explicit owned quantity without sending orders.</summary>
        /// <remarks>Requires drained orders and a paused campaign. External execution additionally requires a
        /// stable operator-supplied execution reference and known price. After publication automatic trading
        /// remains blocked until native/account reconciliation. Account deltas never imply ownership.</remarks>
        /// <exception cref="InvalidOperationException">Ownership, lifecycle, persistence or reconciliation conflict.</exception>
        public void AdjustInventory(Futures2InventoryRequest request)
        {
            lock (Sync)
            {
                if (_disposed || _persistenceFailed) throw new InvalidOperationException("Inventory owner unavailable.");
                if (request == null || string.IsNullOrWhiteSpace(request.Id) || request.Id.Length > 64
                    || request.Id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
                    throw new ArgumentException("Provide a stable inventory operation ID using letters, digits, dash or underscore (up to 64).");
                Futures2InventoryOperation prior = Engine.Data.InventoryOperations.Find(o => o.Request.Id == request.Id);
                if (prior != null)
                {
                    if (JsonSerializer.Serialize(prior.Request) != JsonSerializer.Serialize(request))
                        throw new InvalidOperationException("Inventory operation ID has a different payload.");
                    RecoverInventory(); return;
                }
                Futures2Checkpoint data = Engine.Data;
                if (InventoryPending || data.State == Futures2State.Active || data.Emergency || data.PendingPlan != null
                    || data.Rollover != null && data.Rollover.State != "Applied" && data.Rollover.State != "Canceled"
                    || data.Book.Intents.Any(i => i.CanFill))
                    throw new InvalidOperationException("Pause, drain all orders and finish pending transitions before inventory adjustment.");
                if (!data.Plans.TryGetValue(request.PlanId, out Futures2Plan plan) || request.Endpoint < 0 || request.Endpoint > 1
                    || plan.EndpointIdentity.Length == 0 || plan.EndpointIdentity != EndpointIdentity(request.Endpoint)
                    || _tabs[request.Endpoint].Security == null || _tabs[request.Endpoint].Security.Name != plan.Input.Instrument
                    || request.Levels == null || request.Levels.Count == 0 || request.Levels.Distinct().Count() != request.Levels.Count
                    || request.Levels.Any(l => !plan.Levels.Any(p => p.Id == l)) || !Enum.IsDefined(request.Kind)
                    || request.Quantity < 0 || request.Quantity % plan.Input.VolumeStep != 0 || request.Fee < 0
                    || request.Price.HasValue && !SignedPriceMath.HasFivePlaces(request.Price.Value))
                    throw new ArgumentException("Invalid inventory scope, quantity or literal price.");
                bool execution = request.Kind == Futures2InventoryKind.ExternalIncrease || request.Kind == Futures2InventoryKind.ExternalDecrease;
                if (execution && (request.Levels.Count != 1 || request.Quantity <= 0 || !request.Price.HasValue
                    || !request.ExecutedAt.HasValue || request.ExecutedAt.Value == default || request.ExecutedAt.Value > Now
                    || string.IsNullOrWhiteSpace(request.ExecutionReference)
                    || data.InventoryOperations.Any(o => o.Request.ExecutionReference == request.ExecutionReference)))
                    throw new ArgumentException("External execution needs one level, quantity, price and unused reference.");
                if (!execution && (request.ExecutionReference.Length > 0 || request.Fee != 0 || request.ExecutedAt.HasValue))
                    throw new ArgumentException("Ownership registration has no execution reference or fee.");
                if (request.UseLevelPrices && request.Kind != Futures2InventoryKind.RegisterToCapacity)
                    throw new ArgumentException("Level prices apply only to registration to capacity.");
                foreach (Position position in _tabs[request.Endpoint].PositionsAll ?? new List<Position>())
                    if (HasWorking(position) || position.StopOrderIsActive || position.ProfitOrderIsActive
                        || position.OpenVolume != 0 && !Own(position))
                        throw new InvalidOperationException("Dedicated native journal contains pending or foreign obligations.");
                Futures2InventoryOperation operation = StageInventory(request, plan);
                ValidateInventoryAccount(operation);
                PrepareNativeInventory(operation, plan);
                data.Schema = Math.Max(data.Schema, 2); data.InventoryOperations.Add(operation);
                Reconciled = false; data.ResumeAfterConfigure = false;
                Engine.Reconcile("Prepared inventory operation " + request.Id);
                Save(); // Must precede any Journal quantity/basis change.
                _log("Inventory prepared " + request.Id + "; awaiting native publication");
                RecoverInventory();
                _log("Inventory committed " + request.Id + " " + request.Kind + "; reconcile before resume");
            }
        }

        private Futures2InventoryOperation StageInventory(Futures2InventoryRequest request, Futures2Plan plan)
        {
            Futures2Book book = Engine.Data.Book;
            Futures2InventoryOperation operation = new Futures2InventoryOperation { Request = InventoryCopy(request),
                EndpointIdentity = plan.EndpointIdentity, At = request.ExecutedAt ?? Now, Revision = book.InventoryRevision,
                Before = InventoryCopy(book.Lots), After = InventoryCopy(book.Lots), RealizedBefore = book.Realized,
                RealizedAfter = book.Realized, DaySpentBefore = book.DaySpent, DaySpentAfter = book.DaySpent,
                DayBefore = book.Day, DayAfter = book.Day };
            bool execution = request.Kind == Futures2InventoryKind.ExternalIncrease || request.Kind == Futures2InventoryKind.ExternalDecrease;
            if (execution && operation.DayAfter.Date < operation.At.Date) { operation.DayAfter = operation.At.Date; operation.DaySpentAfter = 0; }
            bool chargeDay = execution && operation.DayAfter.Date == operation.At.Date;
            checked
            {
                foreach (int level in request.Levels)
                {
                    List<Futures2Lot> lots = operation.After.Where(l => l.PlanId == plan.Id && l.LevelId == level && l.Quantity > 0).ToList();
                    if (lots.Any(l => l.Endpoint != request.Endpoint)) throw new InvalidOperationException("Level belongs to a different endpoint.");
                    decimal held = lots.Sum(l => l.Quantity);
                    decimal capacity = plan.Levels.Single(l => l.Id == level).Volume;
                    decimal target = request.Kind == Futures2InventoryKind.RegisterToCapacity ? capacity
                        : request.Kind == Futures2InventoryKind.SetQuantity ? request.Quantity
                        : request.Kind == Futures2InventoryKind.ExternalIncrease ? held + request.Quantity
                        : request.Kind == Futures2InventoryKind.ExternalDecrease ? held - request.Quantity : held;
                    if (target < 0 || target > capacity) throw new ArgumentException("Inventory target exceeds level capacity or held quantity.");
                    if (request.Kind == Futures2InventoryKind.SetBasis)
                    {
                        if (!request.Price.HasValue || held == 0) throw new ArgumentException("Basis requires known price and held quantity.");
                        foreach (Futures2Lot lot in lots) lot.Cost = request.Price.Value * lot.Quantity;
                    }
                    else if (target > held)
                    {
                        decimal? price = request.UseLevelPrices ? plan.Levels.Single(l => l.Id == level).Price : request.Price;
                        if (!price.HasValue) throw new ArgumentException("Registration increase requires an explicit basis.");
                        decimal increase = target - held;
                        operation.After.Add(new Futures2Lot { Id = "R:" + request.Id + ":" + level, PlanId = plan.Id,
                            Endpoint = request.Endpoint, LevelId = level, Quantity = increase, Cost = increase * price.Value,
                            Opened = operation.At });
                        if (chargeDay) operation.DaySpentAfter += increase * plan.Input.Collateral;
                    }
                    else if (target < held)
                    {
                        decimal reduce = held - target;
                        // Release preserves the level's weighted basis; no actual sale is inferred.
                        decimal levelBasis = lots.Sum(l => l.Cost) / held;
                        decimal left = reduce;
                        for (int index = 0; index < lots.Count; index++)
                        {
                            Futures2Lot lot = lots[index];
                            decimal take = Math.Min(left, lot.Quantity);
                            left -= take;
                            decimal basis = execution ? lot.Cost / lot.Quantity : levelBasis;
                            if (execution) operation.RealizedAfter += (request.Price.Value - basis) * take
                                * (plan.IsLongInventory ? 1 : -1) * plan.Input.TickValue / plan.Input.Tick;
                            lot.Quantity -= take; lot.Cost = basis * lot.Quantity;
                        }
                        if (left != 0 || lots.Any(l => l.Quantity < 0)) throw new InvalidOperationException("Cannot allocate ownership reduction.");
                        if (chargeDay && Engine.Data.Policy.CreditDayExits)
                            operation.DaySpentAfter = Math.Max(0, operation.DaySpentAfter - reduce * plan.Input.Collateral);
                    }
                }
                operation.RealizedAfter -= request.Fee;
                if (operation.After.Sum(l => l.Quantity * Engine.Data.Plans[l.PlanId].Input.Collateral) > Engine.Data.Capital)
                    throw new InvalidOperationException("Registered inventory exceeds campaign capital.");
            }
            return operation;
        }

        private void ValidateInventoryAccount(Futures2InventoryOperation operation)
        {
            int endpoint = operation.Request.Endpoint;
            if (!_live || IsPaper(endpoint)) return;
            if (_servers[endpoint] is not OsEngine.Market.Servers.IExplicitAccountSource source || !source.HasExplicitAccountUpdates
                || !_accountNet[endpoint].HasValue || _accountSequence[endpoint] <= _fillSequence[endpoint]
                || _accountAt[endpoint] < _fillAt[endpoint]
                || Now - _accountAt[endpoint] > TimeSpan.FromSeconds(Engine.Data.Policy.FreshnessSeconds)
                || RequiresSignedTransport(endpoint) && (!CurrentSource(endpoint, _sourceSession[endpoint])
                    || _sourceAccountSequence[endpoint] <= _sourceFillSequence[endpoint]))
                throw new InvalidOperationException("Fresh actual account evidence required for inventory attribution.");
            decimal future = operation.After.Where(l => l.Endpoint == endpoint).Sum(l => l.Quantity
                * (Engine.Data.Plans[l.PlanId].IsLongInventory ? 1 : -1));
            if (_accountNet[endpoint] != future + ExternalNet[endpoint])
                throw new InvalidOperationException("Post-operation own plus declared external net does not match account.");
        }

        private void PrepareNativeInventory(Futures2InventoryOperation operation, Futures2Plan plan)
        {
            int endpoint = operation.Request.Endpoint;
            foreach (Futures2Lot lot in operation.After.Where(l => l.PositionNumber == 0 && l.Quantity > 0))
                lot.PositionNumber = NumberGen.GetNumberDeal(_tabs[endpoint].StartProgram);
            int[] affected = operation.After.Where(l => l.PlanId == plan.Id && operation.Request.Levels.Contains(l.LevelId))
                .Select(l => l.PositionNumber).Distinct().ToArray();
            foreach (int number in affected)
            {
                Futures2Lot[] before = operation.Before.Where(l => l.Endpoint == endpoint && l.PositionNumber == number).ToArray();
                Futures2Lot[] after = operation.After.Where(l => l.Endpoint == endpoint && l.PositionNumber == number).ToArray();
                if (before.Sum(l => l.Quantity) == after.Sum(l => l.Quantity) && before.Sum(l => l.Cost) == after.Sum(l => l.Cost)) continue;
                Position position = (_tabs[endpoint].PositionsAll ?? new List<Position>()).SingleOrDefault(p => p.Number == number);
                if (position != null && !Own(position)) throw new InvalidOperationException("Foreign native position number.");
                if (position == null && before.Any(l => l.Quantity > 0)) throw new InvalidOperationException("Owned native position is missing.");
                if (position == null)
                    position = new Position { Number = number, NameBot = _tabs[endpoint].TabName,
                        NameBotClass = "Futures2Grid", SignalTypeOpen = "F2:" + Engine.Data.Campaign + ":R:" + operation.Request.Id,
                        Direction = plan.IsLongInventory ? Side.Buy : Side.Sell,
                        SecurityName = plan.Input.Instrument, Lots = _tabs[endpoint].Security.Lot,
                        PriceStep = plan.Input.Tick, PriceStepCost = plan.Input.TickValue,
                        State = PositionStateType.Done };
                string snapshot = _tabs[endpoint].CaptureInventory(position, plan.Input.PercentBase, operation.At);
                Position captured = new Position(); captured.SetDealFromString(snapshot);
                if (captured.OpenVolume != before.Sum(l => l.Quantity)) throw new InvalidOperationException("Native quantity changed during inventory preparation.");
                PositionInventoryValue value = InventoryCopy(captured.Inventory.Value);
                decimal delta = after.Sum(l => l.Quantity) - before.Sum(l => l.Quantity);
                value.Quantity += delta; value.Cost += after.Sum(l => l.Cost) - before.Sum(l => l.Cost);
                if (delta > 0) value.Entered += delta;
                if (value.Quantity == 0) { value.Cost = 0; value.ClosedAt = operation.At; }
                else { value.LastBasis = value.Cost / value.Quantity; value.ClosedAt = default; }
                if (operation.Request.Kind == Futures2InventoryKind.ExternalDecrease)
                {
                    decimal costRemoved = before.Sum(l => l.Cost) - after.Sum(l => l.Cost);
                    value.Realized += (operation.Request.Price.Value * -delta - costRemoved)
                        * (plan.IsLongInventory ? 1 : -1);
                }
                if (operation.Request.Fee > 0)
                    value.ExternalFees += operation.Request.Fee * Math.Abs(delta) / operation.Request.Quantity;
                operation.Native.Add(new Futures2NativeInventoryChange { Snapshot = snapshot,
                    Adjustment = captured.PrepareInventory(operation.Request.Id + ":" + number, value) });
            }
            if (operation.Native.Count == 0) throw new InvalidOperationException("Inventory request makes no change.");
        }

        /// <summary>Replays durable native ownership records; only Prepared records may advance the Book.</summary>
        private void RecoverInventory()
        {
            foreach (Futures2InventoryOperation operation in Engine.Data.InventoryOperations)
            {
                int endpoint = operation.Request.Endpoint;
                if (operation.EndpointIdentity != EndpointIdentity(endpoint))
                    throw new InvalidOperationException("Inventory recovery endpoint identity mismatch.");
                Futures2Book book = Engine.Data.Book;
                if (!operation.Committed && (book.InventoryRevision != operation.Revision
                    || JsonSerializer.Serialize(book.Lots) != JsonSerializer.Serialize(operation.Before)
                    || book.Realized != operation.RealizedBefore || book.DaySpent != operation.DaySpentBefore || book.Day != operation.DayBefore))
                    throw new InvalidOperationException("Prepared inventory Book pre-state mismatch.");
                foreach (Futures2NativeInventoryChange native in operation.Native)
                {
                    Position snapshot = new Position(); snapshot.SetDealFromString(native.Snapshot);
                    if (!Own(snapshot)) throw new InvalidOperationException("Inventory snapshot belongs to another campaign.");
                    _tabs[endpoint].ApplyInventory(snapshot, native.Adjustment);
                    if (!operation.Committed) _log("Inventory native applied " + operation.Request.Id + ":" + snapshot.Number);
                }
                if (operation.Committed) continue;
                book.Lots = InventoryCopy(operation.After); book.Realized = operation.RealizedAfter;
                book.DaySpent = operation.DaySpentAfter; book.Day = operation.DayAfter;
                book.InventoryRevision = checked(operation.Revision + 1); operation.Committed = true;
                Reconciled = false; Engine.Data.ResumeAfterConfigure = false;
                Engine.Reconcile("Inventory committed " + operation.Request.Id + "; reconcile native/account state");
                Save();
            }
        }
    }
}
