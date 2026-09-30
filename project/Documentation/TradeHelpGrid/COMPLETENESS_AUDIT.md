# THG-COMPLETENESS-AUDIT-007: полнота переноса Futures2

**Статус:** READ-ONLY INVENTORY — TERMINAL DOC REVIEW CLEAN; FULL SOURCE PARITY NOT ESTABLISHED.
**Дата:** 2026-09-29. **HEAD:** `088add98b728f8088fb18ff2e59c8d4113ad043c`.
Вопрос владельца: что ещё отсутствует, изменено и требует отдельного согласования.
Снимок007 сохранён как факт на audit checkpoint. Последующее исправление
наблюдаемости/reserve и уточнение общего счёта: [THG-OWNERSHIP-008](SHARED_ACCOUNT_AND_REGISTRATION.md);
оно не означает завершения остальных строк этого перечня. Следующий checkpoint009
реализует ручной учёт и список уровней именно для Inventory:
[контракт](INVENTORY_REGISTRATION.md), [bounded review](INVENTORY_REVIEW.md).
Следующий checkpoint010 добавляет [IsHedge](HEDGE_MODE.md); review/evidence
ограничены этим дополнением.
Checkpoint011 добавляет [обычные команды по списку уровней](BATCH_LEVELS.md).
Ниже сохранён исторический снимок007, а не актуальная отрицательная оценка009.
Scope: current Futures2Grid, его native adapter/controller и standard TRANSAQ,
сопоставленные с реконструкцией Futures2. Production behavior в этом аудите не менялся.

Источники: [ADR-THG-002](ADR-0002_OSENGINE_IMPLEMENTATION.md), current code,
[исходные уровни/заявки](FUTURES2_LEVELS_AND_ORDERS.md),
[исходные runtime settings](FUTURES2_RUNTIME_SETTINGS.md),
[исходные переходы](FUTURES2_TRANSITIONS.md),
[host/callbacks](FUTURES2_HOST_AND_CALLBACKS.md),
[expiration/rotation](FUTURES2_EXPIRATION_AND_ROTATION.md).
Документальное описание исключения не доказывает согласия владельца убрать функцию.

## Отсутствующее и частичное

| Возможность | Current факт | Прямой code/source anchor |
|---|---|---|
| Ручная регистрация объёма, RegisterZone/RegisterPart/SetLots, правка средней | External adoption отсутствует; ExternalNet лишь декларация для сверки, Enter/Exit selected level создают новые собственные заявки | ADR002 Native data/recovery; LEVELS §ручные изменения; NativeAdapter.AccountMatches/ResolveAbsent; robot controls |
| TrendType / IsHedge | Нет отдельной независимой hedge-инверсии source; есть обычные Long/Short | SETTINGS:49; HOST:52; Plan direction и Controller entry side |
| Генетический подбор replacement | Нет source RotateViewModel/VIndividual/population; replacement задаётся вручную | SETTINGS:291–306; robot PrepareRollover/StartRollover |
| Внешнее AutoUpdateUpDown / ArbitrationService | Нет провайдера диапазона; есть ручные Shift/Widen и локальные ShiftAfterFlat/WidenWhenFlat | TRANSITIONS:159–160; Commands.Shift/Widen |
| Унаследованные многоинструментные корзины | Кампания не multileg basket: два таба current/replacement, без Ratio/DirectionalIntegrity/PriorityInstrument | LEVELS:193; SETTINGS:112–114; PlanInput.Instrument |
| Портфельный helper | Только участвующие Futures2Grid в одном процессе/режиме/валюте, не произвольные OsEngine-стратегии | HOST:167–174; robot PortfolioScope/Peer; ADR002 transfer/group |
| Автоудаление пустой стратегии | EmptyStop отключает helper/входы, но не удаляет native robot | HOST:194,237; Hj/Controller empty stop |
| Произвольный список выделенных уровней | Одна selected level или все для части команд; нет source произвольного batch selection | SETTINGS:81; HOST:56; robot controls/Commands.EditLevels |
| Автоматический budget из TRANSAQ broker free | Недоступен; требуется manual envelope, связь free с pending не доказана | NativeAdapter.Reconcile/Pump; current TRANSAQ contract |
| Автоматический запрос полной истории/status после reconnect | Capabilities false, соответствующие query methods не реализованы; полного replay proof нет. Manual Unknown recovery существует | TransaqServerPermission:198–210; TransaqServer query stubs; adapter Reconcile/ResolveAbsent |
| Наблюдаемость account mismatch и runtime reserve | Status может показывать сохранённый Reconciled=true при закрытом AccountMatches; отдельного текущего суммарного резерва нет | adapter OnExplicitAccount/AccountMatches; robot Status; Book.Reserved |

