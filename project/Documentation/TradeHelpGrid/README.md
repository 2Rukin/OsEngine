# THG-INDEX-001: диапазонный сеточный торговый модуль

**Статус:** SOURCE BASELINE + CURRENT IMPLEMENTATION — OFFLINE CHECKS PASSED, LIVE NOT QUALIFIED  
**Дата:** 2026-09-29  
**Источник:** локальная поставка TradeHelp4; OsEngine `088add98b728f8088fb18ff2e59c8d4113ad043c`.

Реализуется нативный робот `Futures2Grid` и необходимые opt-in изменения
OsEngine. Текущий контракт — [ADR-THG-002](ADR-0002_OSENGINE_IMPLEMENTATION.md),
инструкция — [THG-OPERATOR-002](OSENGINE_OPERATOR.md). Независимая проверка исходного
переноса завершена в [IMPLEMENTATION_REVIEW](IMPLEMENTATION_REVIEW.md).
Новая адаптация Финам/TRANSAQ и её отдельная проверка описаны в
[THG-TRANSAQ-IMPLEMENTATION-006](FINAM_TRANSAQ_IMPLEMENTATION.md);
готовность к реальной торговле не заявлена.
Режим IsHedge: [контракт010](HEDGE_MODE.md), [проверка010](HEDGE_REVIEW.md).
Команды по списку уровней: [контракт011](BATCH_LEVELS.md), [проверка011](BATCH_REVIEW.md).
Удаление пустого экземпляра: [контракт012](EMPTY_REMOVAL.md), [проверка012](EMPTY_REMOVAL_REVIEW.md).
Приоритет/обычные limit-цены: [контракт013](EXECUTION_OPTIONS.md), [проверка013](EXECUTION_OPTIONS_REVIEW.md).
Range-команды сохраняют ручные настройки уровней: [контракт014](RANGE_STATE.md), [проверка014](RANGE_STATE_REVIEW.md).
Исторические SRU6/QSH и signed native Tester прогоны: [квалификация015](HISTORICAL_QUALIFICATION.md),
[проверка015](HISTORICAL_QUALIFICATION_REVIEW.md).
Source reconstruction ниже сохранена отдельно от current implementation.

Подтверждённый владельцем reference для переноса — **«Фьючерсы 2»**, класс
`DTI.StrategyDTIDTF` из `Strategyes/dti.lf`: заданный диапазон, число уровней,
распределение средств по уровням, внешняя граница и режим ликвидации. В той же
поставке есть другая сетка **«Фьючерсы»** (`dtf.lf`), сетка акций (`dtm.lf`),
арбитражные варианты (`dti.lf`) и ручной TouchScalp (`ts.lf`). Это разные
алгоритмы. Конкретная ранее сохранённая пользовательская конфигурация не исследовалась.

Уточнённый объём: обычный робот OsEngine с ручными параметрами и runtime
управлением функциями Futures2. Произвольное сокращение до базовой первой
версии не согласовано. До реализации подготовлена подробная спецификация
переходов, включая унаследованные режимы, callbacks и изменение настроек.
Оба режима выхода — по уровням и по средней всей позиции с runtime
переключением — требование владельца; второй не найден как рабочий legacy TP.

Дополнительно владелец потребовал цены до пяти знаков и отрицательные цены.
Целевой домен включает ноль и диапазоны через ноль. Единые правила tick,
обеспечения/лотов, наценки, стопов и сохранения находятся в THG-PRICE-001;
они заменяют прежнее target-ограничение L>0. Legacy TradeHelp не менялся. Opt-in native paths и границы их проверки
описаны в новом implementation ADR.

Критические особенности найденного решения:

- У «Фьючерсы 2» активный диапазон и внутренний диапазон менеджера различаются.
  На неблагоприятной стороне стоп соответствует описанию владельца, на другой
  стороне граница отнесена ещё на ширину диапазона.
- Расчётный бюджет уровня основан на заданном проценте ГО. Это не автоматическое
  доказательство достаточности фактической маржи у брокера.
- `SellAll` — режим последовательного закрытия; он не означает уже нулевую
  позицию. `FreezeVolume`, активные заявки и лимиты действий меняют результат.
- Предварительная постановка, блокировки входа/выхода, фильтр HJ, групповая
  отправка и частичные исполнения требуют отдельных сценариев квалификации.

## Как читать

