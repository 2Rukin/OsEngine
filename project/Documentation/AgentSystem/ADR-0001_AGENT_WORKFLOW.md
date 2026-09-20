# ADR-AGENT-001: адаптировать agent workflow TXML Market к OsEngine

**Статус:** ACCEPTED
**Дата:** 20.09.2026 20:42:41 (UTC+03:00, Москва)
**Scope:** repository engineering workflow; production runtime OsEngine не
меняется.

## Контекст

TXML Market содержит полезный agent-system contour: общий canonical layer,
read-only research/review roles, compact task state, finite review automaton,
documentation review, JavaDoc standard, deterministic fixtures и thin
Codex/Claude adapters.

Прямое копирование некорректно. OsEngine — C#/.NET 10 Windows/WPF монолит с
несколькими connectors, Tester/Optimizer/OsData/OsTrader, MCP test stand и
legacy code style. Java/FFM/Maven/TRANSAQ-only rules и unrestricted project
permissions здесь были бы ложными или опасными.

## Решение

### 1. Canonical layer и scope

Создать root `AGENTS.md` как общий router. Существующий `project/AGENTS.md`
сохранить как directory-scoped OsEngine supplement. Общие skills находятся
только в `.agents/skills/`; Codex/Claude files являются thin adapters.

Main остаётся единственной write-capable ролью. Три роли работают read-only:

- `codebase-researcher`;
- `production-code-reviewer`;
- `documentation-reviewer`.

### 2. Finite workflow

`AGENT-WORKFLOW-001` владеет routing, live state, frozen scope, completion и
evidence reuse. Scoped review ограничен:

```text
PRIMARY -> FIX -> VALIDATION_1 -> TERMINAL
```

Один conditional `VALIDATION_2` допустим только для regression/proof error/
незавершённого in-scope path. Full review имеет отдельный конечный cycle и
exact review identity. Findings не создают fix scope без owner decision.

### 3. OsEngine domain adaptation

Production checklist проверяет не один connector, а платформу целиком:

- market-data identity/time/order/units;
- causality и Tester/Optimizer/live parity;
- financial precision и instrument metadata;
- order lifecycle, idempotency и reconciliation;
- positions, portfolio и risk;
- connectors/protocol/rate limits;
- event/thread/WPF lifecycle;
- persistence/backward compatibility;
- MCP/security/destructive operations;
- observability, performance и test evidence.

Нереализованный `ORDER-FLOW-ROADMAP-001` не считается дефектом. При его будущей
реализации эти же categories применяются к common engine, tester execution,
visualization boundary и live rollout.

### 4. Documentation adaptation

`javadoc-style` заменён на `csharp-xml-doc-style`. Обязательны semantic comments
для нового/изменённого public/protected и safety-critical contract; массовый
backfill нетронутого legacy code запрещён. Generated C# не редактируется.

Review diagrams используют Mermaid. Documentation map отличает accepted/current
sources, target roadmap и baseline-bound reports.

### 5. External/live boundary

Нет единственного vendor artifact. Official vendor specification применяется
только к соответствующему connector/API scope. MCP test stand, live connector,
credentials/account и real orders требуют отдельного разрешения. Missing
evidence закрывается `REQUIRES LIVE CONNECTOR/TEST STAND/OWNER-RUN`.

### 6. Permission model

Не переносить TXML `.codex/config.toml` с `danger-full-access` и отключением
approval. Native reviewers получают `read-only`; Main использует обычные
permission boundaries среды и правила пользователя.

### 7. Acceptance

Repository-local Bash validator проверяет topology, routing, read-only adapters,
finite-review fixtures, task-state bounds и отсутствие stale Java/TXML routes.
Он не заменяет semantic reviewer и не запускает production application.

## Отклонённые варианты

| Вариант | Решение | Причина |
|---|---|---|
| Скопировать TXML files поиском/заменой | REJECT | Сохраняет неверные Java/FFM/vendor assumptions |
| Один write-capable «reviewer» | REJECT | Нет независимой read-only boundary |
| Review до отсутствия любых замечаний | REJECT | Бесконечный scope drift |
| Автоматически запускать MCP/live tests | REJECT | Credentials, process и real-order risk |
| Требовать XML comments на весь legacy code | REJECT | Огромный noise diff без текущего semantic value |
| Перенести `danger-full-access` | REJECT | Не нужен системе и расширяет полномочия проекта |

## Последствия

Положительные:

- одинаковый workflow для code, docs и review;
- resumable state в Git;
- bounded independent review с owner triage;
- OsEngine-specific safety checklist и честная live-evidence граница;
- единый C# documentation standard без массовой перезаписи legacy.

Стоимость:

- нетривиальные задачи обновляют `ACTIVE_TASK.md`;
- runtime/safety changes требуют независимого reviewer;
- full review сохраняет governed report/index;
- validator гарантирует structure/contracts, но semantic качество всё равно
  требует code/test evidence и человеческого решения владельца.
