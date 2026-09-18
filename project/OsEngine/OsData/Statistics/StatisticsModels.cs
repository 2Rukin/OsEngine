/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using System;
using System.Collections.Generic;

namespace OsEngine.OsData.Statistics
{
    public class StatisticsInstrument
    {
        public string Name { get; set; }
        public string FullName { get; set; }
        public string DisplayName { get; set; }
        public Dictionary<string, string> CandleFiles { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public class StatisticsCandle
    {
        public DateTime Time { get; set; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public decimal Volume { get; set; }
    }

    public class PairStatisticsOptions
    {
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        // Illustrative defaults only, not an assessment of market liquidity.
        public decimal MinVolumeA { get; set; } = 1;
        public decimal MinVolumeB { get; set; } = 1;
        public int MinActiveBarsA { get; set; } = 1;
        public int MinActiveBarsB { get; set; } = 1;
        public decimal MinCoveragePercent { get; set; } = 80;
    }

    public class PairStatisticsPoint
    {
        public DateTime Time { get; set; }
        public decimal Value { get; set; }
    }

    public class PairStatisticsSummary
    {
        public int Count { get; set; }
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public decimal Minimum { get; set; }
        public decimal Maximum { get; set; }
        public decimal Range { get; set; }
        public decimal Mean { get; set; }
        public double StandardDeviation { get; set; }
    }

    public class PairStatisticsDay
    {
        public DateTime Date { get; set; }
        public decimal VolumeA { get; set; }
        public decimal VolumeB { get; set; }
        public int ActiveBarsA { get; set; }
        public int ActiveBarsB { get; set; }
        public int MatchedBars { get; set; }
        public decimal CoveragePercent { get; set; }
        public bool Accepted { get; set; }
        public string Reason { get; set; }
        public PairStatisticsSummary Summary { get; set; }
    }

    public class PairStatisticsInterval
    {
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public int Days { get; set; }
        public PairStatisticsSummary Summary { get; set; }
    }

    public class PairStatisticsResult
    {
        public List<PairStatisticsPoint> Points { get; set; } = new List<PairStatisticsPoint>();
        public List<PairStatisticsDay> Days { get; set; } = new List<PairStatisticsDay>();
        public List<PairStatisticsInterval> Intervals { get; set; } = new List<PairStatisticsInterval>();
        public PairStatisticsSummary Summary { get; set; }
    }
}
