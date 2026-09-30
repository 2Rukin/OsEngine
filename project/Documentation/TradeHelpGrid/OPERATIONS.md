# THG-OPERATIONS-001: эксплуатационный контракт

> Историческая спецификация и evidence baseline этапа исследования. Текущий
> implementation contract: [ADR-THG-002](ADR-0002_OSENGINE_IMPLEMENTATION.md);
> native команды: [THG-OPERATOR-002](OSENGINE_OPERATOR.md). Указания «будущий»/
> «код не менялся» ниже относятся к исходному исследованию, не к текущему diff.

**Статус:** PROPOSED RUNBOOK — MODULE NOT IMPLEMENTED / LIVE NOT QUALIFIED.

Это процедуры для будущего модуля из ADR-THG-001. Они не являются инструкцией
запустить найденный TradeHelp.exe и не описывают существующие кнопки OsEngine.
Фактические legacy options находятся в THG-MATH-001.

Числа вводятся и отображаются по [THG-PRICE-001](PRICE_DOMAIN.md): знак и
пятый разряд сохраняются, цена0 отличается от пустого поля и выключенного
stop. Preview показывает tick, фактические шаги, положительное обеспечение
на лот и отдельную базу процентных расстояний. Проверять поддержку signed/zero
пути инструмента и connector; отсутствие такой поддержки запрещает активацию,
но не скрывает существующую позицию. Отрицательная цена не исправляется на abs.

## Подготовка и активация

До активации оператор выбирает инструмент, connector/account reference,
режим Long/Short и профиль расчёта. Preview показывает active range, отдельные
границы ликвидации, число уровней и полную таблицу: цена, объём, стоимость,
цель выхода, расстояние до stop. Подписи единиц обязательны. Для legacy import
показываются внутренний/видимый диапазоны и изменения округления.

Система проверяет metadata/steps, budget и exposure limits, свежесть данных,
readiness соединения и режима, торговую сессию, собственные незавершённые
заявки, native/broker position agreement и ownership инструмента. Ошибки
плана не исправляются молча. Сохранение плана не активирует торговлю.

После команды активации создаются campaign ID и plan version. Оператор видит
состояние Active, текущий h/e/x и источник protective behavior: локальный
stop либо подтверждённая broker capability. Нельзя показывать «защищено» по
одному отправленному stop intent.

## Команды и отображение результата

| Команда | Намерение | Когда операция завершена |
|---|---|---|
| PauseEntries | Прекратить набор, отменить входы, сохранить сопровождение | Все собственные entry outcomes известны; пауза не обещает flat |
| Resume | Возобновить существующий валидный план | Readiness + reconciliation пройдены; stop latch отсутствует |
| CancelWorking | Отозвать выбранные собственные ордера | Подтверждены terminal outcomes; позиция может остаться |
| Reduce | Добровольно сократить до заданного остатка | Остаток и все влияющие intents согласованы |
| Flatten | Аварийно закрыть кампанию до нуля | FlatConfirmed по THG-EXECUTION-001 |
| StopWhenFlat | Не входить повторно после выхода | Ноль и нет потенциальных исполнений |
| Reconfigure | Рассчитать новую версию плана | Preview принят, старые intents урегулированы, mapping сохранён |

Каждая команда получает correlation ID и видимые состояния requested,
in progress, completed либо blocked с причиной. Надпись «заявка отправлена»
не заменяет подтверждение заявки или исполнения. Закрытие окна не отменяет
выполняющуюся ликвидацию и не освобождает risk ownership.

Emergency close не зависит от обычных HJ/entry/exit-block filters или
FreezeVolume. При отключении связи UI показывает «ликвидация запрошена,
исполнение не подтверждено», остаток и список Unknown, а не «остановлено».

## Аварийные ситуации

