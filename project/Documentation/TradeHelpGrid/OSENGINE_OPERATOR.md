# THG-OPERATOR-002: управление Futures2Grid в OsEngine

**Статус:** CURRENT OPERATOR GUIDE — OFFLINE CHECKS PASSED, LIVE NOT QUALIFIED.  
**Контракт:** [ADR-THG-002](ADR-0002_OSENGINE_IMPLEMENTATION.md).
GUI и реальные подключения в этой задаче не запускались.

## Первоначальная настройка

1. Выберите обычного робота `Futures2Grid`. Начальный Regime — Off. Используются
   два штатных простых таба: endpoint0 для текущего инструмента, endpoint1 для замены.
2. В штатных настройках таба выберите инструмент/счёт/сервер. Tick и instrument
   берутся оттуда. Выбор соединения не заменяет проверку разрешённых signed/zero
   цен конкретной площадкой. Переключатель `Signed order capability selected`
   — явное подтверждение выбранного профиля, а не автоматическая сертификация.
3. В Control задайте Capital и Manual available funds. Это выделенный общий
   конверт, из которого вычитаются held/pending обязательства. Переключать
   `Use portfolio available funds` можно только при известном смысле денежных
   полей конкретного сервера. Валюта должна соответствовать Grid.Currency.
4. В Grid задайте Low/High, Direction, CountMode и Count либо RequestedStep;
   Budget, Collateral, TickValue, VolumeStep, MinimumVolume, FixedVolume,
   Markup и PercentBase. FixedVolume=0 выбирает распределение бюджета.
   TickValue — стоимость одного tick за native единицу количества; Collateral
   — положительное обеспечение этой единицы. Не подставляйте цену вместо ГО.
5. При необходимости включите LowerStopEnabled/UpperStopEnabled и задайте
   внешние абсолютные цены. Они должны быть строго вне диапазона и кратны tick.
   Ноль допустим как цена, флаг включения хранится отдельно.
6. `Preview and status` показывает preview текущих полей, фактические held lots
   и IDs принятых планов. Preview не означает применения. Нажмите
   `Apply configuration`, затем `Reconcile native and account`, затем
   `Start or resume`. Для live сверка требует `Owner verified account and orders`.
   Этот флаг используют после проверки реального состояния, не для обхода Unknown.
   Live non-paper требует source capability HasExplicitAccountUpdates. Сейчас
   она есть в Alor и в явно включённом signed FUT профиле TRANSAQ; реализации
   без неё отклоняются. Для TRANSAQ дополнительно обязателен ручной конверт,
   см. раздел ниже. Наличие кода не подтверждает реальную совместимость. Paper/replay
   имеют отдельную account boundary и не требуют этой live capability.

Например Low=−0.00010, High=0.00010, Count=5 и tick=0.00001 дают пять
различных уровней через0. Collateral/Capital/PercentBase остаются положительными.
Первый технический прогон выполняется в отдельно разрешённом окружении;
эта инструкция не означает, что такой прогон уже выполнен.

## Настройки сопровождения

Все поля `Policy.*` доступны в native Parameters. Изменение пользовательских
параметров вызывает staging и cancel-confirm; существующий held сохраняется
на исходном PlanId. Ниже перечислены связанные группы, а точные формулы — в ADR.

