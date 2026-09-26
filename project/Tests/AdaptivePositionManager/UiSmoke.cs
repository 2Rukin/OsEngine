using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using OsEngine.OsTrader.AdaptivePositionManager;

namespace OsEngine.AdaptivePositionManager.Tests
{
    /// <summary>
    /// Explicit owner-authorized GUI smoke in a separate clean working directory. Captures only its own
    /// HWND at the actual monitor DPI. Resizing does not certify other physical DPI settings.
    /// </summary>
    internal static class UiSmoke
    {
        internal static int Run(string output)
        {
            Directory.CreateDirectory(output);
            Application application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            application.Resources.MergedDictionaries.Add(new ResourceDictionary
                { Source = new Uri("/OsEngine;component/Themes/ThemeDarkOrange.xaml", UriKind.Relative) });
            List<object> evidence = new List<object>();
            try
            {
                foreach (string scenario in new[] { "S01", "S02", "S08", "S09", "S19" })
                {
                    Gateway gateway = new Gateway { Unknown = scenario == "S19" };
                    ApmPolicy policy = new ApmPolicy { FastEnabled = scenario == "S08" || scenario == "S09" };
                    using ApmExecutionController controller = new ApmExecutionController(new ApmCampaign(CoreTests.Spec(), policy), gateway, null);
                    gateway.Controller = controller;
                    controller.DetailedDiagnostics = true;
                    decimal[] prices = scenario == "S02" ? new decimal[] { 100, 98, 96, 98, 96, 98 }
                        : new decimal[] { 100, 102, 104, 102, 100, 104, 102 };
                    for (int i = 0; i < prices.Length; i++)
                    {
                        try { controller.Process(CoreTests.Market(prices[i], i, true, i == 0 ? 0 : scenario == "S08" ? 4 : scenario == "S09" ? -4 : 0)); }
                        catch (ApmExecutionUncertainException) { }
                    }
                    Func<ApmRunDiagnosticView> runView = () => BuildRunView(controller, scenario == "S01");
                    ApmDiagnosticsWindow window = new ApmDiagnosticsWindow(controller, null, runView) { Left = 10, Top = 10 };
                    window.Show();
                    Pump(400);
                    if (scenario == "S01")
                    {
                        VerifyPauseHistory(window, controller);
                    }
                    string traceBefore = JsonSerializer.Serialize(controller.Recent);
                    int monitorDpi = (int)GetDpiForWindow(new WindowInteropHelper(window).Handle);
                    Console.WriteLine("UI_ACTUAL_DPI " + monitorDpi);
                    foreach (bool minimumSize in scenario == "S01" ? new[] { false, true } : new[] { false })
                    {
                        window.Width = minimumSize ? window.MinWidth : Math.Min(1280 * 96.0 / monitorDpi, SystemParameters.WorkArea.Width);
                        window.Height = minimumSize ? window.MinHeight : Math.Min(720 * 96.0 / monitorDpi, SystemParameters.WorkArea.Height);
                        Pump(200);
                        VerifyButtons(window);
                        string file = Path.Combine(output, scenario + (minimumSize ? "-minimum" : "-viewport") + ".png");
                        NativeRect bounds = Capture(window, file);
                        evidence.Add(new { scenario, minimumSize, actualMonitorDpi = monitorDpi,
                            physicalWidth = bounds.Right - bounds.Left, physicalHeight = bounds.Bottom - bounds.Top,
                            file = Path.GetFileName(file), physicalDpiQualification = "ACTUAL_MONITOR_ONLY" });
                    }
                    Click(window, "ButtonOrders"); Click(window, "ButtonDecisions");
                    Click(window, "ButtonCampaigns"); Click(window, "ButtonQuality"); Click(window, "ButtonReport");
                    Pump(100);
                    Program.Equal(5, application.Windows.OfType<ApmTableWindow>().Count(), "five independent table windows");
                    Program.Check(application.Windows.OfType<ApmTableWindow>().All(t => t.Owner == null), "tables have independent HWND ownership");
                    if (scenario == "S01")
                    {
                        ApmTableWindow campaigns = application.Windows.OfType<ApmTableWindow>().Single(t => t.Title == "APM — Кампании");
                        ApmTableWindow report = application.Windows.OfType<ApmTableWindow>().Single(t => t.Title == "APM — Отчёт");
                        Program.Equal(2, ((DataGrid)campaigns.FindName("DataGridRows")).Items.Count,
                            "campaign window renders one typed row per campaign");
                        Program.Equal(2, ((DataGrid)report.FindName("DataGridRows")).Items.Count,
                            "report window renders distinct campaign summaries");
                    }
                    Click(window, "ButtonDecisions");
                    Program.Equal(5, application.Windows.OfType<ApmTableWindow>().Count(), "reopen activates existing table");
                    ApmTableWindow decisions = application.Windows.OfType<ApmTableWindow>().Single(t => t.Title == "APM — Решения");
                    VerifyTable(decisions, controller, output, scenario);
                    decisions.Close();
                    Click(window, "ButtonDecisions");
                    Program.Equal(5, application.Windows.OfType<ApmTableWindow>().Count(), "close/reopen does not duplicate subscriptions");
                    window.Close(); Pump(50);
                    Program.Equal(0, application.Windows.Count, "closing diagnostics cleans every child window");
                    Program.Equal(traceBefore, JsonSerializer.Serialize(controller.Recent), "GUI sorting/windows never change canonical trading trace");
                }
                VerifyCloseControl("ButtonClose", "MANUAL_EXIT");
                VerifyCloseControl("ButtonEmergency", "EMERGENCY_EXIT");
                File.WriteAllText(Path.Combine(output, "ui-evidence.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine("UI_SMOKE_PASS " + output);
                return 0;
            }
            finally { application.Shutdown(); }
        }

        internal static void Pump(int milliseconds)
        {
            DateTime until = DateTime.UtcNow.AddMilliseconds(milliseconds);
            while (DateTime.UtcNow < until)
            {
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(Empty));
                Thread.Sleep(10);
            }
        }

        private static void Empty() { }
        private static void Click(Window window, string name) => ((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        private static void VerifyPauseHistory(ApmDiagnosticsWindow window, ApmExecutionController controller)
        {
            Click(window, "ButtonPause");
            Program.Equal("АКТИВНА", ((TextBlock)window.FindName("StatusPause")).Text, "current pause is visible");
            Click(window, "ButtonNext");
            Program.Equal("Сохранённый snapshot", ((TextBlock)window.FindName("StatusMoment")).Text,
                "next decision selects historical snapshot");
            Program.Equal("нет", ((TextBlock)window.FindName("StatusPause")).Text, "historical running state remains visible");
            Program.Equal(ApmState.PausedNoIncrease, controller.CaptureView().Snapshot.State, "history never resumes controller");
            Program.Equal(FontWeights.Bold, ((Button)window.FindName("ButtonPause")).FontWeight,
                "current pause button stays active while viewing pre-pause history");
            controller.Process(CoreTests.Market(102, 7));
            ApmAuditRow paused = controller.Recent.Last(r => r.Kind == "Decision" && r.Snapshot.State == ApmState.PausedNoIncrease);
            Click(window, "ButtonResume");
            ApmState resumed = controller.CaptureView().Snapshot.State;
            Program.Check(resumed != ApmState.PausedNoIncrease, "resume clears current pause");
            Click(window, "ButtonDecisions");
            ApmTableWindow table = Application.Current.Windows.OfType<ApmTableWindow>().Single();
            DataGrid grid = (DataGrid)table.FindName("DataGridRows");
            grid.SelectedItem = grid.Items.Cast<ApmAuditRow>().Single(r => r.Sequence == paused.Sequence);
            Click(table, "ButtonSelect");
            Program.Equal("АКТИВНА", ((TextBlock)window.FindName("StatusPause")).Text, "historical paused state remains visible");
            Program.Equal(resumed, controller.CaptureView().Snapshot.State, "history never pauses resumed controller");
            Program.Check(((Button)window.FindName("ButtonPause")).FontWeight != FontWeights.Bold,
                "current pause button stays inactive while viewing paused history");
            table.Close();
            Click(window, "ButtonNow");
            Program.Equal("нет", ((TextBlock)window.FindName("StatusPause")).Text, "now restores current status");
        }

        private static void VerifyCloseControl(string button, string reason)
        {
            Gateway gateway = new Gateway();
            using ApmExecutionController controller = new ApmExecutionController(new ApmCampaign(CoreTests.Spec(), new ApmPolicy()), gateway, null);
            gateway.Controller = controller;
            controller.Process(CoreTests.Market(100, 0));
            Program.Check(controller.CaptureView().Snapshot.FilledVolume > 0, "close UI test starts with filled position");
            ApmDiagnosticsWindow window = new ApmDiagnosticsWindow(controller, null);
            window.Show(); Pump(50);
            Click(window, button);
            ApmSnapshot state = controller.CaptureView().Snapshot;
            Program.Check(state.ExitLatch, button + " latches exit");
            Program.Equal(reason, state.ExitReason, button + " preserves explicit exit reason");
            Program.Equal(0m, state.FilledVolume, button + " closes synthetic position");
            Program.Equal(reason, ((TextBlock)window.FindName("StatusExitReason")).Text, button + " paints reason");
            window.Close();
            controller.Process(CoreTests.Market(100, 1));
            Program.Equal(0m, controller.CaptureView().Snapshot.FilledVolume, "closing diagnostics never clears exit latch");
            Program.Equal(0, Application.Current.Windows.Count, "close-control smoke leaves no windows");
        }

        private static ApmRunDiagnosticView BuildRunView(ApmExecutionController controller, bool includeSecond)
        {
            ApmRunDiagnosticView current = ApmDiagnosticProjection.Capture(controller);
            if (!includeSecond) return current;
            ApmCampaignSummaryRow secondCampaign = current.Campaigns[0] with { CampaignId = "synthetic-second" };
            ApmReportSummaryRow secondReport = current.Report[0] with { CampaignId = "synthetic-second" };
            return new ApmRunDiagnosticView(new[] { current.Campaigns[0], secondCampaign },
                new[] { current.Report[0], secondReport });
        }

        private static void VerifyButtons(ApmDiagnosticsWindow window)
        {
            foreach (string name in new[] { "ButtonStart", "ButtonPause", "ButtonResume", "ButtonClose", "ButtonEmergency",
                "ButtonDecisions", "ButtonOrders", "ButtonCampaigns", "ButtonQuality", "ButtonParameters", "ButtonReport",
                "ButtonNext", "ButtonNow" })
            {
                Button button = (Button)window.FindName(name);
                FrameworkElement viewport = (FrameworkElement)window.Content;
                Rect bounds = button.TransformToAncestor(viewport).TransformBounds(new Rect(button.RenderSize));
                Program.Check(bounds.Top >= 0 && bounds.Left >= 0 && bounds.Bottom <= viewport.ActualHeight
                    && bounds.Right <= viewport.ActualWidth,
                    "critical control within " + window.Width + "x" + window.Height + " viewport: " + name);
            }
            Button start = (Button)window.FindName("ButtonStart");
            Program.Check(!start.IsEnabled && Convert.ToString(start.ToolTip).Contains("schedule"),
                "manual start is disabled with schedule/Tester explanation");
            Program.Check(window.FindName("GroupCampaign") != null && window.FindName("GroupPosition") != null
                && window.FindName("GroupRisk") != null && window.FindName("GroupExecution") != null,
                "status is split into campaign position risk and execution groups");
        }

        private static void VerifyTable(ApmTableWindow table, ApmExecutionController controller, string output, string scenario)
        {
            TextBox action = (TextBox)table.FindName("TextBoxAction");
            action.Text = "Reduce";
            Click(table, "ButtonRefresh");
            DataGrid grid = (DataGrid)table.FindName("DataGridRows");
            ApmAuditRow[] filtered = grid.Items.Cast<ApmAuditRow>().ToArray();
            Program.Check(filtered.All(r => r.Action == "Reduce"), "actual UI action filter");
            ApmArtifacts.ExportCsv(Path.Combine(output, scenario + "-filtered.csv"), filtered);
            if (filtered.Length > 0)
            {
                grid.SelectedIndex = 0;
                Click(table, "ButtonSelect");
                Pump(100);
            }
            ApmAuditRow template = controller.Recent.First();
            ApmAuditRow[] numeric = new[] { 2m, 100m, -10m, 10m }.Select(n => template with { Volume = n }).ToArray();
            grid.ItemsSource = numeric;
            ICollectionView view = CollectionViewSource.GetDefaultView(grid.ItemsSource);
            view.SortDescriptions.Add(new SortDescription("Volume", ListSortDirection.Ascending));
            Program.Equal("-10,2,10,100", string.Join(",", grid.Items.Cast<ApmAuditRow>().Select(r => r.Volume)), "WPF numeric ascending sort");
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription("Volume", ListSortDirection.Descending));
            Program.Equal("100,10,2,-10", string.Join(",", grid.Items.Cast<ApmAuditRow>().Select(r => r.Volume)), "WPF numeric descending sort");
        }

        internal static NativeRect Capture(Window window, string path)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (!GetWindowRect(handle, out NativeRect rect)) throw new InvalidOperationException("Cannot measure UI HWND.");
            using System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(rect.Right - rect.Left, rect.Bottom - rect.Top);
            using System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(bitmap);
            IntPtr dc = graphics.GetHdc();
            try { if (!PrintWindow(handle, dc, 2)) throw new InvalidOperationException("Cannot capture own UI HWND."); }
            finally { graphics.ReleaseHdc(dc); }
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            return rect;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct NativeRect { internal int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr handle);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PrintWindow(IntPtr handle, IntPtr dc, uint flags);

        private sealed class Gateway : IApmOrderGateway
        {
            internal ApmExecutionController Controller;
            internal bool Unknown;
            public void Send(ApmIntent intent)
            {
                if (Unknown) throw new IOException("Synthetic unknown delivery for UI scenario S19");
                Controller.ApplyFill(new ApmFill("ui-" + intent.Id, intent.Id, intent.Time, intent.PriceBound, intent.Volume, 0));
                Controller.ApplyOrder(intent.Id, ApmOrderState.Filled, intent.Volume, "ui-order-" + intent.Id);
            }
            public void Cancel(ApmIntent intent) => Controller.ApplyOrder(intent.Id, ApmOrderState.Canceled, intent.Filled, "ui-order-" + intent.Id);
        }
    }
}
