# AGENT-WORKFLOW-001: repository-wide review

**Load trigger:** только явный запрос полного review всей реализованной кодовой
базы или явно названной крупной подсистемы. Scoped review этот файл не читает.

`REPOSITORY_WIDE` — read-only audit. Его цель — систематически проверить exact
review identity, выдать один ранжированный реестр findings и остановиться до
owner triage.

## 1. Review identity и повтор

До делегации Main:

1. фиксирует baseline commit и `CLEAN` либо inspected dirty paths + SHA-256
   diff;
2. читает `project/Documentation/AgentSystem/FULL_REVIEW_INDEX.md`;
3. читает terminal report только той же review identity, если он существует;
4. возвращает existing report и не запускает новый audit молча.

Deliberate re-audit разрешён только при явном решении владельца, изменённом
baseline/dirty boundary, новом evidence для `REVIEW_INCOMPLETE_BLOCKED` или
явно названной release qualification point.

## 2. Coverage и finite cycle

Main делегирует native `production-code-reviewer`. Reviewer строит inventory
реализованных modules/execution boundaries и coverage matrix. Каждая
применимая category получает `CHECKED`, `NOT_APPLICABLE` с причиной либо
`BLOCKED` с недостающим evidence; `PENDING` в terminal report запрещён.

```text
PRIMARY_FULL_REVIEW -> COVERAGE_VALIDATION -> TERMINAL
```

`COVERAGE_VALIDATION` один раз проверяет пропуски inventory/categories,
duplicates, обязательные finding fields, Mermaid sequence sources и
contradictions. Он не начинает новый поиск улучшений.

Каждая substantive finding использует schema/reachability из
`SCOPED_REVIEW.md`. Один execution path объединяется под одним stable ID.

## 3. Persistent report

Main сохраняет результат по `.agents/templates/FULL_CODE_REVIEW_REPORT.md` в
`project/Documentation/AgentSystem/reviews/YYYY-MM-DD_HH-mm-ss_MSK_full_code_review.md`
и добавляет строку в `FULL_REVIEW_INDEX.md`.

Terminal outcomes:

- `REVIEW_COMPLETE_NO_ACTIONABLE_FINDINGS`;
- `REVIEW_COMPLETE_AWAITING_OWNER_DECISION`;
- `REVIEW_COMPLETE_WITH_OWNER_DECISIONS`;
- `REVIEW_INCOMPLETE_BLOCKED`.

Review завершается даже при наличии `MUST_FIX`. Только owner decision
`APPROVED_FOR_FIX` по конкретным Finding IDs создаёт implementation scope.

## 4. Stop condition

Review останавливается, когда coverage terminal, каждая finding имеет все поля
и отдельную Mermaid sequence, duplicates объединены, gaps отмечены `BLOCKED`,
report/index подготовлены и выдан один terminal verdict. Количество findings и
возможность найти ещё одну abstraction не являются основанием для нового pass.
