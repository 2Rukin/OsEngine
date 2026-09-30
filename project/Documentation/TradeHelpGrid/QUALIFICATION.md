# THG-QUALIFICATION-001: проверка и границы evidence

> Историческая спецификация и evidence baseline этапа исследования. Текущий
> implementation contract: [ADR-THG-002](ADR-0002_OSENGINE_IMPLEMENTATION.md);
> native команды: [THG-OPERATOR-002](OSENGINE_OPERATOR.md). Указания «будущий»/
> «код не менялся» ниже относятся к исходному исследованию, не к текущему diff.

**Статус:** PROPOSED QUALIFICATION + CURRENT OFFLINE COMPONENT EVIDENCE.
Последний implementation verdict: [terminal scoped review](IMPLEMENTATION_REVIEW.md).

Сценарии ниже — требования к будущей реализации, **не выполненные тесты**.
Новая readiness-gate система не вводится. Основание acceptance — решения
ADR-THG-001; проверки относятся к конкретным рискам и документам.

## Матрица сценариев

| № | Сценарий | Наблюдаемый ожидаемый результат | Вид evidence |
|---|---|---|---|
| 01 | Long/short план из контрольного примера | Цены, q, carry и bounds совпадают с выбранным профилем | Deterministic math |
| 02 | Fixed-lot legacy и target | Различия carry явно воспроизводятся; target не превышает budget | Math/profile |
| 03 | N=0/1, U<=L, нулевые τ/v/C/M target; g0 в legacy replay | Отказ до mutation плана и до send; нулевая цена сама по себе не ошибка | Negative component |
| 04 | N слишком велик, duplicate/off-tick уровни | Отказ/новый явный preview, без скрытой перестановки | Math/validation |
| 05 | Не хватает бюджета хотя бы на один уровень | План не активируется, прежний план не повреждён | Component |
| 06 | Точное касание обеих границ | Inclusive trigger согласно explicit bounds и quote source | Component |
| 07 | Gap через несколько levels и stop | Emergency priority; нет входов после latch | Causal replay |
| 08 | Partial entry и отдельный TP | h/e/x верны, close не превышает исполненный доступный объём | Execution component |
| 09 | Fill раньше ack | Bounded unmatched/reconciliation, нет повторного submit | Event permutations |
| 10 | Duplicate fill до/после restart | Один экономический эффект в native и level mapping | Replay/persistence |
| 11 | Duplicate/out-of-order order status | Нет отката executed qty и terminal state | Event permutations |
| 12 | Submit timeout с фактически принятой заявкой | Unknown, reserve сохраняется, query без blind resend | Connector fault injection |
| 13 | Cancel + одновременный partial fill | Учитывается fill, reserve остатка до terminal outcome | Execution component |
| 14 | Cancel rejected | Working exposure сохранён; no false canceled | Negative component |
| 15 | Late entry fill после stop | Новый остаток закрывается, grid не rearm | Liquidation component |
| 16 | Старый exit/stop ещё может исполниться | Нет второго full-volume close/переворота | OCO/capability |
| 17 | Freeze/exit-block/HJ включены при stop | Target emergency не блокируется ordinary policy | Risk interactions |
| 18 | Liquidation ограничена rate/сессией | Видимый остаток/age/alarm; no false FlatConfirmed | Native/connector |
| 19 | Flat holdings, но есть pending entry | FlatConfirmed запрещён | Component |
| 20 | Market reject/price band/limit не исполнен | Классификация и bounded policy, не бесконечный widening | Connector |
| 21 | Disconnect/restart после persist-before-send | Сверка intent; отсутствие дубля | Crash injection |
| 22 | Crash после send до ack/checkpoint | Unknown с восстановлением identities, no blind resend | Crash/connector |
| 23 | Corrupt checkpoint/unsupported schema | Faulted/Reconciling, без чистого автозапуска | Persistence |
| 24 | Другая позиция на netting счёте | Ownership conflict, чужой объём не закрывается | Allocation/native |
| 25 | Stale/crossed/empty quote | Нет increases, visible data status | Data component |
| 26 | Смена tick value/GO/expiry | План остановлен для перепроверки, единицы не смешаны | Metadata/native |
| 27 | Заполненная очередь/медленный UI | No dropped fills, no silent risk loss, bounded resources | Load/lifecycle |
| 28 | Перенастройка при working/unknown | Старая версия прослеживается до terminal outcomes | Reconfigure |
| 29 | Grouping нескольких levels | Суммарный qty, partial allocation, external dedup до local IDs | Grouping |
| 30 | Pause/StopWhenFlat/Flatten | Разные контракты и корректные terminal labels | UI/native |
| 31 | Tester/Optimizer/live одинаковые входные события | Одинаковый decision trace; различия fills отдельно заданы | Mode comparison |
| 32 | Broker flat и пустые ordinary/stop orders | Совпадение native/broker с согласованным watermark | OWNER-RUN connector |
| 33 | Положительный, отрицательный и cross-zero диапазоны до5 знаков | Один алгоритм tick grid, endpoints/уникальность/знак без потерь | Target numeric component |
| 34 | Tick0.00001/0.00005; отрицательная цена между ticks | Floor/ceil по назначению; passive и aggressive различаются | Target numeric component |
| 35 | Недостаточно ticks для N, off-tick границы, precision6 | Явный отказ без partial plan mutation и скрытого округления | Negative component |
| 36 | Цена0 и missing quote; Limit(0) и Market | Отдельные presence/type, zero trade не теряется в native feed/order path | Native data/execution |
| 37 | Цены одного плана меняют знак, бюджет/C неизменны | Лоты/резервы не меняют знак; fixed и equal-budget расчёты воспроизводимы | Target numeric/risk |
| 38 | C0/нехватка бюджета/увеличение известного margin | No new increases до валидного покрытия; существующие fills и риск сохранены | Risk/reconfiguration |
| 39 | Partial fills −1 и+1 со средней0; пятый знак | Average0 действительна при h>0, остаток и TP без раннего округления | Fill/average/native |
| 40 | Long/short TP и stop с обеих сторон0 | Верное направление сравнения/округления, нулевой enabled stop работает | Risk/native |
| 41 | Запрет long/short, block interval и preorder через0 | Сторона независима от цены, preorders не обходят запрет | Feature interactions |
| 42 | Процентные расстояния/trailing и quote0 | Положительная явная база из THG-PRICE-001, без деления на signed entry | Target numeric/feature |
| 43 | Runtime shift/widen/rollover и tick change при partial/Unknown | Новая версия проверяется; старый order/fill не переоценивается; cancel barrier | Reconfiguration/recovery |
| 44 | Save/load/locale/−0/legacy zero sentinel | Round-trip без потери знака/пятого разряда; неоднозначная миграция→Reconciling | Persistence/parser |
| 45 | Decimal/tick-index/product overflow, средняя с >5 знаками | Отказ overflow до side effects; средняя не обрезается как цена заявки | Numeric boundary |
| 46 | Одинаковый signed/zero stream в Tester/Optimizer/live | Сохранение нулевых событий, limit0/type, корректные fills/PnL; capability подтверждена отдельно | Native parity + OWNER-RUN |

