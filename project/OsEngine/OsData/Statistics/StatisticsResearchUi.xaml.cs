using OsEngine.Language;
using OsEngine.Logging;
using OsEngine.Market;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows;
using System.Windows.Forms.DataVisualization.Charting;
using Forms = System.Windows.Forms;

namespace OsEngine.OsData.Statistics
{
    public partial class StatisticsResearchUi
    {
        private PairResearchResult _result;
        private readonly string _pairName;
        private readonly string _timeFrame;
        private readonly int _smaPeriod;
        private Chart _chart;
        private StatisticsSortableGrid _summaryGrid;
        private StatisticsSortableGrid _episodeSummaryGrid;
        private StatisticsSortableGrid _episodesGrid;
        private StatisticsSortableGrid _samplesGrid;
        private readonly List<object[]> _summaryRows = new List<object[]>();
        private readonly List<object[]> _episodeRows = new List<object[]>();
        private double _viewStart;
        private double _viewEnd;
        private bool _closed;
        private bool _rendering;

        public StatisticsResearchUi(PairResearchResult result, string pairName, string timeFrame, int smaPeriod, bool showStatistics = false)
        {
            InitializeComponent();
            _result = result ?? throw new ArgumentNullException(nameof(result));
            _pairName = pairName;
            _timeFrame = timeFrame;
            _smaPeriod = smaPeriod;
            Layout.StickyBorders.Listen(this);
            Layout.StartupLocation.Start_FitHeightToWorkArea(this);
            _summaryGrid = new StatisticsSortableGrid(HostSummary, SummaryHeaders(), SummaryCell);
            _episodeSummaryGrid = new StatisticsSortableGrid(HostEpisodeSummary, EpisodeSummaryHeaders(), EpisodeSummaryCell);
            _episodesGrid = new StatisticsSortableGrid(HostEpisodes, EpisodeHeaders(), EpisodeCell);
            _samplesGrid = new StatisticsSortableGrid(HostSamples, SampleHeaders(), SampleCell);
            _summaryGrid.Grid.Columns[0].Width = 520;
            CreateChart();
            Localize();
            BuildTables();
            ResetView();
            ApplyTheme();
            TabControlResults.SelectedIndex = showStatistics ? 1 : 0;
            ButtonPrevious.Click += Navigation_Click;
            ButtonNext.Click += Navigation_Click;
            ButtonZoomIn.Click += Navigation_Click;
            ButtonZoomOut.Click += Navigation_Click;
            ButtonReset.Click += Navigation_Click;
            OsLocalization.LocalizationTypeChangeEvent += Localization_Changed;
            Themes.ThemeManager.ThemeChangedEvent += Theme_Changed;
            Closed += ResearchUi_Closed;
        }

        #region Tables and definitions

        private static string L(string english, string russian)
        {
            return OsLocalization.ConvertToLocString("Eng:" + english + "_Ru:" + russian + "_");
        }

