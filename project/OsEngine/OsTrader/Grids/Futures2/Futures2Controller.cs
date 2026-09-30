/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Generic;
using System.Linq;
using OsEngine.Entity;

namespace OsEngine.OsTrader.Grids.Futures2
{
    /// <summary>A requested native side effect; the adapter persists the changed checkpoint before executing it.</summary>
    public sealed class Futures2Action
    {
        /// <summary>Cancellation of an existing native identity instead of a submit.</summary>
        public bool Cancel { get; set; }
        /// <summary>Stable intent reference owned by the checkpoint.</summary>
        public Futures2Intent Intent { get; set; }
    }

    /// <summary>Versioned strategy checkpoint; no secrets, live delegates or saved quotes are included.</summary>
    public sealed class Futures2Checkpoint
    {
        /// <summary>Unsupported versions fail closed during load.</summary>
        public int Schema { get; set; } = 1;
        /// <summary>Prepared and committed no-send ownership operations; schema 2 or later is required when nonempty.</summary>
        public List<Futures2InventoryOperation> InventoryOperations { get; set; } = new List<Futures2InventoryOperation>();
        /// <summary>Unique campaign identifier, independent of robot display name.</summary>
        public string Campaign { get; set; } = Guid.NewGuid().ToString("N");
        /// <summary>Campaign lifecycle.</summary>
        public Futures2State State { get; set; } = Futures2State.Draft;
        /// <summary>Every referenced old/current plan, retained while events can still arrive.</summary>
        public Dictionary<string, Futures2Plan> Plans { get; set; } = new Dictionary<string, Futures2Plan>();
        /// <summary>Active plan identifier.</summary>
        public string ActivePlan { get; set; } = "";
        /// <summary>Active instrument endpoint.</summary>
        public int Endpoint { get; set; }
        /// <summary>Accepted runtime policy.</summary>
        public Futures2Policy Policy { get; set; } = new Futures2Policy();
        /// <summary>Replacement plan waiting for the cancellation/reconciliation barrier.</summary>
        public Futures2Plan PendingPlan { get; set; }
        /// <summary>Runtime policy waiting for the same barrier.</summary>
        public Futures2Policy PendingPolicy { get; set; }
        /// <summary>Endpoint for a pending plan.</summary>
        public int PendingEndpoint { get; set; }
        /// <summary>Whether ordinary entries should resume after accepted reconfiguration.</summary>
        public bool ResumeAfterConfigure { get; set; }
        /// <summary>Fixed positive capital limit and return denominator.</summary>
        public decimal Capital { get; set; }
        /// <summary>Capital waiting for atomic configuration publication.</summary>
        public decimal PendingCapital { get; set; }
        /// <summary>Cumulative confirmed transfer credits minus debits, independent of manual initial capital.</summary>
        public decimal FundingDelta { get; set; }
        /// <summary>Positive initial return basis retained when the source transfers all capital away.</summary>
        public decimal ReturnBase { get; set; }
        /// <summary>Voluntary helper is draining its orders before recovery.</summary>
        public bool RecoverReduction { get; set; }
        /// <summary>Native fact projection and reservations.</summary>
        public Futures2Book Book { get; set; } = new Futures2Book();
        /// <summary>Emergency latch survives disconnects and restarts.</summary>
        public bool Emergency { get; set; }
        /// <summary>Operator-visible cause/status.</summary>
        public string Reason { get; set; } = "Draft";
        /// <summary>HJ range state.</summary>
        public Futures2Hj Hj { get; set; } = new Futures2Hj();
        /// <summary>Trailing helper state.</summary>
        public Futures2Trail Trail { get; set; } = new Futures2Trail();
        /// <summary>Single-release entry block state.</summary>
        public bool EntryBlockReleased { get; set; }
        /// <summary>Whether this campaign ever held confirmed inventory.</summary>
        public bool HadInventory { get; set; }
        /// <summary>Monotone participation history: false only for a fresh native adapter, null for unknown legacy history.</summary>
        /// <remarks>Set true before publishing a consenting peer or enabling portfolio roles. Neither
        /// disabling settings nor rearming a campaign clears it. Only false permits optional empty removal.</remarks>
        public bool? CoordinationUsed { get; set; }
        /// <summary>Voluntary reduction active; it may be revoked only through a cancellation barrier.</summary>
        public bool Reducing { get; set; }
        /// <summary>Stable identity of the currently requested protective reduction.</summary>
        public string ReductionGeneration { get; set; } = "";
        /// <summary>Collateral to retain during the current voluntary reduction.</summary>
        public decimal RetainCollateral { get; set; }
        /// <summary>Last ordinary decision time; emergency is evaluated before this throttle.</summary>
        public DateTime LastPass { get; set; }
        /// <summary>Flat-cycle shift is applied once, not on every empty tick.</summary>
        public bool ShiftDue { get; set; }
        /// <summary>Pending operator level edit, applied after existing native orders drain.</summary>
        public Futures2LevelEdit LevelEdit { get; set; }
        /// <summary>One explicit selected-level increase request; normal direction/funding gates still apply.</summary>
        public int? ManualEntry { get; set; }
        /// <summary>Durable selected-level entry progress; removes a level when its intent is recorded.</summary>
        public Futures2EntryBatch EntryBatch { get; set; }
        /// <summary>Whole native intents explicitly selected for cancellation, retained through unknown outcomes.</summary>
        public HashSet<string> SelectedCancels { get; set; } = new HashSet<string>();
        /// <summary>One selected-level reduction request; ordinary exit restrictions still apply.</summary>
        public int? ManualExit { get; set; }
        /// <summary>Plan selected by the pending manual exit; empty selects every retained plan.</summary>
        public string ManualExitPlan { get; set; } = "";
        /// <summary>Snapshot of lots still owned by a manual reduction across partial fills and order limits.</summary>
        public List<string> ManualExitLots { get; set; } = new List<string>();
        /// <summary>Cancel all outstanding campaign orders, without fabricating fills.</summary>
        public bool CancelAll { get; set; }
        /// <summary>Current voluntary transfer identity attached to new reduction intents.</summary>
        public string TransferId { get; set; } = "";
        /// <summary>Durable outbound transfers; confirmed debits are retried until received.</summary>
        public List<Futures2Transfer> Transfers { get; set; } = new List<Futures2Transfer>();
        /// <summary>Cumulative inbound credits, used to prevent replay duplication.</summary>
        public Dictionary<string, decimal> Receipts { get; set; } = new Dictionary<string, decimal>();
        /// <summary>Accepted group-reduction command identities.</summary>
        public HashSet<string> GroupReceipts { get; set; } = new HashSet<string>();
        /// <summary>Explicit old/new contract cutover state.</summary>
        public Futures2Rollover Rollover { get; set; }
        /// <summary>Group helper reduction cycle identity.</summary>
        public string GroupReductionId { get; set; } = "";
        /// <summary>Durable group mailbox commands retried until each selected recipient acknowledges.</summary>
        public Dictionary<string, Futures2PeerCommand> GroupCommands { get; set; } = new Dictionary<string, Futures2PeerCommand>();
        /// <summary>Last helper close/recovery state sent to the selected portfolio.</summary>
        public bool GroupClosing { get; set; }
        /// <summary>Monotone durable sequence for commands emitted by this campaign.</summary>
        public long GroupSequence { get; set; }
        /// <summary>Last applied group sequence per source campaign; stale deliveries are acknowledged without replay.</summary>
        public Dictionary<string, long> GroupVersions { get; set; } = new Dictionary<string, long>();
    }

