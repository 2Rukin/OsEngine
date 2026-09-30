# THG-HISTORICAL-015: SRU6 и signed-price qualification

**Статус:** CURRENT OFFLINE NATIVE TESTER EVIDENCE; LIVE/TRANSAQ NOT QUALIFIED.  
**Дата:** 30.09.2026.  
**Baseline:** HEAD `088add98b728f8088fb18ff2e59c8d4113ad043c` и dirty implementation
Futures2Grid, зафиксированная entry-manifest SHA-256
`21cf59f762702520d41143bbda9042815eda19ddd4cbc83759f1725b63ad5f6b`.

Документ фиксирует реально выполненные прогоны, исправленный дефект и оставшиеся
ручные сценарии. Он дополняет [THG-QUALIFICATION-001](QUALIFICATION.md), но не
является доказательством доходности, ликвидности или совместимости с TRANSAQ.

## Данные и воспроизводимость

Основной источник — `C:\Qscalp\SRU6.txt`, SHA-256
`435EA400FFCC45CD3215BE0806F660368A024D1C2942B8EED8AA8E3D2FED1F7B`.
Полный последовательный аудит прочитал 3 130 667 строк за 174 дня с
16.03.2026 по 17.09.2026. Все строки имеют семь полей, timestamp не убывает,
цены положительны и целочисленны. Поэтому SRU6 подтверждает positive-price
исторический flow, но сам по себе не проверяет отрицательные цены или пять знаков.

В `C:\Qscalp\SRU6` обнаружено по 171 gzip QSH-файлу `Deals`, `Quotes` и
`AuxInfo`. Заголовки всех 513 файлов: QSH v4, один stream, инструмент
`TRANSAQ:SRU6:FUT:1:1`. Native Tester способен читать `Deals` либо `Quotes`, но
текущий выбор файла на день не синхронизирует оба потока. В квалификации
используется только явно выбранный `Quotes.qsh`.

Дополнительный fixture
[SIGNED5.txt](../../Tests/TradeHelpGrid/Fixtures/SIGNED5.txt), SHA-256
`833E562F99091394A5607FB395DF4F4D204C9CB458FA58842F896D4A0240721D`,
содержит 12 тиков от `−0.00010` до `0.00010`, включая literal zero и пятый знак.

## Сверка с существующей поддержкой отрицательных цен

До Futures2 в OsEngine уже существовали signed `decimal` поля и низкоуровневое
создание заявки без запрета отрицательного значения. Это пригодная основа, но не
полный zero/signed contract:

| Путь | Фактическое поведение |
|---|---|
| `Order` / `Trade` / `MyTrade` | Хранят отрицательное значение и zero без смены типа |
| `PositionCreator` | Передаёт literal price в native order |
| Обычные `BotTabSimple` entry helpers | Отрицательную цену не запрещают, но `price == 0` отклоняют |
| Legacy `Position` | Пропускает некоторые zero-расчёты; процент от signed entry меняет знак |
| Legacy quote/emulator path | Использует zero как sentinel отсутствующей котировки |
| Futures2 opt-in path | Явно разделяет presence/type/value, сохраняет literal zero и использует positive `PercentBase` |

Futures2 переиспользует штатный lifecycle заявки, Journal и PositionCreator, а
его `UsesSignedPrice`/explicit-quote слой сохраняется. Замена на обычные
`BuyAtLimit`/`SellAtLimit` изменила бы zero, PnL и quote presence.

## Выполненные прогоны

Все native-команды запускались отдельным процессом в новом каталоге с собственными
`Engine`, `Data` и `Log`. Они создавали только Tester-заявки, не запускали live
connector, не читали credentials и не обращались к брокеру.

