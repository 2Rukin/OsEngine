/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OsEngine.OsData.OrderFlow.Explorer
{
    /// <summary>Immutable, independently versioned formation profile. Prices use the manually supplied step; times use source clock.</summary>
    internal sealed record ExplorerProfile
    {
        public string Layer { get; init; } = "Cloud1";
        public string Scale { get; init; } = "Base";
        public bool SingleTicks { get; init; }
        public bool AllTicks { get; init; }
        public decimal MinimumTickVolume { get; init; } = 1;
        public int MaximumGapMilliseconds { get; init; } = 1000;
        public int MaximumRangeTicks { get; init; } = 5;
        public int ContextSeconds { get; init; } = 30;
        public bool AdaptTick { get; init; }
        public int TickWindow { get; init; } = 1000;
        public int TickMinimum { get; init; } = 200;
        public decimal TickPercentile { get; init; } = .75m;
        public bool AdaptGap { get; init; }
        public int PaceSeconds { get; init; } = 30;
        public int PaceMinimum { get; init; } = 20;
        public decimal PaceFactor { get; init; } = 2;
        public int GapMinimum { get; init; } = 1000;
        public int GapMaximum { get; init; } = 5000;
        public bool AdaptRange { get; init; }
        public decimal AtrFactor { get; init; } = .2m;
        public int RangeMinimum { get; init; } = 1;
        public int RangeMaximum { get; init; } = 40;
        public bool RelativeVolume { get; init; }
        public bool TimeOfDayVolume { get; init; }
        public int VolumeWindow { get; init; } = 200;
        public int VolumeMinimum { get; init; } = 50;
        public int VolumeDates { get; init; } = 5;
        public decimal VolumePercentile { get; init; } = .95m;
        public int TimeOfDayDates { get; init; } = 20;
        public int TimeOfDayMinutes { get; init; } = 15;
        public string Key => Layer + "/" + Scale;

        internal void Validate()
        {
            if ((Layer != "Cloud1" && Layer != "Cloud2") || string.IsNullOrWhiteSpace(Scale) ||
                Scale.Any(c => !char.IsLetterOrDigit(c)) || MinimumTickVolume <= 0 || MaximumGapMilliseconds < 0 ||
                MaximumRangeTicks < 0 || ContextSeconds <= 0 || TickMinimum < 1 || TickWindow < TickMinimum ||
                TickWindow > 100000 || TickPercentile <= 0 || TickPercentile > 1 || PaceSeconds < 1 || PaceMinimum < 1 ||
                PaceFactor <= 0 || GapMinimum < 0 || GapMaximum < GapMinimum || AtrFactor <= 0 || RangeMinimum < 1 ||
                RangeMaximum < RangeMinimum || VolumeMinimum < 1 || VolumeWindow < VolumeMinimum || VolumeWindow > 100000 ||
                VolumeDates < 1 || VolumePercentile <= 0 || VolumePercentile > 1 || TimeOfDayDates < 1 ||
                TimeOfDayDates > 100 || TimeOfDayMinutes < 0 || TimeOfDayMinutes > 720)
            { throw new ArgumentException("Invalid Cloud Explorer formation profile."); }
        }
    }

    /// <summary>Independent episode rules over one layer/scale. Disabling this module does not affect the catalog identity.</summary>
    internal sealed record ExplorerEpisodeSpec
    {
        public bool Enabled { get; init; }
        public string Profile { get; init; } = "Cloud1/Base";
        public int MaximumPauseSeconds { get; init; } = 5;
        public int MaximumDurationSeconds { get; init; } = 30;
        public int MaximumZoneTicks { get; init; } = 10;
        public bool AdaptZone { get; init; }
        public decimal AtrFactor { get; init; } = .4m;
        public int ZoneMinimum { get; init; } = 2;
        public int ZoneMaximum { get; init; } = 80;
    }

    /// <summary>Frozen research hypothesis; display filters never enter these parameters or reassign observation groups.</summary>
    internal sealed record ExplorerStudySpec
    {
        public bool Enabled { get; init; }
        public bool SwingsEnabled { get; init; } = true;
        public string Profile { get; init; } = "Cloud1/Base";
        public bool EpisodeTrigger { get; init; }
        public bool RelativeTrigger { get; init; }
        public decimal TriggerVolume { get; init; } = 900;
        public int SwingReversalTicks { get; init; } = 5;
        public bool AdaptSwing { get; init; } = true;
        public decimal SwingAtrFactor { get; init; } = .2m;
        public int WatchMinutes { get; init; } = 60;
        public bool ActivityGate { get; init; }
        public int ActivitySeconds { get; init; } = 300;
        public int ActivityMinimum { get; init; } = 20;
        public int ActivityMaximumPauseSeconds { get; init; } = 60;
        public int StartHour { get; init; }
        public int EndHour { get; init; } = 24;
        public bool VwapGate { get; init; }
        public ImmutableArray<int> HorizonsMinutes { get; init; } = ImmutableArray.Create(3, 9, 18);
        public decimal TargetAtr { get; init; } = 1.5m;
        public decimal AdverseAtr { get; init; } = 1;
    }

    /// <summary>Immutable worker/replay input. Hashes exclude file location, output location and editable display settings.</summary>
    /// <remarks>ORDER-FLOW-CLOUD-EXPLORER-V2-001. Call Validate before background work. No trading integration.</remarks>
    internal sealed record ExplorerRunSpec
    {
        internal const string CatalogVersion = "cloud-catalog-1";
        internal const string EpisodeVersion = "cloud-episodes-1";
        internal const string StudyVersion = "cloud-structure-study-1";
        public string InputPath { get; init; }
        public string OutputRootPath { get; init; }
        public string InputSha256 { get; init; }
        public decimal PriceStep { get; init; } = 1;
        public DateTime? FromDate { get; init; }
        public DateTime? ToDate { get; init; }
        public ImmutableArray<ExplorerProfile> Profiles { get; init; } = ImmutableArray.Create(new ExplorerProfile());
        public ExplorerEpisodeSpec Episodes { get; init; } = new ExplorerEpisodeSpec();
        public ExplorerStudySpec Study { get; init; } = new ExplorerStudySpec();
        public int MaximumBufferItems { get; init; } = 250000;
        public int MaximumMemoryMegabytes { get; init; } = 1024;
        public string CatalogSpecHash => Hash(new { Version = CatalogVersion, Parser = OrderFlowResearchSchema.ParserVersion,
            InputSha256, PriceStep, FromDate, ToDate, Profiles });
        public string EpisodeSpecHash => Episodes.Enabled ? Hash(new { Version = EpisodeVersion, CatalogSpecHash, Episodes }) : null;
        public string StudyHash => Study.Enabled || Study.SwingsEnabled ? Hash(new { Version = StudyVersion, CatalogSpecHash,
            Episode = Study.EpisodeTrigger ? EpisodeSpecHash : null, Study }) : null;

        internal bool Includes(DateTime time) => !FromDate.HasValue || (time.Date >= FromDate.Value.Date && time.Date <= ToDate.Value.Date);
        internal ExplorerProfile StudyProfile => Profiles.FirstOrDefault(p => p.Key == Study.Profile) ?? Profiles[0];

        internal void Validate()
        {
            if (string.IsNullOrWhiteSpace(InputPath) || string.IsNullOrWhiteSpace(OutputRootPath) || PriceStep <= 0 ||
                FromDate.HasValue != ToDate.HasValue || FromDate > ToDate || Profiles.IsDefaultOrEmpty || Profiles.Length > 6 ||
                Profiles.Select(p => p.Key).Distinct().Count() != Profiles.Length || Profiles.GroupBy(p => p.Layer).Any(g => g.Count() > 3) ||
                MaximumBufferItems < 1000 || MaximumMemoryMegabytes < 64)
            { throw new ArgumentException("Invalid Cloud Explorer input, dates, profiles or resource limit."); }
            foreach (ExplorerProfile profile in Profiles) { profile.Validate(); }
            if (Episodes == null || Study == null || Episodes.MaximumPauseSeconds < 0 || Episodes.MaximumDurationSeconds < 0 ||
                Episodes.MaximumZoneTicks < 0 || Episodes.AtrFactor <= 0 || Episodes.ZoneMinimum < 1 || Episodes.ZoneMaximum < Episodes.ZoneMinimum ||
                (Episodes.Enabled && !Profiles.Any(p => p.Key == Episodes.Profile)) || (Study.Enabled && !Profiles.Any(p => p.Key == Study.Profile)) ||
                (Study.Enabled && Study.EpisodeTrigger && (!Episodes.Enabled || Study.Profile != Episodes.Profile)) || Study.TriggerVolume <= 0 ||
                Study.SwingReversalTicks < 1 || Study.SwingAtrFactor <= 0 || Study.WatchMinutes < 1 || Study.WatchMinutes > 1440 ||
                Study.ActivitySeconds < 1 || Study.ActivityMinimum < 1 || Study.ActivityMaximumPauseSeconds < 0 ||
                Study.StartHour < 0 || Study.EndHour > 24 || Study.StartHour >= Study.EndHour || Study.HorizonsMinutes.IsDefaultOrEmpty ||
                Study.HorizonsMinutes.Length > 12 || Study.HorizonsMinutes.Any(h => h < 1 || h > 1440) ||
                Study.HorizonsMinutes.Distinct().Count() != Study.HorizonsMinutes.Length || Study.TargetAtr <= 0 || Study.AdverseAtr <= 0)
            { throw new ArgumentException("Invalid Cloud Explorer episode or study hypothesis."); }
            if (Study.Enabled && Study.RelativeTrigger && !Study.EpisodeTrigger && !StudyProfile.RelativeVolume && !StudyProfile.TimeOfDayVolume)
            { throw new ArgumentException("Для относительного Cloud-trigger включите «Относительный объём» или «Фон того же времени прежних дат» выбранного профиля и пересчитайте каталог."); }
        }

        internal static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value))).ToLowerInvariant();
    }

    /// <summary>Immutable effective limits selected from strictly previous data at the first included tick.</summary>
    internal sealed record ExplorerEffective(decimal TickVolume, int GapMilliseconds, int RangeTicks, decimal? Atr,
        decimal? RollingThreshold, decimal? TimeOfDayThreshold, decimal? RelativeThreshold, string Status);

    /// <summary>Catalog evidence at one prefix or final state. KnownAt is distinct from the last included trade time.</summary>
    internal sealed record ExplorerCloud
    {
        public string Id { get; init; }
        public string Profile { get; init; }
        public long FirstSequence { get; init; }
        public long LastSequence { get; init; }
        public DateTime StartTime { get; init; }
        public DateTime Time { get; init; }
        public DateTime? KnownAt { get; init; }
        public long? KnownSequence { get; init; }
        public string Reason { get; init; } = "Forming";
        public decimal FirstPrice { get; init; }
        public decimal Price { get; init; }
        public decimal Low { get; init; }
        public decimal High { get; init; }
        public decimal Buy { get; init; }
        public decimal Sell { get; init; }
        public decimal Notional { get; init; }
        public int Count { get; init; }
        public decimal RawVolumeBefore { get; init; }
        public decimal RawVolumeThrough { get; init; }
        public ExplorerEffective Effective { get; init; }
        public OrderFlowImbalanceSnapshot Inside { get; init; }
        public OrderFlowImbalanceSnapshot Context { get; init; }
        public decimal? RelativePercentile { get; init; }
        public decimal Volume => OrderFlowVolumeComparison.AddExact(Buy, Sell);
        public decimal Delta => Buy - Sell;
        public decimal Vwap => Volume == 0 ? 0 : Notional / Volume;
        public decimal DurationMilliseconds => (Time.Ticks - StartTime.Ticks) / (decimal)TimeSpan.TicksPerMillisecond;
        public string Kind => Count == 1 ? "SingleTrade" : "Accumulated";
        public string RelativeStatus => !Effective.RelativeThreshold.HasValue ? "Unknown" : Volume >= Effective.RelativeThreshold ? "Pass" : "Fail";
    }

    /// <summary>Streaming index entry. No final fields are borrowed by a trigger from a later prefix.</summary>
    internal sealed record ExplorerPrefix(string Id, string Profile, long FirstSequence, long Sequence, DateTime Time,
        decimal Price, decimal Low, decimal High, decimal Buy, decimal Sell, decimal Vwap, int Count,
        decimal? RelativeThreshold, bool Completed, string Reason);

    /// <summary>Independent display profile. Inclusive ranges operate on saved evidence and never read source ticks.</summary>
    internal sealed record ExplorerView
    {
        public string Schema { get; init; } = "cloud-explorer-view-1";
        public string Profile { get; init; }
        public decimal? MinimumVolume { get; init; } = 900;
        public decimal? MaximumVolume { get; init; }
        public int? MinimumCount { get; init; }
        public int? MaximumCount { get; init; }
        public string Kind { get; init; }
        public decimal? MinimumDuration { get; init; }
        public decimal? MaximumDuration { get; init; }
        public decimal? MinimumRange { get; init; }
        public decimal? MaximumRange { get; init; }
        public decimal? MinimumDelta { get; init; }
        public decimal? MaximumDelta { get; init; }
        public bool AbsoluteDelta { get; init; }
        public decimal? MinimumContextDelta { get; init; }
        public decimal? MaximumContextDelta { get; init; }
        public bool AbsoluteContextDelta { get; init; }
        public string RelativeStatus { get; init; }
        public decimal? MinimumPercentile { get; init; }
        public decimal? MaximumPercentile { get; init; }
        public string AdaptationStatus { get; init; }
        public int StartHour { get; init; }
        public int EndHour { get; init; } = 24;
        public string IdContains { get; init; }
        public string RelatedId { get; init; }
        public OrderFlowImbalanceSource DiagonalSource { get; init; }
        public OrderFlowImbalanceDirection DiagonalDirection { get; init; }
        public decimal MinimumRatio { get; init; } = 300;
        public decimal MinimumDominant { get; init; }
        public decimal MinimumDifference { get; init; }
        public bool ShowFiltered { get; init; }
        public bool ShowControl { get; init; }
        public bool Visible { get; init; } = true;
        public ImmutableHashSet<string> RelatedCloudIds { get; init; } = ImmutableHashSet<string>.Empty;
        public ImmutableDictionary<string, ExplorerView> Layers { get; init; } = ImmutableDictionary<string, ExplorerView>.Empty;

        internal bool Passes(ExplorerCloud cloud, decimal step)
        {
            if (Layers.TryGetValue(cloud.Profile, out ExplorerView layer)) { return layer.Passes(cloud, step); }
            if (!string.IsNullOrEmpty(RelatedId) && !RelatedCloudIds.Contains(cloud.Id)) { return false; }
            if ((!string.IsNullOrEmpty(Profile) && Profile != cloud.Profile) || (!string.IsNullOrEmpty(IdContains) && !cloud.Id.Contains(IdContains, StringComparison.Ordinal)) ||
                !Range(cloud.Volume, MinimumVolume, MaximumVolume) || !Range(cloud.Count, MinimumCount, MaximumCount) ||
                (!string.IsNullOrEmpty(Kind) && Kind != cloud.Kind) || !Range(cloud.DurationMilliseconds, MinimumDuration, MaximumDuration) ||
                !Range((cloud.High - cloud.Low) / step, MinimumRange, MaximumRange) || cloud.StartTime.Hour < StartHour || cloud.StartTime.Hour >= EndHour ||
                (!string.IsNullOrEmpty(RelativeStatus) && cloud.RelativeStatus != RelativeStatus) ||
                ((MinimumPercentile.HasValue || MaximumPercentile.HasValue) && (!cloud.RelativePercentile.HasValue || !Range(cloud.RelativePercentile.Value, MinimumPercentile, MaximumPercentile))) ||
                (!string.IsNullOrEmpty(AdaptationStatus) && !cloud.Effective.Status.Contains(AdaptationStatus, StringComparison.Ordinal)) ||
                !DeltaPasses(cloud.Buy, cloud.Sell, MinimumDelta, MaximumDelta, AbsoluteDelta) ||
                !DeltaPasses(cloud.Context?.BuyVolume ?? 0, cloud.Context?.SellVolume ?? 0, MinimumContextDelta, MaximumContextDelta, AbsoluteContextDelta)) { return false; }
            if (DiagonalSource == OrderFlowImbalanceSource.Off) { return true; }
            OrderFlowImbalanceSettings diagonal = new OrderFlowImbalanceSettings { Source = DiagonalSource, Direction = DiagonalDirection,
                MinimumRatioPercent = MinimumRatio, MinimumDominantVolume = MinimumDominant, MinimumDifference = MinimumDifference };
            diagonal.Validate();
            return diagonal.Passes(cloud.Inside?.WithFloors(diagonal), cloud.Context?.WithFloors(diagonal));
        }

        internal static bool Range(decimal value, decimal? minimum, decimal? maximum) => (!minimum.HasValue || value >= minimum) && (!maximum.HasValue || value <= maximum);
        internal ExplorerView ForProfile(string profile) => Layers.TryGetValue(profile, out ExplorerView layer) ? layer : this;

        private static bool DeltaPasses(decimal buy, decimal sell, decimal? minimum, decimal? maximum, bool absolute)
        {
            if (!minimum.HasValue && !maximum.HasValue) { return true; }
            decimal total = OrderFlowVolumeComparison.AddExact(buy, sell);
            if (total == 0) { return false; }
            decimal delta = absolute ? Math.Abs(buy - sell) : buy - sell;
            return (!minimum.HasValue || OrderFlowVolumeComparison.CompareProducts(delta, 100, total, minimum.Value) >= 0) &&
                (!maximum.HasValue || OrderFlowVolumeComparison.CompareProducts(delta, 100, total, maximum.Value) <= 0);
        }
    }
}
