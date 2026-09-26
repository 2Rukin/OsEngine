/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using System;
using System.Collections.Generic;
using System.Linq;

namespace OsEngine.OsTrader.AdaptivePositionManager
{
    /// <summary>One atomic diagnostic capture; preview and rows cannot come from a later market event than Snapshot.</summary>
    public sealed record ApmDiagnosticView(ApmSnapshot Snapshot, ApmAuditRow[] Rows, string PreviewState)
    {
        /// <summary>Detached whole-campaign metrics at the same owner-monitor boundary.</summary>
        public ApmCampaignMetrics Metrics { get; init; }
    }

    /// <summary>A transport command may have reached its destination. Reservations remain intact; never resend on this exception.</summary>
    public sealed class ApmExecutionUncertainException : Exception
    {
        /// <summary>Retain the original transport failure for the owner's normal error logger.</summary>
        public ApmExecutionUncertainException(Exception inner) : base("APM execution outcome requires reconciliation.", inner) { }
    }

    /// <summary>Local audit/checkpoint failure. This classification survives synchronous transport callbacks and forbids further sends.</summary>
    public sealed class ApmPersistenceException : Exception
    {
        /// <summary>Retain the original local IO failure without reclassifying it as an uncertain broker command.</summary>
        public ApmPersistenceException(Exception inner) : base("APM persistence failed; reconciliation is required.", inner) { }
    }

    /// <summary>
    /// Owned-position transport boundary. Implementations must not send account-wide commands or infer fills.
    /// Submission may synchronously call controller callbacks; thrown errors mean uncertain delivery.
    /// </summary>
    public interface IApmOrderGateway
    {
        /// <summary>Submit exactly the reserved quantity through the declared execution model.</summary>
        void Send(ApmIntent intent);
        /// <summary>Request cancellation; retain reservations until an authoritative callback.</summary>
        void Cancel(ApmIntent intent);
    }

    /// <summary>
    /// Serializes market, UI and execution events and flushes intent state before dispatch.
    /// One ordinary send per market event; terminal fills can request another protective close immediately.
    /// </summary>
    /// <remarks>
    /// Gateway calls must return promptly and never wait on UI. Reentrant callbacks are accepted under the
    /// same monitor; native adapters buffer callbacks until order identities are bound. Persistence failures
    /// stop sends. No portable broker reconciliation is fabricated. Contract: APM-EXECUTION-001.
    /// </remarks>
    public sealed class ApmExecutionController : IDisposable
    {
        private readonly object _locker = new object();
        private readonly ApmCampaign _campaign;
        private readonly IApmOrderGateway _gateway;
        private readonly ApmArtifacts _artifacts;
        private readonly Queue<ApmAuditRow> _recent = new Queue<ApmAuditRow>();
        private readonly ApmMetricsAccumulator _diagnosticMetrics = new ApmMetricsAccumulator();
        private ApmMarket _market;
        private long _sequence;
        private bool _disposed;
        private bool _driving;
        private bool _protectionDue;
        private bool _detailedDiagnostics;
        private bool _operatorPaused;
        private bool _enabled = true;
        private readonly ApmReplayWatchdog _watchdog = new ApmReplayWatchdog();
        private readonly ApmAcExecutionPlanner _researchPlanner;

        internal object SyncRoot => _locker;

        /// <summary>Bind one validated campaign to an isolated transport and optional fixture-only in-memory audit.</summary>
        public ApmExecutionController(ApmCampaign campaign, IApmOrderGateway gateway, ApmArtifacts artifacts,
            string executionModel = "SyntheticFixture — исполнение условное")
        {
            _campaign = campaign ?? throw new ArgumentNullException(nameof(campaign));
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _artifacts = artifacts;
            _market = campaign.LastMarket;
            _sequence = _market?.Sequence ?? 0;
            _operatorPaused = campaign.IsPaused;
            if (campaign.Policy.ResearchExecution != null) _researchPlanner = new ApmAcExecutionPlanner(campaign.Policy.ResearchExecution);
            ExecutionModel = executionModel;
        }

        /// <summary>Thread-safe detached operator state.</summary>
        public ApmSnapshot Snapshot { get { lock (_locker) return _campaign.Snapshot; } }
        /// <summary>Immutable operator locks.</summary>
        public ApmCampaignSpec Spec => _campaign.Spec;
        /// <summary>Declared fill model shown in diagnostics. It does not change transport behavior.</summary>
        public string ExecutionModel { get; }
        /// <summary>Immutable behavior parameters for explanations; editing requires a new campaign.</summary>
        public ApmPolicy Policy => _campaign.Policy;
        /// <summary>Finite qualified native execution budget, or null for the generic offline component model.</summary>
        public ApmOperationalLimits NativeLimits => _campaign.NativeLimits;
        /// <summary>Passive wall-clock replay progress; never changes the market clock or sends orders.</summary>
        public ApmReplayHealth ReplayHealth => _watchdog.Read(TimeSpan.FromSeconds(10));
        /// <summary>Record compact preview state for later UI inspection. Does not change decisions, order events or required audit.</summary>
        public bool DetailedDiagnostics
        {
            get { lock (_locker) return _detailedDiagnostics; }
            set { lock (_locker) _detailedDiagnostics = value; }
        }
        /// <summary>Thread-safe copies of the most recent diagnostic rows; full audit is in the journal.</summary>
        public ApmAuditRow[] Recent { get { lock (_locker) return _recent.ToArray(); } }
        /// <summary>Copy one coherent UI view. Preview computation is deferred to the caller outside the owner monitor.</summary>
        public ApmDiagnosticView CaptureView()
        {
            lock (_locker) return new ApmDiagnosticView(_campaign.Snapshot, _recent.ToArray(),
                _detailedDiagnostics ? _campaign.ExportPreviewState() : null) { Metrics = _diagnosticMetrics.Snapshot };
        }

