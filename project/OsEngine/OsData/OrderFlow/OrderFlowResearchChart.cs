/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using OsEngine.Logging;
using OsEngine.Language;
using OsEngine.Market;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Input;

namespace OsEngine.OsData.OrderFlow
{
    /// <summary>
    /// Diagnostic-only presenter for research bars and candidate markers.
    /// </summary>
    /// <remarks>
    /// Changing the display timeframe or selected marker never recalculates
    /// features, candidates or labels. View changes are UI-thread-only; the owner
    /// subscribes to ViewChanged/DrawingChanged and unsubscribes when closing. Temporary
    /// source-time/price annotations survive view changes but reset on SetResult; no persistence.
    /// Price styles and Cloud paths are historical display only. Contract: ORDER-FLOW-MVP-RUNBOOK-001.
    /// </remarks>
    internal sealed partial class OrderFlowResearchChart : FrameworkElement
    {
        #region State and navigation

        private OrderFlowResearchResult _result;
        private Rect _cloudPlotBounds = Rect.Empty;
        private double _cloudScale = 1;
        private bool _showDelta = true;
        private bool _showCloud = true;
        private bool _showCloud2 = true;
        private string _selectedCloudId;
        private readonly List<(Point Center, double Radius, OrderFlowCloud Cloud)> _cloudHits = new List<(Point, double, OrderFlowCloud)>();
        private readonly List<(Point Center, double Radius, OrderFlowCloud Cloud)> _cloudHits2 = new List<(Point, double, OrderFlowCloud)>();
        private Dictionary<string, OrderFlowMarketPathLabel> _shortestLabels = new Dictionary<string, OrderFlowMarketPathLabel>();
        private OrderFlowDisplayTimeFrame _timeFrame = OrderFlowDisplayTimeFrame.Min1;
        private string _selectedCandidateId;
        private readonly Dictionary<OrderFlowDisplayTimeFrame, List<OrderFlowDisplayBar>> _displayBarCache
            = new Dictionary<OrderFlowDisplayTimeFrame, List<OrderFlowDisplayBar>>();
        private readonly List<OrderFlowDisplayBar> _emptyBars = new List<OrderFlowDisplayBar>();

        private const double TimeAxisHeight = 36;
        private bool _dragging;
        private bool _dragTimeAxis;
        private Point _dragOrigin;
        private int _dragStart;
        private int _dragCount;
        private double _dragAnchor;

        private int _startIndex;
        private int _visibleCount = 120;

        public event EventHandler ViewChanged;

        public int StartIndex { get { return _startIndex; } }
        public int VisibleCount { get { return Math.Min(_visibleCount, TotalBars); } }
        public int TotalBars { get { return CurrentBars.Count; } }

        private List<OrderFlowDisplayBar> CurrentBars
        {
            get
            {
                List<OrderFlowDisplayBar> bars;
                if (_result == null) { return _emptyBars; }
                if (_result.Bars.TryGetValue(_timeFrame, out bars)) { return bars; }
                if (_displayBarCache.TryGetValue(_timeFrame, out bars)) { return bars; }
                List<OrderFlowDisplayBar> minutes;
                if ((_timeFrame != OrderFlowDisplayTimeFrame.Month1 && OrderFlowChartTimeFrames.GetDuration(_timeFrame) <= TimeSpan.FromMinutes(1)) ||
                    _result.Bars.TryGetValue(OrderFlowDisplayTimeFrame.Min1, out minutes) == false)
                {
                    return _emptyBars;
                }
                bars = OrderFlowChartTimeFrames.AggregateMinutes(minutes, _timeFrame);
                _displayBarCache.Add(_timeFrame, bars);
                return bars;
            }
        }