        private void Localize()
        {
            Title = L("Pair research", "Исследование пары") + " — " + _pairName;
            TextBlockDescription.Text = _pairName + " | " + _timeFrame + " | SMA " + _smaPeriod + " | " +
                L("Accepted synchronized observations", "Принятых синхронных наблюдений") + ": " + _result.Samples.Count;
            if (_result.Samples.Count == 0)
                TextBlockDescription.Text += " | " + L("No observations passed the filters.", "Нет наблюдений, прошедших фильтры.");
            else if (_result.Deviations == null || _result.Deviations.Count == 0)
                TextBlockDescription.Text += " | " + L("No segment is long enough for SMA warmup. Reduce the period or review the gaps.", "Ни один участок не накопил данных для SMA. Уменьшите период или проверьте разрывы.");
            TabItemChart.Header = L("Chart", "График");
            TabItemStatistics.Header = L("Statistics", "Статистика");
            TabItemEpisodes.Header = L("All episodes", "Все эпизоды");
            TabItemSamples.Header = L("All observations", "Все наблюдения");
            ButtonReset.Content = L("Entire history", "Вся история");
            TextBlockChartHelp.Text = L("Wheel — zoom at cursor. Arrows — pan. A silver, B orange; basis blue, SMA gold.", "Колесо — масштаб около курсора. Стрелки — прокрутка. A серый, B оранжевый; базис синий, SMA золотой.");
            TextBlockEpisodesDefinition.Text = L("Each threshold and direction has independent episodes. Return means deviation crosses zero relative to the moving SMA. Duration statistics include returned episodes only; gaps and history end censor episodes. Rates are descriptive, not return probabilities.", "Для каждого порога и направления эпизоды считаются отдельно. Возврат — отклонение пересекло ноль относительно движущейся SMA. Время возврата рассчитано только для завершённых эпизодов; разрыв и конец истории прерывают наблюдение. Доли описательные, не вероятности возврата.");
            TextBlockFooter.Text = L("Basis = Close A − Close B, equal quantities. SMA includes current observation; first N−1 observations of every segment are warmup. Gaps break lines and reset SMA. σ for Z is population standard deviation of basis in the SMA window. Chart uses uniformly sampled observations and may omit extrema; zoom in for detail. Cursor and statistics use original data. No execution simulation or PnL.", "Базис = Close A − Close B, равные количества. SMA включает текущее наблюдение; первые N−1 наблюдений каждого участка — прогрев. Разрывы прерывают линии и сбрасывают SMA. σ для Z — стандартное отклонение базиса в окне SMA (делитель N). График прорежен равномерно и может пропускать экстремумы; увеличьте масштаб для подробностей. Курсор и статистика используют исходные данные. Исполнение и PnL не моделируются.");
        }

        private string[] SummaryHeaders() { return new string[] { L("Metric", "Показатель"), L("Value", "Значение") }; }
        private string[] EpisodeSummaryHeaders()
        {
            return new string[] { "kσ", L("Direction", "Направление"), L("Episodes", "Эпизодов"), L("Returned", "Вернулось"), L("Gap", "Разрыв"), L("End", "Конец истории"), L("Returned %", "Вернулось %"), L("Mean min", "Среднее мин"), L("Median min", "Медиана мин"), L("P90 min", "P90 мин"), L("Max min", "Макс мин"), L("Max further deviation", "Макс дальнейшее отклонение") };
        }
        private string[] EpisodeHeaders()
        {
            return new string[] { L("Start", "Начало"), L("End", "Конец"), L("Direction", "Направление"), "kσ", L("Status", "Статус"), L("Minutes", "Минут"), L("Entry deviation", "Отклонение при входе"), L("Entry threshold", "Порог при входе"), L("Max adverse deviation", "Макс отклонение от SMA"), L("Further deviation", "Дальнейшее отклонение"), L("Observed bars", "Наблюдений") };
        }
        private object SummaryCell(int row, int column) { return _summaryRows[row][column]; }
        private object EpisodeSummaryCell(int row, int column) { return _episodeRows[row][column]; }
        private string[] SampleHeaders()
        {
            return new string[] { L("Time", "Время"), "Close A", "Close B", "A − B", "SMA", "D", "σ", "Z", L("Volume A", "Объём A"), L("Volume B", "Объём B"), L("Segment", "Участок") };
        }
        private object SampleCell(int row, int column)
        {
            PairResearchSample sample = _result.Samples[row];
            switch (column)
            {
                case 0: return sample.Time;
                case 1: return sample.PriceA;
                case 2: return sample.PriceB;
                case 3: return sample.Basis;
                case 4: return sample.Sma;
                case 5: return sample.Deviation;
                case 6: return sample.Sigma;
                case 7: return sample.ZScore;
                case 8: return sample.VolumeA;
                case 9: return sample.VolumeB;
                default: return sample.Segment;
            }
        }
        private object EpisodeCell(int row, int column)
        {
            PairResearchEpisode episode = _result.Episodes[row];
            switch (column)
            {
                case 0: return episode.Start;
                case 1: return episode.End;
                case 2: return Direction(episode.Direction);
                case 3: return episode.SigmaMultiple;
                case 4: return episode.Status == "Returned" ? L("Returned", "Возврат") : episode.Status == "Gap" ? L("Gap", "Разрыв") : L("History end", "Конец истории");
                case 5: return episode.DurationMinutes;
                case 6: return episode.EntryDeviation;
                case 7: return episode.EntryThreshold;
                case 8: return episode.MaximumAdverseDeviation;
                case 9: return episode.FurtherAdverseDeviation;
                default: return episode.ObservedBars;
            }
        }
        private static string Direction(int direction) { return direction > 0 ? L("Above SMA", "Выше SMA") : L("Below SMA", "Ниже SMA"); }

