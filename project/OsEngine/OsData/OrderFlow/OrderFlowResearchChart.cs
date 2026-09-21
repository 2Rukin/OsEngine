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
    /// subscribes to ViewChanged and unsubscribes when closing. Contract: ORDER-FLOW-RESEARCH-001.
    /// </remarks>
    internal sealed class OrderFlowResearchChart : FrameworkElement
    {
        private OrderFlowResearchResult _result;
        private OrderFlowDisplayTimeFrame _timeFrame = OrderFlowDisplayTimeFrame.Min1;
        private string _selectedCandidateId;

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
                return _result != null && _result.Bars.TryGetValue(_timeFrame, out bars)
                    ? bars : new List<OrderFlowDisplayBar>();
            }
        }

        public string RangeText
        {
            get
            {
                List<OrderFlowDisplayBar> bars = CurrentBars;
                if (bars.Count == 0) { return L("No trade bars", "Нет свечей со сделками"); }
                return bars[_startIndex].TimeStart.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture) +
                    " — " + bars[_startIndex + VisibleCount - 1].TimeEnd.ToString("HH:mm:ss", CultureInfo.InvariantCulture) +
                    " · " + L("bars", "свечи") + " " + (_startIndex + 1) + "–" + (_startIndex + VisibleCount) +
                    " / " + bars.Count + " · " + L("Entire file", "Весь файл") + " " +
                    bars[0].TimeStart.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "–" +
                    bars[bars.Count - 1].TimeEnd.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            }
        }

        public OrderFlowResearchChart()
        {
            SnapsToDevicePixels = true;
            MinHeight = 320;
        }

        /// <summary>Resets the display viewport for a new offline result without changing research data.</summary>
        /// <param name="result">Offline result to display, or null to clear the viewport.</param>
        public void SetResult(OrderFlowResearchResult result)
        {
            _result = result;
            _selectedCandidateId = null;
            _visibleCount = 120;
            ScrollTo(Math.Max(0, TotalBars - _visibleCount));
        }

        /// <summary>Changes display bars, retaining the visible midpoint time when possible.</summary>
        /// <param name="timeFrame">Precomputed display aggregation.</param>
        public void SetTimeFrame(OrderFlowDisplayTimeFrame timeFrame)
        {
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
            InvalidateVisual();
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Changes the number of visible bars around the current midpoint; the full file is supported.</summary>
        /// <param name="count">Requested viewport width in bars, clamped to the available range.</param>
        public void Zoom(int count)
        {
            int center = _startIndex + VisibleCount / 2;
            _visibleCount = Math.Clamp(count, Math.Min(10, Math.Max(1, TotalBars)), Math.Max(1, TotalBars));
            ScrollTo(center - VisibleCount / 2);
        }

        /// <summary>Scrolls through existing bars without changing the research result.</summary>
        /// <param name="e">Mouse wheel input handled by this chart.</param>
        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            try
            {
                ScrollTo(_startIndex + (e.Delta > 0 ? -1 : 1) * Math.Max(1, VisibleCount / 10));
                e.Handled = true;
            }
            catch (Exception error)
            {
                ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error);
            }
        }

        /// <summary>Shows the display bar under the pointer, including its book quality.</summary>
        /// <param name="e">Pointer coordinates in the chart.</param>
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            try
            {
                double position = (e.GetPosition(this).X - 92) / Math.Max(1, ActualWidth - 104);
                if (position < 0 || position >= 1 || VisibleCount == 0) { ToolTip = null; return; }
                OrderFlowDisplayBar bar = CurrentBars[_startIndex + Math.Min(VisibleCount - 1, (int)(position * VisibleCount))];
                ToolTip = bar.TimeStart.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture) +
                    "\nO " + F(bar.Open) + " · H " + F(bar.High) + " · L " + F(bar.Low) + " · C " + F(bar.Close) +
                    "\n" + L("Bar delta", "Дельта свечи") + " " + F(bar.Delta) +
                    " · " + L("Window response", "Отклик окна") + " " + F(bar.PriceResponse) +
                    "\n" + L("Book imbalance", "Дисбаланс стакана") + " " + (bar.BookAvailable ? F(bar.BookImbalance) : L("missing", "нет данных")) +
                    (bar.BookStale ? " · " + L("stale", "устарел") : "");
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
            Rect bounds = new Rect(0, 0, ActualWidth, ActualHeight);
            drawingContext.DrawRectangle(BackgroundBrush, null, bounds);

            if (_result == null || ActualWidth < 200 || ActualHeight < 200)
            {
                DrawText(drawingContext, L("Run paired QSH research to display diagnostic bars.", "Запустите расчёт по паре QSH для просмотра графика."),
                    new Point(12, 12), ForegroundBrush, 13);
                return;
            }

            List<OrderFlowDisplayBar> allBars;
            if (_result.Bars.TryGetValue(_timeFrame, out allBars) == false || allBars.Count == 0)
            {
                DrawText(drawingContext, L("No trade bars are available for ", "Нет свечей со сделками для ") + _timeFrame + ".",
                    new Point(12, 12), ForegroundBrush, 13);
                return;
            }

            List<OrderFlowDisplayBar> bars = allBars.GetRange(_startIndex, VisibleCount);
            double left = 92;
            double right = Math.Max(left + 10, ActualWidth - 12);
            double width = right - left;
            double usable = ActualHeight - 96;
            double priceTop = 24;
            double priceBottom = priceTop + usable * 0.46;
            double deltaTop = priceBottom + 20;
            double deltaBottom = deltaTop + usable * 0.18;
            double responseTop = deltaBottom + 20;
            double responseBottom = responseTop + usable * 0.18;
            double bookTop = responseBottom + 20;
            double bookBottom = bookTop + usable * 0.18;

            DrawPanelBoundaries(drawingContext, left, right, priceBottom, deltaBottom, responseBottom, bookBottom);
            DrawText(drawingContext, L("Price · OHLC", "Цена · OHLC"), new Point(left, 3), ForegroundBrush, 11);
            DrawText(drawingContext, L("Bar delta · buy − sell volume", "Дельта свечи · объём покупок − продаж"), new Point(left, deltaTop - 17), ForegroundBrush, 11);
            DrawText(drawingContext, L("Window response · price change / |delta|", "Отклик окна · изменение цены / |дельта|"), new Point(left, responseTop - 17), ForegroundBrush, 11);
            DrawText(drawingContext, L("Book imbalance · + bids / − asks", "Дисбаланс стакана · + покупатели / − продавцы"), new Point(left, bookTop - 17), ForegroundBrush, 11);

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

            DrawScale(drawingContext, minPrice, maxPrice, priceTop, priceBottom);
            DrawScale(drawingContext, -maxDelta, maxDelta, deltaTop, deltaBottom);
            DrawScale(drawingContext, -maxResponse, maxResponse, responseTop, responseBottom);
            DrawScale(drawingContext, -1, 1, bookTop, bookBottom);
            double slotWidth = width / bars.Count;
            DrawPriceBars(drawingContext, bars, left, slotWidth, priceTop, priceBottom, minPrice, maxPrice);
            DrawDeltaBars(drawingContext, bars, left, slotWidth, deltaTop, deltaBottom, maxDelta);
            DrawResponse(drawingContext, bars, left, slotWidth, responseTop, responseBottom, maxResponse);
            DrawBook(drawingContext, bars, left, slotWidth, bookTop, bookBottom);
            DrawCandidates(drawingContext, bars, left, slotWidth, priceTop, priceBottom, minPrice, maxPrice);

            DrawText(drawingContext, bars[0].TimeStart.ToString("dd.MM HH:mm:ss", CultureInfo.InvariantCulture),
                new Point(left, ActualHeight - 18), ForegroundBrush, 10);
            DrawText(drawingContext, bars[bars.Count - 1].TimeStart.ToString("dd.MM HH:mm:ss", CultureInfo.InvariantCulture),
                new Point(Math.Max(left, right - 105), ActualHeight - 18), ForegroundBrush, 10);
            DrawText(drawingContext, _timeFrame + " · " + L("display", "отображение"), new Point(right - 135, 4),
                ForegroundBrush, 10);
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
            double priceBottom, double deltaBottom, double responseBottom, double bookBottom)
        {
            Pen separator = new Pen(Brushes.Gray, 0.5);
            drawingContext.DrawLine(separator, new Point(left, priceBottom), new Point(right, priceBottom));
            drawingContext.DrawLine(separator, new Point(left, deltaBottom), new Point(right, deltaBottom));
            drawingContext.DrawLine(separator, new Point(left, responseBottom), new Point(right, responseBottom));
            drawingContext.DrawLine(separator, new Point(left, bookBottom), new Point(right, bookBottom));
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

        private static void DrawBook(DrawingContext drawingContext, List<OrderFlowDisplayBar> bars,
            double left, double slotWidth, double top, double bottom)
        {
            Point? previous = null;
            Pen line = new Pen(Brushes.MediumPurple, 1.2);

            for (int i = 0; i < bars.Count; i++)
            {
                OrderFlowDisplayBar bar = bars[i];
                double x = left + slotWidth * i + slotWidth / 2;

                if (bar.BookAvailable == false || bar.BookStale)
                {
                    Brush warning = new SolidColorBrush(Color.FromArgb(42, 220, 80, 60));
                    drawingContext.DrawRectangle(warning, null,
                        new Rect(left + slotWidth * i, top, Math.Max(1, slotWidth), bottom - top));
                }

                if (bar.BookAvailable == false) { previous = null; continue; }
                double normalized = Math.Max(-1, Math.Min(1, Convert.ToDouble(bar.BookImbalance)));
                double y = (top + bottom) / 2 - normalized * (bottom - top) / 2;
                Point current = new Point(x, y);

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

            for (int i = 0; i < _result.Candidates.Count; i++)
            {
                OrderFlowCandidate candidate = _result.Candidates[i];
                if (candidate.Time < startTime || candidate.Time >= endTime)
                {
                    continue;
                }

                int barIndex = -1;
                for (int barIndexCandidate = 0; barIndexCandidate < bars.Count; barIndexCandidate++)
                {
                    if (candidate.Time >= bars[barIndexCandidate].TimeStart &&
                        candidate.Time < bars[barIndexCandidate].TimeEnd)
                    {
                        barIndex = barIndexCandidate;
                        break;
                    }
                }

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
            OrderFlowMarketPathLabel label = _result.Labels
                .Where(item => item.CandidateId == candidateId)
                .OrderBy(item => item.HorizonSeconds)
                .FirstOrDefault();

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
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }

        private void DrawScale(DrawingContext context, decimal minimum, decimal maximum, double top, double bottom)
        {
            DrawText(context, F(maximum), new Point(4, top - 5), ForegroundBrush, 10);
            DrawText(context, F(minimum), new Point(4, bottom - 10), ForegroundBrush, 10);
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
    }
}
