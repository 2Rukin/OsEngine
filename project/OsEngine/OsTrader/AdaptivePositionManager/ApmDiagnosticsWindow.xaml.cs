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
    /// controller. Historical selection renders stored snapshots without recalculating policy or orders.
    /// </summary>
    public partial class ApmDiagnosticsWindow : Window
    {
        private readonly ApmExecutionController _controller;
        private readonly Action _parameters;
        private readonly DispatcherTimer _timer;
        private readonly Dictionary<string, ApmTableWindow> _tables = new Dictionary<string, ApmTableWindow>();
        private ApmAuditRow _selected;
        private string _previewState;
        private decimal? _nextAdd;
        private decimal? _nextReduce;

        /// <summary>Construct on the WPF dispatcher. The controller remains owned by the robot.</summary>
        public ApmDiagnosticsWindow(ApmExecutionController controller, Action parameters)
        {
            InitializeComponent();
            _controller = controller;
            _controller.DetailedDiagnostics = true;
            Width = Math.Min(Width, SystemParameters.WorkArea.Width);
            Height = Math.Min(Height, SystemParameters.WorkArea.Height);
            _parameters = parameters;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _timer.Tick += Timer_Tick;
            CheckBoxDetails.Checked += CheckBoxDetails_Changed;
            CheckBoxDetails.Unchecked += CheckBoxDetails_Changed;
            ButtonStart.Click += ButtonResume_Click;
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
            TextBlockStatus.Text = "ResearchOnly · TradeOnly · " + _controller.ExecutionModel + " · " + (_selected == null ? "Текущий момент" : "Сохранённый snapshot")
                + "\n" + state.CampaignId + " · " + _controller.Spec.Direction + " · " + state.State + " · " + state.Regime
                + " · Filled " + state.FilledVolume + " · Raw " + state.Decision?.RawTarget
                + " · Allowed " + state.Decision?.RiskAllowedTarget + " · Pending +" + state.PendingIncrease + "/−" + state.PendingReduce
                + "\nСредняя " + state.AverageEntry + " · Equity " + state.Equity + " · Просадка " + state.MaximumDrawdown
                + " · ExitLatch " + state.ExitLatch
                + " · Replay callbacks " + (_controller.ReplayHealth.Stalled ? "пауза/задержка" : "идут")
                + " · Готовность " + state.Market?.Ready + " / " + state.Market?.Quality
                + "\nБюджет " + _controller.Spec.RiskBudgetCurrency + " " + _controller.Spec.RiskCurrency
                + " · Стресс-риск открытого остатка " + state.FilledVolume * (ApmMathematics.UnitStopRisk(_controller.Spec, state.AverageEntry) + _controller.Spec.FeePerContract)
                + "\nQcurve " + state.Decision?.CurveTarget + " → κ " + state.Decision?.Kappa
                + " → Raw " + state.Decision?.RawTarget + " → Policy " + state.Decision?.PolicyTarget
                + " → Allowed " + state.Decision?.RiskAllowedTarget
                + " · Preview ADD " + (_nextAdd?.ToString() ?? "нет") + " / REDUCE " + (_nextReduce?.ToString() ?? "нет")
                + "\n" + string.Join("; ", (state.Decision?.Reasons ?? Array.Empty<string>()).Select(ApmDisplayText.Reason));
            DrawPrice(rows, state);
            DrawVolume(rows);
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
                ApmTableWindow window = new ApmTableWindow(_controller, name);
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
        private void ButtonResume_Click(object sender, RoutedEventArgs args) { try { _controller.Pause(false); } catch (Exception error) { Log(error); } }
        private void CheckBoxDetails_Changed(object sender, RoutedEventArgs args)
        {
            try { _controller.DetailedDiagnostics = CheckBoxDetails.IsChecked == true; }
            catch (Exception error) { Log(error); }
        }
        private void ButtonPause_Click(object sender, RoutedEventArgs args) { try { _controller.Pause(true); } catch (Exception error) { Log(error); } }
        private void ButtonClose_Click(object sender, RoutedEventArgs args) { try { _controller.Close(); } catch (Exception error) { Log(error); } }
        private void ButtonEmergency_Click(object sender, RoutedEventArgs args) { try { _controller.Close("EMERGENCY_EXIT"); } catch (Exception error) { Log(error); } }
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
                ButtonStart.Click -= ButtonResume_Click; ButtonResume.Click -= ButtonResume_Click;
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
