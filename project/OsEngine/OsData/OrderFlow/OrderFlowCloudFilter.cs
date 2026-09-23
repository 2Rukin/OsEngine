/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace OsEngine.OsData.OrderFlow
{
    /// <summary>Immutable display-filter settings for an already calculated Cloud layer. No input read or chain recalculation.</summary>
    /// <remarks>Owns a private copy of imbalance settings. Price step, context duration and chain formation remain properties of the source run.</remarks>
    internal sealed class OrderFlowCloudFilter
    {
        private readonly OrderFlowImbalanceSettings _imbalance;
        private readonly ImmutableHashSet<string> _allowedClouds;
        public int MinimumTradeCount { get; }
        public int MaximumTradeCount { get; }

        public OrderFlowCloudFilter(OrderFlowImbalanceSettings imbalance, int minimumTradeCount = 0, int maximumTradeCount = 0, IEnumerable<string> allowedClouds = null)
        {
            if (imbalance == null) { throw new ArgumentNullException(nameof(imbalance)); }
            if (minimumTradeCount < 0 || maximumTradeCount < 0 || (maximumTradeCount > 0 && minimumTradeCount > maximumTradeCount))
            { throw new ArgumentException("Cloud tick-count range must be non-negative and ordered; maximum 0 means unlimited."); }
            _imbalance = new OrderFlowImbalanceSettings { ContextSeconds = imbalance.ContextSeconds, Source = imbalance.Source,
                Direction = imbalance.Direction, MinimumRatioPercent = imbalance.MinimumRatioPercent,
                MinimumDominantVolume = imbalance.MinimumDominantVolume, MinimumDifference = imbalance.MinimumDifference,
                MinimumDeltaPercent = imbalance.MinimumDeltaPercent };
            _imbalance.Validate();
            _allowedClouds = allowedClouds?.ToImmutableHashSet(StringComparer.Ordinal);
            MinimumTradeCount = minimumTradeCount; MaximumTradeCount = maximumTradeCount;
        }

        /// <summary>Evaluates the whole current Cloud count, not its first qualification. Returns fresh verdict/witnesses, or shares an identical unchanged source row.</summary>
        /// <remarks>During replay a forming Cloud has its consumed-prefix count. Original DTO, hashes, qualification and exports remain unchanged.</remarks>
        public OrderFlowCloud Apply(OrderFlowCloud cloud)
        {
            if (cloud == null) { throw new ArgumentNullException(nameof(cloud)); }
            if (_imbalance.Source == OrderFlowImbalanceSource.Off && MinimumTradeCount == 0 && MaximumTradeCount == 0 && _allowedClouds == null &&
                cloud.ImbalanceSource == OrderFlowImbalanceSource.Off && cloud.ImbalancePassed) { return cloud; }
            OrderFlowCloud view = cloud.Copy();
            view.InsideImbalance = cloud.InsideImbalance?.WithFloors(_imbalance);
            view.ContextImbalance = cloud.ContextImbalance?.WithFloors(_imbalance);
            view.ImbalanceSource = _imbalance.Source;
            view.ImbalancePassed = WithinCountAndSelection(cloud) &&
                _imbalance.Passes(view.InsideImbalance, view.ContextImbalance);
            return view;
        }

        /// <summary>Evaluates saved evidence without allocating a Cloud view or changing the result.</summary>
        public bool Passes(OrderFlowCloud cloud)
        {
            return WithinCountAndSelection(cloud) && (_imbalance.Source == OrderFlowImbalanceSource.Off ||
                _imbalance.Passes(cloud.InsideImbalance?.WithFloors(_imbalance), cloud.ContextImbalance?.WithFloors(_imbalance)));
        }

        /// <summary>Creates an independent filter limited to the study's causal volatility cohort, for chart exploration only.</summary>
        public OrderFlowCloudFilter WithAllowedClouds(IEnumerable<string> ids) => new OrderFlowCloudFilter(_imbalance, MinimumTradeCount, MaximumTradeCount, ids);

        private bool WithinCountAndSelection(OrderFlowCloud cloud) => cloud.TradeCount >= MinimumTradeCount &&
            (MaximumTradeCount == 0 || cloud.TradeCount <= MaximumTradeCount) && (_allowedClouds == null || _allowedClouds.Contains(cloud.CloudId));
    }
}