        private void BuildTables()
        {
            _summaryRows.Clear();
            PairDeviationSummary summary = _result.Deviations ?? new PairDeviationSummary();
            _summaryRows.Add(new object[] { L("Observations after warmup", "Наблюдений после прогрева"), summary.Count });
            _summaryRows.Add(new object[] { L("Mean absolute deviation", "Среднее абсолютное отклонение"), summary.Count == 0 ? null : (object)summary.MeanAbsolute });
            _summaryRows.Add(new object[] { L("Population stddev of signed deviations", "Стандартное отклонение отклонений от SMA (N)"), summary.Count == 0 ? null : (object)summary.StandardDeviation });
            _summaryRows.Add(new object[] { "P50 |D|", summary.Count == 0 ? null : (object)summary.P50 });
            _summaryRows.Add(new object[] { "P75 |D|", summary.Count == 0 ? null : (object)summary.P75 });
            _summaryRows.Add(new object[] { "P90 |D|", summary.Count == 0 ? null : (object)summary.P90 });
            _summaryRows.Add(new object[] { "P95 |D|", summary.Count == 0 ? null : (object)summary.P95 });
            _summaryRows.Add(new object[] { "P99 |D|", summary.Count == 0 ? null : (object)summary.P99 });
            _summaryRows.Add(new object[] { L("Maximum above SMA", "Максимум выше SMA"), summary.Count == 0 ? null : (object)summary.MaximumUp });
            _summaryRows.Add(new object[] { L("Maximum below SMA", "Максимум ниже SMA"), summary.Count == 0 ? null : (object)summary.MaximumDown });
            _episodeRows.Clear();
            foreach (PairEpisodeSummary group in _result.EpisodeSummaries)
            {
                _episodeRows.Add(new object[] { group.SigmaMultiple, Direction(group.Direction), group.Count, group.Returned,
                    group.Gap, group.HistoryEnd, group.Count == 0 ? null : (object)(100.0 * group.Returned / group.Count),
                    group.MeanMinutes, group.MedianMinutes, group.P90Minutes, group.MaximumMinutes, group.MaximumFurtherDeviation });
            }
            _summaryGrid.SetCount(_summaryRows.Count);
            _episodeSummaryGrid.SetCount(_episodeRows.Count);
            _episodesGrid.SetCount(_result.Episodes.Count);
            _samplesGrid.SetCount(_result.Samples.Count);
        }

        #endregion

        #region Synchronized chart

