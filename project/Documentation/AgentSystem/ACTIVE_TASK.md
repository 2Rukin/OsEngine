# Authoritative active task state

**ID:** `TASK-SOLUTION-DETACHED-TABLES-SPEC-001`
**Статус:** `COMPLETE — DOCUMENTATION ONLY`
**Фаза:** `TERMINAL HANDOFF — IMPLEMENTATION NOT_STARTED`
**Ветка:** `docs/solution-table-layout` → публикация в `docs/order-flow-production-roadmap`
**Baseline HEAD:** `c5651f71c7dfb718b8a00b33e7fb4b8233ce86d7`
**Dirty entry:** clean; ветка создана от актуального origin.
**Completed transition IDs:** `SCOPE_ENTRY`, `CODE_INVENTORY`, `WRITE_SPEC`, `REVIEW_SPEC`, `VALIDATION_1`
**Next transition ID:** `OWNER_IMPLEMENTATION`

## Frozen scope

По запросу владельца оформить проверяемое задание на рефакторинг *всех*
пользовательских таблиц desktop-приложения OsEngine: открытие каждой таблицы
кнопкой в новом окне, все кнопки соответствующего экрана полностью доступны.
Конкретный воспроизводимый пример — Cloud Explorer, вкладка 2.6: кнопки
поиска и открытия исследований обрезаются таблицей параметров в строке
фиксированной высоты 220 px. Нужны единые требования, инвентаризация,
сохранение поведения, проверка на Windows и этапы реализации.

Разрешённые файлы: новая спецификация `project/Documentation/UI/`,
`DOCUMENTATION_MAP.md`, `OrderFlow/README.md`, `OrderFlow/TECHNICAL_DEBT.md`,
этот active snapshot и при необходимости `project/CONTEXT.md`.
Никаких изменений production-кода, тестов, пользовательских данных и
торгового поведения. В этой задаче — задание, а не реализация.
Commit и обычный fast-forward push в origin явно разрешены владельцем;
force push запрещён. Desktop/live/test-stand запуск не требуется.

## Verification status

Current code inspection: Cloud Explorer root `RowDefinition Height="220"`,
вкладка 2.6 содержит `ButtonPatternSearch`, `ButtonPatternOpen` и
`DataGridPatternOptions` в одном `DockPanel`; WPF таблицы также встречаются
в Order Flow/UpdateModule, WinForms `DataGridView` — в других подсистемах.
Полный UI inventory — первый этап будущей реализации, а не вывод текстового
поиска. Зарегистрирована глобальная target-спецификация, Cloud-пример
записан открытым долгом, current руководства не объявляют target реализованным.
Agent validator PASS 109/109; локальные Markdown ссылки PASS 41/41;
`git diff --check` PASS. Независимая semantic review: PRIMARY нашла
`DOC-TABLES-001` (TextBox разделы были ошибочно названы таблицами);
после исправления VALIDATION_1 — terminal CLEAN. Build/Windows UI:
NOT_RUN, только документ; observability/mode parity: NO CHANGE.

## Blockers

Нет blocker для постановки. Физическая приёмка окон относится к будущей
реализации. Коммит и FF push выполняются по явному разрешению владельца.

## Next action

В отдельной задаче реализовать охват из `UI-DETACHED-TABLES-001`, выполнить
проверку всех экранов на Windows и закрыть `TD-CLOUD-LAYOUT-001` по факту.
