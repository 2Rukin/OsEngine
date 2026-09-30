# THG-MATH-001: математика, единицы и параметры

> Историческая спецификация и evidence baseline этапа исследования. Текущий
> implementation contract: [ADR-THG-002](ADR-0002_OSENGINE_IMPLEMENTATION.md);
> native команды: [THG-OPERATOR-002](OSENGINE_OPERATOR.md). Указания «будущий»/
> «код не менялся» ниже относятся к исходному исследованию, не к текущему diff.

**Статус:** STATIC LEGACY CONTRACT + EXPLICIT PROPOSED DIFFERENCES  
**Scope:** Futures2 основной reference; DTF и generic DTI явно отделены.

## Единицы

| Символ | Смысл |
|---|---|
| L, U | Заданные нижняя/верхняя границы активного диапазона в единицах котировки |
| N | Количество уровней выбранного направления; N >= 2 для Futures2 |
| M | Выделенные деньги в валюте расчёта бюджета |
| τ | Accuracy — шаг котировки |
| v | StepPrice — стоимость одного шага одного контракта |
| κ = v/τ | Денежная стоимость единицы котировки одного контракта |
| g | Заданный процент ГО; не фактическое брокерское ГО |
| F | Фиксированное количество контрактов уровня, если включено |
| D | Ширина внешней зоны в единицах стратегии; Futures2 — котировка |
| Pᵢ, qᵢ, rᵢ | Цена уровня, план контрактов, переносимый денежный остаток |

Для акций дополнительно учитываются бумаги в биржевом лоте. Для арбитража
единицы могут быть RUB корзины либо annualized % в Classic. Общая подпись
«рубли» для всех StrategyKinds неверна. Ни один приведённый денежный пример
не является расчётом маржи конкретной биржи.

## OBSERVED: Futures2 / DTI.MakeStrategyDTF

Ширина W = U − L. Внутренние границы менеджера:

| Направление | InternalLower | InternalUpper | Середина |
|---|---:|---:|---:|
| Long | L | U + W | U |
| Short | L − W | U | L |

Для i = 0…N−1 шаг Δ = W/(N−1). Активные цены до округления:
long `Pᵢ = U − iΔ`, short `Pᵢ = L + iΔ`. Создаются также N противоположных
зон с нулевым объёмом. Список затем сортируется по PlanPrice.

Округление legacy зависит одновременно от обеих симметричных цен очередной
пары: если обе <= 10 — `Math.Round(...,3)`; иначе если обе <= 100 — два знака;
иначе ноль знаков. Это **не округление к τ**. Дубли уровней и off-tick
результаты должны проверяться отдельно; production-контракт их запрещает.

Доля бюджета b = M/N. Для активного уровня расчётная цена одного контракта:

```text
cᵢ = Pᵢ × κ × g / 100
aᵢ = b + rᵢ₋₁,  r₋₁ = 0
qᵢ = trunc(aᵢ / cᵢ)
rᵢ = aᵢ − qᵢ × cᵢ
```

В unfixed режиме при положительных входах trunc равен floor. Это равные
денежные доли, а не одинаковые q. Метод возвращает false, если хотя бы один
активный q <= 0, но до этого уже меняет manager zones: не атомарный validate.

При enableFixLots && F>0 сначала рассчитываются доступные q и r по формуле выше.
Если доступно меньше F, результат q=0 и r=a. Иначе q заменяется на F,
**а остаток не пересчитывается** по F. Нельзя описать legacy fixed mode как
полный перенос всей неиспользованной суммы. Target fixed mode просто назначает
F каждому уровню и проверяет суммарную стоимость/резервы; скрытый перенос не
нужен. При F<=0 legacy не применяет fixed override и оставляет расчётный q.

UI-подбор N для single instrument в `GetMaxZonesCount` использует верхнюю
границу, ratio и минимальную партию (F либо 1):

```text
Nmax = trunc(M / (F × ratio × U × κ × g / 100))
```

Это ограничение количества доступных уровней по деньгам, не оптимизация
прибыльности. Оно не доказывает корректность ценового шага и внешних stops.
UI `SettingsDTFViewModel.ZoneWidth` делит W на N, а builder — на N−1;
показывать их как одну и ту же величину нельзя.

## OBSERVED: trigger и выход Futures2

При D != 0:

