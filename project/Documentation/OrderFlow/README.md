# ORDER-FLOW-INDEX-001: документация Order Flow

Комплект документации внутридневного робота, использующего парные потоки сделок
и стакана.

Текущий статус: **target contracts, торговый код не реализован, экономическое
преимущество не доказано**. Документы определяют порядок будущей реализации и
проверки, но не описывают уже доступную функцию OsEngine.

Roadmap и baseline-аудит зафиксированы относительно ветки `master` форка
`2Rukin/OsEngine`, commit
`f54de33961d45f73319ae1c7313f2854bcb27398`. Внешний upstream не является
источником требований или готовых решений для проекта.

## Комплект и источник истины

| ID | Документ | Каноническая ответственность |
|---|---|---|
| `ORDER-FLOW-ROADMAP-001` | [Production roadmap](PRODUCTION_ROADMAP.md) | Цель, границы, архитектура верхнего уровня, последовательность этапов и rollout |
| `ORDER-FLOW-DATA-001` | [Data and replay contract](DATA_REPLAY_CONTRACT.md) | QSH pair, manifest, normalizer, timestamp buckets, deterministic causal replay |
| `ORDER-FLOW-RESEARCH-001` | [Research protocol](RESEARCH_PROTOCOL.md) | Observation dataset, labels, исследование, rule/ML boundary и temporal splits |
| `ORDER-FLOW-STRATEGY-001` | [Strategy lifecycle](STRATEGY_LIFECYCLE.md) | Candidate, confirmation, no-trade, intent, exit и содержание StrategySpec |
| `ORDER-FLOW-EXECUTION-001` | [Execution model](EXECUTION_MODEL.md) | Latency, стакан, partial fills, costs, order/risk lifecycle и parity boundary |
| `ORDER-FLOW-QUALIFICATION-001` | [Testing and qualification](TESTING_AND_QUALIFICATION.md) | Техническое evidence, historical validation, ворота и `go/no-go` |

Если краткое описание roadmap конфликтует с подробным контрактом, применяется
специализированный документ из таблицы. Статус реализации всегда определяется
current code и исполняемыми tests, а не target-документацией.

## Рекомендуемый порядок чтения

1. Roadmap — понять цель и последовательность.
2. Data/replay — понять, какие данные причинно доступны роботу.
3. Research — понять, почему нужны одновременно датасет наблюдений и Tester.
4. Strategy lifecycle — отделить candidate от реальной сделки.
5. Execution model — понять, как signal превращается или не превращается в fill.
6. Qualification — увидеть доказательства, необходимые перед следующим этапом.

Конкретные числовые окна, thresholds, holding horizon и параметры риска пока не
зафиксированы. Они появляются только в версиях `ResearchSpec` и `StrategySpec`
после реализации causal replay и исследования данных.
