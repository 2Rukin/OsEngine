# Authoritative active task state

**ID:** `TASK-ORDER-FLOW-CLOUD-CALIBRATION-SPEC-001`
**Статус:** `COMPLETE — DOCUMENTATION TARGET PUBLISHED`
**Фаза:** `TERMINAL HANDOFF — IMPLEMENTATION NOT_STARTED`
**Ветка:** `docs/order-flow-production-roadmap`
**Baseline HEAD:** `8903023f1d12a7c5fdc6c0d4027d58ac67f96a51`
**Dirty entry:** remote branch was clean at baseline; work performed through GitHub contents API.
**Completed transition IDs:** `SCOPE_ENTRY`, `CURRENT_CONTRACT_REVIEW`, `WRITE_SPEC`, `SCOPED_REVIEW_MAIN`, `FIX_REVIEW_FINDINGS`, `REGISTER_DOC`, `PUBLISH`
**Next transition ID:** `IMPLEMENT_CALIBRATION`

## Frozen scope

По запросу владельца оформить подробное ТЗ следующей итерации Order Flow:
предварительная статистика тиков и цепочек перед настройкой Cloud, визуальный
подбор `MinimumTickVolume / MaximumGapMilliseconds / MaximumRangeTicks`,
учёт времени торгового дня, новая описательная diagonal delta, независимые
Cloud rules для каждого временного диапазона и Cloud Anatomy.

Модуль обязан оставаться внутри `OsData → Order Flow`, а все новые таблицы
сразу выполнять `UI-DETACHED-TABLES-001`: таблицы открываются кнопками в
отдельных окнах, основная рабочая область сохраняет видимые команды и
визуальные графики. Оформление использует действующую theme system Order Flow
без hardcoded colors.

Разрешены только documentation changes:
`OrderFlow/CLOUD_CALIBRATION_SPEC.md`, `OrderFlow/README.md`,
`DOCUMENTATION_MAP.md` и этот active snapshot. Production code, tests,
binaries, trading behavior и user data не меняются. Commit/push в текущую
ветку явно разрешены владельцем; force запрещён.

## Decisions

- Preset source-clock ranges: FORTS Morning 07:00–10:30, Main 10:30–19:00,
  Evening 19:00–23:50; MOEX Morning 06:50–10:30, Main 10:30–19:00,
  Evening 19:00–23:50. Saturday и Sunday — отдельные source-date profiles.
  Custom range поддерживается.
- `US pre-open 60m` не делает молчаливого timezone inference: без явно
  выбранной source timezone он disabled; conversion учитывает DST.
- Chain semantics сохраняют current Order Flow: excluded small ticks не
  разрывают chain, gap считается от последнего included tick, range — общий,
  equality проходит, eligible breaking tick закрывает старую и начинает новую.
  `MinChainVolume` — post-formation filter.
- Diagonal сохраняет current exact-neighbor pair semantics. Новые
  `PairDiagonalDelta` / cumulative `DiagonalDelta` и strict stack считаются
  поверх сохранённой pair evidence; diagonal не формирует собственную
  сегментацию.
- Модель rule после review разделена на
  `FormationMode = Single|Chain` и `RuleKind = Standard|Diagonal`, чтобы
  diagonal rule всегда имел однозначный base event.
- Программа не выбирает winner. Heatmap, quantiles, pin/compare и
  NeighborSensitivity дают описательную информацию; конечные параметры
  выбирает пользователь.
- Future price reaction, MFE/MAE, PnL и trading execution исключены из этой
  итерации.

## Verification status

Current implementation/contracts inspected:
`OrderFlowClouds.cs`, `OrderFlowCloudImbalance.cs`,
`ExplorerCatalog.cs`, `ExplorerModels.cs`, `ExplorerMetrics.cs`,
`OrderFlowResearchUi.xaml`, `CLOUD_EXPLORER_V2_SPEC.md`,
`CLOUD_EXPLORER_FOLLOWUP_SPEC.md`, `UI-DETACHED-TABLES-001` и
`CONTEXT_THEMES.md`.

Scoped semantic review by Main found and fixed before final handoff:

- `OF-CAL-DOC-001`: ambiguous `SourceMode=Diagonal` mixed segmentation and
  filter kind. Fixed by separate FormationMode/RuleKind.
- `OF-CAL-DOC-002`: session edge wording did not define a deterministic
  23:50 boundary. Fixed with half-open intervals and explicit Evening
  EndExclusive=23:51 while UI displays 23:50 inclusive.
- Timezone claim checked against current data contract: source clock remains
  unlabelled unless user explicitly supplies timezone.
- Table/layout/theme requirements checked against current target contracts;
  no embedded analytical DataGrid is allowed in the new main calibration
  workspace.

Independent native `documentation-reviewer` is not exposed in this chat
runtime, therefore the repository's **independent-review gate is formally
NOT_RUN/BLOCKED**, not self-certified CLEAN. The implementation task must run
the mandated independent documentation and production reviews before claiming
terminal completion.

Build/tests: NOT_RUN — documentation-only change. Agent validator and local
`git diff --check`: NOT_RUN in connector-only environment. Direct document
paths and registration were re-read from the remote branch after publication.

## Blockers

Нет blocker для публикации target ТЗ. Для будущей реализации обязательны
independent reviews, offline OrderFlowResearch tests, solution build и
Windows/DPI owner acceptance по документу.

## Next action

Реализовать `ORDER-FLOW-CLOUD-CALIBRATION-001` по этапам A–H, сохранив
legacy Cloud/Explorer semantics. Начать с TimeRange + Tick Distribution,
затем bounded Parameter Explorer, diagonal metrics, per-range Cloud rules,
Cloud Anatomy и detached table UI.