    /// <summary>Deterministic campaign decision engine above native positions/orders.</summary>
    /// <remarks>
    /// The caller owns serialization, persistence-before-effect and adapter readiness/reconciliation.
    /// Decisions retain cancel/unknown reservations and never infer acceptance from a method return.
    /// Emergency cancels conflicting orders before reductions and cannot be revoked by helper policy.
    /// Fresh quotes are supplied on the mode's event clock. THG-EXECUTION-001, THG-PRICE-001.
    /// </remarks>
    public sealed class Futures2Controller
    {
        /// <summary>Mutable only within the adapter's serial decision lock.</summary>
        public Futures2Checkpoint Data { get; }

        /// <summary>Creates an engine over a new or validated recovered checkpoint.</summary>
        public Futures2Controller(Futures2Checkpoint data)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            Futures2Store.ValidateSchema(data);
        }

        /// <summary>Stages a validated plan/policy; working or unknown orders must drain before publication.</summary>
        public void Configure(Futures2Plan plan, Futures2Policy policy, decimal capital, int endpoint)
            => Configure(plan, policy, capital, endpoint, false);

        /// <summary>Stages a configuration, optionally replacing explicit level settings even when input geometry is unchanged.</summary>
        /// <param name="plan">Detached validated plan with native endpoint identity.</param>
        /// <param name="policy">Validated execution policy.</param>
        /// <param name="capital">Positive total held plus fillable-entry collateral limit.</param>
        /// <param name="endpoint">Native endpoint index, zero or one.</param>
        /// <param name="replaceLevelSettings">True for a complete Simple parameter snapshot; false preserves existing manual level edits for equal inputs.</param>
        /// <remarks>Replacement still drains working orders and retains old owned lots and their exit settings.
        /// The four-argument overload preserves the full Futures2Grid contract. THG-SIMPLE-017.</remarks>
        public void Configure(Futures2Plan plan, Futures2Policy policy, decimal capital, int endpoint, bool replaceLevelSettings)
        {
            policy.Validate();
            ValidateHedgePolicy(plan, policy);
            if (capital <= 0 || endpoint < 0 || endpoint > 1) throw new ArgumentException("Invalid capital or endpoint.");
            if (Data.Emergency) throw new InvalidOperationException("Emergency must reach confirmed flat before a new campaign.");
            if (Data.ActivePlan.Length != 0 && Data.Book.Lots.Any(l => l.Quantity > 0)
                && !Data.Plans[Data.ActivePlan].SameOrientation(plan))
                throw new InvalidOperationException("Logical direction/hedge mode cannot change while inventory remains.");
            if (!replaceLevelSettings && Data.ActivePlan.Length > 0 && System.Text.Json.JsonSerializer.Serialize(Data.Plans[Data.ActivePlan].Input)
                == System.Text.Json.JsonSerializer.Serialize(plan.Input)) plan = Data.Plans[Data.ActivePlan];
            if (plan.Input.IsHedge) Data.Schema = Math.Max(Data.Schema, 3);
            if (policy.AscendingLevelPriority || policy.QuoteOrdinaryLimits) Data.Schema = Math.Max(Data.Schema, 5);
            if (Data.PendingPlan == null) Data.ResumeAfterConfigure = Data.State == Futures2State.Active;
            Data.PendingPlan = plan;
            Data.PendingPolicy = policy;
            Data.PendingEndpoint = endpoint;
            Data.PendingCapital = capital;
            if (Data.ActivePlan.Length == 0) Publish();
            else { Data.State = Futures2State.PausingEntries; Data.Reason = "Waiting for reconfiguration order outcomes"; }
        }

