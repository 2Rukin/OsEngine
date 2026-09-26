# APM-IMPLEMENTATION-001 — реализация и доказательства

Статус: IN_PROGRESS. Это факты текущего diff, не PASS поставки.
Baseline/current HEAD: `06d2630c693e3ed76c5c43502d59d1a44de2b4ac`;
ветка `docs/order-flow-production-roadmap`. Commit/push не разрешены и не выполнены.

## Граница и источники

Пользователь поручил реализацию T01–T12. Продуктовый контракт — 01;
математика/данные/исполнение — 03–05. Их предложенные решения уточняются ниже
в рамках разрешённой реализации; завершение gates определяется выполненным
evidence, а не наличием классов. Старые роботы и TradeGrid не изменяются.
Пользовательские dirty binaries и DetachedTables исключены из source diff.
В финальной проверке обнаружен побочный build-effect для DividendsUpdater;
точная граница и сохранённые копии описаны ниже. Сохранность всех binaries
не утверждается только на основании `OutputPath`.
Каталог робота подтверждён: `OsEngine/Robots/MyBots/AdaptivePositionResearchBot/`.
Пользователь явно разрешил запуск штатного Tester для текущей приёмки.

## T01: current capability proof

Read-only researcher `apm_capabilities` проверил текущий checkout.
`BotTabSimple`, `ConnectorCandles`, `Position`, `Order`, `Trade`, `TesterServer`,
`OptimizerServer`, `OptimizerExecutor` в начале задачи не отличались от исходной
базы `f54de33961d45f73319ae1c7313f2854bcb27398`. Затем в TesterServer и
OptimizerServer добавлен описанный ниже opt-in clock hook; остальные файлы
этого capability audit текущим diff не изменены.

| Возможность | Source evidence относительно project/ | Факт / ограничение |
|---|---|---|
| Тики / own fills / orders | `OsEngine/OsTrader/Panels/Tab/BotTabSimple.cs`, NewTickEvent, MyTradeEvent, OrderUpdateEvent | Own fill/order callbacks после обновления Journal; возможны синхронные callbacks |
| Initial/add/reduce | Там же, Buy/SellAtMarket, Buy/SellAtLimit, Buy/SellAtMarketToPosition, Buy/SellAtLimitToPosition, CloseAtMarket/Limit | Одна Position может сокращаться и повторно увеличиваться |
| Partial initial | `OsEngine/Entity/Position.cs`, SetTrade | Первый partial fill переводит Opening в Open; Open не означает полный вход |
| Partial close | Там же | Положительный остаток может иметь State=Closing; один State==Open guard недостаточен |
| Цена среднего | Там же, EntryPrice | Среднее всех исторических открытий; campaign ledger должен отдельно считать оставшуюся стоимость |
| Cancel | BotTabSimple.CloseOrder | Индивидуальная отмена; CloseAllOrderToPosition не подходит как APM-арбитр |
| Stop-market | BotTabSimple.CloseAtStopMarket / CheckStop | Локальный триггер, требуется процесс/связь; внешняя защита не доказана |
| Query | `OsEngine/Market/Servers/IServer.cs` | Нет единого portable authoritative query; live recovery не квалифицирован |
| Tick text | `OsEngine/Entity/Trade.cs`, SetTradeFromString | Side/строковый Id/MicroSeconds сохраняются; Time и MicroSeconds — отдельные поля |
| Tick replay | SecurityTester.CheckTrades / SecurityOptimizer | Проверка ордеров до callback очередного тика; synthetic bid=ask после callback не является реальным стаканом |
| Native fills | TesterServer/OptimizerServer.CheckOrdersInTickTest | Полный order.Volume, без частичной ликвидности/очереди; Tester допускает равное TimeCreate, Optimizer требует более позднее время |
| Native stops | TesterServer.ExecuteOrder | Stop path может использовать lookahead LastTrade; адаптер APM не использует stop flags |
| Optimizer fixed | OptimizerExecutor.CreateNewBot | Для выключенных numeric параметров используется Defolt; выбранные значения требуют корректного native parameter setup |
| Timer | TesterServer/OptimizerServer.ReplayTimeAdvancedEvent | Новый opt-in heartbeat после source.Load, включая пустые итерации. Разрешение равно native replay step; цена не становится свежей от таймера |

