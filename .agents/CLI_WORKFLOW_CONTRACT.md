# AGENT-WORKFLOW-001: инженерный workflow OsEngine

**Статус:** CURRENT
**Дата актуализации:** 20.09.2026 20:42:41 (UTC+03:00, Москва)
**Scope:** компактное обязательное ядро agent workflow форка `2Rukin/OsEngine`.

Main полностью читает этот файл для каждой нетривиальной engineering-задачи.
Условные протоколы физически отделены и загружаются только по таблице в конце.
Не читать весь `.agents/workflow/` превентивно.

## 1. Entry point и routing

Пользователь передаёт задачу Main и не обязан выбирать skill/reviewer. Main
определяет frozen scope, разрешённые изменения и необходимое evidence.

| Intent | Исполнитель |
|---|---|
| Фактическая карта current checkout | native `codebase-researcher` |
| Production-safety review | native `production-code-reviewer` |
| Semantic Markdown/C# XML-doc audit | native `documentation-reviewer` |
| Design, implementation и fix | Main |

Research/review skills — internal dependencies read-only ролей. Прямой
skill-path в write-capable Main не заменяет независимый pass. Недоступная
обязательная роль даёт `BLOCKED`.

Небольшая локальная задача не создаёт active state, не запускает reviewers и не
загружает documentation bundle без реального trigger.

## 2. Authoritative live task state

Для нетривиальной задачи единственный live snapshot —
`project/Documentation/AgentSystem/ACTIVE_TASK.md`. В одной ветке одновременно
разрешена одна активная нетривиальная работа.

Snapshot содержит только resume-critical current state:

- task ID/goal, status/phase, branch и exact baseline;
- frozen scope и запрещённые side effects;
- последний завершённый и текущий незавершённый шаг;
- актуальные decisions/evidence/verification/blockers;
- completed transition IDs и один distinct next transition ID;
- exact next action;
- при maintenance-вставке — suspended phase/checkpoint/resume action.

`ACTIVE_TASK.md` — replacement snapshot, не append-only журнал. Максимум 180
строк и 14 000 символов. Terminal details сначала попадают в task record/report
и Git, затем удаляются из live snapshot. Chat transcript не является durable
state.

### 2.1. Start, checkpoint, interruption

До первого содержательного изменения target files Main:

1. проверяет branch, HEAD, status и пользовательский dirty diff;
2. фиксирует snapshot как `IN_PROGRESS`;
3. записывает scope, запреты, acceptance и next action;
4. создаёт checkpoint commit только при отдельном разрешении пользователя.

После recoverable phase snapshot и evidence обновляются вместе. Checkpoint после
каждого tool call или wording-only edit не нужен.

Если задача прервана maintenance-вставкой, Main записывает suspended phase,
exact clean checkpoint и resume action. Maintenance не расширяет suspended
scope. После terminal maintenance исходная phase восстанавливается.

### 2.2. Resume

После restart сопоставь snapshot с branch/HEAD/status/diff и продолжи с `Next
action`. Незавершённый шаг без evidence не считается успешным. `Completed
transition IDs` содержит только доказанно завершённые переходы; `Next
transition ID` — ровно один ещё не завершённый и не повторяющий completed ID.

## 3. Frozen scope и permissions

До implementation фиксируются target behavior/document, baseline, in-scope
paths, запрещённые side effects, необходимые agent/owner checks и terminal
criteria.

Review различает исходную проблему, regression текущего diff, adjacent problem
и недоказанную hypothesis. Adjacent finding не расширяет scope автоматически.

Диагностика не разрешает fix; изменение не разрешает commit/push; build не
разрешает запуск OsEngine, MCP test stand, live connector или real orders.
Credentials и secret-bearing payload не читаются и не выводятся.

## 4. Completion и verification economy

Применимая задача завершена, когда:

1. заявленное поведение реализовано без незавершённого in-scope path;
2. targeted и обязательная final verification выполнены;
3. current docs и C# XML comments соответствуют behavior/evidence;
4. Tester/live parity, sequence и observability impact оценены;
5. применимый bounded independent review получил terminal outcome;
6. active snapshot содержит terminal evidence/blockers и exact next action;
7. handoff называет evidence boundary, `NOT_RUN`, commit и push status.

Неприменимые пункты получают краткий `NO CHANGE`/`NOT APPLICABLE`.

### 4.1. Deterministic-first evidence

Предпочитать compiler, executable tests, offline validators, schema checks и
точные code anchors повторному модельному рассуждению. В packet/handoff
возвращать command, exit status, totals и только нужный failure tail; полный
successful log не сохранять.

### 4.2. Evidence reuse

- Targeted tests выполняются после relevant code/test change.
- Основной build/final verification выполняется на final code/test/build
  checkpoint и повторяется только если последующий diff затронул ту же
  execution boundary.
- Semantic Markdown/XML-doc diff запускает только применимые documentation и
  agent validators, если code/test/build boundary неизменна.
- State/timestamp/index-only commit не инвалидирует exact runtime/review
  evidence и сам по себе не запускает новый reviewer pass.
- Review-fix production/test change инвалидирует затронутое evidence и требует
  targeted плюс применимую final verification.

Reuse допустим только при recorded checkpoint, inspected paths и неизменённой
evidence boundary. При сомнении проверку повторить, а не предполагать PASS.

### 4.3. Independent-review applicability

Production reviewer обязателен для runtime/production/test behavior change и
для изменений, влияющих на ордера, риск, market data, Tester/live parity,
security или persistence. Documentation reviewer обязателен только при semantic
impact из conditional impact reference. State-only update после `CLEAN`
проверяет Main через diff/validator без нового reviewer pass.

## Conditional sections — читать только по trigger

| Trigger | Reference |
|---|---|
| Scoped production/documentation review или validation | `.agents/workflow/SCOPED_REVIEW.md` |
| Явный full repository review | `.agents/workflow/REPOSITORY_WIDE_REVIEW.md` + scoped finding schema |
| Semantic docs/XML docs, runtime impact или terminal handoff | `.agents/workflow/IMPACT_AND_HANDOFF.md` |
| Vendor/exchange/broker protocol, MCP/live/test-stand evidence | `.agents/workflow/EXTERNAL_AND_LIVE_EVIDENCE.md` |

Сначала выбери режим, затем читай только его reference и internal skill. Один
reference не является trigger другого, кроме явной зависимости full review от
scoped finding schema.
