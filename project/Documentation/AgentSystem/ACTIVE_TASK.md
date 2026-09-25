# Authoritative active task state

**ID:** `TASK-ORDER-FLOW-CLOUD-CALIBRATION-IMPLEMENTATION-001`
**Статус:** `IN_PROGRESS`
**Фаза:** `Implementation and reviews CLEAN; commit/push publication`
**Ветка:** `docs/order-flow-production-roadmap`
**Baseline HEAD:** `ec1acf15f342d186f7527f0e8a502b52679766cd`
**Completed transition IDs:** `SCOPE_ENTRY`, `CONTRACT_READ`, `BASELINE_TESTS`, `CORE_BCD`, `CALIBRATION_UI`, `FINAL_TEST_BUILD`, `PRIMARY_REVIEWS`, `REVIEW_FIX`, `VALIDATION_1`, `VALIDATION_2`
**Next transition ID:** `GIT_PUBLICATION`

## Frozen scope and authority

Implement all A–H of ORDER-FLOW-CLOUD-CALIBRATION-001 inside OsData/OrderFlow:
TimeRange, Tick Distribution, parameter heatmap, diagonal delta/stacks,
independent per-range rules/Tuner, Anatomy, detached tables, persistence.
User explicitly authorizes offline OrderFlowResearch suite, solution build,
independent production/documentation reviews, fixes, commit and ordinary
fast-forward push to this same branch. Durable checkpoints requested.
Main alone writes; native research/review roles are read-only.

