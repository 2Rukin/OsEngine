# THG-F2-LEVELS-001: уровни, предварительные заявки и локальный учёт

**Статус:** STATIC SOURCE SPECIFICATION — OBSERVED, NOT IMPLEMENTED IN OSENGINE.  
**Source boundary:** `dti.cs` Zone/ZoneBasket/ZoneBasketInstrument, DBItem,
LotPart; hashes в [manifest](evidence/manifest.json).

Порядок внешних фильтров и лимитов задан в
[THG-F2-TRANSITIONS-001](FUTURES2_TRANSITIONS.md). Callback routing и реальный
смысл bool отмены — в [THG-F2-HOST-001](FUTURES2_HOST_AND_CALLBACKS.md).

## Представление состояния

Для ноги обозначим P=PlanLots, q=LotsCount, b=pending entry, s=pending exit,
r=ratio[ticker]. `LotPart` — слот одного планового лота, а не отдельная заявка.

| IsBuy | ActiveOrder | Состояние слота |
|---|---|---|
| false | false | Свободен |
| false | true | Зарезервирован под вход |
| true | false | Удерживаемый лот |
| true | true | Удерживаемый лот с ожидающим выходом |

P=count(slots), q=count(IsBuy), b=count(!IsBuy&&ActiveOrder),
s=count(IsBuy&&ActiveOrder). PartIsFull проверяет count(IsBuy||ActiveOrder)==P:
pending entry уже делает слот полным. ZoneIsFull требует полноты всех ног.
Списки Num_OrderBuy/Num_OrderSell и список Num_Trades хранятся отдельно;
связи slot→конкретный order в mutators нет (`dti.cs:3866,15018,15261`).

BalanceIsCorrect проверяет `(q+b−s)%r==0` и равенство `(q+b−s)/r` между
ногами. Вариант withoutActiveOrders дополнительно требует b=s=0. Для одной
ноги с r=1 обычный balance всегда проходит; это не broker reconciliation
(`dti.cs:13237`).

## E: обычный вход

| ID | Guard / событие по порядку | Изменение и результат | Anchor |
|---|---|---|---|
| E01 | ZoneIsFull | При !PreSendOrders и одной ноге проверить stale orders; return−1 | dti.cs:12188 |
| E02 | Long, !ForbidLong, adjusted buy<=PlanPrice, P>0 | Проверить balance и, если включён, AllowSpend; при разрешении вызвать Basket.Buy | dti.cs:12215 |
| E03 | Short, !ForbidShort, adjusted buy>=PlanPrice, P>0 | Симметричная проверка balance/budget и Basket.Buy | dti.cs:12231 |
| E04 | Вызван immediate Buy по E02/E03 | При true установить IntegrityNeedBuy=true; вернуть служебную реакцию100 независимо от bool Buy | dti.cs:12221 |
| E05 | Immediate ветка не дала100 | Перейти к проверке PreBuy, включая случаи отказа направления, budget или balance | dti.cs:12249 |
| E06 | После всех попыток result<0 и одна нога | Проверить stale timeout; здесь PreSendOrders=true не запрещает cleanup | dti.cs:12293 |

Нового пересечения ждать не нужно. При старте long сетки120/110/100 и
adjusted ask110 допустимы уровни110 и120; sorted list рассматривает110 раньше120.
Вне предварительного режима отправляется текущая направленная котировка с
threshold, а не автоматически PlanPrice. Если включён глобальный liquidity
gate, pending первой заявки способен заблокировать следующие проходы.

## P: предварительная постановка

Расстояние d берётся буквально из ветки:

| PreSendOrdersType | Формула d |
|---|---|
| Percent | abs(currentPrice)×(1−PreSendOrdersPercent/100) |
| Raw | PreSendOrdersPercent |
| Zones | ZoneWidth×PreSendOrdersPercent |

Percent1 означает99% абсолютной цены, а не1%. Это наблюдаемый код, не
предлагаемая трактовка интерфейса. Условие Percent>0 относится ко всем трём
типам. Отрицательный результат d при Percent>100 отдельно не отбрасывается.

