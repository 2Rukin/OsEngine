# APM-OPERATIONS-IMPLEMENTATION-001 — текущая эксплуатационная граница

Статус: CURRENT IMPLEMENTATION DESIGN / ResearchOnly. Основания:
[T11](tasks/T11-recovery-operations.md), [execution](05-state-execution.md),
[quality gates](10-quality-gates.md). Это граница текущего кода, не live qualification.

Native Tester/Optimizer adapter создаёт кампанию с `ApmOperationalLimits(256,257)`.
Это конечный бюджет ordinary intents и отдельный запас protective EXIT. После256
обычных intents первый market/timer event ставит необратимый RESOURCE_LIMIT и
закрывает собственный остаток. EXIT не расходует обычный бюджет. После исчерпания
отдельного exit budget открытый риск остаётся FaultedClosing/QUERY; повторная
сверка не обнуляет счётчики. Cancel/reject не возвращают потраченный бюджет.

Все terminal identities и actual fills сохраняются; FIFO-удаления нет. Для
квалифицированной native модели «один полный fill на intent» retained intents и
fills ограничены суммой256+257. Native order mappings ограничены теми же intents;
буфер reentrant callbacks4096, diagnostic history2000, speed history определяется
фиксированным policy window. Кампания с native budget ограничивает длину входных
campaign/signal/instrument/account identifiers128символами.

Эта граница не переносится на произвольные частичные исполнения. Generic offline
core допускает `nativeLimits=null` для partial-fill fault tests без заявления о
finite full-ledger memory. Если native budget получает partial fill, он учитывает
фактический объём и требует reconciliation с CAPABILITY_VIOLATION; такой stream
не квалифицирован данным доказательством. Live adapter отсутствует и отвергается.

Checkpoint сохраняет limits вместе с полной ledger; recovery проверяет её и
потраченные бюджеты. `Recover(string)` перед parsing ограничивает payload
16 777 216 единицами UTF-16 (`string.Length`), не байтами. Envelope ограничен
32MiB размера файла. Это входные ограничения, не предел всей памяти при recovery.
Это additive поле схемыv1: старый generic fixture без limits читается без native
memory claim. Downgrade активной кампании в версию, игнорирующую limits, не
квалифицирован. Нельзя удалять checkpoint/journal для восстановления «с нуля».
Дисковый append-only journal не ограничен quota; при ошибке записи transport
заморожен. Ограниченная память не означает бесконечно доступный диск.

`ApmReplayWatchdog` использует монотонные wall-clock ticks только для диагностики
прогресса callbacks; пауза Tester не изменяет торговые решения. UI показывает
«пауза/задержка» после10секунд без Process. Свежесть цены и SessionExit по-прежнему
используют event time и native replay heartbeat, включая пустые интервалы.
Watchdog не подменяет reconnect или внешнюю брокерскую защиту.

`OperationsTests` проверяет малый ordinary/exit budget, zero-fill cancellation,
поздний полный fill, duplicate после recovery, сохранение ExitLatch и отсутствие
повторного входа. Более ранние fault suites проверяют partial entry, terminal-before-
fill, unknown send/cancel, ledger corruption и сохранение intent перед transport.
Это component evidence. Native adapter требует flat dedicated tab и не умеет
принимать открытую позицию после перезапуска; live/native-session recovery остаётся
BLOCKED_CAPABILITY. Новый native replay — новая симуляция, не broker recovery.

`--load NEW_DIRECTORY` создаёт новый синтетический burst file и измеряет actual
input peak. Через bounded test queue512 выполняются реальные features/controller/
durable audit с явно условными полными fills. Записываются CPU/runtime/OS/build/
dataset hashes, processed counts, p50/p95/p99 без первых2000events, retained state,
очередь, managed memory после GC и diagnostics-off/on parity проекции отправок
{intent.Id,Action,Volume,Quantity}. Это не полный canonical decision/fill trace;
конечный прирост managed memory не доказывает асимптотический plateau. Такой тест
не измеряет native-server/UI pipeline либо peak выбранных реальных данных.
QG11 целиком не становится PASS только по нему; shadow/paper/live NOT_RUN и требуют
отдельного конкретного разрешения. Фактические результаты — в
[work record](evidence/implementation.md), без обещания fixed throughput на другом ПК.
