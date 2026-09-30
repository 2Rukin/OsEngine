# THG-INTEGRATION-001: граница интеграции с OsEngine

> Историческая спецификация и evidence baseline этапа исследования. Текущий
> implementation contract: [ADR-THG-002](ADR-0002_OSENGINE_IMPLEMENTATION.md);
> native команды: [THG-OPERATOR-002](OSENGINE_OPERATOR.md). Указания «будущий»/
> «код не менялся» ниже относятся к исходному исследованию, не к текущему diff.

**Статус:** CURRENT CHECKOUT RESEARCH + PROPOSED MAPPING  
**Baseline:** `088add98b728f8088fb18ff2e59c8d4113ad043c`.

Исследован current fork `2Rukin/OsEngine`. Внешний upstream не использовался.
Ни один найденный TradeHelp класс не является готовой реализацией в OsEngine.

Требование владельца о signed/zero ценах до5 знаков задаёт
[THG-PRICE-001](PRICE_DOMAIN.md). Decimal storage и локальное прохождение
отрицательной цены не равны сквозной поддержке исполнения. Следующая таблица
дополняет baseline узким исследованием именно этого требования; production
код не менялся, connector-specific допустимость не устанавливалась.

## Current native price domain: установленная граница

Anchors ниже относятся к тому же HEAD; пути начинаются с `OsEngine/`.
Краткое имя BotTabSimple.cs означает `OsTrader/Panels/Tab/BotTabSimple.cs`,
Internal/PositionCreator.cs — файл в `OsTrader/Panels/Tab/Internal/`;
ConnectorCandles.cs — `Market/Connectors/ConnectorCandles.cs`.

| Путь | Факт current code | Следствие для target |
|---|---|---|
| Entity/Order.cs:70,75,91,223,395 | Price/StopPrice decimal, Market — отдельный enum; PriceReal0 также означает отсутствие execution evidence | Типы вмещают signed числа; presence нельзя вывести только из цены |
| Entity/Security.cs:68,157 | PriceStep decimal и Decimals; при шаге0.00001/Decimals5 пять знаков представимы, лимита5 у модели нет | Target задаёт свой максимум5 и проверяет tick, не только формат |
| OsTrader/Panels/Tab/BotTabSimple.cs:6735 RoundPrice | Math.Round по Decimals, затем уменьшение до кратности tick; side не используется; при ошибке0, при PriceStep0 исходная цена | Нельзя принять метод за target Down/Up по назначению; заранее валидный tick и отдельный error outcome обязательны |
| BotTabSimple.cs:6086,6232,6135,6281 | Limit create/add отвергает price==0 до округления; отрицательная проходит этот guard, повторной проверки0 после округления нет | Limit(0) не поддержан единообразно; отсутствие negative guard не подтверждает broker acceptance |
| BotTabSimple.cs:4415,4463,6355,6402,6508; Internal/PositionCreator.cs:93 | Limit close проверяет volume и округляет цену, собственного zero/negative guard в этом пути нет | Entry/exit отличаются; квалификация обеих сторон и partial close необходима |
| BotTabSimple.cs:1807,2995,2548,3753 | Market entry/add требует ненулевой quote нужной стороны; live смещает на40ticks, при unsupported Market переходит к Limit | Нулевая quote и смещение в0 могут прервать путь; существующий fallback не доказан для нового domain |
| BotTabSimple.cs:4333,4344,4353,4364,4371 | CloseAtMarket выключает local stop/profit ДО guard BestAsk!=0 для обеих сторон; затем live long использует Bid−40ticks, short Ask+40ticks | Возврат при Ask0 может оставить позицию без прежних local protection flags; вызов не означает close |
| BotTabSimple.cs:6552,6642,6976,4733,4981 | Local stop/profit сохраняют signed цены; CheckStop сравнивает <=/>=; Tester/Optimizer подставляют activation в order price | Установка не доказывает исполнение, signed/zero trigger и mode differences требуют отдельных сценариев |
| Entity/Position.cs:233,238; BotTabSimple.cs:4868,4906 | Long trailing сравнивает исходный RedLine0 с новым отрицательным activation без initialized guard и может только включить старый stop | Первый отрицательный trailing нуждается в исправленном native/strategy design; не считать его установленным |
| BotTabSimple.cs:5523,5708,7450,7492 | New-position server-stop helper отвергает priceLimit0; local opener попадает в обычный create zero guard | Stop-entry0 — отдельная ограниченная ветка |
| BotTabSimple.cs:5979; Market/Connectors/ConnectorCandles.cs:2067; Market/Servers/AServer.cs:4100,3902 | Reprice только OsTrader с readiness/capability; raw newPrice без tab-rounding | Цену проверять до native вызова; Tester reprice parity отсутствует |
| BotTabSimple.cs:5969,5165; ConnectorCandles.cs:2018 | Cancel не имеет своего price guard; CloseAllOrderToPosition отключает local protection и перебирает Active | Signed цена не меняет cancel acknowledgment contract; protection lifecycle проверяется отдельно |
| BotTabSimple.cs:1339,1354; ConnectorCandles.cs:1475,1880,1892 | BestBid/Ask0 используется и при отсутствии данных; decimal quotes не имеют общего sign guard | Domain presence/status нельзя восстановить из числа0 без дополнительного evidence |
| Market/Servers/AServer.cs:3465,3486,3591,3600 | Trade.Price0 отбрасывается; отсутствующая depth сторона становится0, update с обеими0 не отправляется | Нулевые события теряются ещё до робота; одного изменения его валидатора недостаточно |
| Market/Servers/Tester/TesterServer.cs:1276,2012,2069,2181 | Admission без price>0; candle loop заменяет order.Price0 на candle.Open независимо от типа; repricing пуст | Limit(0) меняет смысл, арифметический тест не доказывает Tester execution |
| Market/Servers/Optimizer/OptimizerServer.cs:663,1361,1501 | Аналогичная zero→Open подстановка; admission без sign guard; repricing пуст | Optimizer также требует отдельной квалификации |
| Market/Connectors/OrderExecutionEmulator.cs:245,251,273,295,322 | Market при quote0 использует order.Price; Limit требует ненулевую противоположную quote | Emulator не даёт доказательства zero-price parity |

