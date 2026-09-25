# Форма отчёта задания

Заполнить фактическими значениями; эта форма не является выполненным отчётом.

| Поле | Что указать |
|---|---|
| Task / статус | Txx; DONE / PARTIAL / BLOCKED |
| База / итог | Repository, branch, base SHA, commit SHA |
| Scope | Изменённые файлы и что намеренно не менялось |
| Требования | Rxx, Sxx, QGxx |
| Контракты | Версии модели, данных, схемы recovery и execution profile |
| Build | Точная команда, окружение, exit code, лог; либо NOT_RUN и причина |
| Tests | Имена, expected/actual, результаты и artifact paths; никакого предполагаемого PASS |
| Данные | Dataset/schedule hashes, период, timezone, очистка, capabilities |
| Экономика | Baseline, fees/slippage, OOS/stress, ограничения; либо N/A_WITH_REASON |
| Review | Reviewer identity/role либо self-review; находки и повторная проверка |
| UI | Фактически проверенные размеры/DPI и screenshots |
| Блокеры | Severity, достижимый flow, последствия, next action |
| Авторизация | Какие подключения/операции были разрешены; что не запускалось |
| Решение | Engineering gate; Research status; live authorization отдельно |

## Формат finding

FindingId; severity; reached/potential; commit/path/lines; предусловия; последовательность событий; ожидаемое/фактическое; денежный/операционный эффект; тест воспроизведения; исправление; результат повторной проверки.

## Минимум для run manifest

RunId, CampaignScheduleHash, DatasetHash, CommitSha, ModelVersion, SchemaVersion, Profile, Parameters, RiskLocks, Timezone, EventOrderingPolicy, Warmup, PriceMode, ValuationMetadata, CommissionModel, SlippageModel, FillModel, Seed, Train/Validation/TestBoundaries, CalibrationCutoff, OutputHashes, CompletionStatus.