| ID | Guard | Изменение / отсутствие действия | Anchor |
|---|---|---|---|
| P01 | E05, PreSendOrders, Percent>0, ровно одна нога | Рассчитать d; long допущен при buy<=PlanPrice+d, short при buy>=PlanPrice−d, P>0 | dti.cs:12249 |
| P02 | P01 допущен | PreBuy по PlanPrice/r; для Stock при Lot>0 дополнительно /Lot; result100 только при true | dti.cs:12269 |
| P03 | q>0, balance, обычный TP не дал100, PreSendOrders, Percent>0 | Long pre-exit при sell>=T−d, short при sell<=T+d, P>0 | dti.cs:12405 |
| P04 | P03 допущен | PreSell по T/r и дополнительному /Lot для Stock; метод возвращает false при числе ног!=1 | dti.cs:14125 |
| P05 | Вход PreBuy и q+b<P | Объём P−q+b; исходный ticket LimOrder, без ordinary liquidity/budget/AllowBuy guards | dti.cs:14222 |
| P06 | Вход PreSell и q−s>0 | Объём q−s, исходный ticket LimOrder, без ordinary AllowSell guard | dti.cs:14125 |
| P07 | priority задан и не совпадает с единственным инструментом | Ticket меняется на Market; выбранный manager OrderType здесь не определяет исходный тип | dti.cs:14125 |
| P08 | sendOrder вернул ID>0 | Зарегистрировать local order/pending; PreBuy/PreSell не меняют IntegrityNeedBuy; иначе false без регистрации | dti.cs:14222 |

Внешние ForbidBuyes/ForbidSells, BlockEnter/Exit, HJ и manager liquidity
по-прежнему действуют перед вызовом зоны. Но P01 не перепроверяет
ForbidLong/Short, дневной budget и balance: эти условия E02/E03 можно обойти
fallback-веткой. Это не универсальное разрешение обходить любой фильтр.

PreBuy/PreSell не заполняют OrderInfo.ZoneIndex. `PreSendOrderPorog` и
`preSendOrderEnable`, переданные обычным Basket.Buy/Sell, в их телах не
читаются. У CheckZoneIntegrity аналогично не участвуют в ветвлении
PreSendOrderOtskokPerc/priceWithGo/preSendOrderEnable.

## V: создание обычного ticket

| ID | Guard / операция | Правило и результат | Anchor |
|---|---|---|---|
| V01 | Basket.Buy | AllowBuy требует ненулевую цену входа каждой существующей ноги; q+b>=P пропускает ногу | dti.cs:13382 |
| V02 | Входная нога допущена | Исходный объём **P−q+b**, а не P−q−b | dti.cs:13445 |
| V03 | Buy, EnabledLiquidity, balance | При объёме>=r ограничить до r | dti.cs:13445 |
| V04 | Buy, EnabledLiquidity, !balance | r×maxⱼ ceil((qⱼ+bⱼ)/rⱼ)−q+b, сверху ограничить исходным объёмом | dti.cs:13445 |
| V05 | Basket.Sell | AllowSell проверяет ненулевую цену только ног с q−s>0; исходный объём q−s | dti.cs:13963 |
| V06 | Sell, EnabledLiquidity, balance | Ограничить до r | dti.cs:13963 |
| V07 | Sell, EnabledLiquidity, !balance | q−r×minⱼ floor((qⱼ+bⱼ)/rⱼ)−s, снизу0 | dti.cs:13963 |
| V08 | volumeControl=true | Дополнительная проверка доступного book volume; обычный Active передаёт false | dti.cs:13445 |
| V09 | priority задан | Если у priority ноги есть кандидат, оставить её одну; иначе оставшиеся tickets сделать Market | dti.cs:13445 |
| V10 | Отправка | ID<=0 не создаёт order/pending; Sell может вызвать sender с получившимся0, но регистрирует только ID>0 && lots>0 | dti.cs:13963 |
| V11 | AllowSpend | Повторить Buy sizing и liquidity cap; отказ только TodaySpended+estimate>limit; равенство разрешено | dti.cs:13847 |

Для futures estimate использует lots×GO_buy, для cash types lots×price×Lot.
Это другой расчёт, чем распределение исходного бюджета по CustomGOPerc.
`+b` в V02 подтверждён IL: `dti.il:72610` содержит последовательность
PlanLots, LotsCount, sub, SendedBillBuyCount, add. Пример P10,q2,b3 даёт11,
хотя gate2+3<10 проходит; pending slots и ticket quantity способны разойтись.

При hedge=false обычные цены DBItem: long entry=Ask+kτ, exit=Bid−kτ;
short entry=Bid−kτ, exit=Ask+kτ. При нужной quote либо её InRouble представлении0
цена0. Hedge меняет сторону. Из OrderType сохраняется Market, остальные
ordinary cases создают LimOrder. Исключения lower sender здесь не перехватываются
(`dti.cs:3701,3738,13445,13963`).

