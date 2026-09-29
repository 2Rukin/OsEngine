# DOCMAP-001: реестр документации OsEngine

**Статус:** CURRENT INDEX
**Обновлено:** 29.09.2026 (UTC)
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
| `INDICATOR-TIME-PROFILES-001` | `project/Documentation/Indicators/TIME_PROFILES.md` | CURRENT IMPLEMENTATION CONTRACT — OFFLINE EVIDENCE | Общие временные пороги Volume/Delta/OI/Trades, reset, main OrderFlow layers и opt-in indicator API; physical UI/live не квалифицированы |
| `HFT-CONTEXT-001` | `project/CONTEXT_HIGH_FREQUENCY.md` | CURRENT DOMAIN CONTEXT | Стакан, high-frequency и latency-sensitive patterns |
| `RISK-CONTEXT-001` | `project/CONTEXT_POSITIONS_AND_RISK.md` | CURRENT DOMAIN CONTEXT | Positions, stops и risk controls |
| `MCP-CONTEXT-001` | `project/CONTEXT_MCP.md` | CURRENT DOMAIN CONTEXT | MCP API, endpoints, tools и test stand |
| `MCP-SCENARIO-001` | `project/CONTEXT_MCP_SCENARIO.md` | CURRENT DOMAIN CONTEXT | MCP operational scenarios |
| `SECURITY-CONTEXT-001` | `project/CONTEXT_SECURITY.md` | CURRENT DOMAIN CONTEXT | UI lock, secret encryption и MCP bootstrap security |

Остальные `project/CONTEXT_*.md` имеют статус `CURRENT DOMAIN CONTEXT` в
области, названной в `PROJECT-CONTEXT-001`; они читаются только по trigger.

### Общий интерфейс приложения

| ID | Путь | Статус | Назначение |
|---|---|---|---|
| `UI-DETACHED-TABLES-001` | `project/Documentation/UI/DETACHED_TABLES_SPEC.md` | TARGET SPECIFICATION — NOT IMPLEMENTED | Будущий перенос всех пользовательских таблиц в отдельные окна по кнопке и доступность команд при разных размерах и DPI |

## 3. Order Flow

