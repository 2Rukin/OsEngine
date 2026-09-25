/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using OsEngine.Language;

namespace OsEngine.OsData.OrderFlow
{
    /// <summary>Explanatory help for every owned workbench input and result column; shared controls retain it when detached.</summary>
    internal static class OrderFlowResearchHelp
    {
        internal static Dictionary<string, string> Fields(bool russian)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            Add(result, russian ? "Открыть отдельное окно подбора Cloud по временным диапазонам: распределения, heatmap, независимые правила и Anatomy. Legacy Cloud 1/2 не меняются." : "Open Cloud calibration with time profiles, distributions, heatmap, independent rules and Anatomy. Legacy Cloud 1/2 settings stay independent.", "ButtonCalibration");
            Add(result, russian ? "Локальный UTF-8 файл тиков Date,Time,Price,Volume,Side,MicroSeconds,Id. Выберите файл, затем выполните расчёт." : "Local UTF-8 tick file. Choose Date,Time,Price,Volume,Side,MicroSeconds,Id data, then run the calculation.", "TextBoxTicksPath", "LabelTicks");
            Add(result, russian ? "Выбрать существующий локальный файл тиков. Загрузки из интернета нет." : "Choose an existing local tick file; nothing is downloaded.", "ButtonBrowseTicks");
            Add(result, russian ? "Папка CSV и отчётов расчёта. Фильтры отображения не перезаписывают сохранённые результаты." : "Folder for immutable calculation CSV and audit files. View filters never overwrite these results.", "TextBoxOutputPath", "LabelOutput");
            Add(result, russian ? "Выбрать родительскую папку для результатов расчёта." : "Choose the parent folder for calculation results.", "ButtonBrowseOutput");
            Add(result, russian ? "Даты начала и конца включаются целиком. Задайте обе даты либо очистите обе для расчёта всего файла." : "Inclusive calculation dates. Enter both dates or clear both for the whole file.", "DateFrom", "DateTo", "LabelPeriod");
            Add(result, russian ? "Очистить обе даты. Следующий расчёт использует весь выбранный файл." : "Clear both date limits and use the whole selected file on the next run.", "ButtonAllDates");
            Add(result, russian ? "Рассчитать признаки дельты и реакции на кандидатов при следующем запуске. Показ на графике включается отдельно." : "Calculate delta features and candidate reactions on the next run. Chart visibility is controlled separately.", "CheckBoxCalculateDelta");
            Add(result, russian ? "Рассчитать этот независимый слой Cloud при следующем запуске. Его параметры тиков и цепочек не меняют другой слой." : "Calculate this independent Cloud layer on the next run. Its tick and chain settings do not change the other layer.", "CheckBoxCalculateCloud", "CheckBoxCalculateCloud2");
            Add(result, russian ? "Обязательный шаг цены, например 5 или 0,00001. Все расстояния в тиках цены используют это значение. Можно вводить точку или запятую." : "Required minimum price increment, such as 5 or 0.00001. All price-tick distances use this manual value; dot or comma is accepted.", "TextBoxPriceStep", "LabelPriceStep");
            Add(result, russian ? "Окно расчёта признаков дельты в секундах. Это глубина прошлого, а не горизонт будущего движения." : "Trailing delta-feature window in seconds. This is a lookback, not the future reaction horizon.", "TextBoxWindowSeconds", "LabelWindow");
            Add(result, russian ? "Минимальная абсолютная разность объёмов Buy и Sell для кандидата дельты. Единицы объёма из файла, не проценты." : "Minimum absolute Buy minus Sell volume for a delta candidate, in the file volume units, not percent.", "TextBoxMinimumDelta", "LabelDelta");
            Add(result, russian ? "Минимальное изменение цены в ручных шагах цены для дивергенции дельты. Ноль допускает отсутствие изменения цены." : "Minimum price change in manual price steps for delta divergence. Zero allows a flat price.", "TextBoxMinimumPriceTicks", "LabelPriceTicks");
            Add(result, russian ? "Минимальная пауза между кандидатами дельты, миллисекунды. На формирование Cloud не влияет." : "Minimum pause between delta candidates in milliseconds; does not change Cloud formation.", "TextBoxCooldown", "LabelCooldown");
            Add(result, russian ? "Интервал фоновых наблюдений в секундах. Нужен для сравнения реакции на кандидаты с обычным поведением рынка." : "Interval in seconds for background observations used to compare candidate reactions with ordinary market conditions.", "TextBoxBackground", "LabelBackground");
            Add(result, russian ? "Горизонты оценки будущей реакции на дельту в секундах через точку с запятой, например 60;300;900." : "Future delta-reaction horizons in seconds, separated by semicolons, for example 60;300;900.", "TextBoxHorizons", "LabelHorizons");
            Add(result, russian ? "Расстояние цели для оценки кандидата дельты, в ручных шагах цены. Это граница анализа, торговая заявка не выставляется." : "Favorable price distance for delta labels, in manual price steps. This is an analysis barrier, not an order.", "TextBoxTargetTicks", "LabelTarget");
            Add(result, russian ? "Расстояние против направления кандидата дельты в шагах цены. Для исхода важно, какая граница достигнута раньше." : "Adverse price distance for delta labels, in manual price steps. The first touched barrier defines the outcome.", "TextBoxInvalidationTicks", "LabelInvalidation");
            Add(result, russian ? "Минимальный объём одной сделки, включаемой в этот Cloud. Меньшие сделки всё равно учитываются в окружающем потоке." : "Minimum volume of one trade included in this Cloud layer. Smaller trades still contribute to surrounding-flow statistics.", "TextBoxCloudMinTick", "LabelCloudMinTick");
            Add(result, russian ? "Минимальная сумма объёмов цепочки для появления Cloud. Параметр формирования, применяется кнопкой «Запустить»." : "Minimum summed volume for a chain to qualify as Cloud. Formation setting, applied by Run.", "TextBoxCloudSum", "LabelCloudSum");
            Add(result, russian ? "Максимальная пауза между включёнными сделками, миллисекунды. Большая пауза завершает предыдущую цепочку." : "Maximum pause between included trades, in milliseconds. A larger pause finishes the previous chain.", "TextBoxCloudGap", "LabelCloudGap");
            Add(result, russian ? "Максимальный диапазон High–Low цепочки в шагах цены. Выход за диапазон начинает новую цепочку." : "Maximum high-low span of a chain, in manual price steps. A trade outside the span starts a new chain.", "TextBoxCloudRange", "LabelCloudRange");
            Add(result, russian ? "Окно окружающего потока в секундах, сохраняемое при расчёте. Изменение окна требует «Запустить»; изменение порогов фильтра — нет." : "Trailing surrounding-flow window in seconds, saved during calculation. Changing this formation input requires Run; display thresholds do not.", "TextBoxCloudContextSeconds", "LabelCloudContextSeconds");
            Add(result, russian ? "Выберите статистику внутри Cloud, окружающего потока или оба источника с одной проходящей стороной. «Выключен» отключает условия перевеса; границы числа тиков действуют отдельно." : "Choose inside-Cloud evidence, surrounding flow, or both with the same passing direction. Off disables diagonal/delta conditions; tick-count bounds still apply.", "ComboBoxCloudImbalanceSource", "LabelCloudImbalanceSource");
            Add(result, russian ? "Требовать перевес покупок, продаж или любой стороны в выбранном диагональном профиле." : "Require dominant Buy, Sell, or either direction in the selected diagonal profile.", "ComboBoxCloudImbalanceDirection", "LabelCloudImbalanceDirection");
            Add(result, russian ? "Минимальное отношение объёмов доминирующей и противоположной сторон на соседних ценах. 300% означает 3 к 1. При нулевом противоположном объёме сравнения нет." : "Minimum dominant/opposite volume ratio across adjacent prices. 300% means 3 to 1. Missing or zero opposite volume is not a comparison.", "TextBoxCloudImbalanceRatio", "LabelCloudImbalanceRatio");
            Add(result, russian ? "Минимальный объём доминирующей стороны в ценовой паре, не всего Cloud. Ноль отключает этот порог." : "Minimum dominant-side volume in a compared price pair, not the total Cloud volume. Zero disables this floor.", "TextBoxCloudImbalanceVolume", "LabelCloudImbalanceVolume");
            Add(result, russian ? "Минимальная абсолютная разность объёмов Buy и Sell в диагональной паре. Ноль отключает этот порог." : "Minimum absolute Buy-Sell volume difference within a diagonal pair. Zero disables this floor.", "TextBoxCloudImbalanceDifference", "LabelCloudImbalanceDifference");
            Add(result, russian ? "Дополнительный порог объёмной дельты всего выбранного профиля, от 0 до 100%. Ноль отключает условие; для Buy нужна положительная дельта, для Sell — отрицательная." : "Additional directional volume delta percentage for the whole selected profile, from 0 to 100. Zero disables it; Buy requires positive delta and Sell negative.", "TextBoxCloudImbalanceDelta", "LabelCloudImbalanceDelta");
            Add(result, russian ? "Минимальное итоговое число сделок во всём Cloud. Ноль отключает нижнюю границу; в реплее используется число уже включённых сделок." : "Minimum final number of trades in the whole Cloud. Zero disables the lower bound; replay uses the consumed prefix count.", "TextBoxCloudMinCount", "LabelCloudMinCount");
            Add(result, russian ? "Максимальное итоговое число сделок во всём Cloud. Ноль — без ограничения. Значение 1 оставляет только Cloud из одной сделки." : "Maximum final number of trades in the whole Cloud. Zero means unlimited. Set 1 to keep only single-trade Clouds.", "TextBoxCloudMaxCount", "LabelCloudMaxCount");
            Add(result, russian ? "Применить фильтры к готовым Cloud без повторного чтения тиков. В таблицах остаются все строки с обновлённым признаком прохождения." : "Apply current display filters to saved Clouds, without rereading ticks. Tables retain all rows with updated pass/fail flags.", "ButtonCloudFilterApply", "LabelCloudFilterApply");
            Add(result, russian ? "Открыть фильтры готовых Cloud. Используйте «Применить фильтры», повторный запуск расчёта не нужен." : "Open post-calculation filters. Use Apply filters; Run is not needed.", "ExpanderCloudFilter", "TextBlockCloudFilterHeader");
            Add(result, russian ? "Минимальный объём одной сделки, включаемой в этот Cloud. Меньшие сделки всё равно учитываются в окружающем потоке." : "Minimum volume of one trade included in this Cloud layer. Smaller trades still contribute to surrounding-flow statistics.", "TextBoxCloud2MinTick", "LabelCloud2MinTick");
            Add(result, russian ? "Минимальная сумма объёмов цепочки для появления Cloud. Параметр формирования, применяется кнопкой «Запустить»." : "Minimum summed volume for a chain to qualify as Cloud. Formation setting, applied by Run.", "TextBoxCloud2Sum", "LabelCloud2Sum");
            Add(result, russian ? "Максимальная пауза между включёнными сделками, миллисекунды. Большая пауза завершает предыдущую цепочку." : "Maximum pause between included trades, in milliseconds. A larger pause finishes the previous chain.", "TextBoxCloud2Gap", "LabelCloud2Gap");
            Add(result, russian ? "Максимальный диапазон High–Low цепочки в шагах цены. Выход за диапазон начинает новую цепочку." : "Maximum high-low span of a chain, in manual price steps. A trade outside the span starts a new chain.", "TextBoxCloud2Range", "LabelCloud2Range");
            Add(result, russian ? "Окно окружающего потока в секундах, сохраняемое при расчёте. Изменение окна требует «Запустить»; изменение порогов фильтра — нет." : "Trailing surrounding-flow window in seconds, saved during calculation. Changing this formation input requires Run; display thresholds do not.", "TextBoxCloud2ContextSeconds", "LabelCloud2ContextSeconds");
            Add(result, russian ? "Выберите статистику внутри Cloud, окружающего потока или оба источника с одной проходящей стороной. «Выключен» отключает условия перевеса; границы числа тиков действуют отдельно." : "Choose inside-Cloud evidence, surrounding flow, or both with the same passing direction. Off disables diagonal/delta conditions; tick-count bounds still apply.", "ComboBoxCloud2ImbalanceSource", "LabelCloud2ImbalanceSource");
            Add(result, russian ? "Требовать перевес покупок, продаж или любой стороны в выбранном диагональном профиле." : "Require dominant Buy, Sell, or either direction in the selected diagonal profile.", "ComboBoxCloud2ImbalanceDirection", "LabelCloud2ImbalanceDirection");
            Add(result, russian ? "Минимальное отношение объёмов доминирующей и противоположной сторон на соседних ценах. 300% означает 3 к 1. При нулевом противоположном объёме сравнения нет." : "Minimum dominant/opposite volume ratio across adjacent prices. 300% means 3 to 1. Missing or zero opposite volume is not a comparison.", "TextBoxCloud2ImbalanceRatio", "LabelCloud2ImbalanceRatio");
            Add(result, russian ? "Минимальный объём доминирующей стороны в ценовой паре, не всего Cloud. Ноль отключает этот порог." : "Minimum dominant-side volume in a compared price pair, not the total Cloud volume. Zero disables this floor.", "TextBoxCloud2ImbalanceVolume", "LabelCloud2ImbalanceVolume");
            Add(result, russian ? "Минимальная абсолютная разность объёмов Buy и Sell в диагональной паре. Ноль отключает этот порог." : "Minimum absolute Buy-Sell volume difference within a diagonal pair. Zero disables this floor.", "TextBoxCloud2ImbalanceDifference", "LabelCloud2ImbalanceDifference");
            Add(result, russian ? "Дополнительный порог объёмной дельты всего выбранного профиля, от 0 до 100%. Ноль отключает условие; для Buy нужна положительная дельта, для Sell — отрицательная." : "Additional directional volume delta percentage for the whole selected profile, from 0 to 100. Zero disables it; Buy requires positive delta and Sell negative.", "TextBoxCloud2ImbalanceDelta", "LabelCloud2ImbalanceDelta");
            Add(result, russian ? "Минимальное итоговое число сделок во всём Cloud. Ноль отключает нижнюю границу; в реплее используется число уже включённых сделок." : "Minimum final number of trades in the whole Cloud. Zero disables the lower bound; replay uses the consumed prefix count.", "TextBoxCloud2MinCount", "LabelCloud2MinCount");
            Add(result, russian ? "Максимальное итоговое число сделок во всём Cloud. Ноль — без ограничения. Значение 1 оставляет только Cloud из одной сделки." : "Maximum final number of trades in the whole Cloud. Zero means unlimited. Set 1 to keep only single-trade Clouds.", "TextBoxCloud2MaxCount", "LabelCloud2MaxCount");
            Add(result, russian ? "Применить фильтры к готовым Cloud без повторного чтения тиков. В таблицах остаются все строки с обновлённым признаком прохождения." : "Apply current display filters to saved Clouds, without rereading ticks. Tables retain all rows with updated pass/fail flags.", "ButtonCloud2FilterApply", "LabelCloud2FilterApply");
            Add(result, russian ? "Открыть фильтры готовых Cloud. Используйте «Применить фильтры», повторный запуск расчёта не нужен." : "Open post-calculation filters. Use Apply filters; Run is not needed.", "ExpanderCloud2Filter", "TextBlockCloud2FilterHeader");
            Add(result, russian ? "Каждая подходящая сделка создаёт отдельный квадрат Cloud. Снимите флажок для второго независимого слоя цепочек." : "Each qualifying trade creates one separate square Cloud. Uncheck to form a second independent chain layer.", "CheckBoxCloud2SingleTicks");
            Add(result, russian ? "Таймфрейм отображения. Меняется группировка свечей; время Cloud и рассчитанные показатели сохраняются." : "Display timeframe only. Candles are regrouped; Cloud tick anchors and calculated statistics do not change.", "ComboBoxTimeFrame", "ComboBoxChartTimeFrame", "LabelTimeFrame");
            Add(result, russian ? "Показать или скрыть уже рассчитанные метки дельты." : "Show or hide already calculated delta markers.", "CheckBoxShowDelta");
            Add(result, russian ? "Показать или скрыть этот слой Cloud и его линию без пересчёта." : "Show or hide this Cloud layer and its line, without recalculation.", "CheckBoxShowCloud", "CheckBoxShowCloud2", "CheckBoxChartShowCloud", "CheckBoxChartShowCloud2");
            Add(result, russian ? "Общий размер меток от 0,1 до 3. Не меняет объёмы и решения фильтра." : "Overall marker size, from 0.1 to 3. Does not change volumes or filter decisions.", "SliderCloudScale", "SliderCloud2Scale", "SliderChartCloudScale", "SliderChartCloud2Scale", "LabelCloudScale", "LabelCloud2Scale", "LabelChartCloud2Scale");
            Add(result, russian ? "Контраст размеров по объёму от 0,5 до 10. Чем выше значение, тем заметнее разница крупных и мелких Cloud." : "Volume-size contrast, from 0.5 to 10. Higher values make large and small Cloud volumes more visually distinct.", "SliderCloudContrast", "SliderCloud2Contrast", "SliderChartCloudContrast", "SliderChartCloud2Contrast", "LabelCloudContrast", "LabelCloud2Contrast", "LabelChartCloud2Contrast");
            Add(result, russian ? "Прочитать выбранный файл тиков и рассчитать включённые слои по параметрам формирования. Для фильтров готовых Cloud есть отдельная кнопка применения." : "Read the selected tick file and calculate enabled layers using formation parameters. Display filters have their own Apply button.", "ButtonRun");
            Add(result, russian ? "Отменить текущий расчёт. Последний завершённый результат остаётся доступен." : "Cancel the current calculation. The last completed result remains available.", "ButtonCancel");
            Add(result, russian ? "Открыть папку исходного расчёта и его отчётов." : "Open the output folder containing the original calculation and its audit data.", "ButtonOpenArtifacts");
            Add(result, russian ? "Сводка исходного расчёта и качества данных. Фильтры показа не меняют исходные итоги; текущее число прошедших Cloud указано в панели фильтра." : "Original calculation summary and data-quality findings. Display filtering does not change these raw totals; current pass counts are in the Cloud filter panel.", "TextBoxSummary");
            Add(result, russian ? "Перейти в отдельное окно графика." : "Activate the separate chart window.", "ButtonActivateChartWindow");
            Add(result, russian ? "Перенести тот же график в отдельное окно или вернуть обратно. Настройки вида и рисунки сохраняются." : "Move the same chart to a separate window or return it; view settings and drawings remain.", "ButtonChartPopOut");
            Add(result, russian ? "Дополнительно показать отсечённые Cloud серым и включить их в линию Cloud. Фильтр и исходные данные не меняются." : "Also show rejected Clouds in gray, including them in the Cloud line. Does not change filtering or original data.", "CheckBoxCloudRejected", "CheckBoxCloud2Rejected");
            Add(result, russian ? "Выбрать свечи, бары, линии максимумов/минимумов или приглушённые серые линии High и Low." : "Choose candles, bars, high/low lines, or muted gray High and Low lines.", "ComboBoxPriceDisplay");
            Add(result, russian ? "Соединить цены Cloud, сохраняя их точное время. Линия цены по таймфрейму отображается приглушённым фоном." : "Connect Cloud anchor prices while preserving their exact times; the timeframe price line becomes a muted background.", "CheckBoxCloudPriceMode");
            Add(result, russian ? "Показать число объёма внутри меток, где хватает места. Полное значение всегда доступно при наведении." : "Show numeric Cloud volume inside markers where it fits. Full values are always available on hover.", "CheckBoxCloudVolumes", "CheckBoxCloud2Volumes");
            Add(result, russian ? "Свободное место справа в процентах ширины графика. По умолчанию 5%. Будущие свечи не создаются." : "Empty space to the right, as a percentage of chart width. Default 5%. Does not create future candles.", "SliderRightPadding");
            Add(result, russian ? "Перейти к началу рассчитанного периода." : "Move to the beginning of the calculated period.", "ButtonChartFirst");
            Add(result, russian ? "Перейти к концу рассчитанного периода." : "Move to the end of the calculated period.", "ButtonChartLast");
            Add(result, russian ? "Поставить выбранного кандидата дельты в центр графика. Для Cloud используйте контекстное меню его строки." : "Center the chart on the selected delta candidate. For Cloud use its table context menu.", "ButtonChartSelected");
            Add(result, russian ? "Увеличить горизонтальный масштаб относительно середины видимого диапазона." : "Increase horizontal scale around the center of the visible range.", "ButtonChartZoomIn");
            Add(result, russian ? "Уменьшить горизонтальный масштаб, чтобы увидеть больший период." : "Decrease horizontal scale to see a wider time range.", "ButtonChartZoomOut");
            Add(result, russian ? "Показать весь рассчитанный период на графике." : "Fit the whole calculated period on the chart.", "ButtonChartAll");
            Add(result, russian ? "Начать, приостановить или продолжить воспроизведение исходных тиков на выбранном таймфрейме." : "Start, pause or continue playback of source ticks on the selected chart timeframe.", "ButtonReplayPlay");
            Add(result, russian ? "После паузы воспроизвести ровно следующую строку тика, в том числе с тем же временем." : "After pausing, consume exactly one next physical tick, including a tick with the same timestamp.", "ButtonReplayStep");
            Add(result, russian ? "Остановить реплей и вернуться к полному рассчитанному графику." : "Stop replay and return to the full calculated chart.", "ButtonReplayReturn");
            Add(result, russian ? "Скорость воспроизведения исходного времени. 10× показывает десять секунд записи за одну реальную секунду." : "Playback speed multiplier of recorded time. 10× shows ten source seconds in one real second.", "ComboBoxReplaySpeed", "LabelReplaySpeed");
            Add(result, russian ? "Пропускать паузы записи длиннее 60 секунд при реплее. Сделки не удаляются." : "Skip recorded pauses longer than 60 seconds during replay; no trades are removed.", "CheckBoxReplaySkipGaps");
            Add(result, russian ? "Выбрать указатель, горизонтальную или наклонную линию. Инструмент остаётся активным; тело линии можно перетаскивать, концы — менять отдельно." : "Select pointer, horizontal line or trend line. The chosen drawing tool stays active; drag a line body to move it or endpoints to reshape it.", "ComboBoxDrawingTool");
            Add(result, russian ? "Выбрать цвет выделенной линии и следующих рисунков." : "Choose the color for the selected line and future drawings.", "ButtonDrawingColor");
            Add(result, russian ? "Толщина выделенной линии и следующих рисунков." : "Line thickness for the selected line and future drawings.", "ComboBoxDrawingThickness");
            Add(result, russian ? "Продолжить выбранную линию к соответствующему краю графика. Настройка применяется и к новым линиям." : "Continue the selected line to the corresponding chart edge; also used for new lines.", "CheckBoxDrawingLeft", "CheckBoxDrawingRight");
            Add(result, russian ? "Удалить только выделенный нарисованный объект." : "Delete the selected drawn object only.", "ButtonDrawingDelete");
            Add(result, russian ? "Удалить все временные рисунки с этого графика." : "Remove all temporary drawing objects from this chart.", "ButtonDrawingClear");
            Add(result, russian ? "Перетащите ползунок для прокрутки всего рассчитанного периода с сохранением масштаба." : "Drag to scroll through the whole calculated period while keeping the current zoom.", "ScrollBarChart");
            Add(result, russian ? "Количество завершённых минутных истинных диапазонов в среднем ATR. Для 20 диапазонов нужна 21 завершённая свеча, включая предыдущее закрытие." : "Number of completed one-minute true ranges in the arithmetic ATR average. 20 requires 21 completed bars including the prior close.", "TextBoxStatsAtrPeriod", "LabelStatsAtrPeriod");
            Add(result, russian ? "Цель движения в единицах ATR, зафиксированного при завершении Cloud. По умолчанию 1,5." : "Target movement in ATR units frozen at Cloud completion. Default 1.5.", "TextBoxStatsTargetAtr", "LabelStatsTargetAtr");
            Add(result, russian ? "Граница движения против в единицах зафиксированного ATR. По умолчанию 1. Это статистический анализ, не заявка." : "Adverse movement barrier in frozen ATR units. Default 1. This is statistical analysis, not an order.", "TextBoxStatsStopAtr", "LabelStatsStopAtr");
            Add(result, russian ? "Горизонты реакции в минутах через точку с запятой. По умолчанию 3;9;18. Самый длинный также задаёт расстояние между отобранными наблюдениями." : "Reaction horizons in minutes separated by semicolons. Default 3;9;18; the longest also spaces sampled events.", "TextBoxStatsHorizons", "LabelStatsHorizons");
            Add(result, russian ? "Примерная доля наблюдавшихся дней для подбора параметров. Более поздние дни используются для отдельной проверки; пересекающие границу окна исключаются." : "Approximate share of observed days for parameter selection. Remaining later days are held out, with overlapping feature/label windows excluded.", "TextBoxStatsTrainPercent", "LabelStatsTrainPercent");
            Add(result, russian ? "Минимальное число полных наблюдений в каждой группе подбора и проверки для статуса достаточной выборки. Даже при достаточной выборке сравнение остаётся описательным." : "Minimum completed observations in each fit/test group for the sufficient-sample status. Even sufficient samples give only a descriptive comparison.", "TextBoxStatsMinimumSamples", "LabelStatsMinimumSamples");
            Add(result, russian ? "Оценить реакции после завершённых Cloud по тому же проверенному файлу тиков. Существующие цепочки Cloud не перестраиваются." : "Evaluate reactions after completed Clouds by reading the same verified tick file. Existing Cloud chains are not rebuilt.", "ButtonStatsCalculate");
            Add(result, russian ? "Отменить расчёт статистики без публикации частичных рекомендаций." : "Cancel statistics calculation without publishing partial recommendations.", "ButtonStatsCancel");
            Add(result, russian ? "Применить условия и группу волатильности выбранной рекомендации к готовому Cloud. Показ включает и события вне статистической выборки; файл не читается." : "Apply the selected rule and volatility group to existing Clouds, including events outside the scored sample; no file is read.", "ButtonStatsShow");
            Add(result, russian ? "Счётчики выборки, дата разделения, причины исключений и отдельная папка результатов статистики." : "Statistics sampling counts, split date, exclusions and the independent study output folder.", "TextBoxStatisticsSummary");
            Add(result, russian ? "Подробности выбранного Cloud, кандидата или правила статистики." : "Details of the selected Cloud, candidate or study rule.", "TextBlockCandidateDetails");
            return result;
        }

