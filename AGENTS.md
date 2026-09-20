# AGENTS.md

Канонические общие инструкции для ChatGPT/Codex, Codex CLI и Claude Code в
форке `2Rukin/OsEngine`. Файл действует на весь репозиторий. Для каталога
`project/` дополнительно действует [`project/AGENTS.md`](project/AGENTS.md).

Правило сопровождения: **один факт — один канонический файл**. Native adapters
и skills только маршрутизируют к общим источникам и не копируют проектные
правила своими словами.

Все пути ниже заданы от корня репозитория. Код и документация внешнего
`AlexWan/OsEngine` не являются текущим checkout или готовым решением для этого
форка. Внешний upstream исследуется только по явной задаче пользователя.

## 1. Начало задачи

1. Проверь branch, `HEAD`, `git status` и релевантный diff. Не перезаписывай
   пользовательские изменения и не доверяй описанию вместо current checkout.
2. Для нетривиальной engineering-задачи полностью прочитай компактное ядро
   [`.agents/CLI_WORKFLOW_CONTRACT.md`](.agents/CLI_WORKFLOW_CONTRACT.md),
   затем сопоставь
   [`ACTIVE_TASK.md`](project/Documentation/AgentSystem/ACTIVE_TASK.md) с Git.
   Условные workflow-файлы загружай только по trigger-таблице ядра.
3. Для работы в `project/` прочитай `project/AGENTS.md`, `project/CONTEXT.md`,
   `project/CONTEXT_CODING_GUIDELINES.md` и только применимые доменные
   `CONTEXT_*.md`.
4. Для архитектуры, документации, lifecycle, безопасности, тестовых claims или
   торгового поведения определи authoritative-источники через
   [`DOCMAP-001`](project/Documentation/DOCUMENTATION_MAP.md).
5. Если задача совпадает с internal skill из `.agents/skills/`, полностью
   прочитай его `SKILL.md` до действий.
6. Соблюдай выданные полномочия: анализ не разрешает исправление; изменение
   файлов не разрешает commit/push; build не разрешает запуск терминала,
   коннектора, MCP test stand или реальные торговые операции.

## 2. Проект и проверка

OsEngine — C#/.NET 10 Windows/WPF торговая платформа. Основной код, solution и
тестовые стенды находятся в `project/`.

Базовые команды из корня репозитория:

```text
dotnet build project/OsEngine/OsEngine.csproj
dotnet build project/OsEngine.sln
bash .agents/validation/validate-agent-system.sh
```

- Основной `.csproj` собирается после изменений production-кода или build
  boundary.
- Всё solution собирается после изменений `project/Tests/**`, project files
  тестовых стендов или перед явно запрошенной release-проверкой.
- Documentation/agent-only diff не требует сборки приложения; для него
  обязательны agent validator, `git diff --check` и проверка ссылок/scope.
- Перед локальной сборкой на Windows завершить `OsEngine.exe`, если он держит
  output-файлы.
- MCP test stand, `StopOrdersTestStand`, live connector, реальные credentials,
  real orders, paper/live session и иные внешние стенды запускаются только по
  явному разрешению пользователя на конкретный сценарий.

Фактические команды и ограничения уточняются в `project/AGENTS.md` и доменном
контексте. Успешная компиляция не доказывает экономическую состоятельность
стратегии, parity Tester/live или безопасность реальной торговли.

## 3. Источники истины

Иерархия и статусы документов определены только в `DOCMAP-001`.

- Принятый ADR/architecture contract задаёт обязательное целевое решение.
- Current code и исполняемый тест задают факт текущей реализации.
- `CONTEXT_*.md` задают навигацию и действующие соглашения в пределах статуса
  карты, но их behavioral claims всё равно сверяются с кодом.
- `TARGET`, roadmap, backlog и review report не описывают реализованное
  поведение.
- Agent instructions управляют процессом, но не являются источником торговой
  архитектуры.

Не выбирай сторону при конфликте молча. Зафиксируй source hierarchy и
documentation drift. Не создавай `INV-*`, readiness gate или document ID без
регистрации в `DOCMAP-001` и отдельного архитектурного основания.

## 4. Agent roles и skills