| ID | Путь | Статус | Назначение |
|---|---|---|---|
| `ORDER-FLOW-ROADMAP-001` | `project/Documentation/OrderFlow/PRODUCTION_ROADMAP.md` | TARGET ROADMAP — RESEARCH MVP SLICE | Production-ready roadmap tick/delta strategy, causal Tester, visualization, risk и rollout |
| `ORDER-FLOW-INDEX-001` | `project/Documentation/OrderFlow/README.md` | INDEX | Entry point: research-only workbench доступен, robot/edge не реализованы |
| `ORDER-FLOW-TECH-DEBT-001` | `project/Documentation/OrderFlow/TECHNICAL_DEBT.md` | CURRENT ISSUE REGISTER — OWNER UI ACCEPTANCE PENDING | Исправления Cloud Explorer ожидают ручной проверки окна; новый дефект компоновки и закрытые записи |
| `ORDER-FLOW-MVP-RUNBOOK-001` | `project/Documentation/OrderFlow/RESEARCH_MVP_RUNBOOK.md` | CURRENT IMPLEMENTATION GUIDE — RESEARCH ONLY | Входы OsData workbench, независимые Delta/Cloud, общий график и его настройки, artifacts и evidence boundary |
| `ORDER-FLOW-CLOUD-EXPLORER-V2-001` | `project/Documentation/OrderFlow/CLOUD_EXPLORER_V2_SPEC.md` | CURRENT IMPLEMENTATION CONTRACT — OFFLINE RESEARCH ONLY | Opt-in полный каталог Cloud, адаптация, эпизоды, anchored VWAP, swing/structure research и приёмка без изменения legacy |
| `ORDER-FLOW-CLOUD-EXPLORER-GUIDE-001` | `project/Documentation/OrderFlow/CLOUD_EXPLORER_USER_GUIDE.md` | CURRENT OPERATOR GUIDE — OFFLINE RESEARCH ONLY | Русский пошаговый сценарий отдельного окна Cloud Explorer, результатов, графика, реплея, файлов и типичных ошибок |
| `ORDER-FLOW-CLOUD-EXPLORER-FOLLOWUP-001` | `project/Documentation/OrderFlow/CLOUD_EXPLORER_FOLLOWUP_SPEC.md` | CURRENT IMPLEMENTATION CONTRACT — OFFLINE RESEARCH ONLY | Адресная валидация, согласованный график и ограниченный причинный поиск сценариев с недельным контекстом, Fit/Test и отдельными артефактами |
| `ORDER-FLOW-CLOUD-CALIBRATION-001` | `project/Documentation/OrderFlow/CLOUD_CALIBRATION_SPEC.md` | CURRENT IMPLEMENTATION CONTRACT — OFFLINE RESEARCH ONLY | Реализованный session-aware подбор Single/Chain и Standard/Diagonal rules: статистика, heatmap, per-range rules, detached tables и Anatomy; физический DPI/focus — OWNER-RUN |
| `ORDER-FLOW-SRU6-CALIBRATION-2026-001` | `project/Documentation/OrderFlow/SRU6_CLOUD_CALIBRATION_JUN_AUG_2026.md` | RESEARCH REPORT — OWNER VISUAL VALIDATION PENDING | Воспроизводимый offline-подбор описательных Cloud-параметров SRU6 за июнь–август 2026; не profitability/live claim |
| `ORDER-FLOW-CONTEXT-001` | `project/Documentation/OrderFlow/MULTISCALE_CONTEXT_RUNBOOK.md` | CURRENT IMPLEMENTATION GUIDE / CONTRACT — OFFLINE RESEARCH ONLY | Отдельное окно одного серого графика: три масштаба областей, VWAP/TWAP/σ, лента/структура, причинный replay, профили, исходы и руководство по исследованию |
| `ORDER-FLOW-TICK-PATTERNS-001` | `project/Documentation/OrderFlow/TICK_PATTERN_STATISTICS_SPEC.md` | TARGET IMPLEMENTATION SPECIFICATION — NOT IMPLEMENTED | Один инструмент и TXT; ритм, концентрация размеров, TPS, поглощение; сортируемая статистика, выбор всех/одного события и ромбы на графике |
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

## 6. Adaptive Position Manager

