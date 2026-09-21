/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using OsEngine.Logging;
using OsEngine.Market;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace OsEngine.OsData.OrderFlow
{
    /// <summary>
    /// Diagnostic-only presenter for research bars and candidate markers.
    /// </summary>
    /// <remarks>
    /// Changing the display timeframe or selected marker never recalculates
    /// features, candidates or labels. Contract: ORDER-FLOW-RESEARCH-001.
    /// </remarks>
    internal sealed class OrderFlowResearchChart : FrameworkElement
    {
        private OrderFlowResearchResult _result;
        private OrderFlowDisplayTimeFrame _timeFrame = OrderFlowDisplayTimeFrame.Min1;
        private string _selectedCandidateId;

        public OrderFlowResearchChart()
        {
            SnapsToDevicePixels = true;
            MinHeight = 320;
        }

        public void SetResult(OrderFlowResearchResult result)
        {
            _result = result;
            InvalidateVisual();
        }

        public void SetTimeFrame(OrderFlowDisplayTimeFrame timeFrame)
        {
            _timeFrame = timeFrame;
            InvalidateVisual();
        }

        public void SelectCandidate(string candidateId)
        {
            _selectedCandidateId = candidateId;
            InvalidateVisual();
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
            drawingContext.DrawRectangle(SystemColors.WindowBrush, null, bounds);

            if (_result == null || ActualWidth < 200 || ActualHeight < 200)
            {
                DrawText(drawingContext, "Run paired QSH research to display diagnostic bars.",
                    new Point(12, 12), SystemColors.WindowTextBrush, 13);
                return;
            }

            List<OrderFlowDisplayBar> allBars;
            if (_result.Bars.TryGetValue(_timeFrame, out allBars) == false || allBars.Count == 0)
            {
                DrawText(drawingContext, "No trade bars are available for " + _timeFrame + ".",
                    new Point(12, 12), SystemColors.WindowTextBrush, 13);
                return;
            }

            int selectedIndex = FindSelectedBarIndex(allBars);
            int startIndex = selectedIndex >= 0
                ? Math.Max(0, selectedIndex - 60)
                : Math.Max(0, allBars.Count - 120);
            int endIndex = Math.Min(allBars.Count - 1, startIndex + 119);

            if (selectedIndex >= 0 && selectedIndex > endIndex)
            {
                startIndex = Math.Max(0, selectedIndex - 119);
                endIndex = selectedIndex;
            }

            List<OrderFlowDisplayBar> bars = allBars.GetRange(startIndex, endIndex - startIndex + 1);
            double left = 70;
            double right = Math.Max(left + 10, ActualWidth - 12);
            double width = right - left;
            double priceTop = 24;
            double priceBottom = ActualHeight * 0.55;
            double deltaTop = priceBottom + 18;
            double deltaBottom = ActualHeight * 0.72;
            double responseTop = deltaBottom + 18;
            double responseBottom = ActualHeight * 0.85;
            double bookTop = responseBottom + 18;
            double bookBottom = ActualHeight - 24;

            DrawPanelBoundaries(drawingContext, left, right, priceBottom, deltaBottom, responseBottom, bookBottom);
            DrawText(drawingContext, "Price", new Point(8, priceTop), SystemColors.WindowTextBrush, 11);
            DrawText(drawingContext, "Delta", new Point(8, deltaTop), SystemColors.WindowTextBrush, 11);
            DrawText(drawingContext, "Response", new Point(8, responseTop), SystemColors.WindowTextBrush, 11);
            DrawText(drawingContext, "Book", new Point(8, bookTop), SystemColors.WindowTextBrush, 11);

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

            double slotWidth = width / bars.Count;
            DrawPriceBars(drawingContext, bars, left, slotWidth, priceTop, priceBottom, minPrice, maxPrice);
            DrawDeltaBars(drawingContext, bars, left, slotWidth, deltaTop, deltaBottom, maxDelta);
            DrawResponse(drawingContext, bars, left, slotWidth, responseTop, responseBottom, maxResponse);
            DrawBook(drawingContext, bars, left, slotWidth, bookTop, bookBottom);
            DrawCandidates(drawingContext, bars, left, slotWidth, priceTop, priceBottom, minPrice, maxPrice);

            DrawText(drawingContext, bars[0].TimeStart.ToString("dd.MM HH:mm:ss", CultureInfo.InvariantCulture),
                new Point(left, ActualHeight - 18), SystemColors.WindowTextBrush, 10);
            DrawText(drawingContext, bars[bars.Count - 1].TimeStart.ToString("dd.MM HH:mm:ss", CultureInfo.InvariantCulture),
                new Point(Math.Max(left, right - 105), ActualHeight - 18), SystemColors.WindowTextBrush, 10);
            DrawText(drawingContext, _timeFrame + " · display only", new Point(right - 135, 4),
                Brushes.DimGray, 10);
        }

        private int FindSelectedBarIndex(List<OrderFlowDisplayBar> bars)
        {
            if (string.IsNullOrEmpty(_selectedCandidateId))
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
