# AGENT-WORKFLOW-001: impact и handoff

**Load trigger:** semantic Markdown/C# XML-doc change, runtime behavior change,
новый существенный flow или terminal handoff нетривиальной задачи.

## 1. Documentation, XML docs и sequence

Оцени impact на:

- public/protected API и serialization/persistence contract;
- order/position/risk/session semantics;
- market-data ordering, timestamp, side, units и Tester/live parity;
- lifecycle, event ownership, threading, disposal и WPF dispatcher;
- security/failure semantics;
- qualification commands и test-evidence boundary;
- registered architecture/document IDs.

Затронутый current document и обязательный XML comment обновляются в том же
diff. Жанры, Tier и drift classification определяет только
`csharp-xml-doc-style`.

Independent documentation review требуется, если diff меняет хотя бы одно:

- accepted/current architecture claim;
- public/protected или safety-critical XML-doc contract;
- test-evidence/qualification claim;
- operator interpretation, runbook или существенный documented flow;
- утверждение о parity Tester/Optimizer/shadow/live.

Он не требуется для state/timestamp/index-only изменения, которое только
ссылается на уже проверенное exact evidence и не меняет смысл.

Новый или существенно изменённый runtime/review/recovery flow получает
компактный Mermaid source в соответствующем document/report. Не создавать
rendered image и не backfill-ить старые документы без отдельной задачи.

## 2. Observability impact

Каждый runtime behavior change получает один verdict:

- `OBSERVABILITY: REQUIRED`;
- `OBSERVABILITY: NO CHANGE`;
- `OBSERVABILITY: BLOCKED`.

Проверяются применимые logs, metrics, event/correlation identifiers,
error classification, order/retry/reconciliation visibility, data staleness,
health, resource/performance signals и operator action. Не добавлять telemetry
ради checklist, не вводить high-cardinality labels и не логировать secrets/raw
authenticated payload.

## 3. Tester/live parity impact

Для strategy, execution, data normalization, order simulation или risk change
вернуть один verdict:

- `MODE PARITY: REQUIRED` — нужно доказать одинаковые правила/явные различия;
- `MODE PARITY: NO CHANGE`;
- `MODE PARITY: BLOCKED` — необходимое historical/live evidence недоступно.

Backtest PASS не является profitability claim. В отчёте явно указать модель
комиссий, slippage/latency/liquidity, causal ordering и что именно не покрыто.

## 4. Terminal handoff

Верни только применимое:

- terminal outcome и реализованный результат;
- baseline/current HEAD и dirty boundary;
- изменённые components;
- verification commands/verdicts/totals без полного successful log;
- review rounds и закрытые in-scope Finding IDs;
- docs/XML docs/sequence, observability и mode-parity verdicts;
- новое evidence и его границу;
- blockers и `NOT_RUN`/owner-run evidence;
- commit/push status.

Для `REPOSITORY_WIDE` дополнительно вернуть Review ID/identity, coverage totals,
compact finding table, report/index paths и явную остановку до owner triage.
