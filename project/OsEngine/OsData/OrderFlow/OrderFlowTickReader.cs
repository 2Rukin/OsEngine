/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using OsEngine.Entity;
using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace OsEngine.OsData.OrderFlow
{
    internal static class OrderFlowFileHash
    {
        public static string Calculate(string path)
        {
            using FileStream input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
        }
    }

    /// <summary>Streams one local UTF-8 tick file after hashing its stored bytes on the same protected handle.</summary>
    /// <remarks>
    /// One replay thread owns reading and disposal. FileShare.Read excludes writers/replacement on Windows.
    /// Date/Time is source clock time with unspecified timezone; MicroSeconds is required in 0..999999
    /// and is interpreted by this import contract as the fractional second. The technical Id field is discarded:
    /// neither repeated Ids nor identical rows are deduplicated or used to order trades.
    /// Contract: ORDER-FLOW-DATA-001. This adapter does not claim parity with every legacy Trade parser.
    /// </remarks>
    internal sealed class OrderFlowTickReader : IDisposable
    {
        private FileStream _input;
        private StreamReader _reader;
        private readonly char[] _line = new char[4096];
        private readonly CancellationToken _cancellation;
        private long _lineNumber;
        private long _sequence;
        private DateTime? _previousTime;
        private readonly OrderFlowTickInput _metadata;
        private const string Header = "Date,Time,Price,Volume,Side,MicroSeconds,Id";

        /// <summary>Opens and hashes the file. Successful construction transfers disposal ownership to the caller.</summary>
        /// <param name="path">Local text file, without any network access.</param>
        /// <param name="metadata">Run-owned provenance populated even if preparation fails.</param>
        /// <param name="cancellation">Observed between hash chunks and during reading.</param>
        public OrderFlowTickReader(string path, OrderFlowTickInput metadata, CancellationToken cancellation)
        {
            _metadata = metadata;
            _cancellation = cancellation;
            try
            {
                _input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
                metadata.FileSize = _input.Length;
                using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] buffer = new byte[65536];
                int count;
                while ((count = _input.Read(buffer, 0, buffer.Length)) != 0)
                {
                    cancellation.ThrowIfCancellationRequested();
                    hash.AppendData(buffer, 0, count);
                }
                metadata.Sha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                _input.Position = 0;
                _reader = new StreamReader(_input, new UTF8Encoding(false, true), false, 65536, true);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary>
        /// Reads the next valid tick in source order. An optional first-line header and blank lines are ignored.
        /// Invalid fields, a line over 4096 characters or regressive effective time reject the input.
        /// </summary>
        /// <param name="tick">Next source tick, or null at EOF.</param>
        /// <returns>True for a tick; false only after EOF.</returns>
        /// <exception cref="InvalidDataException">The line violates the tick format or time ordering.</exception>
        /// <exception cref="IOException">Reading fails.</exception>
        /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
        public bool TryRead(out OrderFlowDeal tick)
        {
            tick = null;
            string line;
            while ((line = ReadLine()) != null)
            {
                _lineNumber++;
                if (_lineNumber == 1) { line = line.TrimStart('\uFEFF'); }
                if (string.IsNullOrWhiteSpace(line)) { continue; }
                if (_lineNumber == 1 && string.Equals(line.Trim(), Header, StringComparison.OrdinalIgnoreCase)) { continue; }
                string[] fields = line.Split(',');
                if (fields.Length != 7) { throw Invalid("Expected seven comma-separated fields."); }
                if (fields[0].Length != 8 || fields[1].Length != 6 ||
                    !DateTime.TryParseExact(fields[0] + fields[1], "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateTime seconds))
                {
                    throw Invalid("Date/Time must be yyyyMMdd,HHmmss.");
                }
                if (!int.TryParse(fields[5], NumberStyles.None, CultureInfo.InvariantCulture, out int microseconds) ||
                    microseconds < 0 || microseconds > 999999)
                {
                    throw Invalid("MicroSeconds must be an integer from 0 to 999999.");
                }
                DateTime time = seconds.AddTicks(microseconds * 10L);
                if (_previousTime.HasValue && time < _previousTime.Value) { throw Invalid("Tick time moves backwards; file order is not repaired."); }
                const NumberStyles number = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
                if (!decimal.TryParse(fields[2], number, CultureInfo.InvariantCulture, out decimal price) || price <= 0 ||
                    !decimal.TryParse(fields[3], number, CultureInfo.InvariantCulture, out decimal volume) || volume <= 0)
                {
                    throw Invalid("Price and Volume must be positive decimal numbers with a dot separator.");
                }
                Side side;
                if (fields[4].Equals("Buy", StringComparison.OrdinalIgnoreCase)) { side = Side.Buy; }
                else if (fields[4].Equals("Sell", StringComparison.OrdinalIgnoreCase)) { side = Side.Sell; }
                else { throw Invalid("Side must be Buy or Sell; it is never inferred from price."); }
                tick = new OrderFlowDeal
                {
                    Time = time, SourceSequence = ++_sequence, Price = price, Volume = volume,
                    Side = side
                };
                _previousTime = time;
                _metadata.RecordCount = _sequence;
                _metadata.FirstTime ??= time;
                _metadata.LastTime = time;
                return true;
            }
            _metadata.ReadComplete = true;
            return false;
        }

        private string ReadLine()
        {
            _cancellation.ThrowIfCancellationRequested();
            int count = 0;
            int value;
            while ((value = _reader.Read()) >= 0)
            {
                if (value == '\n') { break; }
                if (value == '\r')
                {
                    if (_reader.Peek() == '\n') { _reader.Read(); }
                    break;
                }
                if (count == _line.Length) { throw Invalid("Line exceeds 4096 characters.", _lineNumber + 1); }
                _line[count++] = (char)value;
            }
            return value < 0 && count == 0 ? null : new string(_line, 0, count);
        }

        private InvalidDataException Invalid(string message, long? number = null)
        {
            return new InvalidDataException("Line " + (number ?? _lineNumber).ToString(CultureInfo.InvariantCulture) + ": " + message);
        }

        /// <summary>Releases the decoder and the pinned file handle; repeated disposal is harmless.</summary>
        public void Dispose()
        {
            _reader?.Dispose();
            _reader = null;
            _input?.Dispose();
            _input = null;
        }
    }
}