Минимальный offline smoke в `Tests/AdaptivePositionManager` исполнил реальный
парсер и Position: 12/12 PASS, exit 0. Он не создаёт серверов, потоков
коннектора, WPF Application или реальных заявок. Результат подтверждает только
перечисленные локальные capabilities. Реальный connector: NOT_VERIFIED.

## Явные инженерные решения

- При нарушении допуска уже открытой позиции выбран EXIT, допустимый по 03§9.
  Нельзя использовать ADD-cap как количество остающихся контрактов после
  убыточного REDUCE: сокращение само уменьшает RealizedNet/доступный бюджет.
- Денежный стоп: `RealizedNet + d*q*(Pmark-B)*c <= -RiskBudgetCurrency`.
  Pmark — объявленная последняя сделка TradeOnly. RealizedNet включает все
  уже начисленные комиссии. Не начисленная комиссия закрытия резервируется
  отдельно в стресс-допуске. Условие включительное, ExitLatch необратим.
- Для минимальной детерминированной поставки активный профиль TradeOnly.
  Book-профили требуют отдельного доказательства T09; подстановка OFI=0 запрещена.
- Native адаптер не обещает live recovery/внешнюю защиту: до квалификации
  коннектора запуск с реальными деньгами должен отклоняться.
- Native stop flags не используются. Приоритетный выход проходит через тот же
  арбитр резервов, что обычное сокращение. Это устраняет второй независимый
  источник закрытия и конкретный lookahead stop path Tester.
- После неизвестного send нет повторной отправки. При rejected EXIT нужна
  сверка перед продолжением; команда не превращает фактический остаток в ноль.
- MinActionInterval отсчитывается от последнего fill соответствующего action;
  ценовой rearm использует VWAP всех fills action. Golden PriceStep=1,
  MaxChildVolume=20, исходный PolicyTarget=InitialVolume.
- Исходное значение MicroSeconds сохраняется без придумывания точности часов;
  исторический event clock — Time, равные секунды упорядочены по исходным строкам.
- Native ResearchOnly допуск сверяет фактические tab mode/server и разрешает
  только TickAllCandleState/TickOnlyReadyCandle. Candle/depth-generated trades
  не принимаются за реальную TradeOnly историю. Явный native VolumeStep и
  DecimalsVolume должны совпадать с расписанием; отсутствующий lot не угадывается.
- ApmSchedule сохраняет неизменяемые risk locks отдельно от policy. Проверяются
  schema, SHA256 выбранного файла, уникальные сигналы, порядок, непересекающиеся
  кампании и единые metadata. Валидация файла потоковая, без сортировки и очистки
  неизвестных дубликатов. Это не автоматический выбор исторического периода.

```mermaid
sequenceDiagram
    participant T as Тик или таймер
    participant C as Кампания
    participant J as Журнал intent
    participant A as Штатный адаптер
    T->>C: Причинный snapshot
    C->>C: Защита, риск, target, pending
    C->>J: Резерв и checkpoint до send
    J-->>A: Durable intent
    A->>A: Команда собственной Position
    A-->>C: Фактический fill или неизвестный результат
    C->>C: Ledger и ExitLatch; без слепого повтора
```

## Проверки и приёмка

Начальная сборка test project с production reference: PASS, 0 errors,
16 существующих warnings, output изолирован в `%TEMP%/OsEngine-APM-build`.
Первый core checkpoint: 7855/7855 assertions PASS, включая 1500 seeded
property cases, зеркальные S01/S02, 100 обратимых циклов и pending/late-fill cases.
Это промежуточное evidence; final totals будут зафиксированы после final diff.

Независимый математический аудит `apm_math_audit`: 97 exact Fraction assertions,
golden equity S01=8 в строке 5 и 36 в строке 7; S02=-4 в конце; S14 ADD max=1.
Неполнота риск-контракта APM-MATH-01/02 разрешена решениями выше;
независимая VALIDATION_1 завершена CLEAN.

Чекпоинт CP02: full solution build exit 0, **0 errors / 45 warnings**;
offline suite **8226/8226 PASS**. Включены отрицательные preflight-контракты,
искажённый checkpoint, fee-aware reduce/re-add recovery, разрешения между
decision/send, native capability guards и fault injection контроллера.
Warnings включают legacy compiler warnings и NU1900 (недоступный NuGet audit).
Изолированный output: `%TEMP%/OsEngine-APM-solution`; build log рядом с ним.