        public string RangeText
        {
            get
            {
                List<OrderFlowDisplayBar> bars = CurrentBars;
                if (bars.Count == 0) { return L("No trade bars", "Нет свечей со сделками"); }
                return bars[_startIndex].TimeStart.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture) +
                    " — " + bars[_startIndex + VisibleCount - 1].TimeEnd.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture) +
                    " · " + L("bars", "свечи") + " " + (_startIndex + 1) + "–" + (_startIndex + VisibleCount) +
                    " / " + bars.Count + " · " + L("Calculated period", "Расчётный период") + " " +
                    bars[0].TimeStart.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture) + "–" +
                    bars[bars.Count - 1].TimeEnd.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture);
            }
        }

        public OrderFlowResearchChart()
        {
            SnapsToDevicePixels = true;
            MinHeight = 320;
            Focusable = true;
        }

        /// <summary>Resets the viewport and temporary annotations for a new result without changing research data.</summary>
        /// <param name="result">Offline result to display, or null to clear the viewport.</param>
        public void SetResult(OrderFlowResearchResult result)
        {
            EndDrag();
            _result = result;
            ClearImbalanceDisplayCache();
            UpdateCloudReference();
            ClearDrawings();
            _shortestLabels = result == null ? new Dictionary<string, OrderFlowMarketPathLabel>() : OrderFlowCandidateView.ShortestLabels(result);
            _displayBarCache.Clear();
            _selectedCandidateId = null;
            _selectedCloudId = null;
            _cloudHits.Clear();
            _cloudHits2.Clear();
            _cloudPlotBounds = Rect.Empty;
            _visibleCount = 120;
            ScrollTo(Math.Max(0, TotalBars - _visibleCount));
        }

        /// <summary>Toggles Delta and both independent Cloud layers on the UI thread without recalculation; hidden Clouds have neither markers, hits nor price paths.</summary>
        public void SetLayers(bool delta, bool cloud, bool cloud2 = false)
        {
            CancelDrawingGesture();
            _drawingPlot = Rect.Empty;
            _showDelta = delta;
            _showCloud = cloud;
            _showCloud2 = cloud2;
            _cloudHits.Clear();
            _cloudHits2.Clear();
            _cloudPlotBounds = Rect.Empty;
            ToolTip = null;
            InvalidateVisual();
        }

        /// <summary>Scales Cloud circle radii and pointer hit areas by a display-only coefficient, without modifying results.</summary>
        /// <param name="coefficient">Finite radius multiplier from 0.1 through 3; initial value is 1.</param>
        /// <param name="secondLayer">True targets Cloud 2; false targets Cloud 1.</param>
        /// <remarks>UI-thread-only. Invalidates the last painted hit targets until the next render.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">The coefficient is non-finite or outside the supported range.</exception>
        public void SetCloudScale(double coefficient, bool secondLayer = false)
        {
            if (!double.IsFinite(coefficient) || coefficient < 0.1 || coefficient > 3)
            {
                throw new ArgumentOutOfRangeException(nameof(coefficient));
            }
            if (secondLayer) { _cloudScale2 = coefficient; }
            else { _cloudScale = coefficient; }
            _cloudHits.Clear();
            _cloudHits2.Clear();
            _cloudPlotBounds = Rect.Empty;
            ToolTip = null;
            InvalidateVisual();
        }

        /// <summary>Centers and temporarily reveals a selected Cloud even if filtered out; completion time remains separate from its anchor.</summary>
        public void SelectCloud(string cloudId)
        {
            _selectedCloudId = cloudId;
            _passingClouds = _passingClouds2 = null;
            _cloudHits.Clear(); _cloudHits2.Clear(); _cloudPlotBounds = Rect.Empty;
            OrderFlowCloud cloud = _result?.Clouds.Find(item => item.CloudId == cloudId) ?? _result?.Clouds2.Find(item => item.CloudId == cloudId);
            int index = cloud == null ? -1 : CurrentBars.FindIndex(bar => bar.TimeStart <= cloud.Time && bar.TimeEnd > cloud.Time);
            ScrollTo(index < 0 ? _startIndex : index - VisibleCount / 2);
        }

        /// <summary>Returns the topmost visible Cloud under the pointer within the last rendered price-panel clip.</summary>
        /// <remarks>UI-thread query; hidden portions of markers never own a Cloud tooltip.</remarks>
        internal OrderFlowCloud CloudAt(Point point)
        {
            if (!_cloudPlotBounds.Contains(point)) { return null; }
            for (int i = _cloudHits2.Count - 1; i >= 0; i--)
            {
                if (CloudHitContains(point, _cloudHits2[i])) { return _cloudHits2[i].Cloud; }
            }
            for (int i = _cloudHits.Count - 1; i >= 0; i--)
            {
                if (CloudHitContains(point, _cloudHits[i])) { return _cloudHits[i].Cloud; }
            }
            return null;
        }

        private static bool CloudHitContains(Point point, (Point Center, double Radius, OrderFlowCloud Cloud) hit)
        {
            Vector offset = point - hit.Center;
            return hit.Cloud.TradeCount == 1 ? Math.Abs(offset.X) <= hit.Radius && Math.Abs(offset.Y) <= hit.Radius : offset.Length <= hit.Radius;
        }

        internal static string CloudDetails(OrderFlowCloud cloud)
        {
            return (cloud.CloudId.StartsWith("CL2-", StringComparison.Ordinal) ? "Cloud 2 " : "Cloud 1 ") + cloud.CloudId + " · " + cloud.Time.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture) +
                "\n" + L("Price", "Цена") + " " + F(cloud.Price) + " · V " + F(cloud.Volume) +
                " · Buy " + F(cloud.BuyVolume) + " / " + cloud.BuyCount + " · Sell " + F(cloud.SellVolume) + " / " + cloud.SellCount +
                "\n" + L("Qualified", "Порог достигнут") + " " + cloud.Qualified.Time.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture) +
                " · V " + F(cloud.Qualified.Volume) + " · VWAP " + F(cloud.Qualified.Vwap) +
                "\n" + L("Count imbalance", "Перевес числа тиков") + " " + F(cloud.SidePercent) + "% · " + cloud.CompletionReason +
                " · " + L("Completed", "Завершён") + " " + (cloud.CompletedAt?.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture) ?? (cloud.CompletionReason == "Forming" ? L("forming", "формируется") : L("open at end", "открыт в конце данных"))) + ImbalanceDetails(cloud);
        }

        /// <summary>Changes display bars, retaining the visible midpoint time when possible.</summary>
        /// <param name="timeFrame">Base display bars or a larger aggregation cached from Min1 bars in this presenter.</param>
        public void SetTimeFrame(OrderFlowDisplayTimeFrame timeFrame)
        {
            CancelDrawingGesture();
            List<OrderFlowDisplayBar> previous = CurrentBars;
            DateTime? anchor = previous.Count == 0 ? null
                : previous[Math.Min(previous.Count - 1, _startIndex + VisibleCount / 2)].TimeStart;
            bool allVisible = previous.Count > 0 && VisibleCount == previous.Count;
            _timeFrame = timeFrame;
            List<OrderFlowDisplayBar> bars = CurrentBars;
            if (allVisible) { _visibleCount = Math.Max(1, bars.Count); }
            int index = anchor.HasValue ? bars.FindIndex(bar => bar.TimeEnd > anchor.Value) : bars.Count - 1;
            if (index < 0) { index = bars.Count - 1; }
            ScrollTo(index - VisibleCount / 2);
        }

        /// <summary>Centers the viewport on a candidate; subsequent scrolling stays under user control.</summary>
        /// <param name="candidateId">Local candidate identifier within the current result.</param>
        public void SelectCandidate(string candidateId)
        {
            _selectedCandidateId = candidateId;
            int index = FindSelectedBarIndex(CurrentBars);
            ScrollTo(index >= 0 ? index - VisibleCount / 2 : _startIndex);
        }

        /// <summary>Moves to a clamped bar index without recalculating features, candidates or labels.</summary>
        /// <param name="startIndex">Requested first bar, clamped to the available range.</param>
        public void ScrollTo(int startIndex)
        {
            _startIndex = Math.Clamp(startIndex, 0, Math.Max(0, TotalBars - VisibleCount));
            _drawingPlot = Rect.Empty;
            _lineHits.Clear();
            InvalidateVisual();
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Changes the number of visible bars around the current midpoint; the full file is supported.</summary>
        /// <param name="count">Requested viewport width in bars, clamped to the available range.</param>
        public void Zoom(int count)
        {
            ZoomAt(count, 0.5);
        }

        /// <summary>Changes horizontal scale while retaining the pointer time when file bounds allow it.</summary>
        /// <param name="count">Requested visible bar count; minimum two unless the file contains fewer.</param>
        /// <param name="anchor">Pointer fraction across the plot, clamped to zero through one.</param>
        public void ZoomAt(int count, double anchor)
        {
            ApplyZoom(_startIndex, VisibleCount, count, anchor);
        }

        private void ApplyZoom(int start, int oldCount, int count, double anchor)
        {
            _visibleCount = Math.Clamp(count, Math.Min(2, Math.Max(1, TotalBars)), Math.Max(1, TotalBars));
            ScrollTo(OrderFlowChartNavigation.ZoomStart(start, oldCount, VisibleCount, TotalBars, anchor));
        }

        /// <summary>Zooms around the pointer; Shift plus wheel scrolls through existing bars.</summary>
        /// <param name="e">Mouse wheel input handled by this chart.</param>
        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            try
            {
                EndDrag();
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
                {
                    ScrollTo(_startIndex + (e.Delta > 0 ? -1 : 1) * Math.Max(1, VisibleCount / 10));
                }
                else
                {
                    int count = OrderFlowChartNavigation.WheelCount(VisibleCount, TotalBars, e.Delta);
                    ZoomAt(count, PointerFraction(e.GetPosition(this).X));
                }
                e.Handled = true;
            }
            catch (Exception error)
            {
                ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error);
            }
        }

        /// <summary>Handles drawing/selection in the price panel before panning, or scaling on the bottom time axis.</summary>
        /// <param name="e">Pointer press within the plot width.</param>
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            try
            {
                Point point = e.GetPosition(this);
                if (TotalBars == 0 || point.X < 92 || point.X > ActualWidth - 12) { return; }
                Focus();
                if (DrawingPointerDown(point))
                {
                    if (_movingAnchor != 0 && !CaptureMouse()) { _movingAnchor = 0; }
                    e.Handled = true;
                    return;
                }
                if (DrawingTool != OrderFlowDrawingTool.Select && point.Y < ActualHeight - TimeAxisHeight) { e.Handled = true; return; }
                _dragOrigin = point;
                _dragStart = _startIndex;
                _dragCount = VisibleCount;
                _dragAnchor = PointerFraction(point.X);
                _dragTimeAxis = point.Y >= ActualHeight - TimeAxisHeight;
                _dragging = CaptureMouse();
                ToolTip = null;
                Cursor = _dragTimeAxis ? Cursors.SizeWE : Cursors.Hand;
                e.Handled = true;
            }
            catch (Exception error) { EndDrag(); ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error); }
        }

        /// <summary>Completes a chart drag and releases pointer capture.</summary>
        /// <param name="e">Pointer release.</param>
        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (_dragging || _movingAnchor != 0) { EndDrag(); e.Handled = true; }
        }

        /// <summary>Clears drag state if another control takes pointer capture.</summary>
        /// <param name="e">Capture loss event.</param>
        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            _dragging = false;
            _movingAnchor = 0;
            Cursor = null;
        }

        private void EndDrag()
        {
            _dragging = false;
            _movingAnchor = 0;
            if (IsMouseCaptured) { ReleaseMouseCapture(); }
            Cursor = null;
        }

        /// <summary>Processes an active navigation drag before a drawing preview; called by mouse input and offline component tests.</summary>
        internal bool MovePointerInteraction(Point point)
        {
            if (_dragging)
            {
                double distance = point.X - _dragOrigin.X;
                if (_dragTimeAxis)
                {
                    int count = (int)Math.Clamp(Math.Round(_dragCount * Math.Exp(Math.Clamp(-distance / 180, -10, 10))), 1, Math.Max(1, TotalBars));
                    ApplyZoom(_dragStart, _dragCount, count, _dragAnchor);
                }
                else
                {
                    ScrollTo(_dragStart - (int)Math.Round(distance * _dragCount / DataWidth));
                }
                return true;
            }
            if (DrawingPointerMove(point)) { Cursor = Cursors.Cross; return true; }
            return false;
        }

        private double PointerFraction(double x)
        {
            return Math.Clamp((x - 92) / DataWidth, 0, 1);
        }

        #endregion

        #region Rendering

        /// <summary>Shows the display bar under the pointer, with OHLC, signed bar volume and the last window response.</summary>
        /// <param name="e">Pointer coordinates in the chart.</param>
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            try
            {
                Point point = e.GetPosition(this);
                if (MovePointerInteraction(point)) { ToolTip = null; e.Handled = true; return; }
                Cursor = point.Y >= ActualHeight - TimeAxisHeight ? Cursors.SizeWE
                    : DrawingTool != OrderFlowDrawingTool.Select ? Cursors.Cross : DrawingAt(point) != null ? Cursors.Hand : null;
                OrderFlowCloud hovered = CloudAt(point);
                if (hovered != null) { ToolTip = CloudDetails(hovered); return; }
                int barIndex = BarIndexAt(point.X);
                if (barIndex < 0) { ToolTip = null; return; }
                OrderFlowDisplayBar bar = CurrentBars[barIndex];
                ToolTip = bar.TimeStart.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture) +
                    "\nO " + F(bar.Open) + " · H " + F(bar.High) + " · L " + F(bar.Low) + " · C " + F(bar.Close) +
                    "\n" + L("Bar delta", "Дельта свечи") + " " + F(bar.Delta) +
                    " · " + L("Window response", "Отклик окна") + " " + F(bar.PriceResponse);

            }
            catch (Exception error)
            {
                ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error);
            }
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            try
            {
                DrawChart(drawingContext);
            }
            catch (Exception error)
            {
                ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error);
            }
        }

        private void DrawChart(DrawingContext drawingContext)
        {
            _drawingPlot = Rect.Empty;
            _lineHits.Clear();
            _cloudHits.Clear();
            _cloudHits2.Clear();
            _cloudPlotBounds = Rect.Empty;
            Rect bounds = new Rect(0, 0, ActualWidth, ActualHeight);
            drawingContext.DrawRectangle(BackgroundBrush, null, bounds);

            if (_result == null || ActualWidth < 200 || ActualHeight < 200)
            {
                DrawText(drawingContext, L("Run tick research to display diagnostic bars.", "Запустите расчёт по файлу тиков для просмотра графика."),
                    new Point(12, 12), ForegroundBrush, 13);
                return;
            }

            List<OrderFlowDisplayBar> allBars = CurrentBars;
            if (allBars.Count == 0)
            {
                DrawText(drawingContext, L("No trade bars are available for ", "Нет свечей со сделками для ") + OrderFlowChartTimeFrames.GetDisplayName(_timeFrame, OsLocalization.CurLocalization == OsLocalization.OsLocalType.Ru) + ".",
                    new Point(12, 12), ForegroundBrush, 13);
                return;
            }

            List<OrderFlowDisplayBar> bars = allBars.GetRange(_startIndex, VisibleCount);
            double left = 92;
            double right = Math.Max(left + 10, ActualWidth - 12);
            double width = right - left;
            double usable = ActualHeight - 100;
            double priceTop = 24;
            double priceBottom = priceTop + usable * 0.54;
            double deltaTop = priceBottom + 20;
            double deltaBottom = deltaTop + usable * 0.23;
            double responseTop = deltaBottom + 20;
            double responseBottom = responseTop + usable * 0.23;

            bool deltaVisible = _showDelta && _result.DeltaCalculated;
            if (!deltaVisible) { priceBottom = ActualHeight - 60; responseBottom = priceBottom; }
            else { DrawPanelBoundaries(drawingContext, left, right, priceBottom, deltaBottom, responseBottom); }
            string priceCaption = _cloudPriceMode ? L("Cloud line · grey High/Low background", "Линия Cloud · серый фон High/Low")
                : _priceDisplay == OrderFlowPriceDisplay.MutedHighLow ? L("Price · grey High/Low", "Цена · серые High/Low")
                : _priceDisplay == OrderFlowPriceDisplay.HighLow ? L("Price · High/Low lines", "Цена · линии High/Low") : L("Price · OHLC", "Цена · OHLC");
            DrawText(drawingContext, priceCaption, new Point(left, 3), ForegroundBrush, 11);
            if (deltaVisible)
            {
            DrawText(drawingContext, L("Bar delta · buy − sell volume", "Дельта свечи · объём покупок − продаж"), new Point(left, deltaTop - 17), ForegroundBrush, 11);
            DrawText(drawingContext, L("Window response · price change / |delta|", "Отклик окна · изменение цены / |дельта|"), new Point(left, responseTop - 17), ForegroundBrush, 11);

            }

            decimal minPrice = bars.Where(bar => bar.HasTrades).Min(bar => bar.Low);
            decimal maxPrice = bars.Where(bar => bar.HasTrades).Max(bar => bar.High);
            if (minPrice == maxPrice)
            {
                minPrice -= 1;
                maxPrice += 1;
            }

            decimal maxDelta = bars.Max(bar => Math.Abs(bar.Delta));
            decimal maxResponse = bars.Max(bar => Math.Abs(bar.PriceResponse));
            if (maxDelta == 0)
            {
                maxDelta = 1;
            }

            if (maxResponse == 0)
            {
                maxResponse = 1;
            }

            DrawPriceGrid(drawingContext, minPrice, maxPrice, left, right, priceTop, priceBottom);
            if (deltaVisible)
            {
                DrawScale(drawingContext, -maxDelta, maxDelta, deltaTop, deltaBottom);
                DrawScale(drawingContext, -maxResponse, maxResponse, responseTop, responseBottom);
            }
            double slotWidth = DataWidth / bars.Count;
            DrawTimeAxis(drawingContext, bars, left, right, slotWidth, priceTop, responseBottom);
            DrawPriceDisplay(drawingContext, bars, left, slotWidth, priceTop, priceBottom, minPrice, maxPrice);
            if (_showCloud && _result.CloudCalculated)
            {
                DrawClouds(drawingContext, bars, left, slotWidth, priceTop, priceBottom, minPrice, maxPrice, false);
            }
            if (_showCloud2 && _result.Cloud2Calculated)
            {
                DrawClouds(drawingContext, bars, left, slotWidth, priceTop, priceBottom, minPrice, maxPrice, true);
            }
            if (deltaVisible)
            {
                DrawDeltaBars(drawingContext, bars, left, slotWidth, deltaTop, deltaBottom, maxDelta);
                DrawResponse(drawingContext, bars, left, slotWidth, responseTop, responseBottom, maxResponse);
                DrawCandidates(drawingContext, bars, left, slotWidth, priceTop, priceBottom, minPrice, maxPrice);
            }

            DrawAnnotations(drawingContext, new Rect(left, priceTop, width, priceBottom - priceTop), minPrice, maxPrice);

            DrawText(drawingContext, OrderFlowChartTimeFrames.GetDisplayName(_timeFrame, OsLocalization.CurLocalization == OsLocalization.OsLocalType.Ru) + " · " + L("display", "отображение"), new Point(right - 135, 4),
                ForegroundBrush, 10);
        }

        private void DrawClouds(DrawingContext context, List<OrderFlowDisplayBar> bars, double left, double slotWidth,
            double top, double bottom, decimal minPrice, decimal maxPrice, bool secondLayer)
        {
            List<OrderFlowCloud> clouds = DisplayClouds(secondLayer);
            List<(Point Center, double Radius, OrderFlowCloud Cloud)> hits = secondLayer ? _cloudHits2 : _cloudHits;
            int low = 0;
            int high = clouds.Count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (clouds[middle].Time < bars[0].TimeStart) { low = middle + 1; }
                else { high = middle; }
            }
            int barIndex = 0;
            _cloudPlotBounds = new Rect(left, top, Math.Max(1, ActualWidth - 12 - left), bottom - top);
            context.PushClip(new RectangleGeometry(_cloudPlotBounds));
            for (int i = low; i < clouds.Count; i++)
            {
                OrderFlowCloud cloud = clouds[i];
                if (cloud.Time >= bars[bars.Count - 1].TimeEnd) { break; }
                while (barIndex < bars.Count && bars[barIndex].TimeEnd <= cloud.Time) { barIndex++; }
                if (barIndex == bars.Count || cloud.Time < bars[barIndex].TimeStart) { continue; }
                double fraction = (cloud.Time.Ticks - bars[barIndex].TimeStart.Ticks) /
                    (double)(bars[barIndex].TimeEnd.Ticks - bars[barIndex].TimeStart.Ticks);
                Point center = new Point(left + slotWidth * (barIndex + fraction), Scale(cloud.Price, minPrice, maxPrice, bottom, top));
                double radius = CloudVolumeRadius(cloud.Volume, secondLayer);
                Color color = cloud.BuyCount > cloud.SellCount ? Colors.LimeGreen : cloud.BuyCount < cloud.SellCount ? Colors.Tomato : Colors.SteelBlue;
                if (!cloud.ImbalancePassed) { color = Colors.Gray; }
                Brush fill = secondLayer ? Brushes.Transparent : new SolidColorBrush(Color.FromArgb(75, color.R, color.G, color.B));
                Pen outline = new Pen(cloud.CloudId == _selectedCloudId ? Brushes.Gold : new SolidColorBrush(color), secondLayer ? 2.5 : 1.3);
                if (cloud.CompletedAt == null) { outline.DashStyle = DashStyles.Dash; }
                if (cloud.TradeCount == 1) { context.DrawRectangle(fill, outline, new Rect(center.X - radius, center.Y - radius, radius * 2, radius * 2)); }
                else { context.DrawEllipse(fill, outline, center, radius, radius); }
                hits.Add((center, radius, cloud));
            }
            DrawCloudVolumeLabels(context, hits, secondLayer);
            context.Pop();
        }

        private void DrawTimeAxis(DrawingContext context, List<OrderFlowDisplayBar> bars, double left, double right,
            double slotWidth, double priceTop, double panelBottom)
        {
            Pen grid = new Pen(new SolidColorBrush(Color.FromArgb(55, 128, 128, 128)), 0.5);
            double axisTop = ActualHeight - TimeAxisHeight;
            context.DrawLine(new Pen(Brushes.Gray, 0.5), new Point(left, axisTop), new Point(right, axisTop));
            DrawText(context, L("Time", "Время"), new Point(8, axisTop + 12), ForegroundBrush, 10);
            List<OrderFlowChartTimeTick> ticks = OrderFlowChartNavigation.TimeTicks(bars, DataWidth);
            for (int i = 0; i < ticks.Count; i++)
            {
                double x = left + slotWidth * (ticks[i].BarIndex + 0.5);
                context.DrawLine(grid, new Point(x, priceTop), new Point(x, panelBottom));
                context.DrawLine(new Pen(Brushes.Gray, 1), new Point(x, axisTop), new Point(x, axisTop + 5));
                FormattedText label = new FormattedText(ticks[i].Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 10, ForegroundBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                context.DrawText(label, new Point(Math.Clamp(x - label.Width / 2, left, Math.Max(left, right - label.Width)), axisTop + 10));
            }
        }

        private int FindSelectedBarIndex(List<OrderFlowDisplayBar> bars)
        {
            if (_result == null || string.IsNullOrEmpty(_selectedCandidateId))
            {
                return -1;
            }

            OrderFlowCandidate selected = _result.Candidates.Find(
                candidate => candidate.CandidateId == _selectedCandidateId);
            if (selected == null)
            {
                return -1;
            }

            for (int i = 0; i < bars.Count; i++)
            {
                if (selected.Time >= bars[i].TimeStart && selected.Time < bars[i].TimeEnd)
                {
                    return i;
                }
            }

            return -1;
        }

        private static void DrawPanelBoundaries(DrawingContext drawingContext, double left, double right,
            double priceBottom, double deltaBottom, double responseBottom)
        {
            Pen separator = new Pen(Brushes.Gray, 0.5);
            drawingContext.DrawLine(separator, new Point(left, priceBottom), new Point(right, priceBottom));
            drawingContext.DrawLine(separator, new Point(left, deltaBottom), new Point(right, deltaBottom));
            drawingContext.DrawLine(separator, new Point(left, responseBottom), new Point(right, responseBottom));
        }

        private static void DrawPriceBars(DrawingContext drawingContext, List<OrderFlowDisplayBar> bars,
            double left, double slotWidth, double top, double bottom, decimal minPrice, decimal maxPrice)
        {
            for (int i = 0; i < bars.Count; i++)
            {
                OrderFlowDisplayBar bar = bars[i];
                if (bar.HasTrades == false)
                {
                    continue;
                }

                double x = left + slotWidth * i + slotWidth / 2;
                double highY = Scale(bar.High, minPrice, maxPrice, bottom, top);
                double lowY = Scale(bar.Low, minPrice, maxPrice, bottom, top);
                double openY = Scale(bar.Open, minPrice, maxPrice, bottom, top);
                double closeY = Scale(bar.Close, minPrice, maxPrice, bottom, top);
                Brush brush = bar.Close >= bar.Open ? Brushes.SeaGreen : Brushes.IndianRed;
                Pen pen = new Pen(brush, 1);
                drawingContext.DrawLine(pen, new Point(x, highY), new Point(x, lowY));

                double bodyWidth = Math.Max(1, Math.Min(8, slotWidth * 0.65));
                double bodyTop = Math.Min(openY, closeY);
                double bodyHeight = Math.Max(1, Math.Abs(closeY - openY));
                drawingContext.DrawRectangle(brush, null,
                    new Rect(x - bodyWidth / 2, bodyTop, bodyWidth, bodyHeight));
            }
        }

        private static void DrawDeltaBars(DrawingContext drawingContext, List<OrderFlowDisplayBar> bars,
            double left, double slotWidth, double top, double bottom, decimal maxAbsolute)
        {
            double zero = (top + bottom) / 2;
            drawingContext.DrawLine(new Pen(Brushes.Gray, 0.5), new Point(left, zero),
                new Point(left + slotWidth * bars.Count, zero));

            for (int i = 0; i < bars.Count; i++)
            {
                decimal normalized = bars[i].Delta / maxAbsolute;
                double valueY = zero - Convert.ToDouble(normalized) * (bottom - top) / 2;
                Brush brush = bars[i].Delta >= 0 ? Brushes.SeaGreen : Brushes.IndianRed;
                double x = left + slotWidth * i + slotWidth * 0.2;
                drawingContext.DrawRectangle(brush, null,
                    new Rect(x, Math.Min(zero, valueY), Math.Max(1, slotWidth * 0.6), Math.Max(1, Math.Abs(valueY - zero))));
            }
        }

        private static void DrawResponse(DrawingContext drawingContext, List<OrderFlowDisplayBar> bars,
            double left, double slotWidth, double top, double bottom, decimal maxAbsolute)
        {
            double zero = (top + bottom) / 2;
            Point? previous = null;
            Pen line = new Pen(Brushes.DodgerBlue, 1.2);

            for (int i = 0; i < bars.Count; i++)
            {
                decimal normalized = bars[i].PriceResponse / maxAbsolute;
                double y = zero - Convert.ToDouble(normalized) * (bottom - top) / 2;
                Point current = new Point(left + slotWidth * i + slotWidth / 2, y);

                if (previous.HasValue)
                {
                    drawingContext.DrawLine(line, previous.Value, current);
                }

                previous = current;
            }
        }

        private void DrawCandidates(DrawingContext drawingContext, List<OrderFlowDisplayBar> bars,
            double left, double slotWidth, double top, double bottom, decimal minPrice, decimal maxPrice)
        {
            DateTime startTime = bars[0].TimeStart;
            DateTime endTime = bars[bars.Count - 1].TimeEnd;

            int barCursor = 0;
            for (int i = 0; i < _result.Candidates.Count; i++)
            {
                OrderFlowCandidate candidate = _result.Candidates[i];
                if (candidate.Time < startTime || candidate.Time >= endTime)
                {
                    continue;
                }

                while (barCursor < bars.Count && bars[barCursor].TimeEnd <= candidate.Time) { barCursor++; }
                int barIndex = barCursor < bars.Count && candidate.Time >= bars[barCursor].TimeStart ? barCursor : -1;

                if (barIndex < 0)
                {
                    continue;
                }

                double x = left + slotWidth * barIndex + slotWidth / 2;
                double y = Scale(candidate.ReferencePrice, minPrice, maxPrice, bottom, top);
                bool selected = candidate.CandidateId == _selectedCandidateId;
                Brush brush = GetCandidateBrush(candidate.CandidateId);
                Pen pen = new Pen(selected ? Brushes.Gold : brush, selected ? 2.5 : 1.2);
                StreamGeometry geometry = new StreamGeometry();

                using (StreamGeometryContext geometryContext = geometry.Open())
                {
                    if (candidate.Direction == OrderFlowDirection.Long)
                    {
                        geometryContext.BeginFigure(new Point(x, y - 7), true, true);
                        geometryContext.LineTo(new Point(x - 6, y + 5), true, false);
                        geometryContext.LineTo(new Point(x + 6, y + 5), true, false);
                    }
                    else
                    {
                        geometryContext.BeginFigure(new Point(x, y + 7), true, true);
                        geometryContext.LineTo(new Point(x - 6, y - 5), true, false);
                        geometryContext.LineTo(new Point(x + 6, y - 5), true, false);
                    }
                }

                geometry.Freeze();
                drawingContext.DrawGeometry(brush, pen, geometry);
            }
        }

        private Brush GetCandidateBrush(string candidateId)
        {
            _shortestLabels.TryGetValue(candidateId, out OrderFlowMarketPathLabel label);

            if (label == null || label.Outcome == OrderFlowBarrierOutcome.Incomplete ||
                label.Outcome == OrderFlowBarrierOutcome.NoFutureTrade)
            {
                return Brushes.SlateGray;
            }

            if (label.Outcome == OrderFlowBarrierOutcome.TargetFirst)
            {
                return Brushes.LimeGreen;
            }

            if (label.Outcome == OrderFlowBarrierOutcome.InvalidationFirst)
            {
                return Brushes.OrangeRed;
            }

            if (label.Outcome == OrderFlowBarrierOutcome.AmbiguousSameTimestamp)
            {
                return Brushes.Goldenrod;
            }

            return Brushes.SteelBlue;
        }

        private Brush BackgroundBrush { get { return TryFindResource("ControlBackgroundNormalLight") as Brush ?? SystemColors.WindowBrush; } }
        private Brush ForegroundBrush { get { return TryFindResource("ControlForegroundWhite") as Brush ?? SystemColors.WindowTextBrush; } }

        private static string L(string english, string russian)
        {
            return OsLocalization.CurLocalization == OsLocalization.OsLocalType.Ru ? russian : english;
        }

        private static string F(decimal value)
        {
            return value.ToString("0.############################", CultureInfo.InvariantCulture);
        }

        private void DrawScale(DrawingContext context, decimal minimum, decimal maximum, double top, double bottom)
        {
            DrawText(context, maximum.ToString("G6", CultureInfo.InvariantCulture), new Point(4, top - 5), ForegroundBrush, 10);
            DrawText(context, minimum.ToString("G6", CultureInfo.InvariantCulture), new Point(4, bottom - 10), ForegroundBrush, 10);
        }

        private static double Scale(decimal value, decimal minimum, decimal maximum,
            double outputMinimum, double outputMaximum)
        {
            decimal normalized = (value - minimum) / (maximum - minimum);
            return outputMinimum + Convert.ToDouble(normalized) * (outputMaximum - outputMinimum);
        }

        private void DrawText(DrawingContext drawingContext, string text, Point point, Brush brush, double size)
        {
            FormattedText formattedText = new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                size,
                brush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            drawingContext.DrawText(formattedText, point);
        }
        #endregion
    }
}