```text
SellLong(threshold) >= InternalUpper + D
OR SellShort(threshold) <= InternalLower − D
```

Следовательно, для long пороги равны `L−D` и `2U−L+D`, для short —
`2L−U−D` и `U+D`. Это не симметричные пороги относительно видимого `[L,U]`.
Threshold связан с Accuracy в DTF через `PriceCorrection`; конкретные
Bid/Ask выходные функции и orientation должны сохраниться при replay.
Нулевой D отключает проверку. Отрицательный D в условии не отклонён самим
trigger; target требует D > 0 и явную валидацию геометрии.

`DTI.Zone.GetSellPrice` в найденном пути вычисляет **аддитивный** выход:
long `B + SellValue`, short `B − SellValue`. `FromEnter` берёт фактическую
среднюю основу; `FromPlan` у long берёт max(средняя, plan), у short min,
с отдельной веткой fractional ratio. `ExitAfterAcross` может ограничить выход
серединой. Futures2 builder устанавливает ExitAfterAcross=false.

Наличие SellModes в UI не доказывает обработку процентов/числа зон здесь:
`SettingsDTFViewModel.MakeStrategy` передаёт Output как SellValue без найденного
пересчёта. Процентные и другие правила DTF ниже — другой модуль.

## OBSERVED: DTF / DTMF.MakeStrategy

DTF рассчитывает N кандидатов между L и U, затем long округляет вниз к τ,
short вверх. Без выравнивания исходных границ крайний уровень может выйти за
диапазон: L=100.05, U=101.05, τ=0.1 дают long-низ100.0 и short-верх101.1.
Clamping в этом builder отсутствует. Бюджет сначала переводится в условные ценовые единицы:
`B = M × 100/g × τ/v`, далее делится на N и распределяется по цене с
переносом остатка. Условное `GetOptimalZonesCount = trunc(B/(U×F))`.
Старый `CalcStrategy`/`PodobrZonesCount` имеет другой currency conversion;
его нельзя выдавать за UI-path `MakeStrategy`.

`Zone.GetSellPrice` реализует варианты:

| SellModes | Операция над основой B, знак + long / − short |
|---|---|
| Percent | B × (1 ± Output/100) |
| Roubles | B ± Output |
| Zones | B ± Output × денежная стоимость ширины зоны, затем промежуточное округление денежного результата к raw Accuracy: вверх long, вниз short |
| Manual | B × (1 ± индивидуальный SellPersent/100) |
| Point | Сырая котировка ± Output |
| ManualPoint | Сырая котировка ± индивидуальный SellPersent |
| ManualRub | Денежная основа ± индивидуальный SellPersent |

Денежная основа использует AveragePrice и AverageStepPrice; point-режимы —
AvPriceWithoutComiss. FromPlan ограничивает основу планом (max для long,
min для short). Общий денежный return округляется **вверх** с шагом 0.1
для обоих направлений, после промежуточного округления Zones. Например,
FromEnter average=100.5, v=100, τ=10, width=10, Output=1 дают B=1005,
long Zones сначала1105, затем1110. Это не доказанная универсальная биржевая
точность. В fill-учёте DTF AveragePrice
включает двойную расчётную комиссию, а AvPriceWithoutComiss её не включает.

## OBSERVED: общий арбитраж DTI

`MakeStrategy(N,S)` отличается от Futures2: C=(L+U)/2, Δ=(C−L)/N,
уровни `C−(i+1)Δ` и `C+(i+1)Δ`; всего 2N. Общая стоимость части основана
на сумме ГО ног × ratio × (1+GOCorrection/100), с усечением. Дробные
соотношения накапливаются и распределяются по уровням, HedgeThrough может
обнулить внутренние зоны. Эти формулы и multileg repair не применяются к
одному контракту без отдельного решения.

## Контрольные числовые примеры

Синтетический инструмент: τ=1, v=1, g=20%, M=1000, `[100,120]`, N=3,
ratio=1, D=5. Для long: уровни 120/110/100, расчётные стоимости 24/22/20,
q=13/16/16, расходы 312+352+320=984, финальный r=16. Для short:
100/110/120, q=16/15/14, расходы 320+330+336=986, остаток14.
Автоподбор Nmax для F=1 даёт41. Он может дать повторяющиеся округлённые
цены; это иллюстрирует, почему Nmax не является готовым допустимым планом.

