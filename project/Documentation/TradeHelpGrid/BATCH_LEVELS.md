# THG-BATCH-011: команды над выбранными уровнями

**Статус:** CURRENT IMPLEMENTATION — OFFLINE CHECKS PASSED, REVIEW CLEAN/CLEAN, LIVE NOT QUALIFIED.
**HEAD:** `088add98b728f8088fb18ff2e59c8d4113ad043c`. **Дата:** 2026-09-29.
Расширение [ADR-THG-002](ADR-0002_OSENGINE_IMPLEMENTATION.md);
результат проверки — [THG-BATCH-REVIEW-011](BATCH_REVIEW.md).

## Выбор и команды

`Selected plan id` выбирает принятый plan, пустое значение — активный.
`Selected levels` — CSV набора ID, например `0,2,5`. Непустой CSV имеет приоритет
над `Selected level`. При пустом CSV используется прежний scalar, `-1` явно
выбирает все уровни. CSV с одним ID означает только этот уровень. Дубликаты,
пустой элемент (`0,`), неизвестный ID или некорректное число отвергают весь выбор
до изменения команды/schema/заявок. Список копируется; дальнейшие UI-изменения
не меняют принятую команду. Исполнение следует принятой priority policy, не CSV: default — порядок уровней
плана; AscendingLevelPriority — по возрастанию price, без смены ID/volume
([THG-EXECUTION-OPTIONS-013](EXECUTION_OPTIONS.md)).
`Inventory levels` остаётся отдельным CSV с тем же resolver/fallback; он не
подхватывает `Selected levels`. Его экономику задаёт [Inventory009](INVENTORY_REGISTRATION.md).

| Команда Parameters | Действие |
|---|---|
| Enter selected levels | По одной попытке увеличения выбранных уровней активного стабильного плана |
| Cancel pending selected entries | Удалить ещё не начатые ручные входы; уже записанные/отправленные заявки сохраняются |
| Exit selected levels | Зафиксировать текущие собственные lot IDs выбранного плана/уровней и сопровождать их уменьшение |
| Apply selected level settings | Отложенно применить Selected markup, Selected entry enabled и Selected exit enabled |
| Set selected markup | Менять только наценку выбранных уровней |
| Set selected entry enabled / Set selected exit enabled | Менять только соответствующий флаг по выбранному boolean-параметру |
| Cancel selected level orders | Снять целые ещё исполнимые собственные intents, содержащие выбранные allocations |

Вход сохраняет direction/HJ/blocks/sessions/day/funds/native gates обычного
ручного режима. Пока очередной уровень заблокирован, он остаётся ожидающим;
MaxActions и бюджет ограничивают каждый проход. Уже заполненный допустимый уровень
не создаёт новую заявку. Второй входной batch не заменяет незавершённый: сначала
его отменяют отдельной командой. Подготовленная/идущая замена контракта запрещает
новый batch; политика reconfiguration также должна быть завершена.

Прогресс хранится как PlanId + оставшиеся уровни. Уровень снимается из списка
при записи intent (или если его ёмкость уже занята), до внешнего эффекта. Intent
и остаток batch сохраняются одним checkpoint перед native send. Partial fill,
отмена или рестарт не создают повторную попытку **этого batch** на обработанном
уровне. Обычная сетка после завершения команды может снова торговать им по своей
политике; это не повтор команды. GroupOrders может объединить совместимые выбранные
entries даже при MaxActions=1; unselected allocations не добавляются.

Новый plan ID отменяет оставшийся входной batch с видимой причиной. Policy-only
изменение, сохранившее plan ID, сохраняет batch. Emergency очищает будущие ручные
входы. Pause сохраняет их для явного возобновления. Ни одна отмена ожидающего batch
не отменяет его уже созданные native orders: для них есть cancel-команды.

Exit сразу фиксирует существующие lot IDs; новые последующие lots туда не входят.
Остаток после частичного закрытия/подтверждённой отмены закрывается существующим
manual-exit lifecycle, с сохранением native reservations и MaxActions. Другой
незавершённый ручной exit нельзя перезаписать. Ordinary exit gates сохраняются.
Edit использует прежний **общий** cancel/reconcile barrier кампании, затем меняет
только выбранные уровни и только заданные nullable-поля. Это не selective drain.

## Отмена, наблюдаемость и сохранение

Selected cancel фиксирует intent IDs на момент команды. Если заявка объединяет
уровни0 и2, выбор2 снимает **всю** заявку, включая allocation0. Частичная отмена
одной allocation у брокера не моделируется. Late fills учитываются; Unknown и
cancel-pending сохраняют резерв до установленного исхода. Команда не отключает
уровень и не запрещает будущую обычную постановку после отмены.
Статус показывает ожидающие уровни с PlanId, выбранные cancel IDs и состав
allocations каждой заявки. Состав команды после restart берётся из checkpoint.

Batch entry, explicit-list edit и selected cancellation требуют **schema4**.
Reader поддерживает1/2/3/4/5; старый reader1/2/3 отвергает4. Проверяются ссылки на
план/уровни/intents и конфликт нового batch с legacy ManualEntry. Прежние scalar
поля читаются для совместимости. Версия не понижается после завершения batch,
включения [hedge](HEDGE_MODE.md) или ручной inventory operation.
Legacy ManualExitLots уже умеет хранить snapshot выхода, поэтому новый exit
использует этот существующий формат без дополнительного поля выбора.

## Отношение к источнику и граница проверки

Source dti.cs SHA256
`523f17b7db8d9b3754f30375c4237ecc0d23c78af8739163da6930a26de6ed78`:
Buy/Sell selected24349/24337 фильтруют IsSelected, empty=no-op; batch6849/6955.
Source SetMarkup24570/11261 при0 или1 selected применяет markup **ко всем**,
Clear25213 использует только видимую страницу. Target использует явный единый
selector, singleton не расширяется молча до all. Это описанное отличие.
Source manual Buy/Sell bypass ordinary guards; current сохраняет guards.
Source batch группировал также exits; target закрывает native positions отдельно.
Добавление CSV не является доказательством полной source execution parity.
Source SetLots/average — фактический учёт; его не заменяет planned capacity edit.

Managed тесты проверяют selection atomicity, два непоследовательных уровня,
nullable edit/drain, частичные exits, entry progress/serialized recovery,
grouped cancel/late fill, schema и durable-before-send с fake IServer.
Physical crash/GUI/broker/full native Tester/Optimizer lifecycle не выполнялись.
Точные totals и независимые verdict — в review011; исходная полная задача ещё
не объявляется завершённой только по этому checkpoint.