Для строк33–46 canonical target — [THG-PRICE-001](PRICE_DOMAIN.md).
Числовой checker из его evidence проверяет только примеры формул. Он не
исполняет эту матрицу, native парсеры/торговые callbacks или брокерские заявки.

Дополнительная детализация переходов, включая Expirate/Rotate, находится в
[THG-F2-COVERAGE-001](FUTURES2_COVERAGE.md). HJ, grouping, настройки и portfolio
trailing не исключены из согласованного объёма переноса. Для trailing обязательны случаи
равенства/превышения порогов, FromMax/From0, dynamic clamp, временные окна,
stale Update и конфликт DeferredClosePos с emergency latch. Их legacy bodies
изучены, но приёмка будущего переноса не выполнена.

## Что записывать в результате

Exact source/build hashes, plan/profile/schema versions, seed/fixture,
input event order, instrument metadata/units, connector capability, mode,
expected/actual intent and fill traces, totals, exit code, skipped reasons и
cleanup. PASS без assertion на экономический результат h/e/x недостаточен.
Fixture, которая заранее возвращает желаемый результат, не проверяет native
order handling. Вместо полного raw broker log сохраняются обезличенные
корреляции и минимальные подтверждающие факты.

Historical evaluation отдельно фиксирует источник/качество данных, комиссии,
slippage, latency, partial fill/очередь, отсутствие future leakage, spread,
сессии и gap. Свеча с несколькими пересечениями не задаёт внутрисвечной
последовательности; такой тест не подтверждает tick/grid parity.
Доходность и максимальная просадка не выводятся из формул распределения денег.

## Выполненное в этой задаче

Статически исследованы 4 strategy modules и shared execution/host modules,
без исполнения vendor code. Основное соответствие — Futures2; explicit
differences зафиксированы для DTF, DTM и generic DTI. Контрольные примеры
проверяются отдельно как арифметика документа, не вызовом legacy binaries.
Воспроизводимые hashes/anchors — [THG-EVIDENCE-001](EVIDENCE.md).

Итоговые totals validator, links, arithmetic и independent reviews находятся
в подразделе «Проверка комплекта» THG-EVIDENCE-001. Это единственный
канонический источник фактических итогов данной задачи.

