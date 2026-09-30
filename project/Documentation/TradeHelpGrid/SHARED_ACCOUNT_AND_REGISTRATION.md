# THG-OWNERSHIP-008: общий счёт и ручной учёт объёма

**Статус:** CURRENT ACCOUNT DIAGNOSTICS + MANUAL INVENTORY — OFFLINE ONLY.
**Дата:** 2026-09-29. **HEAD:** `088add98b728f8088fb18ff2e59c8d4113ad043c`.
Владелец подтвердил: другие роботы могут торговать тем же инструментом на том же
счёте; для внешних испытаний выделен реальный счёт с балансом. Demo не требуется.
Это сведения для подготовки испытаний, а не разрешение на конкретный send/cancel.

## Current: сверка и наблюдаемость

Current [adapter](../../OsEngine/OsTrader/Grids/Futures2/Futures2NativeAdapter.cs)
по-прежнему проверяет `account net = own signed lots + declared ExternalNet`.
Чужой fill/изменение account net не создаёт и не уменьшает собственный lot.
ExternalNet фиксирован оператором и не является автоматической суммой всех
соседних роботов. Native Journal дополнительно сверяется с проекцией своих lots.

`Reconciled` хранит последний принятый результат явной сверки. Для текущего
решения используются `CanConfirmNativeState` и та же проверка непосредственно
перед send. В status отдельно показаны current native/account gate и его причина,
own/declared external/expected/observed/difference, возраст позиции и источник
нулевого остатка. Owner-attested missing zero не называется broker-reported zero.
Дополнительные quote/policy/capability/funds gates остаются самостоятельными.

Actual collateral показан как held + pending entries = total. Unknown и
CancelPending входят в pending; они не прибавляются к нему повторно. Preview
параметров отдельно показывает потенциальный план. У каждого intent видны
remaining и его entry collateral reserve; exit remaining означает количество,
резервирующее свои lots, а не дополнительное обеспечение для входа.

Start/resume и публикуемая готовность получателя transfer используют текущее
native/account agreement, а не один сохранённый Reconciled. Изменение причины
этой блокировки логируется один раз на переход при Pump; credentials/raw payload
и account routing identity не выводятся новым диагностическим методом.

Пример: A держит +2, B держит +3. При account+5 и ExternalNet(A)=+3 оба могут
быть сверены отдельно. После покупки B ещё одного контракта account+6:
A ожидает+5 и блокирует новые sends, B ожидает+6 и сохраняет соответствие.
Current не обеспечивает непрерывную автоматическую совместную торговлю с
произвольными соседями: external declaration A нужно явно актуализировать.
Подстановка ExternalNet=account−own автоматически скрыла бы любое расхождение.
Own+2 и external−2 допустимо дают account0; это не делает own inventory пустым.

## Source: смысл ручных операций

Источники: [уровни/регистрация](FUTURES2_LEVELS_AND_ORDERS.md),
[runtime настройки](FUTURES2_RUNTIME_SETTINGS.md),
[host](FUTURES2_HOST_AND_CALLBACKS.md). Exact source hash остаётся в
[evidence](EVIDENCE.md); приведённые методы подтверждены hash-bound derived dti.cs.

| Source операция | Смысл |
|---|---|
| RegisterZone / RegisterPart | Без pending заявки дополняет уровень до плановой ёмкости; средняя взвешивается по добавленному количеству и выбранной цене |
| GroupRegZones | Количество зон, не контрактов; каждая подходящая зона дополняется целиком |
| SetLotsCount | Целевое количество, не delta; уменьшение снимает владение без расчёта прибыли от продажи |
| UpdateAveragePrice | Меняет учётную среднюю, не количество и не исторические broker fills |
| Clear | Legacy удалял локальный учёт без broker effects; unsafe forgetting pending не переносится |
| ApplyIncorrect / Exclude | Меняет внешний исключённый объём; это отдельная операция от регистрации held |

Регистрация/снятие владения не создают биржевую сделку, комиссию, realized PnL или
дневной расход исполнения. Source zero sentinel для неизвестной средней не
пригоден для signed domain: наличие цены должно храниться отдельно от числа0.

## Current: ручная регистрация

В checkpoint009 добавлены явные Register/SetQuantity/SetBasis и отдельно
атрибутируемое ExternalExecution. Канонический контракт, native формат,
операторский порядок, ограничения replay и стадии сохранения описаны в
[THG-INVENTORY-009](INVENTORY_REGISTRATION.md). Native Position/Journal сохраняют
реальные orders/fills; локальное владение не создаёт фиктивный MyTrade.

Это не вводит общий реестр всех роботов. ExternalExecution reference уникален
в пределах campaign, а его принадлежность явно подтверждает оператор.
Автоматическая непрерывная совместная торговля с произвольными соседями остаётся
отдельным незавершённым направлением: current account equality не ослабляется.

## Evidence boundary

Current diagnostics: `ExternalOwnershipCases` проверяет два native/ledger
владельца общего synthetic account, ненулевой external, изменение соседа,
net0 при own>0, сохранение Unknown reserve, distinct stale/unknown/provenance,
read-only status и запрет transfer в заблокированного получателя.
384/384 offline assertions PASS (356 previous +28 new), включая кнопку Start,
RegimeOn и pending-resume при расхождении счёта. Final production solution
compile:0errors17warnings; промежуточный test-only incremental build:0errors1NU1900.
Это историческое evidence checkpoint008 не покрывает новые ручные операции.
Их managed проверки указаны в THG-INVENTORY-009; физический crash с брокером,
GUI и совместимость установленной DLL/server остаются NOT_RUN.
Обязательный независимый review записан в [review008](OWNERSHIP_REVIEW.md).
OBSERVABILITY: REQUIRED, current diagnostics implemented. MODE PARITY: REQUIRED;
синтетические live-adapter fixtures и прежние simulator regressions не заменяют
полный native/live qualification. Реальный тестовый счёт пока не подключался.
