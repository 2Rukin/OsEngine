# CONTEXT_ADAPTIVE_POSITION_MANAGER

Статус: спецификация нового продукта; торговый код этим комплектом не реализован.

[Начать здесь: Adaptive Position Manager](docs/adaptive-position-manager/README.md).

Комплект описывает обратимое управление размером одной направленной позиции: частичная разгрузка, повторный набор при откате, усреднение и сокращение ниже первоначального входа, адаптивные уровни, пропуск действий при импульсе, неизменяемая защитная граница, Tester и Optimizer.

Агенту: прочитать [правила проекта](AGENTS.md), затем [порядок заданий](docs/adaptive-position-manager/tasks/README.md). Нельзя объявлять реализацию завершённой по наличию этой документации. Критерии выпуска: [quality gates](docs/adaptive-position-manager/10-quality-gates.md).

База исследования: только `2Rukin/OsEngine`, `master` на `f54de33961d45f73319ae1c7313f2854bcb27398`. Ветка документации: `docs/adaptive-position-manager`. Другие ветки и upstream не объединялись.
