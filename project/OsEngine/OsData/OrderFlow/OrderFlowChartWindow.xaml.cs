using OsEngine.Language;
using System.Windows;
using System.Windows.Controls;

namespace OsEngine.OsData.OrderFlow
{
    /// <summary>
    /// Owned modeless shell for the existing research chart surface. The workbench owns transfer, subscriptions and restoration.
    /// This window never starts calculations, reads data or creates a second chart.
    /// </summary>
    public partial class OrderFlowChartWindow : Window
    {
        /// <summary>Initializes an empty shell; the caller assigns Owner and moves the surface before Show.</summary>
        public OrderFlowChartWindow()
        {
            InitializeComponent();
            Title = OsLocalization.ConvertToLocString("Eng:Order Flow chart_Ru:График Order Flow_");
            Layout.StickyBorders.Listen(this);
        }

        internal ContentControl SurfaceHost => ContentControlSurface;
    }
}
