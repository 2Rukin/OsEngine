/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using OsEngine.Entity;
using OsEngine.OsData.OrderFlow;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml.Linq;

namespace OsEngine.OrderFlowResearch.Tests
{
    internal static partial class Program
    {
        private static void TestTimeAxis(string root)
        {
            AssertEqual(115, OrderFlowChartNavigation.ZoomStart(100, 120, 60, 1000, 0.25), "Pointer time anchor");
            AssertEqual(100, OrderFlowChartNavigation.ZoomStart(100, 120, 60, 1000, 0), "Left anchor");
            AssertEqual(160, OrderFlowChartNavigation.ZoomStart(100, 120, 60, 1000, 1), "Right anchor");
            AssertEqual(0, OrderFlowChartNavigation.ZoomStart(0, 20, 100, 1000, 1), "Left history bound");
            AssertEqual(900, OrderFlowChartNavigation.ZoomStart(980, 20, 100, 1000, 0), "Right history bound");
            AssertEqual(3, OrderFlowChartNavigation.WheelCount(2, 100, -120), "Wheel must leave two-bar minimum");
            AssertEqual(3, OrderFlowChartNavigation.WheelCount(2, 100, -1), "Small wheel-out delta is not lost");
            AssertEqual(2, OrderFlowChartNavigation.WheelCount(3, 100, 1), "Small wheel-in delta is not lost");
            AssertEqual(100, OrderFlowChartNavigation.WheelCount(100, 100, -960), "Wheel cannot exceed history");
            List<OrderFlowDisplayBar> bars = new List<OrderFlowDisplayBar>();
            AssertEqual(0, OrderFlowChartNavigation.TimeTicks(bars, 1000).Count, "Empty time scale");
            DateTime time = new DateTime(2026, 9, 18, 23, 59, 45);
            bars.Add(new OrderFlowDisplayBar { TimeStart = time, TimeEnd = time.AddSeconds(15) });
            time = time.AddHours(10);
            bars.Add(new OrderFlowDisplayBar { TimeStart = time, TimeEnd = time.AddSeconds(15) });
            List<OrderFlowChartTimeTick> ticks = OrderFlowChartNavigation.TimeTicks(bars, 1000);
            AssertEqual("18.09 23:59:45", ticks[0].Text, "Source seconds and date");
            AssertEqual("19.09 09:59:45", ticks[1].Text, "Gap does not invent intermediate bars");
            AssertEqual(1, ticks[1].BarIndex, "Tick references actual displayed bar");
            List<OrderFlowChartTimeTick> narrow = OrderFlowChartNavigation.TimeTicks(bars, 96);
            AssertEqual(1, narrow.Count, "Narrow plot has no overlapping endpoint labels");
            FormattedText label = new FormattedText(narrow[0].Text, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, Brushes.White, 1);
            AssertTrue(label.Width <= 96, "Time label fits minimum supported plot width");
            OrderFlowResearchResult result = new OrderFlowResearchResult();
            result.Bars.Add(OrderFlowDisplayTimeFrame.Min1, Enumerable.Range(0, 1000).Select(index =>
                new OrderFlowDisplayBar { TimeStart = time.AddMinutes(index), TimeEnd = time.AddMinutes(index + 1) }).ToList());
            OrderFlowResearchChart chart = new OrderFlowResearchChart();
            chart.SetResult(result);
            chart.ScrollTo(100);
            chart.ZoomAt(60, 0.25);
            AssertEqual(115, chart.StartIndex, "Presenter uses pointer anchor");
            chart.ZoomAt(2, 0.25);
            chart.ZoomAt(OrderFlowChartNavigation.WheelCount(chart.VisibleCount, chart.TotalBars, -120), 0.25);
            AssertEqual(3, chart.VisibleCount, "Presenter can zoom back out from minimum");
        }

