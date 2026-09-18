/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace OsEngine.OsData.Statistics
{
    /// <summary>Matches literal contract suffixes, without guessing short-year decades.</summary>
    public class StatisticsPairCatalog
    {
        public List<StatisticsPair> BuildPairs(IReadOnlyList<StatisticsInstrument> instruments,
            string prefixA, string prefixB, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (instruments == null) throw new ArgumentNullException(nameof(instruments));
            ValidatePrefix(prefixA);
            ValidatePrefix(prefixB);
            if (string.Equals(prefixA, prefixB, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("The two contract prefixes must differ.");
            }
            Dictionary<string, StatisticsInstrument> first = new Dictionary<string, StatisticsInstrument>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, StatisticsInstrument> second = new Dictionary<string, StatisticsInstrument>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < instruments.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                StatisticsInstrument instrument = instruments[i];
                if (instrument == null || string.IsNullOrWhiteSpace(instrument.Name) || instrument.CandleFiles == null)
                {
                    throw new ArgumentException("Invalid instrument in the catalog.", nameof(instruments));
                }
                Dictionary<string, StatisticsInstrument> leg = instrument.Name.StartsWith(prefixA, StringComparison.OrdinalIgnoreCase)
                    ? first : instrument.Name.StartsWith(prefixB, StringComparison.OrdinalIgnoreCase) ? second : null;
                if (leg == null || instrument.Name.Length == 2) continue;
                string suffix = instrument.Name.Substring(2);
                if (!IsContractSuffix(suffix)) continue;
                if (leg.ContainsKey(suffix))
                {
                    throw new InvalidDataException("Ambiguous contract suffix " + suffix + " for prefix " + instrument.Name.Substring(0, 2) + ".");
                }
                leg.Add(suffix, instrument);
            }
            List<string> expiries = new List<string>(first.Keys);
            expiries.Sort(StringComparer.OrdinalIgnoreCase);
            List<StatisticsPair> pairs = new List<StatisticsPair>();
            foreach (string expiry in expiries)
            {
                token.ThrowIfCancellationRequested();
                StatisticsInstrument other;
                if (!second.TryGetValue(expiry, out other)) continue;
                StatisticsInstrument instrument = first[expiry];
                List<string> timeFrames = new List<string>();
                foreach (string timeFrame in instrument.CandleFiles.Keys)
                {
                    token.ThrowIfCancellationRequested();
                    if (other.CandleFiles.ContainsKey(timeFrame)) timeFrames.Add(timeFrame);
                }
                timeFrames.Sort(StringComparer.Ordinal);
                if (timeFrames.Count == 0) continue;
                pairs.Add(new StatisticsPair
                {
                    Name = instrument.Name + " − " + other.Name,
                    Expiry = expiry,
                    First = instrument,
                    Second = other,
                    TimeFrames = timeFrames
                });
            }
            token.ThrowIfCancellationRequested();
            return pairs;
        }

        public static TimeSpan GetTimeFrameDuration(string timeframe)
        {
            switch (timeframe)
            {
                case "Sec1": return TimeSpan.FromSeconds(1);
                case "Sec2": return TimeSpan.FromSeconds(2);
                case "Sec5": return TimeSpan.FromSeconds(5);
                case "Sec10": return TimeSpan.FromSeconds(10);
                case "Sec15": return TimeSpan.FromSeconds(15);
                case "Sec20": return TimeSpan.FromSeconds(20);
                case "Sec30": return TimeSpan.FromSeconds(30);
                case "Min1": return TimeSpan.FromMinutes(1);
                case "Min2": return TimeSpan.FromMinutes(2);
                case "Min3": return TimeSpan.FromMinutes(3);
                case "Min5": return TimeSpan.FromMinutes(5);
                case "Min10": return TimeSpan.FromMinutes(10);
                case "Min15": return TimeSpan.FromMinutes(15);
                case "Min20": return TimeSpan.FromMinutes(20);
                case "Min30": return TimeSpan.FromMinutes(30);
                case "Min45": return TimeSpan.FromMinutes(45);
                case "Hour1": return TimeSpan.FromHours(1);
                case "Hour2": return TimeSpan.FromHours(2);
                case "Hour4": return TimeSpan.FromHours(4);
                case "Day": return TimeSpan.FromDays(1);
                default: throw new ArgumentException("Unsupported candle timeframe: " + timeframe, nameof(timeframe));
            }
        }

        private static void ValidatePrefix(string prefix)
        {
            if (prefix == null || prefix.Length != 2 || !IsAsciiLetter(prefix[0]) || !IsAsciiLetter(prefix[1]))
            {
                throw new ArgumentException("Contract prefixes must contain exactly two Latin letters.");
            }
        }

        private static bool IsAsciiLetter(char value)
        {
            return (value >= 'A' && value <= 'Z') || (value >= 'a' && value <= 'z');
        }

        private static bool IsContractSuffix(string suffix)
        {
            if (suffix.Length < 2 || suffix.Length > 5 || "FGHJKMNQUVXZ".IndexOf(char.ToUpperInvariant(suffix[0])) < 0)
            {
                return false;
            }
            for (int i = 1; i < suffix.Length; i++)
            {
                if (suffix[i] < '0' || suffix[i] > '9') return false;
            }
            return true;
        }
    }
}