`apm_core_review`: PRIMARY нашёл APM-CORE-001 (повторная отмена terminal order
с недостающими fills) и 002 (late tick после timer). FIX + VALIDATION_1 CLEAN;
37 новых assertions проверяют исходные пути. `apm_adapter_review` PRIMARY:
001–005. VALIDATION_1 закрыла 001/002/004/005 и исходный transport-cancel path
003; затем исправлена прямая regression классификации reentrant persistence
exception. CP03: VALIDATION_2 CLEAN, все пять замечаний закрыты; 8231/8231 PASS.

CP05: зарегистрированный робот компилируется; isolated production/test build
exit0, 0 errors / 16 warnings, offline suite **8253/8253 PASS**. T06 review:
UI001/002 закрыты VALIDATION_1 CLEAN; отдельный feature completion review CLEAN.
Preview использует записанное компактное состояние, CaptureView атомарен,
order/report views и CSV дополнены. UI smoke S01/S02/S08/S09/S19 PASS: пять
независимых окон, sorting/filter/export/close/reopen и неизменность trace.
Физический монитор 144dpi (150%); 100/125/200% проверены только layout simulation.
Smoke использует записанные synthetic traces, не конкурентный native replay.
Артефакты: `%TEMP%/OsEngine-APM-ui-09f0cc85eb0144ce8fdc8d3424447575/evidence`.

CP06: actual NativeTester S01/S02 PASS: `10,8,6,8,10,6,8,0` и
`10,14,18,14,18,14,0`. Использованы настоящий BotFactory, registered robot,
BotTabSimple, Connector и native fills; итоговые позиции и pending равны нулю.
Полный TesterUi с реальными App resources также выполнил S01:
`%TEMP%/OsEngine-APM-full-ui-v4` содержит manifest, trace и screenshots.
Test host подавляет только автоматическое создание MainWindow (и его MCP startup);
сервер, торговые callbacks и сам TesterUi не подменены.

Native S05/S06/S07/S08/S09/S26 PASS. S26 запрашивает EXIT по heartbeat в00:02:00,
следующий tick приходит в00:02:10; отправка не объявляется исполнением.
S06/S07 подтверждают разрешённый native same-time fill по90/110; ожидание
обязательного следующего тика было ошибкой test oracle и исправлено.
Ledger сравнивается с независимым cashflow native fills в пределах заранее
объявленной минимальной денежной единицы SYN0.01; объёмы сравниваются точно.
Cost sensitivity: комиссия0.1/contract+native slippage2ticks, затем ×1.5 и×2;
все три прогона завершены со сверкой комиссий. Это synthetic sensitivity.

Robot review PRIMARY выявил001(потеря UI pause/задержка RegimeOff) и002
(terminal status Optimizer/неполного schedule). Исправлены независимые gates
операторской паузы и Regime; terminal finalization и run-summary идемпотентны.
8283/8283 offline assertions PASS; узкая VALIDATION_1 завершена CLEAN, десять native
repeats выполнены (CP07 ниже). Реальный Optimizer pass callback fixture не квалифицирует.

CP07 coverage измерена coverlet.console10.0.1: четыре заранее выбранных core-файла
Campaign/Contracts/Mathematics/MarketData — **685/758 branches =90.37%**.
По файлам:401/448,154/154,35/36,95/120. Whole-module offline coverage61.46%
включает неисполняемые этим запуском native/UI пути и не выдается за90%.
Raw report: `%TEMP%/OsEngine-APM-final-coverage/coverage.json`.
Процент не заменяет именованные safety tests или отдельные native/UI runs.

CP07: full solution build exit0, **0errors/18warnings**; offline **8283/8283 PASS**.
После исправления native FAST oracle повторная solution build:0errors/2NU1900
warnings, offline8283/8283. Production boundary и измеренная core coverage прежние.
Robot VALIDATION_1 CLEAN:001/002 закрыты. Native controls regression PASS.
Десять повторов S01 (plain/slow/UI) имеют один canonical SHA256:
`D8E397EE65EB3F3D2E4929C9926FAB028ED52177F63D337213EA7B0A58A09D6D`.
Slow добавляет wall-clock задержку8ms/tick; event time и native clock step
остаются прежними. Это проверка зависимости от скорости обработки, а не
утверждение о parity разных разрешений replay clock.
Полный TesterUi выполнил S02; сохранённые native settings доступны в
`%TEMP%/OsEngine-APM-demo-final/workspace`. Native legacy control — неизменённый
UnsafeLimitsClosingSample, RegimeOff, сохранённый Volume7: reload и отсутствие
изменений файла настроек подтверждены. Это bounded Off/settings compatibility.
Все шесть cost runs (S01/S02 ×base/1.5/2) прошли cashflow/fee reconciliation.
Агрегат: [APM-NATIVE-EVIDENCE-001](native-tester/qualification.json), рядом actual
S01/S02 CSV, manifest и screenshots. T07 native evidence review завершён:
T07-NATIVE-001 исправлен: S08/S09 добавляют второй шаг00:00:50 при уже активном
FAST. Native runs проверяют Wait/reason при ненулевом target gap и отсутствие
соответствующих Intent/Fill в00:00:50–00:00:54; оба PASS. VALIDATION_1 CLEAN.
S26 имеет native fill с timestamp последнего тика00:01:54 после EXIT Decision
00:02:00: это ограничение самого native fill engine. Прогон доказывает своевременную
отправку защитного намерения, не реалистичные время/ликвидность закрытия без тиков.

