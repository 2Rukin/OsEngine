# THG-F2-COVERAGE-001: покрытие статической спецификации Futures2

**Статус:** CURRENT STATIC COVERAGE — NOT EXECUTED TRADING TESTS.  
**Дата:** 2026-09-29. **Baseline/current HEAD:**
`088add98b728f8088fb18ff2e59c8d4113ad043c`.

## Что означает полнота здесь

Спецификация перечисляет разрешающие/запрещающие guards, состояние до/после,
порядок побочных действий, fallback/no-op/error branches для найденных
управляемых маршрутов Futures2 и связанных host/helper компонентов.
Она включает достижимые inherited Expirate/Rotate и альтернативный settings
workflow, а не только happy path одноинструментного builder.

Полнота конечного реестра переходов не равна перебору бесконечных комбинаций
цен/объёмов, доказательству всех потоковых interleavings либо квалификации
native broker/Lua. Внешние входы моделируются как события/guards; недоказанные
гарантии не заменяются предположениями. Ошибкоопасные source ветки сохранены
как факты, а proposed исправления отделены.

## Каноническое распределение

| Документ | Покрытые families |
|---|---|
| [THG-F2-TRANSITIONS-001](FUTURES2_TRANSITIONS.md) | M dispatcher; A Active; B BlockRules; L liquidation; Q quotes/shift; D daily budget |
| [THG-F2-LEVELS-001](FUTURES2_LEVELS_AND_ORDERS.md) | E immediate entry; P preorders; V ticket sizing; X exits/reentry; O slot/order/fill/cancel; Z manual; I multileg repair |
| [THG-F2-HOST-001](FUTURES2_HOST_AND_CALLBACKS.md) | H host; S schedule/connect; G grouping/money; C containers; K cancel/failure; R replay; T/F/U trailing |
| [THG-F2-SETTINGS-001](FUTURES2_RUNTIME_SETTINGS.md) | N rebuild; W runtime setters; J/API commands и quiet creation; SYNC lot checks/excludes; OPT candidate plans; PERSIST save/load; UI provenance и owner extension |
| [THG-F2-ROLLOVER-001](FUTURES2_EXPIRATION_AND_ROTATION.md) | EX/EB/EA/EC rollover; RT rotation controller |

ID уникален в пределах документа; общий ключ — document ID + row ID.
[transition-index.json](evidence/transition-index.json) содержит машинный
индекс этих строк и source anchors, не альтернативное изложение их семантики.
Таблицы с начальными параметрами и формулами дополнительно раскрывают
ветвление строк и не выдаются за отдельные выполненные тесты.

## Source inventory

| Область / exact view | Прочитанные изменяющие и решающие тела | Канонический результат |
|---|---|---|
| dti.cs StrategyManager | ctor; MakeStrategyDTF/GetLots; UpdateQuotes обе перегрузки, HJ/shift/widen; четыре price functions; GetStrategyReaction все switch cases; BlockRule gates; RegisterOrder/Trade; Flush/Clear; SetMarkup; GroupRegZones; Apply/Cancel/AfterExperate; RotateInit/reset; orientation/completion | TRANSITIONS, SETTINGS, ROLLOVER; план в THG-MATH-001 |
| dti.cs Zone | constructor; GetRatio/fullness/balance; entry/exit/immediate/pre/rotate/expiration; три integrity; RegisterOrder/Trade; GetSellPrice; SellAll; manual buy/sell/register/clear; expiration apply/cancel | LEVELS, ROLLOVER |
| dti.cs ZoneBasket | counts/fullness; balance; AllowBuy/Sell/Spend; Buy/Sell/PreBuy/PreSell; Expirate; loss comparison; register/clear; flush; apply/cancel/new-lots; quantity/GO helpers | LEVELS, ROLLOVER |
| dti.cs ZoneBasketInstrument / LotPart | count/flags; constructors; send registration/ack/fill; commissions/average/PnL; slot mutators; callback cancel; stale/23:55 cleanup; Fill/Clear/RegisterPart/SetLotsCount | LEVELS |
| dti.cs StrategyDTI / StrategyDTIDTF | direct/batch send, money gates, callback wrappers, SellAll/revoke/EnterBack, wrapped/direct Start/Stop, Choose/Adjust/Tune/Rotate settings, scoring setup, LotsIsGood/UpdateLotsCount/excludes/sync flags, manual zones, SetAddData, expiration wrapper completion, command interfaces включая no-op/throws | HOST, SETTINGS, ROLLOVER |
| dti.cs RotateViewModel / VIndividual | Start/Apply; Born/Cross/Mutation; candidate MakeStrategy/FillZones/ClearZones; общий ResetRange manager | SETTINGS; ResetRange в TRANSITIONS |
| dti.cs SettingsDTFViewModel / StrategyViewModel / detail/instrument VM | Initial fields/defaults/guards and MakeStrategy/Apply; runtime setters; command registrations and bodies; no-op handlers; commission/constant/average/local lots; rollover commands | SETTINGS |
| dti.cs BlockRule; Order_callback.cs HJ | BlockCondition/AllowSendOrder; HJ SetPrice/UpdateRange/AllowTrade/PriceInRange/history | TRANSITIONS; HJ construction in THG-MATH-001 |
| Order_callback.cs StrategyAbstract / OrderSendingContainer | LocalId, selector, serializable fields, ctor/ack/fill/cancel; TransactionInfo.IsCorrect | HOST |
| Order_callback.cs ClassicArbitrageController | ctor, Finished, minimum GO, endpoint links, volume fields | ROLLOVER |
| PilotFinanceSystem.cs UserPlatform | InitTimers/platform callbacks; MainTickLua, time guards/polling; start/stop/reconnect/night cleanup; order/trade/cancel buffers/replay; OnTrashOrder; ChangeSettings/Rotate; LoadUserData | HOST, SETTINGS |
| PilotFinanceSystem.cs transport / repository | Quik send/cancel/round/transaction reply/connect; Demo synchronous callbacks; platform construction routes; Emitent.Save/Load, RepositoryAssistant/BinaryRepository, NonActual IDs | HOST, SETTINGS |
| TralingStop.cs helper и VM | Все behavioral методы helper; numeric/timer/selection setters, commands, dynamic/actual stop, Update/Reaction, reset/rearm/Tune/load boundary | HOST; формулы в THG-MATH-001 |
| TradeHelp.cs | RotateBillMenu/RotateTo/target selection и host entry routes | SETTINGS, ROLLOVER |
| DTI.g.resources | Четыре allowlisted BAML entries; raw bytes/hash и presence ключевых строк через GetResourceData | SETTINGS и ui-bindings.json |

