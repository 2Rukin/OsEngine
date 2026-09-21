# Authoritative active task state

**ID:** `TASK-ORDER-FLOW-RESEARCH-MVP-001`
**Статус:** `IN_PROGRESS`
**Фаза:** `OWNER_RUN_VERIFICATION`
**Обновлено:** 21.09.2026 21:17:35 UTC
**Ветка:** `docs/order-flow-production-roadmap`
**Baseline:** `1470e30428299628d5c6861e3811c75885e28170`
**HEAD перед публикацией / hardening и UI baseline:** `dde136f1f7f3016eb43b14df3e9b3933e80e9419`
**State version:** UI defects fixed; ordinary build updated; independent reviews CLEAN; owner interaction check pending
**Completed transition IDs:** `SCOPE_FREEZE,CURRENT_IMPLEMENTATION_MAP,RESEARCH_CORE_IMPLEMENTATION,WORKBENCH_IMPLEMENTATION,DOCUMENTATION_SYNC,STATIC_VALIDATION,PRODUCTION_REVIEW_PRIMARY,DOCUMENTATION_REVIEW_PRIMARY,PRIMARY_REVIEW_FIX,STATIC_REVALIDATION,BOUNDED_VALIDATION_1,VALIDATION_1_FIX,BOUNDED_VALIDATION_2,TERMINAL_STATIC_VALIDATION,HARDENING_IMPLEMENTATION,HARDENING_PRIMARY,HARDENING_FIX,HARDENING_VALIDATION_1,HARDENING_FINAL_VERIFICATION,OWNER_QSH_FAILURE_AUDIT,OWNER_QSH_REPLAY_VERIFICATION,UPDATED_QSH_WINDOW_ANALYSIS,WORKBENCH_USABILITY_IMPLEMENTATION,UI_PRIMARY,UI_FIX,UI_VALIDATION_1,UI_FINAL_VERIFICATION`
**Next transition ID:** `OWNER_RUN_VERIFICATION`

## Цель

Офлайн Order Flow Research MVP: одна локальная пара Deals + Quotes QSH,
causal features, broad Long/Short candidates, отдельные future labels и
визуальный разбор без заявок. Последний scope — исправление сводки,
понятности параметров/графика и навигации по всему файлу по feedback владельца.

## Frozen scope

- Main: `OsEngine/OsData/OrderFlow/**`, изолированный вход OsData,
  `Tests/OrderFlowResearch/**`, применимые current docs и snapshot.
- Последний UI diff: Chart.cs, Ui.xaml, Ui.xaml.cs, ViewModels.cs,
  два offline regression tests и resource entries test csproj, MVP runbook.
- Разрешены offline replay/tests и build. Владелец 22.09 разрешил обновить
  обычную сборку после закрытия приложения; bin/Debug обновлён.
- Запрещены OsEngine runtime launch агентом, live/shadow/paper, orders,
  external connectors/MCP/StopOrders stands, credentials, execution/PnL,
  изменение Tester/Optimizer, upstream research, commit/push без разрешения.
- 22.09 владелец явно разрешил commit всех текущих изменений и обычный push
  в origin/docs/order-flow-production-roadmap. Raw QSH/artifacts не публикуются.

## Acceptance и решения

- QSH pair/header validation, closed buckets и previous-closed book.
- Reason-coded rejected bundles с metadata/checksums обеих ролей.
- Один read-only handle для hash и replay; observation identity input/spec scoped.
- Research-only: stale/missing book даёт предупреждение, candidate сохраняется.
- Future labels физически отделены от causal observations; PnL не рассчитывается.
- Display timeframe, viewport/zoom/selection не меняют engine output.
- Сводка растянута, прокручивается, переведена; динамические пути/коды сохранены.
- График: числовые шкалы, пояснения панелей и цветов, tooltip значений,
  scroll/wheel, начало/конец, zoom, весь период, возврат к кандидату.
- Double-click повторно выбранной строки явно центрирует её после прокрутки.
- Параметры пояснены в tooltips/runbook; «Отмена тики» → «Против, тики».
- Contracts: ORDER-FLOW-DATA-001, RESEARCH-001, STRATEGY-001,
  QUALIFICATION-001 partial/target; MVP-RUNBOOK-001 current implementation.

## Последний завершённый шаг

UI implementation → PRIMARY → FIX → VALIDATION_1 → CLEAN по двум ролям.
OF-UI-001 (double-click same selected row) и OF-UI-DOC-001 (русский rejected
status в инструкции) закрыты. Ранее hardening OF-HARDENING-001 закрыта,
hardening code review/doc review CLEAN. Старый scope повторно не открывался.
Обычная сборка обновлена; агент приложение не запускал.

## Текущий незавершённый шаг

Владелец открывает исправленный workbench и проверяет взаимодействия реального
Window. Ранее он подтвердил дефект одной строки сводки и отсутствие scroll;
этот feedback исправлен и проверен offline, повторный owner feedback ожидается.

## Verification status

- Final source inventory: `.tmp/orderflow-ui-final-checkpoint.json`, 7/7 SHA;
  inventory SHA256 `54eee3258d9b5c5244070e178644d8565042039a294a33e3232230b47a8a7471`.