        /// <summary>Rejects hedge preorders explicitly, matching the source runtime guard; no silent switch edits.</summary>
        public static void ValidateHedgePolicy(Futures2Plan plan, Futures2Policy policy)
        {
            if (plan.Input.IsHedge && (policy.PreEntries || policy.PreExits))
                throw new InvalidOperationException("Hedge mode requires PreEntries and PreExits disabled.");
        }

        private void Publish()
        {
            Futures2Plan plan = Data.PendingPlan;
            if (Data.Book.Lots.Any(l => l.Quantity > 0 && !Data.Plans[l.PlanId].SameOrientation(plan)))
            {
                Data.PendingPlan = null; Data.PendingPolicy = null; Pause();
                Data.Reason = "Configuration rejected: cancellation-time fill conflicts with requested direction/hedge mode";
                return;
            }
            decimal heldReserve = Data.Book.Reserved(Data.Plans);
            if (heldReserve > Data.PendingCapital || plan.Id != Data.ActivePlan && plan.Reserved > Data.PendingCapital)
                throw new InvalidOperationException("Replacement plan plus retained old inventory exceeds capital.");
            Data.Capital = Data.PendingCapital;
            if (Data.ReturnBase <= 0) Data.ReturnBase = Data.Capital;
            bool clearedEntries = Data.EntryBatch != null && Data.EntryBatch.Plan != plan.Id;
            if (clearedEntries) Data.EntryBatch = null;
            if (Data.ActivePlan != plan.Id) Data.ManualEntry = null;
            Data.Plans[plan.Id] = plan;
            Data.ActivePlan = plan.Id;
            Data.Endpoint = Data.PendingEndpoint;
            Data.Policy = Data.PendingPolicy;
            Data.PendingPlan = null; Data.PendingPolicy = null;
            Data.EntryBlockReleased = false;
            Data.Hj = new Futures2Hj();
            Data.State = Data.ResumeAfterConfigure ? Futures2State.Active : Futures2State.Ready;
            Data.Reason = clearedEntries ? "Configuration applied; pending selected entries canceled for changed plan" : "Configuration applied";
        }

        /// <summary>Starts or resumes only after external native/account reconciliation; never clears an emergency latch.</summary>
        public void Start(bool reconciled, DateTime now)
        {
            if (!reconciled || Data.Capital <= 0 || Data.ActivePlan.Length == 0 || Data.PendingPlan != null || Data.Emergency
                || Data.Book.Intents.Any(i => i.State == Futures2IntentState.Unknown || i.State == Futures2IntentState.SubmitPending))
                throw new InvalidOperationException("Reconciliation and an accepted plan are required.");
            Data.State = Futures2State.Active;
            Data.Reason = "Started";
            if (!Data.Trail.Started.HasValue) Data.Trail.Reset(now);
        }

        /// <summary>Pauses new entries and requests their cancellation on subsequent passes; exits remain owned.</summary>
        public void Pause()
        {
            if (Data.Emergency) return;
            Data.ResumeAfterConfigure = false;
            Data.State = Futures2State.PausingEntries; Data.Reason = "Pause entries";
        }

        /// <summary>Latches emergency liquidation across all owned endpoints and plans.</summary>
        public void Flatten(string reason)
        {
            Data.PendingPlan = null; Data.PendingPolicy = null;
            Data.EntryBatch = null; Data.ManualEntry = null;
            if (!Data.Emergency) Data.ReductionGeneration = Guid.NewGuid().ToString("N");
            foreach (Futures2Transfer transfer in Data.Transfers.Where(t => !t.Completed)) transfer.Canceled = true;
            Data.TransferId = "";
            if (Data.Rollover != null && Data.Rollover.State != "Applied") Data.Rollover.State = "Canceled";
            Data.Emergency = true; Data.State = Futures2State.Liquidating;
            Data.Reason = reason; Data.Reducing = false;
        }

        /// <summary>Requests a voluntary reduction retaining positive collateral, without releasing existing exits.</summary>
        public void Reduce(decimal retainCollateral, string reason, string transferId = "")
        {
            if (retainCollateral < 0) throw new ArgumentOutOfRangeException(nameof(retainCollateral));
            if (Data.Emergency) return;
            if (Data.Rollover?.State == "Entering") Data.Rollover.State = "Canceled";
            foreach (Futures2Transfer transfer in Data.Transfers.Where(t => !t.Completed && t.Id != transferId)) transfer.Canceled = true;
            Data.TransferId = transferId;
            Data.ReductionGeneration = Guid.NewGuid().ToString("N");
            Data.Reducing = true; Data.RetainCollateral = retainCollateral;
            Data.State = Futures2State.PausingEntries; Data.Reason = reason;
        }

