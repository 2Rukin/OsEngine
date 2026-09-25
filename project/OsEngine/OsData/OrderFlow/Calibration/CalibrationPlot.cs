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
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace OsEngine.OsData.OrderFlow.Calibration
{
    internal sealed record PlotDatum(int X, int Y, decimal Value, string Label, object Payload = null);
    internal sealed record PlotOverlay(int Row, decimal FromFraction, decimal ToFraction, string Name);

    /// <summary>Themed descriptive histogram/heatmap presenter. Only bounded summary arrays reach the dispatcher; clicks select explicit candidates.</summary>
    internal sealed class CalibrationPlot : Control
    {
        internal static readonly DependencyProperty AccentProperty = DependencyProperty.Register("Accent", typeof(Brush), typeof(CalibrationPlot), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        private IReadOnlyList<PlotDatum> _data = Array.Empty<PlotDatum>();
        private IReadOnlyList<string> _x = Array.Empty<string>(), _y = Array.Empty<string>();
        private readonly List<(Rect Bounds, PlotDatum Datum)> _hits = new List<(Rect, PlotDatum)>();
        private readonly List<(Rect Bounds, string Name)> _overlayHits = new List<(Rect, string)>();
        internal bool Heatmap { get; set; }
        internal string Caption { get; set; }
        internal IReadOnlyList<PlotOverlay> Overlays { get; set; } = Array.Empty<PlotOverlay>();
        internal event Action<object> Selected;
        internal CalibrationPlot()
        {
            SetResourceReference(BackgroundProperty, "ControlBackgroundNormalLight"); SetResourceReference(ForegroundProperty, "ControlForegroundWhite");
            SetResourceReference(AccentProperty, "ControlForeground"); MinHeight = 210; Focusable = true;
        }
        internal void Set(IReadOnlyList<PlotDatum> data, IReadOnlyList<string> x = null, IReadOnlyList<string> y = null)
        { _data = data; _x = x ?? Array.Empty<string>(); _y = y ?? Array.Empty<string>(); InvalidateVisual(); }
        internal void Distribution(string caption, DistributionSummary summary, bool select = false)
        {
            Caption = caption; Heatmap = false;
            Set(summary.Histogram.Select((p, i) => new PlotDatum(i, 0, p.Count, p.Value.ToString("G29", CultureInfo.InvariantCulture) + " · n=" + p.Count,
                select ? p.Value : null)).ToArray(), summary.Histogram.Select(p => p.Value.ToString("G4", CultureInfo.InvariantCulture)).ToArray());
        }

        protected override void OnRender(DrawingContext context)
        {
            base.OnRender(context);
            try
            {
                _hits.Clear(); _overlayHits.Clear(); context.DrawRectangle(Background, null, new Rect(0, 0, ActualWidth, ActualHeight));
                Text(context, Caption ?? "", 8, 5, Math.Max(1, ActualWidth - 16));
                if ((_data.Count == 0 && _x.Count == 0) || ActualWidth < 100 || ActualHeight < 100) { return; }
                double left = Heatmap ? 105 : 55, top = 36, width = Math.Max(1, ActualWidth - left - 15), height = Math.Max(1, ActualHeight - 78);
                int columns = Math.Max(1, Math.Max(_x.Count, _data.Count == 0 ? 0 : _data.Max(p => p.X) + 1));
                int rows = Heatmap ? Math.Max(1, Math.Max(_y.Count, _data.Count == 0 ? 0 : _data.Max(p => p.Y) + 1)) : 1;
                decimal max = _data.Count == 0 ? 1 : Math.Max(1, _data.Max(p => Math.Abs(p.Value)));
                Brush accent = (Brush)GetValue(AccentProperty) ?? Foreground;
                foreach (PlotDatum datum in _data)
                {
                    double strength = (double)(Math.Abs(datum.Value) / max);
                    Rect bounds = Heatmap ? new Rect(left + width * datum.X / columns, top + height * datum.Y / rows,
                        Math.Max(1, width / columns - 2), Math.Max(1, height / rows - 2)) :
                        new Rect(left + width * datum.X / columns, top + height * (1 - strength), Math.Max(1, width / columns - 2), Math.Max(1, height * strength));
                    Brush fill = accent.CloneCurrentValue(); fill.Opacity = Heatmap ? .12 + .88 * strength : .8;
                    context.DrawRectangle(fill, new Pen(Foreground, .3), bounds); _hits.Add((bounds, datum));
                    if (Heatmap && bounds.Width > 55 && bounds.Height > 22) { Text(context, datum.Value.ToString("G4", CultureInfo.InvariantCulture), bounds.X + 3, bounds.Y + 2, bounds.Width - 5); }
                }
                for (int i = 0; i < _x.Count; i++)
                {
                    int every = Math.Max(1, (int)Math.Ceiling(_x.Count * 55 / width));
                    if (i % every == 0) { Text(context, _x[i], left + width * i / columns, top + height + 4, Math.Max(45, width / columns * every)); }
                }
                foreach (PlotOverlay overlay in Overlays)
                {
                    double y = top + height * Math.Max(0, overlay.Row) / rows, h = overlay.Row < 0 ? height : height / rows;
                    Rect bounds = new Rect(left + width * (double)overlay.FromFraction, y, Math.Max(1, width * (double)(overlay.ToFraction - overlay.FromFraction)), h);
                    Pen pen = new Pen(Foreground, 1) { DashStyle = DashStyles.Dash };
                    context.DrawRectangle(null, pen, bounds);
                    _overlayHits.Add((bounds, overlay.Name));
                }
                if (Heatmap)
                { for (int i = 0; i < _y.Count; i++) { if (height / rows > 14 || i % Math.Max(1, rows / 8) == 0) { Text(context, _y[i], 4, top + height * i / rows, left - 8); } } }
                else { Text(context, max.ToString("G4", CultureInfo.InvariantCulture), 4, top, left - 5); }
            }
            catch (Exception error) { ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error); }
        }
        private void Text(DrawingContext context, string value, double x, double y, double width)
        {
            FormattedText text = new FormattedText(value, CultureInfo.CurrentUICulture, System.Windows.FlowDirection.LeftToRight,
                new Typeface(FontFamily, FontStyle, FontWeight, FontStretch), 11, Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip)
            { MaxTextWidth = Math.Max(1, width), MaxLineCount = 2, Trimming = TextTrimming.CharacterEllipsis };
            context.DrawText(text, new Point(x, y));
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            try
            {
                Point point = e.GetPosition(this); string value = _hits.LastOrDefault(p => p.Bounds.Contains(point)).Datum?.Label;
                string ranges = string.Join(" · ", _overlayHits.Where(p => p.Bounds.Contains(point)).Select(p => p.Name).Distinct());
                ToolTip = string.IsNullOrEmpty(ranges) ? value : value + "\n" + ranges;
            }
            catch (Exception error) { ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error); }
        }
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            try { Focus(); PlotDatum datum = _hits.LastOrDefault(p => p.Bounds.Contains(e.GetPosition(this))).Datum; if (datum?.Payload != null) { Selected?.Invoke(datum.Payload); } }
            catch (Exception error) { ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error); }
        }
    }
}