Genetic/external automatic range относятся к инструментам автоматизации source;
владелец прямо выбрал ручные начальные параметры. Их отсутствие следует раскрыть,
но оно само по себе не доказывает нарушение именно ручного режима. Multileg —
унаследованное расширение source, а не основной single-futures builder.

## Реализовано с другим торговым поведением

| Область | Source → current |
|---|---|
| Порядок уровней | Long ascending → descending; влияет на первые allocations при MaxActions/ограниченном бюджете. TRANSITIONS:87–93; Plan.Build:152; Program тест descending |
| Обычные limit-цены | Направленная quote±threshold → bound level/quote для entry и TP для exit. LEVELS:45–49,108–111; Controller ordinary submit |
| Экспирация | Старую/новую ноги можно было послать подряд → drain, old flat, затем new entry. Current entry/HJ/day/funds gates действуют и при replacement; source обходил их |
| Shift/Widen | Симметричный source widen со сдвигом plans/rules → расширение нужных краёв/new plan, rules автоматически не сдвигаются; ShiftAfterFlat отличается source price predicate |
| Grouping/liquidity | Current grouping только совместимых entries, exits по native position; SequentialLiquidity ждёт orders, но не ограничивает каждую заявку source ratio-unit |
| Signed-price математика | Положительный Collateral, явный PercentBase, единая tick-grid и абсолютные stop prices заменяют старые price/GO/percentage/rounding правила. Это адаптация к требованию signed/zero, не численная копия всех старых формул |
| Day budget/helper return | Расход по actual fills + pending reserve, фиксированный ReturnBase и явная FeePerUnit вместо исходных attempt/cancel accounting и текущего helper denominator/int return |
| Stop/reconfiguration/Unknown | Cancellation barrier, latched emergency, удержание Unknown и confirmed flat заменяют unsafe forgetting/revoke/rebuild paths; ordinary policy не блокирует emergency |
| Gap/sessions | Gap references вручную вместо source Open update/current−current; current allow-calendar запрещает outside allow, source отсутствие Disallow могло разрешать |

Опорный current code: [Plan](../../OsEngine/OsTrader/Grids/Futures2/Futures2Plan.cs),
[Controller](../../OsEngine/OsTrader/Grids/Futures2/Futures2Controller.cs),
[Commands](../../OsEngine/OsTrader/Grids/Futures2/Futures2Commands.cs),
[Policy](../../OsEngine/OsTrader/Grids/Futures2/Futures2Policy.cs),
[Book](../../OsEngine/OsTrader/Grids/Futures2/Futures2Book.cs),
[Adapter](../../OsEngine/OsTrader/Grids/Futures2/Futures2NativeAdapter.cs),
[robot](../../OsEngine/Robots/MyBots/Futures2Grid/Futures2Grid.cs).
Порядок уровней, limit pricing и исполнение rollover не являются обязательным
следствием отрицательных цен; их нельзя выдавать за точный перенос без явного
объяснения изменения экономического поведения.

## Что не следует ошибочно называть пропуском

