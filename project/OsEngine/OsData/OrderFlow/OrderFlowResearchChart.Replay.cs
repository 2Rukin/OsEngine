/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Generic;
using System.Windows;

namespace OsEngine.OsData.OrderFlow
{
    internal sealed partial class OrderFlowResearchChart
    {
        private OrderFlowResearchResult _beforeReplay;
        private int _beforeReplayStart;
        private int _beforeReplayCount;
        private OrderFlowDisplayTimeFrame _beforeReplayTimeFrame;
        private DateTime? _beforeReplayFrom;
        private DateTime? _beforeReplayTo;
        internal bool IsReplaying { get; private set; }

        /// <summary>Starts an empty prefix view with separate fixed, known-before-playback Cloud reference volumes.</summary>
        /// <remarks>UI-thread-only. Retains source annotations and original viewport for return; clears historical markers/hit targets.</remarks>
        public void BeginReplay(decimal referenceVolume, decimal referenceVolume2 = 1)
        {
            if (referenceVolume <= 0 || referenceVolume2 <= 0) { throw new ArgumentOutOfRangeException(nameof(referenceVolume)); }
            if (IsReplaying) { throw new InvalidOperationException("Replay is already active."); }
            _beforeReplay = _result;
            _beforeReplayStart = _startIndex;
            _beforeReplayCount = _visibleCount;
            _beforeReplayTimeFrame = _timeFrame;
            List<OrderFlowDisplayBar> bars = CurrentBars;
            _beforeReplayFrom = bars.Count == 0 ? null : bars[_startIndex].TimeStart;
            _beforeReplayTo = bars.Count == 0 ? null : bars[_startIndex + VisibleCount - 1].TimeEnd;
            IsReplaying = true;
            _cloudReferenceVolume = referenceVolume;
            _cloudReferenceVolume2 = referenceVolume2;
            _visibleCount = 120;
            _startIndex = 0;
            InstallPlaybackResult(new OrderFlowResearchResult { Bars = new Dictionary<OrderFlowDisplayTimeFrame, List<OrderFlowDisplayBar>>() });
        }

        /// <summary>Installs a detached prefix on the UI thread, following new bars only while the viewport is at the right edge.</summary>
        public void ApplyReplayFrame(OrderFlowResearchResult result)
        {
            if (!IsReplaying) { throw new InvalidOperationException("Replay is not active."); }
            bool follow = _startIndex + VisibleCount >= TotalBars;
            InstallPlaybackResult(result);
            if (follow) { ScrollTo(Math.Max(0, TotalBars - _visibleCount)); }
        }

        /// <summary>Restores history and volume reference; retains the chosen timeframe and covers the pre-replay source-time range with its bars. Annotations remain.</summary>
        public void EndReplay()
        {
            if (!IsReplaying) { return; }
            IsReplaying = false;
            _startIndex = _beforeReplayStart;
            _visibleCount = _beforeReplayCount;
            InstallPlaybackResult(_beforeReplay);
            _beforeReplay = null;
            if (_timeFrame != _beforeReplayTimeFrame && _beforeReplayFrom.HasValue && TotalBars > 0)
            {
                List<OrderFlowDisplayBar> bars = CurrentBars;
                int first = bars.FindIndex(bar => bar.TimeEnd > _beforeReplayFrom.Value);
                int after = bars.FindIndex(bar => bar.TimeStart >= _beforeReplayTo.Value);
                _startIndex = first < 0 ? bars.Count - 1 : first;
                _visibleCount = Math.Max(1, (after < 0 ? bars.Count : after) - _startIndex);
            }
            UpdateCloudReference();
            ScrollTo(_startIndex);
        }

        private void InstallPlaybackResult(OrderFlowResearchResult result)
        {
            _result = result;
            ClearImbalanceDisplayCache();
            _displayBarCache.Clear();
            _shortestLabels = result == null ? new Dictionary<string, OrderFlowMarketPathLabel>() : OrderFlowCandidateView.ShortestLabels(result);
            _selectedCandidateId = null;
            _selectedCloudId = null;
            _cloudHits.Clear();
            _cloudHits2.Clear();
            _cloudPlotBounds = Rect.Empty;
            ToolTip = null;
            ScrollTo(_startIndex);
        }
    }
}
