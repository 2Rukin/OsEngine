/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.OsData.OrderFlow.Explorer;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace OsEngine.OsData.OrderFlow.Calibration
{
    public partial class CloudCalibrationWindow
    {
        private ParameterCell _chainCell;
        private CloudRule _editingRule;

        #region Tick distribution and candidate selection

        private void RenderTicks(TickStatistics stats)
        {
            _ticks.Distribution(L("Physical tick volume — click a bin to select its lower value", "Объём physical tick — щелчок выбирает нижнее значение столбца"), stats.Volumes, true);
            TextBlockTickSummary.Text = L("Passed / total", "Прошло / всего") + " " + stats.Passed + "/" + stats.Total + " (" + N(stats.PassedPercent) + "%) · " +
                L("active dates ", "активных дат ") + stats.ActiveDays + L(" · calendar dates ", " · календарных дат ") + stats.CalendarDates + L(" · events/active day ", " · событий/активный день ") + N(stats.EventsPerActiveDay) +
                L(" · gap P50/P95 ms ", " · пауза P50/P95 мс ") + stats.Gaps.P50 + "/" + stats.Gaps.P95;
            foreach (Button old in WrapPanelQuantiles.Children.OfType<Button>()) { old.Click -= QuantileClick; }
            WrapPanelQuantiles.Children.Clear();
            DistributionSummary d = stats.Volumes;
            decimal?[] values = { d.P50, d.P75, d.P90, d.P95, d.P99, d.P995, d.P999, d.Maximum };
            string[] labels = { "P50", "P75", "P90", "P95", "P99", "P99.5", "P99.9", "Max" };
            for (int i = 0; i < labels.Length; i++)
            {
                Button button = new Button { Content = labels[i] + " " + (values[i].HasValue ? N(values[i].Value) : "—"), Tag = values[i],
                    Height = double.NaN, MinHeight = 30, Margin = new Thickness(4), Padding = new Thickness(8, 4, 8, 4), IsEnabled = values[i].HasValue };
                button.Click += QuantileClick; WrapPanelQuantiles.Children.Add(button);
            }
            foreach (decimal value in _tickPins)
            {
                Button pin = new Button { Content = L("Pinned ", "Закреплён ") + N(value), Tag = value, Height = double.NaN, MinHeight = 30, Margin = new Thickness(4) };
                pin.Click += QuantileClick; WrapPanelQuantiles.Children.Add(pin);
            }
            TextBlockStatus.Text = L("Tick statistics ready. Formation is recalculated only by Form grid.", "Статистика тиков готова. Формирование пересчитывается только кнопкой «Сформировать сетку».");
        }
        private void QuantileClick(object sender, RoutedEventArgs e)
        { try { if (((Button)sender).Tag is decimal value) { TickSelected(value); } } catch (Exception error) { Report(error); } }
        private void TickSelected(object value)
        { try { TextBoxTickVolume.Text = ((decimal)value).ToString("G29", CultureInfo.InvariantCulture); } catch (Exception error) { Report(error); } }
        private void PinTickClick(object sender, RoutedEventArgs e)
        {
            try
            {
                decimal value = Number(TextBoxTickVolume.Text); if (!_tickPins.Contains(value)) { _tickPins.Add(value); } SaveWorkspace();
                if (_job == null) { RefreshTicks(); }
                TextBlockStatus.Text = L("Pinned tick thresholds", "Закреплённые пороги тиков") + " · " + string.Join("; ", _tickPins);
            }
            catch (Exception error) { Report(error); }
        }
        private static decimal HeatValue(ParameterCell cell, string metric)
        {
            if (metric == "Chains / active day") { return cell.Summary.ChainsPerActiveDay; }
            if (metric == "Singles / active day") { return cell.Summary.SinglesPerActiveDay; }
            if (metric == "NeighborSensitivity") { return cell.NeighborSensitivity; }
            string[] parts = metric.Split(' '); DistributionSummary d = cell.Summary.Distributions[parts[0]];
            return (parts[1] == "P50" ? d.P50 : parts[1] == "P99" ? d.P99 : d.P95) ?? 0;
        }
        private void RenderHeatmap()
        {
            if (_run == null) { return; }
            string metric = (string)ComboBoxHeatMetric.SelectedItem ?? HeatMetrics[0];
            int[] gaps = _run.Spec.Gaps.OrderBy(v => v).ToArray(), ranges = _run.Spec.Ranges.OrderBy(v => v).ToArray();
            _heatmap.Caption = CalibrationText.Label(metric) + L(" · X Range ticks / Y Gap ms", " · X Размах ticks / Y Пауза мс");
            _heatmap.Set(_run.Manifest.Cells.Where(c => c.Formation.Mode == FormationMode.Chain).Select(c => new PlotDatum(
                Array.IndexOf(ranges, c.Formation.MaximumRangeTicks), Array.IndexOf(gaps, c.Formation.MaximumGapMilliseconds), HeatValue(c, metric),
                metric + "=" + HeatValue(c, metric).ToString("G29", CultureInfo.InvariantCulture) + "\n" + CandidateText(c), c)).ToArray(), ranges.Select(v => v.ToString()).ToArray(), gaps.Select(v => v.ToString()).ToArray());
        }
        private string CandidateText(ParameterCell cell) => _run.Spec.Range.Name + " · MinimumTickVolume=" + cell.Formation.MinimumTickVolume +
            " · Gap=" + cell.Formation.MaximumGapMilliseconds + " · Range=" + cell.Formation.MaximumRangeTicks +
            "\nn=" + cell.Summary.Total + " · active days=" + cell.Summary.ActiveDays + " · chains/day=" + N(cell.Summary.ChainsPerActiveDay) +
            " · singles/day=" + N(cell.Summary.SinglesPerActiveDay) + " · NeighborSensitivity=" + N(cell.NeighborSensitivity) + "%" +
            "\n" + string.Join(" · ", cell.Summary.Distributions.Select(p => p.Key + " P50/P95/P99=" + p.Value.P50 + "/" + p.Value.P95 + "/" + p.Value.P99)) +
            "\nNeighbors P95 " + string.Join(" · ", cell.NeighborRanges.Select(p => p.Key + "=" + p.Value.Minimum + "…" + p.Value.Maximum));
        private void HeatMetricChanged(object sender, SelectionChangedEventArgs e) { try { RenderHeatmap(); } catch (Exception error) { Report(error); } }
        private void CellSelected(object value)
        {
            try
            {
                _cell = _chainCell = (ParameterCell)value; _restoring = true;
                ComboBoxFormation.SelectedItem = FormationMode.Chain; ComboBoxGap.SelectedItem = _cell.Formation.MaximumGapMilliseconds; ComboBoxRange.SelectedItem = _cell.Formation.MaximumRangeTicks;
                _restoring = false; TextBlockCandidate.Text = CandidateText(_cell); _editingRule = null;
                QueueFilter();
            }
            catch (Exception error) { Report(error); }
        }
        private void SelectCellClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_run == null || ComboBoxGap.SelectedItem is not int gap || ComboBoxRange.SelectedItem is not int range) { return; }
                CellSelected(_run.Manifest.Cells.Single(c => c.Formation.Mode == FormationMode.Chain && c.Formation.MaximumGapMilliseconds == gap && c.Formation.MaximumRangeTicks == range));
            }
            catch (Exception error) { Report(error); }
        }
        private void PinClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_run == null || _cell == null) { return; }
                if (!_pins.Any(p => p.FormationHash == _cell.FormationHash))
                {
                    if (_pins.Count >= 256) { throw new InvalidOperationException(L("Maximum 256 pinned candidates", "Максимум 256 закреплённых вариантов")); }
                    _pins.Add(new PinnedCandidate(_run.Directory, _run.Spec.Range.Id, _run.Spec.Range.Name, _cell.Formation, _cell.FormationHash, _cell.Summary, _cell.NeighborSensitivity));
                }
                SaveWorkspace(); TextBlockStatus.Text = L("Pinned; compare by the table button", "Закреплено; сравнение доступно по кнопке таблицы");
            }
            catch (Exception error) { Report(error); }
        }
        private void CreateRuleClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_cell == null) { throw new InvalidOperationException(L("First select a candidate", "Сначала выберите вариант")); }
                _editingRule = null; TabControlWorkspace.SelectedItem = TabItemTuner; QueueFilter();
            }
            catch (Exception error) { Report(error); }
        }

        #endregion

        #region Immediate saved-data filters

        private DiagonalSettings ReadDiagonal() => new DiagonalSettings { Enabled = CheckBoxDiagonalFilter.IsChecked == true,
            Source = (DiagonalSource)(ComboBoxDiagonalSource.SelectedItem ?? DiagonalSource.Inside),
            Direction = (FlowDirection)(ComboBoxDiagonalDirection.SelectedItem ?? Calibration.FlowDirection.Any), RatioThreshold = Number(TextBoxRatio.Text),
            MinimumDominantVolume = Number(TextBoxDominant.Text), MinimumDifference = Number(TextBoxDifference.Text), MinimumStackLength = Integer(TextBoxStack.Text) };
        private CloudFilters ReadFilters()
        {
            CloudFilters filters = new CloudFilters { DeltaDirection = (FlowDirection)ComboBoxDeltaDirection.SelectedItem, Diagonal = ReadDiagonal() };
            foreach (KeyValuePair<string, (TextBox Minimum, TextBox Maximum)> editor in _filters)
            { typeof(CloudFilters).GetProperty(editor.Key).SetValue(filters, new NumericFilter(Optional(editor.Value.Minimum.Text), Optional(editor.Value.Maximum.Text))); }
            filters.Validate(); return filters;
        }
        private void FilterChanged(object sender, EventArgs e)
        { try { if (!_restoring) { QueueFilter(); } } catch (Exception error) { Report(error); } }
        private void QueueFilter()
        {
            ButtonSaveRule.IsEnabled = false; _debounce.Stop(); _debounce.Start();
        }
        private void Debounced(object sender, EventArgs e)
        {
            try
            {
                _debounce.Stop(); if (_closed || _run == null || _run.Spec.TickDistributionOnly) { return; }
                if (_job != null && _jobKind != "Filters") { _debounce.Start(); return; }
                FormationMode mode = (FormationMode)ComboBoxFormation.SelectedItem;
                ParameterCell cell = mode == FormationMode.Single ? _run.Manifest.Cells.Single(c => c.Formation.Mode == FormationMode.Single) : _chainCell;
                if (cell == null) { return; } _cell = cell;
                CloudFilters filters = ReadFilters(); RuleKind kind = (RuleKind)ComboBoxRuleKind.SelectedItem; CalibrationRun run = _run;
                CloudRule preview = new CloudRule { Name = TextBoxRuleName.Text.Trim(), BundlePath = run.Directory, Provenance = run.Spec,
                    Formation = cell.Formation, Kind = kind, Filters = filters, Enabled = CheckBoxEnabled.IsChecked == true, Visible = CheckBoxVisible.IsChecked == true };
                int bucket = (int)ComboBoxBucket.SelectedItem; string timeMode = (string)ComboBoxTimeMode.SelectedItem;
                FormationSpec mapFormation = timeMode == "Single" ? run.Manifest.Cells.Single(c => c.Formation.Mode == FormationMode.Single).Formation :
                    timeMode == "Chain" ? _chainCell?.Formation ?? throw new InvalidOperationException(L("Select a Chain candidate for this time map", "Выберите вариант Chain для этой time map")) : cell.Formation;
                StartJob("Filters", token =>
                {
                    EventSummary summary = CalibrationEngine.Filter(run, cell.Formation, kind, filters, bucket, token);
                    CloudFilters mapFilter = timeMode == "Passed rule" ? filters : new CloudFilters { Diagonal = filters.Diagonal with { Enabled = false } };
                    RuleKind mapKind = timeMode == "Diagonal" ? RuleKind.Diagonal : timeMode == "Passed rule" ? kind : RuleKind.Standard;
                    EventSummary map = timeMode == "Passed rule" ? summary : CalibrationEngine.Filter(run, mapFormation, mapKind, mapFilter, bucket, token);
                    return (summary, map, preview);
                }, result =>
                {
                    (EventSummary summary, EventSummary map, CloudRule rule) = ((EventSummary, EventSummary, CloudRule))result;
                    _windows.CloseAll(); _preview = rule; RenderRule(summary, map); ButtonSaveRule.IsEnabled = true;
                });
            }
            catch (Exception error) { Report(error); }
        }
        private void RenderRule(EventSummary summary, EventSummary map)
        {
            TextBlockFormation.Text = _run.Spec.Range.Name + " · " + _cell.Formation + "\n" + L("Frozen formation; enabled post-filters use AND. Empty bounds are disabled.",
                "Зафиксированное формирование; включённые post-фильтры объединены AND. Пустые границы выключены.");
            TextBlockRuleSummary.Text = _preview.Kind + " / " + _preview.Formation.Mode + " · passed/total " + summary.Passed + "/" + summary.Total +
                " · Clouds/active day " + N(summary.EventsPerActiveDay) + " · active days " + summary.ActiveDays + "\n" +
                string.Join(" · ", new[] { "Volume", "Delta", "DiagonalDelta" }.Select(k => k + " P50/P95 " + summary.Distributions[k].P50 + "/" + summary.Distributions[k].P95)) +
                "\nRuleId " + _preview.RuleId + "\n" + L("Filters applied immediately; save explicitly to create a layer.", "Фильтры применены сразу; для создания слоя явно сохраните правило.");
            foreach (KeyValuePair<string, CalibrationPlot> plot in _plots) { plot.Value.Distribution(CalibrationText.Label(plot.Key), summary.Distributions[plot.Key]); }
            TextBlockRuleSummary.Text += "\n" + L("Stack ≥ length / count ", "Стек ≥ длина / число ") + string.Join(" · ", summary.StackFrequencies.OrderBy(p => p.Key).Select(p => p.Key + "/" + p.Value));
            RenderTime(map);
            TextBlockStatus.Text = L("Post-filter applied from saved evidence; no raw text or formation rebuild", "Post-фильтр применён по сохранённым данным; без raw text и нового формирования");
        }
        private void RenderTime(EventSummary summary)
        {
            string metric = (string)ComboBoxTimeMetric.SelectedItem;
            decimal Value(TimeBucket bucket) => metric == "Volume" ? bucket.Volume : metric == "abs(Delta)" ? bucket.AbsoluteDelta : metric == "abs(DiagonalDelta)" ? bucket.AbsoluteDiagonalDelta : bucket.Count;
            int size = (int)ComboBoxBucket.SelectedItem, columns = 1440 / size;
            string[] clock = Enumerable.Range(0, columns).Select(i => TimeSpan.FromMinutes(i * size).ToString(@"hh\:mm")).ToArray();
            DateTime[] dates = _run.Manifest.RangeDates.ToArray();
            List<PlotOverlay> overlays = new List<PlotOverlay>();
            for (int i = 0; i < dates.Length; i++)
            {
                foreach ((decimal from, decimal to) in _run.Spec.Range.SourceIntervals(dates[i]))
                { overlays.Add(new PlotOverlay(i, from / 1440, to / 1440, _run.Spec.Range.Name)); }
                if (!string.IsNullOrWhiteSpace(_run.Spec.Range.SourceTimeZone) && _run.Spec.Range.ClockMode != "USPreOpen")
                {
                    TimeRangeProfile us = TimeRangeProfile.Presets()[8] with { SourceTimeZone = _run.Spec.Range.SourceTimeZone };
                    foreach ((decimal from, decimal to) in us.SourceIntervals(dates[i])) { overlays.Add(new PlotOverlay(i, from / 1440, to / 1440, "US pre-open 60m · " + dates[i].ToString("yyyy-MM-dd"))); }
                }
            }
            _timeMap.Overlays = overlays;
            _timeHistogram.Overlays = overlays.Select(o => o with { Row = -1 }).Distinct().ToArray();
            _timeHistogram.Caption = L("Events by source time · ", "События по времени файла · ") + CalibrationText.Label(metric);
            _timeHistogram.Set(summary.TimeMap.GroupBy(b => b.Minute).Select(g => new PlotDatum(g.Key / size, 0, g.Sum(Value), clock[g.Key / size] + " · " + g.Sum(Value))).ToArray(), clock);
            _timeMap.Caption = L("Day × Time · ", "День × Время · ") + CalibrationText.Label(metric);
            _timeMap.Set(summary.TimeMap.Select(b => new PlotDatum(b.Minute / size, Array.IndexOf(dates, b.Date), Value(b), b.Date.ToString("yyyy-MM-dd") + " " + clock[b.Minute / size] + " · " + Value(b))).ToArray(),
                clock, dates.Select(d => d.ToString("yyyy-MM-dd")).ToArray());
            TextBlockTimeOverlay.Text = _run.Spec.Range.Name + " · [" + _run.Spec.Range.StartTime + ", " + _run.Spec.Range.EndTime + ") · " +
                (_run.Spec.Range.SourceTimeZone ?? L("Unlabelled source clock; US overlay disabled", "Timezone не задана; US overlay отключён"));
        }

        #endregion

        #region Explicit rule save and independent copy

        private void SaveRuleClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_preview == null || _job != null) { return; }
                CloudRule rule = _preview with { Name = TextBoxRuleName.Text.Trim(), Enabled = CheckBoxEnabled.IsChecked == true, Visible = CheckBoxVisible.IsChecked == true };
                CloudRule old = _editingRule; string root = _outputRoot;
                StartJob("Save rule", token =>
                {
                    CalibrationStorage.SaveRule(root, rule, token, old?.RuleId);
                    return CalibrationStorage.LoadRules(root, token);
                }, result => { _rules = (ImmutableArray<CloudRule>)result; _editingRule = rule; _preview = rule; _windows.CloseAll(); LoadLayers(); });
            }
            catch (Exception error) { Report(error); }
        }
        private void CopyRuleClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_preview == null || ComboBoxCopyRange.SelectedItem is not TimeRangeProfile range) { throw new InvalidOperationException(L("Choose a rule and destination profile", "Выберите правило и профиль назначения")); }
                CloudRule source = _preview; ExplorerRunSpec input = _input();
                CalibrationSpec spec = _run.Spec with { InputPath = input.InputPath, OutputRootPath = _outputRoot, Range = range with { SourceTimeZone = range.SourceTimeZone ?? source.Provenance.Range.SourceTimeZone },
                    Gaps = ImmutableArray.Create(source.Formation.MaximumGapMilliseconds), Ranges = ImmutableArray.Create(source.Formation.MaximumRangeTicks) };
                spec.Validate();
                StartJob("Copy formation", token => CalibrationEngine.Run(spec, token, Progress), result =>
                {
                    AcceptRun((CalibrationRun)result, false);
                    _cell = _run.Manifest.Cells.Single(c => c.Formation == source.Formation); _chainCell = _cell.Formation.Mode == FormationMode.Chain ? _cell : null;
                    _editingRule = null; RestoreRule(source with { Provenance = _run.Spec, BundlePath = _run.Directory, Name = source.Name + " · " + range.Name });
                    TabControlWorkspace.SelectedItem = TabItemTuner; QueueFilter();
                });
            }
            catch (Exception error) { Report(error); }
        }
        private void RestoreRule(CloudRule rule)
        {
            _restoring = true; TextBoxRuleName.Text = rule.Name; ComboBoxFormation.SelectedItem = rule.Formation.Mode; ComboBoxRuleKind.SelectedItem = rule.Kind;
            CheckBoxEnabled.IsChecked = rule.Enabled; CheckBoxVisible.IsChecked = rule.Visible;
            foreach (KeyValuePair<string, (TextBox Minimum, TextBox Maximum)> editor in _filters)
            {
                NumericFilter filter = (NumericFilter)typeof(CloudFilters).GetProperty(editor.Key).GetValue(rule.Filters);
                editor.Value.Minimum.Text = filter.Minimum?.ToString("G29", CultureInfo.InvariantCulture) ?? "";
                editor.Value.Maximum.Text = filter.Maximum?.ToString("G29", CultureInfo.InvariantCulture) ?? "";
            }
            ComboBoxDeltaDirection.SelectedItem = rule.Filters.DeltaDirection; DiagonalSettings d = rule.Filters.Diagonal;
            CheckBoxDiagonalFilter.IsChecked = d.Enabled; ComboBoxDiagonalSource.SelectedItem = d.Source; ComboBoxDiagonalDirection.SelectedItem = d.Direction;
            TextBoxRatio.Text = d.RatioThreshold.ToString(CultureInfo.InvariantCulture); TextBoxDominant.Text = d.MinimumDominantVolume.ToString(CultureInfo.InvariantCulture);
            TextBoxDifference.Text = d.MinimumDifference.ToString(CultureInfo.InvariantCulture); TextBoxStack.Text = d.MinimumStackLength.ToString(); _restoring = false;
        }

        #endregion
    }
}
