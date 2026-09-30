# Authoritative active task state

**ID:** `TASK-THG-SRU6-015`
**Статус:** `COMPLETE`
**Фаза:** `TERMINAL`
**Ветка:** `docs/order-flow-production-roadmap`
**Baseline/current HEAD:** `088add98b728f8088fb18ff2e59c8d4113ad043c`
**Entry dirty boundary:** `TEMP/TradeHelp4-analysis/sru6-entry.json`, SHA-256 `21cf59f762702520d41143bbda9042815eda19ddd4cbc83759f1725b63ad5f6b`
**Completed transition IDs:** `SRU6_ENTRY`, `SRU6_RESEARCH`, `SRU6_IMPLEMENTATION`, `SRU6_NATIVE_REPLAY`, `SRU6_OFFLINE_VERIFY`, `SRU6_PRIMARY_REVIEW`, `SRU6_FIX`, `SRU6_REVERIFY`, `SRU6_VALIDATION_1`, `SRU6_TERMINAL`
**Next transition ID:** `SRU6_HANDOFF`

## Frozen scope and authorization

User authorized offline historical testing of Futures2Grid with `C:\Qscalp\SRU6.txt`
and related `C:\Qscalp\SRU6\*.qsh`, comparison with existing OsEngine negative-price
support, fixes for reproducible reachable findings, and a final owner manual-test report.
Scope: current signed-price/Futures2/Tester integration; Tests/TradeHelpGrid; registered
current qualification/operator/completeness documents, DOCMAP and this snapshot.
Read external SRU6 data only; no conversion or mutation of source files.
Main sole writer. No live connector, broker/account, credentials, real/paper orders,
MCP/StopOrders stand, commit, push, reset, rebase or merge.

## Decisions and acceptance

Use SRU6 as the primary positive-price historical set. Treat TXT trades and QSH Quotes
as separate replays because current Tester selects one QSH stream per symbol/day and
does not prove synchronized Deals+Quotes playback. Existing native negative-price
behavior is authoritative where reachable; retain the explicit zero-presence contract
needed by Futures2 and remove/repair duplication only when executable evidence proves it.
Historical replay must use real rows/frames, deterministic configuration and recorded
totals. Fix only defects reproduced on current checkout, with regression coverage.
Acceptance: executable SRU6 evidence, existing managed suite and full solution build;
clear execution-model limits; production and documentation scoped reviews if code/docs
change; final Russian report splits automated PASS from exact manual cases.

## Verification status

Entry inherited from terminal 014: 935/935 managed assertions, full build 0 errors and
17 existing warnings, validator 109 PASS, reviews CLEAN/CLEAN. Current managed suite
still 935/935; full solution build0errors/17 existing warnings plus NU1900. Native zero-loader reproduced FAIL before fix and
PASS after fix. Native signed fixture PASS12orders/12fills; exact12/12 delivery on
each tab, negative/literal-zero/fifth-decimal order and fill prices, replay end and
intent/order/fill ledger match are asserted. SRU6 TXT PASS6/6 flat with exact240/240
delivery per tab; QSH5min PASS2/2 with one depth callback per tab. Both reached the
configured replay end and matched the Futures2 ledger. Recorded errors0.
Static data audit: SRU6.txt has 3,130,667 ordered seven-field trades across 174 days;
171 each v4 one-stream QSH Deals/Quotes/AuxInfo files have valid gzip headers.
Agent validator109PASS, local links4docsPASS, evidence JSON parses, diff-checkPASS.
PRIMARY found `THG-SRU6-SAF-001`, `THG-SRU6-DOC-001` and `THG-SRU6-DOC-002`;
all three bounded fixes are applied and reverified. VALIDATION_1 scope10-file
aggregate SHA-256 `fd9fb0d00b5782ea7549508e9a1339db5460a8536b365c38d193736b3e7057b1`.
Production and documentation VALIDATION_1 outcomes are CLEAN/CLEAN; terminal
review is `THG-HISTORICAL-REVIEW-015`.

## Blockers

None for offline research. Historical Tester cannot prove partial fills, exchange queue,
transport uncertainty, live TRANSAQ recovery or broker-side state. Those remain manual/
owner-run or synthetic fault-injection evidence and must not be reported as replay PASS.

## Next action

Return the terminal Russian handoff with automated evidence and exact remaining
manual/owner-run cases. No further repository action is authorized.
