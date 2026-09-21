# ORDER-FLOW-EXECUTION-001: модель исполнения и риск-контур

**Статус:** TARGET EXECUTION CONTRACT — NOT IMPLEMENTED.

**Назначение:** определить минимально честную модель исполнения Order Flow
стратегии в Tester и границы последующего переноса в shadow/paper/live.

## 1. Принцип

Signal, intent, activation, order, fill и position — разные события. Ни один
результат не может считаться прибыльным по цене сигнала без проверки, можно ли
было причинно исполнить нужный объём после latency и расходов.

Версия 1 поддерживает только taker/marketable-limit поведение. Пассивное
исполнение без order-level данных и доказуемой позиции в очереди исключено.

## 2. Вход модели

Execution controller получает только одобренный risk controller
`TradeIntent`/`ExitIntent`:

| Поле | Назначение |
|---|---|
| Intent ID и StrategySpec | Воспроизводимость решения |
| Instrument/direction | Что купить или продать |
| Signal/intent time | Начало причинной шкалы |
| Desired/max volume | Запрос и жёсткий предел объёма |
| Maximum acceptable price | Защита от неограниченного проскальзывания |
| Expiry | После какого момента неисполненный остаток отменяется |
| Risk reference | Уровень invalidation/stop и денежный предел |
| Execution profile | Latency, fees, adverse slippage и правила остатка |

Intent не является ордером и не создаёт позицию.

## 3. Причинная активация

1. Рассчитывается `activationTime = intentTime + configuredLatency`.
2. Стаканы из породившего signal bucket и все Quotes раньше activationTime
   запрещены для fill.
3. Первой возможностью является первый валидный причинно последующий snapshot,
   разрешённый политикой из
   [ORDER-FLOW-DATA-001](DATA_REPLAY_CONTRACT.md).
4. До fill повторно проверяются stale state, spread, session, price boundary и
   доступный риск.
5. Если подходящего snapshot до expiry нет, ордер получает terminal
   `Expired/Rejected`, а не фиктивное исполнение.

Исторический clock использует время replay, а не wall clock компьютера.

## 4. Проход по стакану

Marketable Buy потребляет asks от лучшей цены вверх, Sell — bids от лучшей цены
вниз. На каждом уровне учитываются:

- видимый доступный объём;
- `VolumeStep` и допустимое округление;
- maximum acceptable price;
- дополнительный adverse slippage из execution profile;
- комиссия на фактически исполненный объём.

Результатом может быть полный fill, partial fill либо отсутствие fill.
Позиция и её защита рассчитываются только по сумме `MyTrade`/simulated fills, а
не по первоначальному объёму intent.

Внутри одного snapshot ведётся ledger уже потреблённой моделью ликвидности.
Один и тот же отображённый объём нельзя повторно использовать для нескольких
fills. Неисполненный остаток ждёт новый причинно последующий Quote либо
отменяется по policy; сам факт следующего тика не восстанавливает ликвидность.

## 5. Жизненный цикл заявки

Минимальные состояния версии 1:

| Состояние | Смысл |
|---|---|
| `IntentReceived` | Решение принято, заявка ещё не активна |
| `RiskRejected` | Risk/data/session gate запретил отправку |
| `PendingActivation` | Идёт latency |
| `Active` | Достигнут первый разрешённый snapshot |
| `PartiallyFilled` | Исполнена часть, остаток контролируется отдельно |
| `Filled` | Весь разрешённый объём исполнен |
| `CancelPending` | Запрошена отмена, поздний fill ещё возможен в live |
| `Cancelled/Expired/Rejected` | Terminal без дальнейшего исполнения этой команды |
| `Reconciling` | Локальное состояние сверяется с broker/exchange state |

Каждый переход идемпотентен по `Intent ID + Order command ID`. Повтор события
не создаёт вторую заявку или дополнительную позицию.

## 6. Stop и выход

Stop/invalidation является порогом сформировать `ExitIntent`, а не гарантией
цены. После его срабатывания применяются та же latency, причинно последующий
стакан, доступная ликвидность, partial fills и расходы.

