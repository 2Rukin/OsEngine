# THG-HEDGE-010: логическая сетка и фактическая сторона

**Статус:** CURRENT IMPLEMENTATION — OFFLINE CHECKS PASSED, REVIEW CLEAN/CLEAN, LIVE NOT QUALIFIED.
**Дата:** 2026-09-29. **HEAD:** `088add98b728f8088fb18ff2e59c8d4113ad043c`, dirty worktree.
Основание: полный ручной перенос Futures2. Дополнение к
[ADR-THG-002](ADR-0002_OSENGINE_IMPLEMENTATION.md); проверка — [review010](HEDGE_REVIEW.md).

## Правило режима

`Grid.IsHedge` и `Replacement.IsHedge` — обычные boolean Parameters, default false.
Direction задаёт логическое направление сетки. IsHedge инвертирует только
фактическую позицию и стороны заявок. Принятый plan фиксирует оба значения;
последующие UI-изменения не переопределяют старые intents/lots.

| Direction | IsHedge | Вход | Выход | Логическая цель FromEnter |
|---|---|---|---|---|
| Long | false | Buy | Sell | average + markup |
| Short | false | Sell | Buy | average - markup |
| Long | true | Sell | Buy | average + markup |
| Short | true | Buy | Sell | average - markup |

Это исходный режим Hedge, а не обещание снижения риска. Например, логический
Long/Hedge продаёт по100 и при возврате до110 покупает для закрытия: результат
-10 на единицу до комиссий. Цель сетки здесь не является прибыльным TP.
Статус отдельно показывает accepted logical grid, hedge, actual inventory и
пояснение о возможном убыточном выходе.

Геометрия, порядок уровней, FromPlan/clamp, threshold-условия входа/выхода,
ForbidLong/Short, BlockEnter/Exit, правила и HJ сохраняют логическую ветку.
WholePosition объединяет только совпадающие instrument/endpoint/Direction/IsHedge;
это существующее расширение target, а не обнаруженный legacy whole-position oracle.
Фактическая сторона задаёт Buy/Sell, liquidating quote, MarginBuy/MarginSell,
знак реализованного/нереализованного результата и net при сверке счёта.
Перед созданием native entry проверяется актуальное MarginBuy/MarginSell
фактической стороны. Свежая сохранённая котировка с margin прежней стороны
после flat-переключения не разрешает превысить Collateral. Guard только для
увеличений: закрытие и аварийное уменьшение им не блокируются. TRANSAQ также
сохраняет дополнительную проверку на границе очереди транспорта.
Комиссия остаётся расходом. Количества и роли Entry/Exit остаются положительными.

Для достигнутого обычного hedge-сигнала limit строится по фактической котировке:
Buy = Ask + (ThresholdTicks + SlippageTicks) × tick,
Sell = Bid - (ThresholdTicks + SlippageTicks) × tick, с направленным округлением.
Логическая target/level не используется как цена перевёрнутого лимита.
Market использует ту же фактическую сторону. Ручной выход и emergency/reduction
сохраняют существующий bounded Aggressive по SlippageTicks; их источник стороны
тоже принадлежит плану. При IsHedge=false прежняя limit-политика сохраняется. Эти цены описывают
default; opt-in QuoteOrdinaryLimits заменяет только reached обычные limits на
physical quote±ThresholdTicks без SlippageTicks. Полная граница —
[THG-EXECUTION-OPTIONS-013](EXECUTION_OPTIONS.md).

## Предварительные заявки и отличие источника

В исходнике runtime setter PreSendOrders принудительно выключает их при IsHedge,
но initial setter этого guard не содержит. Единое target-правило: комбинация
IsHedge с PreEntries либо PreExits **отклоняется**, переключатели не меняются молча.
Перед включением hedge оператор выключает оба preorder-параметра. Это явное
ограничение относительно неоднозначного initial legacy path, а не заявление
полной parity этой ветки. Обычный режим сохраняет предварительные заявки.

Source anchors в hash-bound decompilation dti.cs:
5239/5106 sender inversion;3701/3738 physical quote;9293/9636 entry triggers;
12379–12397 exit trigger;12738 target;10607/10613 HJ;10650 forbids;
22448–22459 runtime preorder guard,21103–21116 initial setter.
SHA256 `523f17b7db8d9b3754f30375c4237ecc0d23c78af8739163da6930a26de6ed78`.
Исходные документы: [уровни](FUTURES2_LEVELS_AND_ORDERS.md),
[настройки](FUTURES2_RUNTIME_SETTINGS.md), [host](FUTURES2_HOST_AND_CALLBACKS.md).

## Изменение, восстановление и ручное владение

Смена Direction либо IsHedge требует отсутствия held inventory. Уже отправленные
заявки сначала проходят обычный cancel/reconcile barrier. Если поздний fill
создал позицию старого режима, pending изменение отклоняется, входы paused.
Rollover сохраняет оба параметра: переворачивать позицию заменой контракта нельзя.
Исторические plan остаются источником стороны для запоздалых native событий.

Любой retained/pending/replacement hedge-plan требует **schema3 или новее**. Новая версия
читает1/2/3/4/5; отсутствующее IsHedge означает false. Старый reader1/2 отвергает3.
После возврата к обычному режиму schema не понижается; manual Inventory009 также
сохраняет3. Это защищает от чтения новым режимом старого формата и от загрузки
hedge-кампании старым кодом, игнорирующим поле IsHedge.
Native INV1 формат не изменяется: его Position.Direction уже физический.

[Ручная регистрация](INVENTORY_REGISTRATION.md) создаёт физическую сторону плана.
ExternalIncrease/Decrease означают увеличение/уменьшение **позиции**, а не Buy/Sell:
для Long/Hedge внешняя покупка является уменьшением short. PnL и native ledger
считают её с фактическим знаком; обычный local SetQuantity не создаёт PnL.
Account gate сравнивает physical own net + declared ExternalNet с observation.
Другие роботы не присваиваются; расхождение требует явной сверки по
[THG-OWNERSHIP-008](SHARED_ACCOUNT_AND_REGISTRATION.md).

## Проверяемая граница

Четыре сочетания направления/режима, signed/zero цены и five-place tick проверяются
в managed offline harness через controller, native Journal/gateway и fake IServer.
Отдельно проверяются hedge registration/external-decrease/reload и schema guards.
Это не live qualification, не GUI acceptance и не полный Tester/Optimizer lifecycle.
Точные totals/verdict и выявленные ограничения фиксируются в review010.