**NOT_RUN:** OsEngine build (нет production/build diff), приложение, QUIK,
broker/connector, paper/live orders, MCP/StopOrders stands, исторический
backtest, native replay нового робота, GUI/DPI walkthrough, performance
measurement и crash recovery будущей реализации. Никакое отсутствие этих
запусков не преобразуется в PASS. Для live claims требуется отдельное
OWNER-RUN разрешение и конкретный выбранный environment.

## Текущая C# реализация: checkpoint перед PRIMARY review

Дата: 2026-09-29. HEAD `088add98b728f8088fb18ff2e59c8d4113ad043c`, dirty
implementation scope ADR-THG-002. Hash inventory checkpoint сохраняется в TEMP
`TradeHelp4-analysis/futures2-primary-checkpoint.json`. Независимые production
и documentation review на том checkpoint ещё не были завершены; итог FIX и
VALIDATION_1 приведён ниже. Исходные46 qualification rows выше
и60 source cases не объявляются пройденными по числу новых assertions.

| Команда / evidence | Результат |
|---|---|
| `dotnet build OsEngine/OsEngine.csproj --no-restore -v:q` | PASS, 0 errors / 0 warnings, incremental |
| `dotnet build OsEngine.sln --no-restore -v:q` | PASS, 0 errors / 17 warnings |
| `dotnet run --project Tests/TradeHelpGrid/OsEngine.TradeHelpGrid.Tests.csproj --no-build --no-restore` | PASS, **201/201 assertions**, exit0 |
| Git Bash: `bash .agents/validation/validate-agent-system.sh` из корня | PASS, **109 checks** |
| `git diff --check` | PASS |

Default Windows `bash` указывает на недоступный WSL `/bin/bash`; validator
успешно выполнен через `C:/Program Files/Git/bin/bash.exe`. Build warning NU1900
означает недоступность NuGet vulnerability service, а не успешный security audit.
Остальные warnings — существующие BCS nullable annotations, OKX unreachable и
unused/unassigned fields; build не превращает их в qualification PASS.

`Program.cs` проверяет exact signed ticks/0, grid carry, budget count/step,
atomic fill projection/dedup, cancel/unknown reservations, runtime policy,
protective generations, daily/funding checks, manual partial exits, multi-credit
allocations, replacement quantity/basis, HJ, helper boundaries, persistence и
coordination. `NativeCases.cs` проверяет order serialization compatibility,
signed native Position PnL/fees, explicit quote presence, emulator matching и
exact private Tester/Optimizer tick matching methods на uninitialized fixtures.
`AdapterCases.cs` проверяет настоящий signed gateway/Journal binding через spy
IServer, persistence before send, reentrant callback, local suppression после
Unknown, quantity validation, policy edit поверх credits, stale group command,
source account snapshots и network/dispose subscriptions.

Fixtures не вызывают конструкторы BotPanel/BotTabSimple/native server, не
запускают их фоновые workers, WPF или реальную сессию. Proxy не отправляет
сетевых запросов. Временные checkpoint файлы принадлежат только fixture и
удаляются после проверки. Это проверка методов/компонентов, а не end-to-end
transport test. Исторические36 signed-price arithmetic examples остаются
отдельным evidence предыдущего этапа.

**NOT_RUN / REQUIRES OWNER-RUN:** полный native Tester/Optimizer replay с robot
lifecycle, физический WPF Parameters/status/DPI, live/shadow/paper connector,
broker-specific signed/zero limits и commissions/margin, реальные restart,
account reconciliation, partial/cancel race transport и rollover двух контрактов.
Live сценарий должен назвать account/environment, инструмент, разрешённые
операции и stop/cleanup. Никакой из этих сценариев пока не разрешён или выполнен.
Profitability, liquidity/slippage model и trading readiness не подтверждены.


### FIX после независимого PRIMARY

Закрываемые IDs и proof: [review record](IMPLEMENTATION_REVIEW.md). FIX прогон:
`dotnet build OsEngine.sln --no-restore -v:q` PASS0errors/17warnings;
тот же offline harness `--no-build --no-restore` PASS **219/219**, exit0.
Новые assertions воспроизводят late-direction fill перед publication, reload
quote-only HJ/trailing checkpoint до Dispose, replacement carry по remaining
lot и следующий обычный вход/WholePosition, фиксированную group ReturnBase,
magnitude gap validation и реальные Alor methods с synthetic JSON.
Alor fixture не вызывает constructor, не читает credentials и не подключается.
Live non-paper без HasExplicitAccountUpdates отклоняется; это проверяемый отказ,
а не расширенный список поддерживаемых коннекторов. Независимая VALIDATION_1
завершена **CLEAN/CLEAN**: все 4 production и 3 semantic docs findings CLOSED.
Exact checkpoint, hash verification и whitespace-команда зафиксированы в
[terminal review record](IMPLEMENTATION_REVIEW.md). Это историческое evidence исходного переноса. Последующая TRANSAQ-адаптация
имеет собственный checkpoint и проверки ниже. NOT_RUN/OWNER-RUN границы выше сохранены.