## Состояние заданий

Это единственный текущий реестр состояний; task-файлы задают критерии приёмки.
Ни одно задание не объявлено опубликованным DONE без разрешённого commit.
Технические PASS отдельных проверок указаны независимо от commit/push status.

| Задание | Фактическое состояние |
|---|---|
| T01 | Capability audit и offline native fixtures выполнены; live capabilities не квалифицированы |
| T02 | Реализованы contracts/parser/features/schedule/validation; история пользователя не выбрана |
| T03 | Математика/golden/property tests, независимый аудит CLEAN; финальная core branch coverage744/820=90.73% |
| T04 | Ledger/risk/terminal lifecycle реализованы; core review CLEAN; native protection/cutoff/ledger проверены |
| T05 | Controller/native adapter реализованы; все замечания закрыты, VALIDATION_2 CLEAN |
| T06 | Диагностика/preview/таблицы/CSV реализованы, независимые reviews CLEAN; UI smoke PASS; полная физическая DPI и пользовательская приёмка открыты |
| T07 | Native S01/S02, protection/cutoff/FAST, costs, полный TesterUi,10repeats/legacy/settings PASS; robot/evidence reviews CLEAN; owner acceptance M1 открыта |
| T08 | Native IS/OOS, rolling WFO, fixed/grid/filter/all-trials, costs/ablation, thread parity и explicit stop PASS; production/docs/UI lifecycle VALIDATION_1 CLEAN; исторический OOS BLOCKED_DATA |
| T09 | Event/sample OFI, bounded window/reset и causal calibration реализованы; reference tests и независимый review CLEAN. Native dual-stream/A4 BLOCKED_DATA/CAPABILITY |
| T10 | AS/AC/ADAPT reference и default-off AC pacing реализованы; 001/002 закрыты, VALIDATION_1 CLEAN; updated native A5 PASS. Экономическая калибровка/преимущество NOT_RUN |
| T11 | Lifetime intent budgets, checkpoint bounds, passive watchdog и synthetic component load выполнены; 001 закрыт, VALIDATION_1 CLEAN. Native/live recovery, real-data/UI load BLOCKED_CAPABILITY/DATA |
| T12 | Доступная локальная часть завершена: build/offline/coverage/native regression PASS, runbook/traceability/release manifest и независимые reviews CLEAN. Полная release/owner acceptance BLOCKED |

## Команды текущего offline checkpoint

Команды из `project/`. Для дальнейших сборок задан временный output и отдельный
test-only import, перенаправляющий legacy AfterBuild copy. Сборка solution не
запускает содержащиеся в нём стенды.

```powershell
dotnet build OsEngine.sln -m:1 -p:OutputPath=C:/Users/Mi/AppData/Local/Temp/OsEngine-APM-release-build/ -p:CustomAfterMicrosoftCommonTargets=C:/Users/Mi/source/repos/project/Tests/AdaptivePositionManager/IsolatedBuild.targets -v:quiet
dotnet C:/Users/Mi/AppData/Local/Temp/OsEngine-APM-release-build/OsEngine.AdaptivePositionManager.Tests.dll
```

OBSERVABILITY: REQUIRED — events.jsonl, checksummed checkpoint, сохранённые
snapshots/reasons и штатное error logging. Полная эксплуатационная наблюдаемость
пока не квалифицирована. MODE PARITY: BLOCKED — общая математика проверена,
выявленная разница equal-time native fills явно сохранена; full Tester/Optimizer
и live parity не доказаны.

