/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OsEngine.OsData.OrderFlow
{
    public partial class OrderFlowResearchUi
    {
        #region Cloud row navigation

        private void InitializeCloudNavigation()
        {
            foreach (DataGrid grid in new[] { DataGridClouds, DataGridClouds2 })
            {
                MenuItem item = new MenuItem { Header = L("Show on chart", "Показать на графике"),
                    ToolTip = L("Open the exact Cloud anchor. A filtered-out Cloud is temporarily revealed with a gold outline.",
                        "Перейти к времени и цене этого Cloud. Отсечённый фильтром Cloud временно показывается с золотым контуром.") };
                item.SetResourceReference(ForegroundProperty, "ControlForegroundWhite");
                item.Click += ShowCloudOnChart_Click;
                grid.ContextMenu = new ContextMenu(); grid.ContextMenu.Items.Add(item);
                grid.PreviewMouseRightButtonDown += CloudGrid_RightButtonDown;
                grid.ContextMenuOpening += CloudGrid_ContextMenuOpening;
            }
        }

        private void CloudGrid_RightButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                DataGrid grid = (DataGrid)sender;
                DataGridRow row = ItemsControl.ContainerFromElement(grid, e.OriginalSource as DependencyObject) as DataGridRow;
                grid.ContextMenu.DataContext = row?.Item;
                if (row?.Item is OrderFlowCloud cloud) { grid.SelectedItem = cloud; }
            }
            catch (Exception error) { ShowError(error); }
        }

        private void CloudGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            try { if (((DataGrid)sender).ContextMenu.DataContext is not OrderFlowCloud) { e.Handled = true; } }
            catch (Exception error) { e.Handled = true; ShowError(error); }
        }

        private void ShowCloudOnChart_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (((MenuItem)sender).Parent is ContextMenu menu && menu.DataContext is OrderFlowCloud cloud) { ShowCloudOnChart(cloud); }
            }
            catch (Exception error) { ShowError(error); }
        }

        private void ShowCloudOnChart(OrderFlowCloud cloud)
        {
            if (_replay != null) { ReturnFromReplay(); }
            bool second = cloud.CloudId.StartsWith("CL2-", StringComparison.Ordinal);
            if (second) { CheckBoxShowCloud2.IsChecked = true; } else { CheckBoxShowCloud.IsChecked = true; }
            _chart.SetLayers(CheckBoxShowDelta.IsChecked == true, CheckBoxShowCloud.IsChecked == true, CheckBoxShowCloud2.IsChecked == true);
            _chart.SelectCloud(cloud.CloudId);
            TextBlockCandidateDetails.Text = OrderFlowResearchChart.CloudDetails(cloud);
            ShowChartView();
        }

        private void DisposeCloudNavigation()
        {
            foreach (DataGrid grid in new[] { DataGridClouds, DataGridClouds2 })
            {
                grid.PreviewMouseRightButtonDown -= CloudGrid_RightButtonDown;
                grid.ContextMenuOpening -= CloudGrid_ContextMenuOpening;
                if (grid.ContextMenu?.Items[0] is MenuItem item) { item.Click -= ShowCloudOnChart_Click; }
                grid.ContextMenu = null;
            }
        }

        #endregion
    }
}