| Поля | Назначение и единицы |
|---|---|
| ExitMode, WholeMarkup, ExitAfterAcross | TP по входу/плану/общей средней; WholeMarkup0 использует Grid.Markup; clamp к середине |
| ForbidEntries/Exits/Long/Short | Запреты обычных действий; emergency отдельно |
| PreEntries/PreExits, PreDistance/Unit | Предзаявки; price/ticks/zones/percent положительной PercentBase |
| MarketOrders, SlippageTicks, ThresholdTicks | Тип обычной заявки, bounded limit displacement и порог срабатывания |
| MaxActions, SequentialLiquidity, GroupOrders | Число submits за проход, ожидание предыдущих, объединение совместимых entry |
| BlockEnter.Enabled/From/To, BlockEnterAlways | Inclusive зона входа и постоянный/однократный режим |
| BlockExit / BlockExit2 | Две inclusive зоны запрета обычного выхода |
| Rules | Дополнительные interval/from/to/gap/file правила |
| GapReferencesKnown, PreviousSessionClose, CurrentSessionOpen | Явно известные ручные исходные цены gap; нулевые цены разрешены |
| HjEntries, HjExits, HjWidth | Фильтр Хука — Дживса; ширина в единицах цены |
| DayLimit, CreditDayExits, ResetDayWhenFlat | Положительный дневной расход обеспечения; DayLimit0 отключает лимит |
| FeePerUnit | Денежная комиссия за фактически исполненную единицу на каждой стороне |
| FreshnessSeconds, OrderLifeSeconds, UnknownAfterSeconds | Возраст snapshots, время до cancel и до Unknown; секунды |
| IntervalMilliseconds, RejectionLimit | Частота обычного прохода и остановка после подтверждённых отказов |
| Sessions | allow/disallow интервалы часов режима; не биржевой календарь автоматически |
| ShiftAfterFlat, WidenWhenFlat, StopAfterExit | Сдвиг после полного цикла, расширение пустого диапазона, остановка после flat |
| Trailing, TrailFromMax, TrailStep/Target/TargetStep/Dynamic/Minimum | Трейлинг по проценту положительной ReturnBase |
| MiniStop/Percent/Minutes/Limited, TimeStopMinutes, EmptyStopMinutes | Ранний убыток, длительность текущего held, отсутствие первой активности |
| DeferredClose, RetainCollateral | Отзыв добровольного закрытия и сохраняемое обеспечение; не emergency |
| EqualizeVolumes, PieVolume | Групповое выравнивание в денежных единицах обеспечения |

Пример Rules: `entry;interval;-1;0;any`. Несколько строк в текстовом параметре
разделяются `|`. Формат: `entry/exit;interval/from/to/gap/file;from;to;any/empty/full;путь`.
Gap сравнивается по модулю; bounds должны быть неотрицательными. Close10/open8
даёт signed−2, поэтому `entry;gap;1;3;any` блокирует такой gap. Интервал−3…−1
отклоняется при применении. Последнее поле обязательно для file и необязательно для остальных. Числа
используют точку. File проверяет существование, содержимое не читается.
Sessions: `allow;10:00;18:45|disallow;13:00;13:05`; matching allow имеет
приоритет на пересечении, поэтому для реального перерыва задайте два отдельных
allow-интервала. Overnight поддержан. Live часы — локальные часы приложения,
Tester/Optimizer — часы событий источника.

## Ручной учёт объёма

Группа Inventory добавляет регистрацию, снятие владения, изменение средней и
отдельный учёт внешнего исполнения. Параметры, target/delta, цены уровней,
operation ID и recovery описаны в [THG-INVENTORY-009](INVENTORY_REGISTRATION.md).
Команды не отправляют заявку брокеру; после них нужна явная сверка перед resume.

## Ручные команды

`Pause entries` переводит Regime в Off, отменяет оставшиеся входы, продолжает
обычные выходы. `Cancel working orders` запрашивает отмену всех orders campaign.
`Emergency flatten` фиксирует неснимаемую до confirmed flat ликвидацию;
обычные запреты выхода её не блокируют. Подтверждённого отсутствия остатка
нельзя вывести только из нажатия кнопки.

`Selected plan id` пустой означает активный план; для retained inventory
скопируйте PlanId из строки Held в status. `Selected level=-1` означает все
уровни выбранного плана при пустом CSV. `Apply selected level settings`
меняет Markup/EntryEnabled/ExitEnabled после order drain. `Enter selected levels`
доступен для выбранного набора уровней активного стабильного плана и соблюдает funding/
direction/filter gates. `Exit selected levels` фиксирует выбранные реальные lots
и сопровождает частичный остаток до закрытия, даже при MaxActions=1.