QG06: реальный UI/native smoke PASS при144dpi; физические100/125/200 и owner
walkthrough NOT_RUN. Native full Tester и Optimizer выполнены; historical
untouched OOS, shadow/paper/live и экономическое преимущество: NOT_RUN.
Никакой суммарный Engineering PASS, Research GO или live authorization не выдан.
UI/schedule production и documentation/XML review прежнего T07 checkpoint: CLEAN.
Финальный T09–T12 documentation/XML review: VALIDATION_1 TERMINAL CLEAN (CP14).

Текущая матрица gates для synthetic TradeOnly ResearchOnly:

| Gate | Статус | Граница |
|---|---|---|
| QG00 | PASS | Validator109/109, local links149/149,255XMLблоков syntax PASS; финальные docs/XML и test-evidence reviews CLEAN |
| QG01–QG05 | PASS | Capability/parser/math/risk/arbitration fixtures и независимые reviews; native/live ограничения указаны |
| QG06 | BLOCKED | Реальный UI150% и layout simulations PASS; физические100/125/200 и пользовательский walkthrough не подтверждены |
| QG07 | PASS | Штатный Tester/полный TesterUi, S01/S02,10повторов, cost/stress и bounded legacy/settings evidence |
| QG08 | BLOCKED | Synthetic native search/rolling WFO/IS/OOS/all-trials/thread parity и automatic UI lifecycle/stop PASS; исторический untouched OOS не выбран |
| QG09 | BLOCKED | Formula/reset/causal component evidence PASS; synchronized native book source, calibration и A4 ablation отсутствуют |
| QG10 | N/A_WITH_REASON | Default TradeOnly: AC выключен. Opt-in A5: reference/native engineering evidence PASS, empirical calibration/ablation BLOCKED_DATA |
| QG11 | BLOCKED | Synthetic component load/fault/checkpoint PASS; native-session recovery, native/UI load на выбранном потоке и shadow/paper не квалифицированы |
| QG12 | BLOCKED | Local automated verification и final independent reviews PASS/CLEAN; physical DPI/walkthrough, empirical/operational gates и публикация не выполнены |

XML-аудит проверил187блоков и обнаружил DOC001/002: уточнены реальный App host
и shallow/read-only контракт DTO-массивов. Behavior не менялся; VALIDATION_1 CLEAN.

## Побочный эффект сборки и сохранение outputs

`Tests/DividendsUpdater/DividendsUpdater.csproj` содержит существующий AfterBuild
`CopyDividendsUpdaterToOsEngineBin`. Он задаёт собственный `OsEngineBinDir`;
одного `-p:OutputPath=TEMP/...` недостаточно. Поэтому предыдущие solution builds
скопировали DLL/EXE/PDB/runtimeconfig/deps DividendsUpdater в `OsEngine/bin/Debug`.
Для будущих команд выше добавлен test-only `IsolatedBuild.targets`: он заменяет
этот copy target и направляет пять файлов в `TargetDir/updater-aux`. Обычная
сборка без явного import не меняется; исходный DividendsUpdater.csproj не изменён.

Исходные hashes этих пяти файлов в каталоге приложения до начала работы не
сохранялись. Старый набор в `Tests/DividendsUpdater/bin/Debug/net10.0-windows`
доступен, но его побайтовое равенство прежнему app output не доказано. Оба набора
скопированы с hashes в `%TEMP%/OsEngine-APM-output-recovery`, подкаталоги
`current-app-output` и `earlier-test-output`, `inventory.json`. Восстановление
app output наугад не выполнялось. Эта незавершённая сверка пользовательских
outputs указана отдельно от PASS торговых tests; исходные файлы роботов,
пользовательские DetachedTables и остальные build-каталоги не редактировались.

CP09: документированная сборка с test-only import реально выполнена: exit0,
0errors/37warnings; offline8283/8283PASS. Все пять auxiliary files созданы в TEMP,
hashes текущего app output до/после этого запуска совпали. Build-isolation review
CLEAN; документационный VALIDATION_2 CLEAN, конечный переход. Recovery inventory
10/10 hashes перепроверен независимо. Пользователь затем потребовал сохранить оба
набора и исключить все пять файлов из APM commit; файлы не восстанавливались.
Commit/push не выполнялись.

## Воспроизводимый запуск в штатном Tester

