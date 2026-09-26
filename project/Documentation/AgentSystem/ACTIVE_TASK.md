# Authoritative active task state

**ID:** `TASK-ADAPTIVE-POSITION-MANAGER-001`
**Статус:** `BLOCKED`
**Фаза:** `Local ResearchOnly implementation/evidence complete; owner/data/capability gates remain open; checkpoint CP14`
**Ветка:** `docs/order-flow-production-roadmap`
**Baseline HEAD:** `06d2630c693e3ed76c5c43502d59d1a44de2b4ac`
**Verified pre-publication HEAD:** `06d2630c693e3ed76c5c43502d59d1a44de2b4ac`
**Specification baseline:** `f54de33961d45f73319ae1c7313f2854bcb27398`
**Completed transition IDs:** `SCOPE_ENTRY`, `WORKFLOW_READ`, `T01_CAPABILITY_PROOF`, `CORE_INITIAL_TESTS`, `MATH_CONTRACT_VALIDATION`, `CORE_VALIDATION_1_CLEAN`, `SOLUTION_BUILD_CP02`, `T05_ADAPTER_VALIDATION_2_CLEAN`, `T06_FEATURE_REVIEW_CLEAN`, `T07_REGISTERED_ROBOT_NATIVE_TESTER`, `T07_ROBOT_VALIDATION_1_CLEAN`, `T07_NATIVE_EVIDENCE_VALIDATION_1_CLEAN`, `T07_CURRENT_DOCS_VALIDATION`, `BUILD_OUTPUT_ISOLATION_VERIFIED`, `T08_REVIEWS_CLEAN`, `T09_T10_MODELS_VALIDATION_1_CLEAN`, `T11_OPERATIONS_VALIDATION_1_CLEAN`, `T12_NATIVE_UI_STUDY_VALIDATION_1_CLEAN`, `T12_FINAL_BUILD_OFFLINE_COVERAGE`, `T12_FINAL_DOCS_AND_EVIDENCE_REVIEW`
**Next transition ID:** `OWNER_DPI_WALKTHROUGH_EVIDENCE`

## Frozen scope

User requested T01–T12, including T08–T11 and feasible T12 without waiting for
physical DPI 100/125/200 or owner walkthrough. Do not simulate those manual checks.
Approved robot: OsEngine/Robots/MyBots/AdaptivePositionResearchBot/.
Main sole writer; independent reviewers read-only. Changed APM core/adapter/UI,
robot, Tests/AdaptivePositionManager, solution, opt-in native replay heartbeat,
Optimizer metadata/research lifecycle, APM docs/routers/DOCMAP.
Synthetic native Tester/Optimizer runs authorized and completed. User explicitly
authorized APM commit and ordinary fast-forward push on 2026-09-26. No reset/rebase/
merge/force, MCP/StopOrders/live/broker/paper, secrets or real credentials.
Historical dataset/interval/timezone/economic profile remain unselected.

## Dirty boundary and safe build

CP14 baseline verified; APM-only staging/publication authorized. Entry had 16 dirty DLL/EXE outputs; user DetachedTables/
README.md and Verification/ remain excluded. Existing DividendsUpdater AfterBuild
previously copied five artifacts into app output despite OutputPath; original
before-image unknown. Four tracked outputs: DLL/EXE/deps/runtimeconfig; one ignored
PDB. Only DLL/EXE Git-modified; updater source untouched.
Owner requires all five excluded from APM commit and both saved sets retained:
TEMP/OsEngine-APM-output-recovery/{current-app-output,earlier-test-output},inventory.json.
CP14: 10/10 backup hashes match; current sources equal their respective saved set.
Safe proposal: preserve bytes and both backups; exact undo needs proven before-image.
HEAD or older test output would be a replacement, not proven rollback. No restoration.
Every build must use explicit Tests/AdaptivePositionManager/IsolatedBuild.targets
via CustomAfterMicrosoftCommonTargets plus TEMP OutputPath. It redirects updater
copy to TargetDir/updater-aux; default build/updater csproj unchanged.

## Implemented boundary

- Core: immutable locks, reversible decimal sizing, single-owner ledger, pending/
  unknown reservations, irreversible ExitLatch and durable intent before send.
- Native TradeOnly: strict saved ticks/hash/metadata, flat dedicated tab, actual
  fills; live/Candle/depth/native automatic protection rejected. Replay timer after
  source.Load handles silent intervals but does not refresh price.