**Классификация:** signed decimal storage — implemented; рассмотренные
negative-price пути — локально допускают, zero-price semantics — partial и
различаются по маршрутам. End-to-end signed/zero trading — NOT_QUALIFIED.
Нужна последующая узкая реализация корректных price/presence/rounding paths
в рамках native lifecycle. Запрещены обход connector, abs/epsilon цена и
новый независимый execution owner ради маскировки ограничений. Таблица
описывает bounded paths, не полный аудит всех коннекторов/графиков/данных.

## Проверенные native компоненты

Пути в anchors относительны `project/`. Номера строк относятся к baseline.

| Задача | Current implementation | Граница |
|---|---|---|
| Робот, инструмент и lifecycle | [BotTabSimple.cs](../../OsEngine/OsTrader/Panels/Tab/BotTabSimple.cs), конструктор:109 | Tab создаёт TradeGridsMaster; transport остаётся native |
| Несколько сеток | [TradeGridsMaster.cs](../../OsEngine/OsTrader/Grids/TradeGridsMaster.cs):104 | List TradeGrids, не singular Grid из некоторых context examples |
| Генерация уровней | [TradeGridCreator.cs](../../OsEngine/OsTrader/Grids/TradeGridCreator.cs):103,153,465 | FirstPrice, LineCountStart, шаг, volume и явный TradeGridLine; нет готового Futures2 budget/range contract |
| Entry на уровне | [TradeGrid.cs](../../OsEngine/OsTrader/Grids/TradeGrid.cs):1918,1983,1996 | BuyAtLimit/SellAtLimit создают отдельную Position для каждой линии в обоих типах grid |
| MM take-profit | TradeGrid.TrySetClosingOrders:1795,1850 | CloseAtLimitUnsafe на line.PriceExit, текущий OpenVolume |
| Общие stop/profit | [TradeGridStopAndProfit.cs](../../OsEngine/OsTrader/Grids/TradeGridStopAndProfit.cs):87,141,196 | Вызывается из OpenPosition; в MM ветке Process вызов отсутствует |
| Движение за границу | [TradeGridStopBy.cs](../../OsEngine/OsTrader/Grids/TradeGridStopBy.cs):149 | Candle.Close и процент от FirstPriceReal, не `[L,U] ± D` |
| Native stop | BotTabSimple.CloseAtStopMarket:4733; CheckStop:6976 | Локальные Position.Stop*; не доказанный stop на стороне брокера |
| Частичное исполнение | [Position.cs](../../OsEngine/Entity/Position.cs):439,1000,1021 | OpenVolume = входы минус выходы; Open наступает после первого ненулевого fill |
| Local fill dedup | [Order.cs](../../OsEngine/Entity/Order.cs):349 | Проверка parent market ID и NumberTrade внутри order |
| Cancel | BotTabSimple.CloseOrder:5969 | Передаёт запрос Connector.OrderCancel, не terminal confirmation |
| Recovery ссылок | TradeGrid:2158; TradeGridsMaster:316 | Связь PositionNum с Tab.PositionsAll, не broker reconciliation |

## Почему готовый TradeGrid не равен исходному роботу

