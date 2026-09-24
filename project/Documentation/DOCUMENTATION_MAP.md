# DOCMAP-001: реестр документации OsEngine

**Статус:** CURRENT INDEX
**Обновлено:** 24.09.2026 (UTC)
**Repository:** `2Rukin/OsEngine`

Этот файл — единая точка входа для определения назначения, статуса и
authoritative level документа. Он не превращает roadmap или review report в
описание текущей реализации.

## 1. Иерархия источников

При конфликте сначала классифицируй claim:

1. Принятый ADR/architecture contract — обязательное проектное решение и
   целевой contract в заявленном scope.
2. Current production code + исполняемые tests — что реализовано сейчас.
3. Current domain/context document — навигация и действующие соглашения; его
   behavioral claims сверяются с уровнем 1–2.
4. Qualification evidence exact baseline — что доказал конкретный прогон.
5. `TARGET`, roadmap, proposal, backlog — будущее, не current behavior.
6. Review/task report — evidence своего baseline, не переносимый source of
   truth.
7. Agent instructions — процесс работы, не торговая архитектура.

Принятый contract и current code могут расходиться: это `CODE_DRIFT`, а не
разрешение молча переписать один под другой.

## 2. Current project navigation и conventions

| ID | Путь | Статус | Назначение |
|---|---|---|---|
| `PROJECT-CONTEXT-001` | `project/CONTEXT.md` | CURRENT INDEX | Карта проекта, режимов и доменных контекстов |
| `CODING-GUIDE-001` | `project/CONTEXT_CODING_GUIDELINES.md` | CURRENT CONVENTIONS | C#/.NET/WPF style и обязательные engineering patterns |
| `CONNECTORS-CONTEXT-001` | `project/CONTEXT_CONNECTORS.md` | CURRENT DOMAIN CONTEXT | Connector architecture, lifecycle, events, market data и orders |
| `ROBOTS-ARCH-001` | `project/CONTEXT_ROBOTS_ARCHITECTURE.md` | CURRENT DOMAIN CONTEXT | Архитектура `BotPanel`/tabs и режимов робота |
| `ROBOTS-CONTEXT-001` | `project/CONTEXT_ROBOTS.md` | CURRENT DOMAIN CONTEXT | Практика разработки торговых роботов |
| `INDICATORS-CONTEXT-001` | `project/CONTEXT_INDICATORS.md` | CURRENT DOMAIN CONTEXT | Индикаторы и их integration boundary |
| `HFT-CONTEXT-001` | `project/CONTEXT_HIGH_FREQUENCY.md` | CURRENT DOMAIN CONTEXT | Стакан, high-frequency и latency-sensitive patterns |
| `RISK-CONTEXT-001` | `project/CONTEXT_POSITIONS_AND_RISK.md` | CURRENT DOMAIN CONTEXT | Positions, stops и risk controls |
| `MCP-CONTEXT-001` | `project/CONTEXT_MCP.md` | CURRENT DOMAIN CONTEXT | MCP API, endpoints, tools и test stand |
| `MCP-SCENARIO-001` | `project/CONTEXT_MCP_SCENARIO.md` | CURRENT DOMAIN CONTEXT | MCP operational scenarios |
| `SECURITY-CONTEXT-001` | `project/CONTEXT_SECURITY.md` | CURRENT DOMAIN CONTEXT | UI lock, secret encryption и MCP bootstrap security |

Остальные `project/CONTEXT_*.md` имеют статус `CURRENT DOMAIN CONTEXT` в
области, названной в `PROJECT-CONTEXT-001`; они читаются только по trigger.

## 3. Order Flow

