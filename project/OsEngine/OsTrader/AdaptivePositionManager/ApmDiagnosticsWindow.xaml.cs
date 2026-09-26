using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using OsEngine.Logging;
using OsEngine.Market;

namespace OsEngine.OsTrader.AdaptivePositionManager
{
    /// <summary>
    /// Nonmodal research diagnostics. Polls detached snapshots; closing this view never disposes the trading
    /// controller. Historical selection renders stored snapshots without recalculating policy or orders;
    /// command buttons always reflect the current controller snapshot.
    /// </summary>
    public partial class ApmDiagnosticsWindow : Window
    {
        private readonly ApmExecutionController _controller;
        private readonly Action _parameters;
        private readonly Func<ApmRunDiagnosticView> _runDiagnostics;
        private readonly DispatcherTimer _timer;
        private readonly Dictionary<string, ApmTableWindow> _tables = new Dictionary<string, ApmTableWindow>();
        private readonly Dictionary<Border, string> _visualValues = new Dictionary<Border, string>();
        private readonly Dictionary<Border, DateTime> _highlightUntil = new Dictionary<Border, DateTime>();
        private ApmAuditRow _selected;
        private string _previewState;
        private decimal? _nextAdd;
        private decimal? _nextReduce;

        /// <summary>Construct on the WPF dispatcher. The controller remains owned by the robot.</summary>
        public ApmDiagnosticsWindow(ApmExecutionController controller, Action parameters)
            : this(controller, parameters, () => ApmDiagnosticProjection.Capture(controller)) { }

        /// <summary>Construct with a detached run-level provider so completed and active campaigns remain separate.</summary>
        public ApmDiagnosticsWindow(ApmExecutionController controller, Action parameters,
            Func<ApmRunDiagnosticView> runDiagnostics)
        {
            InitializeComponent();
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _runDiagnostics = runDiagnostics ?? throw new ArgumentNullException(nameof(runDiagnostics));
            _controller.DetailedDiagnostics = true;
            Width = Math.Min(Width, SystemParameters.WorkArea.Width);
            Height = Math.Min(Height, SystemParameters.WorkArea.Height);
            _parameters = parameters;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _timer.Tick += Timer_Tick;
            CheckBoxDetails.Checked += CheckBoxDetails_Changed;
            CheckBoxDetails.Unchecked += CheckBoxDetails_Changed;
            ButtonResume.Click += ButtonResume_Click;
            ButtonPause.Click += ButtonPause_Click;
            ButtonClose.Click += ButtonClose_Click;
            ButtonEmergency.Click += ButtonEmergency_Click;
            ButtonParameters.Click += ButtonParameters_Click;
            ButtonNext.Click += ButtonNext_Click;
            ButtonNow.Click += ButtonNow_Click;
            foreach (Button button in TableButtons()) button.Click += ButtonTable_Click;
            Closed += Window_Closed;
            _timer.Start();
            ResizeStatusGroups();
        }

        private Button[] TableButtons() => new[] { ButtonDecisions, ButtonOrders, ButtonCampaigns, ButtonQuality, ButtonReport };

        private void Timer_Tick(object sender, EventArgs args)
        {
            try { Paint(); }
            catch (Exception error) { Log(error); }
        }