        /// <summary>Records loss of trustworthy reconciliation while retaining every order and position.</summary>
        public void Reconcile(string reason)
        {
            Data.State = Futures2State.Reconciling; Data.Reason = reason;
        }

        /// <summary>Marks a processing failure; fill intake must continue in the adapter.</summary>
        public void Fault(string reason)
        {
            Data.State = Futures2State.Faulted; Data.Reason = reason;
        }

        /// <summary>Creates native requests from fresh snapshots. Returned reservations must be durably saved before execution.</summary>
        public List<Futures2Action> Decide(IReadOnlyDictionary<int, Futures2Quote> quotes, DateTime now, bool reconciled, decimal? portfolioPercent = null, bool portfolioRequired = false, Futures2HelperScope helperScope = null)
        {
            List<Futures2Action> actions = new List<Futures2Action>();
            if (Data.ActivePlan.Length == 0) return actions;
            Futures2Plan plan = Data.Plans[Data.ActivePlan];
            Futures2Policy policy = Data.Policy;
            Futures2Book book = Data.Book;
            foreach (Futures2Quote currentQuote in quotes.Values)
                currentQuote.Gap = policy.GapReferencesKnown ? checked(policy.CurrentSessionOpen - policy.PreviousSessionClose) : null;
            bool replacing = Data.Rollover?.State == "Entering" && Data.ActivePlan == Data.Rollover.Plan.Id;
            if (replacing && book.Intents.All(i => !i.CanFill) && Data.Rollover.Required.All(r =>
                book.Lots.Where(l => l.PlanId == plan.Id && l.LevelId == r.Key).Sum(l => l.Quantity) >= r.Value))
            { Data.Rollover.State = "Applied"; Data.Reason = "Replacement exposure confirmed"; replacing = false; }
            if (Data.ManualExit.HasValue)
            {
                Data.ManualExitLots = book.Lots.Where(l => l.Quantity > 0
                    && (Data.ManualExitPlan.Length == 0 || l.PlanId == Data.ManualExitPlan)
                    && (Data.ManualExit.Value < 0 || l.LevelId == Data.ManualExit.Value)).Select(l => l.Id).ToList();
                Data.ManualExit = null;
            }
            Data.ManualExitLots.RemoveAll(id => !book.Lots.Any(l => l.Id == id && l.Quantity > 0));
            if (book.Day.Date != now.Date) { book.Day = now.Date; book.DaySpent = 0; }
            bool fresh = quotes.TryGetValue(Data.Endpoint, out Futures2Quote quote) && quote.Valid(now, policy.FreshnessSeconds);
            if (fresh)
            {
                if (!Data.Hj.Initialized || Data.Hj.Width != policy.HjWidth) Data.Hj.Reset(checked(quote.Bid + (quote.Ask - quote.Bid) / 2), policy.HjWidth);
                Data.Hj.Update(checked(quote.Bid + (quote.Ask - quote.Bid) / 2));
                if ((plan.Input.LowerStopEnabled && quote.Bid <= plan.Input.LowerStop)
                    || (plan.Input.UpperStopEnabled && quote.Ask >= plan.Input.UpperStop)) Flatten("External price boundary");
            }
            Data.Trail.FirstEntry = portfolioRequired ? helperScope?.FirstEntry
                : book.Lots.Where(l => l.Quantity > 0).Select(l => (DateTime?)l.Opened).DefaultIfEmpty(null).Min();
            Data.Trail.WasActivity = portfolioRequired ? helperScope?.WasActivity ?? true : book.Lots.Count > 0;
            Data.Trail.Created = portfolioRequired ? helperScope?.Created : null;
            bool helperEmpty = portfolioRequired ? helperScope?.Empty ?? false : book.LocallyEmpty;
            if (!Data.Emergency && Data.Reducing && Data.Trail.Closing && policy.DeferredClose && fresh && reconciled
                && (!portfolioRequired || portfolioPercent.HasValue)
                && !Data.Trail.Evaluate(policy, portfolioPercent ?? ProfitPercent(quotes), helperEmpty, now))
                Data.RecoverReduction = true;
            Data.SelectedCancels.RemoveWhere(id => !book.Intents.Any(i => i.Id == id && i.CanFill));
            foreach (Futures2Intent intent in book.Intents.Where(i => i.CanFill).ToArray())
            {
                if ((intent.State == Futures2IntentState.SubmitPending && now - intent.Created >= TimeSpan.FromSeconds(policy.UnknownAfterSeconds))
                    || (intent.CancelRequested.HasValue && now - intent.CancelRequested.Value >= TimeSpan.FromSeconds(policy.UnknownAfterSeconds)))
                { intent.State = Futures2IntentState.Unknown; Reconcile("Unknown native order outcome"); }
                bool cancel = Data.PendingPlan != null || Data.LevelEdit != null || Data.CancelAll || Data.SelectedCancels.Contains(intent.Id) || (Data.Emergency || Data.Reducing) && !intent.Protective
                    || intent.Protective && intent.ReductionGeneration != Data.ReductionGeneration
                    || Data.RecoverReduction && intent.Protective
                    || (intent.Entry && (Data.State != Futures2State.Active || policy.ForbidEntries || !fresh
                        || policy.ForbidLong && plan.Input.Direction == Futures2Direction.Long || policy.ForbidShort && plan.Input.Direction == Futures2Direction.Short))
                    || now - intent.Created >= TimeSpan.FromSeconds(policy.OrderLifeSeconds);
                if (cancel) RequestCancel(intent, actions, now);
            }
            if (actions.Count != 0) return actions;
            if (book.Intents.Any(i => i.State == Futures2IntentState.Unknown || i.State == Futures2IntentState.SubmitPending)) return actions;
            if (book.Intents.All(i => !i.CanFill) && reconciled)
            {
                Data.CancelAll = false;
                if (Data.LevelEdit != null)
                {
                    Futures2LevelEdit edit = Data.LevelEdit;
                    if (edit.Markup.HasValue && edit.Markup <= 0) throw new ArgumentException("Markup must be positive.");
                    foreach (Futures2Level level in Data.Plans[edit.Plan].Levels.Where(l => edit.Levels != null ? edit.Levels.Contains(l.Id) : edit.Level < 0 || l.Id == edit.Level))
                    {
                        if (edit.Markup.HasValue) level.Markup = edit.Markup.Value;
                        if (edit.Entry.HasValue) level.EntryEnabled = edit.Entry.Value;
                        if (edit.Exit.HasValue) level.ExitEnabled = edit.Exit.Value;
                    }
                    Data.LevelEdit = null;
                }
            }
            if (Data.LevelEdit != null || Data.CancelAll) return actions;
            if (Data.Trail.ForbidEntries) policy.ForbidEntries = true;
            if (Data.RecoverReduction && book.Intents.All(i => !i.CanFill))
            { Data.Reducing = false; Data.RecoverReduction = false; Data.State = Futures2State.PausedEntries; Data.Reason = "Deferred reduction recovered; entries paused"; }
            if (Data.PendingPlan != null)
            {
                if (book.Intents.Any(i => i.CanFill) || !reconciled) return actions;
                Publish(); return actions;
            }
            if (Data.State == Futures2State.PausingEntries && book.Intents.All(i => !i.Entry || !i.CanFill)) Data.State = Futures2State.PausedEntries;
            if (book.Lots.Any(l => l.Quantity > 0)) { Data.HadInventory = true; Data.ShiftDue = true; }
            if (book.LocallyEmpty && reconciled)
            {
                if (policy.ResetDayWhenFlat) book.DaySpent = 0;
                if (Data.Emergency) { Data.State = Futures2State.FlatConfirmed; Data.Reason = "Confirmed flat after emergency"; return actions; }
                if (!replacing && Data.HadInventory && policy.StopAfterExit) { Data.State = Futures2State.Stopped; Data.Reason = "Stop after flat"; return actions; }
                if (Data.Reducing) { Data.Reducing = false; Data.State = Futures2State.PausedEntries; Data.Reason = "Voluntary reduction completed"; }
                if (!replacing && fresh && Data.State == Futures2State.Active)
                {
                    Futures2Plan replacement = null;
                    if (Data.ShiftDue && policy.ShiftAfterFlat != 0) replacement = Futures2Commands.Shift(plan, policy.ShiftAfterFlat);
                    else if (policy.WidenWhenFlat && (quote.Bid < plan.Input.Low || quote.Ask > plan.Input.High)) replacement = Futures2Commands.Widen(plan, quote.Bid, quote.Ask);
                    Data.ShiftDue = false;
                    if (replacement != null) { Configure(replacement, policy, Data.Capital, Data.Endpoint); return actions; }
                }
            }
            if (Data.Emergency || Data.Reducing)
            {
                // Wait until conflicting orders have terminal outcomes before replacing any exit.
                if (!reconciled || book.Intents.Any(i => i.CanFill)) return actions;
                decimal keep = Data.Emergency ? 0 : Data.RetainCollateral;
                decimal toRelease = Math.Max(0, book.Reserved(Data.Plans) - keep);
                foreach (Futures2Lot lot in book.Lots.Where(l => l.Quantity > 0))
                {
                    Futures2Plan oldPlan = Data.Plans[lot.PlanId];
                    if (!quotes.TryGetValue(lot.Endpoint, out Futures2Quote oldQuote) || !oldQuote.Valid(now, policy.FreshnessSeconds)) continue;
                    decimal volume = Math.Min(book.Available(lot), decimal.Floor(toRelease / oldPlan.Input.Collateral / oldPlan.Input.VolumeStep) * oldPlan.Input.VolumeStep);
                    if (volume < oldPlan.Input.MinimumVolume) continue;
                    decimal limit = Aggressive(oldPlan, oldQuote, false, policy.SlippageTicks);
                    Submit(actions, oldPlan, lot.Endpoint, false, false, volume, limit, now, new Futures2Allocation { LevelId = lot.LevelId, LotId = lot.Id, Quantity = volume }, lot.PositionNumber);
                    toRelease -= volume * oldPlan.Input.Collateral;
                    if (actions.Count >= policy.MaxActions || toRelease <= 0) break;
                }
                if (actions.Count == 0 && toRelease == 0 && !Data.Emergency) { Data.State = Futures2State.PausedEntries; Data.Reason = "Retained collateral reached; voluntary reduction remains armed"; }
                return actions;
            }
            if (!fresh || !reconciled || Data.State == Futures2State.Reconciling || Data.State == Futures2State.Faulted
                || Data.State == Futures2State.Ready || Data.State == Futures2State.Stopped || Data.State == Futures2State.FlatConfirmed) return actions;
            if (!Futures2Rules.InSession(policy.Sessions, now)) return actions;
            if (now - Data.LastPass < TimeSpan.FromMilliseconds(policy.IntervalMilliseconds)) return actions;
            Data.LastPass = now;
            if (policy.RejectionLimit > 0 && book.Rejections >= policy.RejectionLimit) { Pause(); Data.Reason = "Confirmed rejection limit"; return actions; }
            decimal percent = portfolioPercent ?? (portfolioRequired ? 0 : ProfitPercent(quotes));
            if (!replacing && (!portfolioRequired || portfolioPercent.HasValue) && Data.Trail.Evaluate(policy, percent, helperEmpty, now))
            { if (!portfolioRequired) Reduce(policy.RetainCollateral, "Trailing: " + Data.Trail.Cause); return actions; }
            if (Data.Trail.Disabled && Data.Trail.Cause == "Empty stop") { Pause(); return actions; }
            if (policy.SequentialLiquidity && book.Intents.Any(i => i.CanFill)) return actions;
            List<Futures2Rule> rules = Futures2Rules.Parse(policy.Rules);
            decimal totalHeld = book.Lots.Sum(l => l.Quantity);
            decimal totalPlan = plan.Levels.Sum(l => l.Volume);
            bool buy = plan.Input.Direction == Futures2Direction.Long;
            decimal entryQuote = checked((buy ? quote.Ask : quote.Bid) + (buy ? 1 : -1) * policy.ThresholdTicks * plan.Input.Tick);
            decimal exitQuote = checked((buy ? quote.Bid : quote.Ask) - (buy ? 1 : -1) * policy.ThresholdTicks * plan.Input.Tick);
            bool block = policy.BlockEnter.Contains(entryQuote);
            if (!block && !policy.BlockEnterAlways) Data.EntryBlockReleased = true;
            block &= policy.BlockEnterAlways || !Data.EntryBlockReleased;
            decimal pre = plan.Distance(policy.PreDistance, policy.PreDistanceUnit);

            // Reductions precede ordinary increases; both still reserve actual native inventory.
            if (!replacing && !policy.ForbidExits && !policy.BlockExit.Contains(exitQuote) && !policy.BlockExit2.Contains(exitQuote)
                && !Futures2Rules.Blocked(rules, false, exitQuote, quote.Gap, totalHeld, totalPlan))
            {
                IEnumerable<Futures2Lot> exitLots = book.Lots.Where(l => l.Quantity > 0);
                if (policy.AscendingLevelPriority)
                    exitLots = exitLots.OrderBy(l => Data.Plans[l.PlanId].Levels.Single(level => level.Id == l.LevelId).Price);
                foreach (Futures2Lot lot in exitLots)
                {
                    Futures2Plan oldPlan = Data.Plans[lot.PlanId];
                    Futures2Level level = oldPlan.Levels.Single(l => l.Id == lot.LevelId);
                    if (!level.ExitEnabled || !quotes.TryGetValue(lot.Endpoint, out Futures2Quote oldQuote) || !oldQuote.Valid(now, policy.FreshnessSeconds)) continue;
                    Futures2Lot[] compatibleLots = book.Lots.Where(l => l.Quantity > 0 && l.Endpoint == lot.Endpoint
                        && Data.Plans[l.PlanId].Input.Instrument == oldPlan.Input.Instrument
                        && Data.Plans[l.PlanId].SameOrientation(oldPlan)).ToArray();
                    decimal basis = policy.ExitMode == Futures2ExitMode.WholePosition
                        ? compatibleLots.Sum(l => l.Cost + l.Quantity * l.ExitCarry) / compatibleLots.Sum(l => l.Quantity)
                        : lot.Average.Value + lot.ExitCarry;
                    Futures2Level targetLevel = policy.ExitMode == Futures2ExitMode.WholePosition
                        ? new Futures2Level { Price = level.Price, Markup = policy.WholeMarkup > 0 ? policy.WholeMarkup : plan.Input.Markup } : level;
                    decimal target = oldPlan.Target(targetLevel, basis, policy.ExitMode, policy.ExitAfterAcross);
                    decimal reference = oldPlan.Input.Direction == Futures2Direction.Long
                        ? checked(oldQuote.Bid - policy.ThresholdTicks * oldPlan.Input.Tick)
                        : checked(oldQuote.Ask + policy.ThresholdTicks * oldPlan.Input.Tick);
                    bool manualExit = Data.ManualExitLots.Contains(lot.Id);
                    bool reached = manualExit || (oldPlan.Input.Direction == Futures2Direction.Long ? reference >= target : reference <= target);
                    if (manualExit) target = Aggressive(oldPlan, oldQuote, false, policy.SlippageTicks);
                    bool near = oldPlan.Input.Direction == Futures2Direction.Long ? reference >= target - pre : reference <= target + pre;
                    if ((!reached && !(policy.PreExits && near)) || (policy.HjExits && !Data.Hj.Allows(target))) continue;
                    decimal volume = book.Available(lot);
                    if (volume < oldPlan.Input.MinimumVolume) continue;
                    decimal executionPrice = oldPlan.Input.IsHedge && !manualExit
                        ? Aggressive(oldPlan, oldQuote, false, checked(policy.ThresholdTicks + policy.SlippageTicks)) : target;
                    if (policy.QuoteOrdinaryLimits && reached && !manualExit)
                        executionPrice = Aggressive(oldPlan, oldQuote, false, policy.ThresholdTicks);
                    Submit(actions, oldPlan, lot.Endpoint, false, reached && policy.MarketOrders, volume, executionPrice, now,
                        new Futures2Allocation { LevelId = lot.LevelId, LotId = lot.Id, Quantity = volume }, lot.PositionNumber);
                    if (actions.Count >= policy.MaxActions) return actions;
                }
            }
            if (actions.Count != 0 || Data.ManualExitLots.Count != 0 || Data.State != Futures2State.Active || policy.ForbidEntries || block
                || buy && policy.ForbidLong || !buy && policy.ForbidShort
                || Futures2Rules.Blocked(rules, true, entryQuote, quote.Gap, totalHeld, totalPlan)) return actions;
            if (quote.Margin.HasValue && quote.Margin.Value > plan.Input.Collateral) { Pause(); Data.Reason = "Broker margin increased: revalidate reserve"; return actions; }
            Futures2EntryBatch batch = Data.EntryBatch;
            IEnumerable<Futures2Level> entryLevels = policy.AscendingLevelPriority ? plan.Levels.OrderBy(level => level.Price) : plan.Levels;
            foreach (Futures2Level level in entryLevels)
            {
                if (batch != null && !batch.Levels.Contains(level.Id)) continue;
                if (!level.EntryEnabled || (policy.HjEntries && !Data.Hj.Allows(level.Price))) continue;
                if (replacing && !Data.Rollover.Required.ContainsKey(level.Id)) continue;
                if (Data.ManualEntry.HasValue && Data.ManualEntry.Value != level.Id) continue;
                bool manualEntry = Data.ManualEntry.HasValue || batch != null;
                if (batch != null && book.Occupied(plan.Id, level.Id) >= level.Volume)
                { CompleteBatchLevel(batch, level.Id); continue; }
                decimal funding = Data.Receipts.Sum(r => Math.Max(0, r.Value - book.Intents.Where(i => i.Entry && i.TransferBudgets.ContainsKey(r.Key))
                    .Sum(i => i.TransferBudgets[r.Key] * (i.CanFill ? 1 : i.Filled / i.Quantity))));
                bool fundedEntry = funding >= plan.Input.MinimumVolume * plan.Input.Collateral;
                bool reached = replacing || manualEntry || fundedEntry || (buy ? entryQuote <= level.Price : entryQuote >= level.Price);
                bool near = buy ? entryQuote <= level.Price + pre : entryQuote >= level.Price - pre;
                if (!reached && !(policy.PreEntries && near)) continue;
                decimal volume = Math.Max(0, (replacing ? Data.Rollover.Required[level.Id] : level.Volume) - book.Occupied(plan.Id, level.Id));
                decimal availableCapital = Math.Max(0, Data.Capital - book.Reserved(Data.Plans));
                if (fundedEntry) availableCapital = Math.Min(availableCapital, funding);
                volume = Math.Min(volume, decimal.Floor(availableCapital / plan.Input.Collateral / plan.Input.VolumeStep) * plan.Input.VolumeStep);
                decimal cost = checked(volume * plan.Input.Collateral);
                decimal pendingDay = book.Intents.Where(i => i.Entry).Sum(i => i.Remaining * Data.Plans[i.PlanId].Input.Collateral);
                if (volume < plan.Input.MinimumVolume || book.Reserved(Data.Plans) + cost > Data.Capital
                    || !quote.FreeMargin.HasValue || cost + pendingDay > quote.FreeMargin.Value
                    || policy.DayLimit > 0 && book.DaySpent + pendingDay + cost > policy.DayLimit) continue;
                decimal price = reached ? SignedPriceMath.Quantize(buy ? Math.Min(level.Price, quote.Ask) : Math.Max(level.Price, quote.Bid), plan.Input.Tick, !buy) : level.Price;
                if (plan.Input.IsHedge) price = Aggressive(plan, quote, true, checked(policy.ThresholdTicks + policy.SlippageTicks));
                else if (replacing || manualEntry || fundedEntry) price = Aggressive(plan, quote, true, policy.SlippageTicks);
                if (policy.QuoteOrdinaryLimits && reached && !replacing && !manualEntry && !fundedEntry)
                    price = Aggressive(plan, quote, true, policy.ThresholdTicks);
                Futures2Intent compatible = policy.GroupOrders && !replacing ? actions.Where(a => !a.Cancel).Select(a => a.Intent)
                    .FirstOrDefault(i => i.Entry && i.TransferId.Length == 0 && Data.Receipts.Count == 0 && i.PlanId == plan.Id && i.Endpoint == Data.Endpoint && i.Price == price && i.Market == (reached && policy.MarketOrders)) : null;
                if (compatible != null)
                { compatible.Quantity += volume; compatible.Allocations.Add(new Futures2Allocation { LevelId = level.Id, Quantity = volume }); }
                else
                {
                    if (actions.Count >= policy.MaxActions) break;
                    Submit(actions, plan, Data.Endpoint, true, reached && policy.MarketOrders, volume, price, now,
                        new Futures2Allocation { LevelId = level.Id, Quantity = volume,
                            ExitCarry = replacing && plan.ExitCarry.TryGetValue(level.Id, out decimal carry) ? carry : 0 }, 0);
                }
                if (batch != null) CompleteBatchLevel(batch, level.Id);
                else if (manualEntry) { Data.ManualEntry = null; break; }
            }
            return actions;
        }

