using OsEngine.Language;
using OsEngine.Logging;
using OsEngine.Market;
using System;
using System.Globalization;
using System.Linq;
using System.Windows;

namespace OsEngine.OsData.OrderFlow.Calibration
{
    /// <summary>Exact saved Cloud anatomy. Summary remains visible; all constituent rows open in independent read-only page windows.</summary>
    public partial class CloudAnatomyWindow : Window
    {
        private readonly CalibrationMarker _marker;
        private readonly CalibrationWindowSet _tables = new CalibrationWindowSet();
        internal CloudAnatomyWindow(CalibrationMarker marker)
        {
            InitializeComponent(); _marker = marker; Title = "Cloud Anatomy · " + marker.Rule.Name;
            ButtonTicks.Content = L("Open table — Cloud ticks", "Открыть таблицу — тики Cloud");
            ButtonLevels.Content = L("Open table — price levels", "Открыть таблицу — ценовые уровни");
            ButtonPairs.Content = L("Open table — inside pairs", "Открыть таблицу — inside пары");
            ButtonContext.Content = L("Open table — context ticks", "Открыть таблицу — context тики");
            ButtonContextPairs.Content = L("Open table — context pairs", "Открыть таблицу — context пары");
            CalibrationEventRow c = CalibrationEventRow.Create(marker.Event, marker.Run.Spec, marker.Rule);
            DiagonalMetrics inside = DiagonalMetrics.Calculate(marker.Event.Evidence.Inside, marker.Run.Spec.PriceStep, marker.Rule.Filters.Diagonal);
            DiagonalMetrics context = DiagonalMetrics.Calculate(marker.Event.Evidence.Context, marker.Run.Spec.PriceStep, marker.Rule.Filters.Diagonal);
            TextBlockSummary.Text = marker.Run.Spec.Range.Name + " · " + marker.Rule.Name + "\nFormationHash " + marker.Rule.FormationHash +
                "\nCalibrationSpecHash " + marker.Run.Spec.Hash + "\nInput SHA-256 " + marker.Run.Spec.InputSha256 + "\n" + CalibrationSpec.Formulas + "\n" +
                string.Join("\n", typeof(CalibrationEventRow).GetProperties().Select(p => CalibrationText.Label(p.Name) + "   " + Scalar(p.GetValue(c)))) +
                "\n" + L("Inside diagonal Δ / Δ% / Buy ratio / Sell ratio / Buy stack / Sell stack", "Внутри: диагональная Δ / Δ% / Buy ratio / Sell ratio / стек покупок / стек продаж") +
                "   " + inside.Delta + " / " + inside.DeltaPercent + " / " + inside.BuyRatio + " / " + inside.SellRatio + " / " + inside.BuyStack + " / " + inside.SellStack +
                "\n" + L("Context diagonal Δ / Δ% / Buy ratio / Sell ratio / Buy stack / Sell stack", "Контекст: диагональная Δ / Δ% / Buy ratio / Sell ratio / стек покупок / стек продаж") +
                "   " + context.Delta + " / " + context.DeltaPercent + " / " + context.BuyRatio + " / " + context.SellRatio + " / " + context.BuyStack + " / " + context.SellStack;
            ButtonTicks.Click += TicksClick; ButtonLevels.Click += LevelsClick; ButtonPairs.Click += PairsClick;
            ButtonContext.Click += ContextClick; ButtonContextPairs.Click += ContextPairsClick; Closed += WindowClosed;
        }
        private static string L(string en, string ru) => OsLocalization.ConvertToLocString("Eng:" + en + "_Ru:" + ru + "_");
        private static string Scalar(object value) => value is DateTime time ? time.ToString("O", CultureInfo.InvariantCulture) : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        private void Open(string key, CalibrationTableSource source)
        { _tables.Open(key, () => new CalibrationTableWindow(source, _marker.CloudId)); }
        private void TicksClick(object sender, RoutedEventArgs e)
        { try { Open("ticks", CalibrationTableSource.Create(L("Cloud ticks", "Тики Cloud"), t => CalibrationAnatomy.Ticks(_marker.Run, _marker.Rule.Formation, _marker.Event, false, t))); } catch (Exception error) { Report(error); } }
        private void LevelsClick(object sender, RoutedEventArgs e)
        { try { Open("levels", CalibrationTableSource.Create(L("Price levels", "Ценовые уровни"), t => CalibrationAnatomy.Levels(_marker.Run, _marker.Rule.Formation, _marker.Event, t))); } catch (Exception error) { Report(error); } }
        private void PairsClick(object sender, RoutedEventArgs e)
        { try { Open("pairs", CalibrationTableSource.Create(L("Inside diagonal pairs", "Диагональные пары внутри"), t => CalibrationAnatomy.Pairs(_marker.Run, _marker.Event, _marker.Rule.Filters.Diagonal with { Source = DiagonalSource.Inside }, t))); } catch (Exception error) { Report(error); } }
        private void ContextClick(object sender, RoutedEventArgs e)
        { try { Open("context", CalibrationTableSource.Create(L("Context ticks", "Тики контекста"), t => CalibrationAnatomy.Ticks(_marker.Run, _marker.Rule.Formation, _marker.Event, true, t))); } catch (Exception error) { Report(error); } }
        private void ContextPairsClick(object sender, RoutedEventArgs e)
        { try { Open("context-pairs", CalibrationTableSource.Create(L("Context diagonal pairs", "Диагональные пары контекста"), t => CalibrationAnatomy.Pairs(_marker.Run, _marker.Event, _marker.Rule.Filters.Diagonal with { Source = DiagonalSource.Context }, t))); } catch (Exception error) { Report(error); } }
        private static void Report(Exception error) => ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error);
        private void WindowClosed(object sender, EventArgs e)
        {
            try { _tables.Dispose(); ButtonTicks.Click -= TicksClick; ButtonLevels.Click -= LevelsClick; ButtonPairs.Click -= PairsClick;
                ButtonContext.Click -= ContextClick; ButtonContextPairs.Click -= ContextPairsClick; Closed -= WindowClosed; }
            catch (Exception error) { Report(error); }
        }
    }
}