В репозитории есть синтетические [S01](../../../Tests/AdaptivePositionManager/Fixtures/S01/schedule.json)
и [S02](../../../Tests/AdaptivePositionManager/Fixtures/S02/schedule.json).
В каждом каталоге `ticks/APM_SYNTH.txt` содержит150секунд; schedule хранит SHA256.
TimezoneUTC, 25.09.2026 00:00:01–00:02:30; вход00:00:35, cutoff00:02:00.
GoldenFixture ядра и NativeTester остаются разными fill profiles.

1. Открыть штатный Tester, создать `AdaptivePositionResearchBot`.
2. **Сервера подключения → Дополнительно → Указать в папке**: выбрать только
   каталог `ticks` соответствующего сценария. SourceFolder, TickOnlyReadyCandle.
   После загрузки проверить диапазон25.09.2026 00:00:01–00:02:30.
3. **Дополнительно → двойной щелчок по APM_SYNTH.txt**: указать PriceStep1,
   PriceStepCost1, Lot1, **Шаг объёма1**, **Точность объёма0**, **Мин объём1**,
   нажать **Принять**. Native tick loader не определяет VolumeStep; редактор
   сохраняет его в `SecuritiesSettings.txt` выбранной папки данных.
4. **Настройки данных**: Tester, `APM_SYNTH.txt`, классTestClass, портфельGodMode,
   timeframeSec1. Timeframe используется штатным графиком; APM получает ticks.
5. **Параметры**: абсолютные пути к `schedule.json` и `ticks/APM_SYNTH.txt`, новый
   writable `Artifacts root`, RegimeOn. Для эталонного прогона оставить FASTfalse,
   FixedScales через Volatility scales=false, Add5, Reduce10, gamma0; native
   slippage0 и schedule fee0. Изменённые cost locks требуют отдельного schedule.
6. В окне сервера нажать **Начать тест**. После первых ticks открыть
   **Настройки торговли бота**: независимое окно APM и кнопки пяти таблиц.
   До создания campaign эта кнопка показывает штатные параметры.
7. Проверить S01/S02 по CSV, Pause/Resume, отсутствие повторного входа после
   cutoff, Completed при q0 и pending0. RegimeOff блокирует набор независимо
   от UI Resume; protective exit и сокращения остаются разрешены.

Автоматизированный полный UI-прогон (создаёт новый временный каталог и сохраняет
его native settings; путь назначения должен отсутствовать):

```powershell
dotnet C:/Users/Mi/AppData/Local/Temp/OsEngine-APM-final-build/OsEngine.AdaptivePositionManager.Tests.dll --native-tester S02 C:/Users/Mi/AppData/Local/Temp/APM-owner-demo full-ui
```

Для обычной component квалификации заменить `full-ui` на `plain`; `ui` включает
окна диагностики, `slow` — wall-clock pacing, `controls` — независимые блокировки,
`legacy` — старый роботOff/сохранённые параметры. Эти команды запускают Tester:
требуется разрешение на конкретный research run, оно получено в текущей задаче.
Harness завершает свой отдельный процесс после terminal evidence: у native
Tester нет public shutdown worker. FullUI использует реальные App resources;
только WPF `_startupUri` очищен reflection в test host, чтобы не запускать
MainWindow/MCP. Native trading execution и UI не заменены fixtures.

## CP10–CP11: T08 native Optimizer

Latest owner scope: continue T08–T11 and feasible T12 without waiting for physical
DPI100/125/200 or owner walkthrough. Those manual checks are not simulated.
DividendsUpdater:4tracked(DLL/EXE/deps/runtimeconfig),1ignored(PDB); only DLL/EXE
have a Git diff. Both5-file backups retained,10/10 hashes verified. Before-image
unknown: neither HEAD nor older test output is a proven exact rollback. Safe
proposal is to retain current files and both backups; future APM staging uses
explicit source/evidence paths, excluding every bin path and DetachedTables.

T08 implementation uses actual OptimizerMaster/OptimizerExecutor. Native iteration
and filters remain unchanged. ApmOptimizerStudy freezes selected current numeric
values into native fixed columns before native counting resets mutable values.
At most3numeric axes,5values each,125combinations; locks remain schedule inputs.
Zero axes is converted to a singleton native numeric axis (native otherwise runs0).
Study plans validate every candidate and phase before launch; all-trials retains
missing/partial/duplicate runs. NotObserved is unknown, never inferred zero profit
or an invented reason for a native rejection. Native filtered counts are retained
separately in qualification.json. Native run summaries include exact policy, source
schedule hash, actual loaded phase bounds and whole-campaign metrics.

