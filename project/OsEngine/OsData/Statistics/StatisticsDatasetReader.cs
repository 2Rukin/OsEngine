/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;

namespace OsEngine.OsData.Statistics
{
    /// <summary>Read-only adapter. Never constructs OsData loaders or writes settings.</summary>
    public class StatisticsDatasetReader
    {
        public const int MaximumCandles = 2000000;

        #region Public methods

        public List<StatisticsInstrument> ReadCatalog(string folder, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string root = Path.GetFullPath(folder);
            RejectLinks(root);
            string settings = Path.Combine(root, "Settings.txt");
            RejectLinks(settings);
            List<StatisticsInstrument> result = new List<StatisticsInstrument>();
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (FileStream stream = new FileStream(settings, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (StreamReader reader = new StreamReader(stream))
            {
                if (string.IsNullOrWhiteSpace(reader.ReadLine()))
                {
                    throw InvalidLine(settings, 1, "Missing dataset settings header.");
                }
                string line;
                int lineNumber = 1;
                while ((line = reader.ReadLine()) != null)
                {
                    token.ThrowIfCancellationRequested();
                    lineNumber++;
                    string[] fields = line.Split('~');
                    if (fields.Length < 7 || string.IsNullOrWhiteSpace(fields[0]))
                    {
                        throw InvalidLine(settings, lineNumber, "Invalid instrument settings.");
                    }
                    // Older rows omit price/volume steps; loader settings are %-separated.
                    bool legacy = fields[6].Contains("%");
                    int settingsIndex = legacy ? 6 : 8;
                    if (fields.Length <= settingsIndex || !fields[settingsIndex].Contains("%"))
                    {
                        throw InvalidLine(settings, lineNumber, "Unknown instrument settings format.");
                    }
                    string safeName = GetSafeName(fields[0], settings, lineNumber);
                    if (!names.Add(safeName))
                    {
                        throw InvalidLine(settings, lineNumber, "Ambiguous instrument directory: " + safeName);
                    }
                    string fullName = fields.Length > settingsIndex + 1 ? fields[settingsIndex + 1] : "";
                    StatisticsInstrument instrument = new StatisticsInstrument
                    {
                        Name = fields[0],
                        FullName = fullName,
                        DisplayName = string.IsNullOrWhiteSpace(fullName) || fullName == fields[0]
                            ? fields[0] : fields[0] + " — " + fullName
                    };
                    string securityFolder = Path.Combine(root, safeName);
                    if (!Directory.Exists(securityFolder))
                    {
                        continue;
                    }
                    RejectLinks(securityFolder);
                    foreach (string tfFolder in Directory.EnumerateDirectories(securityFolder))
                    {
                        token.ThrowIfCancellationRequested();
                        string timeFrame = Path.GetFileName(tfFolder);
                        if (!IsCandleTimeFrame(timeFrame))
                        {
                            continue;
                        }
                        RejectLinks(tfFolder);
                        string path = Path.Combine(tfFolder, safeName + ".txt");
                        if (File.Exists(path))
                        {
                            RejectLinks(path);
                            if (instrument.CandleFiles.ContainsKey(timeFrame))
                            {
                                throw InvalidLine(settings, lineNumber, "Ambiguous timeframe directory: " + tfFolder);
                            }
                            instrument.CandleFiles.Add(timeFrame, path);
                        }
                    }
                    if (instrument.CandleFiles.Count > 0)
                    {
                        result.Add(instrument);
                    }
                }
            }
            token.ThrowIfCancellationRequested();
            return result;
        }

        public List<StatisticsCandle> ReadCandles(string path, DateTime? from, DateTime? to, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ValidateDates(from, to);
            path = Path.GetFullPath(path);
            RejectLinks(path);
            List<StatisticsCandle> result = new List<StatisticsCandle>();
            DateTime? previous = null;
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (StreamReader reader = new StreamReader(stream))
            {
                string line;
                int lineNumber = 0;
                while ((line = reader.ReadLine()) != null)
                {
                    token.ThrowIfCancellationRequested();
                    lineNumber++;
                    StatisticsCandle candle = ParseCandle(line, path, lineNumber);
                    if (previous.HasValue && candle.Time <= previous.Value)
                    {
                        throw InvalidLine(path, lineNumber, "Timestamps must be strictly increasing; duplicate or unordered candle.");
                    }
                    previous = candle.Time;
                    if (!InRange(candle.Time, from, to))
                    {
                        continue;
                    }
                    if (result.Count == MaximumCandles)
                    {
                        throw InvalidLine(path, lineNumber, "The selected range exceeds 2,000,000 candles per leg. Narrow the date range.");
                    }
                    result.Add(candle);
                }
            }
            token.ThrowIfCancellationRequested();
            return result;
        }

        #endregion

        #region Validation and parsing

        internal static void ValidateDates(DateTime? from, DateTime? to)
        {
            if (from.HasValue && to.HasValue && from.Value.Date > to.Value.Date)
            {
                throw new ArgumentException("The start date must not follow the end date.");
            }
        }

        internal static bool InRange(DateTime time, DateTime? from, DateTime? to)
        {
            return (!from.HasValue || time.Date >= from.Value.Date)
                && (!to.HasValue || time.Date <= to.Value.Date);
        }

        internal static bool IsValidCandle(StatisticsCandle candle)
        {
            return candle != null && candle.Volume >= 0 && candle.Low <= candle.High
                && candle.Open >= candle.Low && candle.Open <= candle.High
                && candle.Close >= candle.Low && candle.Close <= candle.High;
        }

        private static StatisticsCandle ParseCandle(string line, string path, int lineNumber)
        {
            string[] fields = line.Split(',');
            if (fields.Length != 7 && fields.Length != 8)
            {
                throw InvalidLine(path, lineNumber, "Expected DATE,TIME,OPEN,HIGH,LOW,CLOSE,VOLUME[,OPEN INTEREST].");
            }
            DateTime time;
            if (fields[0].Length != 8 || fields[1].Length != 6
                || !DateTime.TryParseExact(fields[0] + fields[1], "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out time))
            {
                throw InvalidLine(path, lineNumber, "Invalid date/time; expected yyyyMMdd,HHmmss.");
            }
            StatisticsCandle candle = new StatisticsCandle
            {
                Time = time,
                Open = ParseNumber(fields[2], path, lineNumber),
                High = ParseNumber(fields[3], path, lineNumber),
                Low = ParseNumber(fields[4], path, lineNumber),
                Close = ParseNumber(fields[5], path, lineNumber),
                Volume = ParseNumber(fields[6], path, lineNumber)
            };
            if (!IsValidCandle(candle))
            {
                throw InvalidLine(path, lineNumber, "Negative volume or inconsistent OHLC.");
            }
            if (fields.Length == 8 && ParseNumber(fields[7], path, lineNumber) < 0)
            {
                throw InvalidLine(path, lineNumber, "Open interest must be nonnegative.");
            }
            return candle;
        }

        private static decimal ParseNumber(string value, string path, int lineNumber)
        {
            decimal result;
            if (!decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out result))
            {
                throw InvalidLine(path, lineNumber, "Invalid invariant decimal: " + value);
            }
            return result;
        }

        private static InvalidDataException InvalidLine(string path, int lineNumber, string message)
        {
            return new InvalidDataException(path + ", line " + lineNumber.ToString(CultureInfo.InvariantCulture) + ": " + message);
        }

        private static string GetSafeName(string name, string path, int lineNumber)
        {
            if (name.StartsWith("/", StringComparison.Ordinal) || name.StartsWith("\\", StringComparison.Ordinal)
                || (name.Length >= 2 && char.IsLetter(name[0]) && name[1] == ':'))
            {
                throw InvalidLine(path, lineNumber, "Absolute paths are not allowed in instrument names.");
            }
            string[] segments = name.Replace('\\', '/').Split('/');
            foreach (string segment in segments)
            {
                if (segment == "." || segment == "..")
                {
                    throw InvalidLine(path, lineNumber, "Dot path segments are not allowed in instrument names.");
                }
            }
            // Keep exactly the substitutions used by Entity.Extensions.RemoveExcessFromSecurityName.
            string safe = name.Replace("/", "").Replace("\\", "").Replace("*", "").Replace(":", "")
                .Replace("@", "").Replace(";", "").Replace("\"", "");
            if (string.IsNullOrWhiteSpace(safe) || safe.EndsWith(".", StringComparison.Ordinal)
                || safe.EndsWith(" ", StringComparison.Ordinal) || safe.IndexOfAny(new[] { '<', '>', '?', '|' }) >= 0)
            {
                throw InvalidLine(path, lineNumber, "Unsafe instrument directory name.");
            }
            foreach (char character in safe)
            {
                if (char.IsControl(character))
                {
                    throw InvalidLine(path, lineNumber, "Control character in instrument name.");
                }
            }
            string baseName = safe.Split('.')[0].ToUpperInvariant();
            if (baseName == "CON" || baseName == "PRN" || baseName == "AUX" || baseName == "NUL"
                || (baseName.Length == 4 && (baseName.StartsWith("COM", StringComparison.Ordinal)
                    || baseName.StartsWith("LPT", StringComparison.Ordinal)) && baseName[3] >= '1' && baseName[3] <= '9'))
            {
                throw InvalidLine(path, lineNumber, "Reserved device name is not an instrument directory.");
            }
            return safe;
        }

        private static void RejectLinks(string path)
        {
            string current = Path.GetFullPath(path);
            while (!string.IsNullOrEmpty(current))
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException("Symbolic links and reparse points are not supported: " + current);
                }
                current = Path.GetDirectoryName(current);
            }
        }

        private static bool IsCandleTimeFrame(string value)
        {
            switch (value)
            {
                case "Sec1": case "Sec2": case "Sec5": case "Sec10": case "Sec15": case "Sec20": case "Sec30":
                case "Min1": case "Min2": case "Min3": case "Min5": case "Min10": case "Min15": case "Min20": case "Min30": case "Min45":
                case "Hour1": case "Hour2": case "Hour4": case "Day":
                    return true;
                default:
                    return false;
            }
        }

        #endregion
    }
}