| Событие | Автоматическая реакция target | Действия оператора |
|---|---|---|
| Stale/пустая/некорректная котировка | Запрет входов; состояние данных отдельно от позиции | Проверить источник и время, восстановить данные; не нажимать повторный Start |
| Disconnect в Active | Reconciling, no new entries; исполнение cancel не выдумывается | Проверить связь и состояние у брокера; resume после сверки |
| Disconnect в Liquidating | Stop latch сохраняется, alarm по остатку | Использовать согласованную брокерскую аварийную процедуру; затем сверка |
| Unknown submit | Удержать резерв, query по identity, без слепого retry | Найти заявку/исполнения; при неясности удерживать campaign blocked |
| Fill во время cancel | Обновить held и оставшийся pending, продолжить risk flow | Не считать отмену гарантией отсутствия сделки |
| Exit rejected | Классифицировать, обновить x только при определённом исходе | Проверить сессию, price band, short/close capability и доступность позиции |
| Расхождение broker/native | Запрет increases, visible discrepancy | Сверить scope и чужие сделки; не обнулять local position вручную |
| Повреждён checkpoint | Faulted/Reconciling, без автосоздания новой сетки | Использовать последнюю проверенную версию и broker evidence |
| Переполнение event queue | No new entries, alarm, сверка неполных событий | Устранить задержку; завершить replay/reconciliation |
| Stop есть, а fills не приходят | Liquidating остаётся активным; timeout alarm | Проверить очередь/ликвидность/тип заявки; явное решение по исполнению |
| Dust/minimum volume | Явный остаток, blocked automatic close | Подтвердить допустимый ручной способ; не приравнивать detach к flat |
| Signed/zero цена не проходит native путь или broker price-band | Запрет increases, visible reason; при остатке сохранить risk flow и использовать только проверенный способ close | Проверить domain/capability и состояние позиции; не заменять цену на epsilon/abs и не считать local reset закрытием |

Manual action записывается как отдельное внешнее событие с причиной и
последующей сверкой. Ни rollback кода, ни удаление grid не отменяют сделки.
Нельзя запустить второй экземпляр на том же ownership scope ради «повторной
попытки» без разрешения неоднозначностей первого.

## Наблюдаемость

Target событие содержит receive sequence, source/receive timestamp, mode,
campaign/plan/level/intent IDs, native user/market order identities, action,
предыдущее/новое состояние, quantities h/e/x, error class и причинный event ID.
Внешний timestamp хранится отдельно и не объявляется временем получения.
Идентификаторы счетов маскируются; credentials и raw authenticated payload
не пишутся в лог. Full broker request/response не нужен для диагностики.

Обязательные события: принятие плана, start/pause/resume/stop, trigger внешней
границы, intent persist/submit/outcome, cancel request/outcome, fill/dedup,
unmatched fill, exposure breach, disconnect/reconnect, reconciliation verdict,
FlatConfirmed, изменение metadata, смена плана и ошибка persistence.

Метрики без unbounded ID labels: число Unknown и unmatched, глубина/возраст
очереди, возраст последней котировки, время submit→ack/cancel→terminal,
время в Liquidating, остаток позиции, ожидаемые entry/exit, ошибки отправки,
длительность сверки и число расхождений. IDs допустимы в bounded logs/traces,
не как бесконечные metric labels.

Пороги FreshnessLimit, AckDeadline, CancelDeadline, ReconcileDeadline,
MaxLiquidationAge, MaxQueueAge/Depth, request rate и emergency slippage
задаются deployment profile для конкретной среды. Их значения пока
NOT_DETERMINED; запуск live без выбранного профиля не квалифицирован.
Производительность будущего модуля не измерялась.

## Публикация и rollback будущей реализации

Последовательность: deterministic component tests → native synthetic
Tester/Optimizer evidence → historical research с costs → отдельное
owner-approved connector/GUI qualification → ограниченный ввод по решению
владельца. Ни один этап этой последовательности не выполнен данной задачей,
кроме документальных/static checks, перечисленных в THG-QUALIFICATION-001.

Перед обновлением: запрет новых входов, урегулировать working/unknown orders,
зафиксировать snapshot и exact code/config/plan versions. Для rollback при
ненулевой позиции сначала определить, кто продолжает её сопровождение.
Возврат старой схемы checkpoint без проверенной миграции запрещён контрактом.
После обновления/rollback — Reconciling, отдельное разрешение активации.

## OWNER-RUN сценарий будущей connector-приёмки

Пока не выбраны connector, account/environment и реализация, точной команды
запуска нет. Нельзя заменять это выдуманным CLI.

После отдельного разрешения владельца: выделить тестовый инструмент и
допустимый минимальный объём; проверить отсутствие чужих заявок; поставить
один entry, получить partial fill, запросить cancel остатка, вызвать stop,
проверить остаточный close и брокерский flat. Затем повторить с потерей связи
между submit/ack и с restart после stop latch. Сохранить обезличенные IDs,
timestamps и terminal summaries.

Ожидается отсутствие повторного увеличения, корректные h/e/x и отсутствие
working/unknown orders после FlatConfirmed. Cleanup — подтверждённая отмена
всех собственных ordinary/stop orders, сверка позиции и остановка кампании.
Если допустимость real/paper orders не определена, сценарий NOT_RUN;
автоматическое действие из этого текста не разрешается.
