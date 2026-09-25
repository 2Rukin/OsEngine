/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.Entity;
using OsEngine.OsData.OrderFlow.Explorer;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace OsEngine.OsData.OrderFlow.Calibration
{
    public partial class CloudCalibrationWindow
    {
        private sealed record TickRow(long SourceSequence, DateTime Time, decimal Price, decimal Volume, string Side, string TimeRangeId);
        private sealed record RuleRow(string RuleId, string Name, string TimeRangeId, string Profile, bool Enabled, bool Visible,
            FormationMode FormationMode, RuleKind RuleKind, decimal MinimumTickVolume, int GapMilliseconds, int RangeTicks, string Direction, string InputSha256);

        #region Read-only detached tables

        private void OpenTable(string key, CalibrationTableSource source, Action<object> select = null, string context = null)
        { _windows.Open(key, () => new CalibrationTableWindow(source, context ?? _run?.Spec.Hash ?? _outputRoot, select)); }
        private void TicksClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_run == null) { return; } CalibrationRun run = _run; decimal threshold = Number(TextBoxTickVolume.Text);
                bool eligible = CheckBoxQualifying.IsChecked == true; int side = ComboBoxSide.SelectedIndex;
                OpenTable("ticks-" + run.Spec.Hash + "-" + eligible + "-" + threshold + "-" + side,
                    CalibrationTableSource.Create(L("Range ticks", "Тики диапазона"), token => CalibrationCache.Read(run.Directory, token)
                        .Where(t => run.Spec.Range.Includes(t.Time) && (!eligible || t.Volume >= threshold) && (side == 0 || side == 1 && t.Side == Side.Buy || side == 2 && t.Side == Side.Sell))
                        .Select(t => new TickRow(t.SourceSequence, t.Time, t.Price, t.Volume, t.Side.ToString(), run.Spec.Range.Id))));
            }
            catch (Exception error) { Report(error); }
        }
        private void ChainsClick(object sender, RoutedEventArgs e) { try { OpenEvents(false, false); } catch (Exception error) { Report(error); } }
        private void DiagonalClick(object sender, RoutedEventArgs e) { try { OpenEvents(false, true); } catch (Exception error) { Report(error); } }
        private void PassedClick(object sender, RoutedEventArgs e) { try { OpenEvents(true, false); } catch (Exception error) { Report(error); } }
        private void OpenEvents(bool passed, bool diagonal)
        {
            if (_run == null || _cell == null) { throw new InvalidOperationException(L("Select a candidate first", "Сначала выберите вариант")); }
            CalibrationRun run = _run;
            CloudRule rule = _preview ?? new CloudRule { BundlePath = run.Directory, Provenance = run.Spec, Formation = _cell.Formation };
            if (rule.Formation != _cell.Formation) { rule = rule with { Formation = _cell.Formation }; }
            string title = diagonal ? L("Diagonal events", "Диагональные события") : passed ? L("Passed Clouds", "Прошедшие Cloud") : L("Candidate Chains", "Цепочки варианта");
            CalibrationTableSource source = CalibrationTableSource.Create(title, token => CalibrationStorage.Events(run, rule.Formation, token)
                .Where(item => !passed || CalibrationFilter.Passes(item, run.Spec.PriceStep, rule.Kind, rule.Filters, out _))
                .Select(item => CalibrationEventRow.Create(item, run.Spec, rule)));
            OpenTable(title + rule.RuleId, source, row => SelectEvent(run, rule, ((CalibrationEventRow)row).EventId), run.Spec.Range.Name + " · " + rule.RuleId);
        }
        private void SelectEvent(CalibrationRun run, CloudRule rule, string eventId)
        {
            StartJob("Anatomy", token => new CalibrationMarker(run, rule,
                CalibrationStorage.Events(run, rule.Formation, token).First(c => c.EventId == eventId)), result =>
                { _selected = (CalibrationMarker)result; _chart.SelectCalibration(_selected); OpenAnatomy(_selected); });
        }
        private void CompareClick(object sender, RoutedEventArgs e)
        {
            try
            {
                PinnedCandidate[] pins = _pins.ToArray();
                List<string> columns = new List<string> { "TimeRangeId", "Profile", "FormationHash", "MinimumTickVolume", "Gap", "Range", "Events", "ActiveDays", "ChainsPerDay", "SinglesPerDay", "NeighborSensitivity" };
                foreach (string metric in EventStatistics.Metrics) { columns.Add(metric + "P50"); columns.Add(metric + "P95"); }
                CalibrationTableSource source = new CalibrationTableSource(L("Pinned candidates — descriptive comparison", "Закреплённые варианты — описательное сравнение"), columns,
                    token => pins.Select(pin =>
                    {
                        token.ThrowIfCancellationRequested(); Dictionary<string, object> values = new Dictionary<string, object> {
                            ["TimeRangeId"] = pin.TimeRangeId, ["Profile"] = pin.ProfileName, ["FormationHash"] = pin.FormationHash,
                            ["MinimumTickVolume"] = pin.Formation.MinimumTickVolume, ["Gap"] = pin.Formation.MaximumGapMilliseconds, ["Range"] = pin.Formation.MaximumRangeTicks,
                            ["Events"] = pin.Summary.Total, ["ActiveDays"] = pin.Summary.ActiveDays, ["ChainsPerDay"] = pin.Summary.ChainsPerActiveDay,
                            ["SinglesPerDay"] = pin.Summary.SinglesPerActiveDay, ["NeighborSensitivity"] = pin.NeighborSensitivity };
                        foreach (string metric in EventStatistics.Metrics) { values[metric + "P50"] = pin.Summary.Distributions[metric].P50; values[metric + "P95"] = pin.Summary.Distributions[metric].P95; }
                        return new CalibrationTableRow(values, pin);
                    }));
                OpenTable("compare-" + pins.Length, source, SelectPin, L("No ranking or winner; select a row to reopen its exact formation", "Без рейтинга и победителя; выберите строку для открытия точного формирования"));
            }
            catch (Exception error) { Report(error); }
        }
        private void SelectPin(object value)
        {
            PinnedCandidate pin = (PinnedCandidate)value;
            StartJob("Open pinned", token => CalibrationStorage.Open(pin.BundlePath, token), result =>
            {
                AcceptRun((CalibrationRun)result, false); ParameterCell cell = _run.Manifest.Cells.Single(c => c.FormationHash == pin.FormationHash);
                if (cell.Formation.Mode == FormationMode.Chain) { CellSelected(cell); }
                else { ComboBoxFormation.SelectedItem = FormationMode.Single; QueueFilter(); }
                TabControlWorkspace.SelectedItem = TabItemTuner;
            });
        }
        private void RulesClick(object sender, RoutedEventArgs e)
        {
            try
            {
                string root = _outputRoot;
                StartJob("Rules", token => CalibrationStorage.LoadRules(root, token), result =>
                {
                    _rules = (ImmutableArray<CloudRule>)result; ImmutableArray<CloudRule> rules = _rules;
                    CalibrationTableSource source = CalibrationTableSource.Create(L("Saved Cloud rules — select to edit", "Сохранённые Cloud rules — выбрать для редактирования"), token => rules.Select(r =>
                        new RuleRow(r.RuleId, r.Name, r.TimeRangeId, r.Provenance.Range.Name, r.Enabled, r.Visible, r.Formation.Mode, r.Kind,
                            r.Formation.MinimumTickVolume, r.Formation.MaximumGapMilliseconds, r.Formation.MaximumRangeTicks, r.Filters.DeltaDirection.ToString(), r.Provenance.InputSha256)));
                    OpenTable("rules-" + string.Join("/", rules.Select(r => r.RuleId + r.Enabled + r.Visible)), source,
                        row => SelectRule(rules.Single(r => r.RuleId == ((RuleRow)row).RuleId)), _outputRoot);
                });
            }
            catch (Exception error) { Report(error); }
        }
        private void SelectRule(CloudRule rule)
        {
            StartJob("Open rule", token => CalibrationStorage.Open(rule.BundlePath, token), result =>
            {
                AcceptRun((CalibrationRun)result, false); _cell = _run.Cell(rule.Formation);
                _chainCell = rule.Formation.Mode == FormationMode.Chain ? _cell : null; _editingRule = rule;
                RestoreRule(rule); TabControlWorkspace.SelectedItem = TabItemTuner; QueueFilter();
            });
        }

        #endregion

        #region Independent saved chart layers and anatomy

        private void RefreshLayersClick(object sender, RoutedEventArgs e) { try { LoadLayers(); } catch (Exception error) { Report(error); } }
        private void LoadLayers()
        {
            if (_run == null) { return; }
            CalibrationRun run = _run; string root = _outputRoot; OrderFlowDisplayTimeFrame frame = (OrderFlowDisplayTimeFrame)ComboBoxChartTimeFrame.SelectedItem;
            StartJob("Saved layers", token =>
            {
                ImmutableArray<CloudRule> rules = CalibrationStorage.LoadRules(root, token);
                return (rules, CalibrationPresentation.Load(run, rules, frame, token));
            }, result =>
            {
                (_rules, _chartData) = ((ImmutableArray<CloudRule>, CalibrationChartData))result;
                _chart.SetResult(_chartData.Prices); _chart.SetTimeFrame(frame); _chart.SetLayers(false, false, false); _chart.SetCalibrationLayers(_chartData.Layers);
                TextBlockChartStatus.Text = L("Saved layers; double-click a marker for exact anatomy. Main Order Flow replay uses completion sequence. ",
                    "Сохранённые слои; двойной щелчок по метке открывает точную anatomy. Replay основного Order Flow учитывает sequence завершения. ") +
                    string.Join(" · ", _chartData.Layers.Select(l => l.Rule.Name + " " + l.Markers.Length + "/" + l.PassedCount)) +
                    L(". Up to 2000 last markers per layer; full history in tables. Bars use bounded display aggregation.", ". До 2000 последних меток слоя; полная история в таблицах. Свечи используют ограниченную агрегацию отображения.");
                TextBlockStatus.Text = L("Saved layers loaded; visibility belongs to each saved rule", "Сохранённые слои загружены; видимость задаётся отдельно для каждого правила");
            });
        }
        private void ChartSelected(CalibrationMarker marker)
        { try { _selected = marker; OpenAnatomy(marker); } catch (Exception error) { Report(error); } }
        private void OpenAnatomy(CalibrationMarker marker)
        { _windows.Open("anatomy-" + marker.CloudId, () => new CloudAnatomyWindow(marker)); }
        private void AnatomyClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_selected == null) { throw new InvalidOperationException(L("Double-click a Cloud marker or select a Cloud row", "Щёлкните дважды по метке Cloud или выберите строку Cloud")); }
                OpenAnatomy(_selected);
            }
            catch (Exception error) { Report(error); }
        }
        private void MainChartClick(object sender, RoutedEventArgs e)
        { try { if (_chartData != null) { _showMain?.Invoke(_chartData, _selected); } } catch (Exception error) { Report(error); } }
        private void ChartFrameChanged(object sender, SelectionChangedEventArgs e)
        { try { if (!_restoring && _run != null) { LoadLayers(); } } catch (Exception error) { Report(error); } }
        private void ChartAllClick(object sender, RoutedEventArgs e) { try { _chart.Zoom(_chart.TotalBars); } catch (Exception error) { Report(error); } }
        private void ChartPlusClick(object sender, RoutedEventArgs e) { try { _chart.Zoom(Math.Max(1, _chart.VisibleCount / 2)); } catch (Exception error) { Report(error); } }
        private void ChartMinusClick(object sender, RoutedEventArgs e) { try { _chart.Zoom(_chart.VisibleCount * 2); } catch (Exception error) { Report(error); } }

        #endregion
    }
}
