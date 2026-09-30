# THG-EVIDENCE-001: источники, anchors и проверка комплекта

**Статус:** CURRENT STATIC RESEARCH RECORD — NOT RUNTIME QUALIFICATION  
**Дата:** 2026-09-29. **OsEngine baseline:** `088add98b728f8088fb18ff2e59c8d4113ad043c`.

## Provenance

Исследована только локальная папка `C:/Users/Mi/.codex/TradeHelp4` и current
checkout OsEngine. Файлы поставки не изменялись. Allowlist исследованных
бинарников, SHA-256, размеры, версии инструментов и hashes производных views
находятся в [manifest.json](evidence/manifest.json).

`Strategyes/*.lf` имеют PE/MZ и .NET metadata; Lua-файл имеет заголовок
`1B 4C 75 61 51` (Lua5.1). Первоначальный поиск имён методов служил навигацией;
behavior claims основаны на последующем статическом чтении тел методов.
Содержимое secret-bearing файлов, account/config/database/log files не
открывалось. Имя стратегии в коде не доказывает использование её владельцем.

Для C# views использована локально установленная
`ICSharpCode.Decompiler 9.1.0.7988` из Visual Studio. Полный исходный код
бинарников не является частью этого репозитория. ILSpy — инструмент
статической декомпиляции; документация проекта доступна в
[официальном репозитории](https://github.com/icsharpcode/ILSpy).
Обращение к внешнему сайту касалось только инструмента, не поведения робота.

Первичная попытка установить ilspycmd через NuGet не удалась; скачивание
release ZIP также не удалось. Использована уже имеющаяся local library.
Сборка вспомогательного console reader в TEMP: exit0, 0warnings/0errors.
Это сборка инструмента анализа, не production build OsEngine.

## Воспроизведение

TEMP-root текущего исследования:
`C:/Users/Mi/AppData/Local/Temp/TradeHelp4-analysis/`.
Для дальнейшей проверки сначала сверить hashes исходных модулей с manifest.
Метод/class и binary hash — основные anchors; номера строк производного C#
зависят от версии декомпилятора и resolver configuration.

IL можно получить штатным `ildasm.exe` из SDK, без запуска торгового кода:

```powershell
$sourceFile = 'C:\Users\Mi\.codex\TradeHelp4\Strategyes\dti.lf'
Get-FileHash -LiteralPath $sourceFile -Algorithm SHA256
& 'C:\Program Files (x86)\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.8 Tools\ildasm.exe' $sourceFile /text /nobar /utf8 '/out=C:\Users\Mi\AppData\Local\Temp\dti.il'
```

Вспомогательный C# reader таргетировал net10.0, ссылался на локальную
`ICSharpCode.Decompiler.dll`, вызывал `CSharpDecompiler.DecompileWholeModuleAsString`
с `ThrowOnAssemblyResolveErrors=false`, записывал текст в TEMP. Ни
Assembly.Load для торгового модуля, ни его constructors/entry point не
вызывались. Использованные формы:

```csharp
DecompilerSettings settings = new DecompilerSettings();
settings.ThrowOnAssemblyResolveErrors = false;
CSharpDecompiler reader = new CSharpDecompiler(inputPath, settings);
File.WriteAllText(outputPath, reader.DecompileWholeModuleAsString());
```

Для `dtf-resolved.cs`, `TradeHelp.cs` и `TralingStop.cs` использован overload с
`UniversalAssemblyResolver(inputPath,false,null)` и
`AddSearchDirectory("C:/Users/Mi/.codex/TradeHelp4")`. Первые `dtf.cs`,
`dtm.cs`, `dti.cs`, `ts.cs`, `Order_callback.cs`, `PilotFinanceSystem.cs`
получены до добавления resolver. Unresolved type annotations в первых views
не означают автоматически неверное тело арифметического метода; там, где
тип/вызов не установлен, вывод ограничен. Для DTI enum значения 0/1
сопоставлены с OrderBargType.Long/Short в общем contract.

Производные C# views не компилировались как торговое приложение и не являются
оригинальными исходниками. IL dump — дополнительный способ перепроверить
спорные branches. Нельзя выдавать исправление декомпилятором структуры C# за
исправление binary behavior.

## Реестр содержательных anchors

Пути таблицы — имена файлов в TEMP-root. Lines — навигация внутри exact view.

| Claim | Artifact:line / symbol |
|---|---|
| Вариант Futures2 и видимые bounds | dti.cs:27366,27418 — DTI.StrategyDTIDTF |
| План Futures2, 2N зон, internal range, округление | dti.cs:10084 — DTI.StrategyManager.MakeStrategyDTF |
| Денежное распределение и fixed carry | dti.cs:10176 — StrategyManager.GetLots |
| Подбор количества уровней | dti.cs:10001,21279 — GetMaxZonesCount / OnSelectOptimalZonesCount |
| Общая арбитражная сетка | dti.cs:10202 — StrategyManager.MakeStrategy |
| Вызов реакции через direct/grouping | dti.cs:4922 — StrategyDTI.GetStrategyReaction |
| Trigger внешней зоны, filters, SellAll | dti.cs:10477,10535,10746 — StrategyManager.GetStrategyReaction |
| Единицы и threshold DTF | dti.cs:3503,3589,9393 — DBItem.SupplyInRouble/CallInRouble, PriceCorrection |
| Вход/выход уровня | dti.cs:12188,12360,12738 — Zone.GetZoneReactionBuy/Sell/GetSellPrice |
| Ликвидация по held minus pending exits | dti.cs:12855,13963,14023 — Zone.SellAll / ZoneBasket.Sell |
| FreezeVolume единицы | dti.cs:14872 — ZoneBasket.GetVolume |
| Локальная регистрация ack/fill | dti.cs:15275,15287,15334 — ZoneBasketInstrument.RegisterOrderBuy/Sell/RegisterOrder/RegisterTrade |
| Completion по нулевым lots | dti.cs:6238 — StrategyDTI.RegisterTrade |
| Сдвиг, stop-after-exit, дневной reset | dti.cs:9059,11108,11180,11844 — WidenRange, register trade, CheckCorrectUpDownAfterExit, CorrectUpDownAfterExitNow |
| UI ширина W/N и прямой SellValue | dti.cs:21068,21326 — SettingsDTFViewModel |
| DTI defaults | dti.cs:8769,7974,8059 — StrategyManager ctor, HalfOrderDelay, TodaySpended |
| Только property trailing | dti.cs:7836 — TralingStopEnabled |
| Portfolio trailing: расчёт/пороги/реакция | TralingStop.cs:1135,1338,1354,1817,1998 — TralingStop.Logic.TralingStopHelper |
| Порционное/отложенное закрытие | TralingStop.cs:1888,1904,1926 — HelperReaction: EqualizeVolumes, FreezeVolume, DeferredClosePos |
| Связь helper с grid state | PilotFinanceSystem.cs:7087; dti.cs:7075,7314,7554 — helper-before-grid; SellAll/ClosePositions/RevokeSellAll |
| HJ: область и строгие границы фильтра | Order_callback.cs:2990,3061,3149,3176 — SystemModule.HookeJeeves.HJ constructor/SetPrice/UpdateRange/AllowTrade |
| DTF рабочий builder и подбор N | dtf-resolved.cs:15077,15088 — DTMF.StrategyManager.GetOptimalZonesCount/MakeStrategy |
| DTF lot helper и округление | dtf-resolved.cs:12872,15973 — getLots / Zone.RoundPrice |
| DTF trigger, pre-send, auto-adjust | dtf-resolved.cs:13837 — StrategyManager.ToStrategy |
| DTF типы TP | dtf-resolved.cs:16599 — Zone.GetSellPrice |
| DTF резерв, fills, локальный dedup | dtf-resolved.cs:16138,16457,16689 — PriceToZone/RegisterTrade/RegisterOrder |
| DTF liquidation и stale cancel | dtf-resolved.cs:16762,16802 — CheckOldActiveOrders/SellAll |
| DTF default StopPrice и helper broker stop | dtf-resolved.cs:12651,14492,14799 — StopOrderPrice/SetStopOrders/CheckStopLoss |
| DTF SendOrder, Demo и grouping | dtf-resolved.cs:10005,10239,11630 — GetStrategyReaction/SendOrder |
| Доставка callbacks и local identities | Order_callback.cs:2716,2778,2795 — OrderSendingContainer |
| Group key и delegate | Order_callback.cs:2757,1196 — Key / OrderSendingPullSelector |
| Host readiness и периодическая реакция | PilotFinanceSystem.cs:7049,7134,7165 — UserPlatform.MainTickLua |
| Native wrapper отправки в QUIK | PilotFinanceSystem.cs:26415,27992 — QuikPlatform.SendOrder / Quik.SendOrder |
| Stock grid sizing и stop | dtm.cs:13123,13937,14835,14927 — getLots/ToStrategy/MakeStrategy/GetOptimalZonesCount |
| TouchScalp manual steps | ts.cs:727,1281,1541 — SetTSliderValue/CheckMarket/StrategyTypeName |

OsEngine code anchors находятся только в
[THG-INTEGRATION-001](OSENGINE_INTEGRATION.md), чтобы не дублировать карту.
Документационные claims legacy не подкрепляются ссылками на current OsEngine
как будто это одна реализация.

## Ограничения полноты

Подробно разобраны диапазон/размеры/выход/ликвидация Futures2 и важные
варианты DTF, portfolio trailing и основной HJ-filter; callbacks и native
integration исследованы в непосредственной boundary. Не выполнен полный
audit всей поставки, лицензирования, UI, Lua VM, всех арбитражных legs и сторонних connectors.
Состояние конкретного пользовательского экземпляра NOT_DETERMINED.

Последующее углубление Futures2 расширяет этот обзор до явного реестра
переходов manager/zone/host/helper/UI-команд и reachable inherited
Expirate/Rotate. Каноническая карта покрытия и точная граница статической
полноты — [THG-F2-COVERAGE-001](FUTURES2_COVERAGE.md). Не путать её с
production qualification либо аудитом всего приложения TradeHelp.

Доказательства экономической состоятельности, thread safety, broker protection,
exactly-once, disaster recovery и live parity отсутствуют. ADR обозначает
соответствующие требования как PROPOSED, а не найденные гарантии.

## Проверка исходного комплекта: TASK-TRADEHELP-GRID-DOCS-001

Итог на 2026-09-29; baseline и current HEAD совпадают:
`088add98b728f8088fb18ff2e59c8d4113ad043c`, ветка
`docs/order-flow-production-roadmap`. Изменены только этот комплект, его
регистрация в DOCMAP-001 и ACTIVE-TASK-001. Production/XML-doc изменений нет.

| Проверка | Фактический результат |
|---|---|
| SHA-256 исходных артефактов / производных C# views | 9/9 и 9/9 совпадают с manifest |
| Контрольная арифметика документа | 19/19 PASS; точные рациональные числа, не запуск legacy |
| Локальные ссылки девяти Markdown-документов | 25/25 существуют |
| JSON-приложения | 2/2 успешно разобраны |
| DOCMAP регистрации | 9/9 документов зарегистрированы |
| Offline agent-system acceptance | 110/110 PASS, exit0 |
| Whitespace и scope | git diff --check PASS; новые файлы отдельно проверены, scope соблюдён |
| Независимая документационная проверка | PRIMARY: 3 замечания; FIX выполнен; VALIDATION_1: CLEAN, 3/3 закрыты |
| Независимая production-safety проверка документации | PRIMARY: CLEAN; bounded VALIDATION_1 дополнений: CLEAN |

Закрытые замечания: THG-DOC-01 — выход DTF-уровней за диапазон после
округления; THG-DOC-02 — две стадии округления выхода Zones;
THG-DOC-03 — точное имя native `TrySetClosingOrders`. Дополнения portfolio
trailing/HJ проверены по телам методов. Открытых in-scope findings нет.

Frozen VALIDATION_1 packet находится в TEMP-root как
`validation-checkpoint.json`, SHA-256
`377b946be579646b5c4edb7f73901b50ff80355234e3cfd703b089468c0a9efb`;
reviewers подтвердили 11/11 hashes комплекта. Этот checkpoint предшествует
только добавлению настоящих итогов и обновлению task snapshot; он не
объявляется hash финальной версии EVIDENCE.md.

Команда валидатора из корня репозитория:
`bash .agents/validation/validate-agent-system.sh --baseline 088add98b728f8088fb18ff2e59c8d4113ad043c`.
Review проверяет точность документации и границы предлагаемых гарантий,
не безопасность отсутствующей реализации в реальной торговле.
Runtime scenarios из THG-QUALIFICATION-001 остаются NOT_RUN. Приложения,
connectors, stands и торговые операции не запускались; production build,
backtest и live qualification не выполнялись. Commit/push не выполнялись.

## Углубление переходов: TASK-TRADEHELP-FUTURES2-TRANSITIONS-002

Entry baseline/current HEAD прежний. На входе сохранены предыдущие dirty
документы и их hashes в TEMP `transitions-entry.json`, SHA-256
`0cb7bb452769442d3133b05420f665515e5cd0ac97e1855c962117034d0ec323`.
Предыдущий CLEAN verdict выше не переносится автоматически на расширение.

Прочитаны полные behavioral bodies зон, runtime settings, host callbacks,
trailing и достижимых rollover/controller paths. Три независимых read-only
research scopes использовались для zones, settings, callbacks/host/helper;
Main проверял manager и оформлял документы. Дословная операция `+pending`
дополнительно сверена с IL, а не исправлена по ожидаемому смыслу.

Для UI использован уже извлечённый ildasm ресурс DTI.g.resources и
`ResourceReader.GetResourceData` четырёх именованных entries. Не обращались
к Enumerator.Value, не выполняли десериализацию объектов, не загружали XAML
и vendor assembly. Hashes/raw lengths и substring-presence находятся в
[ui-bindings.json](evidence/ui-bindings.json). Это byte-string evidence, не
полное дерево BAML и не подтверждение доступности controls на экране.

Попытка получить source архив ILSpy для дополнительного BAML tooling была
отклонена средой на уровне socket; использован локальный пассивный resource
reader. Новый decompiler/торговое приложение не устанавливались и не
запускались. Источник инструментальной справки — тот же официальный ILSpy
repository, не внешний OsEngine upstream.

Машинный [transition-index.json](evidence/transition-index.json) содержит
IDs таблиц и проверенные source anchors. Он проверяет структуру/ссылки,
не исполняет ветки торгового модуля. Реестр содержит 363 строки переходов;
60 сценариев THG-F2-COVERAGE-001 — specification cases с ожидаемым legacy
результатом, не 60 PASS тестов.

PRIMARY checkpoint `transitions-primary.json` — 20 файлов, SHA-256
`435898541b13dcf6e08a4a2fc2026521d7a2caa98d1da630e48951d422a596fc`.
Documentation PRIMARY нашёл три замечания: неполный реестр alternative API,
type-remap ошибочно приписан обычному Load, смешаны early guard и аргументы
expiration repair. Safety PRIMARY нашёл два: нулевой fill смешан с int0
slot-mutator, пропущен dOrderNum==0 guard increment errors. Все пять приняты
и исправлены в документации; закрытие подтверждено в VALIDATION_1.

FIX дополнил bounded API/SYNC/OPT inventory, точный ResetRange и сценарии
SC49–SC60. Изменение production-кода для устранения найденных legacy-веток
не выполнялось. Их описание не означает принятия как target OsEngine.

VALIDATION_1 checkpoint `transitions-validation1.json`, SHA-256
`e8187d2031ab5f5915601901fec76fcc0be1702946cfb4bc8c27b479408713cb`:
исходные пять замечаний закрыты. В добавлениях найдены два residual:
THG-F2-DOC-04 — отсутствовал guard data.Started при создании;
THG-F2-DOC-05 — отсутствовал fallback Mutation→Dead→Born→FillZones.
Минимальный FIX уточнил API12 и его sequence, OPT05 и SC59; это основание
для конечной ограниченной VALIDATION_2, без расширения review scope.

Финальный проверенный checkpoint `transitions-validation2-review.json` —
20 файлов, SHA-256
`cea7bee23a3c09e699b5dcd17c0613ad9985a92c62c99f97e110bb1226addd74`.
В рамках TASK-TRADEHELP-FUTURES2-TRANSITIONS-002 после него изменены только
terminal evidence и ACTIVE_TASK; последующие задачи имеют отдельные
checkpoints и не наследуют этот verdict для нового scope.

| Проверка текущего расширения | Результат |
|---|---|
| Независимый documentation review | VALIDATION_2 CLEAN; THG-F2-DOC-01–05 закрыты, 5/5 |
| Независимый production-safety review документации | VALIDATION_2 CLEAN; THG-F2-SAFE-001/002 закрыты, 2/2; DOC04/05 также подтверждены |
| Source hashes / derived view hashes | 9/9 + 9/9 PASS |
| Frozen review packet hashes | Обе роли подтвердили 20/20 |
| Реестр переходов / сценарии | 363 / 60; структурный индекс и source specification, не execution PASS |
| Markdown / JSON / регистрации DOCMAP | 15 / 4 / 15, проверки PASS |
| Локальные ссылки | 60/60 PASS |
| Agent-system offline validator | 110/110 PASS, baseline 088add98b728f8088fb18ff2e59c8d4113ad043c |
| git diff --check / untracked Markdown whitespace | PASS / PASS |

Повторяемые команды из `project/`: `python <TEMP>/TradeHelp4-analysis/transition-evidence.py sources`,
`python <TEMP>/TradeHelp4-analysis/transition-evidence.py index`,
`python <TEMP>/TradeHelp4-analysis/transition-evidence.py check`, `git diff --check`.
Из repository root: `bash .agents/validation/validate-agent-system.sh --baseline 088add98b728f8088fb18ff2e59c8d4113ad043c`.
Hashes TEMP tools/artifacts зарегистрированы в manifest.transitionResearch.
Предыдущие 19 arithmetic cases остаются историческим evidence предыдущего
раздела, повторный запуск vendor runtime ими не заявляется.

Outcome: COMPLETED для статической спецификации. Открытых in-scope findings
и research blockers нет. Production/XML-doc/runtime change отсутствует;
OBSERVABILITY: NO CHANGE; MODE PARITY: NO CHANGE. Робот OsEngine не реализован
этой задачей. Build/backtest/connector/GUI/live NOT_RUN; внешние границы
описаны в THG-F2-COVERAGE-001. Commit/push отсутствуют; HEAD не изменён.

## Signed-price domain: TASK-THG-SIGNED-PRICE-003

Новый запрос владельца: цены до пяти знаков и отрицательные цены.
Целевой числовой домен расширен на0 и cross-zero диапазоны. Канонический
THG-PRICE-001 содержит OWNER REQUIREMENT и PROPOSED числовые решения;
legacy reconstruction не переписана под желаемое поведение. Все15 прежних
документов проверены по impact; теперь комплект16MD,5JSON и один arithmetic
checker. DOCMAP исправляет также устаревшее число48 на фактические60 legacy
сценариев; target qualification расширена с32 до46 сценариев.

Entry snapshot `signed-price-entry.json` в TEMP/TradeHelp4-analysis содержит
hashes21 прежнего файла (включая DOCMAP/ACTIVE), SHA-256
`0254b03db7037a05ec2890f20bf614d8b9492059fc0d00fbc0c71eb19cae8fe7`.
Baseline/current HEAD прежний `088add98b728f8088fb18ff2e59c8d4113ad043c`.
Production/native/binary файлы read-only; прежний dirty worktree сохранён.

Независимый codebase-researcher проверил ограниченные entry/add/exit/stop/
trailing/reprice/cancel пути BotTabSimple, Entity/Order/Security/Position,
PositionCreator, ConnectorCandles, общий AServer, Tester/Optimizer и emulator.
Exact anchors и current ограничения канонически находятся в
[THG-INTEGRATION-001](OSENGINE_INTEGRATION.md). В manifest.signedPriceResearch
зарегистрированы hashes прочитанных native файлов и арифметических artifacts.
Это не repository-wide review и не подтверждение биржевой допустимости.

Команда `python Documentation/TradeHelpGrid/evidence/check_signed_price_cases.py`
из project/ проверила **36/36** примеров target-арифметики. Использованы
точные рациональные числа и заданные expected literals, без вызова OsEngine,
vendor, сети или файлов настроек. Проверяются grid/rounding/обеспечение/fixed
lots/средняя/TP/PnL/процентная база и часть invalid inputs. Не проверяются
.NET overflow/locale/UI/native callbacks/Market/stop/persistence и реальные
сделки. Старые19 legacy arithmetic cases и363 source transitions остаются
отдельным историческим evidence, не результатом этого checker.

На checkpoint draft: links81/81, JSON5/5, DOCMAP16/16 и whitespace PASS.
PRIMARY checkpoint `signed-price-primary.json` —23 файла, SHA-256
`ad15d2d038c6fc710e873bcf4991ef23d76225f21a1d3a5095a7f1c9b9a613a7`.
Safety PRIMARY CLEAN. Documentation PRIMARY выявил THG-PRICE-DOC-01:
формулировка «строго внутри» противоречила включённым endpoints L/U.
Исправлена только эта строка на «в пределах [L,U]»; формулы, fixtures и native
claims неизменны. Ограниченная VALIDATION_1: обе роли CLEAN,
THG-PRICE-DOC-01 закрыта, открытых in-scope findings нет.

Проверенный FIX checkpoint `signed-price-validation1.json`, SHA-256
`420550dd71c64e5e528468bd1efeebad9dc69be2129a8eeb3373d890d8116994`:
обе роли подтвердили23/23 hashes. После этого изменены только terminal
bookkeeping в данном разделе и ACTIVE_TASK, новый review не требуется.

| Итог текущего signed-price scope | Evidence |
|---|---|
| Documentation review | VALIDATION_1 CLEAN, 1/1 замечание закрыто |
| Production-safety review документации | PRIMARY CLEAN; bounded VALIDATION_1 CLEAN |
| Числовые примеры proposal | 36/36 PASS; после wording-only FIX exact evidence переиспользовано |
| Native source hashes / arithmetic artifacts | 10/10 + 2/2 PASS |
| Legacy sources/views и неизменённый source index | 9/9 + 9/9 hashes;363 rows/60 source cases сохранены |
| Структура/ссылки | 16MD,5JSON,1checker;81/81 links,16/16 registrations PASS |
| Будущая qualification | 46 сценариев, NOT_RUN как integration/runtime matrix |
| Agent validator / whitespace | 110/110 PASS; git diff --check и untracked Markdown PASS |

Outcome: COMPLETED для обновления документации. Проверки не доказывают
готовность робота или сквозную signed/zero совместимость платформы.
Build/runtime/GUI/native tests/connector/live NOT_RUN: изменение только docs.
Production/XML-doc/observability/mode-parity implementation не менялись.
Новая signed-price execution и parity квалификация требуются при реализации.
Commit/push отсутствуют.
