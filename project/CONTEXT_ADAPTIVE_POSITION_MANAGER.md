# CONTEXT_ADAPTIVE_POSITION_MANAGER

Статус: реализация в процессе. Текущие компоненты, проверки и незавершённые
этапы зафиксированы в [APM-IMPLEMENTATION-001](docs/adaptive-position-manager/evidence/implementation.md).
Зарегистрированный робот прошёл синтетические native Tester и Optimizer IS/OOS
прогоны. ApmOptimizerStudy фиксирует ограниченный план и all-trials; перебор
выполняет штатный OptimizerExecutor. Native fill timing различается между режимами;
исторический untouched test, полная M1 и live qualification имеют отдельные gates.

[Начать здесь: Adaptive Position Manager](docs/adaptive-position-manager/README.md).

Текущий путь работы с реальной историей, Optimizer и ограничения поставки:
[локальный runbook](docs/adaptive-position-manager/16-release-readiness.md).
Book/OFI пока отдельный component без native dual-stream; AC pacing выключен
по умолчанию. Полная приёмка и все торговые режимы сохраняют ResearchOnly.

Комплект описывает обратимое управление размером одной направленной позиции: частичная разгрузка, повторный набор при откате, усреднение и сокращение ниже первоначального входа, адаптивные уровни, пропуск действий при импульсе, неизменяемая защитная граница, Tester и Optimizer.

Агенту: прочитать [правила проекта](AGENTS.md), затем [порядок заданий](docs/adaptive-position-manager/tasks/README.md). Нельзя объявлять реализацию завершённой по наличию этой документации. Критерии выпуска: [quality gates](docs/adaptive-position-manager/10-quality-gates.md).

Исходная база спецификации: `2Rukin/OsEngine`, `f54de33961d45f73319ae1c7313f2854bcb27398`.
Baseline текущей реализации указан в work record. Другие ветки и upstream не объединялись.