OptimizerServer.TryGetReplayInterval exposes loaded bounds without changing replay.
Robot selects only whole phase campaigns, requires past warmup and room after cutoff
for a final fill; boundary cuts and empty phases reject. Each pass has unique output
and no WPF window. Report windows open only through explicit APM study report button.
ApmStudyReportWindow reads all-trials.json, supports numeric sorting/status filter/CSV;
missing evidence remains blank. Net and drawdown include open inventory; average q
and time-in-position integrate callback-ordered event time, independently of the
2000row diagnostic buffer. MaximumDrawdown in the study table is the maximum
individual campaign drawdown, not a stitched multi-campaign portfolio drawdown.

OptimizerDataStorage now roundtrips Tester-compatible optional metadata fields7–10:
Expiration/MinTradeAmount/VolumeStep/SecurityType. Decimal MarginSell is preserved;
unrelated rows keep complete optional tails. Legacy5/6/7-field rows remain readable
and never infer a volume step. Native qualification saves metadata then reloads
storage into fresh security objects before execution.

Evidence: [native qualification](native-optimizer/qualification.json), companion
experiment/all-trials JSON. Eleven actual native studies completed27passes from30
planned rows: B0/B1/A1/A2/A3, fixed Reduce8, Add3/5/7 neighborhood, deliberate native
filter rejection and cost1/1.5/2. Three OOS rows rejected by preceding native filter
remain NotObserved in all-trials, with native report counts proving no OOS execution.
Six additional grid passes each at1and3threads had identical canonical decision
hashes. S01/S02 native optimizer quantity traces match independent oracles and
Tester; zero-cost net36/-4. Native fill timestamps are NOT identical (Optimizer
requires a later timestamp). This is recorded model difference, not full fill parity.
Historical untouched data/intervals and economic acceptance profile remain unselected.
QG08 empirical acceptance remains BLOCKED_DATA; synthetic engineering evidence is
not historical Research GO. T08 production/docs reviews: VALIDATION_1 CLEAN.

Reproduce in a NEW temporary directory after the documented isolated build:

```powershell
dotnet C:/Users/Mi/AppData/Local/Temp/OsEngine-APM-t08-build/OsEngine.AdaptivePositionManager.Tests.dll --native-optimizer C:/Users/Mi/AppData/Local/Temp/APM-optimizer-demo 3 grid
```

Arguments: outputdirectory, native thread count, B0/B1/A1/A2/A3/fixed/grid/filtered/
cost1/cost1.5/cost2. This explicitly launches synthetic native Optimizer in a child
process, authorized for this task. To open reports in the robot parameters, press
APM study report and select the generated all-trials.json. For owner historical
research, select dataset/hash/metadata, whole-campaign train/validation/untouched
boundaries and economic criteria first; do not reuse these synthetic intervals.
Native UI fixed values use its fixed/default column; the explicit study API accepts
current values and normalizes them locally. Disable native ManualControl protections
on the dedicated APM tab; native manual protections competing with APM are rejected.

## CP12: T09–T11 и автоматическая регистрация experiment

Reference design: [T09/T10](../14-research-implementation.md),
[T11](../15-operations-implementation.md). Offline checkpoint:8534/8534PASS,
isolated production/test build0errors16legacywarnings. T08 review001/002,
T10 review001/002 и T11 review001 закрыты независимыми VALIDATION_1 CLEAN.
T10 tests проверяют перепланирование после target-changing WAIT и конечную
AC trajectory при horizon1e308; T11 — поздний полный ADD при последнем рабочем
EXIT и исчерпанном exit budget: q14/pending10, FaultedClosing/Query, recovery.

Два component load-прогона по20000price events:19594/s без подробной диагностики,
12857/s с ней;97.97x/64.28x измеренного synthetic peak200/s. Core p99=.0954/.1536ms,
end-to-end p99=68.0161/81.215ms. Queue512, retained intents33, history2000,
price/fill/order counts и durable rows сверены. Совпала проекция отправок
{intent.Id,Action,Volume,Quantity}; полный canonical decision/fill trace не
сравнивался. Измерен конечный managed-memory прирост, не асимптотический plateau.
[Raw load](operations/load.json) — PASS_SYNTHETIC_COMPONENT_ONLY.
Это не native-server/UI нагрузка и не peak выбранной реальной истории.

На current T10 fix actual native A5, а также B1, fixed8, grid, filtered и report-ui
прошли повторно: `%TEMP%/OsEngine-APM-t12-hook-{variant}`. Последний режим открыл
реальное ownerless окно study report; физические DPI не переключались.
Отдельные более ранние native regression artifacts: [operations](operations/native-regression.json).

