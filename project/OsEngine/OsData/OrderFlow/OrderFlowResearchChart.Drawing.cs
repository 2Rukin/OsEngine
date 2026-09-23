/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.Logging;
using OsEngine.Market;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace OsEngine.OsData.OrderFlow
{
    internal sealed partial class OrderFlowResearchChart
    {
        #region Annotation state

        private readonly List<OrderFlowChartLine> _drawings = new List<OrderFlowChartLine>();
        private readonly List<(OrderFlowChartLine Line, Point From, Point To)> _lineHits = new List<(OrderFlowChartLine, Point, Point)>();
        private Rect _drawingPlot = Rect.Empty;
        private Size _drawingSize;
        private decimal _drawingMin;
        private decimal _drawingMax;
        private OrderFlowDrawingAnchor? _pendingAnchor;
        private OrderFlowDrawingAnchor _previewAnchor;
        private int _movingAnchor;
        private Point _movingOrigin;
        private OrderFlowDrawingAnchor _movingFirst;
        private OrderFlowDrawingAnchor _movingSecond;
        private Color _lineColor = Colors.Gold;
        private double _lineThickness = 2;
        private bool _extendLeft;
        private bool _extendRight;

        /// <summary>UI-thread notification for tool/selection/style changes; the owning workbench unsubscribes on close.</summary>
        public event EventHandler DrawingChanged;

        internal IReadOnlyList<OrderFlowChartLine> Drawings => _drawings;
        internal OrderFlowChartLine SelectedDrawing { get; private set; }
        internal OrderFlowDrawingTool DrawingTool { get; private set; }
        internal Color DrawingColor => SelectedDrawing?.Color ?? _lineColor;
        internal double DrawingThickness => SelectedDrawing?.Thickness ?? _lineThickness;
        internal bool DrawingExtendLeft => SelectedDrawing?.ExtendLeft ?? _extendLeft;
        internal bool DrawingExtendRight => SelectedDrawing?.ExtendRight ?? _extendRight;
        internal bool HasPendingAnchor => _pendingAnchor.HasValue;

        /// <summary>Selects a persistent two-click drawing tool, or selection/panning. Cancels any unfinished line or drag.</summary>
        public void SetDrawingTool(OrderFlowDrawingTool tool)
        {
            if (!Enum.IsDefined(tool)) { throw new ArgumentOutOfRangeException(nameof(tool)); }
            EndDrag();
            _pendingAnchor = null;
            DrawingTool = tool;
            if (tool != OrderFlowDrawingTool.Select) { SelectedDrawing = null; }
            NotifyDrawingChanged();
        }

        /// <summary>Applies an opaque color, 1–6 device-independent pixel width and time-direction extensions to selection and future lines.</summary>
        public void SetDrawingStyle(Color color, double thickness, bool extendLeft, bool extendRight)
        {
            if (!double.IsFinite(thickness) || thickness < 1 || thickness > 6) { throw new ArgumentOutOfRangeException(nameof(thickness)); }
            _lineColor = Color.FromRgb(color.R, color.G, color.B);
            _lineThickness = thickness;
            _extendLeft = extendLeft;
            _extendRight = extendRight;
            if (SelectedDrawing != null) { ApplyLineStyle(SelectedDrawing); }
            NotifyDrawingChanged();
        }

        /// <summary>Deletes only the selected temporary annotation; research data and export remain untouched.</summary>
        public void DeleteSelectedDrawing()
        {
            if (SelectedDrawing == null) { return; }
            _drawings.Remove(SelectedDrawing);
            SelectedDrawing = null;
            EndDrag();
            NotifyDrawingChanged();
        }

        /// <summary>Clears all temporary annotations and unfinished input, including when a new result is installed.</summary>
        public void ClearDrawings()
        {
            _drawings.Clear();
            SelectedDrawing = null;
            CancelDrawingGesture();
        }

        /// <summary>Cancels transient pointer input while retaining the last explicitly selected drawing tool.</summary>
        public void CancelDrawingGesture()
        {
            EndDrag();
            _pendingAnchor = null;
            NotifyDrawingChanged();
        }

        private void ApplyLineStyle(OrderFlowChartLine line)
        {
            line.Color = _lineColor;
            line.Thickness = _lineThickness;
            line.ExtendLeft = _extendLeft;
            line.ExtendRight = _extendRight;
        }

        private void NotifyDrawingChanged()
        {
            _lineHits.Clear();
            ToolTip = null;
            InvalidateVisual();
            DrawingChanged?.Invoke(this, EventArgs.Empty);
        }

        #endregion

        #region Pointer interaction

        internal bool DrawingPointerDown(Point point)
        {
            if (_drawingPlot.IsEmpty || !_drawingPlot.Contains(point)) { return false; }
            OrderFlowDrawingAnchor anchor = AnchorAt(point);
            if (!_pendingAnchor.HasValue)
            {
                if (SelectedDrawing != null)
                {
                    if ((ProjectAnchor(SelectedDrawing.First) - point).Length <= 8) { _movingAnchor = 1; return true; }
                    if ((ProjectAnchor(SelectedDrawing.Second) - point).Length <= 8) { _movingAnchor = 2; return true; }
                }
                OrderFlowChartLine hit = DrawingAt(point);
                if (hit != null)
                {
                    SelectedDrawing = hit;
                    _movingAnchor = 3;
                    _movingOrigin = point;
                    _movingFirst = hit.First;
                    _movingSecond = hit.Second;
                    NotifyDrawingChanged();
                    return true;
                }
            }
            if (DrawingTool != OrderFlowDrawingTool.Select)
            {
                if (!_pendingAnchor.HasValue) { _pendingAnchor = _previewAnchor = anchor; }
                else if (anchor.Time != _pendingAnchor.Value.Time)
                {
                    OrderFlowChartLine line = new OrderFlowChartLine { Kind = DrawingTool, First = _pendingAnchor.Value,
                        Second = DrawingTool == OrderFlowDrawingTool.Horizontal ? anchor with { Price = _pendingAnchor.Value.Price } : anchor };
                    ApplyLineStyle(line);
                    _drawings.Add(line);
                    SelectedDrawing = line;
                    _pendingAnchor = null;
                }
                NotifyDrawingChanged();
                return true;
            }
            SelectedDrawing = null;
            NotifyDrawingChanged();
            return false;
        }

        internal bool DrawingPointerMove(Point point)
        {
            if (_drawingPlot.IsEmpty) { return false; }
            if (_movingAnchor != 0 && SelectedDrawing != null)
            {
                if (_movingAnchor == 3)
                {
                    double offset = (Math.Clamp(point.X, _drawingPlot.Left, _drawingPlot.Right) - _movingOrigin.X) * VisibleCount / _drawingDataPlot.Width;
                    decimal priceOffset = (decimal)((_movingOrigin.Y - Math.Clamp(point.Y, _drawingPlot.Top, _drawingPlot.Bottom)) / _drawingPlot.Height) * (_drawingMax - _drawingMin);
                    (SelectedDrawing.First, SelectedDrawing.Second) = OrderFlowDrawingGeometry.Translate(CurrentBars, _movingFirst, _movingSecond, offset, priceOffset);
                    NotifyDrawingChanged();
                    return true;
                }
                OrderFlowDrawingAnchor anchor = AnchorAt(point);
                OrderFlowDrawingAnchor other = _movingAnchor == 1 ? SelectedDrawing.Second : SelectedDrawing.First;
                if (anchor.Time == other.Time) { return true; }
                if (_movingAnchor == 1) { SelectedDrawing.First = anchor; }
                else { SelectedDrawing.Second = anchor; }
                if (SelectedDrawing.Kind == OrderFlowDrawingTool.Horizontal)
                {
                    SelectedDrawing.First = SelectedDrawing.First with { Price = anchor.Price };
                    SelectedDrawing.Second = SelectedDrawing.Second with { Price = anchor.Price };
                }
                NotifyDrawingChanged();
                return true;
            }
            if (_pendingAnchor.HasValue)
            {
                _previewAnchor = AnchorAt(point);
                InvalidateVisual();
                return true;
            }
            return false;
        }

        internal OrderFlowChartLine DrawingAt(Point point)
        {
            if (_drawingPlot.IsEmpty || !_drawingPlot.Contains(point)) { return null; }
            for (int i = _lineHits.Count - 1; i >= 0; i--)
            {
                (OrderFlowChartLine Line, Point From, Point To) hit = _lineHits[i];
                if (OrderFlowDrawingGeometry.Distance(point, hit.From, hit.To) <= Math.Max(5, hit.Line.Thickness / 2 + 2)) { return hit.Line; }
            }
            return null;
        }

        private OrderFlowDrawingAnchor AnchorAt(Point point)
        {
            double position = _startIndex + (Math.Clamp(point.X, _drawingPlot.Left, _drawingPlot.Right) - _drawingDataPlot.Left) / _drawingDataPlot.Width * VisibleCount;
            decimal fraction = (decimal)Math.Clamp((_drawingPlot.Bottom - point.Y) / _drawingPlot.Height, 0, 1);
            return new OrderFlowDrawingAnchor(OrderFlowDrawingGeometry.TimeAt(CurrentBars, position), _drawingMin + fraction * (_drawingMax - _drawingMin));
        }

        private Point ProjectAnchor(OrderFlowDrawingAnchor anchor)
        {
            return OrderFlowDrawingGeometry.Project(CurrentBars, _startIndex, VisibleCount, _drawingDataPlot, _drawingMin, _drawingMax, anchor);
        }

        /// <summary>Escape cancels unfinished input; Delete removes the selected annotation while this chart has keyboard focus.</summary>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            try
            {
                if (e.Key == Key.Escape) { SetDrawingTool(OrderFlowDrawingTool.Select); e.Handled = true; }
                else if (e.Key == Key.Delete) { DeleteSelectedDrawing(); e.Handled = true; }
            }
            catch (Exception error) { ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error); }
        }

        /// <summary>Discards pointer geometry when layout changes; the next render rebuilds it from source anchors.</summary>
        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            if (_drawingSize != sizeInfo.NewSize)
            {
                _drawingPlot = Rect.Empty;
                _lineHits.Clear();
            }
        }

        #endregion

        #region Drawing

        private void DrawAnnotations(DrawingContext context, Rect plot, decimal min, decimal max)
        {
            _drawingPlot = plot;
            _drawingDataPlot = new Rect(plot.Left, plot.Top, plot.Width * (1 - _rightPaddingPercent / 100), plot.Height);
            _drawingSize = new Size(ActualWidth, ActualHeight);
            _drawingMin = min;
            _drawingMax = max;
            context.PushClip(new RectangleGeometry(plot));
            foreach (OrderFlowChartLine line in _drawings)
            {
                Point first = ProjectAnchor(line.First);
                Point second = ProjectAnchor(line.Second);
                if (OrderFlowDrawingGeometry.Clip(first, second, line.ExtendLeft, line.ExtendRight, plot, out Point from, out Point to))
                {
                    Brush brush = new SolidColorBrush(line.Color);
                    context.DrawLine(new Pen(brush, line.Thickness), from, to);
                    if ((to - from).Length < 1) { context.DrawEllipse(brush, null, from, line.Thickness / 2, line.Thickness / 2); }
                    _lineHits.Add((line, from, to));
                }
                if (line == SelectedDrawing)
                {
                    Pen handle = new Pen(new SolidColorBrush(line.Color), 1.5);
                    context.DrawEllipse(BackgroundBrush, handle, first, 5, 5);
                    context.DrawEllipse(BackgroundBrush, handle, second, 5, 5);
                }
            }
            if (_pendingAnchor.HasValue)
            {
                OrderFlowDrawingAnchor preview = DrawingTool == OrderFlowDrawingTool.Horizontal
                    ? _previewAnchor with { Price = _pendingAnchor.Value.Price } : _previewAnchor;
                Pen pen = new Pen(new SolidColorBrush(_lineColor), _lineThickness) { DashStyle = DashStyles.Dash };
                Point first = ProjectAnchor(_pendingAnchor.Value);
                if (OrderFlowDrawingGeometry.Clip(first, ProjectAnchor(preview), _extendLeft, _extendRight, plot, out Point from, out Point to))
                { context.DrawLine(pen, from, to); }
                context.DrawEllipse(null, pen, first, 4, 4);
            }
            context.Pop();
        }

        #endregion
    }
}
