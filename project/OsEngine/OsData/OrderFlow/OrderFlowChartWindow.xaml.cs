using OsEngine.Language;
using System.Windows;
using System.Windows.Controls;

namespace OsEngine.OsData.OrderFlow
{
    /// <summary>
    /// Independent modeless shell for the existing research chart surface. The workbench owns transfer, subscriptions, closure and restoration.
    /// This window never starts calculations, reads data or creates a second chart.
    /// </summary>
    public partial class OrderFlowChartWindow : Window
    {
        /// <summary>Initializes an empty shell without a WPF owner; the caller moves the surface before Show and explicitly closes the shell on shutdown.</summary>
        public OrderFlowChartWindow()
        {
            InitializeComponent();
            Title = OsLocalization.ConvertToLocString("Eng:Order Flow chart_Ru:График Order Flow_");
            Layout.StickyBorders.Listen(this);
        }

        internal ContentControl SurfaceHost => ContentControlSurface;
    }
}