Legacy Futures2 long internal range = `[100,140]`, пороги95/145;
short internal = `[80,120]`, пороги75/125. Равенство с порогом включает stop,
если именно соответствующая расчётная цена выхода достигла его.

Fixed F=2 на первом long уровне: affordable=13, legacy q=2, carry остаётся
64/3, хотя неиспользованные деньги после двух контрактов равны856/3.
Это контроль различия carry rules, не утверждение о потере денег на счёте.

## PROPOSED: допустимый production-план

По уточнённому требованию владельца target допускает отрицательные и нулевые
цены до пяти знаков. Канонические числовые правила, примеры и единицы находятся
в [THG-PRICE-001](PRICE_DOMAIN.md). Перед активацией: L<U, N>=2; τ,v,M и
положительное обеспечение C известны; границы кратны τ, tick slots достаточно,
объёмы в своём шаге. Stop расположен снаружи диапазона. L>0 больше не требуется.
Параметр g остаётся частью legacy reconstruction; target sizing использует C.
Integer-tick interpolation, округление signed цены, общая средняя, проценты
и денежный резерв определены только в THG-PRICE-001, без альтернативной формулы
в этом разделе. Старые OBSERVED формулы выше не изменены.

Фактический margin reserve рассчитывается по capability инструмента/connector,
а не автоматически приравнивается к legacy `P×κ×g/100`. Учитываются все
потенциальные entry, неизвестные заявки, комиссии, stress slippage, currency
conversion и margin changes. Максимальный бюджет — ограничение, не гарантия
доступности фондирования в будущем.

Для текущих открытых level quantities hᵢ и adverse stop S оценка ценового
убытка long: `sum(hᵢ × max(0,Entryᵢ−S) × κ)`; short зеркально.
Добавляются fees и stress execution costs, затем проверяется сценарий
исполнения всех pending entries. Gap может превысить эту оценку: stop price
не ограничивает фактический убыток. Применение требует валидных κ/currency.

## Каталог опций и перенос

| Семейство | Найденные параметры | Статус/production трактовка |
|---|---|---|
| План | Up/Down, ZonesCount, Money, GO, EnableFixLots, LotsCountInZoneFix | OBSERVED; versioned preview и полная валидация — PROPOSED |
| Внешняя зона | OutOfBoundValueInRouble | OBSERVED DTI; explicit units/bounds/stop latch — PROPOSED |
| Прибыль | SellValue, SellRule, ExitAfterAcross | OBSERVED DTI; не смешивать с семью DTF SellModes |
| Предварительная постановка | PreSendOrders, PreSendOrderPorog, PreSendOrdersPercent/Type | Точные формулы, fallback и неиспользуемые аргументы — THG-F2-LEVELS-001 |
| Порция обработки | RealizedZonesCountOnStep; EnabledLiquidity | OBSERVED; не равно числу fills/сек или broker rate limit |
| Направление/паузы | ForbidBuyes/Sells, ForbidLong/Short | OBSERVED; в UI писать «набор/сокращение» отдельно от Buy/Sell |
| Области запрета | UseBlockEnter, BlockEnterAlways, BlockEnterMin/Max; UseBlockExit/2 | OBSERVED DTI; emergency override в target |
| HJ | HJBuyEnabled/HJSellEnabled, HJFilter.AllowTrade | OBSERVED: запрет строго внутри активной области; построение области описано ниже |
| Удержание | FreezeVolume | OBSERVED legacy guard; единицы DTI ненадёжны, target emergency игнорирует |
| День/бюджет | UseSpendDayLimit, SpendDayLimit, TodaySpended; DTF CanSpend | OBSERVED; spending не равен daily realized loss |
| Работа с заявками | OrderType, EnabledOrderThreshold, OrderThreshold; grouping | OBSERVED; capability, identity и cancel barriers — target |
| Сдвиг диапазона | CorrectUpDownAfterExitNow, WidenRange; DTF IsUseAutoAdjust | OBSERVED; target только через preview/замену версии и сверку |
| Завершение | IsStopAfterExit, SellAll, RevokeSellAll | OBSERVED; нулевой inventory не означает отсутствие pending |
| Унаследованные режимы | Ratio/FracitonalRatio, HalfOrderDelay, PriorityInstrument, IsHedge, Expirate | Multileg отдельно от основного builder; достижимые Expirate/Rotate — THG-F2-ROLLOVER-001 |
| Portfolio trailing | TralingStopHelper: Step, Target/TargetStep, FromMax/From0, DynamicStop, MiniStop, таймеры, EqualizeVolumes/PieVolume, DeferredClosePos | OBSERVED отдельный portfolio helper; per-strategy property не доказывает его активацию |

