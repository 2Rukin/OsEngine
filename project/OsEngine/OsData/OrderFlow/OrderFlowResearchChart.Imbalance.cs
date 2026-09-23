/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;

namespace OsEngine.OsData.OrderFlow
{
    internal sealed partial class OrderFlowResearchChart
    {
        #region Filtered display

        private bool _showRejectedClouds;
        private bool _showRejectedClouds2;
        private OrderFlowCloudFilter _cloudFilter;
        private OrderFlowCloudFilter _cloudFilter2;
        private List<OrderFlowCloud> _viewClouds;
        private List<OrderFlowCloud> _viewClouds2;
        private List<OrderFlowCloud> _passingClouds;
        private List<OrderFlowCloud> _passingClouds2;

        /// <summary>UI-only per-layer bypass of the recorded imbalance verdict; applies to markers, hit tests and Cloud paths without changing results.</summary>
        public void SetShowRejectedClouds(bool first, bool second)
        {
            _showRejectedClouds = first; _showRejectedClouds2 = second;
            _cloudHits.Clear(); _cloudHits2.Clear(); _cloudPlotBounds = Rect.Empty;
            ToolTip = null; InvalidateVisual();
        }

        /// <summary>Applies independent immutable filters to saved evidence; updates markers, paths, hits and table rows without modifying the run.</summary>
        public void SetCloudFilters(OrderFlowCloudFilter first, OrderFlowCloudFilter second)
        {
            _cloudFilter = first; _cloudFilter2 = second;
            _selectedCloudId = null;
            ClearImbalanceDisplayCache();
            _cloudHits.Clear(); _cloudHits2.Clear(); _cloudPlotBounds = Rect.Empty;
            ToolTip = null; InvalidateVisual();
        }

        /// <summary>Replaces one display filter, preserving the independent other layer.</summary>
        public void SetCloudFilter(OrderFlowCloudFilter filter, bool second) => SetCloudFilters(second ? _cloudFilter : filter, second ? filter : _cloudFilter2);

        /// <summary>All view rows, including failures, for the current historical/replay prefix. The caller must not mutate them.</summary>
        internal List<OrderFlowCloud> CloudViewRows(bool second)
        {
            if (_result == null) { return new List<OrderFlowCloud>(); }
            if (second) { return _viewClouds2 ??= _cloudFilter2 == null ? _result.Clouds2 : _result.Clouds2.Select(_cloudFilter2.Apply).ToList(); }
            return _viewClouds ??= _cloudFilter == null ? _result.Clouds : _result.Clouds.Select(_cloudFilter.Apply).ToList();
        }

        private void ClearImbalanceDisplayCache() { _passingClouds = _passingClouds2 = _viewClouds = _viewClouds2 = null; }

        private List<OrderFlowCloud> DisplayClouds(bool second)
        {
            if (second)
            {
                if (_showRejectedClouds2) { return CloudViewRows(true); }
                return _passingClouds2 ??= CloudViewRows(true).Where(cloud => cloud.ImbalancePassed || cloud.CloudId == _selectedCloudId).ToList();
            }
            if (_showRejectedClouds) { return CloudViewRows(false); }
            return _passingClouds ??= CloudViewRows(false).Where(cloud => cloud.ImbalancePassed || cloud.CloudId == _selectedCloudId).ToList();
        }

        #endregion

        #region Evidence text

        private static string ImbalanceDetails(OrderFlowCloud cloud)
        {
            return "\n" + L("Volume delta", "Дельта объёма") + " " + cloud.DeltaPercent.ToString("F2", CultureInfo.InvariantCulture) + "%"
                + " · " + L("Filter", "Фильтр") + " " + cloud.ImbalanceSource + " " + (cloud.ImbalancePassed ? "PASS" : "FAIL")
                + ProfileDetails(cloud.InsideImbalance, cloud.PriceStep, L("Inside Cloud", "Внутри Cloud"))
                + ProfileDetails(cloud.ContextImbalance, cloud.PriceStep, L("Surrounding flow", "Окружающий поток"));
        }

        private static string ProfileDetails(OrderFlowImbalanceSnapshot snapshot, decimal step, string title)
        {
            if (snapshot == null) { return string.Empty; }
            return "\n" + title + " · Buy " + F(snapshot.BuyVolume) + " / Sell " + F(snapshot.SellVolume)
                + " · Δ " + snapshot.DeltaPercent.ToString("F2", CultureInfo.InvariantCulture) + "%"
                + "\nBuy " + PairDetails(snapshot.BestBuy, step, true) + " · Sell " + PairDetails(snapshot.BestSell, step, false)
                + "\n" + L("After volume/difference floors", "После порогов объёма/разности")
                + " · Buy " + PairDetails(snapshot.EligibleBuy, step, true) + " · Sell " + PairDetails(snapshot.EligibleSell, step, false);
        }

        private static string PairDetails(OrderFlowDiagonalPair pair, decimal step, bool buy)
        {
            if (pair == null) { return L("no comparison", "нет сравнения"); }
            return (buy ? pair.BuyRatioPercent : pair.SellRatioPercent).ToString("F2", CultureInfo.InvariantCulture)
                + "% [Buy " + F(pair.Buy) + " @ " + F(pair.LowerPrice + step) + "; Sell " + F(pair.Sell) + " @ " + F(pair.LowerPrice) + "]";
        }

        #endregion
    }
}