## X: выход и повторный вход

| ID | Guard / событие | Изменение и результат | Anchor |
|---|---|---|---|
| X01 | q<=0 либо !BalanceIsCorrect | Обычного выхода нет | dti.cs:12360 |
| X02 | q>0 и balance | Рассчитать T по GetSellPrice: FromEnter average±SellValue; FromPlan long max(average,plan)+SellValue, short min(average,plan)−SellValue | dti.cs:12738 |
| X03 | HasFracitonalRatio | Использовать average вместо plan basis; ExitAfterAcross при включении ограничивает long T сверху Middle, short снизу Middle | dti.cs:12738 |
| X04 | Long sell>=T либо Short sell<=T | Вызвать Basket.Sell; true снимает IntegrityNeedBuy; реакция100 независимо от send success | dti.cs:12383 |
| X05 | X04 не дал100 | Optional P03/P04; если result<0, !PreSendOrders и одна нога — stale flush | dti.cs:12405 |
| X06 | Все held лоты зоны проданы | Свободные слоты снова пригодны для E02/E03. Локального one-shot/cooldown нет | dti.cs:15680 |

Выход удерживаемой части возможен при ещё pending входе: обычный balance
учитывает b/s, а объём выхода ограничен q−s. Ограничение action-kind одного
прохода не означает запрета сосуществования entry/exit orders между проходами.
Средняя здесь относится к зоне; whole-strategy GetAveragePrice не является
автоматическим источником T.

## O: local order, fill и отмена

| ID | Событие / guard | Мутация / результат | Anchor |
|---|---|---|---|
| O01 | Положительный send entry ID | SetActiveBuyOrder(qty) на свободных слотах; добавить FuturesOrder с mode=ID, Value=qty, ODate=DateTime.Now, float Price | dti.cs:15275 |
| O02 | Положительный send exit ID и qty>0 | SetActiveSellOrder(qty) на held без pending; добавить order | dti.cs:15281 |
| O03 | Order callback совпал с ID-mode order | Заменить временный ID на broker number и mode=OrderNum, только первое совпадение | dti.cs:15287 |
| O04 | Entry fill: совпал текущий order number и local tradeID ещё нет | Добавить ID; пересчитать среднюю; AddLots(int(qty)); Order.Value−=qty; при <=0 удалить order | dti.cs:15334 |
| O05 | Exit fill с теми же guards | Рассчитать PnL/ProfitCache; добавить tradeID; RemoveLots(int(qty)); уменьшить/удалить order | dti.cs:15334 |
| O06 | Нет order match или local tradeID уже есть | false без изменения этой ноги; host unmatched/replay отдельно | dti.cs:15334 |
| O07 | Cancel callback нашёл ID либо number | При qty<0 использовать сохранённый Order.Value; снять N резервных flags стороны; Order.Value не уменьшается | dti.cs:15820 |
| O08 | После O07 суммарный pending стороны==0 | Очистить весь order list стороны; иначе конкретный order может остаться со старым Value | dti.cs:15820 |
| O09 | После O07 требуется flushMoney | Для entry только ID-mode вернуть сохранённый ReservedMoney; manager корректирует TodaySpended | dti.cs:15820 |
| O10 | Возраст старейшего order >HalfOrderDelay | FlushActiveOrders для всех local orders buy/sell, не только самого старого | dti.cs:16173 |
| O11 | Flush delegate вернул true | Снять local pending и сразу удалить order; true не равен подтверждению биржи | dti.cs:15787 |
| O12 | Daily clear после23:55 даты создания order | Вызвать delegate, если он есть; снять local pending/удалить order независимо от bool | dti.cs:15735 |
| O13 | Trade принят и balance без active orders восстановлен | Очистить ProfitCache зоны | dti.cs:12632 |

Проверки qty в slot-mutators имеют важные пограничные ветки. Счётчик
увеличивается после изменения слота; break только при counter==requested.
Поэтому requested0 меняет **все** подходящие слоты, а не делает no-op.
Положительная decimal qty меньше1, дошедшая через ordinary fallback до
совпавшей заявки, превращается в int0. Внешний fill с qty ровно0 отличается:
StrategyDTI.RegisterTrade устанавливает flag=true и пропускает manager,
поэтому эти slot-mutators не вызываются (`dti.cs:6219`). Cancel callback
идёт другим путём и такого общего bypass не имеет. При qty больше доступных
слотов средняя и Order.Value меняются на полную qty, но flags — только у
имеющихся слотов. Это описание входов вне нормальной integer lot-domain,
не утверждение, что брокер обязательно присылает такие callbacks.