        private static void TestArchiveCatalog(string root)
        {
            DateTime day = new DateTime(2026, 9, 18);
            string index = "<A HREF='/2026-09-17/'>17</A><a href='/2026-09-18/'>18</a>" +
                "<A HREF='/2026-09-19/'>19</A><a href='https://evil.invalid/2026-09-18/'>bad</a>" +
                "<a href='/2026-09-18/?x=1'>bad</a>";
            string page = "<pre> 21 <a href='/2026-09-18/SiZ6.2026-09-18.Deals.qsh'>ignored title</a><br>" +
                " 25 <A HREF='/2026-09-18/SiZ6.2026-09-18.Quotes.qsh'>Q</A><br>" +
                " 10 <a href='/2026-09-18/EDU7.2026-09-18.Quotes.qsh'>Q</a><br>" +
                " 10 <a href='https://evil.invalid/2026-09-18/BAD.2026-09-18.Deals.qsh'>bad</a><br>" +
                " 10 <a href='/2026-09-18/%2e%2e/BAD.2026-09-18.Deals.qsh'>bad</a><br>" +
                " 10 <a href='/2026-09-17/BAD.2026-09-17.Deals.qsh'>bad</a><br>" +
                " 10 <a href='/2026-09-18/BAD.2026-09-18.Deals.qsh?x=1'>bad</a></pre>";
            List<string> requests = new List<string>();
            using OrderFlowArchiveClient client = new OrderFlowArchiveClient(new ArchiveHandler((request, cancellation) =>
            {
                requests.Add(request.RequestUri.AbsolutePath);
                return Response(Encoding.UTF8.GetBytes(request.RequestUri.AbsolutePath == "/" ? index : page));
            }));
            OrderFlowArchiveCatalog catalog = client.LoadCatalogAsync(day.AddHours(18), day.AddHours(23),
                null, CancellationToken.None).GetAwaiter().GetResult();
            AssertTrue(requests.SequenceEqual(new[] { "/", "/2026-09-18/" }), "Inclusive dates request only selected listed days");
            AssertEqual(1, catalog.Days.Count, "Selected date count");
            AssertEqual(3, catalog.Days[day].Count, "Reject external/encoded/wrong-day/query files");
            AssertTrue(catalog.Instruments.SequenceEqual(new[] { "EDU7", "SiZ6" }), "Actual instruments include incomplete pairs");
            AssertEqual(21L, catalog.Days[day][0].Length, "Declared file length from anchor prefix");
            ExpectArchiveFailure<InvalidDataException>(() => OrderFlowArchiveClient.ParseFiles(page + page, day));
            ExpectArchiveFailure<InvalidDataException>(() => OrderFlowArchiveClient.ParseDates("<html>service unavailable</html>"));
            ExpectArchiveFailure<ArgumentException>(() => client.LoadCatalogAsync(day, day.AddDays(-1), null,
                CancellationToken.None).GetAwaiter().GetResult());
        }

        private static void TestArchivePair(string root)
        {
            SyntheticPair raw = SyntheticQshFactory.Create(root, "raw", 101, 1000, 10, Side.Sell);
            SyntheticPair[] formats = { raw, SyntheticQshFactory.CompressGzip(root, raw), SyntheticQshFactory.CompressDeflate(root, raw) };
            for (int index = 0; index < formats.Length; index++)
            {
                SyntheticPair source = formats[index];
                List<OrderFlowArchiveFile> files = ArchiveFiles(source);
                int calls = 0;
                string folder = Path.Combine(root, "download-" + index);
                using OrderFlowArchiveClient client = new OrderFlowArchiveClient(new ArchiveHandler((request, cancellation) =>
                {
                    calls++;
                    AssertFalse(Directory.Exists(Path.Combine(folder, "TEST", "2026-09-18")), "Neither response may expose partial target");
                    return Response(File.ReadAllBytes(request.RequestUri.AbsolutePath.Contains(".Deals.", StringComparison.Ordinal)
                        ? source.DealsPath : source.QuotesPath));
                }));
                OrderFlowArchivePair pair = client.DownloadPairAsync(files, folder, null, CancellationToken.None).GetAwaiter().GetResult();
                AssertEqual(2, calls, "Exactly two file transfers");
                AssertFalse(pair.Reused, "New pair");
                AssertEqual(StoredHash(source.DealsPath), StoredHash(pair.DealsPath), "Stored Deals bytes unchanged");
                AssertEqual(StoredHash(source.QuotesPath), StoredHash(pair.QuotesPath), "Stored Quotes bytes unchanged");
                OrderFlowArchivePair reused = client.DownloadPairAsync(files, folder, null, CancellationToken.None).GetAwaiter().GetResult();
                AssertTrue(reused.Reused, "Receipt and hashes permit reuse");
                AssertEqual(2, calls, "Reuse makes no HTTP requests");
                AssertTrue(RunPair(new SyntheticPair { DealsPath = pair.DealsPath, QuotesPath = pair.QuotesPath },
                    Path.Combine(root, "research-" + index)).Quality.ResearchAccepted, "Downloaded pair opens in existing research");
                byte[] damaged = File.ReadAllBytes(pair.QuotesPath);
                damaged[damaged.Length - 1] ^= 1;
                File.WriteAllBytes(pair.QuotesPath, damaged);
                ExpectArchiveFailure<InvalidDataException>(() => client.DownloadPairAsync(files, folder, null,
                    CancellationToken.None).GetAwaiter().GetResult());
                AssertTrue(File.ReadAllBytes(pair.QuotesPath).SequenceEqual(damaged), "Corrupt existing data is not overwritten");
                AssertEqual(2, calls, "Failed reuse makes no HTTP requests");
            }
        }

