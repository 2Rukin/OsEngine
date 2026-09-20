# AGENT-WORKFLOW-001: external и live evidence

**Load trigger:** semantic проверка vendor/exchange/broker protocol, MCP/live
runtime, external data format либо evidence, зависящее от credentials/account,
торговой сессии, реальных заявок или project test stand.

## 1. Источники

- Current implementation проверяется только по checkout форка
  `2Rukin/OsEngine`.
- Официальная vendor/API specification может быть normative для внешнего
  протокола, но не заменяет project architecture.
- Контекст или код внешнего upstream не подменяет current fork и исследуется
  только по явной задаче.
- Generated/converted vendor view допустим только с provenance и checksum;
  при mismatch outcome — `BLOCKED`.

Если официальная спецификация не сохранена/доступна, не восстанавливать
protocol semantics по памяти или случайному примеру. Отметить missing source.

## 2. Offline before live

Сначала выполнить безопасное доступное evidence:

- статический code/config review;
- targeted unit/offline tests;
- compile/build, если входит в scope;
- schema/parser fixtures;
- agent/document validators.

Offline build не запускает OsEngine, MCP host, connector или account session.
Нельзя объявлять live compatibility по одному mocked/offline тесту.

## 3. Запрещённые без явного разрешения запуски

- MCP test stand и `StopOrdersTestStand`;
- подключение любого broker/exchange connector;
- ввод/чтение real credentials или local secret files;
- real/paper order send, cancel/replace, forced close;
- изменение account/portfolio/server settings;
- скачивание исторических данных из платного/авторизованного источника.

Разрешение должно называть конкретный сценарий и среду. Разрешение на build или
«проверить код» не является разрешением на live run.

## 4. Fail-closed handoff

Если required evidence недоступно, вернуть:

- `REQUIRES LIVE CONNECTOR`, `REQUIRES TEST STAND` или `REQUIRES OWNER-RUN`;
- точную команду/последовательность без secret values;
- prerequisites, ожидаемый observable result и cleanup;
- какие claims остаются `NOT_PROVEN`;
- возможно ли появление/изменение real orders и позиций.

Не интерпретировать `SKIPPED`, отсутствие market hours или пустой поток как
PASS. Никогда не печатать secret, raw authenticated request/response или
полный локальный config.