- UI: compact recorded preview, five ownerless tables, sorting/filter/CSV,
  passive watchdog. Study report opens separately, never inside optimizer passes.
- T08: native fixed/grid/filter/IS/OOS/rolling WFO/all-trials; optional research
  lifecycle saves plan before native counting and results before terminal event.
  Stop during IS/OOS finalizes exactly once and restores the original output root.
- T09: event/sample OFI, bounded window/reset, causal calibration components;
  synchronized native trades+quotes adapter/A4 absent. No fake OFI in TradeOnly.
- T10: AS/AC/ADAPT references, default-off ordinary AC pacing; protective EXIT
  bypasses pacing. Explicit settings and past cutoff; full ADAPT controls/phi absent.
- T11: native lifetime ordinary/exit budgets 256/257, retained identities/fills;
  partial capability violation accounts actual volume and requires reconciliation.
  Recover input is 16,777,216 UTF-16 code units; envelope file limit 32 MiB.
  Generic partial model does not claim bounded full ledger; native restart adoption absent.

## Closed independent reviews

Earlier T01–T07/build identities remain closed. T08 production/docs VALIDATION_1 CLEAN;
T09/T10 models 001/002 VALIDATION_1 CLEAN; T11 operations 001 VALIDATION_1 CLEAN;
Native UI study 001 VALIDATION_1 CLEAN. T12 additional test/evidence PRIMARY CLEAN.
T09–T12 final docs/XML 001/002 fixed without runtime change; VALIDATION_1 TERMINAL CLEAN.
No pending reviewer. Do not reopen closed identities on state-only updates.

## Verification status

Commands/hashes/22-requirement traceability: docs/adaptive-position-manager/evidence/release-manifest.json.
Final solution build with isolated import: 0 errors / 1 NU1900 warning in neighboring
OrderFlowResearch (NuGet audit unavailable); prior production compile had 16 legacy warnings.
Offline 8559/8559 PASS. Coverlet 10.0.1 core files: 744/820 branches = 90.73%; whole
module 1291/1985 = 65.04%, not claimed >=90%. Raw: TEMP/OsEngine-APM-release-coverage.
Build: TEMP/OsEngine-APM-release-build. Production SHA256:
6373692E6B8063E3F6F152F9D704EFEA47C1E55223FA981A362144F03DD1005C.
Final native Tester S01 full-ui, S02 controls, legacy Off/saved Volume7 PASS.
Final native Optimizer B1/A5/fixed/filtered/grid PASS; six grid hashes match at 1/3 threads.
IS stop: 6 planned/1 completed/5 NotObserved; OOS stop: 6/4/2; one terminal event each.
Four-phase rolling WFO PASS; second IS includes three whole past campaigns.
Actual study report screenshot retained. Prior 26 Tester/11 Optimizer studies,
10 repeats and cost/FAST evidence retained with checkpoint-specific boundaries.
Component load: two 20,000-event runs, 19,594/12,857 events/sec vs measured peak200;
queue512, retained33 intents/history2000. Send/quantity projection parity only;
finite GC growth is not memory plateau, native/UI or real-data load qualification.
Independent hashes: 49/49 source,65/65 evidence, two DLLs and raw coverage PASS.
Agent validator109/109; local links149/149; XML syntax255 blocks; whitespace PASS.

## Blockers

QG06 only physical DPI100/125/200 and owner walkthrough NOT_RUN; actual150% exists.
QG08 historical untouched data/economic profile BLOCKED_DATA. Native Tester and
Optimizer fill timing differs; silent-market fill may use previous tick timestamp.
QG09 native book/calibration/A4 BLOCKED_DATA_CAPABILITY. Default QG10 N/A (AC off);
opt-in A5 empirical qualification BLOCKED_DATA. QG11 native-session/live recovery,
real-peak native/UI load and shadow/paper remain BLOCKED. Full QG12 BLOCKED.
No overall Engineering PASS, Research GO or live authorization. Commit/push
authorized after CP14; the containing Git commit identifies this publication.

## Next action

Receive actual DPI/walkthrough evidence; then select historical data/intervals and
predeclared economic profile for separately scoped research. Book and native/live
recovery require capability implementation/qualification, not just credentials.
Do not rerun closed reviews or launch external sessions without new relevant scope.
Current task/gates: docs/adaptive-position-manager/evidence/implementation.md.
Owner runbook/rollback: docs/adaptive-position-manager/16-release-readiness.md.