        private void CreateChart()
        {
            _chart = new Chart { Dock = Forms.DockStyle.Fill };
            string[] names = { "Prices", "Basis", "VolumeA", "VolumeB" };
            float[] tops = { 0, 34, 68, 84 };
            float[] heights = { 34, 34, 16, 16 };
            for (int index = 0; index < names.Length; index++)
            {
                ChartArea area = new ChartArea(names[index]);
                area.Position = new ElementPosition(0, tops[index], 100, heights[index]);
                area.InnerPlotPosition = new ElementPosition(9, 6, 87, index < 2 ? 84 : 70);
                area.AxisX.LabelStyle.Format = "MM-dd HH:mm";
                area.AxisX.LabelStyle.Enabled = index == 3;
                area.AxisX.MajorGrid.Enabled = true;
                area.AxisY.LabelStyle.Format = "0.##";
                area.AxisY.IsStartedFromZero = index >= 2;
                area.CursorX.LineDashStyle = ChartDashStyle.Dash;
                if (index > 0)
                {
                    area.AlignWithChartArea = "Prices";
                    area.AlignmentOrientation = AreaAlignmentOrientations.Vertical;
                    area.AlignmentStyle = AreaAlignmentStyles.PlotPosition;
                }
                _chart.ChartAreas.Add(area);
            }
            AddSeries("A", "Prices", Color.Silver, SeriesChartType.Line);
            AddSeries("B", "Prices", Color.OrangeRed, SeriesChartType.Line);
            AddSeries("A − B", "Basis", Color.DodgerBlue, SeriesChartType.Line);
            AddSeries("SMA", "Basis", Color.Goldenrod, SeriesChartType.Line);
            AddSeries("Volume A", "VolumeA", Color.Silver, SeriesChartType.Column);
            AddSeries("Volume B", "VolumeB", Color.OrangeRed, SeriesChartType.Column);
            HostChart.Child = _chart;
            _chart.MouseMove += Chart_MouseMove;
            _chart.MouseWheel += Chart_MouseWheel;
            _chart.Resize += Chart_Resize;
        }

        private void AddSeries(string name, string area, Color color, SeriesChartType type)
        {
            Series series = new Series(name) { ChartArea = area, ChartType = type, XValueType = ChartValueType.DateTime, Color = color, BorderWidth = 2, IsVisibleInLegend = false, MarkerStyle = type == SeriesChartType.Line ? MarkerStyle.Circle : MarkerStyle.None, MarkerSize = 2 };
            series.EmptyPointStyle.Color = Color.Transparent;
            series.EmptyPointStyle.BorderWidth = 0;
            _chart.Series.Add(series);
        }

        private void ResetView()
        {
            if (_result.Samples.Count == 0) return;
            _viewStart = _result.Samples[0].Time.ToOADate();
            _viewEnd = _result.Samples[_result.Samples.Count - 1].Time.ToOADate();
            if (_viewEnd <= _viewStart) _viewEnd = _viewStart + 1.0 / 1440;
            RenderChart();
        }

        private int LowerBound(double time)
        {
            int start = 0;
            int end = _result.Samples.Count;
            while (start < end)
            {
                int middle = start + (end - start) / 2;
                if (_result.Samples[middle].Time.ToOADate() < time) start = middle + 1;
                else end = middle;
            }
            return start;
        }

        private void RenderChart()
        {
            if (_closed || _rendering || _result.Samples.Count == 0) return;
            _rendering = true;
            try
            {
                foreach (Series series in _chart.Series) series.Points.Clear();
                int first = Math.Max(0, LowerBound(_viewStart) - 1);
                int last = Math.Min(_result.Samples.Count - 1, LowerBound(_viewEnd));
                int budget = Math.Max(200, _chart.Width);
                int stride = Math.Max(1, (last - first + 1 + budget - 1) / budget);
                // Bounded uniform sampling keeps interaction responsive on multi-million-row datasets.
                // Segment identity prevents joining samples across any omitted gap.
                int previousIndex = -1;
                for (int sampleIndex = first; sampleIndex <= last; sampleIndex = Math.Min(last, sampleIndex + stride))
                {
                    PairResearchSample sample = _result.Samples[sampleIndex];
                    double time = sample.Time.ToOADate();
                    if (previousIndex >= 0 && sample.Segment != _result.Samples[previousIndex].Segment)
                        AddEmptyPoint((_result.Samples[previousIndex].Time.ToOADate() + time) / 2);
                    for (int field = 0; field < 6; field++)
                    {
                        decimal? value = Value(sample, field);
                        DataPoint point = new DataPoint(time, value.HasValue ? (double)value.Value : 0) { IsEmpty = !value.HasValue };
                        _chart.Series[field].Points.Add(point);
                    }
                    previousIndex = sampleIndex;
                    if (sampleIndex == last) break;
                }
                foreach (ChartArea area in _chart.ChartAreas)
                {
                    area.AxisX.Minimum = _viewStart;
                    area.AxisX.Maximum = _viewEnd;
                    area.RecalculateAxesScale();
                }
                _chart.Invalidate();
            }
            finally { _rendering = false; }
        }

