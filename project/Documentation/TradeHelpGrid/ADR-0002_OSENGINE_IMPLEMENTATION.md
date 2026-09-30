# ADR-THG-002: нативная реализация Futures2Grid

**Статус:** CURRENT IMPLEMENTATION CONTRACT — OFFLINE CHECKS PASSED, LIVE NOT QUALIFIED.  
**Дата:** 2026-09-29. **Основание:** поручение владельца реализовать весь перенос
Futures2, управление параметрами во время работы и signed/zero цены до5 знаков.
**Baseline HEAD:** `088add98b728f8088fb18ff2e59c8d4113ad043c`; реализация находится
в dirty worktree. Commit/push не выполнялись.

Этот ADR конкретизирует первоначальный [ADR-THG-001](ADR-0001_RANGE_GRID.md).
Исторические source-таблицы EX/EB/EA/RT, HJ, helpers и callbacks сохраняются
как реконструкция TradeHelp4. Они не являются описанием исполняемого C#.
Числовая основа — [THG-PRICE-001](PRICE_DOMAIN.md); текущие отличия и границы
интеграции задаются здесь. Проверенное evidence — в
[THG-QUALIFICATION-001](QUALIFICATION.md), команды — в
[THG-OPERATOR-002](OSENGINE_OPERATOR.md).

## Решение и компоненты

Отдельный compact host `Futures2GridSimple`, его exact-step геометрия и
явная замена per-zone настроек через общий controller заданы в
[THG-SIMPLE-017](SIMPLE_GRID.md). Четырёхаргументный Configure полного робота
сохраняет прежний контракт; Simple использует отдельный opt-in overload.

`Futures2Grid` — обычный зарегистрированный `BotPanel` с двумя `BotTabSimple`.
Первый endpoint ведёт текущий контракт, второй используется при замене.
Оператор настраивает штатные подключения и параметры. Робот не создаёт сервер,
не читает внешнюю TradeHelp конфигурацию и не запускает дополнительные хосты.

| Компонент относительно `OsEngine/` | Ответственность |
|---|---|
| `Robots/MyBots/Futures2Grid/Futures2Grid.cs` | Native Parameters, команды, status window, event/timer lifecycle, координация |
| `OsTrader/Grids/Futures2/Futures2Plan.cs` | Проверка входов, immutable-by-convention версии плана, уровни, TP |
| `Futures2Controller.cs` в том же каталоге | Приоритеты решений, резервы, pause/liquidation/reconfiguration |
| `Futures2Book.cs` | Проекция идентифицированных native fills, lot allocation, dedup, pending reservations |
| `Futures2NativeAdapter.cs` | Сопоставление Journal/intent, durable-before-send, котировки, account reconciliation |
| `Futures2Hj.cs`, `Futures2Policy.cs` | HJ, trailing/mini/time/empty, блокировки, расписания |
| `Futures2Coordination.cs` | Detached peer snapshots и mailboxes; durable transfer/rollover DTO |
| `Futures2Inventory.cs`, `Futures2InventoryValidation.cs`, `Entity/Position.Inventory.cs` | Durable manual ownership, native projection и двухэтапное восстановление |
| `Futures2Store.cs`, `Futures2Commands.cs` | Atomic file replacement, hash/schema checks, detached plan transforms |
| `Entity/SignedPriceMath.cs`, `Position.SignedPrices.cs` | Exact tick math, opt-in signed native PnL/commission |
| `OsTrader/Panels/Tab/BotTabSimple.SignedOrders.cs` | Native PositionCreator/Journal/order gateway без hidden cancel/zero fallback |

`Futures2Book` не является вторым брокерским журналом: число позиции и заявки
создаёт штатный OsEngine. Native исполнения признаются через MyTrade; отдельно
доступны явные ownership operations и декларируемые внешние исполнения
по THG-INVENTORY-009.
LotId связывает заявку входа с allocation уровня, а не с ценой. PlanId
сохраняется при изменении котировки; новая геометрия получает новую версию.
Старые планы остаются доступны для закрытия, late callbacks и ручной настройки.

## Пакетные команды уровней