`Reduce to retained collateral` — добровольное уменьшение; `Cancel voluntary
reduction` требует cancel-confirm. `Reset daily counter`, `Reset HJ` и
`Rearm trailing helper` — явные команды сброса соответствующего учёта. Они
не удаляют native orders/fills. `Shift range`/`Widen range to quote` создают
новую геометрию, сохраняя старые lots.

## Замена контракта и несколько роботов

Для replacement настройте второй таб и `Replacement.*`; требуются та же валюта
и логическое направление/IsHedge, достаточные новые quantities по ordinal уровня и капитал.
`Replacement shift by spread=true` переносит текущий диапазон по свежему
межконтрактному spread; false оставляет ручные границы. `Prepare replacement`
проверяет пустой target endpoint. `Start replacement` проходит Draining →
Reducing → Entering → Applied; source orders/fills не исчезают между стадиями.
`Cancel replacement` сохраняет фактическую позицию и отменяет оставшиеся заявки.
Изменять геометрию активной замены следует после её отмены; policy gates могут
приостановить набор нового контракта.

Для локального transfer у destination включите `Accept coordination`, примените
план, сверку и Start. У source задайте имя `Transfer destination` и положительный
`Transfer collateral`, затем `Start collateral transfer`. Сумма должна
представляться admissible source quantities. Source постепенно закрывает,
destination принимает confirmed credits и набирает позицию. Перевод денег между
брокерскими счетами не выполняется. Cancel доставляет уже списанный credit.
При Canceled=false статус Completed учитывает и destination entry fills.
При Canceled=true он означает лишь получение всей уже Debited суммы; Requested
может остаться невыполненным и destination entry fills могут отсутствовать.

Portfolio participants — имена через запятую в том же процессе/режиме; для
Optimizer scope также включает native server number. Все участники должны
согласиться через Accept coordination и использовать одну валюту. Portfolio
leader рассылает helper close/recovery; `Reduce selected portfolio` делает
явный group close с `Portfolio retain fraction` от0 до1 либо EqualizeVolumes.
Отсутствующий/устаревший participant mark останавливает вычисление group helper.

## Перезапуск, Unknown и наблюдаемость

После restart/reconnect новые действия требуют сверки native журнала, списка
заявок и account net. `External net endpoint 0/1` — явно проверенный внешний
net того же инструмента; это не импорт позиции в robot. Другие позиции или
ордера в выделенном табе блокируют reconciliation.

Unknown удерживает резерв. Для `Resolve verified absent order` нужны точный
Resolve intent id, Verified executed quantity, совпадающее с идентифицированными
fills, и проверенный terminal исход. Команда не дорисовывает отсутствующие fills.
После неё снова выполните полную Reconcile. Повреждённый checkpoint/отсутствующий
native journal нельзя лечить удалением файла состояния и повторным Start.

`New campaign after confirmed flat` снимает emergency только после native/account
сверки, отсутствия оставшихся заявок/позиции и незавершённых debited transfers.
Идентификаторы и dedup history сохраняются. Перед удалением робота завершите
сопровождение заявок: Delete не является приказом бирже закрыть позицию.

Status показывает State/Reason, ActivePlan, Capital/DaySpent/Realized, HJ/helper,
последние100 intents с native IDs, все held lots, transfers и replacement.
Preview относится к полям формы; после автоматического сдвига/перевода он может
отличаться от принятого плана. Источником текущего held остаются native IDs и
PlanId в нижней части окна. WPF layout/DPI и реальная работа этой процедуры
требуют owner-run, список границ — в [квалификации](QUALIFICATION.md).

## Финам: обычный TRANSAQ, единый счёт

Реализация и точные ограничения —
[THG-TRANSAQ-IMPLEMENTATION-006](FINAM_TRANSAQ_IMPLEMENTATION.md).
Статус offline/review необходимо проверять там перед использованием.
В штатном TRANSAQ выберите `Signed FUT transport profile = Standard union`
(по умолчанию `Off`). Для отдельного срочного счёта существует отдельная ветвь
`Standard FORTS`; к подтверждённому единому счёту она не применяется.
В табе выберите соответствующий `United_…` и FUT-инструмент.

