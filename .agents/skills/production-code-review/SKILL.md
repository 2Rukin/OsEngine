---
name: production-code-review
description: Внутренняя методика native role production-code-reviewer для read-only production-safety проверки реализованного OsEngine scope.
---

# Критическое production-safety review OsEngine

Ищи доказанные сценарии торгового, финансового, data, lifecycle, security и
operational отказа. Не превращай review в style lint или список будущих идей.

## Режим

До чтения production-кода выбери ровно один режим:

- `SCOPED` — конкретный diff/module/execution path;
- `REPOSITORY_WIDE` — явный полный audit реализованной кодовой базы/подсистемы.

`SCOPED` загружает `SCOPED_REVIEW.md`; `REPOSITORY_WIDE` дополнительно
загружает только `REPOSITORY_WIDE_REVIEW.md`.

## Обязательные источники

1. `AGENTS.md`, compact core, применимый `project/AGENTS.md` и workflow
   выбранного режима.
2. `project/Documentation/DOCUMENTATION_MAP.md`, затем только релевантные
   current/accepted contracts и contexts.
3. Полностью `../production-review-checklist/SKILL.md`.
4. `EXTERNAL_AND_LIVE_EVIDENCE.md` только при protocol/live/test-stand trigger.
5. Current code/tests/config. Researcher output — только navigation index;
   substantive claims перепроверяются.

Не читай active task/history/full logs, если они не входят в review scope. В
repository-wide режиме допустимы index и report той же identity для
deduplication, но не как source of truth current code.

## Порядок

1. Выдели только реализованные modules и их runtime/test boundaries. Roadmap и
   target-only компоненты не являются отсутствующими defects.
2. В `SCOPED` примени каждую релевантную checklist category ко всей затронутой
   execution boundary, включая negative/failure/cleanup paths.
3. В `REPOSITORY_WIDE` сначала создай inventory, затем coverage matrix
   `module × category`; каждая cell terminal: `CHECKED`, `NOT_APPLICABLE` или
   `BLOCKED`.
4. Full review выполняет один `PRIMARY_FULL_REVIEW` и один
   `COVERAGE_VALIDATION`, без второго broad pass.
5. Объединяй один execution path под stable Finding ID. Для каждой finding
   верни все поля `SCOPED_REVIEW.md`, включая mode impact, evidence,
   counterevidence, reachability, action, owner decision и Mermaid sequence.
6. Недоказанная серьёзная hypothesis получает `REQUIRES_EVIDENCE`, а не
   `MUST_FIX`; отсутствующее live evidence не симулируется.
7. Верни baseline/dirty boundary, inspected scope, evidence, blockers и один
   terminal outcome. Reviewer не меняет files и не начинает fix.

## Ограничения

- Read-only. Fix выполняет Main только после owner-approved scope.
- Не повышай severity из-за потенциального убытка без достижимого path.
- Не называй Tester result доказательством live parity или profitability без
  соответствующего evidence.
- Не запускай build/test stand/live connector и не читай credentials.
- Если same-identity full review terminal, не повторяй его без deliberate
  re-audit владельца.