        private void CompleteBatchLevel(Futures2EntryBatch batch, int level)
        {
            batch.Levels.Remove(level);
            if (batch.Levels.Count == 0) Data.EntryBatch = null;
        }

        private void Submit(List<Futures2Action> actions, Futures2Plan plan, int endpoint, bool entry, bool market,
            decimal volume, decimal price, DateTime now, Futures2Allocation allocation, int position)
        {
            Futures2Intent intent = new Futures2Intent { PlanId = plan.Id, Endpoint = endpoint, Entry = entry, Market = market,
                Quantity = volume, Price = price, Created = now, PositionNumber = position,
                Protective = !entry && (Data.Emergency || Data.Reducing), ReductionGeneration = Data.ReductionGeneration, TransferId = entry ? "" : Data.TransferId, FeePerUnit = Data.Policy.FeePerUnit };
            if (entry)
            {
                decimal remainingFunding = checked(volume * plan.Input.Collateral);
                foreach (KeyValuePair<string, decimal> receipt in Data.Receipts.OrderBy(r => r.Key, StringComparer.Ordinal))
                {
                    decimal committed = Data.Book.Intents.Where(i => i.Entry && i.TransferBudgets.ContainsKey(receipt.Key))
                        .Sum(i => i.TransferBudgets[receipt.Key] * (i.CanFill ? 1 : i.Filled / i.Quantity));
                    decimal available = receipt.Value - committed;
                    if (available <= 0) continue;
                    decimal assigned = Math.Min(available, remainingFunding);
                    intent.TransferBudgets.Add(receipt.Key, assigned);
                    if (intent.TransferId.Length == 0) { intent.TransferId = receipt.Key; intent.TransferBudget = assigned; }
                    remainingFunding -= assigned;
                    if (remainingFunding == 0) break;
                }
            }
            intent.Allocations.Add(allocation);
            Data.Book.Record(intent);
            actions.Add(new Futures2Action { Intent = intent });
        }