В роботе задайте положительные Collateral/TickValue и Currency в согласованных
денежных единицах, Capital и `Manual available funds`. Параметр
`Use portfolio available funds` должен быть false: автоматическое распределение
broker free между уже учтёнными и локальными заявками не доказано. Это отдельный
контроль от Capital; ручной конверт не подтверждает фактическое обеспечение у брокера.
После проверки профиля используется `Signed order capability selected`, обычные
Configure, явная Reconcile и Start. Смена профиля требует reconnect и новой сверки.

При missing position робот не объявляет flat. Если полный снимок не содержит
нулевую строку, подтвердить её можно только явной Reconcile после проверки счёта
и заявок; такое подтверждение имеет обычный TTL. Исчезновение строки после
закрытия также требует новой сверки. Периодическая перепубликация cache этот
TTL не продлевает. Пока брокер не даёт доказуемое обновление строки, автоматическое
продолжение после её исчезновения не обещано.

Unknown не пересоздаётся. Проверив именно этот intent, отсутствие активной заявки
и все её исполнения в терминале брокера, используйте `Resolve verified absent order`
с точным объёмом уже известных fills, затем Reconcile. Нельзя уменьшить ранее
наблюдённое исполнение или создать сделку этой командой. Старые реальные TRANSAQ
campaigns без brokerref не мигрируют автоматически: сначала требуется завершение
и сверка старого владения. Paper-состояние нельзя переключать в live как готовое.

При разрыве связи локальная emergency-защита не закрывает позицию на сервере.
Сверяйте реальный счёт в штатном терминале, включая возможные поздние сделки;
до восстановления доказанных orders/fills/net новые отправки робота запрещены.
Пошаговая физическая квалификация DLL/сервера и разрешённых операций приведена
ниже; её граница задана P6 [плана](FINAM_TRANSAQ_PLAN.md). Она ещё не выполнена.


## Общий счёт: текущая граница

Другие роботы могут торговать тем же инструментом, но текущий ExternalNet
остаётся фиксированной декларацией. Изменение чужой доли блокирует новые sends
до актуализации и сверки; оно не становится собственным lot. Status отдельно
показывает Native/account gate, причину, own/declared external/expected/observed/
difference и age. Last reconciliation accepted — прежнее подтверждение, не
текущая готовность. Actual collateral held/pending/total относится к действующей
кампании; Preview reserve — к форме параметров. Owner-attested zero отличается
от account observation. PASS этого gate не отменяет quote/policy/funds checks.
Готовность получателя transfer теперь также зависит от текущего agreement.
Ручной импорт и непрерывная совместная торговля с произвольными другими роботами
ещё не реализованы; source/target разделены в
[THG-OWNERSHIP-008](SHARED_ACCOUNT_AND_REGISTRATION.md).

## P6: сценарий владельца для Standard union

**Статус всех этапов: NOT_RUN / REQUIRES OWNER-RUN.** Инструкция сама не разрешает
подключение или торговлю. Владелец заранее заполняет карточку запуска; секреты
в карточку и отчёт не включаются. Пустое обязательное поле означает, что этап
не готов к запуску. Разрешение read-only не распространяется на этап с заявками.

| Поле карточки | Что зафиксировать до запуска |
|---|---|
| Среда и владелец | Ответственный оператор и дата/окно сессии; владелец выбрал выделенный реальный счёт с балансом, demo не требуется. Точный счёт хранится у оператора, в отчёте только псевдоним |
| Подключение | Обычный TRANSAQ, Standard union, версии DLL/сервера и bitness; SHA DLL; автоматическая замена DLL запрещена |
| Инструмент | Один выбранный FUT, board/seccode, tick, lot, допустимое количество, единицы позиции и денежная валюта |
| Исходное состояние | Native и broker orders/fills/net; отдельный пустой таб/новое имя campaign; declared external net и отсутствие чужих ордеров в табе |
| Разрешённые эффекты | Отдельно: connect/read-only; затем точные send/cancel/close/reconnect/restart; использование второго таба/других роботов исключено |
| Пределы этапа с заявками | Максимальные количество/позиция/денежный риск, допустимая limit-цена, Capital/Collateral/Manual funds/DayLimit, длительность и deadline каждой операции |
| Наблюдения | Где оператор видит broker orders/fills и brokerref; только разрешённые поля N/T/V/client reference, quantity/status/time и итог сравнения, без raw XML и credentials |
| Завершение | Допустимый способ отмены/закрытия своей тестовой позиции, ответственный при потере связи и время контрольной сверки поздних fills |