        private static decimal? Value(PairResearchSample sample, int field)
        {
            switch (field)
            {
                case 0: return sample.PriceA;
                case 1: return sample.PriceB;
                case 2: return sample.Basis;
                case 3: return sample.Sma;
                case 4: return sample.VolumeA;
                default: return sample.VolumeB;
            }
        }

        private void AddEmptyPoint(double time)
        {
            foreach (Series series in _chart.Series) series.Points.Add(new DataPoint(time, 0) { IsEmpty = true });
        }

        private void SetView(double start, double end)
        {
            if (_result.Samples.Count == 0) return;
            double minimum = _result.Samples[0].Time.ToOADate();
            double maximum = _result.Samples[_result.Samples.Count - 1].Time.ToOADate();
            double fullWidth = Math.Max(1.0 / 1440, maximum - minimum);
            double width = Math.Min(fullWidth, Math.Max(1.0 / 86400, end - start));
            _viewStart = Math.Max(minimum, Math.Min(start, minimum + fullWidth - width));
            _viewEnd = _viewStart + width;
            RenderChart();
        }

        private void Navigation_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                double width = _viewEnd - _viewStart;
                if (sender == ButtonReset) ResetView();
                else if (sender == ButtonPrevious) SetView(_viewStart - width * 0.25, _viewEnd - width * 0.25);
                else if (sender == ButtonNext) SetView(_viewStart + width * 0.25, _viewEnd + width * 0.25);
                else if (sender == ButtonZoomIn) SetView(_viewStart + width * 0.25, _viewEnd - width * 0.25);
                else SetView(_viewStart - width * 0.5, _viewEnd + width * 0.5);
            }
            catch (Exception error) { Log(error); }
        }

        private void Chart_MouseWheel(object sender, Forms.MouseEventArgs e)
        {
            try
            {
                if (_closed || _result.Samples.Count == 0) return;
                double time = _chart.ChartAreas[0].AxisX.PixelPositionToValue(e.X);
                double factor = e.Delta > 0 ? 0.5 : 2;
                SetView(time - (time - _viewStart) * factor, time + (_viewEnd - time) * factor);
            }
            catch (Exception error) { Log(error); }
        }

        private void Chart_MouseMove(object sender, Forms.MouseEventArgs e)
        {
            try
            {
                if (_closed || _rendering || _result.Samples.Count == 0) return;
                double time = _chart.ChartAreas[0].AxisX.PixelPositionToValue(e.X);
                if (time < _viewStart || time > _viewEnd) return;
                int index = Math.Min(_result.Samples.Count - 1, LowerBound(time));
                if (index > 0 && time - _result.Samples[index - 1].Time.ToOADate() < _result.Samples[index].Time.ToOADate() - time) index--;
                PairResearchSample sample = _result.Samples[index];
                foreach (ChartArea area in _chart.ChartAreas) area.CursorX.Position = sample.Time.ToOADate();
                TextBlockCursor.Text = sample.Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture) +
                    " | A: " + sample.PriceA + " | B: " + sample.PriceB + " | A − B: " + sample.Basis +
                    " | SMA: " + (sample.Sma.HasValue ? sample.Sma.Value.ToString("0.#####") : L("warmup", "прогрев")) +
                    " | D: " + (sample.Deviation.HasValue ? sample.Deviation.Value.ToString("0.#####") : "—") +
                    " | Z: " + (sample.ZScore.HasValue ? sample.ZScore.Value.ToString("0.###") : "—") +
                    " | " + L("Volume A", "Объём A") + ": " + sample.VolumeA + " | " + L("Volume B", "Объём B") + ": " + sample.VolumeB;
            }
            catch (Exception error) { Log(error); }
        }

        private void Chart_Resize(object sender, EventArgs e)
        {
            try { RenderChart(); }
            catch (Exception error) { Log(error); }
        }

        #endregion

        #region Theme and lifecycle

        private Color ResourceColor(string name, Color fallback)
        {
            object resource = TryFindResource(name);
            if (resource is System.Windows.Media.Color color) return Color.FromArgb(color.A, color.R, color.G, color.B);
            System.Windows.Media.SolidColorBrush brush = resource as System.Windows.Media.SolidColorBrush;
            return brush == null ? fallback : Color.FromArgb(brush.Color.A, brush.Color.R, brush.Color.G, brush.Color.B);
        }

        private void ApplyTheme()
        {
            Color background = ResourceColor("ChartBackColor", Color.FromArgb(21, 25, 28));
            Color foreground = ResourceColor("ControlForegroundWhite", Color.Gainsboro);
            Color grid = ResourceColor("TextSecondaryBrush", Color.DimGray);
            _chart.BackColor = background;
            foreach (ChartArea area in _chart.ChartAreas)
            {
                area.BackColor = background;
                area.CursorX.LineColor = foreground;
                foreach (Axis axis in new Axis[] { area.AxisX, area.AxisY })
                {
                    axis.LineColor = grid;
                    axis.LabelStyle.ForeColor = foreground;
                    axis.MajorGrid.LineColor = Color.FromArgb(45, grid);
                    axis.MajorTickMark.LineColor = grid;
                    axis.TitleForeColor = foreground;
                }
            }
            _chart.ChartAreas[0].AxisY.Title = "A / B";
            _chart.ChartAreas[1].AxisY.Title = "A − B / SMA";
            _chart.ChartAreas[2].AxisY.Title = L("Volume A", "Объём A");
            _chart.ChartAreas[3].AxisY.Title = L("Volume B", "Объём B");
            _summaryGrid.ApplyTheme();
            _episodeSummaryGrid.ApplyTheme();
            _episodesGrid.ApplyTheme();
            _samplesGrid.ApplyTheme();
        }

        private void Theme_Changed()
        {
            try
            {
                if (_closed) return;
                if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(new Action(Theme_Changed)); return; }
                ApplyTheme();
            }
            catch (Exception error) { Log(error); }
        }

        private void Localization_Changed()
        {
            try
            {
                if (_closed) return;
                if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(new Action(Localization_Changed)); return; }
                Localize();
                _summaryGrid.RefreshHeaders(SummaryHeaders());
                _episodeSummaryGrid.RefreshHeaders(EpisodeSummaryHeaders());
                _episodesGrid.RefreshHeaders(EpisodeHeaders());
                _samplesGrid.RefreshHeaders(SampleHeaders());
                BuildTables();
                ApplyTheme();
            }
            catch (Exception error) { Log(error); }
        }

        private void ResearchUi_Closed(object sender, EventArgs e)
        {
            try
            {
                _closed = true;
                Closed -= ResearchUi_Closed;
                OsLocalization.LocalizationTypeChangeEvent -= Localization_Changed;
                Themes.ThemeManager.ThemeChangedEvent -= Theme_Changed;
                ButtonPrevious.Click -= Navigation_Click;
                ButtonNext.Click -= Navigation_Click;
                ButtonZoomIn.Click -= Navigation_Click;
                ButtonZoomOut.Click -= Navigation_Click;
                ButtonReset.Click -= Navigation_Click;
                _chart.MouseMove -= Chart_MouseMove;
                _chart.MouseWheel -= Chart_MouseWheel;
                _chart.Resize -= Chart_Resize;
                HostChart.Child = null;
                _chart.Dispose();
                _chart = null;
                HostChart.Dispose();
                _summaryGrid.Dispose();
                _episodeSummaryGrid.Dispose();
                _episodesGrid.Dispose();
                _samplesGrid.Dispose();
                HostSummary.Dispose();
                HostEpisodeSummary.Dispose();
                HostEpisodes.Dispose();
                HostSamples.Dispose();
                _summaryGrid = null;
                _episodeSummaryGrid = null;
                _episodesGrid = null;
                _samplesGrid = null;
                _summaryRows.Clear();
                _episodeRows.Clear();
                _result = null;
            }
            catch (Exception error) { Log(error); }
        }

        private static void Log(Exception error) { ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error); }

        #endregion
    }
}