Список охватывает решения и mutations, а не утверждает смысл каждого
generated WPF member, formatting getter, report renderer или bytecode
Lua-инструкции. Helper `ZoneBasketInstrument.SellAll` без найденного caller,
пустые handlers и отсутствующий PreSendOrderEnable setter явно учтены как
нерабочие/неподтверждённые маршруты, а не выброшены из карты.

## Контрольные сценарии для реализации и source replay

Ни один сценарий ниже не выполнялся на vendor runtime. Ожидание — прочитанное
legacy поведение; исправленный OsEngine target может иметь иной явный contract.

| Case | Начальные условия / событие | Ожидание source specification | Переходы |
|---|---|---|---|
| SC01 | Старт long120/110/100, adjusted ask110 | Подходят110/120 без нового crossing; порядок по sorted zones | E02, A08–A13 |
| SC02 | Sell/Buy threshold0 и3 | Изменяется adjusted quote и момент inclusive trigger; disabled threshold игнорирует значение обычной ветки | A01, V01–V11 |
| SC03 | Budget immediate запрещает вход, preorders включены | Fallback может отправить PreBuy без нового budget guard | E05, P01–P08 |
| SC04 | ForbidLong=true, общий ForbidBuyes=false | Immediate long запрещён, fallback pre-entry может пройти | E02, P01 |
| SC05 | Percent1, Raw1, Zones1 | Три разные формулы d; Percent1 не1% цены | P01–P04 |
| SC06 | P10,q2,b3, liquidity=false | Buy ticket11 при gate5<10, slot capacity отдельно | V02, O01 |
| SC07 | Sender вернул−1 на immediate entry | Zone result100, pass category Buy/limit могут сработать без local order | E04, A10–A13 |
| SC08 | То же на PreBuy | Result остаётся отрицательным, возможен stale cleanup | P08, E06 |
| SC09 | Entry partial fill, оставшийся pending | Held часть может иметь exit; global liquidity может запретить новый проход | X01–X05, A05 |
| SC10 | Полный выход уровня | Слоты снова доступны; повторный вход при цене, без cooldown | X06, E02/E03 |
| SC11 | Duplicate частичного grouped fill без завершённого container | External ID ещё не запомнен, возможна повторная раздача | C04–C11 |
| SC12 | Fill завершил хотя бы один container | External ID записан, повторная grouped distribution пропущена | C05, C10 |
| SC13 | Fill раньше ack | Unmatched; replay trades перед orders может потребовать второй проход | C02, R05–R08 |
| SC14 | Grouped cancel полностью разобран, fallback false | Local mutation возможна без host success-save | C15, R06 |
| SC15 | Полный cancel затем поздний fill | Container Actual=false; обычный fallback/unmatched не гарантирует принятия | C07/C08, R05 |
| SC16 | Transport rejection dOrderNum0 | Через5s Count−1; grouped lots>0 guard не проходит | K11, C07 |
| SC17 | Старый unknown order age>=1min | OnTrashOrder может вернуть true без broker ack, local order забывается | K04, O11 |
| SC18 | Local order после23:55 его даты | Очистка не зависит от bool cancel | O12 |
| SC19 | Положительный fill qty0.5 дошёл ordinary fallback до совпавшей заявки | int0 slot helper не no-op, изменяет все подходящие slots; literal qty0 через wrapper отдельно SC49 | O04–O08, C13 |
| SC20 | Stop-zone с pending entry и held0 | SellAll на следующей реакции не может закрыть ещё не исполненное | A01, L01–L05 |
| SC21 | FreezeVolume препятствует первой зоне | break всего SellAll loop, не поиск следующей меньшей зоны | L02 |
| SC22 | Held0 при pending orders | Legacy completion возможен, target FlatConfirmed этим не доказан | C14, T11 |
| SC23 | Настройка основного диапазона отменена | Старый manager остаётся, Started после host stop не восстанавливается | N02–N11 |
| SC24 | N1 после валидного preview | Builder false, старые zones могут сохраниться и пройти AllowSave | N07 |
| SC25 | Одна selected zone и SetMarkup | Меняются все зоны; при двух selected только две | W02/W03 |
| SC26 | TP/OrderType изменён при working exit | Setter не делает подтверждённый cancel/reprice | W01 |
| SC27 | Enable PreSend в IsHedge | Runtime setter оставляет false; исходный cloned settings path отдельно | W10, N01 |
| SC28 | Quote обновилась при stopped | UpdateCurParameters/HJ/возможные сдвиги не требуют Started | H03, Q01–Q09 |
| SC29 | Gap rule при обоих Open values известны | В manager передаётся0 из Current−Current | A06, B01–B08 |
| SC30 | WidenRange, held0, pending entry существует | Нет общего pending guard; план способен сдвинуться без reprice старого order | Q06–Q08 |
| SC31 | Session=false или P ровно±100 у helper | Update оставляет старые поля; следующий Reaction может их использовать | T01/T02, H04 |
| SC32 | CurStep==ActualStep | Primary stop не срабатывает; deferred >=StopLoss отдельный | T06, F04 |
| SC33 | Stop helper затем deferred восстановление в том же проходе | SellAll может сразу смениться Active | T06, F01/F05 |
| SC34 | Clear helper после stop | Не сбрасывает cause/FreezeVolume/ForbidBuyes/selection | U05 |
| SC35 | H>=Target и Started=true | Отказ запуска, но event выдаётся | U01/U02 |
| SC36 | Пустой selection helper | P0, All(lots0)=true; Min/Max части не выполняются | T03/T11 |
| SC37 | TimeFrom==TimeTo, Allow и Disallow пересекаются | Весь день; matching Allow может перекрыть Disallow | S02/S05 |
| SC38 | MainTick получил transaction-error stop flag | Stop в конце после уже выполненных реакций | H09 |
| SC39 | Expirate и partially filled old zone | Начальный AllowExperate false; full/empty без pending допустимы | EX03/EX04 |
| SC40 | Expirate разрешён | Old close и new entry могут отправиться в одном вызове | EB04/EB05 |
| SC41 | Во время rollover state→SellAll | Replacement callback fallback больше не выбирается | EC04 |
| SC42 | CancelExpirate с new orders | Mapping/history удаляются без broker cancel | EC01 |
| SC43 | Rollover all completed | Остановка при mapping nonempty, shift/rebase/ticker apply, restore LastState | EB09–EB11, EA01–EA05 |
| SC44 | Rotate и computed orientation указывает на zero-plan zones | Destination Finished может быть true по q=P0 | RT06–RT08 |
| SC45 | Rotate controller существует, пришёл иной fill | GO accounting всё равно обновляется без проверки direction/state | RT17 |
| SC46 | Crash/restart после async Save return | Durable result неизвестен; Actual buffers пусты, actual bag восстановлен | PERSIST01–PERSIST05, R09 |
| SC47 | Demo synchronous ack/fill до возврата sender | Отдельная ordering model, нельзя считать live partial/cancel parity | G03, C02–C12 |
| SC48 | Переключение будущего whole-position TP | Owner extension; конфликтующие exits должны быть урегулированы, legacy oracle для этой новой функции отсутствует | W01 и OWNER REQUIREMENT |
| SC49 | Внешний fill qty ровно0 | Wrapper flag=true без manager/order match/slot mutations; post-success выполняется, включая completion при остальных условиях | C13/C14 |
| SC50 | replyCode!=3/5, order number ненулевой | Нет increment transaction errors; накопленный счётчик всё равно проверяется | K12 |
| SC51 | Expiration integrity: нет pending, old balance=false, new=true, state Expirating | Early return не срабатывает; Basket.Expirate получает new/new=true/true | EB02/EB03 |
| SC52 | EnterBack из Expirate/Rotate/SellAll | Active и Save; Started и существующие exits не меняются | API01 |
| SC53 | Public DTI StartStrategy/SopStrategy вызван напрямую | Меняется только flag; без wrapper Save/event. Фактический caller не установлен | API02, J01 |
| SC54 | Tune нового объекта, SpendDayLimit>0 и старый manager null | Исключение до установки нового manager; никакого успешного создания | API06 |
| SC55 | Tune существующего объекта, builder false после replacement | False не откатывает manager/Papers/поля, post-builder side effects остаются | API07–API12 |
| SC56 | Lua GetPositions вернул null | Count становится0 и IsAbsent=false; non-Lua missing path отличается | SYNC02/SYNC03 |
| SC57 | Lot mismatch, LotsIsGoodDisable=true | Внутренний m_LotsIsGood=false и flags очищены, return=true | SYNC09/SYNC10 |
| SC58 | ApplyIncorrect по ручному подтверждению | Изменяются Exclude/сверка; zone fills и broker position не изменяются | SYNC11/SYNC12 |
| SC59 | Candidate Born/Cross против Mutation rebuild | Первые synthetic FillZones после успеха; обычный Mutation rebuild без refill, но при Dead→Born успешный fallback тоже вызывает FillZones | OPT02–OPT06 |
| SC60 | ResetRange при недостаточном количестве зон одной стороны | Возможны partial plan writes и index exception; внешний UpdateUpDown скрывает exception | Q10/Q12 |

