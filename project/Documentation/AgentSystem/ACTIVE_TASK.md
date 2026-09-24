# Authoritative active task state

**ID:** `TASK-WINDOW-MODELESS-001`
**Статус:** `DEFERRED`
**Фаза:** `TERMINAL — OWNER-ACCEPTED DEFERRAL`
**Ветка:** `docs/order-flow-production-roadmap`
**Baseline HEAD:** `67b0dc904980b4193c5e53fef92cb454f0dfd937`
**Dirty entry:** original implementation began clean; current boundary retains
the preceding window changes and build outputs plus owner-deferral docs.
**Completed transition IDs:** `SCOPE_ENTRY`, `IMPLEMENT_WINDOWS`, `VERIFICATION`, `PRIMARY`, `INSTALL_DEBUG_OUTPUT`, `OWNER_DEFERRAL`, `REVIEW_DEFERRAL_DOCS`, `HANDOFF_VERIFICATION`
**Next transition ID:** `COMMIT_PUSH`

## Frozen scope

Owner reported on 24.09.2026 that Cloud still stays above Order Flow and blocks
input. Previous completion/desktop behavior claims are withdrawn. The owner
explicitly deferred further diagnosis/fixing and requested commit plus push
of the current changes. Preserve the existing implementation; do not attempt
another runtime fix. Record the unresolved defect, correct documentation and
publish through an ordinary commit and fast-forward push to origin.

Canonical debt: [ORDER-FLOW-TECH-DEBT-001](../OrderFlow/TECHNICAL_DEBT.md),
entry TD-CLOUD-WINDOW-001, OPEN / DEFERRED BY OWNER. This is not a fixed issue.
No application/connector/test-stand launch, credentials or live orders.

## Decisions and impact

Prior code changes retained: singleton OrderFlow Show with explicit shutdown;
Cloud and chart factories no longer set WPF Owner; 92 unconditional Topmost
XAML attributes removed. Static source facts do not establish the reason for
the owner's remaining observed blocking. Actual cause is still undetermined.

Only documentation and two XML comments change in this handoff. No executable
logic, signatures, tests or build configuration change after final verification.
Current guide/spec/runbook now link the known defect instead of promising
working desktop focus/input. New debt document is registered in DOCMAP-001.
OBSERVABILITY: NO CHANGE. MODE PARITY: NO CHANGE.

## Verification status

Existing exact runtime/test boundary:
- Normal Debug solution build PASS: 0 errors / 4 NU1900 audit-index warnings.
- Offline OrderFlowResearch suite: 144 passed / 0 failed.
- Source XAML parse: 174/174; no forced Topmost declarations remain.
- Prior agent validator: 109/109; local links: 23; diff --check PASS.
- Production PRIMARY and documentation PRIMARY were statically CLEAN.

Owner's subsequent manual check FAILED for Cloud stacking/input. Offline tests
and prior reviews never proved the desktop scenario and do not override this
new observation. Further runtime diagnosis and fixing are explicitly deferred.
Documentation handoff review: CLEAN (not a runtime acceptance verdict).
Local links: 43 PASS; agent validator: 109/109; git diff --check: PASS.
No application rebuild required for Markdown/XML-comment-only changes.
Fetched origin: current branch ahead 1 / behind 0 before this handoff commit;
normal fast-forward push will include the prior guided-UI commit as well.

## Blockers and residual debt

TD-CLOUD-WINDOW-001 remains open by owner decision. No blocker to the authorized
documentation/commit/push handoff. No current-turn OsEngine/runtime launch;
no new claim that normal desktop switching or input works.

## Next action

Commit the current implementation, relevant build outputs and deferred-debt
documentation, then
push the current branch to origin without force. Do not resume the deferred fix
unless the owner separately requests it.