In scope: project/OsEngine/OsData/OrderFlow/**, project/Tests/OrderFlowResearch/**,
applicable current OrderFlow docs, documentation map and this snapshot.
No trade execution, future outcomes/MFE/MAE/PnL, automatic winner selection,
legacy calculation/hash changes, secrets, live connections or app launch.
Windows physical DPI/focus/mouse acceptance is OWNER-RUN.

## Entry dirty boundary (preserve)

- Modified: Explorer/CloudExplorerControl.Followup.cs, .xaml, .xaml.cs;
  Explorer/CloudExplorerWindow.xaml; OrderFlowResearchUi.ChartTools.cs,
  OrderFlowResearchUi.xaml, .xaml.cs.
- Untracked: DetachedTables/; Explorer/CloudExplorerControl.Tables.cs;
  OrderFlowResearchUi.Tables.cs. Existing detached-table work is relevant
  integration baseline, must not be overwritten; distinguish in review.
- Already modified tracked OsEngine/bin/Debug/OsEngine.dll and .exe;
  never stage build outputs for this task.

## Decisions / current evidence

- Required AGENTS, workflow core/conditional scoped review+impact, DOCMAP,
  calibration spec A–H, detached-table spec, themes and conventions read.
- Source-clock half-open intervals; evening ends 23:51; US pre-open requires
  explicit timezone and DST-aware system conversion; no holiday claim.
- Reuse ExplorerCatalog for formation and existing imbalance Pairs, with
  isolated catalog per range/date. No second Chain algorithm.
- One parameter study parses input once into compact disk cache; bounded
  worker formation/evidence and atomic bundles. Filters use cached evidence.
- Existing detached-table implementation inspected; new tables need sorting,
  numeric/text/range/direction filters and exact-ID navigation.
- Baseline full suite: 172 passed, 0 failed (exit 0). NU1900 feed warning.
- Added Calibration/{CalibrationModels,CalibrationMetrics,CalibrationStorage,
  CalibrationEngine}.cs; immutable profiles/spec/rules, compact fixed-size
  cache, ExplorerCatalog adapter, exact metrics/strict stacks, grid summaries,
  independent rule persistence, atomic checksummed publication.
- Added CalibrationTests.cs and registration: full suite 184 passed, 0 failed
  (exit 0), including 12 calibration groups. Core production build exit 0,
  16 existing warnings, 0 errors. UI implemented subsequently.
- Read-only researcher map complete. Main chart is a partial class; add
  independent overlay/hits in new partial, call from DrawChart, gate replay
  by KnownSequence and EOF (not LastSequence). ExplorerChart has hardcoded
  colors and is not suitable unchanged for new themed calibration UI.

## Verification status

Final full suite: `dotnet run --project Tests/OrderFlowResearch/OsEngine.OrderFlowResearch.Tests.csproj`
194 passed, 0 failed, exit 0 (session 74421 collected, CAL-PROD-004 regression included, test logs console-only). Includes million-tick
fixture, anatomy/filter conservation, exact percent/decimal IDs, global paging,
replay completion, unshown lifecycle, XAML theme and Tuner component.
Final `dotnet build OsEngine.sln -v:minimal`: exit 0, 0 errors, 2 NU1900 warnings
(NuGet vulnerability feed unavailable). Prior full compile had 16 existing code
warnings plus these feed warnings. No code warnings introduced in calibration.
Git Bash agent validator: 109/109 PASS; staged diffcheck PASS; 23 local links PASS.
PRIMARY checkpoint tree ee62670437db0761933e67af6e5a6ace78aabe13.
Independent PRIMARY outcomes BLOCKED pending fixes, not final completion:
CAL-PROD-001 replay prefix lacks SHA so markers hidden; use retained source identity.
CAL-PROD-002 AcceptRun retains _editingRule, can disable independent rule A on B save.
CAL-PROD-003 main timeframe switch blanks transferred single-timeframe chart.
CAL-DOC-001 XML names nonexistent Dispose instead of Closed cleanup.
CAL-DOC-002 preliminary tick distribution requires full grid; add independent
background raw-only preparation and cache reuse before explicit formation.
All five approved in-scope fixes implemented: source SHA retained across real
replay prefixes; AcceptRun clears edit identity; each supported chart timeframe
aggregated directly from disk with <=4000 bars; Closed XML corrected; new
Analyze ticks button publishes raw-only study before grid, grid reuses verified
snapshot with same input path/dates/step. Explicit Analyze refreshes source.
New UI regressions prove prepare without valid grid/no event files, preliminary
statistics survive invalid grid, grid succeeds after removing raw input,
A->B Single save leaves A enabled/visible; replay real Capture/foreignSHA/EOF
and all timeframe bar totals checked. Suite completed194/194, no code failures.
Owner observed invalid-grid MessageBox from negative test: standalone runner had
no ServerMaster.LogMessageEvent subscriber, invoking production fallback. Fixed
test runner only: route logs to stderr and unsubscribe in finally; negative test
asserts exactly one expected error. Production logging unchanged. Rerun194/194 PASS.
VALIDATION_1 checkpoint77de7815b1b23825ac6fd29384cd81ce8d354a0d:
documentation reviewer CLEAN terminal; CAL-DOC-001/002 closed. Production
CAL-PROD-001/002/003 closed; new fix-regression CAL-PROD-004 proven: prepared
cache skipped lowered source-date buffer budget. Main added guard before copy
and 17-date prep(buffer32) -> grid(buffer16, threshold2) direct/reuse rejection
with unchanged bundle count and preserved prepared cache. Final rerun194/194 PASS.
Production VALIDATION_2 CLEAN terminal at tree3453510df0905230cc28cbf9a22927f1273ff0d4;
CAL-PROD-004 closed. All six production/documentation findings closed.
No more review passes required. Semantic docs unchanged since documentation CLEAN.
OBSERVABILITY: REQUIRED — stages/progress/errors and deterministic IDs.
MODE PARITY: NO CHANGE — offline OsData feature only.

## Blockers

No implementation blocker. Physical Windows/DPI/focus remains OWNER-RUN.
NuGet vulnerability feed unavailable (NU1900); compilation and suite succeed.

## Next action

Completed: all core and presentation files in Calibration/, main chart partial,
replay forward-only saved-event cursor/worker, main UI launch/integration,
Anatomy conservation, global bounded table sorting/paging, unshown window tests,
million-tick compact-cache fixture. Core and UI compiled successfully before
latest fixes. Source inspection map from researcher available in chat.
Recent changes: rule index is one atomic rules.json under cloud-calibration-rules
(old semantic rule disabled in same transaction); decimal-scale-canonical IDs;
exact signed-percent comparisons; 100000 saved pairs/event hard storage cap;
US pre-open button gate, theme redraw and translated reader errors.
UI completeness map corrected: independent Chain time-map, exact heatmap tooltip,
transferred chart timeframe, anatomy range/direction filters, localized metric
labels, overlay names in tooltips. Documentation implementation boundary updated
in calibration spec/README/DOCMAP; physical DPI/focus explicitly OWNER-RUN.
Unfinished: commit/push and terminal checkpoint. Final full suite194/194, solution
0errors2NU1900, validator109/109, local links23/23 and whitespace checks PASS.
Next: commit49 staged source/docs/test files, normal fast-forward push same branch,
confirm remote SHA, then persist compact terminal state with implementation SHA.
On restart inspect Git first: do not repeat completed implementation or reviews.
Stage excludes binaries and user DetachedTables/README.md, Verification/.
Do not reimplement existing files. No commit/push yet. Binaries remain unstaged.
