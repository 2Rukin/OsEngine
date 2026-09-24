/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.Logging;
using OsEngine.Market;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace OsEngine.OsData.OrderFlow.Explorer
{
    /// <summary>Bounded page presenter using the same saved filter verdict as the table. No file IO or detector state lives in the renderer.</summary>
    internal sealed class ExplorerChart : FrameworkElement
    {
        private IReadOnlyList<ExplorerCloud> _clouds = Array.Empty<ExplorerCloud>();
        private IReadOnlyList<ExplorerPivot> _pivots = Array.Empty<ExplorerPivot>();
        private IReadOnlyList<ExplorerObservation> _observations = Array.Empty<ExplorerObservation>();
        private IReadOnlyList<ExplorerVwapSample> _vwap = Array.Empty<ExplorerVwapSample>();
        private IReadOnlyList<ExplorerBar> _bars = Array.Empty<ExplorerBar>();
        private IReadOnlyList<ExplorerEpisode> _episodes = Array.Empty<ExplorerEpisode>();
        private readonly List<(Point Point, ExplorerCloud Cloud)> _hits = new List<(Point, ExplorerCloud)>();
        private readonly List<(Point Point, ExplorerObservation Observation)> _observationHits = new List<(Point, ExplorerObservation)>();
        private readonly List<(DateTime ATime, decimal APrice, DateTime BTime, decimal BPrice)> _drawings = new List<(DateTime, decimal, DateTime, decimal)>();
        private (DateTime Time, decimal Price)? _drawingStart;
        private int _dragLine = -1;
        private Point _dragOrigin;
        private (DateTime ATime, decimal APrice, DateTime BTime, decimal BPrice) _dragOriginal;
        private ExplorerObservation _selectedObservation;
        private readonly HashSet<string> _highlight = new HashSet<string>();
        private ExplorerView _view = new ExplorerView();
        private decimal _step = 1, _low, _high;
        private DateTime _from, _to;
        private string _selected;
        private bool _bands = true;
        private double _zoom = 1;
        private double _pan;
        private DateTime? _intervalFrom, _intervalTo;
        private decimal _priceZoom = 1, _pricePan;
        internal DateTime? KnownBoundary { get; set; }
        internal (DateTime From, DateTime To, decimal Low, decimal High) Viewport => (_from, _to, _low, _high);
        internal void SetInterval(DateTime? from, DateTime? to)
        {
            if (_intervalFrom != from || _intervalTo != to) { _zoom = 1; _pan = 0; FitPrice(); }
            _intervalFrom = from; _intervalTo = to; InvalidateVisual();
        }
        internal void FitPrice() { _priceZoom = 1; _pricePan = 0; InvalidateVisual(); }
        internal void ResetAxes() { _zoom = 1; _pan = 0; FitPrice(); }
        internal void PriceAxis(decimal zoom, decimal pan) { _priceZoom = Math.Clamp(_priceZoom * zoom, .01m, 1000m); _pricePan += pan / _priceZoom; InvalidateVisual(); }
        internal bool Drawing { get; set; }
        internal int PriceStyle { get; set; }
        internal event Action<ExplorerCloud> Selected;
        internal event Action<ExplorerObservation> ObservationSelected;
        internal event Action<Exception> Failed;
        private void Report(Exception error)
        { if (Failed != null) { Failed(error); } else { ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.System); } }
        #region Presentation

        internal ExplorerChart() { Focusable = true; ClipToBounds = true; }
        internal void Set(IReadOnlyList<ExplorerCloud> clouds, ExplorerView view, decimal step, IReadOnlyList<ExplorerVwapSample> vwap,
            IReadOnlyList<ExplorerPivot> pivots = null, IReadOnlyList<ExplorerObservation> observations = null, bool bands = true)
        {
            _clouds = clouds; _view = view; _step = step; _vwap = vwap ?? Array.Empty<ExplorerVwapSample>();
            _pivots = pivots ?? Array.Empty<ExplorerPivot>(); _observations = observations ?? Array.Empty<ExplorerObservation>(); _bands = bands; InvalidateVisual();
        }
        internal void Select(string id) { _selected = id; InvalidateVisual(); }
        internal void ResetSelection() { _selected = null; _selectedObservation = null; _highlight.Clear(); InvalidateVisual(); }
        internal void SetContext(IReadOnlyList<ExplorerBar> bars, IReadOnlyList<ExplorerEpisode> episodes) { _bars = bars; _episodes = episodes; InvalidateVisual(); }
        internal void SelectObservation(ExplorerObservation observation) { _selectedObservation = observation; _selected = observation.Trigger?.VolumeId; InvalidateVisual(); }
        internal void SelectEpisode(ExplorerEpisode episode) { _highlight.Clear(); foreach (string id in episode.ChildIds) { _highlight.Add(id); } InvalidateVisual(); }
        internal void ClearDrawings() { _drawings.Clear(); _drawingStart = null; InvalidateVisual(); }
        protected override void OnRender(DrawingContext drawing)
        {
            try
            {
                drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(24, 28, 36)), null, new Rect(0, 0, ActualWidth, ActualHeight)); _hits.Clear(); _observationHits.Clear();
                DateTime[] times = _intervalFrom.HasValue && _intervalTo.HasValue ? new[] { _intervalFrom.Value, _intervalTo.Value } :
                    _bars.Count > 0 ? new[] { _bars.Min(b => b.Start), _bars.Max(b => b.End) } : _clouds.SelectMany(c => new[] { c.StartTime, c.Time }).ToArray();
                if (ActualWidth < 120 || ActualHeight < 100 || times.Length == 0) { Text(drawing, "Выберите страницу каталога или начните реплей", 12, 12, Brushes.LightGray); return; }
                _from = times.Min(); _to = times.Max();
                if (_to <= _from) { _to = _from.AddSeconds(1); }
                if (_zoom > 1) { long span = (_to - _from).Ticks; _to = _to.AddTicks(-(long)(span * _pan)); _from = _to.AddTicks(-(long)(span / _zoom)); }
                ExplorerBar[] bars = _bars.Where(b => b.End >= _from && b.Start <= _to).ToArray();
                decimal[] prices = _clouds.Where(c => c.Time >= _from && c.Time <= _to && _view.ForProfile(c.Profile).Visible &&
                        (_view.Passes(c, _step) || _view.ForProfile(c.Profile).ShowFiltered || c.Id == _selected)).SelectMany(c => new[] { c.Low, c.High })
                    .Concat(bars.SelectMany(b => new[] { b.Low, b.High }))
                    .Concat(_pivots.Where(p => p.ObservedAt >= _from && p.ObservedAt <= _to).Select(p => p.Price))
                    .Concat(_observations.Where(o => o.Time >= _from && o.Time <= _to && o.Status == "Breakout" && (_view.ShowControl || o.Group != "Control")).Select(o => o.Price))
                    .Concat(_vwap.Where(v => v.Time >= _from && v.Time <= _to).SelectMany(v => new[] { v.Price, v.Vwap - (_bands ? 2 * v.Sigma : 0), v.Vwap + (_bands ? 2 * v.Sigma : 0) })).ToArray();
                if (prices.Length == 0) { Text(drawing, "В выбранном интервале нет сохранённых свечей или видимых событий. Выберите другой интервал.", 12, 30, Brushes.LightGray); return; }
                _low = prices.Min(); _high = prices.Max();
                decimal padding = Math.Max(_step, (_high - _low) / 20); _low -= padding; _high += padding;
                decimal spanY = _high - _low, centerY = (_high + _low) / 2 + spanY * _pricePan;
                _low = centerY - spanY / _priceZoom / 2; _high = centerY + spanY / _priceZoom / 2;
                if (KnownBoundary.HasValue && KnownBoundary >= _from && KnownBoundary <= _to)
                {
                    double x = Map(KnownBoundary.Value, _high).X;
                    drawing.DrawRectangle(new SolidColorBrush(Color.FromArgb(35, 150, 150, 150)), null, new Rect(x, 28, Math.Max(0, ActualWidth - 85 - x), Math.Max(0, ActualHeight - 62)));
                    drawing.DrawLine(new Pen(Brushes.DeepSkyBlue, 1.5), new Point(x, 28), new Point(x, ActualHeight - 34));
                    Text(drawing, "Известно на якоре | будущее →", Math.Max(12, x - 100), 26, Brushes.DeepSkyBlue);
                }
                for (int i = 0; i <= 4; i++)
                {
                    decimal price = _low + (_high - _low) * i / 4;
                    Point point = Map(_from, price); drawing.DrawLine(new Pen(Brushes.DimGray, .4), point, new Point(ActualWidth - 80, point.Y));
                    Text(drawing, price.ToString("G8", CultureInfo.InvariantCulture), ActualWidth - 75, point.Y - 7, Brushes.Silver);
                }
                Text(drawing, _from.ToString("dd.MM HH:mm:ss"), 10, ActualHeight - 24, Brushes.Silver);
                Text(drawing, _to.ToString("dd.MM HH:mm:ss"), Math.Max(10, ActualWidth - 210), ActualHeight - 24, Brushes.Silver);
                Text(drawing, "□ одиночная · ● Cloud · золото: экстремум (заливка = подтверждён) · △ событие · колесо X, Ctrl+колесо Y", 10, 4, Brushes.Silver);
                if (bars.Length == 0) { Text(drawing, "Свечей в этом интервале нет; показаны только сохранённые события.", 12, 45, Brushes.Silver); }
                foreach (ExplorerBar bar in bars)
                {
                    DateTime time = bar.Start < _from ? _from : bar.Start; Point high = Map(time, bar.High), low = Map(time, bar.Low);
                    Brush brush = bar.Close >= bar.Open ? Brushes.SeaGreen : Brushes.IndianRed; Pen pen = new Pen(brush, 1);
                    drawing.DrawLine(pen, high, low); Point open = Map(time, bar.Open), close = Map(time, bar.Close);
                    if (PriceStyle == 0) { drawing.DrawRectangle(brush, pen, new Rect(open.X - 2, Math.Min(open.Y, close.Y), 4, Math.Max(1, Math.Abs(open.Y - close.Y)))); }
                    else if (PriceStyle == 1) { drawing.DrawLine(pen, open, new Point(open.X - 3, open.Y)); drawing.DrawLine(pen, close, new Point(close.X + 3, close.Y)); }
                }
                foreach (ExplorerEpisode episode in _episodes)
                {
                    if (episode.Time < _from || episode.StartTime > _to) { continue; }
                    Point left = Map(episode.StartTime < _from ? _from : episode.StartTime, episode.High);
                    Point right = Map(episode.Time > _to ? _to : episode.Time, episode.Low);
                    drawing.DrawRectangle(null, new Pen(Brushes.MediumPurple, 1.5), new Rect(left, right));
                }
                Dictionary<string, Point> previous = new Dictionary<string, Point>(); HashSet<(int, int, string)> pixels = new HashSet<(int, int, string)>();
                foreach (ExplorerCloud cloud in _clouds.OrderBy(c => c.LastSequence))
                {
                    if (cloud.Time < _from || cloud.Time > _to) { continue; }
                    if (!_view.ForProfile(cloud.Profile).Visible) { continue; }
                    bool pass = _view.Passes(cloud, _step);
                    if (!pass && !_view.ForProfile(cloud.Profile).ShowFiltered && cloud.Id != _selected) { continue; }
                    Point point = Map(cloud.Time, cloud.Price); _hits.Add((point, cloud));
                    Brush color = !pass ? Brushes.Gray : cloud.Buy >= cloud.Sell ? Brushes.MediumAquamarine : Brushes.Coral;
                    if (previous.TryGetValue(cloud.Profile, out Point prior)) { drawing.DrawLine(new Pen(color, .7), prior, point); }
                    previous[cloud.Profile] = point;
                    if (!pixels.Add(((int)point.X, (int)point.Y, cloud.Profile)) && cloud.Id != _selected) { continue; }
                    double radius = Math.Clamp(2 + Math.Log10(1 + (double)cloud.Volume), 3, 11);
                    bool selected = cloud.Id == _selected || _highlight.Contains(cloud.Id);
                    Pen outline = new Pen(selected ? Brushes.Gold : color, selected ? 2 : 1);
                    if (cloud.Count == 1) { drawing.DrawRectangle(color, outline, new Rect(point.X - radius, point.Y - radius, radius * 2, radius * 2)); }
                    else { drawing.DrawEllipse(color, outline, point, radius, radius); }
                }
                DrawVwap(drawing);
                foreach (ExplorerPivot pivot in _pivots)
                {
                    if (pivot.ObservedAt < _from || pivot.ObservedAt > _to) { continue; }
                    Point point = Map(pivot.ObservedAt, pivot.Price);
                    drawing.DrawEllipse(pivot.KnownAt.HasValue ? Brushes.Gold : null, new Pen(Brushes.Gold, 1), point, 4, 4);
                }
                foreach (ExplorerObservation observation in _observations)
                {
                    if (observation.Status != "Breakout" || observation.Time < _from || observation.Time > _to || (!_view.ShowControl && observation.Group == "Control")) { continue; }
                    Point point = Map(observation.Time, observation.Price); Brush color = observation.Group == "Control" ? Brushes.Silver : Brushes.DeepSkyBlue;
                    int direction = observation.Direction == "Long" ? 1 : -1;
                    _observationHits.Add((point, observation));
                    drawing.DrawLine(new Pen(color, 2), new Point(point.X - 6, point.Y + direction * 9), point);
                    drawing.DrawLine(new Pen(color, 2), new Point(point.X + 6, point.Y + direction * 9), point);
                }
                if (_selectedObservation != null)
                {
                    foreach (ExplorerPivot pivot in new[] { _selectedObservation.H0, _selectedObservation.H1, _selectedObservation.L0, _selectedObservation.L1, _selectedObservation.Rebound, _selectedObservation.Turn }.Where(p => p != null))
                    { Point point = Map(pivot.ObservedAt, pivot.Price); drawing.DrawEllipse(null, new Pen(Brushes.Gold, 2), point, 7, 7); }
                }
                foreach ((DateTime ATime, decimal APrice, DateTime BTime, decimal BPrice) line in _drawings)
                { drawing.DrawLine(new Pen(Brushes.Gold, 1.5), Map(line.ATime, line.APrice), Map(line.BTime, line.BPrice)); }
            }
            catch (Exception error) { Report(error); }
        }
        private void DrawVwap(DrawingContext drawing)
        {
            ExplorerVwapSample previous = null;
            foreach (ExplorerVwapSample sample in _vwap)
            {
                if (sample.Time < _from || sample.Time > _to) { continue; }
                if (previous != null)
                {
                    drawing.DrawLine(new Pen(Brushes.SlateGray, .7), Map(previous.Time, previous.Price), Map(sample.Time, sample.Price));
                    for (int band = _bands ? -2 : 0; band <= (_bands ? 2 : 0); band++)
                    { drawing.DrawLine(new Pen(band == 0 ? Brushes.DeepSkyBlue : Brushes.SteelBlue, band == 0 ? 1.8 : .7), Map(previous.Time, previous.Vwap + band * previous.Sigma), Map(sample.Time, sample.Vwap + band * sample.Sigma)); }
                }
                previous = sample;
            }
        }
        private Point Map(DateTime time, decimal price) => new Point(12 + (time.Ticks - _from.Ticks) / (double)(_to.Ticks - _from.Ticks) * (ActualWidth - 100),
            28 + (double)((_high - price) / (_high - _low)) * (ActualHeight - 62));
        private void Text(DrawingContext drawing, string text, double x, double y, Brush brush) => drawing.DrawText(new FormattedText(text, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip), new Point(x, y));
        #endregion

        #region Pointer interaction

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            try
            {
                base.OnMouseDown(e); Point point = e.GetPosition(this);
                if (_high <= _low || _to <= _from) { return; }
                if (Drawing)
                {
                    (DateTime Time, decimal Price) anchor = Unmap(point);
                    if (!_drawingStart.HasValue) { _drawingStart = anchor; }
                    else { _drawings.Add((_drawingStart.Value.Time, _drawingStart.Value.Price, anchor.Time, anchor.Price)); _drawingStart = null; }
                    InvalidateVisual(); return;
                }
                for (int i = 0; i < _drawings.Count; i++)
                {
                    (DateTime ATime, decimal APrice, DateTime BTime, decimal BPrice) line = _drawings[i]; Point a = Map(line.ATime, line.APrice), b = Map(line.BTime, line.BPrice);
                    Vector axis = b - a; double length = axis.LengthSquared;
                    double fraction = length == 0 ? 0 : Math.Clamp(Vector.Multiply(point - a, axis) / length, 0, 1);
                    if ((point - (a + axis * fraction)).Length <= 5) { _dragLine = i; _dragOrigin = point; _dragOriginal = line; CaptureMouse(); return; }
                }
                (Point Point, ExplorerObservation Observation) marker = _observationHits.OrderBy(h => (h.Point - point).LengthSquared).FirstOrDefault();
                if (marker.Observation != null && (marker.Point - point).Length <= 12) { ObservationSelected?.Invoke(marker.Observation); return; }
                (Point Point, ExplorerCloud Cloud) closest = _hits.OrderBy(h => (h.Point - point).LengthSquared).FirstOrDefault();
                if (closest.Cloud != null && (closest.Point - point).Length <= 16) { Select(closest.Cloud.Id); Selected?.Invoke(closest.Cloud); }
            }
            catch (Exception error) { Report(error); }
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            try
            {
                base.OnMouseMove(e); Point point = e.GetPosition(this);
                if (_dragLine >= 0 && IsMouseCaptured)
                {
                    (DateTime Time, decimal Price) from = Unmap(_dragOrigin), to = Unmap(point); TimeSpan time = to.Time - from.Time; decimal price = to.Price - from.Price;
                    _drawings[_dragLine] = (_dragOriginal.ATime + time, _dragOriginal.APrice + price, _dragOriginal.BTime + time, _dragOriginal.BPrice + price); InvalidateVisual(); return;
                }
                (Point Point, ExplorerCloud Cloud) closest = _hits.OrderBy(h => (h.Point - point).LengthSquared).FirstOrDefault();
                ToolTip = closest.Cloud != null && (closest.Point - point).Length < 16 ? closest.Cloud.Id + "\n" + closest.Cloud.Volume + " · " + closest.Cloud.Reason + "\nObserved " + closest.Cloud.Time.ToString("O") + "\nKnown " + closest.Cloud.KnownAt?.ToString("O") : null;
            }
            catch (Exception error) { Report(error); }
        }
        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            try
            {
                bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) { PriceAxis(shift ? 1 : e.Delta > 0 ? 1.4m : 1 / 1.4m, shift ? e.Delta > 0 ? .1m : -.1m : 0); }
                else if (shift) { _pan = Math.Clamp(_pan + (e.Delta > 0 ? .1 : -.1) / _zoom, 0, 1 - 1 / _zoom); }
                else { _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.4 : 1 / 1.4), 1, 1000); _pan = Math.Min(_pan, 1 - 1 / _zoom); }
                InvalidateVisual(); e.Handled = true;
            }
            catch (Exception error) { Report(error); }
        }
        protected override void OnMouseUp(MouseButtonEventArgs e)
        { try { base.OnMouseUp(e); _dragLine = -1; ReleaseMouseCapture(); } catch (Exception error) { Report(error); } }
        private (DateTime, decimal) Unmap(Point point) => (_from.AddTicks((long)(Math.Clamp((point.X - 12) / (ActualWidth - 100), 0, 1) * (_to - _from).Ticks)),
            _high - (decimal)Math.Clamp((point.Y - 28) / (ActualHeight - 62), 0, 1) * (_high - _low));
        #endregion
    }
}