| Проверка | Результат и наблюдаемое evidence |
|---|---|
| Zero-loader до fix | FAIL: `1 → 0 → −1` приводило к `securityLoaded=false`; исключение было проглочено загрузчиком |
| Zero-loader после fix | PASS: инструмент сохранён, `PriceStep=1`, 3/3 строки приняты |
| Signed/cross-zero native replay | PASS: каждый из2 tab получил точные12/12 тиков с исходными ID/time/side/volume/price; 12 native orders и12 fills содержат отрицательную, literal-zero и пятизнаковую цену; identity/quantity/filled-cost ledger совпал, все orders `UsesSignedPrice=true`, replay дошёл до конца, ошибок0 |
| SRU6 TXT, 16.03.2026 | PASS: каждый из2 tab получил точные240/240 тиков, input SHA-256 `832D94EA0FBB32F556686FD485A4BB51FD19C2658DE4D79555392BF8D4EDA504`; 6 orders/6 fills и ledger совпали, replay дошёл до конца, итог `Stopped / Stop after flat`, held quantity0, ошибок0 |
| SRU6 QSH Quotes, первые5 минут | PASS: source/input SHA-256 `B8EF60E23E4D889D604EC747D48D4348927FBEE3065613FC682ACC90D9981CA9`; 313 152 clock callbacks, по1 depth callback на каждый tab, 2 orders/2 fills и ledger совпали, replay дошёл до конца, ошибок0 |
| Managed component suite | PASS: 935/935 assertions; partial/late/duplicate/cancel/Unknown, persistence, inventory, hedge, batches, range и signed arithmetic |

QSH-проход ограничен интервалом `03:55:24.026–04:00:24.026`. Попытка полного
дня остановлена harness timeout через 180 секунд: 9 979 435 clock callbacks и
2 224 depth callbacks, terminal event не получен. Это `NOT_PASS` и измеренная
граница производительности текущего миллисекундного Tester clock.

Native runner:

```text
dotnet Tests/TradeHelpGrid/bin/Debug/net10.0-windows/OsEngine.TradeHelpGrid.Tests.dll --native-zero-loader <new-output-directory>
dotnet Tests/TradeHelpGrid/bin/Debug/net10.0-windows/OsEngine.TradeHelpGrid.Tests.dll --native-sru6 txt C:\Qscalp\SRU6.txt 2026-03-16 <new-output-directory>
dotnet Tests/TradeHelpGrid/bin/Debug/net10.0-windows/OsEngine.TradeHelpGrid.Tests.dll --native-sru6 qsh C:\Qscalp\SRU6\SRU6.2026-03-16.Quotes.qsh 2026-03-16 <new-output-directory>
```

Успешно дошедший до проверки replay сохраняет `result.json`; при штатном либо
аварийном завершении runner также сохраняет `errors.txt`. Если exception или
timeout возникает до проверки результата, вместо `result.json` создаётся
`failure.txt`. Runner завершает только свой процесс через `Environment.Exit`,
потому что native Tester worker не имеет публичного shutdown API.
Успешный JSON сохраняет per-tab delivery, фактические native order/fill prices,
локальные identities/quantities и их сверку с Futures2 ledger.
Сводные хэши и totals сохранены в
[sru6-native-qualification.json](evidence/sru6-native-qualification.json).

## Исправленный дефект

`TesterServer.LoadTickFromFolder` определял шаг цены через перебор завершающих
нулей строкового представления. Для literal `0` индекс становился `−1`; общий
`catch` удалял инструмент без диагностической ошибки. Теперь zero принимается
как рыночное наблюдение, но не используется для вывода tick. Если все
проанализированные цены равны нулю, исходный шаг устанавливается в `1` и может
быть явно изменён в metadata Tester. Ненулевые цены и прежнее определение шага
не меняются.

## Что этот replay не доказывает

- TXT формирует `bid=ask=last trade`; исторический spread и очередь отсутствуют.
- Tester исполняет заявку полным объёмом и не расходует доступную глубину QSH.
- `Deals` и `Quotes` не воспроизводятся как единая причинная лента.
- В Tester у Futures2 нет live checkpoint; process-kill/restart recovery не проверяется.
- Нет network loss, submit uncertainty, broker reject, частичного fill или cancel/fill race.
- Комиссии, реальное ГО, price bands и TRANSAQ signed capability не квалифицированы.
- Результат не является backtest доходности: параметры выбраны для прохождения
  order lifecycle, а не для экономической оценки стратегии.

## Ручные test cases

### M01 — физический UI и загрузка SRU6 TXT

1. Запустить Tester, выбрать `TickOnlyReadyCandle`, папку с отдельным `SRU6.txt`.
2. Создать `Futures2Grid`, подключить обе вкладки к `SRU6.txt / TestClass / GodMode`.
3. Установить `Regime=On`, signed capability, range внутри цен выбранного дня,
   положительные TickValue/Collateral/PercentBase и Preview.
