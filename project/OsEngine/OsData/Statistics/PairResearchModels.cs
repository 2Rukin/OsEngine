/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using System;
using System.Collections.Generic;

namespace OsEngine.OsData.Statistics
{
    public class StatisticsPair
    {
        public string Name { get; set; }
        public string Expiry { get; set; }
        public StatisticsInstrument First { get; set; }
        public StatisticsInstrument Second { get; set; }
        public List<string> TimeFrames { get; set; } = new List<string>();
    }

    public class PairResearchOptions
    {
        public int SmaPeriod { get; set; } = 120;
        public TimeSpan TimeFrameDuration { get; set; } = TimeSpan.FromMinutes(1);
    }

    public class PairResearchSample
    {
        public DateTime Time { get; set; }
        public decimal PriceA { get; set; }
        public decimal PriceB { get; set; }
        public decimal VolumeA { get; set; }
        public decimal VolumeB { get; set; }
        public decimal Basis { get; set; }
        public decimal? Sma { get; set; }
        public decimal? Deviation { get; set; }
        public double? Sigma { get; set; }
        public double? ZScore { get; set; }
        public int Segment { get; set; }
    }

    public class PairDeviationSummary
    {
        public int Count { get; set; }
        public decimal MeanAbsolute { get; set; }
        public double StandardDeviation { get; set; }
        public decimal P50 { get; set; }
        public decimal P75 { get; set; }
        public decimal P90 { get; set; }
        public decimal P95 { get; set; }
        public decimal P99 { get; set; }
        public decimal MaximumUp { get; set; }
        public decimal MaximumDown { get; set; }
    }

    public class PairResearchEpisode
    {
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public int Direction { get; set; }
        public double SigmaMultiple { get; set; }
        public double EntrySigma { get; set; }
        public decimal EntryDeviation { get; set; }
        public decimal EntryThreshold { get; set; }
        public decimal MaximumAdverseDeviation { get; set; }
        public decimal FurtherAdverseDeviation { get; set; }
        public double DurationMinutes { get; set; }
        public int ObservedBars { get; set; }
        // Returned, Gap or HistoryEnd. End is the last observed time when censored.
        public string Status { get; set; }
    }

    public class PairResearchResult
    {
        public List<PairResearchSample> Samples { get; set; } = new List<PairResearchSample>();
        public PairDeviationSummary Deviations { get; set; }
        public List<PairResearchEpisode> Episodes { get; set; } = new List<PairResearchEpisode>();
        public List<PairEpisodeSummary> EpisodeSummaries { get; set; } = new List<PairEpisodeSummary>();
    }

    public class PairEpisodeSummary
    {
        public double SigmaMultiple { get; set; }
        public int Direction { get; set; }
        public int Count { get; set; }
        public int Returned { get; set; }
        public int Gap { get; set; }
        public int HistoryEnd { get; set; }
        public double? MeanMinutes { get; set; }
        public double? MedianMinutes { get; set; }
        public double? P90Minutes { get; set; }
        public double? MaximumMinutes { get; set; }
        public decimal? MaximumFurtherDeviation { get; set; }
    }
}
