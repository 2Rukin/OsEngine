/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace OsEngine.OsData.OrderFlow.Explorer
{
    public partial class CloudExplorerControl
    {
        private List<ExplorerOption> _patternOptions;
        private ExplorerPatternRun _patternRun;
        private ExplorerPatternCard _patternCard;
        private ExplorerPatternSnapshot _patternExample;
        private bool _pendingPatternExample;
        private string _patternQualityText;
        private ExplorerPatternCard[] _patternCards = Array.Empty<ExplorerPatternCard>();
        private void InitializePatterns()
        {
            _patternOptions = ExplorerOptions.Create(new ExplorerPatternSpec(), "Direction", "Context", "ProfilePreset", "Overlap", "DayBoundary");
            DataGridPatternOptions.ItemsSource = _patternOptions;
            ComboBoxPatternDirection.ItemsSource = new[] { "Оба", "Рост", "Снижение" }; ComboBoxPatternDirection.SelectedIndex = 0;
            ComboBoxPatternContext.ItemsSource = new[] { "Оба", "Сегодня", "Текущая неделя" }; ComboBoxPatternContext.SelectedIndex = 0;
            ComboBoxPatternExample.ItemsSource = new[] { "Успех", "Неудача", "Контроль" }; ComboBoxPatternExample.SelectedIndex = 0;
            ButtonPatternSearch.Click += PatternSearch; ButtonPatternOpen.Click += PatternOpen; ButtonPatternChart.Click += PatternChart;
            ButtonPatternReplay.Click += PatternReplay; DataGridPatternCards.SelectionChanged += PatternSelected; ComboBoxPatternExample.SelectionChanged += PatternExampleChanged;
            TextBoxPatternFilter.TextChanged += PatternFilter;
        }
        private void DisposePatterns()
        {
            ButtonPatternSearch.Click -= PatternSearch; ButtonPatternOpen.Click -= PatternOpen; ButtonPatternChart.Click -= PatternChart;
            ButtonPatternReplay.Click -= PatternReplay; DataGridPatternCards.SelectionChanged -= PatternSelected; ComboBoxPatternExample.SelectionChanged -= PatternExampleChanged;
            TextBoxPatternFilter.TextChanged -= PatternFilter;
            DataGridPatternCards.ItemsSource = null; DataGridPatternWeek.ItemsSource = null; _patternRun = null; _patternExample = null;
        }
        private void PatternSearch(object sender, RoutedEventArgs e)
        {
            try
            {
                RequireIdle(); DataGridPatternOptions.CommitEdit(DataGridEditingUnit.Cell, true); DataGridPatternOptions.CommitEdit(DataGridEditingUnit.Row, true);
                ExplorerPatternSpec plan;
                try { plan = ExplorerOptions.Read(new ExplorerPatternSpec(), _patternOptions); }
                catch (ExplorerInputException error) { throw new ExplorerInputException("Pattern." + error.Field, ExplorerValidation.UserMessage(error, ""), inner: error); }
                plan = plan with { Direction = ComboBoxPatternDirection.SelectedIndex == 1 ? "Long" : ComboBoxPatternDirection.SelectedIndex == 2 ? "Short" : "Both",
                    Context = ComboBoxPatternContext.SelectedIndex == 1 ? "Day" : ComboBoxPatternContext.SelectedIndex == 2 ? "Week" : "Both",
                    ProfilePreset = CheckBoxPatternExpert.IsChecked == true ? "expert-1" : "three-causal-scales-1" };
                plan.Validate(); ExplorerRunSpec spec = ReadSpec();
                if (CheckBoxPatternExpert.IsChecked != true) { spec = ExplorerPatternSpec.Preset(spec); }
                ExplorerValidation.Preflight(spec); StopPlayback();
                TextBoxPatternQuality.Text = "План зафиксирован ДО исходов.\n" + $"Направление: {ComboBoxPatternDirection.Text}; контекст: {ComboBoxPatternContext.Text}.\n" +
                    $"Цель {plan.TargetAtr} ATR; неблагоприятный барьер {plan.AdverseAtr} ATR. Нет ATR → неизвестно, не ноль.\nГоризонты: {string.Join(";", plan.HorizonsMinutes)} мин и конец той же исходной даты.\n" +
                    $"Бюджет: {plan.CandidateBudget} правил × направления × горизонты; максимум {plan.MaximumCards} карточек. Seed {plan.Seed}.\n" +
                    $"Профили: {string.Join(", ", spec.Profiles.Select(p => p.Key))}; набор {plan.ProfilePreset}.\n" +
                    "Граница дня: полночь по ленте. Неделя: с понедельника, без предположений о часовом поясе/сессии.\nFit/Test разделяются целыми неделями. Контроль сопоставим по часу, активности и причинному ATR.\n" +
                    "Число дней/недель будет известно после проверки файла. Этап, строки, память и время — внизу окна.";
                TabControlResult.SelectedItem = TabItemPatterns; TabControlPatterns.SelectedItem = TabItemPatternQuality;
                StartJob(token => ExplorerPatternRunner.Run(spec, plan, token, Progress), result => InstallPattern((ExplorerPatternRun)result));
            }
            catch (Exception error) { Error(error); }
        }
        private void PatternOpen(object sender, RoutedEventArgs e)
        {
            try
            {
                RequireIdle(); Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Поиск предвестников|manifest.json", Title = "Открыть сохранённый поиск предвестников" };
                if (dialog.ShowDialog() != true) { return; } StopPlayback(); string input = _run?.Spec.InputPath;
                StartJob(token => ExplorerPatternRunner.Open(Path.GetDirectoryName(dialog.FileName), input, token), result => InstallPattern((ExplorerPatternRun)result));
            }
            catch (Exception error) { Error(error); }
        }
        private void InstallPattern(ExplorerPatternRun pattern)
        {
            InstallRun(pattern.Run); _patternRun = pattern; _patternCard = null; _patternExample = null;
            _view = new ExplorerView { MinimumVolume = null }; RestoreViews();
            ExplorerPatternQuality quality = JsonSerializer.Deserialize<ExplorerPatternQuality>(File.ReadAllText(Path.Combine(pattern.Directory, "quality.json")), ExplorerStorage.Json);
            TextBoxPatternQuality.Text = $"Поиск {pattern.Manifest.Hash}\nГрамматика {ExplorerPatternSpec.Grammar}; набор {pattern.Plan.ProfilePreset}.\n" +
                $"Применённые профили: {string.Join(", ", pattern.Run.Spec.Profiles.Select(p => p.Key))}\n" +
                $"Даты: {quality.Days}; недели: {quality.Weeks} → Cloud: {quality.Clouds:N0} → эпизоды: {quality.Episodes:N0}\n" +
                $"Якоря: {quality.Anchors:N0} → полные исходы (по направлениям/горизонтам): {quality.CompleteLabels:N0}\n" +
                $"Сетка правил: {quality.CandidateGrid:N0}; рассмотрено {quality.Considered}; отсечено бюджетом {quality.BudgetExcluded}.\n" +
                $"Достаточная поддержка Fit: {quality.Supported} гипотез (правило × направление × горизонт); проверено на Test: {quality.Tested}.\nНачало Test: {(quality.TestStart.HasValue ? quality.TestStart.Value.ToString("yyyy-MM-dd") : "нет: менее двух недель")}.\n" +
                string.Join("\n", quality.Reasons.Select(p => ExplorerValidation.Status(p.Key) + ": " + p.Value.ToString("N0"))) + "\n\n" + quality.Warning +
                (quality.Tested == 0 ? "\n\nСценариев нет: не хватило разных дат/недель, сопоставимого контроля либо положительного отличия от контроля на Fit. Это корректный пустой результат, не доказательство отсутствия закономерностей." : "");
            _patternQualityText = TextBoxPatternQuality.Text;
            _patternCards = ExplorerStorage.ReadRows<ExplorerPatternCard>(pattern.Directory, "cards").Take(32).ToArray();
            TextBoxPatternFilter.Clear(); DataGridPatternCards.ItemsSource = _patternCards;
            TextBoxPatternCard.Text = "Выберите сценарий на вкладке 3. Для каждого доступны успех, неудача и контроль при наличии.";
            DataGridPatternWeek.ItemsSource = null; TabControlResult.SelectedItem = TabItemPatterns; TabControlPatterns.SelectedItem = TabItemPatternQuality;
            TextBlockStatus.Text = "Поиск открыт. Числа воспроизводятся локальным движком. Для нового подбора после Test нужен новый неиспользованный период.";
            TextBlockIdentity.Text = ExplorerPatternSpec.Version + " · " + pattern.Manifest.Hash;
        }
        private static string MetricsText(string name, ExplorerPatternMetrics m) => $"{name}: {m.Success}/{m.Eligible} успехов; контроль {m.ControlSuccess}/{m.Control}; разных дат {m.Dates}, недель {m.Weeks}; у контроля дат {m.ControlDates}, недель {m.ControlWeeks}.\n" +
            $"Частота {m.Rate:P1}; контроль {m.ControlRate:P1}; разность {m.Difference:P1}; разброс частот недель {m.WeeklyMin:P1}…{m.WeeklyMax:P1}. Неизвестно {m.Unknown}; неполно {m.Incomplete}.\n" +
            $"До сопоставления {m.BeforeMatchingCases}/{m.BeforeMatchingControls}; без пары {m.Unmatched}; избыток группы {m.BalanceExcluded}; пересечение Fit/Test {m.Purged}. Состав и причины — fit/test-membership.bin.";
        private void PatternSelected(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (DataGridPatternCards.SelectedItem is not ExplorerPatternCard card)
                { _patternCard = null; _patternExample = null; _pendingPatternExample = false; DataGridPatternWeek.ItemsSource = null; TextBoxPatternCard.Clear(); return; }
                if (_playback != null) { _patternCard = card; if (_frame != null) { PaintPatternFrame(_frame); } return; }
                _patternCard = card; ShowPatternCard(); TabControlPatterns.SelectedItem = TabItemPatternCard; LoadPatternExample();
            }
            catch (Exception error) { Error(error); }
        }
        private void ShowPatternCard()
        {
            if (_patternCard == null || _patternRun == null) { return; }
            ExplorerPatternCard c = _patternCard;
            TextBoxPatternCard.Text = $"{c.Rule.Text}\nВерсия {ExplorerPatternSpec.Grammar}; ID {c.Id}\n" +
                $"{(c.Direction == "Long" ? "Рост" : "Снижение")}; горизонт {(c.Minutes == 0 ? "до конца исходного дня" : c.Minutes + " минут")}; цель {_patternRun.Plan.TargetAtr} ATR, барьер {_patternRun.Plan.AdverseAtr} ATR.\n" +
                "Часы: все исходные, сопоставление контроля в том же часе. Контекст: " + _patternRun.Plan.Context + ".\n" +
                MetricsText("Fit", c.Fit) + "\n" + MetricsText("Test", c.Test) + "\n" + c.Status +
                "\nПерекрытия исключены общей политикой до исходов. Признаки с неизвестным фоном не превращаются в контроль. Не исполнение заявок, не прибыльность, не финальный OOS.";
        }
        private void PatternExampleChanged(object sender, SelectionChangedEventArgs e) { try { if (_playback == null) { LoadPatternExample(); } } catch (Exception error) { Error(error); } }
        private void LoadPatternExample()
        {
            if (_patternCard == null || _patternRun == null || _playback != null) { return; }
            ShowPatternCard(); _patternExample = null; DataGridPatternWeek.ItemsSource = null;
            if (_job != null) { _pendingPatternExample = true; return; }
            _pendingPatternExample = false;
            ExplorerPatternMetrics test = _patternCard.Test, fit = _patternCard.Fit;
            int exampleIndex = ComboBoxPatternExample.SelectedIndex;
            string id = exampleIndex switch { 1 => test.FailureId ?? fit.FailureId, 2 => test.ControlId ?? fit.ControlId, _ => test.SuccessId ?? fit.SuccessId };
            if (id == null) { TextBoxPatternCard.AppendText("\nДля выбранного класса нет примера в сопоставленной выборке. Другой класс не подставляется."); return; }
            ExplorerPatternRun pattern = _patternRun; ExplorerPatternCard card = _patternCard;
            long ordinal = long.Parse(id.Substring(id.LastIndexOf('/') + 1), CultureInfo.InvariantCulture) - 1;
            StartJob(token =>
            {
                ExplorerPatternSnapshot snapshot = ExplorerStorage.ReadAt<ExplorerPatternSnapshot>(pattern.Directory, "snapshots", ordinal);
                ExplorerPatternLabel label = null;
                foreach (ExplorerPatternLabel item in ExplorerStorage.ReadRows<ExplorerPatternLabel>(pattern.Directory, "labels"))
                { token.ThrowIfCancellationRequested(); if (item.SnapshotId == id && item.Direction == card.Direction && item.Minutes == card.Minutes) { label = item; break; } }
                return (snapshot, label);
            }, result =>
            {
                if (_patternRun != pattern || _patternCard != card || ComboBoxPatternExample.SelectedIndex != exampleIndex || _playback != null) { return; }
                (ExplorerPatternSnapshot snapshot, ExplorerPatternLabel label) = ((ExplorerPatternSnapshot, ExplorerPatternLabel))result;
                _patternExample = snapshot; DataGridPatternWeek.ItemsSource = snapshot.WeekDays;
                TextBoxPatternCard.AppendText($"\n\nПример {snapshot.Id}\nПрофиль {snapshot.Profile}; событие {snapshot.Kind}; исходное событие {snapshot.EventId}.\n" +
                    $"Наблюдалось {snapshot.ObservedAt:O}; известно {snapshot.KnownAt:O}, ordinal {snapshot.KnownSequence}. Неделя: {ExplorerValidation.Status(snapshot.WeekStatus)}.\n" +
                    $"ATR {snapshot.Atr}; дельта дня {snapshot.DayDelta:P1}; дельта недели {snapshot.WeekDelta:P1}; Cloud {snapshot.CloudVolume}; эпизодов сегодня {snapshot.DayEpisodes}.\n" +
                    (label == null ? "Метка отсутствует." : $"Исход: {ExplorerValidation.Status(label.Status)}; первое касание {ExplorerValidation.Status(label.FirstHit)} {label.FirstHitAt:O}; MFE {label.MfeAtr}; MAE {label.MaeAtr}; изменение {label.Return}."));
                TextBlockStatus.Text = "Пример открыт без расчёта. Ниже — известная на якоре часть недели. Кнопка 5 открывает цену этого дня.";
            });
        }
        private void PatternChart(object sender, RoutedEventArgs e)
        {
            try
            {
                RequireIdle(); if (_patternExample == null) { throw new InvalidOperationException("Сначала выберите доступный пример успеха, неудачи или контроля."); }
                StopPlayback(); ClearObservationContext(); _chart.KnownBoundary = _patternExample.KnownAt;
                ShowInterval(_patternExample.KnownAt.Date, _patternExample.KnownAt.Date.AddDays(1).AddTicks(-1)); TabControlResult.SelectedItem = TabItemChart;
            }
            catch (Exception error) { Error(error); }
        }
        private void PatternReplay(object sender, RoutedEventArgs e)
        {
            try
            {
                RequireIdle(); if (_patternRun == null) { return; }
                ExplorerRunSpec input = RequestProvider?.Invoke();
                if (input != null) { _run = _run with { Spec = _run.Spec with { InputPath = input.InputPath } }; }
                StopPlayback(); BeginPlayback(); _playback.Step(); TabControlResult.SelectedItem = TabItemChart;
                TextBoxPatternCard.Text = "Причинный реплей: исторические итоги скрыты. Условия и метки появятся только по мере обработки исходных сделок.";
                DataGridPatternWeek.ItemsSource = null;
            }
            catch (Exception error) { Error(error); }
        }
        private void PaintPatternFrame(ExplorerFrame frame)
        {
            if (_patternRun == null) { return; }
            ExplorerPatternSnapshot snapshot = frame.PatternSnapshots.LastOrDefault();
            DataGridPatternWeek.ItemsSource = snapshot?.WeekDays;
            TextBoxPatternCard.Text = $"Причинный кадр #{frame.Sequence}: последние {frame.PatternSnapshots.Length} якорей и {frame.PatternLabels.Length} уже доступных меток (буфер 250).\n" +
                (snapshot == null ? "Якорей пока нет." : $"Последний якорь {snapshot.Id}; известен {snapshot.KnownAt:O}; {ExplorerValidation.Status(snapshot.WeekStatus)}.\n" +
                (_patternCard == null ? "" : "Условие выбранного правила: " + (_patternCard.Rule.Text) + " → " + (ExplorerPatternGrammar.Matches(_patternCard.Rule, snapshot)?.ToString() ?? "неизвестно"))) +
                "\nПоследние закрывшиеся исходы:\n" + string.Join("\n", frame.PatternLabels.TakeLast(8).Select(l => l.SnapshotId + " · " + ExplorerValidation.Status(l.Status) + " · " + ExplorerValidation.Status(l.FirstHit)));
        }
        private void PatternReplayMode(bool replay)
        {
            DataGridPatternCards.IsEnabled = !replay; DataGridPatternCards.Visibility = replay ? Visibility.Hidden : Visibility.Visible;
            if (_patternRun == null) { return; }
            TextBoxPatternQuality.Text = replay ? "Причинный реплей: полные итоги и исторический каталог сценариев скрыты. Выбранное правило и его текущий контекст — во вкладке 4. Для возврата нажмите «Вернуться к истории»." : _patternQualityText;
            if (!replay) { ShowPatternCard(); DataGridPatternWeek.ItemsSource = _patternExample?.WeekDays; }
        }
        private void PatternFilter(object sender, TextChangedEventArgs e)
        {
            try
            {
                if (_playback != null) { return; } string text = TextBoxPatternFilter.Text.Trim();
                DataGridPatternCards.ItemsSource = _patternCards.Where(c => c.Rule.Text.Contains(text, StringComparison.OrdinalIgnoreCase) || c.Status.Contains(text, StringComparison.OrdinalIgnoreCase)).ToArray();
            }
            catch (Exception error) { Error(error); }
        }
    }
}