        private void Paint()
        {
            ApmDiagnosticView view = _controller.CaptureView();
            ApmSnapshot state = _selected?.Snapshot ?? view.Snapshot;
            ApmAuditRow[] rows = view.Rows;
            if (_selected != null) rows = rows.Where(r => r.Sequence <= _selected.Sequence).ToArray();
            string previewState = _selected?.PreviewState ?? (_selected == null ? view.PreviewState : null);
            if (previewState != _previewState)
            {
                _previewState = previewState;
                _nextAdd = _previewState == null ? null : ApmCampaign.PreviewRecorded(_previewState, ApmAction.Add);
                _nextReduce = _previewState == null ? null : ApmCampaign.PreviewRecorded(_previewState, ApmAction.Reduce);
            }
            StatusCampaignId.Text = state.CampaignId;
            StatusDirection.Text = _controller.Spec.Direction.ToString();
            StatusProfile.Text = "ResearchOnly · TradeOnly · " + _controller.ExecutionModel;
            StatusMoment.Text = _selected == null ? "Текущий момент" : "Сохранённый snapshot";
            StatusFilled.Text = state.FilledVolume.ToString();
            StatusTargets.Text = (state.Decision?.RawTarget.ToString() ?? "—") + " / "
                + (state.Decision?.RiskAllowedTarget.ToString() ?? "—");
            StatusAverage.Text = state.AverageEntry.ToString();
            StatusEquity.Text = state.Equity.ToString();
            StatusBudget.Text = _controller.Spec.RiskBudgetCurrency + " " + _controller.Spec.RiskCurrency;
            StatusDrawdown.Text = state.MaximumDrawdown.ToString();
            StatusStress.Text = (state.FilledVolume * (ApmMathematics.UnitStopRisk(_controller.Spec, state.AverageEntry)
                + _controller.Spec.FeePerContract)).ToString();
            StatusPreview.Text = "ADD " + (_nextAdd?.ToString() ?? "нет") + " / REDUCE " + (_nextReduce?.ToString() ?? "нет");
            StatusState.Text = state.State + " / " + state.Regime;
            StatusPause.Text = state.State == ApmState.PausedNoIncrease ? "АКТИВНА" : "нет";
            StatusExitLatch.Text = state.ExitLatch ? "ДА" : "нет";
            StatusExitReason.Text = string.IsNullOrWhiteSpace(state.ExitReason) ? "—" : state.ExitReason;
            StatusPending.Text = "+" + state.PendingIncrease + " / −" + state.PendingReduce;
            StatusReplay.Text = (_controller.ReplayHealth.Stalled ? "пауза/задержка" : "callbacks идут")
                + " · " + state.Market?.Ready + " / " + state.Market?.Quality;
            StatusReasons.Text = string.Join("; ", (state.Decision?.Reasons ?? Array.Empty<string>()).Select(ApmDisplayText.Reason));
            UpdateControlVisuals(state, view.Snapshot);
            DrawPrice(rows, state);
            DrawVolume(rows);
        }

        private void StatusGroups_SizeChanged(object sender, SizeChangedEventArgs args)
        {
            try { ResizeStatusGroups(); }
            catch (Exception error) { Log(error); }
        }

        private void ResizeStatusGroups()
        {
            if (StatusGroups == null) return;
            double available = Math.Max(260, StatusGroups.ActualWidth);
            double width = available < 820 ? available - 8 : available / 2 - 8;
            foreach (GroupBox group in new[] { GroupCampaign, GroupPosition, GroupRisk, GroupExecution })
                group.Width = Math.Max(250, width);
        }

        private void UpdateControlVisuals(ApmSnapshot state, ApmSnapshot current)
        {
            bool paused = state.State == ApmState.PausedNoIncrease;
            bool closing = state.State == ApmState.Closing;
            bool emergency = state.State == ApmState.FaultedClosing || state.State == ApmState.Reconciling
                || state.ExitReason == "EMERGENCY_EXIT";
            Brush normal = new SolidColorBrush(Color.FromRgb(28, 65, 45));
            Brush pending = new SolidColorBrush(Color.FromRgb(112, 82, 20));
            Brush warning = new SolidColorBrush(Color.FromRgb(128, 68, 18));
            Brush error = new SolidColorBrush(Color.FromRgb(125, 38, 38));
            Brush neutral = new SolidColorBrush(Color.FromRgb(23, 32, 42));
            SetVisual(StateCell, StatusState.Text, emergency ? error : closing ? warning : paused ? pending : normal);
            SetVisual(PauseCell, StatusPause.Text, paused ? pending : neutral);
            SetVisual(ExitLatchCell, StatusExitLatch.Text, state.ExitLatch ? emergency ? error : warning : neutral);
            SetVisual(ExitReasonCell, StatusExitReason.Text,
                state.ExitReason == "EMERGENCY_EXIT" ? error : string.IsNullOrWhiteSpace(state.ExitReason) ? neutral : warning);
            SetVisual(PendingCell, StatusPending.Text,
                state.PendingIncrease > 0 || state.PendingReduce > 0 ? pending : neutral);
            if (current.State == ApmState.PausedNoIncrease)
            {
                ButtonPause.Background = pending;
                ButtonPause.FontWeight = FontWeights.Bold;
                ButtonResume.Background = normal;
            }
            else
            {
                ButtonPause.ClearValue(BackgroundProperty); ButtonPause.ClearValue(FontWeightProperty);
                ButtonResume.ClearValue(BackgroundProperty);
            }
        }

        private void SetVisual(Border cell, string value, Brush background)
        {
            if (_visualValues.TryGetValue(cell, out string previous) && previous != value)
                _highlightUntil[cell] = DateTime.UtcNow.AddSeconds(2);
            _visualValues[cell] = value;
            cell.Background = background;
            bool highlighted = _highlightUntil.TryGetValue(cell, out DateTime until) && until > DateTime.UtcNow;
            cell.BorderBrush = highlighted ? Brushes.White : Brushes.Transparent;
            cell.BorderThickness = highlighted ? new Thickness(1) : new Thickness(0);
        }

