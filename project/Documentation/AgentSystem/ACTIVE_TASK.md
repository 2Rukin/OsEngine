# Authoritative active task state

**ID:** `TASK-ORDER-FLOW-SRU6-CALIBRATION-001`
**Статус:** `COMPLETED`
**Фаза:** `TERMINAL_REPORT_DELIVERED`
**Ветка:** `docs/order-flow-production-roadmap`
**Baseline/current HEAD:** `97bd89fb676d074842169a1f9041af595b7eb3ec`
**Completed transition IDs:** `ENTRY_BASELINE_VERIFIED`, `RESEARCH_SPEC_FROZEN`, `CALCULATE_DEVELOPMENT_AND_VALIDATION`, `SELECT_STABLE_SETTINGS`, `WRITE_AND_REGISTER_REPORT`, `DOC_REVIEW_PRIMARY_FIX_VALIDATION_1_CLEAN`
**Next transition ID:** `OWNER_VISUAL_CLOUD_ASSESSMENT`

## Frozen scope

Owner requests an offline descriptive Cloud calibration study for local SRU6
tick text from 2026-06-01 through 2026-08-31 inclusive, followed by one Markdown
report linked in chat. Goal: recommend settings for owner visual inspection,
with evidence and limitations; no profitability or live-readiness claim.

Study source:
`OsEngine/bin/Debug/Data/Set_SRTicks/SRU6/Tick/SRU6.txt`.
Manual PriceStep is 1. Source-clock profiles are FORTS Morning 07:00-10:30,
FORTS Main 10:30-19:00 and FORTS Evening 19:00-23:51 exclusive. June-July is
development; August is chronological validation. Tick threshold is frozen from
the development P95 for each profile. Candidate grid uses Gap
100/250/500/1000/2000 ms and Range 1/2/3/5/8/13 ticks. Chain summaries require
TradeCount >= 2. Selection favors adequate observations, low neighbor sensitivity
and stable validation rather than the largest isolated metric.

In-scope writes: final registered Markdown report, DOCMAP entry and this state.
A temporary same-assembly offline harness and all large calibration bundles live
outside final repository scope and are removed after extracted evidence is frozen.
No application, connector, MCP/StopOrders stand, broker session, orders,
credentials, commit, push, reset, rebase or merge.

## Baseline and dirty boundary

The existing Order Flow time-profile implementation remains dirty and completed;
it is not modified by this research run. User/runtime dirty binaries
`OsEngine/bin/Debug/OsEngine.dll` and `.exe`, the tracked APM schedule fixture,
and three untracked APM fixture files are preserved. All builds use isolated
temporary output. Input tick data is read-only and is not registered in Git.

Input inventory at entry: 158316586 bytes, 3130667 physical rows, spanning
2026-03-16 through 2026-09-17. Requested date rows: June 540116, July 1329413,
August 766308; total 2635837 before profile/day filtering.

## Research and evidence decisions

- Calculations use current production calibration engine and exact source-order
  parser through a temporary friend-assembly harness; no reimplementation of
  Cloud formation is accepted as evidence.
- Development and validation are chronological. August is not used to choose the
  tick threshold or candidate formation.
- Report includes all tested variants or an attached compact table, dataset/code
  identity, data quality, descriptive metrics, selection rationale and limitations.
- This is descriptive formation calibration only. No future price reaction,
  commissions, slippage, fills, PnL, StrategySpec or final OOS is calculated.
- OBSERVABILITY: NO CHANGE. MODE PARITY: NO CHANGE. Trading/order/risk behavior:
  NO CHANGE.

## Verification status

- Branch/HEAD/status and relevant dirty boundary inspected.
- Source file existence, size, first/last timestamps and requested monthly row
  counts verified read-only.
- Applicable authorities: DOCMAP-001, ORDER-FLOW-CLOUD-CALIBRATION-001,
  ORDER-FLOW-RESEARCH-001 and AGENT-WORKFLOW-001.
- Input SHA-256:
  `435EA400FFCC45CD3215BE0806F660368A024D1C2942B8EED8AA8E3D2FED1F7B`.
  Requested dates accepted 2635837 rows. June-July is development; August is
  validation. Full grid is 90 cells per segment pair across three weekday FORTS
  profiles; chosen formation is Gap2000/Range5/Context30, diagonal off.
- Primary thresholds are Morning tick10/cloud85/absDelta75/trades2, Main
  tick14/cloud367/absDelta343/trades2, Evening tick10/cloud89/absDelta82/trades2.
  A separate DayMask127 control matches the main editor's daily applicability.
- Report `Documentation/OrderFlow/SRU6_CLOUD_CALIBRATION_JUN_AUG_2026.md`
  SHA-256 `0EE829AA17B636A06C94A4FD884A7BCC23E4FA838165F7C623E27DEEF2EECF1A`.
  Compact/full-grid summary SHA-256 `7ACB2B...3A69`, selected-filter summary
  `34D8B91E...25B0`, daily control `5786B5D0...C939`.
- Isolated calculation build: 0 errors; only existing compiler and NU1903
  dependency warnings. Temporary harness source and work bundles removed from
  repository scope; compact evidence remains in the task TEMP directory.
- Agent validator: 109/109 PASS. `git diff --check`: PASS. Report registration
  and links checked. OsEngine application/process was not launched or stopped;
  existing PID20252 and dirty native bin files were preserved.
- Independent documentation review PRIMARY found one calendar-transfer mismatch.
  Main fixed it with the DayMask127 control. VALIDATION_1: CLEAN; 9 rows / 42
  numeric cells and 4/4 hashes independently checked.
- No production behavior was changed by this research/report step. OBSERVABILITY:
  NO CHANGE. MODE PARITY: NO CHANGE. Visual chart assessment: OWNER-RUN/NOT_RUN.
- Commit/push: NOT RUN; no authorization.

## Blockers

None. Physical chart evaluation remains OWNER-RUN by explicit request and is the
next separate transition, not a blocker for the completed offline report.

## Suspended/completed prior work

TASK-ORDER-FLOW-TIME-LAYERS-001 is completed on the same unchanged HEAD and dirty
worktree: full offline regression 213/213, main build 0 errors, production and
documentation review terminal CLEAN. No commit/push. TASK-ADAPTIVE-POSITION-
MANAGER-001 remains blocked only on its separate owner physical acceptance.

## Next action

Owner enters the three recommended daily periods and visually evaluates density,
chain merging near 10:30/19:00 and sign distribution. Any tuning starts from the
two documented P75/P95 alternatives; do not claim profitability from this report.