| ID | Путь | Статус | Назначение |
|---|---|---|---|
| `ORDER-FLOW-ROADMAP-001` | `project/Documentation/OrderFlow/PRODUCTION_ROADMAP.md` | TARGET ROADMAP — RESEARCH MVP SLICE | Production-ready roadmap tick/delta strategy, causal Tester, visualization, risk и rollout |
| `ORDER-FLOW-INDEX-001` | `project/Documentation/OrderFlow/README.md` | INDEX | Entry point: research-only workbench доступен, robot/edge не реализованы |
| `ORDER-FLOW-TECH-DEBT-001` | `project/Documentation/OrderFlow/TECHNICAL_DEBT.md` | CURRENT ISSUE REGISTER — OWNER UI ACCEPTANCE PENDING | Исправления Cloud Explorer ожидают ручной проверки окна; исторические симптомы, границы подтверждения и закрытая оконная запись |
| `ORDER-FLOW-MVP-RUNBOOK-001` | `project/Documentation/OrderFlow/RESEARCH_MVP_RUNBOOK.md` | CURRENT IMPLEMENTATION GUIDE — RESEARCH ONLY | Входы OsData workbench, независимые Delta/Cloud, общий график и его настройки, artifacts и evidence boundary |
| `ORDER-FLOW-CLOUD-EXPLORER-V2-001` | `project/Documentation/OrderFlow/CLOUD_EXPLORER_V2_SPEC.md` | CURRENT IMPLEMENTATION CONTRACT — OFFLINE RESEARCH ONLY | Opt-in полный каталог Cloud, адаптация, эпизоды, anchored VWAP, swing/structure research и приёмка без изменения legacy |
| `ORDER-FLOW-CLOUD-EXPLORER-GUIDE-001` | `project/Documentation/OrderFlow/CLOUD_EXPLORER_USER_GUIDE.md` | CURRENT OPERATOR GUIDE — OFFLINE RESEARCH ONLY | Русский пошаговый сценарий отдельного окна Cloud Explorer, результатов, графика, реплея, файлов и типичных ошибок |
| `ORDER-FLOW-CLOUD-EXPLORER-FOLLOWUP-001` | `project/Documentation/OrderFlow/CLOUD_EXPLORER_FOLLOWUP_SPEC.md` | CURRENT IMPLEMENTATION CONTRACT — OFFLINE RESEARCH ONLY | Адресная валидация, согласованный график и ограниченный причинный поиск сценариев с недельным контекстом, Fit/Test и отдельными артефактами |
| `ORDER-FLOW-DATA-001` | `project/Documentation/OrderFlow/DATA_REPLAY_CONTRACT.md` | TARGET CONTRACT — PARTIAL RESEARCH MVP | Локальный tick text, ручной decimal step, inclusive dates, provenance и causal replay |
| `ORDER-FLOW-RESEARCH-001` | `project/Documentation/OrderFlow/RESEARCH_PROTOCOL.md` | TARGET RESEARCH CONTRACT — PARTIAL MVP | Observation dataset, labels, experiment registry, rule/ML boundary и temporal validation |
| `ORDER-FLOW-STRATEGY-001` | `project/Documentation/OrderFlow/STRATEGY_LIFECYCLE.md` | TARGET CONTRACT — BROAD CANDIDATE ONLY | Candidate/confirmation/no-trade/intent lifecycle и обязательное содержание StrategySpec |
| `ORDER-FLOW-EXECUTION-001` | `project/Documentation/OrderFlow/EXECUTION_MODEL.md` | TARGET EXECUTION CONTRACT — NOT IMPLEMENTED | Ограничения tick input, будущая квалификация fill-модели, costs/risk и Tester/live boundary |
| `ORDER-FLOW-QUALIFICATION-001` | `project/Documentation/OrderFlow/TESTING_AND_QUALIFICATION.md` | TARGET CONTRACT — SYNTHETIC STAND ADDED | Technical evidence, historical validation, transition gates и go/no-go |

Отсутствие target components roadmap не является code-review defect. Claim о
реализации должен иметь code/test evidence вне этого документа.

## 4. Agent system