Значения конструктора DTI: EnabledLiquidity=true, threshold включён со
значением3, RealizedZonesCountOnStep=1, CustomGOPerc=20, SellRule=FromPlan,
HalfOrderDelay fallback30s. D по умолчанию0, поэтому внешняя ликвидация
отключена до настройки. Это defaults кода, не прочитанные пользовательские
настройки и не рекомендуемые production значения.

Каталог перечисляет найденные свойства, а не обещает видимость всех controls
в Futures2. Начальные поля, direct BAML strings и inherited API разделены в
[THG-F2-SETTINGS-001](FUTURES2_RUNTIME_SETTINGS.md). Точные переходы постановки,
включая `P−held+pending` и служебную реакцию100, описаны в
[THG-F2-LEVELS-001](FUTURES2_LEVELS_AND_ORDERS.md).

## OBSERVED: HJ-фильтр

`SystemModule.HookeJeeves.HJ` в Order_callback.dll строит собственную сетку
вокруг начальной цены с заданной ZoneWidth. `SetPrice` отслеживает индекс
зоны, направление и глубину движения. При продолжении движения глубина
увеличивается через смещения `depth×(depth−1)/2 + 1`; при развороте
сбрасывается в1. `UpdateRange` активирует область только при depth>1,
границы выводит из последней зафиксированной зоны, глубины и направления,
добавляя одну ширину зоны с соответствующей стороны.

`AllowTrade(price)` запрещает **строго** `From < price < To`, когда Range.Active;
на границах разрешает. `PriceInRange` имеет другую верхнюю границу (`<= To`),
и подменять им AllowTrade нельзя. История сокращается до последних50 точек.
Это описание тела фильтра, не утверждение об оптимизационном алгоритме
Хука–Дживса по одному namespace. Target перенос требует causal replay
последовательностей направлений, границ и reset.

## OBSERVED: портфельный trailing helper

`TralingStop.Logic.TralingStopHelper` агрегирует стратегии, отмеченные
`StartStopList`. В `Update` вычисляет `z = trunc(sum(GetPrib) − sum(GetProsadka))`
в целое и `P = 100z/sum(GetMoney)` (при неположительном плане P=0).
Это координата helper, не независимо сверенный broker PnL. При P<=−100 либо
P>=100 метод возвращается без обновления остальных полей. Если не все
выбранные стратегии в SessionState, Update также возвращается.

H — максимум наблюдавшегося P. `CurStep`:

- FromMax: H−P.
- From0: H−P, если H<0 или H>=Target; иначе −P.

При обычных положительных настройках DynamicStop задаёт
`min(Step, max(MinDynamicStop, round(Step−H×Step/Target,2)))`;
при Target=0 вычислительная ветка не выполняется и начальное значение0
сохраняется до верхнего clamp. Без DynamicStop используется Step.
Если H>0 и H>=Target, ActualStep переключается на TargetStep.
Первичная реакция закрытия: **CurStep > ActualStep**, не >=.

Имеются MiniStop после времени от самого раннего входа выбранной группы
при `P < −MiniStopPercent`, StopByTime, остановка неактивных стратегий по
времени от самого раннего создания. Limited MiniStop проверяет окно меньше
Minutes+1 и имеет флаг деактивации. Target и Step defaults равны3,
MinDynamicStop=1, TargetStep=0.2; это defaults, не рекомендация.

После stop `EqualizeVolumes` назначает FreezeVolume по максимуму/минимуму
GetVolume выбранных стратегий и минимальному PieVolume. При выключении этой
опции FreezeVolume=0. Это унаследует ограничения единиц GetVolume соответствующей
стратегии. `DeferredClosePos` может отзывать SellAll и возвращать его снова
по сравнению P с Target−TargetStep либо CurStep со StopLoss; при возврате
после stop устанавливается ForbidBuyes. Подробная цепочка и несовместимость
с target emergency latch — в THG-LEGACY-001.
