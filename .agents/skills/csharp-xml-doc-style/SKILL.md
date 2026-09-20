---
name: csharp-xml-doc-style
description: Внутренний стандарт жанров, Tier, stable-ID ссылок и documentation drift для C# XML Documentation в OsEngine.
---

# C# XML Documentation standard для OsEngine

Стандарт отвечает только на вопрос **как документировать C# code**. Архитектура
живёт в зарегистрированных project documents, operational workflow — в
`AGENTS.md` и `AGENT-WORKFLOW-001`.

Это internal standard для Main в `IMPLEMENTATION MODE` и
`documentation-reviewer` в `AUDIT MODE`.

## 1. Перед записью comment

1. Открой `project/Documentation/DOCUMENTATION_MAP.md` и определи status/
   authority применимых sources.
2. Установи жанр и Tier ниже.
3. Проверь actual signature, callers, threading/lifecycle и tests.
4. Не превращай предположение, roadmap или desired behavior в current fact.

Стабильный document ID пишется в `<remarks>` обычным текстом. `<see cref>` и
`<paramref>` используются только для компилируемых C# symbols, не для Markdown
paths. Новый ID не изобретается внутри comment.

## 2. Scope и legacy boundary

В проекте нет требования одномоментно документировать каждый public member.
Обязательное XML Documentation добавляется/обновляется для нового или
semantic-changed public/protected API и safety-critical internal contract.
Нетронутый legacy-код не получает массовый backfill без отдельной задачи.

Не редактировать generated files (`*.Designer.cs`, generated resources,
tool output). Не включать `GenerateDocumentationFile` и warning policy как
побочный эффект documentation-задачи.

## 3. Жанры

| Жанр | Примеры | Главный смысл |
|---|---|---|
| Engine production | `OsEngine/**/*.cs` | contract, lifecycle, threading, failure semantics |
| Robot/indicator | scripts, `BotPanel`, `Aindicator` descendants | signal inputs, bar/event timing, state, trading restrictions |
| Test/test-stand/support | `Tests/**/*.cs` | scenario, dependencies, safety и граница доказательства |
| Generated | `*.Designer.cs` и tool output | не редактировать |

## 4. Tier

### Tier 1 — safety/qualification critical

Полный contract обязателен для order send/cancel/replace, position/risk,
connector lifecycle, market-data normalization/ordering, persistence migration,
encryption/auth/destructive MCP, Tester/live execution parity, concurrency и
real-order test stands.

Class/member comment описывает:

- роль и границу ответственности;
- preconditions и state/lifecycle;
- thread/event ownership;
- financial/data/order consequences;
- failure/cancellation/disposal semantics;
- что method **не** гарантирует;
- applicable document ID и evidence boundary.

### Tier 2 — обычный component contract

2–4 содержательных предложения: роль/операция, важное ограничение или side
effect, failure/return semantics. Не пересказывать имя member.

### Tier 3 — тривиальный member

Одна точная `<summary>` строка; `<param>`/`<returns>` только если добавляют
семантику, nullability, units или range. Очевидный private helper не требует
ceremonial comment.

Если Tier неясен, выбери Tier 2.

## 5. Production C# template

Используй корректные XML elements:

```csharp
/// <summary>
/// Отправляет уже проверенную заявку через реализацию текущего сервера.
/// </summary>
/// <remarks>
/// Метод не выполняет reconciliation после неизвестного результата отправки;
/// вызывающий обязан остановить retry до получения broker state.
/// Контракт: CONNECTORS-CONTEXT-001.
/// </remarks>
/// <param name="order">Заявка с положительным объёмом и ценой в шаге инструмента.</param>
/// <exception cref="InvalidOperationException">Соединение не готово к торговой операции.</exception>
public void SendOrder(Order order)
```

Порядок: `<summary>`, затем при необходимости `<remarks>`, `<param>`,
`<typeparam>`, `<returns>`, `<exception>`, `<example>`. Nullability, units,
timezone, side effects и ownership указываются там, где они не очевидны из
type system.

`<inheritdoc />` допустим только при полном совпадении contract. Если override
сужает capability, добавляет side effect, threading или failure behavior,
нужен собственный comment.

## 6. Robot и indicator comments

Class-level Tier 1/2 фиксирует:

- используемый tab/data type и событие расчёта;
- требуется ли finished candle, tick или market depth;
- параметры и units;
- state persistence/reset;
- signal vs visualization boundary;
- Tester/Optimizer/live applicability и известные различия;
- order/risk ownership: робот не должен обещать broker protection, если её нет.

Не писать «прибыльная стратегия», «безопасный вход» или «гарантирует» по одному
backtest. Indicator comment не выдаёт визуальный marker за торговый signal.

## 7. Tests и test stands

Тестовый comment описывает доказательную силу, а не API-контракт production.

Для Tier 1 class/scenario укажи:

- тип: offline/unit/component/MCP stand/live connector/real-order;
- subsystem и закрываемый risk;
- canonical command;
- внешние dependencies, credentials/account/market-hours;
- создаёт ли scenario реальные заявки/позиции или меняет settings;
- cleanup и timeout;
- что именно test не доказывает.

Mocked/offline test нельзя называть live compatibility test. `SKIPPED` из-за
missing token/market data не является PASS.

## 8. Правило «почему, а не что»

Плохо:

```csharp
/// <summary>Устанавливает цену.</summary>
public void SetPrice(decimal price)
```

Хорошо:

```csharp
/// <summary>
/// Фиксирует максимальную цену входа кандидата, чтобы последующий retest не
/// мог улучшить уровень задним числом.
/// </summary>
/// <param name="price">Цена, округлённая к <c>Security.PriceStep</c>.</param>
public void SetMaximumEntryPrice(decimal price)
```

## 9. Запрещённое переобещание

- «thread-safe», если защищён не весь reachable shared state;
- «атомарно», если crash между writes оставляет partial state;
- «идемпотентно», если broker/client identity не предотвращает duplicate send;
- «live-equivalent», если проверен только Tester/mock;
- «полный стакан», если source отдаёт snapshot/ограниченную глубину;
- «биржевое время», если provenance timestamp не подтверждён;
- «гарантирует отсутствие убытка/deadlock/race» по ограниченному тесту.

Предпочитай: «проверяет конкретный scenario», «защищает от regression в...»,
«не покрывает...», «требует live evidence...».

## 10. Documentation drift

Используй только `DOC_STALE`, `CODE_DRIFT`, `ARCHITECTURE_DRIFT`,
`TEST_EVIDENCE_DRIFT`, `MODE_PARITY_DRIFT` и общий формат из
`documentation-review`. Drift не исправляется молча в `AUDIT MODE`.

## 11. Итоговая проверка

1. XML syntax well-formed; `cref`/`paramref` указывают реальные symbols.
2. Comment соответствует signature, code path и tests.
3. Tier 1 раскрывает lifecycle/thread/order/data/safety boundary.
4. Нет claims сильнее evidence и нет secrets/raw payload.
5. Markdown упомянут стабильным ID, а не невалидным `cref`.
6. Generated/untouched legacy files не переписаны.
7. В `AUDIT MODE` behavior/signatures/assertions/project files не менялись.