| Документ | Каноническое содержание |
|---|---|
| [План Finam / TRANSAQ](FINAM_TRANSAQ_PLAN.md) | REVIEWED TARGET: основание адаптации; текущий код и evidence описаны в [реализации TRANSAQ](FINAM_TRANSAQ_IMPLEMENTATION.md) |
| [Текущий implementation ADR](ADR-0002_OSENGINE_IMPLEMENTATION.md) | Native контракт, переходы, переносы, recovery и явные отличия от legacy |
| [Итог независимых проверок](IMPLEMENTATION_REVIEW.md) | CLEAN/CLEAN, закрытые findings и точная граница offline evidence |
| [Ручной учёт и восстановление](INVENTORY_REGISTRATION.md) | Native inventory, явные внешние исполнения и двухэтапная local persistence |
| [Управление роботом](OSENGINE_OPERATOR.md) | Поля Parameters, команды, статусы и восстановление |
| [Первоначальный ADR](ADR-0001_RANGE_GRID.md) | Выбор архитектуры, альтернативы, решения и открытые продуктовые вопросы |
| [Исходное поведение](LEGACY_BEHAVIOR.md) | Что действительно найдено в бинарных модулях; варианты и ограничения |
| [Переходы Futures2](FUTURES2_TRANSITIONS.md) | Manager/host states, приоритеты, фильтры, stop, budget и сдвиги |
| [Уровни и заявки Futures2](FUTURES2_LEVELS_AND_ORDERS.md) | Immediate/pre-send, объёмы, slot ledger, partial/cancel/повторный вход |
| [Runtime-настройки Futures2](FUTURES2_RUNTIME_SETTINGS.md) | Начальные поля, полная замена manager, live setters, ручные команды, persistence |
| [Host и callbacks Futures2](FUTURES2_HOST_AND_CALLBACKS.md) | Grouping, ошибки, буферы/replay, расписание, полная машина trailing |
| [Экспирация и перенос](FUTURES2_EXPIRATION_AND_ROTATION.md) | Достижимые inherited пути old/new contracts и общий Rotate controller |
| [Покрытие переходов](FUTURES2_COVERAGE.md) | Source inventory, сценарии взаимодействий и граница полноты |
| [Математика и параметры](MATHEMATICS_AND_PARAMETERS.md) | Единицы, уровни, объёмы, выходы, числовые примеры и каталог опций |
| [Цены до5 знаков и отрицательные цены](PRICE_DOMAIN.md) | Канонический target: tick grid через0, независимое обеспечение, signed exits/stops, runtime и persistence |
| [Заявки и восстановление](EXECUTION_AND_RECOVERY.md) | Legacy flow и предлагаемые состояния, cancel/fill races, ликвидация, restart |
| [Интеграция OsEngine](OSENGINE_INTEGRATION.md) | Текущие native API, ограничения повторного использования, граница переноса |
| [Эксплуатация](OPERATIONS.md) | Предлагаемые операторские действия, мониторинг и аварийные процедуры |
| [Квалификация](QUALIFICATION.md) | Матрица проверок, ожидаемые результаты и реально выполненное evidence |
| [SRU6 и signed native Tester](HISTORICAL_QUALIFICATION.md) | Выполненные replay, исправленный zero-loader и оставшиеся ручные test cases |
| [Независимая проверка SRU6 qualification](HISTORICAL_QUALIFICATION_REVIEW.md) | Закрытые findings, exact evidence и terminal CLEAN/CLEAN |
| [Источники и воспроизводимость](EVIDENCE.md) | Hashes, методы, инструменты, пределы статического исследования |

## Статусы утверждений

**OBSERVED** — тело метода в конкретном локальном бинарном файле или current
OsEngine code. Это статический факт, не результат live-прогона.
**OWNER REQUIREMENT** — диапазон, уровни, лоты и ликвидация за внешней зоной,
явно описанные владельцем. **PROPOSED** — выбранный здесь будущий контракт.
**NOT_DETERMINED** — недостаточно evidence. Первоначальный ADR остаётся историческим proposal. Последующее поручение
реализовать робота оформлено отдельным ADR-THG-002; evidence не переносится
между этапами автоматически.

Документы не воспроизводят реальные настройки, счета, журналы или сделки.
Внешние бинарники и полный результат декомпиляции не включаются в репозиторий.
Hash-bound anchors находятся в [evidence](EVIDENCE.md); будущие изменения файлов
требуют повторной проверки затронутых claims.
