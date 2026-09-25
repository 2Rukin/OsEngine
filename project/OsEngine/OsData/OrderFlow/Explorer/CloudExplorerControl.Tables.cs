/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.OsData.OrderFlow.DetachedTables;
using System;
using System.Windows;
using System.Windows.Controls;

namespace OsEngine.OsData.OrderFlow.Explorer
{
    public partial class CloudExplorerControl
    {
        private DetachedTableSet _tableWindows;

        private void InitializeTableWindows()
        {
            _tableWindows = new DetachedTableSet(this, Error);
            DataGrid[] settings = { DataGridFormation, DataGridView, DataGridAdaptation, DataGridModules, DataGridStudy, DataGridPatternOptions };
            string[] settingTitles = { "Формирование Cloud", "Видимость Cloud", "Адаптация профиля", "Модули и эпизоды", "Параметры исследования", "Дополнительные параметры предвестников" };
            for (int i = 0; i < settings.Length; i++)
            {
                int tab = i;
                _tableWindows.Add(settings[i], settingTitles[i], () => "Профиль: " + ComboBoxProfile.Text,
                    () => TabControlOptions.SelectedIndex = tab, () => SettingsTableTools(tab));
            }
            DataGrid[] results = { DataGridCatalog, DataGridEpisodes, DataGridObservations, DataGridLabels, DataGridTriggers, DataGridPivots, DataGridDiagnostics };
            string[] resultTitles = { "Каталог Cloud", "Эпизоды", "Наблюдения", "Будущие исходы", "Триггеры", "Экстремумы", "Причины и активные наблюдения" };
            for (int i = 0; i < results.Length; i++)
            {
                int tab = i == 0 ? 0 : i + 1;
                _tableWindows.Add(results[i], resultTitles[i], () => ResultTableContext(tab),
                    () => TabControlResult.SelectedIndex = tab, () => ResultTableTools(tab));
            }
            _tableWindows.Add(DataGridPatternCards, "Найденные сценарии", TableContext,
                () => { TabControlResult.SelectedItem = TabItemPatterns; TabControlPatterns.SelectedItem = TabItemPatternList; },
                PatternTableTools);
            _tableWindows.Add(DataGridPatternWeek, "Недельный контекст примера", TableContext,
                () => { TabControlResult.SelectedItem = TabItemPatterns; TabControlPatterns.SelectedItem = TabItemPatternCard; }, PatternExampleTools);
        }

        private void OpenInputTable(DataGrid grid)
        {
            // Validation can also run in an unmounted offline component test.
            if (Window.GetWindow(this) != null) { _tableWindows.Open(grid); }
        }

        private string TableContext()
        {
            if (_run == null) { return "Результат ещё не открыт"; }
            string dates = _run.Catalog.Dates.Length == 0 ? "Нет исходных дат" :
                (_run.Spec.FromDate ?? _run.Catalog.Dates[0]).ToString("yyyy-MM-dd") + " … " +
                (_run.Spec.ToDate ?? _run.Catalog.Dates[_run.Catalog.Dates.Length - 1]).ToString("yyyy-MM-dd");
            return (_patternRun?.Manifest.Hash ?? _run.Spec.StudyHash ?? _run.Spec.CatalogSpecHash) + " · " + dates;
        }

        private string ResultTableContext(int tab)
        {
            string state = _run == null ? "Сначала рассчитайте или откройте сохранённый результат." :
                tab == 2 ? TextBlockEpisodeState.Text : tab >= 3 && _run.StudyPath == null ? "Исследовательский модуль не включён в этот результат." : TextBlockStatus.Text;
            return TableContext() + " · " + state;
        }

        private FrameworkElement SettingsTableTools(int tab)
        {
            WrapPanel panel = new WrapPanel();
            if (tab == 0 || tab == 1 || tab == 2) { panel.Children.Add(DetachedTableSet.Field("Профиль", DetachedTableSet.Choice(ComboBoxProfile))); }
            panel.Children.Add(_tableWindows.Command(tab == 1 ? ButtonFilter : tab == 5 ? ButtonPatternSearch : ButtonCalculate));
            panel.Children.Add(_tableWindows.Command(ButtonCancel));
            return panel;
        }

        private FrameworkElement PatternTableTools()
        {
            WrapPanel panel = new WrapPanel();
            panel.Children.Add(DetachedTableSet.Field("Поиск сценария", DetachedTableSet.Search(TextBoxPatternFilter)));
            panel.Children.Add(_tableWindows.ActionButton("Открыть карточку выбранного сценария", () =>
            { TabControlResult.SelectedItem = TabItemPatterns; TabControlPatterns.SelectedItem = TabItemPatternCard; Window.GetWindow(this)?.Activate(); }));
            return panel;
        }

        private FrameworkElement PatternExampleTools()
        {
            WrapPanel panel = new WrapPanel();
            panel.Children.Add(DetachedTableSet.Field("Пример", DetachedTableSet.Choice(ComboBoxPatternExample)));
            panel.Children.Add(_tableWindows.Command(ButtonPatternChart));
            panel.Children.Add(_tableWindows.Command(ButtonPatternReplay));
            return panel;
        }

        private FrameworkElement ResultTableTools(int tab)
        {
            WrapPanel panel = new WrapPanel();
            panel.Children.Add(_tableWindows.Command(ButtonFirst, () => { TabControlResult.SelectedIndex = tab; FirstPage(null, new RoutedEventArgs()); }));
            panel.Children.Add(_tableWindows.Command(ButtonNext, () => { TabControlResult.SelectedIndex = tab; NextPage(null, new RoutedEventArgs()); }));
            if (tab == 0)
            {
                panel.Children.Add(DetachedTableSet.Search(TextBoxFind));
                panel.Children.Add(_tableWindows.Command(ButtonFind, () => { TabControlResult.SelectedIndex = tab; Find(null, new RoutedEventArgs()); }));
            }
            if (tab <= 2) { panel.Children.Add(_tableWindows.Command(ButtonAnchor)); }
            if (tab == 0 || tab == 2 || tab == 3)
            {
                panel.Children.Add(_tableWindows.ActionButton("Показать выбранное на графике", () =>
                {
                    if (tab == 0) { CatalogDoubleClick(DataGridCatalog, null); }
                    else { TabControlResult.SelectedItem = TabItemChart; (_window as Window ?? Window.GetWindow(this))?.Activate(); }
                }));
            }
            panel.Children.Add(_tableWindows.Command(ButtonArtifacts));
            return panel;
        }
    }
}