Новый opt-in `IOptimizerResearchRun` связывает стандартный Optimizer UI с APM study:
план сохраняется до native counting, completed pass summary — до удаления сервера,
all-trials и native selection — до terminal event; Artifacts root восстанавливается.
Неподдерживающие interface роботы используют прежний путь. Независимый PRIMARY
нашёл NATIVE-UI-001: внутренний stop публиковал terminal event слишком рано/дважды.
Research stop теперь возвращается во внешний completion; native IS/OOS stop PASS:
6planned/1completed/5NotObserved и6/4/2, ровно один terminal event, evidence/root
готовы уже при первом callback. Четыре native rolling WFO фазы PASS, второй IS
содержит три целые прошлые кампании. NATIVE-UI-001 VALIDATION_1 TERMINAL CLEAN.

## CP13: финальная автоматическая проверка и локальная поставка

Final solution build с isolated import:0errors/1warningNU1900 в соседнем
OrderFlowResearch (NuGet audit недоступен); предыдущая полная перекомпиляция
production:0errors/16legacywarnings. Offline suite8559/8559PASS. Production SHA256
`6373692E6B8063E3F6F152F9D704EFEA47C1E55223FA981A362144F03DD1005C` одинаков для
native stop/WFO и финального release-build. Test-only additions проверили bounded
IDs/payload, ledger-budget mismatch, invalid fills и actual unexpected native partial:
q3/pending7 учитываются, reconciliation запрещает слепой повтор.

Повторный coverlet10.0.1: Campaign456/506, Contracts158/158, Mathematics35/36,
MarketData95/120, вместе744/820=90.73%. Whole-module offline1291/1985=65.04%;
native/UI paths им не выдаются за покрытые. Raw coverage JSON сохранён в TEMP/
OsEngine-APM-release-coverage, hashes/counts в release manifest. Инструментирование
выполнялось на отдельной копии, runtime native-прогонов не изменялся.

Final native Tester S01 full-ui, S02 controls и legacy Off/settings PASS. S02 controls
имеет собственную trace10,18,14,0 из-за управляемых pause/Off; она не подменяет
baseline S02. Старые10repeat/cost/FAST evidence остаются checkpoint evidence, а не
новым полным прогоном всех сценариев. Current readiness и instructions:
[локальный runbook](../16-release-readiness.md), [release manifest](release-manifest.json).
Финальные local links149/149 и255XMLблоков syntax PASS; commit/push отсутствуют.

## CP14: конечный handoff текущего разрешённого scope

APM_T12_ADDITIONAL_TEST_EVIDENCE PRIMARY CLEAN: независимая проверка49/49source и
65/65evidence hashes, двух release DLL, raw coverage и22requirements. Новые tests
подтверждают конкретные invalid-input/native-partial/recovery-budget пути.
APM_T09_T12_FINAL_DOCS PRIMARY нашёл два DOC_STALE: единица payload limit и
неоднозначная ссылка на прежний T07 review. Оба исправлены без изменения кода;
VALIDATION_1 TERMINAL CLEAN. Production/test/runtime evidence не инвалидировано.

Текущий результат — локальный ResearchOnly candidate; доступная автоматическая
часть T12 выполнена. QG06 открыт только на реальные DPI100/125/200 и owner
walkthrough. QG08/09/11 и полная QG12 остаются BLOCKED по причинам в матрице;
исторические данные, книга, native/live recovery и external qualification не
подменяются синтетикой. Физические DPI в этом продолжении не имитировались.
Baseline/current HEAD06d2630c693e3ed76c5c43502d59d1a44de2b4ac; commit/push NOT_RUN.
Оба5-filebackupsets DividendsUpdater сохранены,10/10hashes совпадают. Все пять
outputs исключены из APM scope; исходное состояние не восстанавливалось догадкой.

## Публикация после CP14

Пользователь отдельно разрешил commit и обычный push текущей APM-реализации.
Публикуется ResearchOnly candidate с прежними открытыми gates. Runtime/source
evidence CP14 не изменено; повторная сборка не требуется. В commit входят только
APM source/tests/docs/evidence, без generated outputs, пяти DividendsUpdater files
и пользовательских DetachedTables. Commit идентифицируется содержащим его Git ref;
успех push проверяется совпадением remote ref с локальным HEAD после отправки.