        internal static Dictionary<string, string> Columns(bool russian)
        {
            return new Dictionary<string, string>
            {
                ["CloudId"] = russian ? "Код Cloud в этом расчёте. Нажмите правой кнопкой по строке, чтобы перейти к его метке." : "Cloud identifier in this calculation; right-click the row to show its exact anchor.",
                ["CandidateId"] = russian ? "Код кандидата дельты для сопоставления наблюдения и будущей реакции." : "Delta candidate identifier used to join observation and future labels.",
                ["Time"] = russian ? "Время события или последнего включённого тика Cloud. Завершение показано отдельно. Часовой пояс не пересчитывается." : "Recorded event or Cloud anchor time; completion is shown separately. No timezone conversion is performed.",
                ["TimeText"] = russian ? "Время кандидата из записи, без пересчёта часового пояса." : "Recorded candidate time, without timezone conversion.",
                ["CompletedAt"] = russian ? "Момент, когда разрывающий тик завершил Cloud; режим одиночных сделок завершается сразу. Пустое значение — не завершён." : "When a breaking tick finished the Cloud; single-trade mode completes immediately. Empty means not completed.",
                ["CompletionReason"] = russian ? "Причина завершения. Forming и OpenAtEnd — незавершённые цепочки; SingleTick — одна сразу завершённая сделка." : "Why the chain ended. Forming and OpenAtEnd are unfinished; SingleTick is one immediately completed trade.",
                ["ImbalancePassed"] = russian ? "Прохождение текущих фильтров показа, включая итоговое число тиков. Исходные CSV не меняются." : "Whether this row passes current display filters, including the final tick-count bounds. Raw CSV values do not change.",
                ["DeltaPercent"] = russian ? "100 × (объём Buy − объём Sell) / общий объём Cloud. Диапазон от −100% до +100%." : "100 times (Buy volume minus Sell volume) divided by total Cloud volume. Signed range from -100% to +100%.",
                ["SidePercent"] = russian ? "Перевес числа сделок: 100 × (число Buy − число Sell) / число всех сделок. Это не объёмная дельта." : "Count imbalance: 100 times (Buy trade count minus Sell trade count) divided by total trade count. Not volume delta.",
                ["Volume"] = russian ? "Сумма объёмов включённых в Cloud сделок, в единицах исходного файла." : "Total included Cloud trade volume in the source file units.",
                ["BuyVolume"] = russian ? "Суммарный объём сделок Buy в наблюдении или Cloud." : "Summed recorded Buy volume in the observation or Cloud.",
                ["SellVolume"] = russian ? "Суммарный объём сделок Sell в наблюдении или Cloud." : "Summed recorded Sell volume in the observation or Cloud.",
                ["TradeCount"] = russian ? "Итоговое число включённых сделок во всём Cloud, не число на момент первого достижения порога суммы." : "Total number of physical included trades in the whole Cloud, not the count when the sum threshold was first reached.",
                ["Price"] = russian ? "Цена последней включённой сделки Cloud. Она может относиться к более раннему моменту, чем завершение." : "Price of the last included Cloud trade; may precede the later completion tick.",
                ["Vwap"] = russian ? "Средняя цена включённых сделок Cloud, взвешенная по объёму." : "Volume-weighted average price of included Cloud trades.",
                ["Qualified.Time"] = russian ? "Первое достижение исходного порога объёма Cloud. Итоговые показатели цепочки тогда ещё не были известны." : "First time the original Cloud volume threshold was reached. Final chain statistics were not yet known then.",
                ["Qualified.Volume"] = russian ? "Объём на момент первого достижения исходного порога суммы Cloud." : "Included volume at the first crossing of the original Cloud sum threshold.",
                ["Direction"] = russian ? "Направление кандидата или оцениваемого движения. Это метка анализа, не открытая позиция." : "Candidate or evaluated movement direction. It is an analysis label, not an executed position.",
                ["Delta"] = russian ? "Объём Buy минус объём Sell в окне признаков." : "Recorded Buy volume minus Sell volume in the feature window.",
                ["PriceChange"] = russian ? "Изменение цены за окно признаков в единицах цены." : "Price change over the feature window, in price units.",
                ["PriceResponse"] = russian ? "Реакция цены на направленный перевес объёма в окне признаков." : "Price response to signed volume imbalance in the feature window.",
                ["HorizonSeconds"] = russian ? "Горизонт будущей реакции в секундах от события кандидата." : "Future reaction horizon measured in seconds from the candidate event.",
                ["Outcome"] = russian ? "Какая граница цены достигнута раньше либо неполнота горизонта. Исполнение заявок не моделируется." : "Which market-path barrier was reached first, or whether the horizon is incomplete. No execution fills are modeled.",
                ["Mfe"] = russian ? "Максимальное движение цены в пользу за горизонт наблюдения, не гарантированно доступная прибыль." : "Largest favorable price excursion over the observation horizon, not guaranteed obtainable profit.",
                ["Mae"] = russian ? "Максимальное движение цены против за горизонт наблюдения." : "Largest adverse price excursion over the observation horizon.",
                ["DataQualityCode"] = russian ? "Оценка качества исходных данных наблюдения. Подробности доступны в журнале." : "Quality assessment of source data for this observation; inspect the journal for details.",
                ["ReasonCode"] = russian ? "Причина появления кандидата или события аудита." : "Reason for the candidate or audit event.",
                ["Kind"] = russian ? "Тип события или Cloud. SingleTrade содержит одну включённую сделку, Accumulated — несколько." : "Event or Cloud type; SingleTrade contains one included trade, Accumulated more than one.",
                ["CorrelationId"] = russian ? "Локальный код для связи записей аудита. Это не игнорируемое техническое поле Id из файла тиков." : "Local identifier joining related audit records. It is not the ignored technical Id from the tick file.",
                ["Message"] = russian ? "Подробное пояснение события аудита и возможной проблемы входных данных." : "Detailed explanation of the audit event and any input problem.",
                ["InsideImbalance.BuyRatioPercent"] = russian ? "Максимальное диагональное отношение с перевесом Buy внутри Cloud. 300% означает 3 к 1; пустое значение — нет сравнения." : "Maximum dominant Buy diagonal ratio in this profile. 300% means 3 to 1; empty means no comparison.",
                ["InsideImbalance.SellRatioPercent"] = russian ? "Максимальное диагональное отношение с перевесом Sell внутри Cloud. 300% означает 3 к 1; пустое значение — нет сравнения." : "Maximum dominant Sell diagonal ratio in this profile. 300% means 3 to 1; empty means no comparison.",
                ["ContextImbalance.BuyRatioPercent"] = russian ? "Максимальное диагональное отношение с перевесом Buy в окружающем потоке. 300% означает 3 к 1; пустое значение — нет сравнения." : "Maximum dominant Buy diagonal ratio in this profile. 300% means 3 to 1; empty means no comparison.",
                ["ContextImbalance.SellRatioPercent"] = russian ? "Максимальное диагональное отношение с перевесом Sell в окружающем потоке. 300% означает 3 к 1; пустое значение — нет сравнения." : "Maximum dominant Sell diagonal ratio in this profile. 300% means 3 to 1; empty means no comparison.",
                ["ContextImbalance.DeltaPercent"] = russian ? "Знаковая объёмная дельта всех сделок в сохранённом окне окружающего потока, проценты." : "Signed volume delta percentage of all trades in the saved surrounding-flow window.",
                ["Layer"] = russian ? "Независимый слой Cloud. Метрики двух слоёв не объединяются в общую независимую выборку." : "Independent Cloud layer; metrics from the two layers are not pooled.",
                ["Regime"] = russian ? "Группа волатильности ATR. Границы низкой, средней и высокой группы определены только на ранней выборке." : "ATR volatility group. Low/middle/high boundaries are fitted only on the earlier sample.",
                ["Reaction"] = russian ? "Продолжение следует знаку итоговой объёмной дельты Cloud; разворот направлен противоположно." : "Continuation follows the sign of final Cloud volume delta; reversal uses the opposite direction.",
                ["HorizonMinutes"] = russian ? "Горизонт реакции в минутах после завершающего тика. Не зависит от окна признаков и таймфрейма отображения." : "Future reaction horizon in minutes after the completion tick; separate from feature windows and display timeframe.",
                ["Parameters"] = russian ? "Правило, выбранное на ранней выборке. Off отключает перевес, но границы числа тиков могут действовать. Примените кнопкой показа параметров." : "Rule selected on the earlier sample. Off means no imbalance filter; count bounds may still apply. Apply it with Show recommended parameters.",
                ["Fit.Count"] = russian ? "Число полных отобранных событий, прошедших правило на раннем участке подбора." : "Number of complete sampled events passing the rule in the earlier fitting period.",
                ["Fit.WinPercent"] = russian ? "Процент достижения цели раньше противоположной границы на подборе. Истечение времени считается недостижением цели." : "Percentage that touched the target before the adverse barrier on fit. Timeouts count as not reaching the target.",
                ["Fit.LowerPercent"] = russian ? "Приближённая нижняя граница Уилсона 95% для ранжирования правил на подборе. Это не поправка на множественный перебор и не доказательство прибыльности." : "Approximate 95% Wilson lower bound used to rank fit rules. This is not a correction for testing many rules and not proof of profitability.",
                ["Test.Count"] = russian ? "Полные отобранные события, прошедшие уже выбранное правило на позднем участке проверки после удаления пересечений." : "Complete sampled events passing the already chosen rule on the later check period, after purge.",
                ["Test.WinPercent"] = russian ? "Процент достижения цели раньше отмены на поздней проверке. Этот участок не выбирает правило." : "Target-before-adverse percentage on the later check, which never chooses the rule.",
                ["TestBaseline.WinPercent"] = russian ? "Доля достижения цели без выбранного фильтра на той же поздней проверке, в том же слое, режиме волатильности, горизонте и направлении реакции." : "Later-check target rate without the selected filter, within the same layer, volatility group, horizon and reaction direction.",
                ["Test.MeanMfeAtr"] = russian ? "Среднее максимальное движение в пользу за полный горизонт на поздней проверке, в единицах зафиксированного ATR. Это не доступная прибыль сделки." : "Mean maximum favorable excursion over the full horizon on the later check, in frozen ATR units; not achievable trading profit.",
                ["Test.MeanMaeAtr"] = russian ? "Среднее максимальное движение против за полный горизонт на поздней проверке, в единицах зафиксированного ATR." : "Mean maximum adverse excursion over the full horizon on the later check, in frozen ATR units.",
                ["TriedRules"] = russian ? "Количество заранее заданных правил, проверенных на подборе, включая базовое. Полный перебор всех сочетаний параметров не выполняется." : "Number of predefined single-coordinate rules tried on fit, including baseline. No full Cartesian parameter search.",
                ["Status"] = russian ? "Достаточность выборки и наличие прироста доли целей на поздней проверке у выбранного на подборе правила. Это не разрешение на реальную торговлю." : "Whether samples are large enough and the fit-selected rule improved the descriptive later-check target rate against baseline. This is not an execution approval.",
            };
        }