При partial opening защищается исполненный объём. Отмена остатка входа и выход
из уже набранной части являются разными командами. Поздний fill после cancel
обрабатывается reconciliation, а не игнорируется.

Перед session cutoff:

1. новые входы блокируются;
2. неисполненные остатки входов отменяются;
3. формируются exit intents на фактически открытый объём;
4. попытки выхода продолжаются независимо от поступления новых сделок;
5. итог сверяется с broker state в live.

## 7. Профили исполнения

Одна StrategySpec проверяется минимум на трёх заранее заданных профилях:

| Профиль | Назначение |
|---|---|
| Baseline | Обоснованная комиссия, latency и видимая ликвидность без бесплатных улучшений |
| Adverse | Увеличенные latency/slippage и более строгая доступность объёма |
| Severe but plausible | Стресс допустимого production диапазона для проверки хвостового риска |

Точные значения задаются отдельно по connector/instrument/session evidence и
версионируются. Выбирать профиль после просмотра PnL запрещено.

Отчёт показывает диапазон результатов, fill ratio, rejected/expired intents,
partial fills и чувствительность к каждому предположению, а не одну
«правильную» прибыль.

## 8. Что версия 1 не моделирует

- место пассивной заявки в очереди;
- гарантированный maker fill по касанию цены;
- скрытую ликвидность и точные add/cancel order-level события;
- сохранение видимого объёма между одинаковыми snapshots;
- бесплатное улучшение цены и rebate без подтверждённого тарифа;
- исполнение на том же событии, которое породило signal;
- универсальную поддержку server stop, IOC, OCO или reduce-only всеми
  коннекторами.

Если прибыль зависит от одного из этих предположений, версия стратегии получает
`no-go` либо отдельную задачу на данные и модель.

## 9. Risk controller

До создания order command проверяются:

- режим `Off/Research/Shadow/Paper/Live`;
- causal data health, stale/gap/disconnect и session state;
- отсутствие незавершённого opening/closing/reconciliation;
- размер из денежного риска и спецификации реального контракта;
- лимит убытка на сделку, день и совокупную открытую позицию;
- maximum position/order volume и доступный лимит;
- запрет overnight и достижимость принудительного выхода;
- соответствие локальной позиции broker/exchange state в live.

Risk controller может уменьшить объём либо отвергнуть intent, но не менять его
direction и не создавать противоположную идею.

## 10. Persistence и восстановление live

Перед `Live` должны сохраняться как минимум:

- текущая торговая дата и использованный дневной риск;
- активные Candidate/Intent/Order IDs;
- фактически исполненный объём и средняя цена;
- pending cancel/exit и последнее reconciliation state;
- StrategySpec/execution profile versions.

После старта или reconnect новые входы запрещены до запроса реальных позиций и
активных заявок. Расхождение переводит инструмент в `Blocked/Reconciling` и
требует явного разрешения, а не автоматического обнуления локального состояния.

## 11. Audit trail и observability

Для каждого intent/order/fill сохраняются correlation IDs и времена signal,
intent, activation, submission, broker acknowledgement, fill/cancel. Логируются
reason codes отказов, stale age, latency profile, использованные book snapshot
IDs, levels/volume, комиссия и slippage.

Не логируются credentials и raw authenticated payload. Высокочастотные события
не должны превращаться в неограниченный UI/log поток: детальный event journal
пишется структурированно, а operator log агрегирует состояние и ошибки.

## 12. Tester/live parity

Общими обязаны быть:

- StrategySpec и candidate lifecycle;
- normalizer/feature semantics;
- intent и risk правила;
- order state machine и reason codes.

Явно различаются:

- источник market data;
- историческая simulated latency против измеренной live latency;
- simulated fill против broker execution;
- доступные типы заявок и server-side protections.

Shadow записывает, что **было бы** отправлено и как это исполнила бы модель, но
не выдаёт simulated fill за broker fill. Paper/live qualification задана в
[ORDER-FLOW-QUALIFICATION-001](TESTING_AND_QUALIFICATION.md).
