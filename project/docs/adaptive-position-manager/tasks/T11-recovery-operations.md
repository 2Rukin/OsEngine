# T11. Recovery, watchdog и эксплуатационная устойчивость

Статус: NOT_STARTED. Зависимости: T05,T07. Gate: QG11. Требования: R05,R08,R19,R20.

## Цель

Сохранить смысл одной кампании при crash, disconnect, потерянном ack и отсутствии тиков. Runbook: [12](../12-operations.md); invariant rules: [05](../05-state-execution.md).

## Работа

Версионированные checkpoints и intents до send; восстановление ownership/positions/orders/fills; сохранение ExitLatch; блокировка набора до сверки и warmup. Watchdog отделён от event-time стратегии. Session timer работает независимо от появления новой свечи или тика.

Обработать отказ Market/price limits/закрытие торгов, не маскировать остаток Completed. Оператор получает конкретный статус, pending/unknown counts и доступные действия. Логи не содержат credentials. Деградация полной диагностики не теряет критический execution audit.

## Тесты

S17–S21,S24–S26. Crash-инъекции на каждой границе durable intent/send/ack/fill/checkpoint; восстановление после частичного initial entry и после terminal signal. Проверить противоположные порядки событий и ручное вмешательство с чужой позицией. Отдельно отсутствие data ticks при cutoff.

Нагрузка по [QG11](../10-quality-gates.md): зарегистрированная машина, ≥2× измеренного peak stream, ноль потерь, ограниченные buffers, стабильная очередь, p50/p95/p99, UI отдельно. Повторный recovery с тем же брокерским snapshot не создаёт новые заявки.

## Приёмка

Невозможно получить ложный flat только по отправке команды. Неизвестная заявка не дублируется. ExitLatch не снимается восстановлением. Возможности реального коннектора и местоположение защиты документированы. Shadow/paper запускаются только с явным разрешением пользователя; их отсутствие — NOT_RUN, не предполагаемый успех.

Артефакты: fault report, recovery manifests, load report, операторские screenshots, заполненный runbook и known limitations. Далее [T12](T12-release.md).