        private void DrawPrice(ApmAuditRow[] rows, ApmSnapshot state)
        {
            CanvasPrice.Children.Clear();
            ApmAuditRow[] prices = rows.Where(r => r.Kind == "Decision" || r.Kind == "Fill").TakeLast(600).ToArray();
            if (prices.Length == 0 || CanvasPrice.ActualWidth < 10 || CanvasPrice.ActualHeight < 10) return;
            ApmCampaignSpec spec = _controller.Spec;
            decimal low = Math.Min(prices.Min(r => r.Price), Math.Min(spec.HardStopPrice, spec.FinalTargetPrice));
            decimal high = Math.Max(prices.Max(r => r.Price), Math.Max(spec.HardStopPrice, spec.FinalTargetPrice));
            double width = CanvasPrice.ActualWidth - 10;
            double height = CanvasPrice.ActualHeight - 20;
            decimal range = Math.Max(spec.PriceStep, high - low);
            Polyline line = new Polyline { Stroke = Brushes.LightSteelBlue, StrokeThickness = 1.5 };
            for (int i = 0; i < prices.Length; i++)
            {
                double x = 5 + width * i / Math.Max(1, prices.Length - 1);
                double y = 10 + height * (double)((high - prices[i].Price) / range);
                if (prices[i].Kind == "Decision") line.Points.Add(new Point(x, y));
                if (prices[i].Kind == "Fill" || prices[i].Volume > 0)
                {
                    Shape marker = prices[i].Kind == "Fill" ? new Rectangle() : new Ellipse();
                    marker.Width = marker.Height = prices[i].Kind == "Fill" ? 8 : 5;
                    marker.Fill = prices[i].Kind == "Fill" ? Brushes.Gold : Brushes.DeepSkyBlue;
                    marker.ToolTip = prices[i].Kind + " " + prices[i].Action + " " + prices[i].Volume + " @ " + prices[i].Price;
                    Canvas.SetLeft(marker, x - marker.Width / 2); Canvas.SetTop(marker, y - marker.Height / 2);
                    CanvasPrice.Children.Add(marker);
                }
            }
            CanvasPrice.Children.Add(line);
            AddLevel(spec.HardStopPrice, "HardStop", Brushes.IndianRed, high, range, height);
            AddLevel(spec.FinalTargetPrice, "FinalTarget", Brushes.LightGreen, high, range, height);
            AddLevel(state.Anchor, "Anchor", Brushes.White, high, range, height);
            decimal boundary = spec.HardStopPrice + _controller.Policy.NoAddFraction * (state.Anchor - spec.HardStopPrice);
            AddLevel(boundary, "NoAddZone", Brushes.Orange, high, range, height);
            if (_nextAdd.HasValue) AddLevel(_nextAdd.Value, "Preview ADD", Brushes.DeepSkyBlue, high, range, height);
            if (_nextReduce.HasValue) AddLevel(_nextReduce.Value, "Preview REDUCE", Brushes.MediumPurple, high, range, height);
            TextBlock legend = new TextBlock { Text = "● решение · ■ исполнение · цена последней сделки", Foreground = Brushes.LightGray, FontSize = 11 };
            Canvas.SetRight(legend, 8); Canvas.SetTop(legend, 4); CanvasPrice.Children.Add(legend);
        }

        private void AddLevel(decimal price, string name, Brush brush, decimal high, decimal range, double height)
        {
            double y = 10 + height * (double)((high - price) / range);
            CanvasPrice.Children.Add(new Line { X1 = 0, X2 = CanvasPrice.ActualWidth, Y1 = y, Y2 = y, Stroke = brush, Opacity = 0.6 });
            TextBlock text = new TextBlock { Text = name + " " + price, Foreground = brush, FontSize = 11 };
            Canvas.SetLeft(text, 7); Canvas.SetTop(text, Math.Max(0, y - 14)); CanvasPrice.Children.Add(text);
        }

