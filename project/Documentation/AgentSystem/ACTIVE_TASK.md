# Authoritative active task state

**ID:** `TASK-ORDER-FLOW-RESEARCH-MVP-001`
**Статус:** `BLOCKED`
**Фаза:** `TERMINAL`
**Обновлено:** 21.09.2026 12:49:25 (UTC)
**Ветка:** `docs/order-flow-production-roadmap`
**Baseline:** `1470e30428299628d5c6861e3811c75885e28170`
**State version:** terminal implementation checkpoint; owner-run evidence absent
**Completed transition IDs:** `SCOPE_FREEZE,CURRENT_IMPLEMENTATION_MAP,RESEARCH_CORE_IMPLEMENTATION,WORKBENCH_IMPLEMENTATION,DOCUMENTATION_SYNC,STATIC_VALIDATION,PRODUCTION_REVIEW_PRIMARY,DOCUMENTATION_REVIEW_PRIMARY,PRIMARY_REVIEW_FIX,STATIC_REVALIDATION,BOUNDED_VALIDATION_1,VALIDATION_1_FIX,BOUNDED_VALIDATION_2,TERMINAL_STATIC_VALIDATION`
**Next transition ID:** `OWNER_RUN_VERIFICATION`

## Цель

Реализовать полностью офлайн-первичный Order Flow Research MVP, чтобы владелец
мог на одной паре QSH `Deals + Quotes` проверить качество данных, причинный
replay, смысл минимальных признаков и broad Long/Short candidates, отдельно
увидеть будущий market path и визуально разобрать эпизоды без создания заявок.

## Frozen scope

Разрешены:

- новый изолированный research/data модуль в `project/OsEngine/OsData/OrderFlow/**`;
- минимальная точка входа и окно workbench в `project/OsEngine/OsData/**`;
- offline deterministic test stand в `project/Tests/OrderFlowResearch/**` и его
  регистрация в solution;
- обновление только применимых Order Flow/current navigation документов,
  `DOCMAP-001` и этого snapshot;
- build и запуск нового offline test stand без внешних соединений.

Запрещены:

- торговые заявки, execution/PnL, risk controller, robot, live/shadow/paper;
- изменение существующего Tester execution либо Optimizer;
- MCP test stand, OsEngine runtime launch, live connectors и credentials;
- внешнее upstream-исследование, force push и переписывание истории;
- автоматическая публикация или коммит raw market data.

## Acceptance

- пользователь выбирает ровно одну согласованную пару `.Deals.qsh` и
  `.Quotes.qsh`, настраивает ограниченный `ResearchSpec` и запускает анализ из
  OsData;
- malformed/incomplete/mismatched pair отклоняется с reason codes, а отчёт
  содержит checksums, metadata, counts, time ranges и quality counters;
- два reader объединяются по закрытым timestamp buckets, previous closed quote
  используется для deal/book features, same-bucket quote не создаёт look-ahead;
- повторный прогон даёт те же event/feature/candidate hashes;
- минимальные causal features включают delta, price change/response, spread,
  top-N imbalance и book age;
- broad Long/Short candidate не является сделкой, failed Long не создаёт Short;
- future MFE/MAE/barrier labels физически экспортируются отдельно от causal
  observations/features;
- workbench показывает summary, event journal, candidates и один и тот же набор
  маркеров на `Sec15`, `Sec30` и `Min1`; display timeframe не меняет расчёты;
- CSV/JSON artifacts воспроизводимы и не содержат execution PnL claims;
- synthetic/golden/no-look-ahead/determinism tests, production build, solution
  build и обязательные validators проходят;
- independent production и documentation review получают terminal outcome.

## Последний завершённый шаг

Оба независимых validation round 2 получили `CLEAN`. Все findings
`OFR-PR-001..005`, `OFR-V1-001` и `OF-DOC-PRIMARY-001..003` закрыты на
проверенной статике; terminal static validation прошла.

## Текущий незавершённый шаг

Обязательные .NET build и executable owner scenarios не выполнены, потому что
в среде отсутствуют SDK/MSBuild/C# compiler. До этого task остаётся `BLOCKED`,
несмотря на terminal `CLEAN` обоих bounded reviews.

## Принятые решения

- первый MVP является research workbench, а не торговым роботом;
- существующий Tester execution не расширяется до появления и отдельной
  квалификации execution model;
- визуализация является presenter над результатом ядра и не пересчитывает
  признаки/candidates;
- raw QSH fixtures не добавляются; test stand генерирует минимальные synthetic
  QSH пары локально и удаляет временные файлы после прогона.

## Relevant evidence

- baseline/branch/status check: `PASS`, clean at scope freeze;
- authoritative contracts: `ORDER-FLOW-DATA-001`,
  `ORDER-FLOW-RESEARCH-001`, `ORDER-FLOW-STRATEGY-001`,
  `ORDER-FLOW-QUALIFICATION-001`;
- current implementation map: `PASS` — target components отсутствуют; QSH
  readers пригодны для переиспользования формата, но Tester replay однопоточный;
- production/tests/docs: `NOT_RUN` before implementation.

## Verification status

- Independent current-code map: `PASS` относительно exact baseline.
- Research core/workbench/artifact implementation: `ADDED`.
- C# lexical delimiter scan: `PASS — 11 files`.
- XAML/csproj XML parse: `PASS — 3 files`.
- Solution registration/configuration map: `PASS — 12/12 mappings`.
- Agent-system validator: `PASS — 109 checks`.
- Local Markdown links/fences: `PASS — 60 local links, 11 files`.
- `git diff --check` and forbidden-pattern scan: `PASS`.
- Synthetic offline test stand: `ADDED — 15 scenarios, NOT RUN`.
- Production project/solution build: `NOT RUN — dotnet/MSBuild отсутствуют в
  текущей Linux-среде`.
- OsEngine UI/manual real-QSH scenario: `NOT RUN — owner-run evidence required`.
- MCP/live connector/real orders: `NOT RUN — outside frozen scope`.
- Independent production validation 2: `CLEAN — OFR-PR-001..005 and
  OFR-V1-001 closed`.
- Independent documentation validation 2: `CLEAN — OF-DOC-PRIMARY-001..003
  closed; 12 targeted XML blocks well formed`.
- Observability: `REQUIRED/SATISFIED FOR RESEARCH MVP — reason codes, counters,
  journal, correlations and deterministic hashes; full-day performance NOT RUN`.
- Mode parity: `NO CHANGE — Tester/live/Optimizer untouched; parity not proven`.

## Blockers

Локальная среда не содержит .NET SDK/MSBuild; обязательное build/test evidence
нельзя получить здесь и потребуется выполнить на Windows/.NET 10 checkout.

## Next action

`OWNER_RUN_VERIFICATION` из каталога `project/` на Windows/.NET 10:
`dotnet run --project Tests/OrderFlowResearch/OsEngine.OrderFlowResearch.Tests.csproj`,
`dotnet build OsEngine/OsEngine.csproj`, `dotnet build OsEngine.sln`, затем
ручной запуск workbench на одной реальной паре и визуальная сверка artifacts.
