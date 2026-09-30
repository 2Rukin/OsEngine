# THG-OWNERSHIP-REVIEW-008: account diagnostics и source registration design

**Статус:** TERMINAL CLEAN/CLEAN — DIAGNOSTICS CHECKPOINT ONLY. **Дата:** 2026-09-29.
**Baseline/current HEAD:** `088add98b728f8088fb18ff2e59c8d4113ad043c`.
**Scope:** текущая наблюдаемость account gate/reserves/provenance, truthful
start/peer readiness; отдельно read-only source-grounded target регистрации.
Регистрация и автоматическое совместное владение ещё не реализованы.

Entry manifest ownership-entry.json SHA256
`04a479cbf5c42dbfe99f97b313e37379683f25ccbee3387381c93eb4aadf71c8`;
81 prior task files сохранены в TEMP/TradeHelp4-analysis/ownership-entry-sources.
Dirty work до entry не переопределяется как новые изменения008. Source/current
baseline описан в [THG-OWNERSHIP-008](SHARED_ACCOUNT_AND_REGISTRATION.md).

Runtime diff: Futures2NativeAdapter + Futures2Grid. Tests: ExternalOwnershipCases,
Program registry и доступ к существующему synthetic Robot fixture в AdapterCases.
Native Position/Journal persistence в008 не менялись.

## Verification

- `dotnet build OsEngine.sln --no-restore -v:q`: production rebuild PASS0errors17warnings;
  после исправления ключа synthetic fill в тесте incremental PASS0errors1NU1900;
  final production FIX rebuild PASS0errors17warnings.
- `dotnet run --project Tests/TradeHelpGrid/OsEngine.TradeHelpGrid.Tests.csproj --no-build --no-restore`:
  PRIMARY379/379, final FIX384/384 assertions,0failures. Первый test run выявил некорректный fixture dedup key;
  исправлен fixture, production accounting ради него не менялся.
- Final agent validator109PASS,34links0missing, whitespacePASS. Runtime/test/project
  hashes remain equal to the final reviewed manifest. Eleven own generated tracked
  build outputs restored only after entry-status proof; vendor/user files retained.
- Broker/native GUI/stands/physical restart: NOT_RUN. Credentials не читались.

## Review outcome

Independent production and documentation PRIMARY each found the same incomplete
runtime RegimeOn/pending-resume path. Findings THG-OWN-SAF-001 (lifecycle) and
THG-OWNERSHIP-DOC-001 (CODE_DRIFT): MEDIUM/BUSINESS_LOW/IN_SCOPE, PROVEN and
reachable when old Reconciled=true but current account equality=false.
SettingsChanged used the old flag, accepted Start(true) or armed pending resume;
physical sends remained blocked (strongest counterevidence). MUST_FIX,
APPROVED_FOR_FIX under current implementation scope. FIX uses current agreement
and clears pending automatic resume when that gate fails, including a policy edit
inheriting prior Active state. Five additional assertions cover both UI entry
paths, pending config and absence of new intents. The first extended fixture
caught a missing inherited-resume clearing branch; the final FIX includes it.

```mermaid
sequenceDiagram
    participant U as Оператор
    participant S as SettingsChanged
    participant A as Native account gate
    participant C as Controller
    U->>S: RegimeOn или policy edit
    S->>A: Current agreement
    A-->>S: false при прежнем Reconciled=true
    S->>C: Не начинать; снять pending resume
    Note over A,C: Order dispatch guards также остаются действующими
```

PRIMARY manifest ownership-primary.json SHA256
`ac149bf07e769c3872690ab8c97c0af7389cf721bc22c7f59a0f0a0043e17ee3`;
83/83 hashes verified independently by both roles. VALIDATION_1 CLEAN/CLEAN;
THG-OWN-SAF-001 and THG-OWNERSHIP-DOC-001 CLOSED. Final manifest
ownership-validation1.json SHA256
`e4e984e6c0759dfd85a8f7f5b58c933de310a1856dcb0b5ae9870596166afe8d`;
83/83 hashes independently confirmed by both roles. Terminal status/index edits
follow that frozen semantic/runtime checkpoint. Original full transfer and
external inventory registration are not declared complete.
Old terminal TRANSAQ006/completeness007 reviews не переоткрываются.
Commit/push не выполнялись. Большой исходный scope переноса остаётся незавершённым.