4. Запустить один день и открыть status window.

Ожидание: пять уникальных on-tick levels, intent/order/fill IDs видимы, status не
показывает `Faulted`/`Reconciling`, после `StopAfterExit` held/native position равны0.

### M02 — отрицательные цены, zero и пять знаков в UI

Повторить M01 с `SIGNED5.txt`, range `−0.00010…0.00010`, Count5,
Tick `0.00001`, Markup `0.00005`, PercentBase `0.00010`. Проверить Preview,
таблицу заявок и журнал позиций.

Ожидание: уровни `−0.00010, −0.00005, 0, 0.00005, 0.00010`; Limit(0) остаётся
limit, отрицательные fill prices и average0 не исчезают, percent/PnL конечны.

### M03 — QSH Quotes в полном окне

1. Поместить только выбранные `*.Quotes.qsh` в отдельную папку; не смешивать их
   с `Deals`/`AuxInfo`.
2. Выбрать `MarketDepthOnlyReadyCandle`, подключить Futures2Grid и запустить
   сначала 5 минут, затем полный торговый интервал.
3. Зафиксировать wall time, depth callbacks, orders/fills и terminal event.

Ожидание короткого окна: BBO приходит из стакана и заявки проходят signed gateway.
Полный день пока считается ручной performance-проверкой; отсутствие terminal
event нельзя помечать PASS.

### M04 — runtime-настройки при working orders

На медленном replay создать resting entries, затем по одному менять markup,
entry/exit enabled, `ForbidLong`/`ForbidShort`, selected levels, Shift/Widen.
После каждого изменения нажимать Apply и наблюдать cancel-confirm barrier.

Ожидание: старый intent остаётся привязан к старому plan ID; новая версия не
публикуется до terminal cancel/fill, late fill учитывается ровно один раз.

### M05 — стоп и аварийная ликвидация

Выбрать день/диапазон, где цена пересекает enabled outer stop. Одновременно
включить ordinary exit block и forbid exits.

Ожидание: emergency latch имеет приоритет, новые entries не создаются, reduction
не превышает owned quantity, `FlatConfirmed` появляется только после отсутствия
fillable orders и нулевого native/ledger остатка.

### M06 — ручная регистрация внешнего объёма без заявки

При `Regime=Off` применить план, задать уникальный operation ID, endpoint,
levels, quantity, известную execution price/time/reference и выполнить сначала
`External increase`, затем частичный `External decrease`.

Ожидание: native order/fill count не меняется; ledger quantity/cost/fee меняются
один раз, повтор того же operation ID ничего не удваивает. Произвольная ручная
сделка другого робота или брокерского интерфейса автоматически не присваивается
Futures2; её нужно явно зарегистрировать.

### M07 — partial/cancel/late/Unknown

Этот сценарий нельзя создать историческим Tester, потому что он даёт full fill.
На управляемом fake connector: исполнить часть entry, отправить cancel, дать ещё
один late fill, повторить fill ID, затем вернуть terminal status без всех details.

Ожидание: owned quantity соответствует уникальным fills; canceled remainder
освобождается только после terminal evidence; duplicate не влияет; недостающие
details оставляют `Unknown/Reconciling`; blind resend отсутствует.

### M08 — process restart и восстановление

Требуется отдельный persistent paper connector или live-like stand: остановить
процесс (a) после durable intent до send, (b) после send до ack, (c) после partial
fill до journal callback. Исторический Tester этот case не закрывает, потому что
simulation store создаётся заново.

Ожидание: один client/native identity, никакого повторного send до reconciliation,
late broker facts восстанавливают тот же intent. До появления такого стенда case
остаётся `REQUIRES OWNER-RUN`; real account для него не требуется и не используется.

## Вердикты impact

`OBSERVABILITY: REQUIRED` закрыта отдельными `result.json`, `errors.txt`, native
IDs и существующим status/log робота; production telemetry не менялась.
`MODE PARITY: BLOCKED` для live/TRANSAQ и partial-fill модели; native Tester
подтверждён только в перечисленных точных сценариях. Commit/push не выполнялись.
