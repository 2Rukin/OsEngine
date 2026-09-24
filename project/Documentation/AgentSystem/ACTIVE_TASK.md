# Authoritative active task state

**ID:** `TASK-ORDER-FLOW-CLOUD-EXPLORER-WINDOW-001`
**Статус:** `COMPLETED`
**Фаза:** `TERMINAL — CLEAN`
**Ветка:** `docs/order-flow-production-roadmap`
**Baseline HEAD:** `a6cca6bccd65d624bde5c4ae0bad957ef01dff31`
**Dirty entry:** completed Cloud Explorer V2 implementation remains uncommitted.
The immediately preceding scale-dialog change is also present and is explicitly
superseded by the owner's clarification in this task. No unrelated owner edits
were detected.
**Completed transition IDs:** `SCOPE_ENTRY`, `IMPLEMENTATION`, `VERIFICATION`, `PRIMARY`, `TERMINAL`
**Next transition ID:** `AWAIT_OWNER`

## Frozen scope

Restore the original inline Cloud Explorer multi-scale checkbox and remove the
separate scale-settings dialog. Change only the entry behavior of the existing
`Исследование Cloud` result tab: selecting it opens the complete Cloud Explorer
workbench in a separate modeless owned Window instead of embedding the workbench
inside the tab. Existing Cloud Explorer V2 calculation, storage, replay,
filtering and chart behavior remains unchanged.

In scope: `OrderFlowResearchUi` Explorer routing, one dedicated Explorer host
Window, exact reversion of the scale-dialog UI/code/tests/docs, targeted
`Tests/OrderFlowResearch/**`, applicable Cloud Explorer runbook/spec/
qualification wording, this live state and resulting build outputs.

No commit/push authorization. Do not start OsEngine, MCP/test stands, brokers,
connectors, live sessions or orders. Physical window/mouse/focus evidence is
owner-run; deterministic tests may construct an unshown Window object.

## Acceptance

- The inline `Узкий / базовый / широкий` checkbox and prior profile workflow are restored.
- The scale-settings button/dialog and their files are removed.
- Selecting `Исследование Cloud` immediately returns the main workbench to its
  previous result tab and opens the entire Explorer in a separate modeless Window.
- Only one Explorer Window exists per parent; another selection activates it.
- Closing either Explorer or its parent cancels/disposes Explorer-owned work and
  detaches handlers; selecting the launcher later creates a fresh Window.
- Existing Cloud V2 calculations, bundles, replay, filters and legacy UI are unchanged.
- Targeted offline tests, final solution build, validators and bounded independent
  production/documentation reviews complete on one exact checkpoint.

## Impact

WPF UI entry and lifecycle only. No market-data, persistence format, trading,
Tester/live, hash formula or computation semantics change.
`OBSERVABILITY: NO CHANGE`; existing status/log error paths remain.
`MODE PARITY: NO CHANGE` (offline OsData research UI only).

## Verification status

Entry branch/HEAD/status inspected. The superseded scale dialog and button are
removed; the original inline checkbox/profile workflow is restored. The result
tab now delegates to a bounded launcher that returns the prior selection, owns
one modeless Explorer Window, activates/reuses it and releases it on close or
parent disposal. The Window hosts the complete existing Explorer control.

Release OrderFlowResearch project build: PASS, 0 errors / 21 existing warnings.
Offline OrderFlowResearch suite after the final runtime change: 143 passed /
0 failed. The new component path constructs unshown Window objects and proves
return-to-prior-tab, singleton reuse, release/reopen and hosted-control disposal.
The first test run exposed only an unavailable app icon in the no-Application
fixture (142/143); removing that optional Window icon produced the clean rerun.
Full Release solution build: PASS, 0 errors / 31 existing warnings. The initial
normal Debug attempt respected the owner-running OsEngine process and did not
terminate it. After the owner process exited, the canonical Debug solution build
passed with 0 errors / 21 existing warnings and rebuilt the normal application
and OrderFlowResearch outputs. Final Debug offline rerun: 143 passed / 0 failed.

Agent validator: 109/109. Four XAML files, nine relevant XML-doc blocks and
34 local Markdown links parse; `git diff --check` passes. Frozen 12-file PRIMARY
boundary: `.tmp/cloud-explorer-window-primary-checkpoint.json`; both superseded
scale Window paths are recorded absent. Production PRIMARY: TERMINAL CLEAN,
12/12 hashes and both removals verified; lifecycle, scale semantics,
observability and mode parity have no findings. Documentation/XML PRIMARY:
TERMINAL CLEAN, 12/12 hashes, operator flow, Mermaid, XML Tier and evidence
boundary verified; no drift finding. No FIX/VALIDATION round was required.

## Blockers

No implementation blocker. Physical window display, mouse, focus, activation
and owned-window behavior remain `REQUIRES OWNER-RUN`; deterministic component
evidence constructs unshown Window objects. OsEngine, MCP/test stands,
connectors, live sessions and orders were NOT_RUN.

## Next action

The owner authorized an ordinary commit and fast-forward publication of this
verified terminal boundary on 24.09.2026. After that repository operation,
wait for the next explicit action.
