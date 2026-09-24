# Authoritative active task state

**ID:** `TASK-ORDER-FLOW-CLOUD-EXPLORER-GUIDED-UI-001`
**Статус:** `COMPLETED`
**Фаза:** `TERMINAL — CLEAN`
**Ветка:** `docs/order-flow-production-roadmap`
**Baseline HEAD:** `7942bdc527e3c4f2d3d79def002c44a7df5e850c`
**Dirty entry:** clean worktree; local branch matched
`origin/docs/order-flow-production-roadmap`.
**Completed transition IDs:** `SCOPE_ENTRY`, `IMPLEMENT_GUIDED_THEME_UI`, `VERIFICATION`, `PRIMARY`, `FIX`, `VALIDATION_1`, `TERMINAL`
**Next transition ID:** `AWAIT_OWNER`

## Frozen scope

Bring the separate Cloud Explorer workbench and its chart Window into the
current OsEngine theme, add Russian hover help for every operator-facing
control, and make the intended workflow understandable without external
instruction through compact numbered steps.

In scope: `CloudExplorerControl` layout/help/theme bindings, both Explorer
Window XAML hosts, targeted OrderFlowResearch UI assertions, a standalone
Russian Cloud Explorer operator guide plus its documentation-map/index links,
applicable runbook/qualification wording, this live state and resulting build
outputs.

Preserve all calculation, filtering, replay, artifact, identity, persistence,
threading and disposal semantics. Do not alter theme dictionaries or the
DarkOrange palette. Do not start OsEngine, MCP/test stands, connectors, live
sessions or orders. Physical hover/window appearance remains an owner-run
check. The owner authorized one ordinary commit of this verified boundary on
24.09.2026; push remains unauthorized.

## Acceptance

- The Explorer and detached chart use the same resizable Window chrome and
  dynamic theme resources as the main application.
- Editable and result DataGrids use the shared themed DataGrid style; text,
  panels and status areas remain readable in every built-in theme.
- The workbench presents an unambiguous numbered flow: verify inherited input,
  configure, run/manage, inspect results.
- Every operator-facing input, selector, toggle, button and result tab exposes
  a meaningful Russian hover description; editable option rows retain their
  visible explanation and expose it on hover.
- Existing element names and event ownership remain compatible; no calculation
  starts merely by opening the Window.
- A registered Russian Markdown guide leads a new operator through the complete
  workflow, result interpretation, replay, artifacts and common errors.
- Targeted offline tests, final solution build, validators and bounded
  independent production/documentation reviews finish on one exact checkpoint.

## Impact

WPF presentation and operator guidance only. Public API, market data, artifact
formats, calculations, trading, Tester/live behavior and security do not
change. `OBSERVABILITY: NO CHANGE`; existing progress/status/error paths remain.
`MODE PARITY: NO CHANGE` (offline OsData research UI only).

## Verification status

Entry branch, exact HEAD, upstream and clean worktree verified. Both Explorer
Windows now use the application resizable chrome and dynamic theme background;
all option/result DataGrids use the shared style. The workbench exposes four
visible steps, Russian help for interactive controls/result tabs and row-level
option help. The registered Russian operator guide covers setup, calculation,
results, chart/replay, artifacts and common errors.

Final Debug `dotnet build OsEngine.sln --nologo`: PASS, 0 errors / 21 existing
warnings. Final offline OrderFlowResearch suite after that build: 144 passed /
0 failed. The new markup fixture checks numbered flow, Russian hover help,
DataGrid/window style bindings and both Window hosts. Three XAML files parse;
all referenced palette keys exist in DarkOrange, Midnight, Tiffany and Gray;
no hard-coded Explorer color attribute was introduced. Changed-document local
links and guide Mermaid fences pass; `git diff --check` passes. Agent validator:
109/109.

Production PRIMARY: TERMINAL CLEAN, no findings. Documentation PRIMARY found
`CE2-GUIDED-DOC-001` (search help exceeded Cloud-ID behavior) and
`CE2-GUIDED-DOC-002` (two stale tab labels). Both wording defects were fixed
without runtime expansion; bounded documentation `VALIDATION_1`: CLEAN. Public
or protected C# contracts did not change, so XML-doc changes are not required.

## Blockers

No implementation blocker. Physical theme pixels, native hover timing, focus,
window chrome and the minimum-size layout remain `REQUIRES OWNER-RUN` visual
smoke. OsEngine, MCP/test stands, connectors, live sessions and orders were
NOT_RUN.

## Next action

Create the authorized ordinary commit of this verified terminal boundary, then
handoff the rebuilt application and operator guide for the owner's visual
click/hover check. Do not push without a separate explicit request.
