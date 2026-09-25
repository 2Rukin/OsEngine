# Authoritative active task state

**ID:** `TASK-CLOUD-CALIBRATION-BOUNDED-MEMORY-001`
**Статус:** `IN_PROGRESS`
**Фаза:** `VERIFIED — publication pending`
**Ветка:** `docs/order-flow-production-roadmap`
**Baseline HEAD:** `e3ec754aa1880143a9763b150f67df3f74a3523e`
**Completed transition IDs:** `SCOPE_ENTRY`, `CONTRACT_READ`, `BOUNDED_STATISTICS_FIX`, `REGRESSION_SUITE`, `SOLUTION_BUILD`, `PRIMARY_REVIEWS`, `OWNER_RUN_TERMINAL`
**Next transition ID:** `GIT_PUBLICATION`

## Frozen scope and authority

Memory fix inside existing OsData Order Flow calibration, not a new module.
User explicitly authorized full SRU6 offline run, FORTS Main, threshold12,
PriceStep1, default57, no date restriction, tests/build/reviews and normal
fast-forward commit/push to this branch. Preserve default1024MiB limit,
Chain/Delta/Diagonal/Cloud algorithms and streaming/atomic artifacts.
Previous A–H iteration remains complete; do not reimplement it.

Changed scope: CalibrationDistribution.cs(new), CalibrationEngine.cs,
CalibrationMetrics.cs; CalibrationMemoryTests.cs(new), CalibrationTests.cs,
Program.cs; CLOUD_CALIBRATION_SPEC.md section16.1 and this checkpoint.
Do not stage unrelated tracked binaries or user untracked
DetachedTables/README.md and Verification/. No app/connector/trading launch.

## Implementation and diagnosis

Each metric previously retained both an AVL and sorted dictionary per distinct
decimal; now fixed4096-value buffers plus exact counted binary disk runs.
All14 buffers total at most57344 decimals (896KiB at defaults), no value trees.
Per-cell method/disposal closes scratch and releases buffers before next cell.
Only immutable summaries and date/time maps remain. Nearest-rank and histogram
semantics are unchanged. Scratch shares the artifact budget and rolls back.
The process-wide guard now reclaims collectible garbage under pressure before
enforcing the same live-memory cap; it does not ignore other live app objects.

Original isolated SRU6 runs also passed57/57 (workstation and ServerGC).
Actual host-state root cause remains NOT_DETERMINED; hosting code can retain
previous main results and run other jobs concurrently. This does not establish
which roots existed in the owner's failed session. Do not claim reproduction
of the original host failure or a measured427MiB SRU6 distribution heap.

## Verification status

- Exact current-code suite:198/198 PASS, exit0, session84084 collected;
  executable under temp OsEngine-calibration-memory-final-bin/.
- Canonical no-build suite from normal output:198/198 PASS, exit0,
  session7480 collected.
- Normal solution: dotnet build OsEngine.sln --no-restore -v:minimal:
  PASS0errors/1NU1900 vulnerability-feed warning. Earlier output-lock failure
  resolved after owner closed OsEngine; successful normal build log below.
- Git Bash agent validator109/109 PASS; diff whitespace PASS.
- Production memory_production PRIMARY CLEAN -> TERMINAL.
- Documentation calibration_documentation_review PRIMARY CLEAN -> TERMINAL.
  No findings/fix-validation cycle; do not repeat completed reviews.
- Frozen production SHA256 Engine:
  ADEFA3724D49EBE2FA4D1ABC33BDD2EB9FB2AB73B861A4E0DB65E1EF690EC85D.
  Distribution:A4946F3162A05268B598FC6B5A73824E0B82A1B4A3CDD52347451AD875888EE5.
  Later formatting changed only mixed line endings in spec/Program, no semantics.
- Four new regression groups:60000-observation sorted oracle/exact histogram;
  six40000-event high-cardinality cells, fixed buffers, unreachable collectors
  and no inter-cell live-memory accumulation; scratch cancel/budget cleanup;
  garbage reclamation and rejection of genuinely live over-budget memory.

## Full owner-file evidence

Input project/OsEngine/bin/Debug/Data/Set_SRTicks/SRU6/Tick/SRU6.txt:
158316586bytes,3130667rows,174source dates,134active FORTS Main dates.
SHA256:435ea400ffcc45cd3215be0806f660368a024d1c2942b8eed8aa8e3d2fed1f7b.
Final exact-code session45520 collected exit0, terminal PASS57/57.
ServerGC=true,22processors, default bounds; P95=12 confirmed before pin/grid.
Grid707.6323415s; full scenario765.274585s.
Whole-scenario peak managed535046368bytes (510.26MiB,20ms/progress sampling);
OS peak working set574607360bytes (547.99MiB).
Tuner21770 passed; context diagonal stack>=2 filter8266; chart2000markers;
saved rule/reopen and Anatomy tick/level conservation PASS.
Final manifest JsonElement.DeepEquals baseline PASS: all57cell summaries,
neighbors, provenance and all artifact checksums match exactly.
A first fixed run (before guard anti-repeat refinement) also passed57/57/fullflow.

Logs/artifacts survive under C:/Users/Mi/AppData/Local/Temp/:
- OsEngine-calibration-memory-final-owner.log and same-name output root;
- OsEngine-calibration-memory-normal-build.log;
- OsEngine-calibration-memory-suite-final.log and -normal-suite.log;
- baseline roots/logs OsEngine-calibration-memory-baseline-e3ec754 and
  OsEngine-calibration-server-baseline-e3ec754;
- first fixed run OsEngine-calibration-memory-fixed-owner-1.
Final bundle suffix:
cloud-calibration-9cdb89952a1f095d78fcf03ea9145834d32c2fb9608f325a7dfc2a8169d75b0d.
These are explicit owner offline outputs, not committed market data.

## Blockers

No implementation/review/verification blocker. Physical Windows/DPI/focus
remains OWNER-RUN; no physical UI or actual prior host-state reproduction claimed.

## Next action

Stage only eight scoped files, final validator/diff
checks, commit and normal push. Then terminal metadata checkpoint/push.
On restart do not repeat implementation, clean reviews or completed owner runs.
