# Authoritative active task state

**ID:** `TASK-ADAPTIVE-POSITION-MANAGER-001`
**Статус:** `BLOCKED`
**Фаза:** `CP17 fixes and reviews TERMINAL CLEAN; T12 owner/research gates remain`
**Ветка:** `docs/order-flow-production-roadmap`
**Baseline HEAD:** `1a6163b59ec80b084da50d2edf560f4171cad829`
**Specification baseline:** `f54de33961d45f73319ae1c7313f2854bcb27398`
**Completed transition IDs:** `T01_T11_IMPLEMENTATION_AND_REVIEWS`, `T12_FEASIBLE_VERIFICATION`, `OWNER_WALKTHROUGH_FINDINGS_FIX`, `OWNER_UX_FIX_REVIEWS_CLEAN`, `CP17_PRIMARY`, `CP17_FIX_AND_FINAL_VERIFICATION`, `CP17_VALIDATION_TERMINAL_CLEAN`
**Next transition ID:** `OWNER_MANUAL_UI_ACCEPTANCE`

## Frozen scope and authority

Owner explicitly requests review of CP16 UX fixes, correction of findings, commit
and ordinary fast-forward push. Main sole writer. Runtime scope is 14 source/test
files listed by exact GitBlob/SHA256 in
`docs/adaptive-position-manager/evidence/cp17-verification.json`, plus matching
APM docs, DOCMAP and this snapshot. Robot directory remains
`OsEngine/Robots/MyBots/AdaptivePositionResearchBot/`.
No reset/rebase/merge/force, live/paper/broker/MCP/StopOrders, credentials or
physical-DPI simulation. Synthetic native Tester and UI checks are authorized.
Earlier closed review identities remain historical; CP17 is newly owner-requested.

## Dirty boundary and build isolation

16 pre-existing tracked DLL/EXE changes and three untracked generated fixture
files remain excluded: bin outputs of OsEngine, DividendsUpdater, McpTestStand,
OrderFlowResearch, StopOrdersTestStand, WikiConnectionTest; S01 ticks
SecuritiesSettings.txt/apm-audit.csv and S02 ticks SecurityTestSettings.txt.
19/19 match CP17 entry SHA256 in TEMP/OsEngine-APM-cp17-c39ec605eb5c4a00a9f6055bf26f054a/excluded-before.json.
DetachedTables changes were separately committed before baseline; outside scope.
Five DividendsUpdater artifacts remain excluded. Updater source unchanged;
original before-image of earlier AfterBuild effect unknown. Keep both sets:
TEMP/OsEngine-APM-output-recovery/{current-app-output,earlier-test-output},inventory.json.
Do not restore from HEAD or another build by guess. Exact undo requires proven
before-image; both saved sets remain backup/evidence.
All builds use TEMP OutputPath and
`Tests/AdaptivePositionManager/IsolatedBuild.targets` through
CustomAfterMicrosoftCommonTargets to redirect updater copies to updater-aux.
Normal application bin has not been refreshed by CP17.

## Verification status

- CP16 implements UX01–08: grouped status, semantic state styling, schedule-only
  disabled Start, typed run-level campaign/report rows, unique CSV names,
  normalized paths, audit Source. Original work record retains earlier reviews.
- CP17 production F01/docs D01 fixed: button Pause uses current controller state;
  historical fields remain historical. Both history directions tested.
- Main proved clipped buttons at minimum 520x320 DIP; flexible status row now
  keeps controls inside content viewport. SizeChanged logs exceptions.
- Docs D02/D03 corrected current/history evidence boundary and path error claims.
  New manifest registers exact source/binary/artifact hashes, commands and exits.
- Build: full solution, exit0, 0errors/17warnings (16 existing + NU1900).
- Offline: 8580/8580 PASS. UI: S01/S02/S08/S09/S19 PASS at actual DPI144;
  1280x720 physical viewport and 520x320 DIP minimum. No LayoutTransform simulation.
  Actual Pause/Resume/manual/emergency clicks, latch/reason/q0 and cleanup tested.
  Two displayed campaign rows are synthetic projections, not native lifecycle proof.
- Native Tester S01 path-crlf: PASS 10,8,6,8,10,6,8,0.
  Native Tester S02 plain: PASS 10,14,18,14,18,14,0.
- Build and executable: TEMP/OsEngine-APM-cp17-final/.
  UI: TEMP/OsEngine-APM-cp17-ui-final/.
  Native: TEMP/OsEngine-APM-cp17-native-S01/ and -S02/.
- APM_CP17_REVIEW and APM_CP17_DOCS VALIDATION_1: TERMINAL CLEAN.
  14/14 source hashes/blobs and 14/14 artifact hashes independently verified;
  47 local links, zero broken. Scoped diff whitespace PASS; agent validator 109/109 PASS.
- CP17 publication is identified by the containing Git commit. Ordinary push is
  explicitly authorized; remote SHA verification follows the commit in handoff.

## Blockers

QG06 physical DPI100/125/200 and repeat owner walkthrough: OWNER-RUN/NOT_RUN.
T12 remains blocked by manual/research gates; no physical checks are simulated.
ResearchOnly: native TradeOnly Tester/Optimizer capability exists; historical
file/period/timezone/economic profile not qualified. Baseline/OOS/cost-stress,
book/OFI production adapter, full ADAPT, native restart/live recovery and
profitability readiness remain unproven. Live is rejected.
XML-doc current-vs-historical contract updated; audit Source additive;
sizing/risk/order dispatch unchanged. Full current task evidence and historical
reviews are in docs/adaptive-position-manager/evidence/implementation.md.

## Next action

After authorized scoped publication, owner runs physical DPI100/125/200 and
walkthrough of fixed UI. Record actual evidence before closing QG06/T12; select
and qualify real historical data separately. Do not reopen terminal CP17 reviews
or repeat unchanged execution evidence automatically.
