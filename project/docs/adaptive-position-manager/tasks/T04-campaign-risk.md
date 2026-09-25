# T04. Жизненный цикл, ledger и независимый риск

Статус: NOT_STARTED. Зависимость: T03. Gate: QG04. Требования: R01–R08,R12,R20,R21.

## Цель

Добавить к математике кампанию с необратимым завершением и денежными ограничениями, не разрушающими повторные ADD/REDUCE. Спецификация: [01](../01-product.md), [05](../05-state-execution.md), денежный раздел [03](../03-mathematics.md).

## Работа

Preflight/Entering/Active/Closing/Completed, отдельный ExitLatch; фиксация A после initial fills; защита фактически исполненной части; AverageEntry/RealizedNet/полная equity; риск текущего и потенциального pending объёма; NoAddZone; окончательная цель и session timer.

Кампания принимает fills и брокерские состояния как факты. Рассчитываемые intent/target не меняют q. Запрет ADD не блокирует REDUCE в убыточной области. Profit credit по умолчанию не расширяет утверждённый риск. Ни одна адаптация не сдвигает HardStop и не снимает ExitLatch.

## Тесты

S01–S07,S14,S16,S21,S26. Частичный initial entry с пересечением stop до завершения заявки. Риск ровно на границе и сверх неё на один lot. Денежные тождества по signed fills и средней цене. Повторные циклы с комиссиями и исчерпанием бюджета. Полный target/stop/cutoff, затем возврат цены к входу: кампанию не открывать снова.

## Приёмка

Обратимость доказана в обеих ценовых зонах и для Short. RiskAllowedTarget не превышает разрешений. PendingCancel резерв не освобождён преждевременно. При истечении сессии без ticks срабатывает timer event. Completed не ставится при outstanding/unknown orders.

Артефакты: FSM/ledger/risk tests, таблица S expected/actual, денежная сверка, sequence для stop-vs-fill, обновлённые ADR при необходимости. Далее [T05](T05-execution.md).