[THG-BATCH-011](BATCH_LEVELS.md) задаёт явный CSV selection, durable entry progress,
lot-snapshot exits, nullable selected edits и отмену целых затронутых intents.
Новые поля требуют schema4. Ordinary manual gates и общий edit drain сохраняются.

## Настройки уровней при изменении диапазона

[THG-RANGE-014](RANGE_STATE.md) сохраняет explicit markup/entry/exit controls по ID
при Shift/Widen с неизменным числом уровней. Если CountMode меняет число уровней
и есть overrides, трансформация отклоняется до staging; требуется явная перестройка
и проверка настроек. Прежняя geometry/count модель сохраняется, literal source
Widen/autoShift этим исправлением не реализованы.

## Выбираемые execution-политики

[THG-EXECUTION-OPTIONS-013](EXECUTION_OPTIONS.md) добавляет defaultfalse
AscendingLevelPriority/QuoteOrdinaryLimits. Они отделяют приоритет обхода от
сохранённых ID/количеств, а обычную reached limit-цену — от logical target.
Изменения проходят cancel-confirm barrier и требуют schema5. Остальные guards,
защитные/ручные/предварительные price paths и приоритет reductions сохраняются.

## Hedge направление

[THG-HEDGE-010](HEDGE_MODE.md) задаёт отдельный IsHedge: геометрия/триггеры/markup
остаются логическими, заявки, PnL и net используют физическую сторону.
Обычная логическая цель hedge может закрыть позицию с убытком. Hedge+preorders
отклоняется явно; runtime смена режима требует flat и cancel/reconcile barrier.
Hedge требует checkpoint schema3, включая pending и retained планы.

## План, деньги и цены

Все цены — decimal; точная арифметика ticks использует BigInteger fractions.
Ноль — цена, наличие котировки задаётся отдельными HasBid/HasAsk. Low<High,
2≤Count≤10000, tick положителен и представим до5 знаков. Off-tick границы
отклоняются. Средняя не округляется до расчёта конечного TP.

CountMode=Manual использует Count; Budget выбирает доступное число уровней
из положительного Collateral и FixedVolume либо минимального объёма; Step
выводит Count из RequestedStep. Затем все режимы используют единую inclusive
интерполяцию THG-PRICE-001. Автоматическое изменение числа не скрывает лимит10000.

Budget — бюджет построения, Capital — общий верхний предел held+fillable-entry
резерва, Collateral — положительные деньги за native единицу количества.
FixedVolume>0 требует финансирования всех уровней; иначе применяется точный
carry равных долей. При сопровождении остатка очередной вход ограничивается
оставшимся Capital и volume step. Размеры проверяются повторно на native
DecimalsVolume/VolumeStep/MinTradeAmount. Currency-notional minimum не
поддержан данным contract-unit gateway: профиль явно отклоняется.

Manual available funds — вручную выделенный общий денежный конверт: held и
pending уменьшают доступный остаток. Альтернатива ValueCurrent−ValueBlocked
включается только явно (для TRANSAQ отключена до доказанного reservation coverage): эти поля не имеют универсального смысла free margin
для всех коннекторов. Положительное broker margin выше Collateral приостанавливает
новые входы до перепроверки. Fees/stress оператор оставляет вне выделенного бюджета.

Уровневые TP: FromEnter от фактической средней, FromPlan от более строгой
из средней/плановой цены. WholePosition использует общую взвешенную среднюю
совместимых lots и общую WholeMarkup; ноль этой настройки выбирает исходную
наценку активного плана. Native close остаётся отдельным по native position,
поэтому MaxActions ограничивает скорость общего выхода. Направления и разные
контракты не смешиваются. Runtime смена режима проходит cancellation barrier.

Сырой PnL равен signed price difference × quantity × TickValue/Tick минус
FeePerUnit за каждую сторону. Intent фиксирует fee на момент постановки.
ReturnBase фиксируется при первом принятом положительном капитале и сохраняется
при переводах; цены и вход по нулю не участвуют в знаменателе. Native Journal
имеет собственную настройку CommissionType: для сопоставления результатов
оператор должен согласовать модели комиссий; стратегия не подменяет её молча.

## Состояния, порядок и изменение параметров

