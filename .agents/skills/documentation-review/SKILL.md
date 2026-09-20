---
name: documentation-review
description: Внутренняя методика native role documentation-reviewer для read-only проверки Markdown и C# XML Documentation относительно current code, tests и contracts.
---

# Семантическое ревью документации OsEngine

Проверяй не литературный стиль, а соответствие claims фактическому коду,
исполняемым тестам и authoritative sources. Неверный claim об order lifecycle,
market-data ordering, risk, persistence, security или Tester/live parity —
production-risk.

Это internal dependency native `documentation-reviewer`. Finding schema и
finite cycle задаёт `.agents/workflow/SCOPED_REVIEW.md`; Main не подменяет
independent read-only pass.

## Обязательные источники

1. Всегда: `AGENTS.md`, compact core, `SCOPED_REVIEW.md`, применимый
   `project/AGENTS.md` и `project/Documentation/DOCUMENTATION_MAP.md`.
2. Только релевантные current/accepted docs и domain `CONTEXT_*.md`.
3. `../csharp-xml-doc-style/SKILL.md` — при XML comments, genre/Tier или formal
   drift classification.
4. `EXTERNAL_AND_LIVE_EVIDENCE.md` — только при vendor/live/test-stand claim.

Не читай полный `ACTIVE_TASK.md`, старые case histories или successful logs,
если они не входят в inspected scope.

## Порядок

1. Для diff/PR/branch проверь changed docs/comments и непосредственно
   затронутые contracts/tests/execution paths. Для full documentation audit
   сначала зафиксируй конечный inventory.
2. Для каждого claim установи authority/status по `DOCMAP-001`.
3. Сверь Markdown/XML comments с implementation, configuration и реальной
   доказательной силой tests/test stands.
4. Проверь links, commands, paths, totals, mode names и safety qualifiers.
5. Классифицируй drift только как `DOC_STALE`, `CODE_DRIFT`,
   `ARCHITECTURE_DRIFT`, `TEST_EVIDENCE_DRIFT` или `MODE_PARITY_DRIFT`.
6. Верни structured findings по `SCOPED_REVIEW.md`, отдельно указав:
   - drift с `path:line` и competing claims;
   - отсутствующие/переобещающие XML comments;
   - незарегистрированные governed documents;
   - broken links/commands;
   - evidence boundary и terminal `CLEAN`, `NO_CHANGE` или `BLOCKED`.

Формат drift:

```text
DOCUMENTATION DRIFT DETECTED
Тип: <DOC_STALE|CODE_DRIFT|ARCHITECTURE_DRIFT|TEST_EVIDENCE_DRIFT|MODE_PARITY_DRIFT>
Где: <path:line или document ID>
Расхождение: <claim vs code/test/contract>
Authoritative источник: <ID/path/status и основание>
```

## Ограничения

- Только `AUDIT MODE`; файлы не изменять.
- Не исправлять behavior или документацию «заодно».
- Не присваивать новые document/invariant IDs.
- Не читать credentials/raw authenticated payload.
- Не выдавать общий verdict без конкретного claim и counterevidence.