Перед любым разрешённым запуском собрать текущее приложение из project/:
`dotnet build OsEngine/OsEngine.csproj`. Запускать именно эту сборку обычным GUI.
Версии/профиль карточки должны совпадать; неожиданные differences оставляют
compatibility NOT_PROVEN. Native Tester/Optimizer проверяется отдельным локальным
разрешением с выбранным dataset/периодом, без подключения к брокеру.

### Этап A — разрешённое read-only подключение

1. В новом роботе оставить `Regime=Off`, `Signed order capability selected=false`,
   `Accept coordination=false`. Не загружать campaign с обязательствами и не
   нажимать Start/Enter/Exit/Cancel/Emergency. Настроить единственный таб0 и
   профиль `Standard union`, затем выполнить разрешённое подключение.
2. Сверить выбранные `United_…`, FUT, tick/lot с карточкой и терминалом брокера.
   Задать ручные параметры плана и средства, `Use portfolio available funds=false`.
   Нажать `Apply configuration` и `Preview and status`: проверить геометрию,
   количество/обеспечение и денежные единицы. Заявок от этих действий быть не должно.
3. Сравнить фактический net и присутствие строк в счёте. После ручной проверки
   orders/fills/net установить `Owner verified account and orders`, выполнить
   `Reconcile native and account`. Ожидается accepted только при свежих quotes,
   account и совпадении net; missing row сама по себе не означает zero.
   Если оператор подтверждает отсутствие нулевой строки, действует обычный TTL;
   просрочку/исчезновение строки нельзя обходить повторением флага без проверки.
4. При отдельно разрешённом disconnect/reconnect убедиться, что прежние данные
   не восстанавливают readiness: нужны новая source generation и свежая сверка.
   Ошибки единиц/профиля/freshness — STOP и запись факта, не подбор параметров
   ради прохождения. На этапе A внешних order effects должно быть **0**.

На пустом read-only счёте **не проверены** brokerref echo, приём команды,
исполнение, отмена, replay orders и recovery открытых обязательств.

### Этап B — только отдельно разрешённые заявки

1. Начать после принятого A и заполненных лимитов/timeout/cleanup карточки.
   Этап требует отдельного разрешения на весь автоматический набор выбранного
   сценария: entry, обычные exit и cancel, включая отмену при Pause/Regime Off.
   При разрешении только send этот runbook не запускается. Проверить, что утверждённые grid quantities и ручной конверт ограничивают
   суммарную held + потенциально исполнимую entry exposure разрешённым объёмом.
   `Policy.MaxActions=1` ограничивает один проход, а не всю сессию; его одного
   недостаточно. Использовать согласованную limit-цену и минимальный разрешённый
   объём; market, zero и отрицательные реальные цены ради теста не посылать.
2. Только теперь включить `Signed order capability selected`, выполнить свежую
   Reconcile и `Start or resume`. Наблюдать один согласованный intent: native N
   не меняется на T; V связывается с тем же intent. Ожидаемый client reference —
   `F2` + intent.Id. В разрешённом источнике проверить точный echo в order/trade.
   Если поле недоступно для наблюдения, записать echo NOT_PROVEN; не включать
   полный authenticated XML log. Отдельную диагностику согласовать до продолжения.
3. При фактическом исполнении сверить quantity/price/trade ID, native position,
   allocation/Realized и account net. Частичное исполнение фиксировать только
   если оно произошло в пределах разрешённого объёма: отсутствие partial не PASS,
   а PARTIAL NOT_RUN. Не увеличивать объём ради получения partial.