Draft → Configure → Ready → явная Reconcile + Start → Active.
Off/Pause дают PausingEntries → PausedEntries после подтверждённых исходов
входов; обычные выходы продолжают сопровождаться. Stopped/Ready сами не торгуют.
Reconfiguration хранит PendingPlan/Policy/Capital и ResumeAfterConfigure;
публикация возможна после terminal outcomes всех старых заявок и reconciliation.
Перед публикацией направление и IsHedge повторно проверяются по фактическим lots: поздний
fill противоположного направления отклоняет pending plan и оставляет входы paused.
Изменение только policy сохраняет геометрию и принятые transfer credits.
Pause во время ожидания снимает автоматическое возобновление.

Порядок одного прохода: явная quote presence/freshness и external stop;
cancel/Unknown barrier; pending edits/configuration; confirmed-flat processing;
emergency/voluntary reduction; session/throttle/helper; обычные выходы; входы.
Emergency latched, отменяет replacement и будущую часть transfer, игнорирует
обычные запреты выхода, HJ и пользовательские расписания. Но отсутствие свежей
котировки, native readiness или reconciliation не разрешает слепую отправку.
Остаток меньше допустимого native minimum остаётся owned и требует оператора.

Ликвидация сначала отменяет конфликтующие orders, затем закрывает подтверждённый
остаток bounded aggressive limits. Она не обещает исполнения лимита. Новый
порог retained collateral создаёт новую reduction generation; предыдущие
protective orders сначала отменяются. Собственная текущая защитная заявка не
отменяется на каждом проходе. FlatConfirmed требует одновременно отсутствия
lots/fillable orders и native/account agreement.

```mermaid
sequenceDiagram
    participant UI as Оператор
    participant Core as Контроллер
    participant Store as Checkpoint
    participant Native as Native Journal и connector
    UI->>Core: Применить параметры или close
    Core->>Store: Зафиксировать intent и allocation
    Core->>Native: Создать native position/order
    Native->>Store: beforeSend фиксирует оба native ID
    Native->>Native: OrderExecute
    Native-->>Core: Order / MyTrade в произвольном порядке
    Core->>Store: Идентифицированные fills и terminal knowledge
    Note over Core,Native: Cancel request не освобождает резерв; timeout даёт Unknown
```

## Фильтры и helpers

Предварительные входы/выходы, per-level skips, global/directional forbids,
две зоны блокировки выхода, одноразовый/постоянный блок входа, interval/from/to,
gap/file rules, дневной лимит, grouping и sequential liquidity представлены
отдельными policy fields. ThresholdTicks сдвигает условия входа и TP.
Grouping объединяет только совместимые entry allocations; transfer-funded
входы остаются отдельными. Close никогда не превышает незарезервированный held.

GapReferencesKnown явно разрешает две вручную заданные session reference prices;
signed gap=CurrentSessionOpen−PreviousSessionClose; gap-rule сравнивает его
модуль с неотрицательным inclusive интервалом. Отрицательные bounds отклоняются.
Например, close10/open8 даёт signed−2 и magnitude2; правило1…3 блокирует вход.
Отсутствующий gap блокирует заданное
gap-правило; legacy current−current=0 не копируется. File rule проверяет лишь
существование указанного файла, не читает его содержимое. При ошибке доступа
поведение File.Exists ограничено BCL; это не надёжный внешний risk service.
Sessions используют часы режима, inclusive границы и overnight intervals;
matching allow разрешает пересечение disallow. При наличии allow вне его
интервалов обычная торговля запрещена — явный календарь данного робота.

HJ сохраняет triangular depth, reversal hysteresis, границы и историю50;
строго внутренние цены HJ-зоны блокируются, границы разрешены. Entries/Exits
включаются раздельно. Reset HJ — явная команда.

Trailing сохраняет максимум/минимум return, from-zero/from-max, target step,
dynamic minimum, mini stop, time stop, empty stop и deferred recovery. Времена
mini/time отсчитываются от текущего самого раннего entry; empty — от создания
helper при отсутствии истории активности. Primary drawdown использует строгое
сравнение `>`; mini limited имеет минутное окно и одно срабатывание. Target=0
при dynamic не превращает stop step в ноль. Значения return вне ±100 принимаются
как новые факты вместо legacy stale-return. Empty stop отключает helper/входы,
по умолчанию сохраняя native robot. Opt-in `Policy.RemoveEmptyRobot` добавляет
удаление только never-active/never-coordinated live экземпляра через владельца;
повторные guards, durable stop и Tester/Optimizer retention —
[THG-EMPTY-012](EMPTY_REMOVAL.md). Recovery отменяет voluntary orders через barrier,
оставляет входы paused и не отменяет emergency.

