/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.OsData.OrderFlow.DetachedTables;
using System.Windows;
using System.Windows.Controls;

namespace OsEngine.OsData.OrderFlow
{
    public partial class OrderFlowResearchUi
    {
        private DetachedTableSet _tableWindows;

        private void InitializeTableWindows()
        {
            _tableWindows = new DetachedTableSet(this, ShowError);
            _tableWindows.Add(DataGridCandidates, "Кандидаты Order Flow", TableContext,
                () => TabControlResults.SelectedItem = TabItemCandidates,
                () => _tableWindows.ActionButton("Показать выбранного кандидата на графике", () =>
                { DataGridCandidates_MouseDoubleClick(DataGridCandidates, null); }));
            _tableWindows.Add(DataGridClouds, "Cloud 1", TableContext,
                () => TabControlResults.SelectedItem = TabItemClouds, () => CloudTableTools("Cloud", DataGridClouds));
            _tableWindows.Add(DataGridClouds2, "Cloud 2", TableContext,
                () => TabControlResults.SelectedItem = TabItemClouds2, () => CloudTableTools("Cloud2", DataGridClouds2));
            _tableWindows.Add(DataGridRecommendations, "Рекомендованные параметры", TableContext,
                () => TabControlResults.SelectedItem = TabItemStatistics, () => _tableWindows.Command(ButtonStatsShow));
            _tableWindows.Add(DataGridJournal, "Журнал событий Order Flow", TableContext,
                () => TabControlResults.SelectedItem = TabItemJournal);
        }

        private string TableContext() => _displayedResult == null ? "Исследование ещё не рассчитано" :
            _displayedResult.Input.FileName + " · " + _displayedResult.ResearchSpecHash;

        private FrameworkElement CloudTableTools(string prefix, DataGrid grid)
        {
            WrapPanel panel = new WrapPanel();
            panel.Children.Add(DetachedTableSet.Field("Фильтр", DetachedTableSet.Choice(ImbalanceControl<ComboBox>(prefix, "ImbalanceSource"))));
            panel.Children.Add(DetachedTableSet.Field("Направление", DetachedTableSet.Choice(ImbalanceControl<ComboBox>(prefix, "ImbalanceDirection"))));
            string[] fields = { "ImbalanceRatio", "ImbalanceVolume", "ImbalanceDifference", "ImbalanceDelta", "MinCount", "MaxCount" };
            string[] titles = { "Соотношение, %", "Мин. объём стороны", "Мин. разность", "Мин. дельта, %", "Мин. тиков", "Макс. тиков (0 — любое)" };
            for (int i = 0; i < fields.Length; i++)
            {
                TextBox editor = DetachedTableSet.Search(ImbalanceControl<TextBox>(prefix, fields[i]));
                editor.MinWidth = 70; editor.Width = 90;
                panel.Children.Add(DetachedTableSet.Field(titles[i], editor));
            }
            // Raise Click on the original button: the existing handler uses sender identity to choose Cloud 1/2.
            panel.Children.Add(_tableWindows.Command(ImbalanceControl<Button>(prefix, "FilterApply")));
            panel.Children.Add(_tableWindows.ActionButton("Показать выбранный Cloud на графике", () =>
            { if (grid.SelectedItem is OrderFlowCloud cloud) { ShowCloudOnChart(cloud); } }));
            return panel;
        }
    }
}