4. Через `Pause entries` отозвать новые входы: команда, как и `Regime=Off`,
   также отменяет оставшиеся working entries и продолжает обычные exits.
   Затем `Cancel working orders` запросит отмену всех своих working orders;
   эти эффекты уже должны входить в разрешение этапа B. Принятый cancel request
   и `withdrawtime=0` не terminal. До фактической terminal отмены и известных
   trade details остаток остаётся reserved; дополнительный reported execution
   переводит intent в Unknown до соответствующих реальных fills.
5. Только при разрешённом recovery-сценарии отключить/восстановить соединение
   либо перезапустить процесс с сохранёнными checkpoint и native journal.
   Ожидается Reconciling без повторного submit. После появления поздних fills
   сравнить native/account/projection: количество и PnL учитываются один раз.
   Старый T не разрешает cancel в новом соединении. Неполный replay оставляет
   Unknown; `Resolve verified absent order` допустим только после проверки
   точного intent и всех fills, затем снова Reconcile. Не удалять файлы состояния.

На каждом шаге: deadline, unexpected send/duplicate, несовпадение IDs/net/единиц,
необъяснённый Unknown, потеря свежести или связи означают **STOP**. Выключить
новые входы; не повторять send. При недоступном транспорте проверять фактический
счёт и выполнять только заранее разрешённый cleanup в штатном терминале.
Локальная emergency-команда при потере связи не является серверной защитой.

### Завершение и итог

По заранее разрешённой процедуре отменить только свои тестовые working orders;
закрыть только разрешённый остаток собственной тестовой позиции. Проверить
terminal orders и все fills одновременно в broker terminal, native Journal и
campaign; затем свежий account net должен совпадать с исходным declared external
net. Выполнить контрольную сверку в срок карточки на случай late fills. Оставить
`Regime=Off`, выключить signed capability и отключить соединение только после
подтверждённого cleanup. При невозможности подтвердить flat/terminal записать
остаток и передать его ответственному оператору; успешным cleanup это не считать.
Сохранить checkpoint/journal; Delete/reset не использовать как способ закрытия.

В отчёте по каждому этапу записать PASS/FAIL/NOT_RUN, версии/профиль, наблюдаемые
факты и границы. Отдельно остаются NOT_PROVEN: ненаблюдавшиеся partial/replay/echo,
другая DLL/сервер/FORTS/HFT, неиспытанные цены, экономическая достаточность
обеспечения и прибыльность. Tester/Optimizer lifecycle: на отдельно разрешённом
локальном dataset выполнить start→stop→repeat, сравнить IDs/количество/PnL и
сброс replay-clock; в WPF проверить кнопки/status/параметры после перезапуска.
Отсутствие этих прогонов не заменяется успешной managed fixture.

## Hedge: отдельная сторона исполнения

`Grid.IsHedge`/`Replacement.IsHedge` задаются вручную, default false.
Условия сетки и Direction сохраняются, физические Buy/Sell инвертируются.
Логическая цель может закрыть hedge с убытком; статус показывает обе стороны.
Для смены режима нужно освободить позицию и пройти cancellation/reconciliation.
При hedge выключите `Policy.PreEntries` и `Policy.PreExits`: комбинация отклоняется.
Замена контракта сохраняет режим. Новые сохранения hedge используют schema3;
возврат к старому binary без поддержки3 не допускается. Подробности:
[THG-HEDGE-010](HEDGE_MODE.md). ExternalIncrease/Decrease относятся к фактической
позиции: покупка уменьшает short даже при logical Direction=Long.

## Произвольный список уровней