## Перенос контракта и средств

Replacement хранит два отдельных endpoint identity. Prepared → Draining
(cancel-confirm) → Reducing (закрытие старого) → Entering (фактический набор
замены по уровням) → Applied. После drain фиксируются реальные quantities
каждого старого уровня. Новый план обязан вмещать их, совпадать по currency и
direction/IsHedge и проходить native metadata/funding checks. Новые входы не зависят
от обычного price trigger, но obey запреты входа, HJ, дневной бюджет и funds.
При запрете операция остаётся незавершённой и видна в status.

Replacement shift by spread переносит старые Low/High на средний bid/ask spread
между контрактами и квантует по новому tick. При выключении используются
ручные Replacement.Low/High. Наценка/ГО/tick value нового контракта заданы вручную.
После confirmed old flat переносится экономическая база:
old remaining cost − identified old close cost, с переводом через tick value.
План хранит шаблон ExitCarry только для replacement allocations. Каждый новый
replacement lot получает собственную поправку на единицу: она действует лишь
на его remaining quantity. Обычные последующие входы имеют нулевой carry.
Native execution averages/realized PnL
не переписываются. Новый TP учитывает эту поправку. Пустые старые уровни
не создают фиктивного replacement inventory. Cancel сохраняет фактически
исполненные old/new lots и отменяет remaining orders. Это последовательная
замена с периодом отсутствия позиции, а не атомарная биржевая spread-сделка.

Transfer между явно названными consenting instances использует одну валюту.
Source close fills с TransferId освобождают collateral; debit сохраняется до
доставки credit. Повторные cumulative credits принимаются один раз; delivery
retry не создаёт денег. Policy edit и emergency не стирают уже принятый кредит.
При pending configuration debit уменьшает также PendingCapital. Destination
перестраивает финансируемый план с budget sizing, набирает доступный перевод
без обычного price trigger и учитывает долю каждого transfer в partial fills.
При Canceled=false Completed означает получение полного credit и его подтверждённое расходование
во входах destination, а не обещание, что этот объём всё ещё удерживается.
Cancel прекращает новые source reductions, но доставляет уже debited сумму.
При Canceled=true Completed означает только settlement уже списанной суммы:
received≥Debited, без требования Requested или destination entry fills.
Это локальное перераспределение лимитов стратегии, не банковский перевод.

Group trailing использует свежий detached portfolio snapshot одной валюты.
EqualizeVolumes вычисляет общий retained collateral, PieVolume — минимальную
снимаемую денежную порцию. Leader хранит outbox до durable receipt каждого
получателя. Campaign+monotone sequence запрещает старой повторной close-команде
перезаписать более новую recovery. Реестр/mailboxes существуют только в одном
процессе; после restart источники повторяют persisted intents. Это не протокол
согласования между несколькими экземплярами приложения.

## Native data, recovery и lifecycle

`IExplicitQuoteSource` в AServer копирует присутствие сторон до legacy empty-book
guard; coalescing по инструменту сохраняет source capture time. Tester/Optimizer
публикуют свои candle/tick/depth quotes на event clock. Emulator получает
presence-aware quote только для текущего paper profile; stale/reset снимает sides.
Поддержка opt-in не объявляет все legacy candle/indicator/connector price paths
совместимыми с отрицательными ценами.