        private static void TestArchiveFailures(string root)
        {
            SyntheticPair source = SyntheticQshFactory.Create(root, "source", 101, 1000, 10, Side.Sell);
            List<OrderFlowArchiveFile> files = ArchiveFiles(source);
            for (int scenario = 0; scenario < 6; scenario++)
            {
                string folder = Path.Combine(root, "failure-" + scenario);
                using OrderFlowArchiveClient client = new OrderFlowArchiveClient(new ArchiveHandler((request, cancellation) =>
                {
                    bool quotes = request.RequestUri.AbsolutePath.Contains(".Quotes.", StringComparison.Ordinal);
                    byte[] bytes = File.ReadAllBytes(quotes ? source.QuotesPath : source.DealsPath);
                    long declared = bytes.Length;
                    if (quotes)
                    {
                        if (scenario == 0) { return new HttpResponseMessage(HttpStatusCode.NotFound); }
                        if (scenario == 1) { bytes = bytes.Take(bytes.Length - 1).ToArray(); }
                        if (scenario == 2) { declared++; }
                        if (scenario == 3) { bytes = bytes.Concat(new byte[] { 42 }).ToArray(); }
                        if (scenario == 4) { bytes[1] = 0; }
                        if (scenario == 5) { bytes[19] = 3; }
                    }
                    HttpResponseMessage response = Response(bytes);
                    response.Content.Headers.ContentLength = declared;
                    return response;
                }));
                if (scenario == 0)
                {
                    ExpectArchiveFailure<HttpRequestException>(() => client.DownloadPairAsync(files, folder, null,
                        CancellationToken.None).GetAwaiter().GetResult());
                }
                else
                {
                    ExpectArchiveFailure<InvalidDataException>(() => client.DownloadPairAsync(files, folder, null,
                        CancellationToken.None).GetAwaiter().GetResult());
                }
                AssertFalse(Directory.Exists(Path.Combine(folder, "TEST", "2026-09-18")), "Failed second file never publishes pair");
                AssertEqual(0, Directory.GetDirectories(Path.Combine(folder, "TEST")).Length, "Owned partial directory cleaned");
            }
            string existing = Path.Combine(root, "existing", "TEST", "2026-09-18");
            Directory.CreateDirectory(existing);
            File.WriteAllText(Path.Combine(existing, "owner.txt"), "keep");
            int requests = 0;
            using OrderFlowArchiveClient blocked = new OrderFlowArchiveClient(new ArchiveHandler((request, cancellation) =>
            {
                requests++;
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }));
            ExpectArchiveFailure<IOException>(() => blocked.DownloadPairAsync(files, Path.Combine(root, "existing"),
                null, CancellationToken.None).GetAwaiter().GetResult());
            AssertEqual("keep", File.ReadAllText(Path.Combine(existing, "owner.txt")), "Unrecognized destination preserved");
            files[0].Url = "https://evil.invalid/" + files[0].Name;
            ExpectArchiveFailure<InvalidDataException>(() => blocked.DownloadPairAsync(files, root, null,
                CancellationToken.None).GetAwaiter().GetResult());
            AssertEqual(0, requests, "Invalid input and existing unrecognized directory cause no transfer");
        }

        private static void TestArchiveCancellation(string root)
        {
            SyntheticPair source = SyntheticQshFactory.Create(root, "source", 101, 1000, 10, Side.Sell);
            List<OrderFlowArchiveFile> files = ArchiveFiles(source);
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            string folder = Path.Combine(root, "download");
            using OrderFlowArchiveClient client = new OrderFlowArchiveClient(new ArchiveHandler((request, token) =>
            {
                if (request.RequestUri.AbsolutePath.Contains(".Deals.", StringComparison.Ordinal))
                {
                    return Response(File.ReadAllBytes(source.DealsPath));
                }
                AssertTrue(Directory.GetFiles(Path.Combine(folder, "TEST"), "*.Deals.qsh", SearchOption.AllDirectories).Length == 1,
                    "Deals completed before cancellation during Quotes read");
                HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK);
                response.Content = new StreamContent(new CancelReadStream(cancellation));
                response.Content.Headers.ContentLength = files[1].Length;
                return response;
            }));
            ExpectArchiveFailure<OperationCanceledException>(() => client.DownloadPairAsync(files, folder, null,
                cancellation.Token).GetAwaiter().GetResult());
            AssertEqual(0, Directory.GetDirectories(Path.Combine(folder, "TEST")).Length, "Cancellation removes only partial pair");
        }