| ID | Путь | Статус | Назначение |
|---|---|---|---|
| `APM-INDEX-001` | `project/docs/adaptive-position-manager/README.md` | TARGET INDEX — IMPLEMENTATION IN PROGRESS | Продукт, последовательность T01–T12 и границы доказательств |
| `APM-PRODUCT-001` | `project/docs/adaptive-position-manager/01-product.md` | ACCEPTED PRODUCT CONTRACT | Пользовательские требования R01–R22 |
| `APM-DECISIONS-001` | `project/docs/adaptive-position-manager/02-decisions.md` | TARGET ARCHITECTURE | Реестр ADR-APM-001–010; уточнения реализации отдельно |
| `APM-MATH-001` | `project/docs/adaptive-position-manager/03-mathematics.md` | TARGET MATHEMATICAL CONTRACT | APM-Target-v0.1, единицы, кривая, риск и hysteresis |
| `APM-DATA-001` | `project/docs/adaptive-position-manager/04-data-contracts.md` | TARGET DATA CONTRACT | Профили, причинность, качество и manifest |
| `APM-EXECUTION-001` | `project/docs/adaptive-position-manager/05-state-execution.md` | TARGET EXECUTION CONTRACT | Кампания, intents/fills, ledger, отмена и recovery |
| `APM-INTEGRATION-001` | `project/docs/adaptive-position-manager/06-osengine-integration.md` | HISTORICAL BASELINE / TARGET INTEGRATION | Аудит исходной базы и требования к нативным адаптерам |
| `APM-SCENARIOS-001` | `project/docs/adaptive-position-manager/07-tester-scenarios.md` | TARGET QUALIFICATION | Эталоны S01–S26; не результаты прогона |
| `APM-RESEARCH-001` | `project/docs/adaptive-position-manager/08-optimization.md` | TARGET RESEARCH PROTOCOL | Baseline, OOS, cost stress и locks |
| `APM-UI-001` | `project/docs/adaptive-position-manager/09-interface.md` | TARGET UI CONTRACT | График, отдельные таблицы и физическая приёмка |
| `APM-GATES-001` | `project/docs/adaptive-position-manager/10-quality-gates.md` | TARGET QUALIFICATION CONTRACT | QG00–QG12 и границы engineering/research/live |
| `APM-TRACEABILITY-001` | `project/docs/adaptive-position-manager/11-traceability.md` | TARGET COVERAGE INDEX | R/S/T/QG |
| `APM-OPERATIONS-001` | `project/docs/adaptive-position-manager/12-operations.md` | TARGET RUNBOOK | Preflight, аварии, эксплуатационная приёмка |
| `APM-SOURCES-001` | `project/docs/adaptive-position-manager/13-sources.md` | RESEARCH REFERENCE | Первоисточники и пределы переноса формул |
| `APM-IMPLEMENTATION-001` | `project/docs/adaptive-position-manager/evidence/implementation.md` | CURRENT WORK RECORD — RESEARCH ONLY / GATES OPEN | Фактический scope, решения неоднозначностей, capability/evidence и blockers текущего diff; evidence/cp17-verification.json — source/build hashes, команды и локальные артефакты исправлений owner walkthrough |
| `APM-NATIVE-EVIDENCE-001` | `project/docs/adaptive-position-manager/evidence/native-tester/qualification.json` | CURRENT SYNTHETIC NATIVE TESTER EVIDENCE | Результаты отдельных native runs, hashes, fill traces и границы GUI/ResearchOnly; release-regression.json и сопутствующие CSV/manifest/screenshots в том же каталоге |
| `APM-OPTIMIZER-EVIDENCE-001` | `project/docs/adaptive-position-manager/evidence/native-optimizer/qualification.json` | CURRENT SYNTHETIC NATIVE OPTIMIZER EVIDENCE | IS/OOS, baseline, all-trials, fixed values, native filters и thread parity; lifecycle-qualification.json, companion experiment/all-trials/native-selection JSON и study-report screenshot в том же каталоге; не historical GO |
| `APM-RESEARCH-IMPLEMENTATION-001` | `project/docs/adaptive-position-manager/14-research-implementation.md` | CURRENT IMPLEMENTATION DESIGN — RESEARCH ONLY | T09/T10 formula mapping, causal calibration, units, default-off AC pacing и BLOCKED book/native-data boundary |
| `APM-OPERATIONS-IMPLEMENTATION-001` | `project/docs/adaptive-position-manager/15-operations-implementation.md` | CURRENT IMPLEMENTATION DESIGN — RESEARCH ONLY | T11 native full-fill lifetime budgets, checkpoint compatibility, watchdog, load/recovery evidence boundary |
| `APM-OPERATIONS-EVIDENCE-001` | `project/docs/adaptive-position-manager/evidence/operations/` | CURRENT SYNTHETIC COMPONENT/NATIVE EVIDENCE | load.json и native-regression.json с raw results/hashes; не native/live recovery или real-data peak qualification |
| `APM-RELEASE-READINESS-001` | `project/docs/adaptive-position-manager/16-release-readiness.md` | CURRENT RUNBOOK — RESEARCH ONLY / GATES OPEN | Реальная история, штатный Optimizer, ручная приёмка, ограничения и rollback; evidence/release-manifest.json — checkpoint hashes/traceability, не опубликованный release |
| `APM-MANUAL-UI-001` | `project/docs/adaptive-position-manager/17-manual-ui-acceptance.md` | CURRENT OWNER WALKTHROUGH / FINDINGS FIXED, DPI OPEN | Ручные S01/S02 и controls evidence, UX-01…UX-08, исправления и остающаяся физическая DPI-приёмка |

## 7. TradeHelp range grid

Текущий implementation contract и operator guide отделены от hash-bound source
reconstruction и первоначальных target документов. Native code находится в
dirty worktree; offline component checks пройдены, оба scoped reviews CLEAN.
Физические replay/GUI/live qualification остаются NOT_RUN.

