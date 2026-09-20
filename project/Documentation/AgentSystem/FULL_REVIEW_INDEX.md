# Repository-wide production review index

**ID:** `FULL-REVIEW-INDEX-001`
**Статус:** CURRENT CATALOG
**Обновлено:** 20.09.2026 20:42:41 (UTC+03:00, Москва)

Каталог делает terminal repository-wide reviews обнаруживаемыми для
deduplication, coverage и owner triage. Он не является source of truth current
code: каждый report привязан к exact review identity (`baseline commit + dirty
boundary`).

Перед новым full review Main ищет terminal report той же identity. Если он
существует, report возвращается владельцу без нового broad pass, пока владелец
не запросит deliberate re-audit.

| Review ID | Baseline | Dirty boundary | Report | Scope | Coverage | Terminal verdict | Owner triage | Superseded by |
|---|---|---|---|---|---|---|---|---|
| — | — | — | — | — | — | — | — | — |

## Правила обновления

- строка добавляется только после terminal report;
- reports хранятся в `project/Documentation/AgentSystem/reviews/`;
- одна identity получает второй report только после deliberate re-audit;
- новый report не удаляет старый, а заполняет `Superseded by`;
- owner decisions не переписывают исходное evidence;
- implementation task ссылается на выбранные Finding IDs.
