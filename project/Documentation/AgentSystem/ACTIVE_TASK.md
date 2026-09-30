# Authoritative active task state

**ID:** `TASK-THG-SIMPLE-017`
**Статус:** `COMPLETE`
**Фаза:** `TERMINAL`
**Ветка:** `docs/order-flow-production-roadmap`
**Baseline HEAD:** `41b240332d140337fc4b5c5c44ae35a96e1939a1`
**Entry dirty boundary:** only project/OsEngine/bin/Debug/OsEngine.dll and .exe;
existing output is preserved or rebuilt, never restored to older binaries.
**Completed transition IDs:** `ENTRY_BASELINE_VERIFIED`, `REUSE_RESEARCH`, `SIMPLE_IMPLEMENTATION`, `MAIN_BUILD`, `SOLUTION_BUILD`, `MANAGED_CASES`, `PRIMARY`, `FIX`, `VALIDATION_1`, `TERMINAL_CLEAN`
**Next transition ID:** `OWNER_UI_CHECK`

## Frozen scope

Add ordinary native robot Futures2GridSimple: manual inclusive lower/upper
bounds, exact grid step, common markup and quantity with optional per-zone
overrides. Long/Short, signed/zero prices through five decimal places, runtime
cancel-confirm configuration and existing Futures2 ownership/recovery. Reuse
controller/native adapter/store; keep full Futures2Grid behavior compatible.
Separate checkpoint namespace, compact parameters and status view. Add offline
regression cases and current registered operator/design documentation.

Main is the only writer. Read-only codebase research completed; independent
production and documentation scoped reviews follow the implementation checkpoint.

## Verification status

- Main build --no-restore: PASS, 0 errors / 16 existing warnings.
- Full solution --no-restore: PASS, 0 errors / 1 existing NU1900 warning in the
  final incremental build (unavailable NuGet vulnerability feed); initial full
  build had 28 existing warnings and no errors.
- Managed TradeHelpGrid suite --no-build: PASS, 1026/1026 assertions (91 new).
- Source/test diff whitespace PASS; new guide links PASS; agent validator
  PASS109/109 after correcting the active-state headings.
- Independent production and documentation: PRIMARY -> FIX -> VALIDATION_1 ->
  CLEAN/CLEAN. THG-SIMPLE-P01 closed: after Off reconfiguration, the next host
  pass restores exit management without permitting entries. Regression covers
  four synthetic live/simulation-flag and pause-before/after combinations.
  THG-SIMPLE-DOC-01/02 closed: target tick-rounding and exact TickValue fallback.
  No native/UI/live evidence claimed; default suite uses managed fixtures.

Build production and solution; run managed TradeHelpGrid component cases, agent
validator and diff checks. No credentials, broker session, real orders, application
or native test-stand launch. User subsequently authorized rebuilding the application,
committing the completed Simple implementation and pushing the current branch.
Existing historic/native evidence belongs to Futures2Grid and is not claimed as
qualification of the new host.

OBSERVABILITY: REQUIRED — preview, accepted/pending plan, state/reason, native
intent quantities and IDs, recovery diagnostics and standard logging implemented.
MODE PARITY: REQUIRED — common decision engine; live recovery versus simulation
reset/autostart must be explicit. Physical UI and connector evidence OWNER-RUN.

## Blockers

No implementation blocker. Physical UI/native replay/live are OWNER-RUN and
are not prerequisites for the authorized managed implementation scope.

## Next action

Open the rebuilt project/OsEngine/bin/Debug/OsEngine.exe, create Futures2GridSimple,
configure only its first tab and follow THG-SIMPLE-017. Physical native replay/UI
and live qualification remain explicit owner scenarios. The publication commit
contains the reviewed source, tests, documentation and rebuilt main executable/DLL.
Publication targets origin/docs/order-flow-production-roadmap using ordinary push;
Git HEAD/upstream records the resulting commit and synchronization state.
