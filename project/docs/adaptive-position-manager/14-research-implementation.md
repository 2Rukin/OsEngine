# APM-RESEARCH-IMPLEMENTATION-001 — research design и границы реализации

Статус: CURRENT IMPLEMENTATION DESIGN / ResearchOnly. Основание: T09/T10,
[математика](03-mathematics.md), [источники](13-sources.md),
[research protocol](08-optimization.md). Разрешение пользователя на реализацию
не является разрешением live. Все дополнительные модели по умолчанию выключены.

## Решение: книга и калибровка

`ApmOrderBookResearch.EventOfi` реализует четыре индикаторных слагаемых SRC-OFI.
При равных ценах действуют обе соответствующие части. Единицы — объём книги.
Окно ограничено числом наблюдений; NormalizedOfi = WindowOfi / MeanDepth, где
MeanDepth — среднее (bidSize+askSize)/2 по наблюдениям окна. Это явно выбранное
оконное агрегирование. Повторная depth-нормировка не применяется.
SampledBook всегда помечается SAMPLED_PROXY. Reset, gap, crossed/empty и нарушение
порядка отменяют continuity; первая книга после reset только задаёт базу.

`ApmCausalCalibration` разделяет contemporaneous impact и lagged prediction.
Оценка — zero-intercept least squares sum(x*y)/sum(x*x), не готовый торговый
коэффициент. Единицы задаются явно. Для prediction FeatureEnd строго раньше
ResponseStart; label должен созреть к asOf и не выходить за заранее выбранный
training cutoff. Окна response не перекрываются. Нулевая энергия x даёт null.
Online update в holdout не включён; отдельный prequential-протокол не имитируется.

Согласованный native trades+quotes feed отсутствует в квалифицированном профиле.
Поэтому книга не подключена к TradeOnly policy, нет фиктивного OFI из bid=ask тика,
veto либо scale coefficient не назначены наугад. A4 и M3 — BLOCKED_DATA/CAPABILITY.
Dual-stream adapter требует отдельного решения об источнике/порядке и собственных
native ordering tests; простое объединение двух файлов не принято как parity.

## Решение: ограниченная реализация SRC-AS / SRC-AC / SRC-ADAPT

| Источник | Метод | Независимый oracle | Граница |
|---|---|---|---|
| SRC-AS reservation price | `ReservationPrice` | q=±2, gamma=.1, variance=.2, horizon10 →100∓.4 | reference, не target объёма |
| SRC-AC16–18 | `DiscreteInventory` | X12,N3,dt1,lambda=variance=eta=1,gammaPerm0 →12,4.5,1.5,0 | дискретная trajectory, не непрерывное приближение |
| SRC-ADAPT4–5 | `LinearDemand` | I1,c1,p1,L2 →−1 | отрицательное значение reference сохраняется |
| Причинная оценка demand | `ApmDemandCalibration` | (c,p)=(1,1),(3,3),L1 →3 | E[Icp]−L*E[Ic], не произведение средних |

AS: r=P−q*gamma*variance*(T−t). Для фьючерсной quote price введено собственное
размерностное преобразование через денежную цену Y=mP: rP=P−q*gamma*m*varianceP*h.
Это адаптация с одним money-per-price multiplier, не дословная формула статьи.
Gamma здесь имеет единицы inverse-money; APM InventoryPenalty безразмерен.

AC: dt=T/N; etaTilde=eta−gammaPermanent*dt/2>0;
kappa=acosh(1+dt²*lambda*variance/(2*etaTilde))/dt.
x_j=X*sinh(kappa*(T−j*dt))/sinh(kappa*T); child_j=x_(j−1)−x_j.
Реализация использует тождественный asinh и устойчивое отношение экспонент.
При lambda=0 либо variance=0 результат точно линейный; kappa имеет единицы1/time.
Полные optimal controls SRC-ADAPT и отдельный running-inventory penalty phi
не реализованы: локальный контракт задаёт demand reference4–5, а не новую стратегию
котирования. Clipping отрицательного demand не выполняется скрыто.

## Решение: opt-in AC pacing обычных действий

`Research AC enabled=false` по умолчанию. Для включения нужен явно выбранный
`Research AC settings file` с каждым полем `ApmAcSettings`: Steps, HorizonSeconds,
Lambda, MonetaryVariancePerSecond, TemporaryImpact, PermanentImpact,
CalibrationVersion, TrainingCutoff. Ограничение32KiB, missing fields отклоняются;
cutoff строго раньше входа каждой кампании. В manifest сохраняются все значения.
Синтетические коэффициенты qualification явно UNFITTED, не рекомендация для рынка.

`ApmAcExecutionPlanner` перепланирует обычный ADD/REDUCE при изменении текущего
allowed target/action, начиная с фактического q; первый bucket доступен сразу.
Подтверждённые fills и уже отправленные pending вычитаются из cumulative schedule.
Контроллер может только уменьшить обычный child на lot-grid. Raw/policy/risk target,
S/U/R и volume locks не меняются. Entry, hard stop, money stop, risk breach,
session cutoff и любой EXIT обходят замедление. Восстановление начинает новый
ordinary plan от актуального состояния после штатной сверки, не повторяет send.

A5 остаётся ResearchOnly даже при успешном synthetic native run: full-volume native
модель не измеряет очередь, underfill и реальные c/p. Выбор более сложной модели
без OOS ablation после расходов не является economic GO. Текущий статус gates и
фактические totals находятся только в [work record](evidence/implementation.md).