HJ, partial owned fills/ведение, запреты сторон, skip уровней, whole-position TP,
trailing/mini/time, replacement и transfer имеют реализацию. Market также
проходит controller→adapter→native signed gateway→TRANSAQ bymarket; permission true.
Отсутствующие ветки StrategyState.Speed/Classic в main reaction switch, пустой
UseStopOrders setter и нечитавшиеся аргументы не являются потерянными рабочими
возможностями. Это не означает, что все inherited Speed/Classic были no-op:
Speed обновлял baseline, а ClassicController использовался в Rotate.
Это не утверждение полной parity
всех комбинаций; перечень исходных переходов не равен исполняемому oracle test.

## Проверки и полномочия

[Транспортный review](FINAM_TRANSAQ_IMPLEMENTATION_REVIEW.md) завершён CLEAN/CLEAN
в своём bounded scope: solution0errors17warnings,356/356 offline assertions,
validator109/109. Внешний эффект заменён SyntheticTransaq. Новый audit не
переоткрывает review и не распространяет его verdict на полный source parity.

Проверены managed crash/reload/Unknown/no-resend, partial/cancel/late/duplicate
owned executions и source ordering. Не выполнены реальный process kill/restart
с брокером, DLL/server compatibility, полный native Tester/Optimizer lifecycle,
WPF и live fills. Tracked DLL2.26.0 против исследованного manual2.26.4.
Ненулевой ExternalNet, полный manual buy/sell adoption и его allocation/PNL не
покрыты. Локальная emergency-защита не работает без процесса/соединения; наличие
server-side stop protection не заявлено.

Поручение реализовать весь объём уже разрешает code/offline работу. Не нужно
задним числом требовать повторного разрешения на обычные доработки. Существенное
изменение торговых правил и исключение исходной функции следовало явно обсудить;
сам ADR не заменяет такую договорённость. Отдельное разрешение по AGENTS требуется
для конкретного native GUI/stand либо broker account/environment/scenario:
connect, send/cancel/close/reconnect и их пределы. Замена vendor DLL не выполняется
автоматически. Ни один такой эффект в аудите не совершён; commit/push отсутствуют.

## Независимая проверка этого перечня

Documentation reviewer: PRIMARY с одной адресной поправкой, VALIDATION_1 CLEAN.
THG-COMPLETENESS-DOC-001 CLOSED: no-op ограничен отсутствующими ветками reaction
switch; сохранённые действия Speed/Classic теперь указаны явно. Production/test
код не менялся. Проверены 2/2 hashes; HEAD неизменён. Manifest
`completeness-validation1.json` SHA256
`688302abd6648a93494d533320110f7f5be8915eec8e6e777bc2a2654ee203a0`.
Terminal status/index updates находятся вне этого frozen semantic checkpoint.
Проверка документа не означает завершения полного переноса или live qualification.


### Последующее уточнение012: EmptyStop owner removal

Исторический inventory выше не переписывает outcomes ранних проверок.
[THG-EMPTY-012](EMPTY_REMOVAL.md) реализует optional удаление отдельного never-active,
never-coordinated live робота через native owner. Групповое удаление helper/чужих
стратегий, ever-submitted campaigns и Tester/Optimizer physical deletion не
заявлены. Остальные незакрытые строки исходного полного объёма сохраняются.


### Последующее уточнение013: execution options

[THG-EXECUTION-OPTIONS-013](EXECUTION_OPTIONS.md) добавляет явные opt-in настройки
ascending traversal и directed quote±threshold для reached ordinary limits.
Default поведение и все сохранённые IDs/volume allocations остаются прежними.
Это закрывает выбор двух указанных вариантов, а не legacy sizing/interleaving,
manual/rollover bypass или полную численную parity. Исторические строки007 выше
не означают отсутствия новых опций в013.


### Последующее уточнение014: range controls

[THG-RANGE-014](RANGE_STATE.md) закрывает потерю per-level markup/entry/exit controls
при target Shift/Widen. Буквальный source geometry вариант остаётся отдельным scope.
Source уточнение: Shift11844/11854 меняет bounds/PlanPrice, **не rules**; преобразование
ограниченного класса entry rules относится к Widen9075–9130. Поэтому историческую
обобщённую строку Shift/Widen выше нельзя трактовать как translation rules на любой Shift.
