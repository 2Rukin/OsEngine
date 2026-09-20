---
name: codebase-research
description: Внутренняя методика native role codebase-researcher для проверяемой карты current checkout OsEngine без оценки качества и изменения файлов.
---

# Исследование кодовой базы OsEngine

Построй точную карту текущей реализации. Отделяй факты code/tests/config от
целевой архитектуры, roadmap, historical notes и неподтверждённых claims.

Это internal dependency native `codebase-researcher`. Выполнение методики в
write-capable Main не является независимым read-only pass.

## Порядок

1. Прочитай `AGENTS.md`, `.agents/CLI_WORKFLOW_CONTRACT.md` и применимый
   `project/AGENTS.md`. Conditional references не загружай без trigger.
2. Зафиксируй caller scope, current `HEAD`, branch, status, baseline и exact
   dirty boundary.
3. Прочитай `project/Documentation/DOCUMENTATION_MAP.md`, затем только
   релевантные current/accepted documents и domain `CONTEXT_*.md`. Не загружай
   прошлые review reports автоматически.
4. Найди production classes, registration/config paths, tests/test stands и
   runtime entry points. Для робота/индикатора проверь Tester, Optimizer и live
   integration paths; для connector — lifecycle/events/orders/data; для MCP —
   transport, auth и target module.
5. Сверь каждый существенный documentation claim с code или executable test.
6. Верни evidence bundle:
   - scope, `HEAD`/baseline и worktree boundary;
   - карту «document/contract → implementation/tests»;
   - relevant files, symbols и реальные entry points;
   - implemented/partial/target-only классификацию;
   - установленные mode differences;
   - явный drift без оценки качества;
   - вопросы, неразрешимые чтением checkout.

## Ограничения

- Не оценивай качество и не предлагай fix.
- Не изменяй и не сохраняй файлы; верни результат caller.
- Не считай upstream, roadmap или старый report фактом current fork.
- Не запускай OsEngine, connector или test stand.
- Не читай secret-bearing local files.
- Если факт не установлен, пиши `NOT_DETERMINED`, не домысливай.