        private void RequestCancel(Futures2Intent intent, List<Futures2Action> actions, DateTime now)
        {
            if (intent.State == Futures2IntentState.Recorded) { intent.State = Futures2IntentState.Rejected; return; }
            if (!intent.CanFill || intent.OrderNumber == 0 || intent.CancelRequested.HasValue) return;
            intent.State = Futures2IntentState.CancelPending; intent.CancelRequested = now;
            actions.Add(new Futures2Action { Cancel = true, Intent = intent });
        }

        /// <summary>Creates bounded aggressive limit prices; explicit side is derived from the plan and action.</summary>
        public static decimal Aggressive(Futures2Plan plan, Futures2Quote quote, bool entry, int ticks)
        {
            bool buy = plan.IsLongInventory == entry;
            decimal price = checked((buy ? quote.Ask : quote.Bid) + (buy ? 1 : -1) * ticks * plan.Input.Tick);
            return SignedPriceMath.Quantize(price, plan.Input.Tick, buy);
        }

        /// <summary>Money PnL projected from current liquidation-side quotes over fixed positive capital.</summary>
        public decimal ProfitPercent(IReadOnlyDictionary<int, Futures2Quote> quotes)
        {
            decimal result = Data.Book.Realized;
            checked
            {
                foreach (Futures2Lot lot in Data.Book.Lots.Where(l => l.Quantity > 0))
                {
                    Futures2Plan plan = Data.Plans[lot.PlanId];
                    if (!quotes.TryGetValue(lot.Endpoint, out Futures2Quote quote) || !quote.HasAsk || !quote.HasBid) throw new InvalidOperationException("Missing mark quote.");
                    decimal delta = plan.IsLongInventory ? quote.Bid - lot.Average.Value : lot.Average.Value - quote.Ask;
                    result += delta * lot.Quantity * plan.Input.TickValue / plan.Input.Tick;
                }
                return result / (Data.ReturnBase > 0 ? Data.ReturnBase : Data.Capital) * 100;
            }
        }
    }
}
