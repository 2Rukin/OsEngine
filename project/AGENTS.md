# AGENTS.md — OsEngine-specific правила

> Действует на `project/` и подкаталоги. Сначала прочитай корневой
> [`../AGENTS.md`](../AGENTS.md); этот файл только дополняет общий workflow
> правилами C#/.NET/WPF-кодовой базы.

## Перед работой

1. [`CONTEXT.md`](CONTEXT.md) — карта проекта.
2. [`CONTEXT_CODING_GUIDELINES.md`](CONTEXT_CODING_GUIDELINES.md) — стиль кода.
3. Доменный `CONTEXT_*.md` по задаче.
4. Работа с `OsEngine/Market/Servers/` —
   [`CONTEXT_CONNECTORS.md`](CONTEXT_CONNECTORS.md).
5. Архитектурные или documentation claims —
   [`Documentation/DOCUMENTATION_MAP.md`](Documentation/DOCUMENTATION_MAP.md).

## Принципы кода

- Делай минимальные изменения, сохраняй стиль, signatures и обратную
  совместимость без отдельного решения о breaking change.
- Используй существующие `BotPanel`, `BotTabSimple`, `IServer`, `AServer`,
  `ServerMaster`, `Aindicator`, `Journal`, `RiskManager`, Tester/Optimizer
  abstractions; не создавай параллельный lifecycle.
- Сохраняй BOM и исходные line endings. Не форматируй соседний legacy-код ради
  косметики.
- Учитывай фоновые callbacks, WPF dispatcher, подписку/отписку событий и
  thread-safe shared state.
- Исключения не проглатываются и проходят через штатное логирование.
- Не используй `var`; подробности и существующие исключения — только в
  `CONTEXT_CODING_GUIDELINES.md`.

## Сборка и тесты

Команды ниже выполняются из каталога `project/`:

```bash
# Основной production project
dotnet build OsEngine/OsEngine.csproj

# Всё solution — если затронуты Tests/*, test project files или release scope
dotnet build OsEngine.sln

# Offline agent-system acceptance — из корня репозитория
cd ..
bash .agents/validation/validate-agent-system.sh
```

Перед `dotnet build` на Windows завершить `OsEngine.exe`, если процесс держит
output-файлы.

### MCP test stand

```bash
cd Tests/McpTestStand/OsEngine.McpApi.TestStand/bin/Debug/net10.0
./OsEngine.McpApi.TestStand.exe --module Tester
./OsEngine.McpApi.TestStand.exe --module 5,6
```

Целевые totals current context: **196/196** (`--transport v2`) и **187/187**
(`--transport v1`). Перед запуском перепроверь current test registry, потому что
totals могут измениться вместе со стендом.

MCP test stand запускается только с явного разрешения пользователя. Он работает
foreground; не скрывай окно и не объявляй PASS до terminal summary. Те же
границы действуют для `StopOrdersTestStand`, live connectors и real-order
scenarios.

## Документация по области

Если меняешь:

- MCP API — `CONTEXT_MCP.md` и применимые MCP development docs;
- MCP scenarios — `CONTEXT_MCP_SCENARIO.md`;
- engine conventions — `CONTEXT_CODING_GUIDELINES.md`;
- project map — `CONTEXT.md`;
- connector behavior — `CONTEXT_CONNECTORS.md`;
- robot/indicator/tester behavior — соответствующий domain context;
- Order Flow target — `Documentation/OrderFlow/`;
- agent workflow — root `AGENTS.md`, `.agents/**` и
  `Documentation/AgentSystem/` по impact;
- новый governed document — зарегистрируй его в
  `Documentation/DOCUMENTATION_MAP.md`.

## Среда и permissions

- Целевая среда — Windows + Git Bash; production target — `net10.0-windows`.
- Реальные credentials не запрашиваются для обычной сборки или offline review.
- Нужное live/server/account evidence не обходится: остановись с точным
  `OWNER-RUN` handoff.
- Commit/push и опасные Git-операции регулируются корневым `AGENTS.md` и
  разрешением пользователя на текущую задачу.

## Чек-лист перед ответом

- [ ] Diff ограничен frozen scope и не затёр пользовательские изменения.
- [ ] Применимые build/tests/validators завершены либо честно помечены `NOT_RUN`.
- [ ] Документация и C# XML comments обновлены по semantic impact.
- [ ] Live/test-stand границы и секреты соблюдены.
- [ ] Commit/push status сообщён точно.