AddLots/RemoveLots не привязывают слот к исполняемой заявке. Удалённая локально
order не распознаёт поздний fill в этой ноге; host может сохранить его как
unmatched. Num_Trades общий для buy/sell ноги данной зоны, не для всего счёта.

Средняя entry включает двойную комиссию:
`Anew=(Aold×q + fillPrice×fillQty + 2×otchisl)/(q+fillQty)`.
GetOtchisl/ComputeComiss имеют две commission-компоненты, conversion step/tick,
sign по Short/hedge. Отдельная AvPriceWithoutComiss участвует в realized PnL;
его exit branch использует первую Format/Value и вычитает свою двойную
комиссию. Нельзя объявлять эти два расчёта идентичными
(`dti.cs:15334,15466,15493`).

## Z: ручные изменения зоны

| ID | Команда / guard | Эффект | Anchor |
|---|---|---|---|
| Z01 | Zone.SellAll из manager | Basket.Sell q−s, preSend=false, volumeControl=false; cancel-all barrier отсутствует | dti.cs:12855 |
| Z02 | SellZone / BuyZone | Принудительная Basket.Sell/Buy, EnabledLiquidity=false, volumeControl=true, LimOrder; локальные TP/HJ/block guards не проверяются | dti.cs:12923 |
| Z03 | RegisterZone при b=s=0 | Заполнить недостающие local слоты через RegisterPart по текущей цене либо заданной unified price; это не broker order | dti.cs:12901 |
| Z04 | ClearZone без pending | Очистить local slots | dti.cs:12911 |
| Z05 | ClearZone с pending | Сначала FlushAll, затем безусловный Clear и предупреждение; отсутствие подтверждения не препятствует local clear | dti.cs:12911 |
| Z06 | Instrument.Clear | Сбросить held/pending/средние; Num_OrderBuy/Sell, Num_Trades и Profit этим методом не очищаются | dti.cs:16000 |
| Z07 | SetLotsCount | Добавить не больше свободной ёмкости; убрать не больше held, только без ActiveOrder; при pending sell фактический результат может отличаться | dti.cs:16258 |

Отдельный `ZoneBasketInstrument.SellAll` (`dti.cs:15969`) сначала отменяет
orders старше15s, требует b=s=0 и отправляет q по bid−tick/ask+tick. Caller
этого метода в исследованном dti.cs не найден. Реальный Z01 проходит через
ZoneBasket.Sell и не наследует его15s/barrier поведение.

## I: многоногая целостность

Active вызывает integrity только при Instruments.Count>1. Таблица сохраняет
унаследованные ветки, но не делает их обычным одноинструментным Futures2.

| ID | Guard / событие | Действие | Anchor |
|---|---|---|---|
| I01 | 0<q<P, b=s=0 | Проверить balance; при balance && EnabledLiquidity return−1 без stale flush | dti.cs:12462 |
| I02 | I01, DirectionalIntegrity | IntegrityNeedBuy выбирает Buy, иначе Sell | dti.cs:12462 |
| I03 | I01, !DirectionalIntegrity | LB=GetLossFromBuy; LS=GetLossFromSell−ProfitCache; при LS>0 && LB<2×LS выбрать Buy, иначе Sell | dti.cs:12462 |
| I04 | Repair send | LimOrder, preSend=false, threshold от Active0; успешный Buy добавляет spended, Sell вычитает; в конце stale flush | dti.cs:12462 |

Экспирация и rotation изменяют состав инструмента и рассматриваются отдельно
в THG-F2-SETTINGS-001; их zone-level ветки включены в coverage, даже если
основной range plan содержит только одну ногу.

## Дополнение target: signed-price

Source P в формулах количества означает PlanLots: требование P>0 не
запрещает отрицательную цену уровня. Новые правила signed entry/TP,
обеспечения, средней при partial fills и запрета обхода directional flags
через preorders находятся в [THG-PRICE-001](PRICE_DOMAIN.md).
Literal legacy rounding/storage и preorder formulas выше не объявляются
совместимыми с пятью знаками и нулевой ценой.
