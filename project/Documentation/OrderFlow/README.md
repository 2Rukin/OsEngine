# ORDER-FLOW-INDEX-001: документация Order Flow

Комплект документации исследования дельты и реакции цены по локальной ленте
сделок, визуализации Cloud и будущего внутридневного робота.

Текущий статус: **изолированный offline Research MVP реализован в OsData;
торговый робот, Tester execution и экономическое преимущество не реализованы и
не доказаны**. Пошаговая работа с отдельным окном описана в
[руководстве Cloud Explorer](CLOUD_EXPLORER_USER_GUIDE.md), а полная граница
доступной функции — в [runbook первичной проверки](RESEARCH_MVP_RUNBOOK.md).
Cloud Explorer V2 и поиск предвестников имеют отдельные current contracts; остальные архитектурные
документы сохраняют полный target scope.

Открытые наблюдения владельца по интерфейсу Cloud Explorer и история
закрытой проблемы переключения окон — в [реестре](TECHNICAL_DEBT.md).
Общая постановка будущего переноса **всех пользовательских таблиц приложения**
в отдельные окна по кнопке и доступности команд — в
[задании на рефакторинг интерфейса](../UI/DETACHED_TABLES_SPEC.md). Оно ещё
не реализовано; руководство ниже описывает текущий интерфейс.

