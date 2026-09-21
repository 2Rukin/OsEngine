/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace OsEngine.OsData.OrderFlow
{
    /// <summary>
    /// Sequential, unauthenticated reader of the public QScalp archive. It owns its
    /// HTTP client and publishes a daily Deals/Quotes directory only after both transfers pass.
    /// </summary>
    /// <remarks>
    /// No connector or application settings are used. Cancellation discards only this call's
    /// staging directory. Existing destinations are never overwritten; reuse requires the
    /// versioned receipt, current catalog lengths and matching local SHA-256 hashes.
    /// The bounded signature/version probe is not a full QSH replay validation.
    /// </remarks>
    internal sealed class OrderFlowArchiveClient : IDisposable
    {
        internal static readonly Uri Origin = new Uri("https://erinrv.qscalp.ru/");
        private const string ReceiptName = "download.json";
        private const string ReceiptVersion = "qscalp-download-1";
        private readonly HttpClient _http;
        private static readonly Regex _links = new Regex(
            "<a\\s+[^>]*href\\s*=\\s*[\"'](?<href>[^\"']+)[\"'][^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        private static readonly Regex _fileName = new Regex(
            @"^(?<instrument>[A-Za-z0-9][A-Za-z0-9_.-]{0,99})\.(?<date>\d{4}-\d{2}-\d{2})\.(?<role>Deals|Quotes)\.qsh$",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        #region Catalog

        /// <summary>Creates an owned HTTP client; an injected handler supports offline tests.</summary>
        public OrderFlowArchiveClient(HttpMessageHandler handler = null)
        {
            _http = new HttpClient(handler ?? new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false
            });
            _http.Timeout = Timeout.InfiniteTimeSpan;
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("OsEngine-OrderFlowArchive/1.0");
        }

        /// <summary>
        /// Loads the root and each listed day in an inclusive calendar-date range.
        /// A failed page aborts the catalog instead of presenting partial availability as complete.
        /// </summary>
        public async Task<OrderFlowArchiveCatalog> LoadCatalogAsync(DateTime from, DateTime to,
            IProgress<string> progress, CancellationToken cancellation)
        {
            from = from.Date;
            to = to.Date;
            if (from > to || (to - from).TotalDays > 5000)
            {
                throw new ArgumentException("Select an ordered period of at most 5001 calendar days.");
            }

            OrderFlowArchiveCatalog catalog = new OrderFlowArchiveCatalog { From = from, To = to };
            string root = await GetPageAsync(Origin, cancellation).ConfigureAwait(false);
            foreach (DateTime date in ParseDates(root).Where(date => date >= from && date <= to))
            {
                cancellation.ThrowIfCancellationRequested();
                progress?.Report(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                await Task.Delay(100, cancellation).ConfigureAwait(false);
                Uri uri = new Uri(Origin, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "/");
                catalog.Days.Add(date, ParseFiles(await GetPageAsync(uri, cancellation).ConfigureAwait(false), date));
            }
            return catalog;
        }

        internal static List<DateTime> ParseDates(string html)
        {
            SortedSet<DateTime> dates = new SortedSet<DateTime>();
            foreach (Match match in _links.Matches(html))
            {
                if (TryArchiveUri(match.Groups["href"].Value, out Uri uri) &&
                    DateTime.TryParseExact(uri.AbsolutePath, "/yyyy-MM-dd/", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out DateTime date))
                {
                    dates.Add(date);
                }
            }
            if (dates.Count == 0)
            {
                throw new InvalidDataException("The archive index contains no dated directories.");
            }
            return dates.ToList();
        }

        internal static List<OrderFlowArchiveFile> ParseFiles(string html, DateTime date)
        {
            List<OrderFlowArchiveFile> files = new List<OrderFlowArchiveFile>();
            string directory = "/" + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "/";
            foreach (Match link in _links.Matches(html))
            {
                if (!TryArchiveUri(link.Groups["href"].Value, out Uri uri) ||
                    !uri.AbsolutePath.StartsWith(directory, StringComparison.Ordinal))
                {
                    continue;
                }
                string name = uri.AbsolutePath.Substring(directory.Length);
                Match match = _fileName.Match(name);
                if (!match.Success || match.Groups["date"].Value != date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                {
                    continue;
                }
                string preceding = html.Substring(Math.Max(0, link.Index - 40), Math.Min(40, link.Index));
                Match size = Regex.Match(preceding, @"(?<bytes>\d+)\s*$", RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(1));
                if (!long.TryParse(size.Groups["bytes"].Value, NumberStyles.None, CultureInfo.InvariantCulture,
                    out long bytes) || bytes <= 0)
                {
                    throw new InvalidDataException("Missing archive length: " + name);
                }
                if (files.Any(file => file.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidDataException("Duplicate archive file: " + name);
                }
                files.Add(new OrderFlowArchiveFile
                {
                    Instrument = match.Groups["instrument"].Value, Date = date.Date,
                    Role = match.Groups["role"].Value, Name = name, Url = uri.AbsoluteUri, Length = bytes
                });
            }
            return files;
        }

        private static bool TryArchiveUri(string href, out Uri uri)
        {
            href = WebUtility.HtmlDecode(href);
            uri = null;
            // The observed archive uses literal ASCII paths. Reject encoded separators and
            // traversal before Uri normalizes them, and never follow external links.
            if (href.Contains('%') || href.Contains('\\') || href.Contains("..", StringComparison.Ordinal) ||
                !Uri.TryCreate(Origin, href, out Uri candidate) || candidate.Scheme != Origin.Scheme ||
                candidate.Host != Origin.Host || candidate.Port != Origin.Port ||
                candidate.UserInfo.Length != 0 || candidate.Query.Length != 0 || candidate.Fragment.Length != 0)
            {
                return false;
            }
            uri = candidate;
            return true;
        }

        private async Task<string> GetPageAsync(Uri uri, CancellationToken cancellation)
        {
            using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(TimeSpan.FromSeconds(40));
            using HttpResponseMessage response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead,
                deadline.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using Stream source = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using MemoryStream buffer = new MemoryStream();
            byte[] chunk = new byte[16384];
            int read;
            while ((read = await source.ReadAsync(chunk, deadline.Token).ConfigureAwait(false)) != 0)
            {
                if (buffer.Length + read > 4 * 1024 * 1024)
                {
                    throw new InvalidDataException("Archive listing exceeds 4 MiB.");
                }
                buffer.Write(chunk, 0, read);
            }
            return Encoding.UTF8.GetString(buffer.ToArray());
        }

        #endregion

        #region Download and local integrity

        /// <summary>
        /// Downloads or verifies one complete daily pair. Publication is an atomic directory
        /// move on the destination volume; cancellation or failure cannot publish a partial pair.
        /// </summary>
        /// <exception cref="HttpRequestException">An HTTP request fails or returns an unsuccessful status.</exception>
        /// <exception cref="IOException">Local reading, writing, cleanup or publication fails.</exception>
        /// <exception cref="InvalidDataException">Pair identity, size, signature or local integrity checks fail.</exception>
        /// <exception cref="OperationCanceledException">Cancellation or a request deadline expires.</exception>
        public async Task<OrderFlowArchivePair> DownloadPairAsync(IReadOnlyList<OrderFlowArchiveFile> pair,
            string root, IProgress<string> progress, CancellationToken cancellation)
        {
            ValidatePair(pair);
            cancellation.ThrowIfCancellationRequested();
            string parent = Path.Combine(Path.GetFullPath(root), pair[0].Instrument);
            string target = Path.Combine(parent, pair[0].Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            if (Directory.Exists(target))
            {
                await VerifyExistingAsync(target, pair, cancellation).ConfigureAwait(false);
                return MakePair(target, pair, true);
            }
            Directory.CreateDirectory(parent);
            string staging = Path.Combine(parent, ".partial-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try
            {
                OrderFlowArchiveReceipt receipt = new OrderFlowArchiveReceipt { Version = ReceiptVersion };
                foreach (OrderFlowArchiveFile file in pair)
                {
                    await Task.Delay(100, cancellation).ConfigureAwait(false);
                    receipt.Files.Add(await DownloadFileAsync(file, staging, progress, cancellation).ConfigureAwait(false));
                }
                await File.WriteAllTextAsync(Path.Combine(staging, ReceiptName), JsonSerializer.Serialize(receipt),
                    cancellation).ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested();
                Directory.Move(staging, target);
                return MakePair(target, pair, false);
            }
            finally
            {
                // This exact unique staging directory was created above, never a caller's existing directory.
                if (Directory.Exists(staging))
                {
                    Directory.Delete(staging, true);
                }
            }
        }

        private static void ValidatePair(IReadOnlyList<OrderFlowArchiveFile> pair)
        {
            if (pair == null || pair.Count != 2 || pair.Count(file => file.Role == "Deals") != 1 ||
                pair.Count(file => file.Role == "Quotes") != 1 || pair[0].Instrument != pair[1].Instrument ||
                pair[0].Date.Date != pair[1].Date.Date)
            {
                throw new InvalidDataException("A matching Deals + Quotes pair is required.");
            }
            foreach (OrderFlowArchiveFile file in pair)
            {
                Match match = _fileName.Match(file.Name);
                string expected = file.Instrument + "." + file.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) +
                    "." + file.Role + ".qsh";
                if (!match.Success || file.Name != expected || file.Length <= 0 ||
                    !TryArchiveUri(file.Url, out Uri uri) || uri.AbsolutePath != "/" +
                    file.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "/" + expected)
                {
                    throw new InvalidDataException("Invalid archive file identity.");
                }
            }
        }

        private async Task<OrderFlowArchiveStoredFile> DownloadFileAsync(OrderFlowArchiveFile file, string staging,
            IProgress<string> progress, CancellationToken cancellation)
        {
            using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(TimeSpan.FromMinutes(5));
            using HttpResponseMessage response = await _http.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead,
                deadline.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength != file.Length)
            {
                throw new InvalidDataException("Archive size changed; refresh the catalog: " + file.Name);
            }
            string path = Path.Combine(staging, file.Name);
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using (Stream source = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false))
            using (FileStream output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                65536, FileOptions.Asynchronous))
            {
                byte[] chunk = new byte[65536];
                long total = 0;
                long lastProgress = -1;
                int read;
                while ((read = await source.ReadAsync(chunk, deadline.Token).ConfigureAwait(false)) != 0)
                {
                    total += read;
                    if (total > file.Length)
                    {
                        throw new InvalidDataException("Archive response exceeds listed size: " + file.Name);
                    }
                    await output.WriteAsync(chunk.AsMemory(0, read), deadline.Token).ConfigureAwait(false);
                    hash.AppendData(chunk, 0, read);
                    if (Environment.TickCount64 - lastProgress >= 150 || total == file.Length)
                    {
                        progress?.Report(file.Name + "  " + total.ToString("N0", CultureInfo.InvariantCulture) +
                            " / " + file.Length.ToString("N0", CultureInfo.InvariantCulture) + " B");
                        lastProgress = Environment.TickCount64;
                    }
                }
                if (total != file.Length)
                {
                    throw new InvalidDataException("Incomplete archive response: " + file.Name);
                }
            }
            ValidateSignature(path);
            return new OrderFlowArchiveStoredFile
            {
                Name = file.Name, Url = file.Url, Length = file.Length,
                Sha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()
            };
        }

        private static async Task VerifyExistingAsync(string target, IReadOnlyList<OrderFlowArchiveFile> pair,
            CancellationToken cancellation)
        {
            string receiptPath = Path.Combine(target, ReceiptName);
            using FileStream receiptStream = new FileStream(receiptPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (receiptStream.Length > 65536)
            {
                throw new InvalidDataException("Invalid download receipt: " + target);
            }
            OrderFlowArchiveReceipt receipt = await JsonSerializer.DeserializeAsync<OrderFlowArchiveReceipt>(
                receiptStream, cancellationToken: cancellation).ConfigureAwait(false);
            if (receipt?.Version != ReceiptVersion || receipt.Files == null || receipt.Files.Count != 2)
            {
                throw new InvalidDataException("Invalid download receipt: " + target);
            }
            foreach (OrderFlowArchiveFile file in pair)
            {
                OrderFlowArchiveStoredFile stored = receipt.Files.SingleOrDefault(entry => entry?.Name == file.Name);
                if (stored == null || stored.Url != file.Url || stored.Length != file.Length)
                {
                    throw new InvalidDataException("Existing pair differs from the catalog; choose another folder: " + target);
                }
                using FileStream input = new FileStream(Path.Combine(target, file.Name), FileMode.Open,
                    FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
                string sha = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellation).ConfigureAwait(false)).ToLowerInvariant();
                if (input.Length != stored.Length || sha != stored.Sha256)
                {
                    throw new InvalidDataException("Existing pair failed integrity verification: " + file.Name);
                }
            }
        }

        private static void ValidateSignature(string path)
        {
            using FileStream input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            int first = input.ReadByte();
            int second = input.ReadByte();
            input.Position = 0;
            using Stream payload = first == 'Q' ? input : first == 0x1f && second == 0x8b
                ? new GZipStream(input, CompressionMode.Decompress, true)
                : new DeflateStream(input, CompressionMode.Decompress, true);
            byte[] prefix = new byte[20];
            payload.ReadExactly(prefix);
            if (Encoding.ASCII.GetString(prefix, 0, 19) != "QScalp History Data" || prefix[19] != 4)
            {
                throw new InvalidDataException("The archive response is not a supported QSH v4 payload.");
            }
        }

        private static OrderFlowArchivePair MakePair(string target, IReadOnlyList<OrderFlowArchiveFile> files, bool reused)
        {
            return new OrderFlowArchivePair
            {
                Instrument = files[0].Instrument, Date = files[0].Date, Reused = reused,
                DealsPath = Path.Combine(target, files.Single(file => file.Role == "Deals").Name),
                QuotesPath = Path.Combine(target, files.Single(file => file.Role == "Quotes").Name)
            };
        }

        /// <summary>Disposes the owned HTTP client after the caller has awaited its active operation.</summary>
        public void Dispose()
        {
            _http.Dispose();
        }

        #endregion
    }

    internal sealed class OrderFlowArchiveCatalog
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public SortedDictionary<DateTime, List<OrderFlowArchiveFile>> Days { get; } = new SortedDictionary<DateTime, List<OrderFlowArchiveFile>>();
        public List<string> Instruments => Days.Values.SelectMany(files => files).Select(file => file.Instrument)
            .Distinct(StringComparer.Ordinal).OrderBy(instrument => instrument, StringComparer.Ordinal).ToList();
    }

    internal sealed class OrderFlowArchiveFile
    {
        public string Instrument { get; set; }
        public DateTime Date { get; set; }
        public string Role { get; set; }
        public string Name { get; set; }
        public string Url { get; set; }
        public long Length { get; set; }
    }

    internal sealed class OrderFlowArchivePair
    {
        public string Instrument { get; set; }
        public DateTime Date { get; set; }
        public string DealsPath { get; set; }
        public string QuotesPath { get; set; }
        public bool Reused { get; set; }
    }

    /// <summary>Local integrity receipt; SHA-256 records received bytes, not archive authenticity or QSH semantics.</summary>
    internal sealed class OrderFlowArchiveReceipt
    {
        public string Version { get; set; }
        public List<OrderFlowArchiveStoredFile> Files { get; set; } = new List<OrderFlowArchiveStoredFile>();
    }

    internal sealed class OrderFlowArchiveStoredFile
    {
        public string Name { get; set; }
        public string Url { get; set; }
        public long Length { get; set; }
        public string Sha256 { get; set; }
    }
}
