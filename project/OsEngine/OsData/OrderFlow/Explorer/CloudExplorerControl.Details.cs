/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.Entity;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace OsEngine.OsData.OrderFlow.Explorer
{
    public partial class CloudExplorerControl
    {
        #region Saved profiles and reopen

        private void InstallRun(ExplorerRun run)
        {
            ClearObservationContext(); _pendingPatternExample = false;
            _run = run; _selectedAnchor = _anchor = null; _vwap = ImmutableArray<ExplorerVwapSample>.Empty;
            _tableOffsets.Clear(); _chartFrom = _chartTo = null; _chart.SetInterval(null, null);
            _chartContext = null; _pendingInterval = null; _patternRun = null; _chart.KnownBoundary = null;
            _patternCard = null; _patternExample = null; DataGridPatternCards.ItemsSource = null; DataGridPatternWeek.ItemsSource = null;
            _patternCards = Array.Empty<ExplorerPatternCard>();
            DataGridCatalog.SelectedItem = null; DataGridEpisodes.SelectedItem = null; _chart.ResetSelection();
        }
        private string OptionsIdentity() => string.Join("|", _profiles.Values.SelectMany(v => v).Concat(_episodeOptions).Concat(_studyOptions).Select(o => o.Value)) +
            string.Join("|", CheckBoxLayer1.IsChecked, CheckBoxLayer2.IsChecked, CheckBoxScales.IsChecked, TextBoxBufferLimit.Text, TextBoxMemoryLimit.Text, InputFingerprint?.Invoke());
        private void RestoreViews()
        {
            foreach (string key in _profiles.Keys) { _views[key] = ViewOptions(_view.ForProfile(key)); }
            if (ComboBoxProfile.SelectedItem is string profile) { DataGridView.ItemsSource = _views[profile]; }
        }
        private void Reopen(object sender, RoutedEventArgs e)
        {
            try
            {
                Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Explorer manifest|manifest.json", Title = "Выберите manifest каталога или исследования" };
                if (dialog.ShowDialog() != true) { return; }
                string directory = Path.GetDirectoryName(dialog.FileName); ExplorerRunSpec input = RequestProvider(); StopPlayback();
                StartJob(token =>
                {
                    ExplorerManifest manifest = JsonSerializer.Deserialize<ExplorerManifest>(File.ReadAllText(dialog.FileName), ExplorerStorage.Json);
                    bool study = manifest.Version == ExplorerRunSpec.StudyVersion;
                    if (!study && manifest.Version != ExplorerRunSpec.CatalogVersion) { throw new InvalidDataException("Выберите каталог или исследование поддерживаемой версии."); }
                    ExplorerStorage.Verify(directory, manifest.Version, manifest.Hash, token);
                    ExplorerRunSpec spec = JsonSerializer.Deserialize<ExplorerRunSpec>(File.ReadAllText(Path.Combine(directory, study ? "run-spec.json" : "catalog-spec.json")), ExplorerStorage.Json)
                        with { InputPath = input.InputPath, OutputRootPath = Path.GetDirectoryName(directory) };
                    spec.Validate();
                    if (manifest.Hash != (study ? spec.StudyHash : spec.CatalogSpecHash)) { throw new InvalidDataException("Saved run specification does not match its manifest."); }
                    string catalogPath = Path.Combine(spec.OutputRootPath, "cloud-catalog-" + spec.CatalogSpecHash);
                    ExplorerManifest catalog = ExplorerStorage.Verify(catalogPath, ExplorerRunSpec.CatalogVersion, spec.CatalogSpecHash, token);
                    string episode = spec.Episodes.Enabled ? Path.Combine(spec.OutputRootPath, "cloud-episodes-" + spec.EpisodeSpecHash) : null;
                    if (episode != null) { ExplorerStorage.Verify(episode, ExplorerRunSpec.EpisodeVersion, spec.EpisodeSpecHash, token); }
                    return new ExplorerRun(spec, catalogPath, episode, study ? directory : null, catalog);
                }, result =>
                {
                    InstallRun((ExplorerRun)result); _view = ExplorerStorage.LoadView(_run); RestoreViews();
                    _pageStart = 0; _appliedOptions = null; LoadPage(true);
                });
            }
            catch (Exception error) { Error(error); }
        }
        private static T[] AuxPage<T>(string directory, string name, long start)
        { return directory == null ? Array.Empty<T>() : ExplorerStorage.ReadRows<T>(directory, name, Math.Min(start, new FileInfo(Path.Combine(directory, name + ".idx")).Length / 8)).Take(250).ToArray(); }

        #endregion

        #region Chart choices and causal details

        private string _observationId;
        private IReadOnlyList<ExplorerVwapSample> _observationVwap;
        private ExplorerObservation _pendingObservation;
        private void ClearObservationContext() { _observationId = null; _observationVwap = null; _pendingObservation = null; }

        private void TimeFrameChanged(object sender, SelectionChangedEventArgs e)
        { try { if (ComboBoxTimeFrame.SelectedItem is OrderFlowDisplayTimeFrame frame) { _timeFrame = frame; if (_job == null && _playback == null) { LoadPage(false); } else { Paint(); } } } catch (Exception error) { Error(error); } }
        private void BeginPlayback()
        {
            if (_job != null) { throw new InvalidOperationException("Дождитесь завершения текущей операции."); }
            if (_playback != null) { return; }
            ClearObservationContext(); _pendingPatternExample = false;
            _playback = new ExplorerPlayback(_run, _anchor, _patternRun?.Plan);
            PatternReplayMode(true);
            _pendingInterval = null; TextBlockEpisodeState.Text = "Реплей: ожидание первого причинного кадра. Исторические итоги скрыты.";
            _chart.ResetSelection();
            foreach (DataGrid grid in new[] { DataGridCatalog, DataGridEpisodes, DataGridObservations, DataGridLabels, DataGridTriggers, DataGridPivots, DataGridDiagnostics }) { grid.ItemsSource = null; }
            TextBoxSummary.Text = "Ожидание первого причинного кадра."; TextBoxDetails.Clear();
            TextBoxComparison.Text = "Полное сравнение доступно только в режиме «История» после расчёта.";
            _chart.Set(Array.Empty<ExplorerCloud>(), _view, _run.Spec.PriceStep, null);
            _chart.SetContext(Array.Empty<ExplorerBar>(), Array.Empty<ExplorerEpisode>());
        }
        private ExplorerView FrameView(ExplorerFrame frame)
        {
            ExplorerView Resolve(ExplorerView view)
            {
                if (string.IsNullOrWhiteSpace(view.RelatedId)) { return view; }
                ExplorerEpisode episode = frame.Episodes.FirstOrDefault(e => e.Id == view.RelatedId);
                ExplorerObservation observation = frame.Observations.FirstOrDefault(o => o.Id == view.RelatedId);
                ImmutableArray<string> ids = episode?.ChildIds ?? ImmutableArray<string>.Empty;
                if (observation?.Trigger != null)
                {
                    ExplorerTrigger trigger = observation.Trigger;
                    episode = frame.Episodes.FirstOrDefault(e => e.Id == trigger.VolumeId);
                    ids = episode == null ? ImmutableArray.Create(trigger.VolumeId) : episode.ChildIds.Take(trigger.Children).ToImmutableArray();
                }
                return view with { RelatedCloudIds = ids.ToImmutableHashSet() };
            }
            return Resolve(_view) with { Layers = _view.Layers.ToImmutableDictionary(p => p.Key, p => Resolve(p.Value)) };
        }
        private void PriceStyleChanged(object sender, SelectionChangedEventArgs e)
        { try { if (_chart != null) { _chart.PriceStyle = ComboBoxPriceStyle.SelectedIndex; _chart.InvalidateVisual(); } } catch (Exception error) { Error(error); } }
        private void DrawChanged(object sender, RoutedEventArgs e) { try { _chart.Drawing = CheckBoxDraw.IsChecked == true; } catch (Exception error) { Error(error); } }
        private void ClearLines(object sender, RoutedEventArgs e) { try { _chart.ClearDrawings(); } catch (Exception error) { Error(error); } }
        private void ChartObservationSelected(ExplorerObservation observation)
        { try { DataGridObservations.SelectedItem = DataGridObservations.Items.Cast<ExplorerObservation>().FirstOrDefault(o => o.Id == observation.Id); ShowObservation(observation); } catch (Exception error) { Error(error); } }
        private void ShowObservation(ExplorerObservation observation)
        {
            string text = $"{observation.Direction} · {observation.Group} · {observation.Status}\nНаблюдение {observation.WatchStart:O} → событие {observation.Time:O}; ordinal {observation.Sequence}\n" +
                $"H0 {observation.H0?.Price}; H1 {observation.H1?.Price}; L0 {observation.L0?.Price}; L1 {observation.L1?.Price}; откат {observation.Rebound?.Price}; подтверждённый поворот {observation.Turn?.Price}\n" +
                $"VWAP на событии {observation.WatchVwap}; ATR {observation.Atr}; ориентир отмены {observation.DiagnosticStop}. Исполнение и риск не рассчитаны.";
            TextBoxDetails.Text = text; _chart.SelectObservation(observation);
            if (_run == null || _playback != null) { return; }
            _observationId = observation.Id; _observationVwap = Array.Empty<ExplorerVwapSample>(); _pendingInterval = null;
            if (_job != null) { _pendingObservation = observation; return; }
            if (observation.Trigger == null) { ShowInterval(observation.WatchStart, observation.Time.AddMinutes(2)); return; }
            ExplorerRun run = _run;
            StartJob(token =>
            {
                ExplorerView related = ExplorerStorage.ResolveRelations(run, new ExplorerView { RelatedId = observation.Id }, token);
                List<ExplorerCloud> selected = new List<ExplorerCloud>();
                foreach (ExplorerCloud cloud in ExplorerStorage.ReadRows<ExplorerCloud>(run.CatalogPath, "catalog"))
                { token.ThrowIfCancellationRequested(); if (related.RelatedCloudIds.Contains(cloud.Id)) { selected.Add(cloud); if (selected.Count == 250) { break; } } }
                ExplorerVwapSample[] samples = ExplorerStorage.ReadRows<ExplorerWatchVwap>(run.StudyPath, "watch-vwap-samples").Where(v => v.WatchId == observation.Id && v.Sequence <= observation.Sequence)
                    .Take(2048).Select(v => new ExplorerVwapSample(v.Time, v.Sequence, v.Vwap, v.Vwap, v.Sigma)).ToArray();
                DateTime from = observation.WatchStart, to = observation.Time.AddMinutes(2);
                return (selected, samples, ExplorerBars.ReadRange(run.CatalogPath, from, to, _timeFrame, token));
            }, result =>
            {
                if (_run != run || _observationId != observation.Id) { return; }
                (List<ExplorerCloud> selected, ExplorerVwapSample[] samples, IReadOnlyList<ExplorerBar> bars) = ((List<ExplorerCloud>, ExplorerVwapSample[], IReadOnlyList<ExplorerBar>))result;
                _observationVwap = samples;
                _chartFrom = observation.WatchStart; _chartTo = observation.Time.AddMinutes(2); _bars = bars;
                _chart.SetInterval(_chartFrom, _chartTo); _chart.SetContext(bars, Array.Empty<ExplorerEpisode>());
                if (selected.Count > 0) { _chart.Set(selected, _view, run.Spec.PriceStep, samples, new[] { observation.H0, observation.H1, observation.L0, observation.L1, observation.Rebound, observation.Turn }.Where(p => p != null).ToArray(), new[] { observation }); }
                _chart.SelectObservation(observation); TabControlResult.SelectedItem = TabItemChart;
                TextBlockStatus.Text = text;
                ShowInterval(observation.WatchStart, observation.Time.AddMinutes(2));
            });
        }
        private void LoadCloudDetails(ExplorerCloud cloud)
        {
            if (_run == null || _job != null || _playback != null) { return; }
            ExplorerRun run = _run;
            IReadOnlyList<ExplorerCloud> pageRows = _page?.Rows ?? Array.Empty<ExplorerCloud>();
            StartJob(token =>
            {
                List<string> lines = new List<string>(); ExplorerPrefix previous = null; int found = 0;
                foreach (ExplorerPrefix prefix in ExplorerStorage.Prefixes(run.CatalogPath, run.Spec))
                {
                    token.ThrowIfCancellationRequested(); if (prefix.Id != cloud.Id || prefix.Completed) { continue; }
                    if (++found > 100) { lines.Add("Далее — в prefix index; показаны первые 100 включённых сделок."); break; }
                    decimal volume = prefix.Buy + prefix.Sell - (previous == null ? 0 : previous.Buy + previous.Sell);
                    lines.Add($"#{prefix.Sequence} {prefix.Time:HH:mm:ss.fffffff} · цена {prefix.Price} · объём {volume} · пауза {(previous == null ? 0 : (prefix.Time - previous.Time).TotalMilliseconds)} мс · изменение {(previous == null ? 0 : prefix.Price - previous.Price)}"); previous = prefix;
                }
                ExplorerTrigger trigger = null;
                if (run.StudyPath != null)
                {
                    foreach (ExplorerTrigger item in ExplorerStorage.ReadRows<ExplorerTrigger>(run.StudyPath, "triggers"))
                    { token.ThrowIfCancellationRequested(); if (item.VolumeId == cloud.Id) { trigger = item; break; } }
                }
                string first = trigger == null ? "Триггер — отсутствует в выбранном study" : $"Первый триггер {trigger.Time:O}; ordinal {trigger.Sequence}; порог {trigger.Threshold}; накоплено {trigger.Volume}";
                string neighbors = string.Join("\n", pageRows.Where(c => c.Profile == cloud.Profile && c.Id != cloud.Id).OrderBy(c => Math.Abs(c.FirstSequence - cloud.FirstSequence)).Take(2).Select(c => "Сосед на странице " + c.Id + " · " + c.Volume));
                return first + $"\nrolling p95 {cloud.Effective.RollingThreshold}; time-of-day p95 {cloud.Effective.TimeOfDayThreshold}\n" + neighbors + "\n" + string.Join("\n", lines);
            }, result => { if (DataGridCatalog.SelectedItem is CatalogRow row && row.Id == cloud.Id) { TextBoxDetails.AppendText("\n" + (string)result); } });
        }

        #endregion
    }
}