Задайте `Selected levels`, например `0,2`, и `Selected plan id` (пусто=активный).
CSV важнее `Selected level`; пустой CSV использует scalar, `-1` явно означает все.
`Enter selected levels` сохраняет прогресс, `Cancel pending selected entries`
удаляет только ещё не начатые входы. `Exit selected levels` фиксирует текущие lots.
`Set selected markup` и команды `Set selected entry enabled`/`Set selected exit enabled`
меняют одно свойство; `Apply selected level settings` — все три. Edit отменяет
и дожидается исходов всех текущих заявок кампании перед публикацией настроек.
`Cancel selected level orders` снимает целые заявки, включая все allocations
групповой заявки. Для запрета повторных обычных входов выключите entry уровня; для обычных
выходов — exit уровня. Emergency и voluntary reduction этими ordinary-флагами
не блокируются.
Inventory использует свой отдельный `Inventory levels`. Новый формат команд —
schema4. Полные guards, restart и отличия legacy: [THG-BATCH-011](BATCH_LEVELS.md).


## Удаление неработавшего пустого робота

`Policy.RemoveEmptyRobot` выключен по умолчанию. Вместе с `Policy.Trailing=true`
и положительным `Policy.EmptyStopMinutes` он разрешает запрос native owner после
паузы EmptyStop. Удаляется только собственный никогда не владевший объёмом и не
отправлявший заявки live робот, с известной историей отсутствия coordination.
Любые native позиции (даже закрытые), локальные stop-openers на обеих вкладках,
manual/config commands, неизвестное/устаревшее состояние счёта блокируют удаление.
История когда-либо включённых portfolio ролей сохраняется; выключение переключателя
не очищает её. Старый checkpoint без этой истории остаётся для ручного удаления.

Строка `Empty removal` в Preview and status показывает текущую причину отказа или
принятый запрос; queued ещё не означает удаление. Владелец проверяет повторно,
сохраняет Stopped, затем удаляет native robot/settings. Campaign JSON остаётся.
Tester/Optimizer сохраняют робот и результаты. Hot-update экземпляр без точной
регистрации у владельца автоматически не удаляется. Полный контракт и граница
проверки: [THG-EMPTY-012](EMPTY_REMOVAL.md).


## Приоритет обхода и ordinary limit-цены

`Policy.AscendingLevelPriority=true` выбирает сначала допустимые уровни с меньшей
ценой (входы и обычные выходы); ID и объём уровня не перестраиваются. Default false
сохраняет plan order для входов и book order для выходов. Это влияет на выбор
первых заявок при MaxActions/ограниченном бюджете, включая selected commands.

`Policy.QuoteOrdinaryLimits=true` использует для достигнутого обычного сигнала
Buy=Ask+ThresholdTicks*tick либо Sell=Bid-ThresholdTicks*tick по фактической стороне.
SlippageTicks в этой ветке не добавляется. Manual/funded/replacement/protective
и ещё не достигнутые preorders сохраняют прежние цены. Обе настройки выключены
по умолчанию, проходят cancel-confirm barrier и сохраняются в schema5. В status
видна **принятая** execution policy; pending не выдаётся за уже применённую.
[Контракт013](EXECUTION_OPTIONS.md) описывает точные границы и отличие от source.


## Range-команды и индивидуальные настройки уровней

Shift/Widen сохраняют вашу отдельную наценку и EntryEnabled/ExitEnabled по ordinal
ID, если число уровней не изменилось. Цена уровня при этом меняется по принятой
geometry. При изменении количества и наличии таких настроек команда отклоняется
с `Range change alters level count with custom controls`. Нужна явная перестройка
через Apply configuration и повторная проверка индивидуальных настроек; ручное
удаление checkpoint для этого не требуется. Отказ автоматического WidenWhenFlat
переводит adapter в Faulted; исправьте план/настройку и используйте штатную сверку
и запуск. Неподтверждённые заявки/старые lots при перестройке сохраняют прежний plan.
Точные границы: [THG-RANGE-014](RANGE_STATE.md).


## Ручная историческая приёмка

Пошаговые cases для SRU6 TXT/QSH, signed fixture, runtime-изменений, стопов,
ручной регистрации объёма, partial/cancel и restart находятся в
[THG-HISTORICAL-015](HISTORICAL_QUALIFICATION.md#ручные-test-cases). Исторический
Tester не заменяет управляемые partial-fill и persistent-restart сценарии.