## Закрытые пробелы предыдущего обзора

Начальный вход теперь определён predicate текущей цены. Preorders раскрыты
до формул и fallback обходов. Резервирование раскрыто до отдельных slot flags
и literal `+pending`. Grouped dedup уточнён для partial/full. Найдены
unmatched replay и точный bool локальной отмены. Перенастройка разделена на
rebuild и немедленные setter-изменения. Helper раскрыт до порядка повторного
SellAll/revoke, reset и stale Update. Expirate/Rotate прослежены до apply,
cancel и callback-dispatch конфликтов. Whole-average TP отделён от общего
Markup и per-zone average. Наличие UI controls отделено от inherited API.
Отдельно раскрыты quiet creation/Tune, EnterBack/direct Start/Stop,
агрегированная сверка позиций и ручные Exclude, no-op/throws интерфейсы,
генетические candidate-планы и synthetic FillZones/ClearZones. Нулевой
внешний fill отделён от нулевого аргумента нижнего slot-mutator.

## Внешние границы, не подменяемые статической полнотой

| Граница | Что установлено / чего спецификация не обещает |
|---|---|
| Native QUIK/Lua/broker | Managed send/receive wrappers и guards описаны; фактическая доставка, порядок, исполнение/отмена и callback loss требуют runtime evidence |
| Конкретный проект пользователя | Actual settings/accounts/serialized positions не читались; исходные значения constructor не выдаются за его конфигурацию |
| UI | Presence BAML strings и тела команд проверены; full visual tree/binding runtime, visibility, DPI и licence-dependent display не запускались |
| Лицензирование | Вход Started имеет внешний AllowStartStrategy guard; алгоритм лицензирования и его обход не являются частью торговой спецификации |
| Persistence/threading | Код async save/replay найден; атомарность/durability/all-interleavings не доказаны |
| OsEngine | Новый робот отсутствует; перенос, режимы Tester/Optimizer/live, broker capability и экономический результат не квалифицированы |

Это не список неразобранных обычных переходов manager: это граница источника
и доступного evidence. Результаты independent review и финальные проверки
публикуются только в [THG-EVIDENCE-001](EVIDENCE.md).

## Дополнение target: signed-price

363 source-transition rows и SC01–SC60 остаются реестром оригинала.
Требование владельца о пяти знаках и отрицательных ценах расширяет target,
а не меняет их legacy oracle. Полный числовой контракт —
[THG-PRICE-001](PRICE_DOMAIN.md), новые acceptance-сценарии33–46 —
[THG-QUALIFICATION-001](QUALIFICATION.md). Arithmetic fixtures не заменяют
native signed/zero feed, order, recovery и mode-parity qualification.
