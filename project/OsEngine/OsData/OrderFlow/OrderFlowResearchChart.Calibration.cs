/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.OsData.OrderFlow.Calibration;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace OsEngine.OsData.OrderFlow
{
    internal sealed partial class OrderFlowResearchChart
    {
        private ImmutableArray<CalibrationLayer> _calibrationLayers = ImmutableArray<CalibrationLayer>.Empty;
        private readonly List<(Point Point, double Radius, CalibrationMarker Marker)> _calibrationHits = new List<(Point, double, CalibrationMarker)>();
        private CalibrationMarker _calibrationSelection;
        private long _calibrationSequence;
        private bool _calibrationComplete;
        private ImmutableArray<CalibrationLayer> _calibrationPlaybackLayers = ImmutableArray<CalibrationLayer>.Empty;
        internal bool CalibrationTheme { get; set; }
        internal event Action<CalibrationMarker> CalibrationSelected;

        /// <summary>Installs independent saved layers on the dispatcher. Mismatched input hashes and disabled layers never draw or hit.</summary>
        internal void SetCalibrationLayers(ImmutableArray<CalibrationLayer> layers)
        { _calibrationLayers = layers; _calibrationHits.Clear(); _calibrationSelection = null; InvalidateVisual(); }

        /// <summary>Only completed evidence known at the consumed source ordinal is visible during replay; EOF is explicit.</summary>
        internal void SetCalibrationReplay(long sequence, bool complete)
        { _calibrationSequence = sequence; _calibrationComplete = complete; if (sequence == 0) { _calibrationPlaybackLayers = ImmutableArray<CalibrationLayer>.Empty; } _calibrationHits.Clear(); InvalidateVisual(); }
        internal void SetCalibrationPlaybackLayers(ImmutableArray<CalibrationLayer> layers)
        { _calibrationPlaybackLayers = layers; _calibrationHits.Clear(); InvalidateVisual(); }

        internal void SelectCalibration(CalibrationMarker marker)
        {
            if (!CalibrationVisible(marker)) { return; }
            _calibrationSelection = marker;
            int index = CurrentBars.FindIndex(b => b.TimeEnd > marker.Event.Evidence.Time);
            if (index >= 0) { ScrollTo(Math.Max(0, index - VisibleCount / 2)); }
            InvalidateVisual();
        }
        private bool CalibrationVisible(CalibrationMarker marker)
        {
            // Prefix snapshots deliberately omit metadata; identity belongs to the verified replay source, not a partial frame.
            OrderFlowResearchResult source = IsReplaying ? _beforeReplay : _result;
            return marker.Rule.Enabled && marker.Rule.Visible && marker.Rule.Provenance.InputSha256 == (source?.InputHash ?? source?.Input?.Sha256) &&
                (!IsReplaying || marker.Known(_calibrationSequence, _calibrationComplete));
        }

        private bool CalibrationClick(Point point)
        {
            for (int i = _calibrationHits.Count - 1; i >= 0; i--)
            {
                (Point position, double radius, CalibrationMarker marker) = _calibrationHits[i];
                if (!CalibrationVisible(marker) || (position - point).Length > radius + 3) { continue; }
                _calibrationSelection = marker; InvalidateVisual(); CalibrationSelected?.Invoke(marker); return true;
            }
            return false;
        }

        private Brush CalibrationBrush(string key) => TryFindResource(key) as Brush ?? ForegroundBrush;

        private void DrawCalibration(DrawingContext context, List<OrderFlowDisplayBar> bars, double left, double slot,
            double top, double bottom, decimal min, decimal max)
        {
            _calibrationHits.Clear();
            if (_calibrationLayers.IsDefaultOrEmpty || bars.Count == 0) { return; }
            context.PushClip(new RectangleGeometry(new Rect(left, top, Math.Max(1, DataWidth), bottom - top)));
            IEnumerable<CalibrationMarker> markers = (IsReplaying ? _calibrationPlaybackLayers : _calibrationLayers).SelectMany(l => l.Markers);
            if (_calibrationSelection != null) { markers = markers.Append(_calibrationSelection); }
            foreach (CalibrationMarker marker in markers.OrderBy(m => m.Event.Evidence.LastSequence))
            {
                if (!CalibrationVisible(marker)) { continue; }
                DateTime time = marker.Event.Evidence.Time;
                int low = 0, high = bars.Count;
                while (low < high) { int mid = low + (high - low) / 2; if (bars[mid].TimeEnd <= time) { low = mid + 1; } else { high = mid; } }
                if (low == bars.Count || time < bars[low].TimeStart) { continue; }
                double fraction = (time.Ticks - bars[low].TimeStart.Ticks) / (double)(bars[low].TimeEnd.Ticks - bars[low].TimeStart.Ticks);
                Point point = new Point(left + slot * (low + fraction), Scale(marker.Event.Evidence.Price, min, max, bottom, top));
                double radius = Math.Clamp(3 + Math.Log10(1 + (double)marker.Event.Volume), 4, 12);
                Brush brush = CalibrationBrush(marker.Event.Delta >= 0 ? "JournalSwatchLongBrush" : "JournalSwatchShortBrush");
                bool selected = marker.CloudId == _calibrationSelection?.CloudId;
                Pen pen = new Pen(selected ? CalibrationBrush("ControlForeground") : brush, selected ? 3 : 1.5);
                if (!marker.Event.Evidence.KnownAt.HasValue) { pen.DashStyle = DashStyles.Dash; }
                if (marker.Rule.Formation.Mode == FormationMode.Single) { context.DrawRectangle(brush, pen, new Rect(point.X - radius, point.Y - radius, radius * 2, radius * 2)); }
                else { context.DrawEllipse(null, pen, point, radius, radius); }
                _calibrationHits.Add((point, radius, marker));
            }
            context.Pop();
        }

        private void DrawCalibrationPrices(DrawingContext context, List<OrderFlowDisplayBar> bars, double left, double slot,
            double top, double bottom, decimal min, decimal max)
        {
            for (int i = 0; i < bars.Count; i++)
            {
                OrderFlowDisplayBar bar = bars[i]; double x = left + slot * (i + .5);
                Brush brush = CalibrationBrush(bar.Close >= bar.Open ? "JournalSwatchLongBrush" : "JournalSwatchShortBrush");
                Pen pen = new Pen(brush, 1); context.DrawLine(pen, new Point(x, Scale(bar.Low, min, max, bottom, top)), new Point(x, Scale(bar.High, min, max, bottom, top)));
                double open = Scale(bar.Open, min, max, bottom, top), close = Scale(bar.Close, min, max, bottom, top);
                context.DrawRectangle(brush, pen, new Rect(x - Math.Max(1, slot * .25), Math.Min(open, close), Math.Max(2, slot * .5), Math.Max(1, Math.Abs(open - close))));
            }
        }
    }
}
