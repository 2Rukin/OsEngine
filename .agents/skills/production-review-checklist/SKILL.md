---
name: production-review-checklist
description: Внутренний domain checklist production-code-reviewer для торговой платформы OsEngine; не задаёт routing или самостоятельный output schema.
---

# Чек-лист production-review OsEngine

Helper загружается только из `production-code-review`. Routing, scope,
reachability и finding schema определяют workflow-файлы.

Применяй каждый релевантный раздел ко всей inspected execution boundary, а не
к одному найденному файлу. В full review каждая category получает terminal
coverage status.

## 0. Граница scope

Проверяется только реализованное поведение. Target/roadmap functionality не
является defect. Будущий integration risk допустим отдельной `ADJACENT`
finding, если current API уже создаёт доказанную несовместимость.

## 1. Market data correctness

- Сохранены ли instrument identity, price/volume units, lot, `PriceStep`,
  `VolumeStep`, side и timestamps?
- Явно ли различаются exchange/source/receive/local time и timezone?
- Нет ли duplicate, regressive, reordered или stale trade/depth/candle events?
- Empty/crossed/partial depth, snapshots vs deltas и reconnect reset обработаны?
- Tick side не угадывается и не подменяется направлением изменения цены без
  явного documented mode?

## 2. Causality и mode parity

- Tester/Optimizer не используют future candle, future depth или same-timestamp
  порядок, недоступный live strategy?
- Signal, order activation и fill происходят причинно после доступных данных?
- Одинаковы ли normalizer, strategy rules и risk policy в historical/shadow/
  paper/live либо различия явны и протестированы?
- Commission, slippage, latency, limited liquidity, partial fills, queue
  assumptions и session gaps не скрыты?
- Визуализация не меняет signal и execution logic?

## 3. Финансовая точность и instrument metadata

- Цена, объём, PnL, комиссия и money limits используют `decimal` либо
  доказанно безопасное representation?
- Rounding следует price/volume step и стороне операции?
- Lot, multiplier, `PriceStepCost`, currency и futures expiry учитываются?
- Serialization/conversion не теряет precision и culture не меняет parsing?

## 4. Order lifecycle и идемпотентность

- Validation выполняется до physical send; zero/invalid price/volume не уходят?
- Timeout/unknown result не приводит к слепому resend и duplicate order?
- Client/exchange IDs, state transitions, partial fills, cancel/replace races и
  terminal statuses согласованы?
- Reconnect выполняет reconciliation active orders/trades/positions до новых
  trading actions?
- Polling status не создаёт duplicate events?
- Retry bounded и различает idempotent/non-idempotent commands?

## 5. Position, portfolio и risk

- Internal position сверяется с broker portfolio и open orders?
- Stop/profit/forced close работают при partial fill, disconnect и restart?
- Daily loss, exposure, one-position/averaging policy и session cutoff
  применяются до order send?
- Kill switch fail-closed при stale data, lost connection или inconsistent
  state?
- Exit не зависит от UI/visual timeframe, если contract обещает обратное?

## 6. Concurrency, lifecycle и UI

- Shared mutable state защищён; callback order и reentrancy учтены?
- Event subscription имеет симметричную unsubscribe на dispose/reconnect?
- Background threads/tasks останавливаются, не остаются duplicate readers?
- WPF state изменяется через dispatcher; long work не блокирует UI thread?
- Locks не держатся вокруг network/UI callback и имеют стабильный order?
- Exception в event/background path логируется и не оставляет half-state?

## 7. Connector/protocol capabilities

- Реализация соответствует официальной specification, а permission flags —
  фактическим возможностям?
- Connect/Disconnect/Dispose/reconnect очищают sockets, queues, subscriptions,
  caches и identifiers?
- Rate limits, paging, retry/backoff, malformed response и auth expiry
  обработаны?
- Secrets не попадают в URL/log/exception; TLS/proxy/auth не ослаблены?
- Непроверяемое live behavior помечено `REQUIRES LIVE CONNECTOR`.

## 8. Persistence и backward compatibility

- Старые settings/journals/strategy parameters читаются без silent loss?
- Новая запись атомарна или имеет recovery/rollback?
- Culture, encoding, BOM, line endings и path semantics не ломают format?
- Versioning/migration не переписывают unknown fields или encrypted secrets?
- Restart восстанавливает достаточно state, чтобы избежать duplicate orders и
  неверных positions?

## 9. MCP, UI и security boundary

- Destructive MCP/UI operation требует явных params/auth и не расширяет remote
  доступ?
- Locked/encrypted state fail-closed; sensitive values masked?
- Input validation, JSON-RPC errors, file paths и process controls ограничены?
- UI action и MCP action используют один domain contract, а не расходящиеся
  bypass paths?
- Logs и responses не раскрывают API key/master password/connector secrets?

## 10. Observability и recovery

- Можно восстановить order/data/lifecycle sequence по timestamps и stable IDs?
- Unknown/partial/retry/reconcile/stale состояния видимы оператору?
- Ошибка классифицирована без raw secrets и без ложного success?
- Telemetry bounded: нет unbounded logs, high-cardinality labels или payload
  dump?
- Operator action и cleanup понятны из error/runbook?

## 11. Performance и backpressure

- Tick/depth queues bounded или имеют осознанную overload policy?
- Hot path не делает линейный search, sync disk/network/UI work на каждый tick?
- History/depth memory bounded; event handlers не накапливаются?
- Rate gates и batch/paging не создают starvation или unbounded latency?
- Optimization не меняет semantics без regression evidence?

## 12. Качество tests/evidence

- Assert проверяет production logic, а не только заранее настроенный mock?
- Тест упадёт при реальном regression; есть negative/failure/cleanup cases?
- Deterministic offline test отделён от MCP stand/live connector/real orders?
- Timing-dependent tests имеют bounded waits и диагностический failure?
- Test comments не обещают больше, чем запускается на самом деле?
- `SKIPPED`, no market data или missing token не считаются PASS?

Test finding начинает problem с `[ТЕСТЫ]`.

## Domain additions к finding contract

- Всегда называй affected modes/components.
- Technical severity, business impact и reachability оценивай независимо.
- `BUSINESS_CRITICAL` требует конкретного observable сценария uncontrolled
  trade, financial loss, safety-gate bypass или сопоставимого impact.
- `NOT_PROVEN`/partial/live-dependent получает `REQUIRES_EVIDENCE`;
  `UNREACHABLE` — `DO_NOT_FIX`.
- Не выноси verdict «всё хорошо/плохо» без entry point, execution path,
  evidence и strongest counterevidence.