        private static void TestArchiveHandoff(string root)
        {
            AssertFalse(OrderFlowResearchUi.HasSameDealsInstrument("SRZ6.X.2026-09-18.Deals.qsh", "SRZ6"),
                "Prefix collision cannot retain another instrument's steps");
            AssertFalse(OrderFlowResearchUi.HasSameDealsInstrument("SRZ6.2026-09-18.Deals.qsh", "SRZ6.X"),
                "Reverse prefix collision cannot retain steps");
            AssertTrue(OrderFlowResearchUi.HasSameDealsInstrument(@"C:\archive\srz6.2026-09-19.deals.QSH", "SRZ6"),
                "Exact instrument retains explicit steps across dates and case");
            AssertTrue(OrderFlowResearchUi.HasSameDealsInstrument("SRZ6.X.2026-09-18.Deals.qsh", "srz6.x"),
                "Dotted instrument identity is preserved exactly");
            foreach (string invalid in new[] { null, "", "SRZ6.invalid.Deals.qsh", "SRZ6.2026-02-30.Deals.qsh",
                "SRZ6.2026-09-18.Quotes.qsh", "SRZ6.2026-09-18.Deals.qsh.bak", "SRZ6X2026-09-18.Deals.qsh" })
            {
                AssertFalse(OrderFlowResearchUi.HasSameDealsInstrument(invalid, "SRZ6"), "Invalid identity cannot retain steps");
            }
        }

        private static void TestArchiveTextContrast(string root)
        {
            XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            XDocument ui;
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Research.DownloadUi.xaml"))
            {
                ui = XDocument.Load(stream);
            }
            foreach (string theme in new[] { "DarkOrange", "Midnight", "Tiffany", "Gray" })
            {
                ResourceDictionary resources = (ResourceDictionary)Application.LoadComponent(
                    new Uri("/OsEngine;component/Themes/Theme" + theme + ".xaml", UriKind.Relative));
                GradientBrush background = (GradientBrush)resources["WindowBackgroundGradientBrush"];
                foreach (XElement element in ui.Descendants(ns + "TextBlock"))
                {
                    TextBlock block = (TextBlock)XamlReader.Parse(element.ToString());
                    Grid host = new Grid { Resources = resources };
                    host.Children.Add(block);
                    block.Text = "Пара загружена. Проверьте шаг цены и объёма перед запуском исследования. " +
                        "Отменённая загрузка сохраняет готовые дни.";
                    host.Measure(new Size(280, 200));
                    host.Arrange(new Rect(0, 0, 280, 200));
                    host.UpdateLayout();
                    Color foreground = ((SolidColorBrush)block.Foreground).Color;
                    foreach (GradientStop stop in background.GradientStops)
                    {
                        double first = RelativeLuminance(foreground) + 0.05;
                        double second = RelativeLuminance(stop.Color) + 0.05;
                        AssertTrue(Math.Max(first, second) / Math.Min(first, second) >= 4.5,
                            theme + " " + block.Name + " text is readable against window background");
                    }
                    AssertTrue(block.ActualHeight > 20, "Wrapped status text is not constrained to one line");
                }
            }
            AssertTrue(Application.Current == null, "Contrast probe creates no Application or Window");
        }

        private static double RelativeLuminance(Color color)
        {
            double[] components = { color.R / 255.0, color.G / 255.0, color.B / 255.0 };
            for (int index = 0; index < components.Length; index++)
            {
                components[index] = components[index] <= 0.04045 ? components[index] / 12.92 :
                    Math.Pow((components[index] + 0.055) / 1.055, 2.4);
            }
            return components[0] * 0.2126 + components[1] * 0.7152 + components[2] * 0.0722;
        }

        private static List<OrderFlowArchiveFile> ArchiveFiles(SyntheticPair pair)
        {
            List<OrderFlowArchiveFile> files = new List<OrderFlowArchiveFile>();
            foreach (string path in new[] { pair.DealsPath, pair.QuotesPath })
            {
                string name = Path.GetFileName(path);
                files.Add(new OrderFlowArchiveFile { Instrument = "TEST", Date = new DateTime(2026, 9, 18),
                    Role = name.Contains(".Deals.", StringComparison.Ordinal) ? "Deals" : "Quotes", Name = name,
                    Length = new FileInfo(path).Length, Url = "https://erinrv.qscalp.ru/2026-09-18/" + name });
            }
            return files;
        }

        private static HttpResponseMessage Response(byte[] bytes)
        {
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        }

        private static void ExpectArchiveFailure<T>(Action action) where T : Exception
        {
            bool failed = false;
            try { action(); }
            catch (T) { failed = true; }
            AssertTrue(failed, "Expected " + typeof(T).Name);
        }
    }

    internal sealed class ArchiveHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _respond;
        public ArchiveHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond) { _respond = respond; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_respond(request, cancellationToken));
        }
    }

    internal sealed class CancelReadStream : MemoryStream
    {
        private readonly CancellationTokenSource _cancellation;
        public CancelReadStream(CancellationTokenSource cancellation) { _cancellation = cancellation; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _cancellation.Cancel();
            return ValueTask.FromCanceled<int>(cancellationToken);
        }
    }
}