Следующая отдельная target-итерация Order Flow — [«Подбор Cloud»](CLOUD_CALIBRATION_SPEC.md):
предварительная статистика тиков и цепочек по времени дня, визуальный подбор
`MinimumTickVolume / Gap / Range`, описательная diagonal delta, независимые
Cloud rules для каждого временного диапазона и разбор внутренностей Cloud.
Она **не реализована** и не меняет current Cloud Explorer/legacy Cloud.
Исправления и отдельный ограниченный автоматический поиск доступны в
разделе 2.6. Алгоритм, исходные критерии и граница проверки — в
[контракте доработки](CLOUD_EXPLORER_FOLLOWUP_SPEC.md#6-фактический-алгоритм-версии-1).

Источник загружается владельцем самостоятельно; workbench читает один локальный
файл с выбором дат и обязательным ручным шагом цены. Стаканные признаки исключены. Дельта/Cloud 1/Cloud 2 включаются независимо, настройки
разделены на вкладки; над общим графиком доступны выбор из 21 таймфрейма
(до календарного месяца), размер/контраст и подписи объёмов Cloud. График переносится
в отдельное окно, поддерживает перенос пользовательских линий с сохранением наклона,
свободное поле справа и сетку цен, свечи/бары/High-Low,
приглушённые серые High/Low и отдельные Cloud-ломаные. Второй Cloud поддерживает
одиночные крупные тики и цепочки; оба слоя независимо скрываются без пересчёта.
Для каждого слоя доступны статистика объёмной дельты и диагонального перевеса
внутри Cloud/в окружающем потоке, фильтры перевеса и итогового числа тиков без
пересчёта, показ отсечённых записей и переход из строки таблицы к точной метке.
Одиночные тики рисуются квадратами. Отдельная [вкладка статистики](RESEARCH_MVP_RUNBOOK.md#47-статистика-реакций-cloud)
сравнивает реакции после завершения Cloud относительно ATR, подбирает условия
на раннем участке и проверяет на более позднем; рекомендацию можно показать
на графике. Это исследовательские показатели без моделирования исполнения/издержек.
Шаг цены по-прежнему вводится вручную. Доступен реплей исходных тиков
с формированием свечей на выбранном TF, паузой, шагом и регулировкой скорости. Управление описано в [runbook](RESEARCH_MVP_RUNBOOK.md#44-график-и-сводка).
Правила и ограничения
Cloud находятся в [runbook](RESEARCH_MVP_RUNBOOK.md#45-cloud-цепочки-сделок).

## Комплект и источник истины

| ID | Документ | Каноническая ответственность |
|---|---|---|
| `ORDER-FLOW-ROADMAP-001` | [Production roadmap](PRODUCTION_ROADMAP.md) | Цель, границы, архитектура верхнего уровня, последовательность этапов и rollout |
| `ORDER-FLOW-DATA-001` | [Data and replay contract](DATA_REPLAY_CONTRACT.md) | Tick text, manual decimal step, inclusive dates, provenance и causal replay |
| `ORDER-FLOW-RESEARCH-001` | [Research protocol](RESEARCH_PROTOCOL.md) | Observation dataset, labels, исследование, rule/ML boundary и temporal splits |
| `ORDER-FLOW-STRATEGY-001` | [Strategy lifecycle](STRATEGY_LIFECYCLE.md) | Candidate, confirmation, no-trade, intent, exit и содержание StrategySpec |
| `ORDER-FLOW-EXECUTION-001` | [Execution model](EXECUTION_MODEL.md) | Ограничения тиков для fills, будущая квалификация модели, costs/risk и parity boundary |
| `ORDER-FLOW-QUALIFICATION-001` | [Testing and qualification](TESTING_AND_QUALIFICATION.md) | Техническое evidence, historical validation, ворота и `go/no-go` |
| `ORDER-FLOW-MVP-RUNBOOK-001` | [Research MVP runbook](RESEARCH_MVP_RUNBOOK.md) | Текущий OsData workbench, входы, artifacts, reason codes и честная граница evidence |
| `ORDER-FLOW-CLOUD-EXPLORER-V2-001` | [Cloud Explorer v2 contract](CLOUD_EXPLORER_V2_SPEC.md) | Отдельный реализованный offline-режим, формулы и критерии приёмки |
| `ORDER-FLOW-CLOUD-EXPLORER-GUIDE-001` | [Cloud Explorer user guide](CLOUD_EXPLORER_USER_GUIDE.md) | Пошаговая русская инструкция по отдельному окну, настройкам, результатам, графику и реплею |
| `ORDER-FLOW-CLOUD-EXPLORER-FOLLOWUP-001` | [Cloud Explorer follow-up contract](CLOUD_EXPLORER_FOLLOWUP_SPEC.md) | Русские ошибки, исправления экранов и отдельный offline-поиск сочетаний событий до внутридневного движения |
| `ORDER-FLOW-CLOUD-CALIBRATION-001` | [Cloud calibration target](CLOUD_CALIBRATION_SPEC.md) | Будущий session-aware подбор Single/Chain/Diagonal Cloud: статистика, heatmap параметров, per-range rules и Cloud Anatomy |
| `UI-DETACHED-TABLES-001` | [Задание на окна таблиц](../UI/DETACHED_TABLES_SPEC.md) | Целевое правило всего приложения: таблицы по кнопке в отдельных окнах, кнопки видны и доступны |

Если краткое описание roadmap конфликтует с подробным контрактом, применяется
специализированный документ из таблицы. Статус реализации всегда определяется
current code и исполняемыми tests, а не target-документацией.

Новая opt-in вкладка-запуск [«Исследование Cloud»](CLOUD_EXPLORER_USER_GUIDE.md#1-быстрый-старт)
открывает отдельное окно с полным disk-backed каталогом, независимыми фильтрами
и масштабами, причинной адаптацией, эпизодами, выбранным anchored VWAP,
swing/structure markers и отдельными future labels. Старые вкладки, настройки,
bundles и `cloud-reaction-study-1`
сохраняются. Это локальное исследование, не торговые сигналы для отправки заявок.

## Рекомендуемый порядок чтения

1. Cloud Explorer user guide — пройти отдельное окно по четырём шагам.
2. Research MVP runbook — проверить доступный offline slice и его границы.
3. Roadmap — понять цель и последовательность полного проекта.
4. Data/replay — понять, какие данные причинно доступны роботу.
5. Research — понять, почему нужны одновременно датасет наблюдений и Tester.
6. Strategy lifecycle — отделить candidate от реальной сделки.
7. Execution model — понять, как signal превращается или не превращается в fill.
8. Qualification — увидеть доказательства, необходимые перед следующим этапом.

Workbench уже хеширует параметры одного research run, но его UI defaults не
являются выбранной политикой. Числовые окна, thresholds, holding horizon и
параметры риска фиксируются как версия `StrategySpec` только после массового
исследования и до final out-of-sample.