| ID | Путь | Статус | Назначение |
|---|---|---|---|
| `AGENTS-MD-001` | `AGENTS.md` | SHARED AGENT INSTRUCTIONS | Repo-wide entry point, permissions и safety boundaries |
| `PROJECT-AGENTS-001` | `project/AGENTS.md` | SCOPED AGENT INSTRUCTIONS | C#/.NET/WPF/build/test правила для `project/` |
| `CLAUDE-MD-001` | `CLAUDE.md` | CLAUDE ADAPTER | Импортирует общие правила и описывает discovery |
| `AGENT-WORKFLOW-001` | `.agents/CLI_WORKFLOW_CONTRACT.md` | CURRENT WORKFLOW CONTRACT | Routing, task state, completion и evidence reuse |
| `AGENT-SYSTEM-README-001` | `project/Documentation/AgentSystem/README.md` | CURRENT OPERATOR GUIDE | Как пользователь и Main применяют систему |
| `ADR-AGENT-001` | `project/Documentation/AgentSystem/ADR-0001_AGENT_WORKFLOW.md` | ACCEPTED | Решение об адаптации TXML agent workflow к OsEngine |
| `ACTIVE-TASK-001` | `project/Documentation/AgentSystem/ACTIVE_TASK.md` | CURRENT LIVE STATE | Единственный resume-critical snapshot нетривиальной задачи ветки |
| `FULL-REVIEW-INDEX-001` | `project/Documentation/AgentSystem/FULL_REVIEW_INDEX.md` | CURRENT AUDIT REGISTRY | Terminal repository-wide review identities и owner triage |
| `FULL-REVIEW-TEMPLATE-001` | `.agents/templates/FULL_CODE_REVIEW_REPORT.md` | CURRENT TEMPLATE | Coverage/finding/triage schema полного review |
| `AGENT-VALIDATOR-001` | `.agents/validation/validate-agent-system.sh` | OFFLINE VALIDATOR | Topology, routing и deterministic fixture acceptance; не semantic reviewer |
| `SKILL-RESEARCH-001` | `.agents/skills/codebase-research/SKILL.md` | INTERNAL SKILL | Read-only карта current checkout |
| `SKILL-PROD-REVIEW-001` | `.agents/skills/production-code-review/SKILL.md` | INTERNAL SKILL | Production-safety review procedure |
| `SKILL-PROD-CHECKLIST-001` | `.agents/skills/production-review-checklist/SKILL.md` | INTERNAL HELPER | OsEngine domain checklist |
| `SKILL-DOC-REVIEW-001` | `.agents/skills/documentation-review/SKILL.md` | INTERNAL SKILL | Semantic Markdown/XML-doc audit |
| `SKILL-XMLDOC-001` | `.agents/skills/csharp-xml-doc-style/SKILL.md` | INTERNAL STANDARD | C# XML Documentation genres, Tier и drift |

Conditional workflow fragments, `.codex/agents/*.toml`,
`.claude/agents/*.md`, `.claude/skills/*/SKILL.md` и
`.agents/skills/*/agents/openai.yaml` являются частями `AGENT-WORKFLOW-001`, а
не отдельными архитектурными authorities.

## 5. Review reports и working notes

- Repository-wide reports хранятся только в
  `project/Documentation/AgentSystem/reviews/` и регистрируются в
  `FULL-REVIEW-INDEX-001`.
- Report привязан к exact baseline + dirty boundary. Его findings при будущем
  использовании перепроверяются по current checkout.
- Иной persistent report создаётся только по явному запросу пользователя и,
  если он governed, регистрируется здесь в том же diff.
- Chat/subagent result не становится project document автоматически.

## 6. Правила обновления карты

- Новый ADR, binding architecture contract, runbook, qualification report,
  общий skill/native adapter или governed review registry добавляется сюда в
  том же diff.
- ID уникален и не переиспользуется после удаления/архивации документа.
- Status должен отличать current contract от target/history/report.
- Переименование пути обновляет эту карту и все direct links атомарно.
