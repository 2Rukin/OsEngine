/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.OsData.OrderFlow.Context;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace OsEngine.OsData.OrderFlow
{
    internal sealed partial class OrderFlowResearchChart
    {
        private FlowContextResult _contextResult;
        private Rect _contextPlot;
        private Dictionary<string, FlowContextEvent[]> _contextPivots = new Dictionary<string, FlowContextEvent[]>();
        private FlowContextView _contextView = new FlowContextView();
        private long _contextSequence;
        private readonly List<(Point Position, FlowContextEvent Event)> _contextHits = new List<(Point, FlowContextEvent)>();
        internal event Action<FlowContextEvent> ContextSelected;
        internal long ContextSequence => IsReplaying ? _contextSequence : long.MaxValue;

        /// <summary>Installs a completed context run only for the same source; view filters remain independent of calculation settings.</summary>
        internal void SetContext(FlowContextResult result, FlowContextView view)
        {
            OrderFlowResearchResult source = IsReplaying ? _beforeReplay : _result;
            if (result != null && result.SourceHash != (source?.InputHash ?? source?.Input?.Sha256)) { throw new InvalidOperationException("Контекст и график относятся к разным исходным файлам."); }
            if (!ReferenceEquals(_contextResult, result))
            {
                _contextPivots = result?.Events.Where(item => item.RegionId != null && item.Kind.StartsWith("Подтверждённый", StringComparison.Ordinal)).GroupBy(item => item.RegionId).ToDictionary(group => group.Key, group => group.ToArray()) ?? new Dictionary<string, FlowContextEvent[]>();
            }
            _contextResult = result; _contextView = view ?? new FlowContextView(); _contextHits.Clear(); InvalidateVisual();
        }
        internal void SetContextReplay(long sequence) { _contextSequence = sequence; _contextHits.Clear(); InvalidateVisual(); }
        internal void SelectContextEvent(FlowContextEvent item)
        {
            if (_contextResult == null || item.Sequence > ContextSequence) { return; }
            _contextView.SelectedEventId = item.Id;
            _contextView.SelectedRegionId = item.RegionId ?? item.Coordinates.FirstOrDefault(coordinate => coordinate.Scale == 1)?.RegionId ?? item.Coordinates.FirstOrDefault()?.RegionId;
            int index = CurrentBars.FindIndex(bar => bar.TimeEnd > item.Time);
            if (index >= 0) { ScrollTo(Math.Max(0, index - VisibleCount / 2)); }
            InvalidateVisual(); ContextSelected?.Invoke(item);
        }
        internal void SelectContextRegion(FlowContextRegion region)
        {
            if (region.KnownSequence > ContextSequence) { return; }
            _contextView.SelectedRegionId = region.Id;
            int index = CurrentBars.FindIndex(bar => bar.TimeEnd > region.KnownAt);
            if (index >= 0) { ScrollTo(Math.Max(0, index - VisibleCount / 2)); }
            InvalidateVisual();
        }
        internal void ClearContextSelection()
        { _contextView.SelectedEventId = null; _contextView.SelectedRegionId = null; _contextHits.Clear(); InvalidateVisual(); }
        private bool ContextClick(Point point)
        {
            if (!_contextView.Enabled || !_contextPlot.Contains(point)) { return false; }
            for (int index = _contextHits.Count - 1; index >= 0; index--)
            {
                if ((_contextHits[index].Position - point).Length > 9) { continue; }
                SelectContextEvent(_contextHits[index].Event); return true;
            }
            return false;
        }
        private void DrawContext(DrawingContext drawing, List<OrderFlowDisplayBar> bars, double left, double slot, double top, double bottom, decimal min, decimal max)
        {
            _contextHits.Clear(); _contextPlot = Rect.Empty;
            if (_contextResult == null || !_contextView.Enabled || bars.Count == 0) { return; }
            long cursor = ContextSequence;
            List<FlowContextRegion> known = _contextResult.Regions.Where(region => region.KnownSequence <= cursor && (_contextView.Scale < 0 || region.Scale == _contextView.Scale)).ToList();
            HashSet<string> selected = new HashSet<string>();
            FlowContextRegion focus = known.FirstOrDefault(region => region.Id == _contextView.SelectedRegionId);
            if (focus != null)
            {
                selected.Add(focus.Id);
                FlowContextRegion parent = focus;
                while (parent.ParentId != null && (parent = known.FirstOrDefault(region => region.Id == parent.ParentId)) != null) { selected.Add(parent.Id); }
                foreach (FlowContextRegion child in known.Where(region => region.ParentId == focus.Id && region.KnownAt <= bars[bars.Count - 1].TimeEnd)) { selected.Add(child.Id); }
            }
            else
            {
                foreach (FlowContextRegion region in known.Where(region => region.KnownAt <= bars[bars.Count - 1].TimeEnd && (!region.End.HasValue || region.End >= bars[0].TimeStart || region.EndSequence > cursor))
                    .GroupBy(region => region.Scale).Select(group => group.OrderByDescending(region => region.KnownSequence).First())) { selected.Add(region.Id); }
            }
            _contextPlot = new Rect(left, top, Math.Max(1, DataWidth), bottom - top);
            drawing.PushClip(new RectangleGeometry(_contextPlot));
            Color[] colors = { Colors.DodgerBlue, Colors.DarkOrange, Colors.MediumOrchid };
            foreach (FlowContextRegion region in known.Where(region => _contextView.AllRegions || selected.Contains(region.Id)))
            {
                if (region.Start > bars[bars.Count - 1].TimeEnd || (region.Points.Count > 0 && region.Points[region.Points.Count - 1].Time < bars[0].TimeStart)) { continue; }
                Brush brush = new SolidColorBrush(colors[region.Scale]); Pen pen = new Pen(brush, region.Id == _contextView.SelectedRegionId ? 2.5 : 1.2);
                FlowContextPoint[] points = region.Points.Where(point => point.Sequence <= cursor).ToArray();
                if (points.Length == 0) { continue; }
                DateTime visibleEnd = points[points.Length - 1].Time;
                if (_contextView.Regions)
                {
                    Point first = ContextProject(region.Start, region.High, bars, left, slot, top, bottom, min, max);
                    Point last = ContextProject(region.KnownAt, region.Low, bars, left, slot, top, bottom, min, max);
                    drawing.DrawRectangle(new SolidColorBrush(Color.FromArgb(24, colors[region.Scale].R, colors[region.Scale].G, colors[region.Scale].B)), pen, new Rect(first, last));
                    Pen extension = new Pen(brush, 1) { DashStyle = DashStyles.Dot };
                    ContextSegment(drawing, extension, region.KnownAt, region.High, visibleEnd, region.High, bars, left, slot, top, bottom, min, max);
                    ContextSegment(drawing, extension, region.KnownAt, region.Low, visibleEnd, region.Low, bars, left, slot, top, bottom, min, max);
                    ContextSegment(drawing, extension, region.KnownAt, region.InitialMean, visibleEnd, region.InitialMean, bars, left, slot, top, bottom, min, max);
                }
                for (int index = 1; index < points.Length; index++)
                {
                    FlowContextPoint before = points[index - 1], after = points[index];
                    if (after.Time < bars[0].TimeStart || before.Time > bars[bars.Count - 1].TimeEnd) { continue; }
                    if (_contextView.Vwap) { ContextSegment(drawing, pen, before.Time, before.Vwap, after.Time, after.Vwap, bars, left, slot, top, bottom, min, max); }
                    if (_contextView.Twap && before.Twap.HasValue && after.Twap.HasValue) { ContextSegment(drawing, new Pen(brush, 1.5) { DashStyle = DashStyles.Dash }, before.Time, before.Twap.Value, after.Time, after.Twap.Value, bars, left, slot, top, bottom, min, max); }
                    if (_contextView.Bands)
                    {
                        foreach (int sign in new[] { -1, 1 }) { ContextSegment(drawing, new Pen(brush, .7) { DashStyle = DashStyles.Dot }, before.Time, before.Vwap + sign * _contextView.SigmaMultiplier * before.Sigma, after.Time, after.Vwap + sign * _contextView.SigmaMultiplier * after.Sigma, bars, left, slot, top, bottom, min, max); }
                    }
                }
                if (_contextView.Swings)
                {
                    FlowContextEvent previous = null;
                    foreach (FlowContextEvent pivot in (_contextPivots.TryGetValue(region.Id, out FlowContextEvent[] pivots) ? pivots : Array.Empty<FlowContextEvent>()).Where(item => item.Sequence <= cursor))
                    {
                        if (previous != null) { ContextSegment(drawing, pen, previous.ObservedAt, previous.ObservedPrice, pivot.ObservedAt, pivot.ObservedPrice, bars, left, slot, top, bottom, min, max); }
                        previous = pivot;
                    }
                }
            }
            if (_contextView.Events)
            {
                foreach (FlowContextEvent item in _contextResult.Events.Where(item => item.Sequence <= cursor && item.Time >= bars[0].TimeStart && item.Time < bars[bars.Count - 1].TimeEnd && _contextView.Matches(item)))
                {
                    if (!_contextView.AllRegions && item.RegionId != null && !selected.Contains(item.RegionId)) { continue; }
                    if (_contextView.Scale >= 0 && !item.Coordinates.Any(coordinate => coordinate.Scale == _contextView.Scale)) { continue; }
                    Point point = ContextProject(item.Time, item.Price, bars, left, slot, top, bottom, min, max);
                    Brush brush = item.Kind == "Одинаковые размеры" ? Brushes.DeepSkyBlue : item.Kind == "Ускорение ленты" ? Brushes.Gold : item.Direction > 0 ? Brushes.LimeGreen : item.Direction < 0 ? Brushes.OrangeRed : Brushes.Silver;
                    double radius = item.Id == _contextView.SelectedEventId ? 7 : 4;
                    StreamGeometry diamond = new StreamGeometry();
                    using (StreamGeometryContext geometry = diamond.Open()) { geometry.BeginFigure(new Point(point.X, point.Y - radius), true, true); geometry.LineTo(new Point(point.X + radius, point.Y), true, false); geometry.LineTo(new Point(point.X, point.Y + radius), true, false); geometry.LineTo(new Point(point.X - radius, point.Y), true, false); }
                    drawing.DrawGeometry(brush, new Pen(ForegroundBrush, .5), diamond); if (_contextPlot.Contains(point)) { _contextHits.Add((point, item)); }
                }
            }
            drawing.Pop();
        }
        private Point ContextProject(DateTime time, decimal price, List<OrderFlowDisplayBar> bars, double left, double slot, double top, double bottom, decimal min, decimal max)
            => new Point(left + slot * OrderFlowDrawingGeometry.BarPosition(bars, time), Scale(price, min, max, bottom, top));
        private void ContextSegment(DrawingContext drawing, Pen pen, DateTime firstTime, decimal firstPrice, DateTime lastTime, decimal lastPrice,
            List<OrderFlowDisplayBar> bars, double left, double slot, double top, double bottom, decimal min, decimal max)
        { drawing.DrawLine(pen, ContextProject(firstTime, firstPrice, bars, left, slot, top, bottom, min, max), ContextProject(lastTime, lastPrice, bars, left, slot, top, bottom, min, max)); }
    }
}