Main остаётся единственной write-capable ролью и сам выполняет design,
implementation и fix. Пользователь формулирует задачу, а не обязан выбирать
внутренний skill.

Три независимые роли работают read-only:

| Intent | Native role | Каноническая методика |
|---|---|---|
| Карта current checkout | `codebase-researcher` | `.agents/skills/codebase-research/SKILL.md` |
| Production-safety review | `production-code-reviewer` | `.agents/skills/production-code-review/SKILL.md` |
| Semantic docs/XML-doc review | `documentation-reviewer` | `.agents/skills/documentation-review/SKILL.md` |

`production-review-checklist` и `csharp-xml-doc-style` — internal helpers, а не
самостоятельные write-capable агенты. Прямое чтение review skill в Main не
заменяет обязательную независимую read-only проверку. Если обязательная роль
недоступна, outcome — `BLOCKED`, а не самосертификация.

Reviewer получает compact packet: frozen scope, exact baseline/dirty boundary,
changed/direct paths, применимые document IDs и краткие verification verdicts.
Он не читает полный active state или старые отчёты, если они не входят в scope.

Repository-wide review — отдельный конечный режим. Повтор той же review identity
не запускается автоматически; правила находятся в
`.agents/workflow/REPOSITORY_WIDE_REVIEW.md`.

## 5. Документация и C# XML Documentation

Перед semantic Markdown или XML-doc изменением прочитай `DOCMAP-001` и только
релевантные current/accepted источники.

**IMPLEMENTATION MODE** меняет поведение. В том же diff обновляются применимые
XML comments и current-документы, если затронуты public/protected contract,
trading/order/risk semantics, lifecycle, concurrency, persistence format,
security, Tester/live parity, qualification command или test-evidence boundary.

**AUDIT MODE** проверяет документацию без изменения поведения, signatures,
assertions, project files и runtime config. Найденные проблемы возвращаются как
findings; они не исправляются «заодно».

Стандарт жанров и Tier находится только в
`.agents/skills/csharp-xml-doc-style/SKILL.md`. Он не требует массового backfill
нетронутого legacy-кода и не разрешает редактировать generated `.cs`.

Documentation drift классифицируется так:

- `DOC_STALE` — документ/комментарий отстал от корректного current code;
- `CODE_DRIFT` — код отклонился от принятого контракта;
- `ARCHITECTURE_DRIFT` — конфликт равноправных architecture sources;
- `TEST_EVIDENCE_DRIFT` — claim сильнее или отличается от реального теста;
- `MODE_PARITY_DRIFT` — Tester/Optimizer/shadow/live выполняют разные правила,
  хотя документация утверждает parity.

## 6. Безопасность и внешние системы

- Никогда не читать, не печатать и не коммитить реальные API keys, passwords,
  tokens, `test-secrets.json`, `tinvest-token.txt`, `Engine/*Params.txt` и иные
  локальные secret-bearing файлы.
- Не помещать raw authenticated requests/responses или master password в log,
  exception, XML docs, review report или agent state.
- Не включать MCP host и не ослаблять allowlist/auth/encryption ради теста.
- Не подключаться к брокеру/бирже и не отправлять/отменять заявки без явного
  разрешения пользователя на точный сценарий и account/environment.
- Недоступное live evidence обозначается `REQUIRES LIVE CONNECTOR` или
  `REQUIRES OWNER-RUN`, а не оптимистическим PASS.

## 7. Git и границы изменений

- Не выполнять commit, push, reset, rebase, merge или force-update без явного
  разрешения пользователя на текущую задачу.
- При разрешённом обновлении ветки использовать обычный fast-forward push;
  force запрещён, если пользователь отдельно его не запросил.
- Сохранять пользовательский dirty worktree и кодировку файлов. Не менять BOM
  и CRLF/LF массово.
- Не менять production behavior под видом documentation/review-задачи.
- Новый ADR, architecture contract, общий skill, native adapter, review report
  или workflow регистрировать в `DOCMAP-001` в том же diff.

## 8. Terminal handoff

Верни только применимые сведения: результат, baseline/current HEAD, изменённые
components, выполненное evidence с totals, review outcome, docs/XML-doc/
observability impact, blockers/`NOT_RUN`, commit и push status. Не выдавай
непроверенные live, trading или profitability claims за подтверждённые.