`IExplicitAccountSource` содержит capability HasExplicitAccountUpdates. AServer
передаёт отдельные immutable observations от realization и не конструирует их
из legacy PortfolioEvent, который может повторять весь cache. В данном diff
инструментированы actual Alor position/funds websocket callbacks: position-only
обновляет лишь указанную строку, funds-only — только деньги. Receipt фиксируется
при входе соответствующего callback; публикация cache другого счёта/инструмента
не освежает выбранный. Missing delta row означает no update. Другие realization
без этой явной capability заблокированы для live non-paper данного робота.
Дополнительный opt-in профиль штатного TRANSAQ описан в
[THG-TRANSAQ-IMPLEMENTATION-006](FINAM_TRANSAQ_IMPLEMENTATION.md): оригинальный
session/sequence до очередей, decimal depth, N/T/V и brokerref, восстановление
и ручной денежный конверт. Его qualification/review boundary указан отдельно.
Полный TRANSAQ snapshot с отсутствующей строкой переводит выбранный net в unknown,
а не zero; только явная owner-сверка может подтвердить отсутствующий ноль. Адаптер использует detached
net/funds, source capture age, последовательность callback и receipt после
последнего нового fill. Это консервативная локальная проверка, не exchange
sequence или атомарный snapshot. Первоначальный отсутствующий нулевой row
может быть принят только явной owner-verified Reconcile при свежем account frame.
Live non-paper server без подтверждённой source capability не допускается. Duplicate Connect
notification не меняет epoch; disconnect/reconnect сбрасывает readiness.

Checkpoint schema1/2/3/4/5: JSON envelope с SHA256 exact UTF8 payload; hash обнаруживает
повреждение, не является authentication. Flush(true), затем atomic File.Replace
с backup; повреждённый primary не заменяется молча старым backup. Load ошибки
блокируют создание адаптера. Save failure закрывает дальнейшие sends до restart.
Live checkpoint находится в `Engine/Futures2Grid/<SHA256(robot name)>.json`;
симуляции используют отдельное состояние в памяти и native reset events.

Recovered/reconnected robot начинает с Reconciling. Отсутствующая/Unknown
заявка не пересоздаётся. Done без деталей fill сохраняет резерв до MyTrade;
автоматический возврат допускается только для этого временного пробела в ранее
согласованной сессии. Остальные recovery требуют команды оператора. Manual
Register/SetQuantity/SetBasis реализованы отдельным ledger без фиктивных сделок;
ExternalExecution имеет самостоятельный экономический эффект. Контракт,
schema2 (либо3 для hedge)/native INV1 и ограничения recovery —
[THG-INVENTORY-009](INVENTORY_REGISTRATION.md).

Каждый завершённый Pump сохраняет изменившийся durable state, включая
quote-only HJ/trailing watermarks и timers; отсутствие заявок не пропускает
сохранение. Неизменённый payload повторно не переписывается. Это synchronous
flush-before-continuation, без заявления о максимальной latency под нагрузкой.

Все решения/команды/callbacks сериализованы одним adapter Sync. Native callbacks
могут быть синхронными при dispatch; intent/native IDs сохраняются раньше send,
а перед каждым следующим действием повторяются readiness/ownership checks.
DeletingEvent останавливает robot timer и отписывает callbacks до native tab
teardown. Удаление не считается ликвидацией: рабочие заявки нужно обработать
до удаления робота. Status window обновляется через WPF dispatcher, не торгует.

## Последствия и границы доказательства

OBSERVABILITY: REQUIRED — state/reason, plan/intent/native IDs, remaining fills,
HJ/helper, transfer/replacement status доступны в штатном log/status. Текущий
native/account gate показан отдельно от сохранённого результата Reconciled:
own/external/expected/observed/difference, age и zero provenance; фактический
held+pending collateral отделён от preview. Start и eligibility получателя
transfer используют текущее agreement. Общий счёт описан в
[THG-OWNERSHIP-008](SHARED_ACCOUNT_AND_REGISTRATION.md), ручная регистрация —
в [THG-INVENTORY-009](INVENTORY_REGISTRATION.md). EndpointIdentity является хешем;
native before-images дополнительно сохраняют исходные routing metadata счёта и
сервера. Checkpoint не содержит credentials/raw authenticated broker payload.

MODE PARITY: REQUIRED, частично проверено offline. Общий controller используется
в режимах, но live quotes/account receipt, native simulator fills и paper queues
различаются. Offline component fixtures не доказывают полный native replay,
WPF interaction, broker metadata/fees/market-order capabilities, historical
liquidity, real reconnect/restart или прибыльность. Для них нужен отдельный
owner-run сценарий на конкретном account/environment. Пока эти проверки не
выполнены, слово «production» обозначает архитектуру и код, а не разрешение live.
