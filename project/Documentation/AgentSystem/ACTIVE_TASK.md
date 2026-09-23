# Authoritative active task state

**ID:** `TASK-ORDER-FLOW-CLOUD-EXPLORER-V2-SPEC-001`
**Статус:** `COMPLETE`
**Фаза:** `TERMINAL`
**Ветка:** `docs/order-flow-production-roadmap`
**Baseline HEAD:** `40b79a1b3eca77e19d22905660b05067cff777d4`
**Dirty entry:** clean; no owner changes in this checkout.
**Completed transition IDs:** `SCOPE_ENTRY`, `SPECIFICATION`, `VALIDATION`, `PRIMARY`, `FIX`, `VALIDATION_1`, `TERMINAL`
**Next transition ID:** `AWAIT_NEW_TASK`

## Frozen scope

Составить полное техническое задание нового opt-in исследовательского режима Cloud:
полный каталог, фильтры, адаптивные параметры, эпизоды, anchored VWAP/σ,
экстремумы, исследовательские сигналы, UI, причинный реплей, артефакты,
проверки на SRU6.txt. Сохранить legacy Cloud 1/2 и существующее исследование
без изменения. Только документация: новый файл OrderFlow, DOCMAP-001, индекс
OrderFlow и этот snapshot. Не менять production/tests/build outputs, не добавлять
сырой файл тиков и не подключать брокера или Tester/live. Пользователь прямо
разрешил commit и обычный fast-forward push в эту же ветку.

## Verification status

Постановка сверена с current reader/Clouds/runbook и характеристиками локального
SRU6.txt: 3 130 667 строк, 174 даты, SHA-256 в самой постановке. Локальные
ссылки проверены; `git diff --check` PASS, agent validator 109/109 PASS.
Documentation PRIMARY: FIX, OF-CLOUD-V2-DOC-001..004. После точечных исправлений
VALIDATION_1 terminal CLEAN, все четыре finding закрыты. DOCMAP и README
регистрируют цель как TARGET, старое поведение не объявлено изменённым.
Documentation-only: build, runtime tests, Tester/live, торговые операции NOT_RUN.
OBSERVABILITY: NO CHANGE; MODE PARITY: NO CHANGE.

## Blockers

Нет для завершённого documentation scope. Будущая реализация нового режима
не выполнена и не заявляется проверенной.

## Next action

Документ готов к передаче агенту реализации. Пользователь разрешил итоговый
commit и обычный fast-forward push в docs/order-flow-production-roadmap;
commit/remote identity определяются Git. Ждать отдельной задачи реализации.