| ID | Путь | Статус | Назначение |
|---|---|---|---|
| `THG-INDEX-001` | `project/Documentation/TradeHelpGrid/README.md` | SOURCE BASELINE + CURRENT IMPLEMENTATION INDEX | Вход в комплект реконструкции локальной TradeHelp4 и будущего range grid |
| `ADR-THG-001` | `project/Documentation/TradeHelpGrid/ADR-0001_RANGE_GRID.md` | HISTORICAL PROPOSAL — SUPERSEDED FOR IMPLEMENTATION | Архитектура single-instrument grid, ownership, emergency liquidation и выбор native интеграции |
| `ADR-THG-002` | `project/Documentation/TradeHelpGrid/ADR-0002_OSENGINE_IMPLEMENTATION.md` | CURRENT IMPLEMENTATION CONTRACT — OFFLINE CHECKS PASSED, LIVE NOT QUALIFIED | Нативный Futures2Grid, signed-price gateway, data/recovery, полный feature mapping и явные target отличия |
| `THG-OPERATOR-002` | `project/Documentation/TradeHelpGrid/OSENGINE_OPERATOR.md` | CURRENT OPERATOR GUIDE — OFFLINE CHECKS PASSED, LIVE NOT QUALIFIED | Ручные параметры, runtime команды, replacement/transfer, recovery и evidence boundary |
| `THG-TRANSAQ-PLAN-001` | `project/Documentation/TradeHelpGrid/FINAM_TRANSAQ_PLAN.md` | TARGET PLAN — REVIEWED CLEAN/CLEAN; IMPLEMENTATION TRACKED SEPARATELY | План Finam/TRANSAQ: profiles, stable order IDs, source account/data, recovery, offline и owner-run evidence; не реализация |
| `THG-OWNERSHIP-008` | `project/Documentation/TradeHelpGrid/SHARED_ACCOUNT_AND_REGISTRATION.md` | CURRENT ACCOUNT DIAGNOSTICS + MANUAL INVENTORY — OFFLINE ONLY | Shared-account observation, source ownership semantics and pointer to manual inventory contract |
| `THG-INVENTORY-009` | `project/Documentation/TradeHelpGrid/INVENTORY_REGISTRATION.md` | CURRENT IMPLEMENTATION — OFFLINE CHECKS PASSED, REVIEW CLEAN/CLEAN, LIVE NOT QUALIFIED | No-send ownership operations, native inventory, external declarations and crash recovery boundary |
| `THG-RANGE-014` | `project/Documentation/TradeHelpGrid/RANGE_STATE.md` | CURRENT IMPLEMENTATION — OFFLINE CHECKS PASSED, REVIEW CLEAN/CLEAN | Preserve explicit level controls across range transformations |
| `THG-RANGE-REVIEW-014` | `project/Documentation/TradeHelpGrid/RANGE_STATE_REVIEW.md` | SCOPED REVIEW — TERMINAL CLEAN/CLEAN | Range override regression and evidence |
| `THG-HISTORICAL-015` | `project/Documentation/TradeHelpGrid/HISTORICAL_QUALIFICATION.md` | CURRENT NATIVE TESTER EVIDENCE — LIVE NOT QUALIFIED | SRU6 TXT/QSH, signed/zero replay, fixed loader defect and manual owner cases |
| `THG-HISTORICAL-REVIEW-015` | `project/Documentation/TradeHelpGrid/HISTORICAL_QUALIFICATION_REVIEW.md` | SCOPED REVIEW — TERMINAL CLEAN/CLEAN | Independent review, bounded fixes and exact evidence for SRU6/native Tester qualification |
| `THG-EXECUTION-OPTIONS-013` | `project/Documentation/TradeHelpGrid/EXECUTION_OPTIONS.md` | CURRENT IMPLEMENTATION — OFFLINE CHECKS PASSED, REVIEW CLEAN/CLEAN | Explicit traversal and ordinary quote-price policies, schema5 |
| `THG-EXECUTION-OPTIONS-REVIEW-013` | `project/Documentation/TradeHelpGrid/EXECUTION_OPTIONS_REVIEW.md` | SCOPED REVIEW — TERMINAL CLEAN/CLEAN | Execution options bounded review and evidence |
| `THG-EMPTY-012` | `project/Documentation/TradeHelpGrid/EMPTY_REMOVAL.md` | CURRENT IMPLEMENTATION — OFFLINE CHECKS PASSED, REVIEW CLEAN/CLEAN | Opt-in never-active isolated robot removal through native owner and explicit mode boundaries |
| `THG-EMPTY-REVIEW-012` | `project/Documentation/TradeHelpGrid/EMPTY_REMOVAL_REVIEW.md` | SCOPED REVIEW — TERMINAL CLEAN/CLEAN | Native owner lifecycle and empty removal verification |
| `THG-BATCH-011` | `project/Documentation/TradeHelpGrid/BATCH_LEVELS.md` | CURRENT IMPLEMENTATION — OFFLINE CHECKS PASSED, REVIEW CLEAN/CLEAN | Selected-level entry progress, lot exits, edits/cancels and schema4 |
| `THG-BATCH-REVIEW-011` | `project/Documentation/TradeHelpGrid/BATCH_REVIEW.md` | SCOPED REVIEW — TERMINAL CLEAN/CLEAN | Independent selected-level implementation review and evidence |
| `THG-HEDGE-010` | `project/Documentation/TradeHelpGrid/HEDGE_MODE.md` | CURRENT IMPLEMENTATION — OFFLINE CHECKS PASSED, REVIEW CLEAN/CLEAN | Logical/physical direction, hedge targets, schema3 and recovery/manual inventory |
| `THG-HEDGE-REVIEW-010` | `project/Documentation/TradeHelpGrid/HEDGE_REVIEW.md` | SCOPED REVIEW — TERMINAL CLEAN/CLEAN | Bounded independent safety/docs review of hedge integration |
| `THG-INVENTORY-REVIEW-009` | `project/Documentation/TradeHelpGrid/INVENTORY_REVIEW.md` | SCOPED IMPLEMENTATION REVIEW — TERMINAL CLEAN/CLEAN | Independent safety/docs findings, bounded fixes and exact manual inventory evidence |
| `THG-OWNERSHIP-REVIEW-008` | `project/Documentation/TradeHelpGrid/OWNERSHIP_REVIEW.md` | SCOPED REVIEW — TERMINAL CLEAN/CLEAN | Independent review of account diagnostics/readiness and explicit target boundaries |
| `THG-COMPLETENESS-AUDIT-007` | `project/Documentation/TradeHelpGrid/COMPLETENESS_AUDIT.md` | READ-ONLY INVENTORY — TERMINAL DOC REVIEW CLEAN | User-requested source/current omissions, changed semantics and authorization/evidence boundaries |
| `THG-TRANSAQ-CODE-REVIEW-006` | `project/Documentation/TradeHelpGrid/FINAM_TRANSAQ_IMPLEMENTATION_REVIEW.md` | SCOPED IMPLEMENTATION REVIEW — TERMINAL CLEAN/CLEAN | Independent production/docs review and exact evidence for TRANSAQ implementation |
| `THG-TRANSAQ-IMPLEMENTATION-006` | `project/Documentation/TradeHelpGrid/FINAM_TRANSAQ_IMPLEMENTATION.md` | CURRENT IMPLEMENTATION — OFFLINE CHECKS PASSED, REVIEW CLEAN/CLEAN, LIVE NOT QUALIFIED | Version/profile decisions, native identities, source observations and evidence boundary for standard TRANSAQ |
| `THG-TRANSAQ-REVIEW-001` | `project/Documentation/TradeHelpGrid/FINAM_TRANSAQ_PLAN_REVIEW.md` | SCOPED PLAN REVIEW — TERMINAL CLEAN/CLEAN | Независимая критическая проверка плана и disposition; не review нового production code |
| `THG-IMPLEMENTATION-REVIEW-004` | `project/Documentation/TradeHelpGrid/IMPLEMENTATION_REVIEW.md` | SCOPED REVIEW RECORD — TERMINAL CLEAN | PRIMARY findings, bounded FIX/VALIDATION disposition и exact offline evidence для Futures2Grid |
| `THG-LEGACY-001` | `project/Documentation/TradeHelpGrid/LEGACY_BEHAVIOR.md` | STATIC RECONSTRUCTION — LOCAL BINARY BASELINE | Найденные Futures2/DTF/DTM/DTI варианты и точные границы claims |
| `THG-F2-TRANSITIONS-001` | `project/Documentation/TradeHelpGrid/FUTURES2_TRANSITIONS.md` | CURRENT STATIC SOURCE SPECIFICATION — NOT RUNTIME QUALIFIED | Manager states/guards/priorities, filters, stop, budget и сдвиги Futures2 |
| `THG-F2-LEVELS-001` | `project/Documentation/TradeHelpGrid/FUTURES2_LEVELS_AND_ORDERS.md` | CURRENT STATIC SOURCE SPECIFICATION | Уровни, immediate/preorders, sizing, slot ledger, fills/cancel и ручной учёт |
| `THG-F2-SETTINGS-001` | `project/Documentation/TradeHelpGrid/FUTURES2_RUNTIME_SETTINGS.md` | STATIC SOURCE SPECIFICATION + OWNER REQUIREMENTS | Начальные/рабочие параметры, rebuild, команды, persistence; ui-bindings.json — passive resource evidence |
| `THG-F2-HOST-001` | `project/Documentation/TradeHelpGrid/FUTURES2_HOST_AND_CALLBACKS.md` | CURRENT STATIC SOURCE SPECIFICATION | Host/schedules, grouping, callbacks/replay, transport failures и полный helper lifecycle |
| `THG-F2-ROLLOVER-001` | `project/Documentation/TradeHelpGrid/FUTURES2_EXPIRATION_AND_ROTATION.md` | CURRENT STATIC SOURCE SPECIFICATION — INHERITED PATHS | Expirate/Rotate preparation, guards, apply/cancel и конфликты режимов |
| `THG-F2-COVERAGE-001` | `project/Documentation/TradeHelpGrid/FUTURES2_COVERAGE.md` | CURRENT STATIC COVERAGE — NOT EXECUTED TESTS | Source inventory, 60 legacy сценариев, граница полноты; transition-index.json — индекс переходов/anchors |
| `THG-PRICE-001` | `project/Documentation/TradeHelpGrid/PRICE_DOMAIN.md` | OWNER REQUIREMENT + NUMERICAL TARGET — IMPLEMENTATION IN ADR-THG-002 | Signed/zero цены до5 знаков, tick grid, обеспечение/лоты, выходы/стопы, runtime/persistence; signed-price-cases.json и checker — arithmetic evidence без native исполнения |
| `THG-MATH-001` | `project/Documentation/TradeHelpGrid/MATHEMATICS_AND_PARAMETERS.md` | STATIC LEGACY CONTRACT + PROPOSED DIFFERENCES | Формулы уровней/лотов/внешней зоны, единицы, параметры и контрольные примеры |
| `THG-EXECUTION-001` | `project/Documentation/TradeHelpGrid/EXECUTION_AND_RECOVERY.md` | OBSERVED LEGACY FLOW + PROPOSED EXECUTION CONTRACT | Заявки/fills/cancel, campaign state, liquidation и recovery |
| `THG-INTEGRATION-001` | `project/Documentation/TradeHelpGrid/OSENGINE_INTEGRATION.md` | CURRENT CHECKOUT RESEARCH + PROPOSED MAPPING | Native grid/order APIs, mode differences и границы повторного использования |
| `THG-OPERATIONS-001` | `project/Documentation/TradeHelpGrid/OPERATIONS.md` | PROPOSED RUNBOOK — NOT IMPLEMENTED | Operator actions, incidents, observability и owner-run connector acceptance |
| `THG-QUALIFICATION-001` | `project/Documentation/TradeHelpGrid/QUALIFICATION.md` | PROPOSED QUALIFICATION + CURRENT STATIC EVIDENCE | 46 будущих сценариев, включая signed/zero precision, и граница фактической документальной проверки |
| `THG-EVIDENCE-001` | `project/Documentation/TradeHelpGrid/EVIDENCE.md` | CURRENT STATIC RESEARCH RECORD | Provenance, method anchors, verification; evidence/manifest.json и arithmetic.json — hash/арифметические приложения, не runtime qualification |

## 8. Правила обновления карты

- Новый ADR, binding architecture contract, runbook, qualification report,
  общий skill/native adapter или governed review registry добавляется сюда в
  том же diff.
- ID уникален и не переиспользуется после удаления/архивации документа.
- Status должен отличать current contract от target/history/report.
- Переименование пути обновляет эту карту и все direct links атомарно.