        private void DrawVolume(ApmAuditRow[] source)
        {
            CanvasVolume.Children.Clear();
            ApmAuditRow[] rows = source.Where(r => r.Kind == "Decision" || r.Kind == "Fill").TakeLast(600).ToArray();
            Polyline filled = new Polyline { Stroke = Brushes.Gold, StrokeThickness = 1.5 };
            Polyline allowed = new Polyline { Stroke = Brushes.DeepSkyBlue, StrokeThickness = 1 };
            for (int i = 0; i < rows.Length; i++)
            {
                double x = CanvasVolume.ActualWidth * i / Math.Max(1, rows.Length - 1);
                filled.Points.Add(new Point(x, CanvasVolume.ActualHeight * (1 - (double)(rows[i].Filled / _controller.Spec.MaxVolume))));
                allowed.Points.Add(new Point(x, CanvasVolume.ActualHeight * (1 - (double)(rows[i].Allowed / _controller.Spec.MaxVolume))));
            }
            CanvasVolume.Children.Add(filled); CanvasVolume.Children.Add(allowed);
            CanvasVolume.Children.Add(new TextBlock { Text = "Объём · жёлтый — Filled, голубой — Allowed", Foreground = Brushes.White, Margin = new Thickness(5) });
        }

        private void ButtonTable_Click(object sender, RoutedEventArgs args)
        {
            try
            {
                string name = (string)((Button)sender).Tag;
                if (_tables.TryGetValue(name, out ApmTableWindow existing))
                { if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal; existing.Activate(); return; }
                ApmTableWindow window = new ApmTableWindow(_controller, name, _runDiagnostics);
                window.SelectedEvent += Table_SelectedEvent;
                window.Closed += Table_Closed;
                _tables.Add(name, window);
                window.Show();
            }
            catch (Exception error) { Log(error); }
        }

        private void Table_SelectedEvent(ApmAuditRow row) { try { _selected = row; Paint(); } catch (Exception error) { Log(error); } }
        private void Table_Closed(object sender, EventArgs args)
        {
            try
            {
                ApmTableWindow table = (ApmTableWindow)sender;
                table.SelectedEvent -= Table_SelectedEvent; table.Closed -= Table_Closed;
                string key = _tables.First(p => ReferenceEquals(p.Value, table)).Key;
                _tables.Remove(key);
            }
            catch (Exception error) { Log(error); }
        }
        private void ButtonResume_Click(object sender, RoutedEventArgs args)
        { try { _selected = null; _controller.Pause(false); Paint(); } catch (Exception error) { Log(error); } }
        private void CheckBoxDetails_Changed(object sender, RoutedEventArgs args)
        {
            try { _controller.DetailedDiagnostics = CheckBoxDetails.IsChecked == true; }
            catch (Exception error) { Log(error); }
        }
        private void ButtonPause_Click(object sender, RoutedEventArgs args)
        { try { _selected = null; _controller.Pause(true); Paint(); } catch (Exception error) { Log(error); } }
        private void ButtonClose_Click(object sender, RoutedEventArgs args)
        { try { _selected = null; _controller.Close(); Paint(); } catch (Exception error) { Log(error); } }
        private void ButtonEmergency_Click(object sender, RoutedEventArgs args)
        { try { _selected = null; _controller.Close("EMERGENCY_EXIT"); Paint(); } catch (Exception error) { Log(error); } }
        private void ButtonParameters_Click(object sender, RoutedEventArgs args) { try { _parameters?.Invoke(); } catch (Exception error) { Log(error); } }
        private void ButtonNow_Click(object sender, RoutedEventArgs args) { try { _selected = null; Paint(); } catch (Exception error) { Log(error); } }
        private void ButtonNext_Click(object sender, RoutedEventArgs args)
        {
            try
            {
                _selected = _controller.Recent.FirstOrDefault(r => r.Kind == "Decision" && r.Volume > 0 && r.Sequence > (_selected?.Sequence ?? -1)) ?? _selected;
                Paint();
            }
            catch (Exception error) { Log(error); }
        }
        private void Window_Closed(object sender, EventArgs args)
        {
            try
            {
                _timer.Stop(); _timer.Tick -= Timer_Tick;
                _controller.DetailedDiagnostics = false;
                CheckBoxDetails.Checked -= CheckBoxDetails_Changed; CheckBoxDetails.Unchecked -= CheckBoxDetails_Changed;
                ButtonResume.Click -= ButtonResume_Click;
                ButtonPause.Click -= ButtonPause_Click; ButtonClose.Click -= ButtonClose_Click;
                ButtonEmergency.Click -= ButtonEmergency_Click; ButtonParameters.Click -= ButtonParameters_Click;
                ButtonNext.Click -= ButtonNext_Click; ButtonNow.Click -= ButtonNow_Click;
                foreach (Button button in TableButtons()) button.Click -= ButtonTable_Click;
                foreach (ApmTableWindow table in _tables.Values.ToArray()) table.Close();
                Closed -= Window_Closed;
            }
            catch (Exception error) { Log(error); }
        }
        private static void Log(Exception error) => ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error);
    }
}