        /// <summary>Serialize one causal market/timer event and dispatch at most its single ordinary proposal.</summary>
        public ApmDecision Process(ApmMarket market) => Process(market, "component");

        /// <summary>Serialize one causal event with an audit-only source label; source never changes decisions.</summary>
        public ApmDecision Process(ApmMarket market, string source)
        {
            lock (_locker)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _market = market with { Sequence = ++_sequence };
                _watchdog.Observe();
                return Drive(false, source);
            }
        }

        /// <summary>UI pause requests cancel active increases on the current snapshot; protection stays enabled.</summary>
        public void Pause(bool paused)
        {
            lock (_locker)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _operatorPaused = paused;
                ApplyPause();
            }
        }

        /// <summary>
        /// Apply the shell's increase permission synchronously. Enabling the shell never clears operator pause;
        /// operator Resume never overrides a disabled shell. Reductions and protection remain enabled.
        /// </summary>
        public void SetEnabled(bool enabled)
        {
            lock (_locker)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _enabled = enabled;
                ApplyPause();
            }
        }

        private void ApplyPause()
        {
            bool paused = _operatorPaused || !_enabled;
            _campaign.Pause(paused);
            if (paused)
            {
                Exception error = CancelConflicts(false, "operator");
                if (error != null) throw new ApmExecutionUncertainException(error);
            }
            Save();
        }

        /// <summary>Latch and persist an operator exit before any cancel or close command.</summary>
        public void Close(string reason = "MANUAL_EXIT")
        {
            lock (_locker)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _campaign.RequestExit(reason);
                Save();
                if (_market != null) { NextSequence(); Drive(true, "operator"); }
            }
        }

        /// <summary>Apply a real fill once; terminal late fills trigger protection using last-known market data.</summary>
        public void ApplyFill(ApmFill fill)
        {
            lock (_locker)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _sequence++;
                _campaign.ApplyFill(fill);
                Audit("Fill", "FILL", fill.Price, fill.Volume, fill.IntentId, "ACTUAL_FILL", fill.Time, "callback");
                Save();
                if (_campaign.Snapshot.ExitLatch)
                {
                    _protectionDue = true;
                    if (!_driving && _market != null) { NextSequence(); Drive(true, "callback"); }
                }
            }
        }

        /// <summary>Apply an authoritative order state without releasing a missing-fill reservation.</summary>
        public void ApplyOrder(string intentId, ApmOrderState state, decimal filled, string brokerId)
        {
            lock (_locker)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _sequence++;
                _campaign.ApplyOrder(intentId, state, filled, brokerId);
                Audit("Order", state.ToString(), 0, filled, intentId, "ORDER_UPDATE", _market?.Time ?? Spec.EntryTime, "callback");
                Save();
                if (_campaign.Snapshot.ExitLatch)
                {
                    _protectionDue = true;
                    if (!_driving && _market != null) { NextSequence(); Drive(true, "callback"); }
                }
            }
        }

        /// <summary>Freeze on ownership, transport or data uncertainty and expose a diagnostic reason.</summary>
        public void Fault(string reason)
        {
            lock (_locker)
            {
                _campaign.RequireReconciliation(reason);
                Audit("Fault", "QUERY_REQUIRED", 0, 0, "", reason, _market?.Time ?? Spec.EntryTime, "adapter");
            }
        }

        /// <summary>Verify a separately obtained authoritative snapshot; never queries or adopts foreign volume.</summary>
        public bool Reconcile(decimal actualQuantity, bool allOrdersKnown, bool ownershipMatches)
        {
            lock (_locker) return _campaign.Reconcile(actualQuantity, allOrdersKnown, ownershipMatches);
        }

        private ApmDecision Drive(bool protectionOnly, string source)
        {
            if (_driving) { _protectionDue = true; return _campaign.Snapshot.Decision; }
            _driving = true;
            try
            {
                _protectionDue = false;
                ApmDecision decision = _campaign.OnMarket(_market);
                if (_researchPlanner != null)
                {
                    decimal cap = _researchPlanner.AllowedVolume(decision, _campaign.Snapshot, Spec.VolumeStep);
                    if (decision.Action == ApmAction.Add || decision.Action == ApmAction.Reduce)
                        decision = _campaign.CapResearchExecution(decision, cap);
                }
                Audit("Decision", decision.Action.ToString(), decision.Price, decision.Volume,
                    decision.IntentId, string.Join(";", decision.Reasons), decision.Time, source);
                Exception cancelError = _campaign.Snapshot.ExitLatch ? CancelConflicts(true, source) : null;
                if (decision.Action == ApmAction.Cancel)
                {
                    cancelError ??= Cancel(_campaign.Intents.First(i => i.Id == decision.IntentId), source);
                }
                else if (decision.Volume > 0 && (!protectionOnly || decision.Action == ApmAction.Exit))
                {
                    ApmIntent intent = _campaign.Reserve(decision);
                    Save();
                    Audit("Intent", intent.Action.ToString(), intent.PriceBound, intent.Volume, intent.Id,
                        "PREPARED_BEFORE_SEND", intent.Time, source);
                    try
                    {
                        _gateway.Send(intent);
                    }
                    catch (Exception error) when (error is not ApmPersistenceException)
                    {
                        _campaign.MarkUnknown(intent.Id);
                        Save();
                        throw new ApmExecutionUncertainException(error);
                    }
                }
                if (cancelError != null) throw new ApmExecutionUncertainException(cancelError);
                return decision;
            }
            finally
            {
                _driving = false;
                // No loop of ordinary orders on one snapshot. A synchronous actual fill is a new event.
                if (_protectionDue && _market != null && _campaign.Snapshot.ExitLatch
                    && _campaign.Snapshot.FilledVolume > _campaign.Snapshot.PendingReduce)
                {
                    _protectionDue = false;
                    NextSequence();
                    Drive(true, source);
                }
            }
        }

        private Exception CancelConflicts(bool terminal, string source)
        {
            Exception failure = null;
            foreach (ApmIntent intent in _campaign.Intents)
                if (intent.CanCancel && (intent.Increases || (terminal && intent.IsLimit)))
                {
                    Exception error = Cancel(intent, source);
                    failure ??= error;
                }
            return failure;
        }

        private Exception Cancel(ApmIntent intent, string source)
        {
            if (!intent.CanCancel) return null;
            _campaign.MarkCancelPending(intent.Id);
            Save();
            Audit("Cancel", "CANCEL_PENDING", intent.PriceBound, intent.Remaining, intent.Id,
                "CANCEL_REQUEST", _market?.Time ?? intent.Time, source);
            try
            {
                _gateway.Cancel(intent);
            }
            catch (Exception error) when (error is not ApmPersistenceException)
            {
                _campaign.MarkUnknown(intent.Id);
                Save();
                Audit("Fault", "CANCEL_UNKNOWN", 0, 0, intent.Id, "EXECUTION_UNKNOWN",
                    _market?.Time ?? intent.Time, source);
                return error;
            }
            return null;
        }

        private void Save()
        {
            try { _artifacts?.SaveCheckpoint(_campaign); }
            catch (Exception error)
            {
                _campaign.RequireReconciliation("PERSISTENCE_FAILURE");
                throw new ApmPersistenceException(error);
            }
        }

        private void NextSequence() { _market = _market with { Sequence = ++_sequence }; }

        private void Audit(string kind, string action, decimal price, decimal volume, string intent, string reason,
            DateTime time, string source)
        {
            ApmSnapshot snapshot = _campaign.Snapshot;
            ApmIntent child = string.IsNullOrEmpty(intent) ? null : _campaign.Intents.FirstOrDefault(i => i.Id == intent);
            int side = child == null ? 0 : (int)Spec.Direction * (child.Increases ? 1 : -1);
            ApmAuditRow row = new ApmAuditRow(_sequence, time, Spec.CampaignId, kind, action, price, volume,
                snapshot.FilledVolume, snapshot.Decision?.RawTarget ?? 0, snapshot.Decision?.RiskAllowedTarget ?? 0, reason, intent, snapshot,
                child, side > 0 ? "Buy" : side < 0 ? "Sell" : "",
                kind == "Fill" && child?.DecisionPrice != null ? side * (price - child.DecisionPrice.Value) : null,
                _detailedDiagnostics ? _campaign.ExportPreviewState() : null) { Source = source };
            try { _artifacts?.Append(row); }
            catch (Exception error)
            {
                _campaign.RequireReconciliation("PERSISTENCE_FAILURE");
                throw new ApmPersistenceException(error);
            }
            _diagnosticMetrics.Add(row);
            _recent.Enqueue(row);
            while (_recent.Count > 2000) _recent.Dequeue();
        }

        /// <summary>Release audit resources only after the owner stops callbacks; does not send or pretend to flatten.</summary>
        public void Dispose()
        {
            lock (_locker)
            {
                if (_disposed) return;
                _artifacts?.Dispose(); _disposed = true;
            }
        }
    }
}
