/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
namespace OsEngine.OsData.OrderFlow.Explorer
{
    /// <summary>Owns only the temporarily transferred Explorer chart surface; the workbench retains calculation lifetime.</summary>
    public partial class CloudExplorerChartWindow
    {
        /// <summary>Creates an empty chart host; the workbench attaches and later reclaims its existing surface.</summary>
        public CloudExplorerChartWindow() { InitializeComponent(); }
    }
}
