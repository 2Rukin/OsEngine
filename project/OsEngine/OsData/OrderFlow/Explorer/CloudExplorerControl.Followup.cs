/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace OsEngine.OsData.OrderFlow.Explorer
{
    public partial class CloudExplorerControl
    {
        private readonly Dictionary<string, long> _tableOffsets = new Dictionary<string, long>();
        private Dictionary<string, (long Unknown, long Eligible)> _profileQuality = new Dictionary<string, (long, long)>();
        private DateTime? _chartFrom, _chartTo;
        private ExplorerChartContext _chartContext;
        private (DateTime From, DateTime To)? _pendingInterval;
        private void RequireIdle() { if (_job != null) { throw new InvalidOperationException("Дождитесь завершения операции или нажмите «Отмена»."); } }
        private long Offset(string table) => _tableOffsets.TryGetValue(table, out long value) ? value : 0;
        private string SelectedTable() => TabControlResult.SelectedIndex switch
        { 2 => "episodes", 3 => "observations", 4 => "future-labels", 5 => "triggers", 6 => "pivots", 7 => "diagnostics", _ => null };
        private static long TableCount(string directory, string name) => directory == null ? 0 : new FileInfo(Path.Combine(directory, name + ".idx")).Length / 8;
        private void FocusInput(ExplorerInputException error)
        {
            if (error == null) { return; }
            if (new[] { "InputPath", "OutputRootPath", "PriceStep", "FromDate", "ToDate" }.Contains(error.Field)) { InputFocus?.Invoke(error.Field); return; }
            if (error.Field == "ReplaySpeed") { TabControlResult.SelectedItem = TabItemChart; TextBoxSpeed.Focus(); TextBoxSpeed.SelectAll(); return; }
            if (error.Profile != null && _profiles.ContainsKey(error.Profile)) { ComboBoxProfile.SelectedItem = error.Profile; }
            if (error.Field == "MaximumBufferItems") { TabControlOptions.SelectedIndex = 0; TextBoxBufferLimit.Focus(); TextBoxBufferLimit.SelectAll(); return; }
            if (error.Field == "MaximumMemoryMegabytes") { TabControlOptions.SelectedIndex = 0; TextBoxMemoryLimit.Focus(); TextBoxMemoryLimit.SelectAll(); return; }
            if (error.Field == "Profiles") { CheckBoxLayer1.Focus(); return; }
            bool view = error.Field.StartsWith("View.", StringComparison.Ordinal);
            string field = error.Field.Substring(error.Field.LastIndexOf('.') + 1);
            if (error.Field.StartsWith("Pattern.", StringComparison.Ordinal))
            {
                TabControlOptions.SelectedIndex = 5; ExplorerOption option = _patternOptions.FirstOrDefault(o => o.Property.Name == field);
                if (option != null) { OpenInputTable(DataGridPatternOptions); DataGridPatternOptions.SelectedItem = option; DataGridPatternOptions.ScrollIntoView(option); DataGridPatternOptions.Focus(); } return;
            }
            DataGrid[] grids = view ? new[] { DataGridView } : error.Field.StartsWith("Study.", StringComparison.Ordinal) ? new[] { DataGridStudy, DataGridModules, DataGridAdaptation } :
                error.Field.StartsWith("Episodes.", StringComparison.Ordinal) ? new[] { DataGridModules } : new[] { DataGridFormation, DataGridAdaptation, DataGridModules, DataGridStudy };
            foreach (DataGrid grid in grids)
            {
                ExplorerOption option = grid.Items.OfType<ExplorerOption>().FirstOrDefault(o => o.Property.Name == field &&
                    (!error.Field.StartsWith("Study.", StringComparison.Ordinal) || _studyOptions.Contains(o)) &&
                    (!error.Field.StartsWith("Episodes.", StringComparison.Ordinal) || _episodeOptions.Contains(o)));
                if (option == null) { continue; }
                TabControlOptions.SelectedIndex = grid == DataGridFormation ? 0 : grid == DataGridView ? 1 : grid == DataGridAdaptation ? 2 : grid == DataGridModules ? 3 : 4;
                OpenInputTable(grid); grid.SelectedItem = option; grid.ScrollIntoView(option); grid.Focus(); return;
            }
            TextBlockStatus.Text += " Поле находится в основном окне Order Flow.";
        }
        private void FitPrice(object sender, RoutedEventArgs e) { try { _chart.FitPrice(); } catch (Exception error) { Error(error); } }
        private void ResetAxes(object sender, RoutedEventArgs e) { try { _chart.ResetAxes(); } catch (Exception error) { Error(error); } }
        private void ShowInterval(DateTime from, DateTime to)
        {
            if (_run == null || _playback != null) { return; }
            if (_job != null) { _pendingInterval = (from, to); return; }
            _chartFrom = from; _chartTo = to <= from ? from.AddSeconds(1) : to;
            ExplorerRun run = _run; OrderFlowDisplayTimeFrame frame = _timeFrame; DateTime end = _chartTo.Value;
            StartJob(token => ExplorerChartContext.Load(run, from, end, frame, token), result =>
            {
                _chartContext = (ExplorerChartContext)result; Paint();
                TextBlockStatus.Text = $"Интервал графика: {from:dd.MM.yyyy HH:mm:ss} — {end:dd.MM.yyyy HH:mm:ss}. Свечей: {_chartContext.Bars.Count}." +
                    (_chartContext.Limited ? " Показаны первые 4000 меток каждого слоя; сузьте интервал для остальных." : "");
            });
        }
        private string EpisodeState(ExplorerRun run, int rows)
        {
            if (!run.Spec.Episodes.Enabled) { return "Модуль выключен. Откройте 2.4 → Включить модуль = True, затем нажмите «Рассчитать». Старый каталог сохраняется."; }
            long count = TableCount(run.EpisodePath, "episodes"), children = TableCount(run.EpisodePath, "children");
            string rules = $"Профиль {run.Spec.Episodes.Profile}; пауза ≤ {run.Spec.Episodes.MaximumPauseSeconds} с; длительность ≤ {run.Spec.Episodes.MaximumDurationSeconds} с; зона ≤ {run.Spec.Episodes.MaximumZoneTicks} шагов. ";
            if (count == 0)
            {
                _profileQuality.TryGetValue(run.Spec.Episodes.Profile, out (long Unknown, long Eligible) quality);
                return rules + $"Эпизодов 0; пригодных завершённых Cloud профиля: {quality.Eligible}. Незавершённые на конце файла (OpenAtEnd) не объединяются. Проверьте каталог и параметры формирования.";
            }
            return rules + $"Всего {count:N0} эпизодов / {children:N0} дочерних Cloud. Страница с {Offset("episodes") + 1:N0}; строк {rows}. " + (rows == 0 ? "Достигнут конец списка — нажмите «Первая страница»." : "");
        }
        private DataGrid[] ResultGrids() => new[] { DataGridCatalog, DataGridEpisodes, DataGridObservations, DataGridLabels, DataGridTriggers, DataGridPivots, DataGridDiagnostics, DataGridPatternWeek };
        private void TranslateColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            try
            {
                string title = e.PropertyName switch
                { "ObservedAt" => "Наблюдалось", "KnownAt" => "Стало известно", "KnownSequence" => "Порядок подтверждения", "Profile" => "Слой / масштаб", "Reason" => "Причина", "Status" => "Состояние",
                    "RelativeStatus" => "Относительный фон", "Date" => "Исходная дата", "Buy" => "Покупки", "Sell" => "Продажи", "Volume" => "Объём", "Count" => "Число сделок", "Clouds" => "Завершённые Cloud", "Episodes" => "Завершённые эпизоды", _ => e.PropertyName };
                e.Column.Header = title;
                if (e.Column is DataGridTextColumn column && column.Binding is Binding binding && e.PropertyType == typeof(string)) { binding.Converter = ExplorerStatusConverter.Instance; }
            }
            catch (Exception error) { Error(error); }
        }
    }
    internal sealed class ExplorerStatusConverter : IValueConverter
    {
        internal static readonly ExplorerStatusConverter Instance = new ExplorerStatusConverter();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        { if (value is not string code) { return value; } string text = ExplorerValidation.Status(code); return text == code ? code : text + " (" + code + ")"; }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
