using OsEngine.Language;
using OsEngine.Logging;
using OsEngine.Market;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace OsEngine.OsData.OrderFlow
{
    /// <summary>
    /// User-operated public archive downloader. Each action owns one cancellable operation;
    /// closing waits for that operation to finish cleanup before detaching handlers.
    /// </summary>
    public partial class OrderFlowDownloadUi
    {
        private OrderFlowArchiveCatalog _catalog;
        private readonly List<OrderFlowArchiveDayRow> _rows = new List<OrderFlowArchiveDayRow>();
        private CancellationTokenSource _cancellation;
        private bool _closeAfterCancellation;

        internal OrderFlowArchivePair SelectedPair { get; private set; }

        /// <summary>Creates the selector without issuing HTTP requests or starting research.</summary>
        public OrderFlowDownloadUi()
        {
            InitializeComponent();
            Layout.StickyBorders.Listen(this);
            Layout.StartupLocation.Start_FitHeightToWorkArea(this);
            DateFrom.SelectedDate = DateTime.Today.AddDays(-7);
            DateTo.SelectedDate = DateTime.Today;
            TextBoxFolder.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "QScalp");
            GridDays.ItemsSource = _rows;
            ApplyLocalization();
            ButtonCatalog.Click += ButtonCatalog_Click;
            ButtonDownload.Click += ButtonDownload_Click;
            ButtonCancel.Click += ButtonCancel_Click;
            ButtonFolder.Click += ButtonFolder_Click;
            ButtonOpen.Click += ButtonOpen_Click;
            DateFrom.SelectedDateChanged += Period_Changed;
            DateTo.SelectedDateChanged += Period_Changed;
            ComboInstrument.SelectionChanged += Instrument_Changed;
            GridDays.SelectionChanged += Day_Changed;
            Closing += Window_Closing;
            Closed += Window_Closed;
        }

        #region Presentation

        private static string L(string english, string russian)
        {
            return OsLocalization.CurLocalization == OsLocalization.OsLocalType.Ru ? russian : english;
        }

        private void ApplyLocalization()
        {
            Title = L("Download QSH — QScalp archive", "Загрузка QSH — архив QScalp");
            TextBlockHelp.Text = L(
                "Source: https://erinrv.qscalp.ru/ • Choose inclusive dates, load instruments, then download Deals + Quotes. Each day is saved separately. Open one ready day to research; review price and volume steps before running.",
                "Источник: https://erinrv.qscalp.ru/ • Выберите даты включительно, получите список инструментов и загрузите Deals + Quotes. Каждый день сохраняется отдельно. Откройте готовый день для исследования; перед расчётом проверьте шаг цены и объёма.");
            LabelFrom.Content = L("From", "С");
            LabelTo.Content = L("Through", "По");
            LabelInstrument.Content = L("Instrument", "Инструмент");
            LabelFolder.Content = L("Download folder", "Папка загрузки");
            ButtonCatalog.Content = L("Load instruments", "Получить инструменты");
            ButtonFolder.Content = L("Browse", "Выбрать");
            ButtonDownload.Content = L("Download period", "Загрузить период");
            ButtonCancel.Content = L("Cancel", "Отмена");
            ButtonOpen.Content = L("Open selected day", "Открыть выбранный день");
            ColumnDate.Header = L("Date", "Дата");
            ColumnSize.Header = L("Bytes", "Байт");
            ColumnStatus.Header = L("Status", "Состояние");
            TextBlockStatus.Text = L("Select dates and load instruments.", "Выберите период и нажмите «Получить инструменты».");
        }

        private void UpdateControls()
        {
            bool busy = _cancellation != null;
            PanelPeriod.IsEnabled = !busy;
            GridFolder.IsEnabled = !busy;
            ButtonCancel.IsEnabled = busy;
            ButtonDownload.IsEnabled = !busy && _rows.Any(row => row.Files.Count == 2);
            ButtonOpen.IsEnabled = !busy && GridDays.SelectedItem is OrderFlowArchiveDayRow row && row.Pair != null;
        }

        private void FillRows()
        {
            _rows.Clear();
            string instrument = ComboInstrument.SelectedItem as string;
            if (_catalog != null && instrument != null)
            {
                int days = (int)(_catalog.To - _catalog.From).TotalDays;
                for (int offset = 0; offset <= days; offset++)
                {
                    DateTime date = _catalog.From.AddDays(offset);
                    bool listed = _catalog.Days.TryGetValue(date, out List<OrderFlowArchiveFile> allFiles);
                    List<OrderFlowArchiveFile> files = listed
                        ? allFiles.Where(file => file.Instrument == instrument).ToList()
                        : new List<OrderFlowArchiveFile>();
                    _rows.Add(new OrderFlowArchiveDayRow
                    {
                        Date = date, Files = files,
                        Status = !listed ? L("Date is absent from archive", "Дата отсутствует в архиве")
                            : files.Count == 2 ? L("Ready to download", "Доступен для загрузки")
                            : files.Count == 0 ? L("Instrument is absent", "Инструмент отсутствует")
                            : L("Incomplete pair: missing ", "Неполная пара: нет ") +
                                (files[0].Role == "Deals" ? "Quotes" : "Deals")
                    });
                }
            }
            GridDays.Items.Refresh();
            TextBlockAvailability.Text = L("Days with a full pair: ", "Дней с полной парой: ") +
                _rows.Count(row => row.Files.Count == 2) + " / " + _rows.Count;
            UpdateControls();
        }

        private void ShowError(Exception error)
        {
            ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error);
            TextBlockStatus.Text = L("Error: ", "Ошибка: ") + error.Message;
        }

        #endregion

        #region Operations

        private async void ButtonCatalog_Click(object sender, RoutedEventArgs e)
        {
            await RunOperationAsync(async cancellation =>
            {
                if (!DateFrom.SelectedDate.HasValue || !DateTo.SelectedDate.HasValue)
                {
                    throw new ArgumentException(L("Select both dates.", "Выберите обе даты."));
                }
                _catalog = null;
                ComboInstrument.ItemsSource = null;
                FillRows();
                Progress.IsIndeterminate = true;
                TextBlockStatus.Text = L("Reading archive index…", "Чтение каталога архива…");
                using OrderFlowArchiveClient client = new OrderFlowArchiveClient();
                _catalog = await client.LoadCatalogAsync(DateFrom.SelectedDate.Value, DateTo.SelectedDate.Value,
                    CreateProgress(cancellation), cancellation.Token);
                ComboInstrument.ItemsSource = _catalog.Instruments;
                ComboInstrument.SelectedIndex = _catalog.Instruments.Count > 0 ? 0 : -1;
                TextBlockStatus.Text = L("Instruments: ", "Инструментов: ") + _catalog.Instruments.Count +
                    L(". Select one and download the period.", ". Выберите инструмент и загрузите период.");
            });
        }

        private async void ButtonDownload_Click(object sender, RoutedEventArgs e)
        {
            await RunOperationAsync(async cancellation =>
            {
                if (string.IsNullOrWhiteSpace(TextBoxFolder.Text))
                {
                    throw new ArgumentException(L("Choose a download folder.", "Выберите папку загрузки."));
                }
                string folder = Path.GetFullPath(TextBoxFolder.Text.Trim());
                List<OrderFlowArchiveDayRow> available = _rows.Where(row => row.Files.Count == 2).ToList();
                Progress.Maximum = Math.Max(1, available.Count);
                Progress.Value = 0;
                using OrderFlowArchiveClient client = new OrderFlowArchiveClient();
                foreach (OrderFlowArchiveDayRow row in available)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    row.Pair = null;
                    row.Status = L("Downloading / verifying…", "Загрузка / проверка…");
                    GridDays.Items.Refresh();
                    try
                    {
                        row.Pair = await client.DownloadPairAsync(row.Files, folder, CreateProgress(cancellation), cancellation.Token);
                        row.Status = row.Pair.Reused ? L("Ready (verified local pair)", "Готово (локальная пара проверена)")
                            : L("Downloaded", "Загружено");
                    }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                    {
                        row.Status = L("Cancelled", "Отменено");
                        throw;
                    }
                    catch (Exception error)
                    {
                        ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error);
                        row.Status = L("Error: ", "Ошибка: ") + error.Message;
                    }
                    finally
                    {
                        GridDays.Items.Refresh();
                    }
                    Progress.Value++;
                }
                TextBlockStatus.Text = L("Finished. Ready days: ", "Завершено. Готовых дней: ") +
                    available.Count(row => row.Pair != null) + " / " + available.Count +
                    L(". Select a ready day to open.", ". Выберите готовый день для открытия.");
                GridDays.SelectedItem = available.LastOrDefault(row => row.Pair != null);
            });
        }

        private IProgress<string> CreateProgress(CancellationTokenSource operation)
        {
            return new Progress<string>(message =>
            {
                if (_cancellation == operation && !operation.IsCancellationRequested)
                {
                    TextBlockStatus.Text = message;
                }
            });
        }

        private async Task RunOperationAsync(Func<CancellationTokenSource, Task> operation)
        {
            if (_cancellation != null)
            {
                return;
            }
            _cancellation = new CancellationTokenSource();
            UpdateControls();
            try
            {
                await operation(_cancellation);
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
            {
                TextBlockStatus.Text = L("Cancelled. Completed days remain available: ",
                    "Отменено. Завершённые дни остаются доступны: ") + _rows.Count(row => row.Pair != null);
            }
            catch (Exception error)
            {
                ShowError(error);
            }
            finally
            {
                _cancellation.Dispose();
                _cancellation = null;
                Progress.IsIndeterminate = false;
                UpdateControls();
                if (_closeAfterCancellation)
                {
                    Close();
                }
            }
        }

        #endregion

        #region Events and lifetime

        private void Period_Changed(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                _catalog = null;
                ComboInstrument.ItemsSource = null;
                FillRows();
                TextBlockStatus.Text = L("Period changed; load instruments again.", "Период изменён; получите список инструментов заново.");
            }
            catch (Exception error) { ShowError(error); }
        }

        private void Instrument_Changed(object sender, SelectionChangedEventArgs e)
        {
            try { FillRows(); }
            catch (Exception error) { ShowError(error); }
        }

        private void Day_Changed(object sender, SelectionChangedEventArgs e)
        {
            try { UpdateControls(); }
            catch (Exception error) { ShowError(error); }
        }

        private void ButtonCancel_Click(object sender, RoutedEventArgs e)
        {
            try { _cancellation?.Cancel(); }
            catch (Exception error) { ShowError(error); }
        }

        private void ButtonFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Microsoft.Win32.OpenFolderDialog dialog = new Microsoft.Win32.OpenFolderDialog();
                if (Directory.Exists(TextBoxFolder.Text)) { dialog.InitialDirectory = TextBoxFolder.Text; }
                if (dialog.ShowDialog(this) == true) { TextBoxFolder.Text = dialog.FolderName; }
            }
            catch (Exception error) { ShowError(error); }
        }

        private void ButtonOpen_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_cancellation == null && GridDays.SelectedItem is OrderFlowArchiveDayRow row && row.Pair != null)
                {
                    SelectedPair = row.Pair;
                    DialogResult = true;
                }
            }
            catch (Exception error) { ShowError(error); }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            try
            {
                if (_cancellation != null)
                {
                    e.Cancel = true;
                    _closeAfterCancellation = true;
                    _cancellation.Cancel();
                    TextBlockStatus.Text = L("Cancelling and cleaning up…", "Отмена и удаление незавершённой загрузки…");
                }
            }
            catch (Exception error) { ShowError(error); }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            ButtonCatalog.Click -= ButtonCatalog_Click;
            ButtonDownload.Click -= ButtonDownload_Click;
            ButtonCancel.Click -= ButtonCancel_Click;
            ButtonFolder.Click -= ButtonFolder_Click;
            ButtonOpen.Click -= ButtonOpen_Click;
            DateFrom.SelectedDateChanged -= Period_Changed;
            DateTo.SelectedDateChanged -= Period_Changed;
            ComboInstrument.SelectionChanged -= Instrument_Changed;
            GridDays.SelectionChanged -= Day_Changed;
            Closing -= Window_Closing;
            Closed -= Window_Closed;
        }

        #endregion
    }

    internal sealed class OrderFlowArchiveDayRow
    {
        public DateTime Date { get; set; }
        public string DateText => Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        public List<OrderFlowArchiveFile> Files { get; set; }
        public string SizeText => Files.Count == 2 ? Files.Sum(file => file.Length).ToString("N0", CultureInfo.InvariantCulture) : "—";
        public string Status { get; set; }
        public OrderFlowArchivePair Pair { get; set; }
    }
}