- `dotnet build OsEngine.sln --no-restore --verbosity:quiet -m:1 -p:UseSharedCompilation=false`:
  PASS exit0, 0 errors / 19 warnings (NU1900 and unchanged compiler warnings).
  Solution включает production project; isolated output build также PASS.
- `Tests/OrderFlowResearch/bin/Debug/net10.0-windows/OsEngine.OrderFlowResearch.Tests.exe`:
  PASS exit0, 24/24; запуск из isolated `.tmp/orderflow-ui-probe-cwd`.
- Новые tests: реальный implicit App.xaml style + summary scrolling/lastline,
  formatter value preservation в двух языках, programmatic viewport bounds,
  timeframe/selection/zoom/fullrange. Chart navigation test не доказывает render
  или button/wheel routing; весь Window им не открывается.
- Отдельный offline RenderTargetBitmap probe: real Sep18 QSH, window180,
  accepted174 candidates,924 Min1 bars; `.tmp/orderflow-ui-visual/chart-detail.png`
  и `chart-all.png` отрисованы/просмотрены Main; Application.Current=false.
  Probe работает в isolated cwd и не открывает Window, connector или settings UI.
- Installed OsEngine.dll и test-copy SHA256 совпадают:
  `97C88D0E8313BA4D4C5049FAA55894884E1BF3F82C6F7B7A0EB919040741E59F`.
- Production review: UI VALIDATION_1 CLEAN; docs/XML review: CLEAN; 7/7 hashes.
- XML comments: 11 touched-file blocks valid; XAML/csproj XML 2/2;
  Markdown fences balanced, новых local links нет.
- Agent validator: PASS, 109 checks; source/docs whitespace check PASS.
  При staging всех outputs полный cached check даёт 213 trailing-whitespace
  замечаний в 3 vendor WebView2 XML и OsEngine.dll.config. Содержимое совпадает
  с production build outputs (с учётом Git line endings); копии не форматировались.
- OBSERVABILITY: REQUIRED/SATISFIED — visible range, scales, legend,
  localized summary and parameter tooltips; runtime error logging сохраняется.
- MODE PARITY: NO CHANGE; trading/live parity и profitability не заявляются.
- Full interactive Window smoke: REQUIRES OWNER-RUN. Live/MCP/real orders NOT_RUN.

## Owner QSH evidence

- Владелец подтвердил `VolumeStep=1 контракт`; источник `C:\Users\Mi\Downloads\qsh`.
- Sep20 прежняя пара:1182Deals33024Quotes,7candidates; repeat deterministic PASS.
- Пользователь заменил пару на SRZ6.2026-09-18 и подтвердил окна признаков
  180/540/1080 sec (горизонты labels остаются 60/300/900 sec).
- Sep18:21194Deals144318Quotes; candidates174/339/389;
  Long/Short109/65,229/110,311/78; stale24/56/67;
  complete labels522/522,1017/1017,1165/1167.
- Все три прогона повторены: hashes/byte-identical bundles совпали,
  source QSH hashes не изменились. Sec15/Sec30/Min1 bars2435/1595/924.
- `.tmp/orderflow-owner-run/SRZ6-20260918-windows/comparison-summary.json`,
  `candidate-times-comparison.csv` и per-window CSV содержат итог/времена.
- Owner probe не доказывает execution, economic validity или full Window flow.

## Dirty boundary и сохранность

- Все накопленные source/docs/test changes и видимые Git build outputs
  включаются в публикацию по явному запросу владельца; force запрещён.
- Старый hardening checkpoint `.tmp/orderflow-hardening-validation-checkpoint.json`
  подтверждал 9/9 до UI фазы; tests/runbook изменены теперь намеренно.
- Binaries владельца перед обновлением сохранены в
  `.tmp/orderflow-ui-owner-binary-backup/`; новые OsEngine.dll/exe оставлены
  в привычном bin/Debug по его явному запросу.
- 5 других build-only tracked DLL восстановлены из HEAD (до build были clean).
- Прежние `.tmp/orderflow-user-dirty-backup-20260921/`, hardening backups
  сохранены локально; Tests/OrderFlowResearch/bin включается в commit
  согласно запросу всех изменений и действующим правилам repository ignore.
- Новые UI изменения не меняют parser/schema/core формулы или source QSH.

## Blockers

Для code fix blockers нет. Полный ручной прогон исправленного Window ожидает
владельца; offline tests/render evidence не объявлены полным UI acceptance.

## Next action

Открыть обычный `OsEngine/bin/Debug/OsEngine.exe` → Data → Order Flow;
Sep18 пара, VolumeStepOverride=1, window180 (174 candidates), затем при
необходимости540/1080. Проверить сводку, tooltips, scrollbar/wheel, весь период,
повторный double-click и смену timeframe. По owner feedback завершить
OWNER_RUN_VERIFICATION или исправить конкретный воспроизведённый дефект.
Разрешённые commit/push выполняются в текущую ветку; точный commit и remote
проверяются по Git. Ручное UI acceptance остаётся отдельным следующим шагом.
