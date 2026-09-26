/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace OsEngine.OsTrader.AdaptivePositionManager
{
    /// <summary>
    /// Serialized campaign owner: proposals reserve risk, only identified fills change the ledger.
    /// The caller serializes every call and durably saves a prepared intent before sending it.
    /// </summary>
    /// <remarks>
    /// ExitLatch is irreversible. Unknown/cancel-pending orders retain reservations. Recovery starts
    /// in reconciliation and cannot retry an uncertain send. This class does not guarantee exchange
    /// finality, broker protection, or an attainable stop price. Contract: APM-EXECUTION-001.
    /// </remarks>
    public sealed class ApmCampaign
    {
        private ApmCheckpoint _state;
        private ApmDecision _decision;

        #region Lifecycle and snapshots

        /// <summary>Create one new signal with immutable validated locks and behavior parameters.</summary>
        public ApmCampaign(ApmCampaignSpec spec, ApmPolicy policy, ApmOperationalLimits nativeLimits = null)
        {
            policy.Validate(spec);
            nativeLimits?.Validate();
            if (nativeLimits != null && (spec.CampaignId.Length > 128 || spec.EntrySignalId.Length > 128
                || spec.Instrument.Length > 128 || spec.Account.Length > 128))
                throw new ArgumentException("Native campaign identifiers exceed the qualified memory bound.");
            _state = new ApmCheckpoint { Spec = spec, Policy = policy, Anchor = spec.EntryReference,
                InitialFilled = spec.InitialVolume, PolicyTarget = spec.InitialVolume, NativeLimits = nativeLimits };
        }

        /// <summary>Immutable operator locks.</summary>
        public ApmCampaignSpec Spec => _state.Spec;
        /// <summary>Immutable behavior settings for this campaign.</summary>
        public ApmPolicy Policy => _state.Policy;
        /// <summary>Optional qualified native lifetime budget. Null is the generic offline partial-fill model, without a native memory-bound claim.</summary>
        public ApmOperationalLimits NativeLimits => _state.NativeLimits;
        /// <summary>Last accepted immutable market event, including its causal sequence, for controller recovery.</summary>
        public ApmMarket LastMarket => _state.Market;
        /// <summary>Copies of intent records, including terminal identities needed to recognize late fills.</summary>
        public ApmIntent[] Intents => _state.Intents.Values.ToArray();

        /// <summary>Detached view of actual accounting and the latest proposal.</summary>
        public ApmSnapshot Snapshot => new ApmSnapshot(Spec.CampaignId, CurrentState(), _state.ExitLatch,
            _state.ExitReason, _state.Anchor, _state.Quantity, _state.Average, _state.Realized, _state.Fees,
            Equity(_state.Market?.Price ?? _state.Anchor), _state.Drawdown, _state.MaxQuantity, _state.Turnover,
            Pending(true), Pending(false), _state.Regime, _decision, _state.Market);

        /// <summary>Mark-to-market whole-campaign net result; no winning-lot attribution.</summary>
        public decimal Equity(decimal price) => _state.Realized
            + (int)Spec.Direction * _state.Quantity * (price - _state.Average) * Spec.MoneyPerPriceUnit;

        /// <summary>Pause increases without disabling reductions, reconciliation or terminal protection.</summary>
        public void Pause(bool paused) { _state.Paused = paused; }

        internal bool IsPaused => _state.Paused;

        /// <summary>Latch an operator or protection exit; later fills stay under the same terminal intent.</summary>
        public void RequestExit(string reason)
        {
            if (!_state.ExitLatch)
            {
                _state.ExitLatch = true;
                _state.ExitReason = string.IsNullOrWhiteSpace(reason) ? "MANUAL_EXIT" : reason;
            }
        }

        /// <summary>Freeze new risk after transport, ownership, numerical or persistence uncertainty.</summary>
        public void RequireReconciliation(string reason)
        {
            _state.Reconciling = true;
            _state.Fault = reason;
        }

        private ApmState CurrentState()
        {
            if (_state.ExitLatch)
            {
                if (_state.Quantity == 0 && !_state.Intents.Values.Any(i => i.Unresolved)
                    && !_state.Reconciling) return ApmState.Completed;
                return _state.FaultedClosing || _state.Reconciling ? ApmState.FaultedClosing : ApmState.Closing;
            }
            if (_state.Reconciling) return ApmState.Reconciling;
            if (_state.Paused) return ApmState.PausedNoIncrease;
            if (!_state.Started) return ApmState.Preflight;
            return _state.AnchorFixed ? ApmState.Active : ApmState.Entering;
        }

        #endregion

        #region Decision pipeline

        /// <summary>
        /// Consume one causal event or explicit timer snapshot. At most one ordinary child is proposed.
        /// Fill and order callbacks must be applied first. Protection bypasses freshness and ordinary hysteresis.
        /// </summary>
        public ApmDecision OnMarket(ApmMarket market)
        {
            if (market == null) throw new ArgumentNullException(nameof(market));
            if (_state.Market != null && (market.Sequence <= _state.Market.Sequence || market.Time < _state.Market.Time))
            {
                RequireReconciliation("OUT_OF_ORDER");
                return Explain(ApmAction.Query, 0, new List<string> { "RECONCILIATION_REQUIRED", "OUT_OF_ORDER" });
            }
            _state.Market = market;
            if (market.SlowVariance < 0 || market.FastVariance < 0 || market.Profile != ApmDataProfile.TradeOnly)
                RequireReconciliation("UNSUPPORTED_OR_INVALID_MARKET");
            try
            {
                UpdateEquity();
                CheckProtection(market);
                CheckOperationalLimits();
                return Decide(market);
            }
            catch (ArithmeticException)
            {
                RequireReconciliation("NUMERIC_RANGE");
                return Explain(ApmAction.Query, 0, new List<string> { "NUMERIC_RANGE", "RECONCILIATION_REQUIRED" });
            }
        }

        private void CheckProtection(ApmMarket market)
        {
            // Pre-entry observations initialize features; they are not part of the scheduled campaign.
            // Any actual/reserved exposure remains protected, including an anomalous early callback.
            if (market.Time < Spec.EntryTime && _state.Quantity == 0 && _state.Intents.Count == 0) return;
            int d = (int)Spec.Direction;
            if (d * (market.Price - Spec.HardStopPrice) <= 0) RequestExit("HARD_STOP");
            else if (Equity(market.Price) <= -Spec.RiskBudgetCurrency) RequestExit("MONEY_STOP");
            else if (market.Time >= Spec.SessionExitTime) RequestExit("SESSION_EXIT");
            else if (d * (market.Price - Spec.FinalTargetPrice) >= 0) RequestExit("FINAL_TARGET");
            // A protective reduction changes realized losses and its own remaining risk budget.
            // Exit rather than applying an invalid static risk cap to the remaining quantity.
            decimal available = Math.Max(0, Spec.RiskBudgetCurrency + Math.Min(0, _state.Realized));
            decimal openRisk = _state.Quantity * (ApmMathematics.UnitStopRisk(Spec, _state.Average) + Spec.FeePerContract);
            if (_state.Quantity > Spec.MaxVolume || openRisk > available) RequestExit("RISK_CAP");
        }

        private void CheckOperationalLimits()
        {
            if (NativeLimits == null) return;
            if (_state.Intents.Values.Count(i => i.Action != ApmAction.Exit) >= NativeLimits.OrdinaryIntents)
                RequestExit("RESOURCE_LIMIT");
            if (_state.Quantity > Pending(false)
                && _state.Intents.Values.Count(i => i.Action == ApmAction.Exit) >= NativeLimits.ExitIntents)
                RequireReconciliation("EXIT_BUDGET_EXHAUSTED");
        }

        private ApmDecision Decide(ApmMarket market)
        {
            List<string> reasons = new List<string>();
            if (_state.ExitLatch)
            {
                reasons.Add(_state.ExitReason);
                if (_state.Fault == "EXIT_BUDGET_EXHAUSTED") reasons.Add(_state.Fault);
                ApmIntent conflicting = _state.Intents.Values.FirstOrDefault(i => i.CanCancel
                    && (i.Increases || i.IsLimit));
                // Cancellation is dispatched separately; reserve only the unclaimed confirmed close volume.
                decimal reducible = Math.Max(0, _state.Quantity - Pending(false));
                if (reducible > 0 && (!_state.Reconciling || _state.Fault == "EXECUTION_UNKNOWN"))
                    return Explain(ApmAction.Exit, reducible, reasons);
                if (conflicting != null) return Explain(ApmAction.Cancel, 0, reasons, conflicting.Id);
                if (_state.Reconciling) reasons.Add("RECONCILIATION_REQUIRED");
                return Explain(_state.Reconciling ? ApmAction.Query : ApmAction.Wait, 0, reasons);
            }

            if (!market.Ready) reasons.Add("DATA_NOT_READY");
            if (_state.Paused) reasons.Add("PAUSED_NO_INCREASE");
            if (_state.Reconciling) reasons.Add("RECONCILIATION_REQUIRED");
            if (_state.Reconciling) return Explain(ApmAction.Query, 0, reasons);
            bool noAddZone = (int)Spec.Direction * (market.Price - Spec.HardStopPrice)
                <= Policy.NoAddFraction * (int)Spec.Direction * (_state.Anchor - Spec.HardStopPrice);
            if (noAddZone) reasons.Add("NO_ADD_ZONE");
            UpdateRegime(market);
            if (_state.Regime == ApmRegime.FastAdverse) reasons.Add("FAST_ADVERSE_NO_ADD");

            if (_state.AnchorFixed && market.Ready) UpdateTarget(market);

            ApmIntent working = _state.Intents.Values.FirstOrDefault(i => i.Unresolved);
            if (working != null)
            {
                reasons.Add("PENDING_ORDER");
                bool cancel = working.Increases && (!market.Ready || _state.Paused || noAddZone
                    || _state.Regime == ApmRegime.FastAdverse);
                cancel |= !working.Increases && _state.Regime == ApmRegime.FastFavorable && !_state.DeferConsumed;
                if (_state.AnchorFixed && market.Ready)
                {
                    decimal effective = _state.Quantity + Pending(true) - Pending(false);
                    cancel |= working.Increases ? _state.PolicyTarget < effective : _state.PolicyTarget > effective;
                }
                cancel |= (market.Time - working.Time).TotalSeconds >= Policy.OrderLifetimeSeconds;
                if (cancel && working.CanCancel)
                    return Explain(ApmAction.Cancel, 0, reasons, working.Id);
                return Explain(ApmAction.Wait, 0, reasons);
            }

            if (!_state.Started)
            {
                if (market.Time < Spec.EntryTime || !market.Ready || _state.Paused || noAddZone
                    || _state.Regime == ApmRegime.FastAdverse) return Explain(ApmAction.Wait, 0, reasons);
                _state.KappaStart = ApmMathematics.Kappa(Spec, Policy, market);
                decimal allowed = Capacity(market.Price);
                if (allowed < Spec.InitialVolume)
                {
                    reasons.Add("INITIAL_RISK_REJECTED");
                    return Explain(ApmAction.Wait, 0, reasons);
                }
                reasons.Add("INITIAL_ENTRY");
                return Explain(ApmAction.InitialEntry, Spec.InitialVolume, reasons);
            }
            if (!_state.AnchorFixed || !market.Ready) return Explain(ApmAction.Wait, 0, reasons);

            decimal allowedTarget = Math.Min(_state.PolicyTarget, _state.Quantity + Capacity(market.Price));
            if (allowedTarget < _state.PolicyTarget) reasons.Add("RISK_CAP");
            decimal delta = allowedTarget - _state.Quantity;
            _state.Allowed = allowedTarget;
            if (delta == 0) return Explain(ApmAction.Wait, 0, reasons);
            if (delta > 0 && (_state.Paused || noAddZone || _state.Regime == ApmRegime.FastAdverse))
                return Explain(ApmAction.Wait, 0, reasons);
            if (delta < 0 && _state.Regime == ApmRegime.FastFavorable && !_state.DeferConsumed)
            {
                _state.DeferStart ??= market.Time;
                if ((market.Time - _state.DeferStart.Value).TotalSeconds < Policy.MaxFavorableDeferSeconds)
                {
                    reasons.Add("FAST_FAVORABLE_DEFER");
                    return Explain(ApmAction.Wait, 0, reasons);
                }
                _state.DeferConsumed = true;
            }
            decimal rearm = Math.Max(Policy.RearmMinTicks * Spec.PriceStep,
                Policy.RearmVolatilityFactor * ApmMathematics.Sqrt(market.SlowVariance * Policy.SpeedWindowSeconds));
            if (_state.LastAction != null)
            {
                ApmIntent last = _state.Intents[_state.LastAction];
                decimal vwap = last.FillNotional / last.Filled;
                bool opposite = delta > 0 ? !last.Increases : last.Increases;
                decimal travel = (int)Spec.Direction * (market.Price - vwap);
                if ((opposite && (delta > 0 ? travel > -rearm : travel < rearm))
                    || (market.Time - last.LastFillTime).TotalSeconds < Policy.MinActionIntervalSeconds)
                {
                    reasons.Add("REARM_PENDING");
                    return Explain(ApmAction.Wait, 0, reasons);
                }
            }
            decimal volume = ApmMathematics.Floor(Math.Min(Math.Abs(delta), Policy.MaxChildVolume), Spec.VolumeStep);
            if (volume < Spec.VolumeStep) return Explain(ApmAction.Wait, 0, reasons);
            reasons.Add(delta > 0 ? ((int)Spec.Direction * (market.Price - _state.Anchor) < 0
                ? "AVERAGE_ADVERSE" : "RESTORE_AFTER_REDUCE") : "REDUCE_ON_REBOUND");
            return Explain(delta > 0 ? ApmAction.Add : ApmAction.Reduce, volume, reasons);
        }

        private void UpdateTarget(ApmMarket market)
        {
            (decimal add, decimal reduce) = ApmMathematics.Scales(Spec, Policy, market);
            decimal curve = Policy.ConstantInventory ? _state.InitialFilled
                : ApmMathematics.Curve(Spec, _state.Anchor, _state.InitialFilled, market.Price, add, reduce);
            decimal kappa = ApmMathematics.Kappa(Spec, Policy, market);
            decimal raw = Policy.ConstantInventory ? curve
                : ApmMathematics.Target(curve, Spec.MaxVolume, _state.KappaStart, kappa);
            raw = Math.Clamp(raw, Policy.MinActiveVolume, Spec.MaxVolume);
            if (Math.Abs(raw - _state.PolicyTarget) >= Policy.MinRebalanceVolume + Policy.VolumeDeadband)
                _state.PolicyTarget = ApmMathematics.Quantize(raw, Spec.VolumeStep);
            _state.Curve = curve; _state.Raw = raw; _state.Kappa = kappa;
            _state.AddScale = add; _state.ReduceScale = reduce;
        }

        private void UpdateRegime(ApmMarket market)
        {
            if (!Policy.FastEnabled || !market.Ready) return;
            ApmRegime before = _state.Regime;
            bool held = (market.Time - _state.RegimeSince).TotalSeconds >= Policy.MinRegimeHoldSeconds;
            if (_state.Regime == ApmRegime.FastFavorable && held && market.Speed <= Policy.FavorableOff)
                _state.Regime = ApmRegime.Normal;
            if (_state.Regime == ApmRegime.FastAdverse && held && market.Speed >= -Policy.AdverseOff)
                _state.Regime = ApmRegime.Normal;
            if (_state.Regime == ApmRegime.Normal)
            {
                if (market.Speed >= Policy.FavorableOn) _state.Regime = ApmRegime.FastFavorable;
                else if (market.Speed <= -Policy.AdverseOn) _state.Regime = ApmRegime.FastAdverse;
            }
            if (before != _state.Regime)
            {
                _state.RegimeSince = market.Time;
                _state.DeferStart = null;
                _state.DeferConsumed = false;
            }
        }

        private decimal Capacity(decimal price)
        {
            decimal bound = price + (int)Spec.Direction * Spec.EntrySlippageReserveTicks * Spec.PriceStep;
            decimal reserved = 0;
            foreach (ApmIntent intent in _state.Intents.Values)
                if (intent.Increases) reserved += intent.Remaining * (ApmMathematics.UnitStopRisk(Spec, intent.PriceBound)
                    + 2 * Spec.FeePerContract);
            decimal available = Math.Max(0, Spec.RiskBudgetCurrency + Math.Min(0, _state.Realized));
            decimal openRisk = _state.Quantity * (ApmMathematics.UnitStopRisk(Spec, _state.Average) + Spec.FeePerContract);
            decimal unit = ApmMathematics.UnitStopRisk(Spec, bound) + 2 * Spec.FeePerContract;
            decimal maximum = Math.Max(0, Spec.MaxVolume - _state.Quantity - Pending(true));
            return ApmMathematics.Floor(Math.Min(maximum, unit > 0
                ? Math.Max(0, available - openRisk - reserved) / unit : maximum), Spec.VolumeStep);
        }

        private decimal Pending(bool increases) => _state.Intents.Values.Where(i => i.Increases == increases).Sum(i => i.Remaining);

        private ApmDecision Explain(ApmAction action, decimal volume, List<string> reasons, string intent = "")
        {
            ApmMarket m = _state.Market;
            _decision = new ApmDecision(++_state.DecisionNumber, m.Time, m.Sequence, m.Price,
                _state.Curve, _state.Raw, _state.PolicyTarget, _state.Allowed, _state.Quantity,
                Pending(true), Pending(false), action, volume, intent, reasons.ToArray(), _state.Regime,
                _state.Kappa, _state.AddScale, _state.ReduceScale);
            return _decision;
        }

        #endregion

        #region Intent and fill accounting

        internal ApmDecision CapResearchExecution(ApmDecision decision, decimal cap)
        {
            if (decision != _decision || (decision.Action != ApmAction.Add && decision.Action != ApmAction.Reduce)
                || cap < 0 || cap > decision.Volume || cap % Spec.VolumeStep != 0)
                throw new InvalidOperationException("Research execution may only reduce the current ordinary child on its volume grid.");
            if (cap == decision.Volume) return decision;
            _decision = decision with { Action = cap == 0 ? ApmAction.Wait : decision.Action, Volume = cap,
                Reasons = decision.Reasons.Append("RESEARCH_AC_PACING").ToArray() };
            return _decision;
        }

        /// <summary>
        /// Reserve the current proposal exactly once. The caller must persist ExportCheckpoint before send.
        /// A stale decision is rejected. This method never calls a broker or assumes an execution.
        /// </summary>
        public ApmIntent Reserve(ApmDecision decision)
        {
            if (decision == null || decision != _decision || decision.Id == _state.ReservedDecision
                || decision.Volume <= 0 || (decision.Action != ApmAction.InitialEntry && decision.Action != ApmAction.Add
                    && decision.Action != ApmAction.Reduce && decision.Action != ApmAction.Exit))
                throw new InvalidOperationException("Only the current unreserved actionable decision may be sent.");
            if (_state.ExitLatch && decision.Action != ApmAction.Exit)
                throw new InvalidOperationException("Exit latch forbids ordinary sends.");
            if (NativeLimits != null && (decision.Action == ApmAction.Exit
                ? _state.Intents.Values.Count(i => i.Action == ApmAction.Exit) >= NativeLimits.ExitIntents
                : _state.Intents.Values.Count(i => i.Action != ApmAction.Exit) >= NativeLimits.OrdinaryIntents))
                throw new InvalidOperationException("Native execution lifetime budget exhausted.");
            bool increases = decision.Action == ApmAction.InitialEntry || decision.Action == ApmAction.Add;
            if (increases && (_state.Paused || _state.Reconciling || _state.Intents.Values.Any(i => i.Unresolved)
                || decision.Volume > Capacity(decision.Price)))
                throw new InvalidOperationException("Increase permissions changed before reservation.");
            if (!increases && decision.Volume > _state.Quantity - Pending(false))
                throw new InvalidOperationException("Close volume changed before reservation.");
            string key = Spec.CampaignId + "/" + decision.Id;
            decimal bound = decision.Price + (int)Spec.Direction * Spec.EntrySlippageReserveTicks * Spec.PriceStep;
            ApmIntent intent = new ApmIntent(key, decision.Id, decision.Action, decision.Volume,
                bound, decision.Time, Policy.UseLimitOrders && decision.Action != ApmAction.Exit, DecisionPrice: decision.Price);
            _state.Intents.Add(key, intent);
            _state.ReservedDecision = decision.Id;
            if (decision.Action == ApmAction.InitialEntry) _state.Started = true;
            return intent;
        }

        /// <summary>Reserve cancellation uncertainty before dispatch. Terminal states and their missing-fill reservations are preserved.</summary>
        public void MarkCancelPending(string id)
        {
            ApmIntent intent = _state.Intents[id];
            if (intent.CanCancel) _state.Intents[id] = intent with { State = ApmOrderState.CancelPending };
        }

        /// <summary>Submission timeout is unknown, never a reason to resend.</summary>
        public void MarkUnknown(string id)
        {
            _state.Intents[id] = _state.Intents[id] with { State = ApmOrderState.Unknown };
            RequireReconciliation("EXECUTION_UNKNOWN");
        }

        /// <summary>
        /// Apply an authoritative cumulative order state. Done before own fills retains their reservation.
        /// Stale working acknowledgements cannot reopen a terminal order or cancel its pending cancellation.
        /// </summary>
        public void ApplyOrder(string id, ApmOrderState state, decimal reportedFilled, string brokerId = "")
        {
            ApmIntent old = _state.Intents[id];
            if (reportedFilled < 0 || reportedFilled > old.Volume) { RequireReconciliation("INVALID_ORDER_TOTAL"); return; }
            bool terminal = state == ApmOrderState.Filled || state == ApmOrderState.Canceled || state == ApmOrderState.Rejected;
            if (terminal && (reportedFilled < old.Filled || (state == ApmOrderState.Filled && reportedFilled != old.Volume)))
            { MarkUnknown(id); return; }
            bool wasTerminal = old.State == ApmOrderState.Filled || old.State == ApmOrderState.Canceled || old.State == ApmOrderState.Rejected;
            if (wasTerminal && (state == ApmOrderState.Working || state == ApmOrderState.Prepared)) return;
            if (old.State == ApmOrderState.CancelPending && state == ApmOrderState.Working) state = ApmOrderState.CancelPending;
            _state.Intents[id] = old with { State = state, ReportedFilled = Math.Max(old.ReportedFilled, reportedFilled),
                BrokerId = string.IsNullOrEmpty(brokerId) ? old.BrokerId : brokerId };
            if (state == ApmOrderState.Unknown) RequireReconciliation("EXECUTION_UNKNOWN");
            if (state == ApmOrderState.Rejected && old.Action == ApmAction.Exit)
            {
                _state.FaultedClosing = true;
                RequireReconciliation("EXIT_REJECTED");
            }
            FinalizeEntry();
        }

        /// <summary>
        /// Apply a uniquely identified actual fill, including late terminal fills. Duplicate payloads are ignored;
        /// conflicting identities or excessive closes freeze reconciliation instead of inventing a flat position.
        /// Fees must be supplied by the adapter's declared model when the transport has no fee field.
        /// </summary>
        public void ApplyFill(ApmFill fill)
        {
            if (fill == null || string.IsNullOrWhiteSpace(fill.Id) || fill.Volume <= 0 || fill.Fee < 0)
                throw new ArgumentException("Invalid APM fill.");
            if (_state.Fills.TryGetValue(fill.Id, out ApmFill existing))
            {
                if (existing != fill) RequireReconciliation("CONFLICTING_FILL_ID");
                return;
            }
            if (!_state.Intents.TryGetValue(fill.IntentId, out ApmIntent intent))
            { RequireReconciliation("FOREIGN_FILL"); return; }
            if (intent.Filled + fill.Volume > intent.Volume || (!intent.Increases && fill.Volume > _state.Quantity))
            { RequireReconciliation("EXCESS_FILL"); return; }
            if (intent.Increases)
            {
                _state.Average = (_state.Quantity * _state.Average + fill.Volume * fill.Price) / (_state.Quantity + fill.Volume);
                _state.Quantity += fill.Volume;
            }
            else
            {
                _state.Realized += (int)Spec.Direction * fill.Volume * (fill.Price - _state.Average) * Spec.MoneyPerPriceUnit;
                _state.Quantity -= fill.Volume;
                if (_state.Quantity == 0) _state.Average = 0;
            }
            _state.Realized -= fill.Fee;
            _state.Fees += fill.Fee;
            _state.Turnover += fill.Volume;
            _state.MaxQuantity = Math.Max(_state.MaxQuantity, _state.Quantity);
            _state.SignedCash -= (intent.Increases ? (int)Spec.Direction : -(int)Spec.Direction)
                * fill.Volume * fill.Price * Spec.MoneyPerPriceUnit + fill.Fee;
            intent = intent with { Filled = intent.Filled + fill.Volume,
                FillNotional = intent.FillNotional + fill.Volume * fill.Price,
                LastFillTime = fill.Time > intent.LastFillTime ? fill.Time : intent.LastFillTime };
            if (intent.Filled == intent.Volume) intent = intent with { State = ApmOrderState.Filled, ReportedFilled = intent.Volume };
            _state.Intents[fill.IntentId] = intent;
            _state.Fills.Add(fill.Id, fill);
            if (NativeLimits != null && fill.Volume != intent.Volume)
                RequireReconciliation("NATIVE_FULL_FILL_CAPABILITY_VIOLATION");
            _state.LastAction = fill.IntentId;
            if (intent.Action == ApmAction.InitialEntry)
            {
                decimal filledAnchor = intent.FillNotional / intent.Filled;
                if ((int)Spec.Direction * (filledAnchor - Spec.HardStopPrice) <= 0
                    || (int)Spec.Direction * (Spec.FinalTargetPrice - filledAnchor) <= 0)
                    RequestExit("INVALID_FILLED_ANCHOR");
            }
            if (_state.ExitLatch && intent.Increases && !_state.Reconciling) _state.Fault = "LATE_FILL";
            if (_state.Quantity == 0 && _state.AnchorFixed) RequestExit("POSITION_FLAT");
            FinalizeEntry();
            if (_state.Market != null) { UpdateEquity(); CheckProtection(_state.Market); }
        }

        private void FinalizeEntry()
        {
            if (!_state.Started || _state.AnchorFixed) return;
            ApmIntent initial = _state.Intents.Values.First(i => i.Action == ApmAction.InitialEntry);
            if (initial.Unresolved) return;
            if (initial.Filled == 0) { RequestExit("ENTRY_NOT_FILLED"); return; }
            _state.InitialFilled = initial.Filled;
            _state.Anchor = initial.FillNotional / initial.Filled;
            _state.PolicyTarget = initial.Filled;
            _state.AnchorFixed = true;
            if ((int)Spec.Direction * (_state.Anchor - Spec.HardStopPrice) <= 0
                || (int)Spec.Direction * (Spec.FinalTargetPrice - _state.Anchor) <= 0)
                RequestExit("INVALID_FILLED_ANCHOR");
        }

        private void UpdateEquity()
        {
            decimal equity = Equity(_state.Market.Price);
            _state.PeakEquity = Math.Max(_state.PeakEquity, equity);
            _state.Drawdown = Math.Max(_state.Drawdown, _state.PeakEquity - equity);
        }

        /// <summary>Independent signed-fill cash identity, including open inventory at the supplied mark.</summary>
        public decimal CashEquity(decimal mark) => _state.SignedCash
            + (int)Spec.Direction * _state.Quantity * mark * Spec.MoneyPerPriceUnit;

        #endregion

        #region Persistence and preview

        /// <summary>Versioned checkpoint, including all intent and deduplication identities; contains no credentials.</summary>
        public string ExportCheckpoint() => JsonSerializer.Serialize(_state);

        /// <summary>
        /// Capture only the immutable state needed by the ordinary preview pipeline. Historical terminal
        /// fill identities are omitted; this payload is diagnostic-only and must never be used for recovery.
        /// </summary>
        public string ExportPreviewState() => JsonSerializer.Serialize(_state.ForPreview());

        /// <summary>
        /// Evaluate a recorded preview on the UI thread without holding the trading owner's lock or using
        /// newer data. Returns only a price; the detached object cannot submit orders or alter the campaign.
        /// </summary>
        public static decimal? PreviewRecorded(string previewState, ApmAction action, int maximumSteps = 1000)
        {
            ApmCheckpoint state = JsonSerializer.Deserialize<ApmCheckpoint>(previewState)
                ?? throw new ArgumentException("Missing APM preview state.");
            if (state.Version != "APM-Preview-v1") throw new ArgumentException("Not an APM diagnostic preview.");
            ApmCampaign copy = new ApmCampaign(state.Spec, state.Policy) { _state = state };
            return copy.Preview(action, maximumSteps);
        }

        /// <summary>
        /// Recover arithmetic and intent identities without resending. A broker snapshot and fresh warmup are
        /// mandatory before increases; ExitLatch is preserved. Malformed/unknown schemas fail closed.
        /// </summary>
        public static ApmCampaign Recover(string json)
        {
            if (json == null || json.Length > 16 * 1024 * 1024) throw new ArgumentException("APM checkpoint exceeds bounded input size.");
            using JsonDocument document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("Version", out JsonElement version)
                || version.GetString() != "APM-Checkpoint-v1") throw new ArgumentException("Missing APM checkpoint version.");
            ApmCheckpoint state = JsonSerializer.Deserialize<ApmCheckpoint>(json)
                ?? throw new ArgumentException("Empty APM checkpoint.");
            if (state.Version != "APM-Checkpoint-v1") throw new ArgumentException("Unsupported APM checkpoint version.");
            ApmCampaign result = new ApmCampaign(state.Spec, state.Policy, state.NativeLimits);
            if (state.Quantity < 0 || state.Fees < 0 || state.Intents == null || state.Fills == null)
                throw new ArgumentException("Invalid APM checkpoint ledger.");
            ValidateCheckpointLedger(state);
            if (state.NativeLimits != null && (state.Intents.Values.Count(i => i.Action != ApmAction.Exit) > state.NativeLimits.OrdinaryIntents
                || state.Intents.Values.Count(i => i.Action == ApmAction.Exit) > state.NativeLimits.ExitIntents))
                throw new ArgumentException("Recovered state exceeds its execution lifetime budget.");
            result._state = state;
            result.RequireReconciliation("RECOVERY_REQUIRES_BROKER_SNAPSHOT");
            return result;
        }

        private static void ValidateCheckpointLedger(ApmCheckpoint state)
        {
            decimal quantity = 0, average = 0, realized = 0, fees = 0, cash = 0;
            Dictionary<string, decimal> quantities = new Dictionary<string, decimal>();
            Dictionary<string, decimal> notionals = new Dictionary<string, decimal>();
            foreach (KeyValuePair<string, ApmFill> pair in state.Fills)
            {
                ApmFill fill = pair.Value;
                if (fill == null || fill.Id != pair.Key || fill.Volume <= 0 || fill.Fee < 0
                    || !state.Intents.TryGetValue(fill.IntentId, out ApmIntent intent))
                    throw new ArgumentException("Invalid recovered fill identity.");
                quantities.TryGetValue(intent.Id, out decimal filled);
                notionals.TryGetValue(intent.Id, out decimal notional);
                quantities[intent.Id] = filled + fill.Volume;
                notionals[intent.Id] = notional + fill.Volume * fill.Price;
                if (intent.Increases)
                {
                    average = (quantity * average + fill.Volume * fill.Price) / (quantity + fill.Volume);
                    quantity += fill.Volume;
                }
                else
                {
                    if (fill.Volume > quantity) throw new ArgumentException("Recovered ledger reverses campaign direction.");
                    realized += (int)state.Spec.Direction * fill.Volume * (fill.Price - average) * state.Spec.MoneyPerPriceUnit;
                    quantity -= fill.Volume;
                    if (quantity == 0) average = 0;
                }
                realized -= fill.Fee; fees += fill.Fee;
                cash -= (intent.Increases ? (int)state.Spec.Direction : -(int)state.Spec.Direction)
                    * fill.Volume * fill.Price * state.Spec.MoneyPerPriceUnit + fill.Fee;
            }
            foreach (KeyValuePair<string, ApmIntent> pair in state.Intents)
            {
                ApmIntent intent = pair.Value;
                quantities.TryGetValue(pair.Key, out decimal filled);
                notionals.TryGetValue(pair.Key, out decimal notional);
                if (intent == null || pair.Key != intent.Id || intent.Volume <= 0 || filled != intent.Filled
                    || notional != intent.FillNotional || filled > intent.Volume || intent.ReportedFilled > intent.Volume
                    || intent.ReportedFilled < 0 || !Enum.IsDefined(intent.State) || !Enum.IsDefined(intent.Action))
                    throw new ArgumentException("Recovered intent and fills disagree.");
            }
            if (quantity != state.Quantity || average != state.Average || realized != state.Realized
                || fees != state.Fees || cash != state.SignedCash)
                throw new ArgumentException("Recovered ledger does not reconcile to actual fills.");
        }

        /// <summary>
        /// Clear reconciliation only after the adapter independently confirms ownership, all order finality,
        /// fills and the actual quantity. A mismatch never adopts a foreign position.
        /// </summary>
        public bool Reconcile(decimal actualQuantity, bool allOrdersKnown, bool ownershipMatches)
        {
            if (!allOrdersKnown || !ownershipMatches || actualQuantity != _state.Quantity
                || _state.Intents.Values.Any(i => i.State == ApmOrderState.Unknown || i.ReportedFilled > i.Filled))
            { RequireReconciliation("RECONCILIATION_REQUIRED"); return false; }
            _state.Reconciling = false;
            _state.FaultedClosing = false;
            return true;
        }

        /// <summary>
        /// Bounded tick-grid preview using cloned full policy state. Null means no ordinary action in the
        /// searched interval; preview cannot reserve, send or mutate the live campaign.
        /// </summary>
        public decimal? Preview(ApmAction action, int maximumSteps = 1000)
        {
            if (maximumSteps < 1 || maximumSteps > 10000 || (action != ApmAction.Add && action != ApmAction.Reduce))
                throw new ArgumentOutOfRangeException(nameof(maximumSteps));
            if (_state.Market == null) return null;
            string frozen = ExportPreviewState();
            int sign = (int)Spec.Direction * (action == ApmAction.Add ? -1 : 1);
            decimal price = (sign > 0 ? Math.Floor(_state.Market.Price / Spec.PriceStep)
                : Math.Ceiling(_state.Market.Price / Spec.PriceStep)) * Spec.PriceStep;
            for (int i = 0; i < maximumSteps; i++)
            {
                price += sign * Spec.PriceStep;
                if ((int)Spec.Direction * (price - Spec.HardStopPrice) <= 0
                    || (int)Spec.Direction * (Spec.FinalTargetPrice - price) <= 0) return null;
                ApmCampaign copy = new ApmCampaign(Spec, Policy);
                copy._state = JsonSerializer.Deserialize<ApmCheckpoint>(frozen);
                ApmDecision decision = copy.OnMarket(_state.Market with { Price = price, Sequence = _state.Market.Sequence + 1 });
                if (decision.Action == action) return price;
            }
            return null;
        }

        #endregion

        // Private schema exposed to System.Text.Json through public properties only.
        private sealed class ApmCheckpoint
        {
            internal ApmCheckpoint ForPreview()
            {
                ApmCheckpoint copy = (ApmCheckpoint)MemberwiseClone();
                copy.Version = "APM-Preview-v1";
                copy.Intents = Intents.Where(p => p.Value.Unresolved || p.Key == LastAction)
                    .ToDictionary(p => p.Key, p => p.Value);
                copy.Fills = new Dictionary<string, ApmFill>();
                return copy;
            }

            public string Version { get; set; } = "APM-Checkpoint-v1";
            public ApmCampaignSpec Spec { get; set; }
            public ApmPolicy Policy { get; set; }
            public Dictionary<string, ApmIntent> Intents { get; set; } = new Dictionary<string, ApmIntent>();
            public Dictionary<string, ApmFill> Fills { get; set; } = new Dictionary<string, ApmFill>();
            public ApmMarket Market { get; set; }
            public bool Started { get; set; }
            public bool AnchorFixed { get; set; }
            public bool ExitLatch { get; set; }
            public string ExitReason { get; set; } = "";
            public bool Reconciling { get; set; }
            public bool Paused { get; set; }
            public bool FaultedClosing { get; set; }
            public ApmOperationalLimits NativeLimits { get; set; }
            public string Fault { get; set; } = "";
            public decimal Anchor { get; set; }
            public decimal InitialFilled { get; set; }
            public decimal Quantity { get; set; }
            public decimal Average { get; set; }
            public decimal Realized { get; set; }
            public decimal Fees { get; set; }
            public decimal Turnover { get; set; }
            public decimal MaxQuantity { get; set; }
            public decimal SignedCash { get; set; }
            public decimal PeakEquity { get; set; }
            public decimal Drawdown { get; set; }
            public decimal PolicyTarget { get; set; }
            public decimal KappaStart { get; set; }
            public decimal Kappa { get; set; }
            public decimal Curve { get; set; }
            public decimal Raw { get; set; }
            public decimal Allowed { get; set; }
            public decimal AddScale { get; set; }
            public decimal ReduceScale { get; set; }
            public ApmRegime Regime { get; set; }
            public DateTime RegimeSince { get; set; }
            public DateTime? DeferStart { get; set; }
            public bool DeferConsumed { get; set; }
            public string LastAction { get; set; }
            public long DecisionNumber { get; set; }
            public long ReservedDecision { get; set; }
        }
    }
}
