/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using OsEngine.Entity;
using OsEngine.Market.Servers.Entity;
using OsEngine.OsData.BinaryEntity;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace OsEngine.OsData.OrderFlow
{
    internal static class OrderFlowFileHash
    {
        public static string Calculate(string filePath)
        {
            using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(stream);
                return Convert.ToHexString(hash).ToLowerInvariant();
            }
        }
    }

    /// <summary>
    /// Opens a raw, GZip or Deflate QSH payload while retaining ownership of
    /// every underlying stream until the reader is disposed.
    /// </summary>
    internal sealed class OrderFlowQshInputStream : IDisposable
    {
        private static readonly byte[] _prefix = Encoding.UTF8.GetBytes("QScalp History Data");

        private FileStream _fileStream;
        private Stream _payloadStream;

        public DataBinaryReader Reader { get; private set; }

        /// <summary>
        /// Opens and hashes the stored bytes on the same read-only handle later used for replay.
        /// </summary>
        /// <remarks>
        /// Windows FileShare.Read excludes writers and replacement until disposal. The caller
        /// owns disposal after successful construction, including when OpenReader fails.
        /// The supplied metadata retains the size and completed hash on decoding failure.
        /// </remarks>
        /// <param name="filePath">Local QSH path.</param>
        /// <param name="header">Run-owned metadata populated before payload decoding.</param>
        /// <exception cref="IOException">The input cannot be opened or hashed.</exception>
        /// <exception cref="UnauthorizedAccessException">Read access is denied.</exception>
        public OrderFlowQshInputStream(string filePath, OrderFlowQshHeader header)
        {
            _fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                header.FileSize = _fileStream.Length;
                using (SHA256 sha256 = SHA256.Create())
                {
                    header.Sha256 = Convert.ToHexString(sha256.ComputeHash(_fileStream)).ToLowerInvariant();
                }

                _fileStream.Position = 0;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary>
        /// Opens the payload once on the retained handle and consumes its QSH signature.
        /// The owner must dispose this instance even when probing or decoding fails.
        /// </summary>
        /// <exception cref="InvalidOperationException">The payload reader is already open.</exception>
        /// <exception cref="InvalidDataException">No supported payload contains the QSH signature.</exception>
        /// <exception cref="IOException">The payload cannot be read.</exception>
        public void OpenReader()
        {
            if (Reader != null)
            {
                throw new InvalidOperationException("The QSH payload reader is already open.");
            }

            _payloadStream = OpenPayload(_fileStream);
            Reader = new DataBinaryReader(_payloadStream);
        }

        private static Stream OpenPayload(FileStream fileStream)
        {
            byte[] buffer = new byte[_prefix.Length];
            List<Exception> compressionErrors = new List<Exception>();

            fileStream.Position = 0;
            if (ReadPrefix(fileStream, buffer))
            {
                return fileStream;
            }

            fileStream.Position = 0;
            GZipStream gzipStream = new GZipStream(fileStream, CompressionMode.Decompress, true);
            try
            {
                if (ReadPrefix(gzipStream, buffer))
                {
                    return gzipStream;
                }
            }
            catch (InvalidDataException error)
            {
                compressionErrors.Add(error);
            }

            gzipStream.Dispose();
            fileStream.Position = 0;

            DeflateStream deflateStream = new DeflateStream(fileStream, CompressionMode.Decompress, true);
            try
            {
                if (ReadPrefix(deflateStream, buffer))
                {
                    return deflateStream;
                }
            }
            catch (InvalidDataException error)
            {
                compressionErrors.Add(error);
            }

            deflateStream.Dispose();
            Exception innerError = compressionErrors.Count == 0
                ? null
                : new AggregateException("QSH compression probes failed.", compressionErrors);
            throw new InvalidDataException("QSH signature or compression format is not supported.", innerError);
        }

        private static bool ReadPrefix(Stream stream, byte[] buffer)
        {
            int bytesRead = 0;

            while (bytesRead < buffer.Length)
            {
                int currentRead = stream.Read(buffer, bytesRead, buffer.Length - bytesRead);
                if (currentRead == 0)
                {
                    return false;
                }

                bytesRead += currentRead;
            }

            for (int i = 0; i < _prefix.Length; i++)
            {
                if (buffer[i] != _prefix[i])
                {
                    return false;
                }
            }

            return true;
        }

        public void Dispose()
        {
            if (Reader != null)
            {
                Reader.Dispose();
                Reader = null;
            }

            if (_payloadStream != null && ReferenceEquals(_payloadStream, _fileStream) == false)
            {
                _payloadStream.Dispose();
                _payloadStream = null;
            }

            if (_fileStream != null)
            {
                _fileStream.Dispose();
                _fileStream = null;
            }
        }
    }

    /// <summary>
    /// Shared QSH v4 header and frame reader for the paired research adapters.
    /// </summary>
    /// <remarks>
    /// The adapter preserves source timestamps without claiming exchange-time
    /// provenance. EOF before a new frame is normal; EOF inside a frame is an
    /// invalid-data failure. Contract: ORDER-FLOW-DATA-001.
    /// </remarks>
    internal abstract class OrderFlowQshReaderBase : IDisposable
    {
        private static readonly Regex _fileNameRegex = new Regex(
            "^(?<instrument>.+)\\.(?<date>[0-9]{4}-[0-9]{2}-[0-9]{2})\\.(?<type>Deals|Quotes)\\.qsh$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private OrderFlowQshInputStream _input;
        private long _lastFrameMilliseconds;

        protected DataBinaryReader Reader
        {
            get { return _input.Reader; }
        }

        public OrderFlowQshHeader Header { get; private set; }

        /// <summary>
        /// Opens one role-specific QSH v4 stream and validates its header.
        /// </summary>
        /// <param name="filePath">Path whose semantic file name identifies instrument, date and role.</param>
        /// <param name="expectedFileType">Expected <c>Deals</c> or <c>Quotes</c> file-name role.</param>
        /// <param name="expectedStreamType">Expected QSH stream byte.</param>
        /// <param name="priceStepOverride">Positive explicit step or zero to require the header value.</param>
        /// <param name="volumeStepOverride">Positive explicit step or zero to require the comment value.</param>
        /// <param name="header">Run-owned role metadata retained even when header decoding fails.</param>
        /// <exception cref="InvalidDataException">The signature, header, role or required step is invalid.</exception>
        /// <exception cref="IOException">The local input cannot be read.</exception>
        /// <remarks>Normal EOF is recognized only before a new frame; partial headers and frames fail closed.</remarks>
        protected OrderFlowQshReaderBase(string filePath, string expectedFileType, StreamType expectedStreamType,
            decimal priceStepOverride, decimal volumeStepOverride, OrderFlowQshHeader header)
        {
            Header = header;
            try
            {
                _input = new OrderFlowQshInputStream(filePath, header);
                ReadFileIdentity(header, expectedFileType);
                _input.OpenReader();
                ReadHeader(expectedStreamType, priceStepOverride, volumeStepOverride);
                header.HeaderComplete = true;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private void ReadHeader(StreamType expectedStreamType,
            decimal priceStepOverride, decimal volumeStepOverride)
        {
            OrderFlowQshHeader header = Header;
            int version = Reader.BaseStream.ReadByte();
            if (version != 4)
            {
                throw new InvalidDataException("Only QSH version 4 is supported by the research MVP.");
            }

            header.ApplicationName = Reader.ReadString();
            header.Comment = Reader.ReadString();
            long headerTicks = Reader.ReadInt64();
            try
            {
                header.HeaderTime = new DateTime(headerTicks, DateTimeKind.Utc);
            }
            catch (ArgumentOutOfRangeException error)
            {
                throw new InvalidDataException("QSH header timestamp is outside the DateTime range.", error);
            }
            _lastFrameMilliseconds = TimeManager.GetTimeStampMillisecondsFromStartTime(header.HeaderTime);

            int streamCount = Reader.ReadByte();
            if (streamCount != 1)
            {
                throw new InvalidDataException("QSH research input must contain exactly one stream.");
            }

            StreamType actualStreamType = (StreamType)Reader.ReadByte();
            if (actualStreamType != expectedStreamType)
            {
                throw new InvalidDataException("QSH stream type does not match its selected role.");
            }

            header.InstrumentHeader = Reader.ReadString();
            header.HeaderInstrument = ParseHeaderInstrument(header.InstrumentHeader);
            header.HeaderPriceStep = ParsePriceStep(header.InstrumentHeader);
            header.HeaderVolumeStep = ParseVolumeStep(header.Comment);
            header.EffectivePriceStep = priceStepOverride > 0 ? priceStepOverride : header.HeaderPriceStep;
            header.EffectiveVolumeStep = volumeStepOverride > 0 ? volumeStepOverride : header.HeaderVolumeStep;
            header.PriceStepOverridden = priceStepOverride > 0;
            header.VolumeStepOverridden = volumeStepOverride > 0;

            if (header.EffectivePriceStep <= 0)
            {
                throw new InvalidDataException("PriceStep is missing from the QSH header. Provide an explicit override.");
            }

            if (header.EffectiveVolumeStep <= 0)
            {
                throw new InvalidDataException("VolumeStep is missing from the QSH header. Provide an explicit override.");
            }
        }

        private static void ReadFileIdentity(OrderFlowQshHeader header, string expectedFileType)
        {
            Match match = _fileNameRegex.Match(header.FileName);
            if (match.Success == false)
            {
                throw new InvalidDataException("QSH file name must end with .yyyy-MM-dd.Deals.qsh or .yyyy-MM-dd.Quotes.qsh.");
            }

            string actualType = match.Groups["type"].Value;
            if (string.Equals(actualType, expectedFileType, StringComparison.OrdinalIgnoreCase) == false)
            {
                throw new InvalidDataException("QSH file name type does not match the selected input field.");
            }

            DateTime tradingDate;
            if (DateTime.TryParseExact(match.Groups["date"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out tradingDate) == false)
            {
                throw new InvalidDataException("QSH file trading date is invalid.");
            }

            header.FileInstrument = match.Groups["instrument"].Value;
            header.TradingDate = tradingDate;
        }

        private static decimal ParsePriceStep(string instrumentHeader)
        {
            if (string.IsNullOrWhiteSpace(instrumentHeader))
            {
                return 0;
            }

            string[] parts = instrumentHeader.Split(':');
            if (parts.Length != 5)
            {
                return 0;
            }

            decimal priceStep;
            if (decimal.TryParse(parts[4].Replace(',', '.'), NumberStyles.Float,
                CultureInfo.InvariantCulture, out priceStep) == false)
            {
                return 0;
            }

            return priceStep;
        }

        private static string ParseHeaderInstrument(string instrumentHeader)
        {
            if (string.IsNullOrWhiteSpace(instrumentHeader))
            {
                return string.Empty;
            }

            string[] parts = instrumentHeader.Split(':');
            return parts.Length == 5 ? parts[1] : string.Empty;
        }

        private static decimal ParseVolumeStep(string comment)
        {
            if (string.IsNullOrWhiteSpace(comment))
            {
                return 0;
            }

            string[] parts = comment.Split(':');
            if (parts.Length != 2 || string.Equals(parts[0], "VolumeStep", StringComparison.OrdinalIgnoreCase) == false)
            {
                return 0;
            }

            decimal volumeStep;
            if (decimal.TryParse(parts[1].Replace(',', '.'), NumberStyles.Float,
                CultureInfo.InvariantCulture, out volumeStep) == false)
            {
                return 0;
            }

            return volumeStep;
        }

        /// <summary>
        /// Reads the delta-encoded outer time of the next QSH frame.
        /// </summary>
        /// <param name="frameTime">Decoded source frame time when a frame is available.</param>
        /// <returns><c>false</c> only for clean EOF before the next frame; otherwise <c>true</c>.</returns>
        /// <exception cref="InvalidDataException">The timestamp is truncated, overlong or outside DateTime range.</exception>
        protected bool TryReadFrameTime(out DateTime frameTime)
        {
            int firstByte = Reader.BaseStream.ReadByte();
            if (firstByte < 0)
            {
                frameTime = DateTime.MinValue;
                return false;
            }

            uint offset = ReadUnsignedLeb128(firstByte, Reader.BaseStream);
            if (offset > ULeb128.Max4BValue)
            {
                throw new InvalidDataException("QSH frame timestamp offset exceeds the supported growing-value range.");
            }

            long delta = offset == ULeb128.Max4BValue ? Leb128.Read(Reader.BaseStream) : offset;

            try
            {
                checked
                {
                    _lastFrameMilliseconds += delta;
                }

                frameTime = TimeManager.GetDateTimeFromStartTimeMilliseconds(_lastFrameMilliseconds);
                return true;
            }
            catch (ArgumentOutOfRangeException error)
            {
                throw new InvalidDataException("QSH frame timestamp is outside the DateTime range.", error);
            }
            catch (OverflowException error)
            {
                throw new InvalidDataException("QSH frame timestamp overflowed.", error);
            }
        }

        private static uint ReadUnsignedLeb128(int firstByte, Stream stream)
        {
            uint value = (uint)(firstByte & 0x7f);
            int shift = 7;
            int currentByte = firstByte;

            while ((currentByte & 0x80) != 0)
            {
                if (shift >= 35)
                {
                    throw new InvalidDataException("QSH unsigned LEB128 value is too long.");
                }

                currentByte = stream.ReadByte();
                if (currentByte < 0)
                {
                    throw new EndOfStreamException("QSH frame timestamp is truncated.");
                }

                value |= (uint)(currentByte & 0x7f) << shift;
                shift += 7;
            }

            return value;
        }

        public void Dispose()
        {
            if (_input != null)
            {
                _input.Dispose();
                _input = null;
            }
        }
    }

    /// <summary>
    /// Sequential QSH Deals adapter preserving file order and source side.
    /// </summary>
    internal sealed class OrderFlowDealsQshReader : OrderFlowQshReaderBase
    {
        private readonly DealsStream _dealsStream = new DealsStream();
        private long _sourceSequence;

        /// <summary>
        /// Opens a Deals-role QSH v4 stream with the supplied effective-step policy.
        /// </summary>
        /// <param name="filePath">Semantic <c>*.Deals.qsh</c> path.</param>
        /// <param name="priceStepOverride">Positive override or zero.</param>
        /// <param name="volumeStepOverride">Positive override or zero.</param>
        /// <param name="header">Run-owned role metadata, including partial data on failure.</param>
        public OrderFlowDealsQshReader(string filePath, decimal priceStepOverride, decimal volumeStepOverride,
            OrderFlowQshHeader header)
            : base(filePath, "Deals", StreamType.Deals, priceStepOverride, volumeStepOverride, header)
        {
        }

        /// <summary>
        /// Decodes the next Deals frame while preserving source sequence, side,
        /// payload time and outer frame time.
        /// </summary>
        /// <param name="deal">Detached decoded deal, or <c>null</c> at clean EOF.</param>
        /// <returns><c>true</c> when one complete frame was decoded.</returns>
        /// <exception cref="InvalidDataException">The frame is truncated or numerically invalid.</exception>
        public bool TryRead(out OrderFlowDeal deal)
        {
            DateTime frameTime;
            if (TryReadFrameTime(out frameTime) == false)
            {
                deal = null;
                return false;
            }

            try
            {
                Trade trade = _dealsStream.Read(Reader, Header.EffectivePriceStep, Header.EffectiveVolumeStep);

                deal = new OrderFlowDeal();
                deal.SourceSequence = ++_sourceSequence;
                deal.FrameTime = frameTime;
                deal.Time = trade.Time == DateTime.MinValue ? frameTime : trade.Time;
                deal.Price = trade.Price;
                deal.Volume = trade.Volume;
                deal.PriceTicks = _dealsStream.lastPrice;
                deal.VolumeSteps = _dealsStream.lastVolume;
                deal.Side = trade.Side;
                deal.SourceId = trade.Id;
                return true;
            }
            catch (EndOfStreamException error)
            {
                throw new InvalidDataException("Deals QSH ends inside a frame.", error);
            }
            catch (OverflowException error)
            {
                throw new InvalidDataException("Deals QSH contains a price or volume outside the supported range.", error);
            }
        }
    }

    /// <summary>
    /// Sequential QSH Quotes adapter producing detached depth snapshot DTOs.
    /// </summary>
    internal sealed class OrderFlowQuotesQshReader : OrderFlowQshReaderBase
    {
        private const long MaximumChangesPerFrame = 100000;
        private const int MaximumActiveLevels = 100000;

        private readonly Dictionary<long, long> _levelVolumes = new Dictionary<long, long>();
        private long _sourceSequence;
        private long _lastPriceTicks;

        /// <summary>
        /// Opens a Quotes-role QSH v4 stream with private incremental book state.
        /// </summary>
        /// <param name="filePath">Semantic <c>*.Quotes.qsh</c> path.</param>
        /// <param name="priceStepOverride">Positive override or zero.</param>
        /// <param name="volumeStepOverride">Positive override or zero.</param>
        /// <param name="header">Run-owned role metadata, including partial data on failure.</param>
        public OrderFlowQuotesQshReader(string filePath, decimal priceStepOverride, decimal volumeStepOverride,
            OrderFlowQshHeader header)
            : base(filePath, "Quotes", StreamType.Quotes, priceStepOverride, volumeStepOverride, header)
        {
        }

        /// <summary>
        /// Applies one bounded Quotes change frame to private reader state and
        /// publishes a detached, sorted snapshot DTO.
        /// </summary>
        /// <param name="snapshot">Decoded book copy, or <c>null</c> at clean EOF.</param>
        /// <returns><c>true</c> when one complete frame was decoded.</returns>
        /// <exception cref="InvalidDataException">The frame is truncated, has an invalid change count or overflows.</exception>
        /// <remarks>Negative or excessive change counts fail before they can refresh the prior book timestamp.</remarks>
        public bool TryRead(out OrderFlowBookSnapshot snapshot)
        {
            DateTime frameTime;
            if (TryReadFrameTime(out frameTime) == false)
            {
                snapshot = null;
                return false;
            }

            try
            {
                snapshot = new OrderFlowBookSnapshot();
                snapshot.SourceSequence = ++_sourceSequence;
                snapshot.Time = frameTime;
                ReadChanges(snapshot);
                SetQuality(snapshot);
                return true;
            }
            catch (EndOfStreamException error)
            {
                throw new InvalidDataException("Quotes QSH ends inside a frame.", error);
            }
            catch (OverflowException error)
            {
                throw new InvalidDataException("Quotes QSH contains a price or volume outside the supported range.", error);
            }
        }

        private void ReadChanges(OrderFlowBookSnapshot snapshot)
        {
            long changeCount = Reader.ReadLeb128();
            if (changeCount < 0 || changeCount > MaximumChangesPerFrame)
            {
                throw new InvalidDataException("Quotes QSH frame contains an invalid change count.");
            }

            for (long i = 0; i < changeCount; i++)
            {
                checked
                {
                    _lastPriceTicks += Reader.ReadLeb128();
                }

                long signedVolumeSteps = Reader.ReadLeb128();
                if (signedVolumeSteps == 0)
                {
                    _levelVolumes.Remove(_lastPriceTicks);
                }
                else
                {
                    _levelVolumes[_lastPriceTicks] = signedVolumeSteps;
                }
            }

            if (_levelVolumes.Count > MaximumActiveLevels)
            {
                throw new InvalidDataException("Quotes QSH book exceeds the supported active-level limit.");
            }

            foreach (KeyValuePair<long, long> pair in _levelVolumes)
            {
                OrderFlowBookLevel level = new OrderFlowBookLevel();
                checked
                {
                    level.Price = pair.Key * Header.EffectivePriceStep;
                    level.Volume = pair.Value > 0
                        ? pair.Value * Header.EffectiveVolumeStep
                        : -pair.Value * Header.EffectiveVolumeStep;
                }

                if (pair.Value > 0)
                {
                    snapshot.Asks.Add(level);
                }
                else
                {
                    snapshot.Bids.Add(level);
                }
            }

            snapshot.Bids.Sort((first, second) => second.Price.CompareTo(first.Price));
            snapshot.Asks.Sort((first, second) => first.Price.CompareTo(second.Price));
        }

        private static void SetQuality(OrderFlowBookSnapshot snapshot)
        {
            if (snapshot.Bids.Count == 0 || snapshot.Asks.Count == 0)
            {
                snapshot.IsValid = false;
                snapshot.QualityCode = "BOOK_EMPTY";
                return;
            }

            if (snapshot.BestBid >= snapshot.BestAsk)
            {
                snapshot.IsValid = false;
                snapshot.QualityCode = "BOOK_CROSSED_OR_LOCKED";
                return;
            }

            for (int i = 0; i < snapshot.Bids.Count; i++)
            {
                if (snapshot.Bids[i].Price <= 0 || snapshot.Bids[i].Volume <= 0)
                {
                    snapshot.IsValid = false;
                    snapshot.QualityCode = "BOOK_INVALID_LEVEL";
                    return;
                }
            }

            for (int i = 0; i < snapshot.Asks.Count; i++)
            {
                if (snapshot.Asks[i].Price <= 0 || snapshot.Asks[i].Volume <= 0)
                {
                    snapshot.IsValid = false;
                    snapshot.QualityCode = "BOOK_INVALID_LEVEL";
                    return;
                }
            }

            snapshot.IsValid = true;
            snapshot.QualityCode = "OK";
        }
    }
}
