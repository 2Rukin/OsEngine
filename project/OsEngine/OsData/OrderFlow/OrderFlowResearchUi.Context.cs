/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.OsData.OrderFlow.Calibration;
using OsEngine.OsData.OrderFlow.Context;
using System;
using System.Windows;

namespace OsEngine.OsData.OrderFlow
{
    public partial class OrderFlowResearchUi
    {
        private readonly CalibrationWindowSet _contextWindows = new CalibrationWindowSet();
        private void InitializeContext()
        {
            ButtonContext.Content = L("Market context", "Контекст рынка");
            ButtonContext.ToolTip = L("Separate multiscale workspace with one grey-price chart", "Отдельная система областей и событий: три масштаба на одном графике с серой ценой");
            ButtonContext.Click += ContextClick;
        }
        private void ContextClick(object sender, RoutedEventArgs e)
        {
            try { _contextWindows.Open("market-context", () => new FlowContextWindow(CreateExplorerInput())); }
            catch (Exception error) { ShowError(error); }
        }
        private void DisposeContext() { ButtonContext.Click -= ContextClick; _contextWindows.Dispose(); }
    }
}