`Regime` setter меняет состояние; обработка происходит позже в `Process`.
`Off` не отменяет working orders и при включённом AutoStarter может перейти
обратно в On. `OffAndCancelOrders` делает последовательные попытки отмены;
market exits пропускаются. `CloseOnly` отменяет входы и сопровождает выходы.
`CloseForced` сначала проходит отмены, затем отдельный forced-close pass.

`TryForcedCloseGrid:2208–2265` пропускает позиции с CloseActive до выставления
флага havePositions; переход к Off возможен при всё ещё работающих выходах.
При CheckMicroVolumes остаток может быть отвязан от line без биржевого fill.
Ни Off, ни отсутствие связанной Position не подтверждают broker-flat.

`FirstPriceReal` записывается при создании первого opening order, не первого
fill. StopBy использует его и последнюю Close свечи; readiness, startup delay,
waiting orders/cancels и другие checks выполняются раньше. Вызов локального
stop не гарантирует действия при выключенном/отключённом терминале.

Мастер поддерживает несколько сеток на tab, но callback активации stop в
OpenPosition (`TradeGrid:1279`) не проверяет принадлежность Position перед
установкой флага сетки. Поэтому совместное владение tab требует отдельной
проверки, а не автоматического объединения независимых контроллеров риска.

## Существенная event semantics

- Journal обновляется раньше публичных MyTradeEvent/OrderUpdateEvent
  (`BotTabSimple:8322–8336,8420–8446`). Копировать delta повторно в native
  Position из обработчика робота нельзя.
- Локальные stops проверяются до NewTickEvent, и после первой активации цикл
  по позициям прерывается (`8248–8287`). Собственный tick-handler не получает
  безусловный приоритет над штатными стопами.
- `Open` и opening-success не означают полного исполнения entry. Может
  оставаться активный вход, который наполнит позицию позже.
- `CloseAllOrderToPosition:5165` обрабатывает Active, поэтому общий claim
  «отменяет все Pending/Partial» без дополнительной проверки неверен.
- Native Order.VolumeExecute может выводиться из Done при нулевом локальном
  счётчике (`Order:115`). Это требует сверки с реальными fills; нельзя
  наивно складывать независимые cumulative counters и raw fills.

## PROPOSED mapping

Будущий BotPanel создаёт один BotTabSimple, параметры и обработчики в native
lifecycle. План вычисляется чистой функцией и имеет hash/version. Campaign
controller принимает market/order/fill/connectivity события, накладывает
политику THG-EXECUTION-001 и использует native методы входа/выхода/отмены.
Существующие risk/log/journal abstractions сохраняются.

Перед реализацией подтвердить: точные event hooks, reentrancy SendOrder,
отмену Partial/Pending, correlation unknown submits, query/recovery capabilities,
приоритет stop и возможности close residual. Если native API не покрывает
сценарий, нужен отдельный узкий implementation design, а не обход коннектора.
Название и расположение нового робота определяются отдельной implementation
задачей; документация не создаёт его автоматически.

Данные стратегии дополняют Journal только plan/level/intent mapping и stop
latch. Запрещены параллельный broker sender и конкурирующий TradeGrid worker
на тех же позициях. Native RiskManager остаётся внешним ограничением; его
реакции включаются в campaign reconciliation, а не игнорируются.

## Mode parity

Current grid live worker обрабатывает примерно секундные проходы, тогда как
Tester/Optimizer используют OnTrade (`TradeGrid:65–78,672–700`). В live
дополнительно действуют startup waits, error/distance/operation-delay checks,
missing market ID/cancel waits и коррекция объёма MM exits. Forced close
в Tester/Optimizer принудительно Market, даже при выбранном Limit в live.
Округление объёма, формулы currency/deposit и загрузка состояния также
различаются (`TradeGridCreator:213,347`; `TradeGridsMaster:316–346`).

Предлагаемый общий расчётный модуль может быть одинаковым во всех режимах,
но execution simulation должен явно моделировать latency, partial fills,
cancel races и queue assumptions. Candle backtest не доказывает корректный
порядок пересечения нескольких grid/stop levels внутри свечи.

## Documentation drift baseline

`DOC_STALE`: CONTEXT_GRIDS описывает OpenPosition как одну физическую позицию,
использует отсутствующие singular Grid/отдельный TrailingDown API и слишком
сильные формулировки немедленной отмены/закрытия. В current коде позиции
раздельные, направления trailing находятся в одном компоненте TrailingUp.
Риск-контекст/robot event table называют Open полным исполнением; фактический
SetTrade этого не требует. Это соседние документы; в данной задаче они не
исправлялись. Новый контракт опирается на указанные тела методов.

Поиск прямых `TradeGrid|GridCreator|CloseForced` в `Tests/**/*.cs` и project
files не нашёл совпадений. Это не доказательство отсутствия косвенных тестов.
Интеграционная, native/live и restart qualification нового модуля — NOT_RUN.
