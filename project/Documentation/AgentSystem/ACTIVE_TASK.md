# Authoritative active task state

**ID:** `TASK-THG-SIMPLE-READINESS-018`
**Статус:** `COMPLETE`
**Фаза:** `TERMINAL`
**Ветка:** `docs/order-flow-production-roadmap`
**Baseline HEAD:** `127137fa318fbac93f57425da59079defcdcc5b5`
**Entry dirty boundary:** clean.
**Completed transition IDs:** `ENTRY_BASELINE_VERIFIED`, `NULL_SECURITY_PATH_CONFIRMED`, `READINESS_FIX`, `ISOLATED_SOLUTION_BUILD`, `MANAGED_CASES`, `PRIMARY_CLEAN_CLEAN`, `INSTALL_DEFAULT_OUTPUT`
**Next transition ID:** `OWNER_UI_CHECK`

## Frozen scope

User reported Simple Apply -> Plan -> Build(null) throwing before any plan/order
is created. Handle unavailable first-tab instrument metadata as a visible
preparation state in Simple Apply/Start/simulation initialization. Keep strict
builder validation, native readiness/reconciliation and connector configuration
unchanged. No fallback to second tab, fabricated instrument or automatic live
start. Add managed host regression cases and update THG-SIMPLE-017.

The source of the user's null metadata (unselected instrument versus connector
readiness) is not yet established; clarification requested. Code shows either
can occur. Main is sole writer, independent research/reviews read-only.

## Verification status

Owner closed the running application. Default main build subsequently PASS:
dotnet build OsEngine/OsEngine.csproj --no-restore -p:CopyRetryCount=0,
0 errors / 16 existing warnings. Updated EXE/DLL installed to OsEngine/bin/Debug.
The former MSB3021 file-lock blocker is resolved; application was not launched.

- Isolated solution: PASS0errors/29warnings using
  dotnet build OsEngine.sln --no-restore -p:OutDir=%TEMP%/Futures2Simple-readiness-018/.
  Warnings:28 existing plus one resource-copy retry from shared output.
- dotnet %TEMP%/Futures2Simple-readiness-018/OsEngine.TradeHelpGrid.Tests.dll:
  PASS1047/1047, including21 new readiness assertions.
- Production/docs independent PRIMARY both CLEAN, no findings. XML, guide links
  and diff whitespace PASS. No actual user session reproduction claimed.
- No broker/native/UI execution, credentials or configuration-file reads.

OBSERVABILITY: REQUIRED — readable deduplicated preparation message and status.
MODE PARITY: REQUIRED — simulations wait for metadata; live still explicit start.
No credentials/config files, native sessions, broker operations or app launch.
User subsequently authorized commit and ordinary push of this completed fix.

## Blockers

No implementation or installation blocker. User mode/selection still unknown;
physical UI/live evidence remains OWNER-RUN.

## Next action

Owner may launch OsEngine/bin/Debug/OsEngine.exe and retry first-tab instrument
selection / Apply. Source/test semantics remain frozen; isolated1047/1047 and
CLEAN/CLEAN evidence reused. The publication commit includes the reviewed fix,
tests, documentation and rebuilt EXE/DLL. Publication targets
origin/docs/order-flow-production-roadmap; Git HEAD/upstream records the result.