## TRANSAQ implementation checkpoint

Задача TASK-THG-TRANSAQ-IMPLEMENTATION-006, baseline HEAD прежний; prior219
assertions не переименовываются в брокерскую квалификацию. Новые managed сценарии
находятся в `Tests/TradeHelpGrid/TransaqCases.cs`, включая настоящий конечный
AServer dispatch, ConnectorCandles, Journal и adapter. SyntheticTransaq подменяет
только физический command boundary, экземпляры создаются без native constructors.

Проверяются source-clock/epoch, order deltas, ранние/частичные/повторные fills,
terminal-before-details, signed/zero serialization, account units/presence,
manual funds gate, final queue authority, lost response, native+campaign reload,
owner absent resolution и late fills через disconnected Connector. DLL, брокер,
GUI и полный native Tester/Optimizer lifecycle не запускаются. Итоговый exact
checkpoint, totals, full solution и независимый review фиксируются в
[THG-TRANSAQ-IMPLEMENTATION-006](FINAM_TRANSAQ_IMPLEMENTATION.md).

P6 остаётся NOT_RUN / REQUIRES OWNER-RUN. До сессии должны быть явно указаны
профиль, DLL/сервер, account/environment, FUT-инструмент и допустимые операции.
Read-only сверка отдельно проверяет brokerref echo, units, свежесть нулевой и
неизменной позиции. Минимальные send/partial/cancel/reconnect сценарии требуют
отдельного лимита количества/экспозиции, timeout и проверяемого cleanup. Для
отрицательных и нулевых реальных цен нужен реальный допуск инструмента;
синтетические assertions не являются таким допуском.

Практический Standard union handoff: [сценарий владельца](OSENGINE_OPERATOR.md#p6-сценарий-владельца-для-standard-union). Все его physical этапы остаются NOT_RUN.


## Empty-stop native owner checkpoint

[THG-EMPTY-012](EMPTY_REMOVAL.md) добавляет managed EmptyRemovalCases: реальное
срабатывание helper, отказ после queued request при поздних фактах, сохранение
Stopped до disposal, write failure, restart до keeper removal, native stop-openers,
legacy unknown и monotone coordination history. Проверяются actual owner membership
и veto методы на объектах без native constructors. Fake owner моделирует deferred
callback; настоящий WPF dispatcher loop, DeleteRobotCore/keeper destruction,
скринер/горячее обновление в UI и процессное аварийное завершение остаются NOT_RUN.
Totals и exact checkpoint — [review012](EMPTY_REMOVAL_REVIEW.md); это не live qualification.


## Выбираемые execution policies

ExecutionPolicyCases проверяет fixed IDs/carry и priority при ограниченном бюджете,
selected batch после partial/cancel/reload, обычные/protective exit priorities,
12 комбинаций signed/zero-centered диапазонов и logical/physical direction,
preorders/manual/funded/emergency price regressions, runtime cancel-confirm/Unknown/
late-fill публикацию и schema5. Native spy проверяет точную цену в реальном managed
Journal/SubmitSignedOrder path. Внешние эффекты/ликвидность не выполняются.
Exact totals/checkpoint: [review013](EXECUTION_OPTIONS_REVIEW.md).


## Сохранение per-level controls при range transforms

RangeStateCases проверяет обе logical стороны/Hedge, signed Shift/Widen черезноль,
сохранение наценки/двух запретов/ID/количеств/nativeprofile, отказ при CountMode.Step
и overrides, прежний untouched sizing, cancel-confirm/late-fill и checkpoint reload.
Managed native adapter проверяет Faulted/reason и отсутствие sends/cancels при
автоматическом неоднозначном remap. Source geometry и physical lifecycle не
квалифицированы. Exact totals: [review014](RANGE_STATE_REVIEW.md).


## Последующая квалификация015: SRU6 и signed native Tester

[THG-HISTORICAL-015](HISTORICAL_QUALIFICATION.md) заменяет прежний `NOT_RUN`
только для перечисленных exact сценариев: zero-loader, cross-zero/five-decimal
TXT, один день SRU6 TXT и пятиминутный QSH Quotes interval. Он не превращает
46 target rows в общий PASS и не подтверждает partial fills, full-day QSH,
restart, Optimizer/live parity, TRANSAQ или прибыльность.