        private static void Add(Dictionary<string, string> result, string help, params string[] names)
        { foreach (string name in names) { result[name] = help; } }
    }

    public partial class OrderFlowResearchUi
    {
        private void InitializeFieldHelp()
        {
            bool russian = OsLocalization.CurLocalization == OsLocalization.OsLocalType.Ru;
            foreach (KeyValuePair<string, string> help in OrderFlowResearchHelp.Fields(russian))
            {
                if (FindName(help.Key) is FrameworkElement field)
                {
                    field.ToolTip = help.Value;
                    ToolTipService.SetShowOnDisabled(field, true);
                    ToolTipService.SetInitialShowDelay(field, 300);
                    ToolTipService.SetShowDuration(field, 60000);
                }
            }
            Dictionary<string, string> columns = OrderFlowResearchHelp.Columns(russian);
            foreach (string name in new[] { "DataGridCandidates", "DataGridClouds", "DataGridClouds2", "DataGridJournal", "DataGridRecommendations" })
            {
                if (FindName(name) is not DataGrid grid) { continue; }
                foreach (DataGridColumn column in grid.Columns)
                {
                    if (column is not DataGridBoundColumn bound || bound.Binding is not Binding binding ||
                        !columns.TryGetValue(binding.Path.Path, out string help)) { continue; }
                    Style header = new Style(typeof(DataGridColumnHeader), column.HeaderStyle);
                    header.Setters.Add(new Setter(ToolTipProperty, help)); column.HeaderStyle = header;
                    Style cell = new Style(typeof(DataGridCell), column.CellStyle);
                    cell.Setters.Add(new Setter(ToolTipProperty, help)); column.CellStyle = cell;
                }
            }
        }
    }
}
