/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.OsData.OrderFlow.Explorer;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace OsEngine.OsData.OrderFlow.Calibration
{
    internal enum FormationMode { Single, Chain }
    internal enum RuleKind { Standard, Diagonal }
    internal enum FlowDirection { Any, Buy, Sell }
    internal enum DiagonalSource { Inside, Context }

    /// <summary>Immutable source-date profile. Intervals are half-open and never wrap midnight.</summary>
    /// <remarks>ORDER-FLOW-CLOUD-CALIBRATION-001. Names/tags are presentation; timezone is explicit provenance, never inferred.</remarks>
    internal sealed record TimeRangeProfile
    {
        public string Id { get; init; } = "custom";
        public string Name { get; init; } = "Custom";
        public string Kind { get; init; } = "Custom";
        public int DayMask { get; init; } = 62;
        public TimeSpan StartTime { get; init; }
        public TimeSpan EndTime { get; init; } = TimeSpan.FromDays(1);
        public string ClockMode { get; init; } = "Source";
        public string SourceTimeZone { get; init; }
        public string Tag { get; init; }
        [JsonIgnore] public string Identity => CalibrationIdentity.Hash(new { Id, DayMask, StartTime, EndTime, ClockMode, SourceTimeZone });
        [JsonIgnore] public bool Enabled => ClockMode != "USPreOpen" || !string.IsNullOrWhiteSpace(SourceTimeZone);

        internal void Validate()
        {
            if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name) || DayMask <= 0 || DayMask > 127 ||
                StartTime < TimeSpan.Zero || EndTime > TimeSpan.FromDays(1) || StartTime >= EndTime ||
                (ClockMode != "Source" && ClockMode != "USPreOpen"))
            { throw new ArgumentException("Некорректный временной профиль. Интервал не может пересекать полночь."); }
            if (!Enabled) { throw new ArgumentException("US pre-open отключён: явно выберите timezone исходного файла."); }
            if (!string.IsNullOrWhiteSpace(SourceTimeZone)) { TimeZoneInfo.FindSystemTimeZoneById(SourceTimeZone); }
        }

        internal bool Includes(DateTime time)
        {
            if ((DayMask & (1 << (int)time.DayOfWeek)) == 0 || !Enabled) { return false; }
            if (ClockMode == "Source") { return time.TimeOfDay >= StartTime && time.TimeOfDay < EndTime; }
            TimeZoneInfo source = TimeZoneInfo.FindSystemTimeZoneById(SourceTimeZone);
            DateTime unspecified = DateTime.SpecifyKind(time, DateTimeKind.Unspecified);
            if (source.IsAmbiguousTime(unspecified) || source.IsInvalidTime(unspecified))
            { throw new InvalidDataException("Неоднозначное или отсутствующее source time при переходе DST; укажите однозначный источник."); }
            DateTime ny = TimeZoneInfo.ConvertTimeFromUtc(TimeZoneInfo.ConvertTimeToUtc(unspecified, source),
                TimeZoneInfo.FindSystemTimeZoneById("America/New_York"));
            return ny.DayOfWeek != DayOfWeek.Saturday && ny.DayOfWeek != DayOfWeek.Sunday &&
                ny.TimeOfDay >= TimeSpan.FromMinutes(510) && ny.TimeOfDay < TimeSpan.FromMinutes(570);
        }

        internal static ImmutableArray<TimeRangeProfile> Presets()
        {
            return ImmutableArray.Create(
                Preset("forts-morning", "FORTS Morning", 420, 630), Preset("forts-main", "FORTS Main", 630, 1140),
                Preset("forts-evening", "FORTS Evening 19:00–23:50", 1140, 1431),
                Preset("moex-morning", "MOEX Morning", 410, 630), Preset("moex-main", "MOEX Main", 630, 1140),
                Preset("moex-evening", "MOEX Evening 19:00–23:50", 1140, 1431),
                Preset("saturday", "Saturday", 0, 1440) with { DayMask = 64 },
                Preset("sunday", "Sunday", 0, 1440) with { DayMask = 1 },
                Preset("us-pre-open", "US pre-open 60m", 0, 1440) with { ClockMode = "USPreOpen", DayMask = 127 });
        }
        internal ImmutableArray<(decimal FromMinute, decimal ToMinute)> SourceIntervals(DateTime date)
        {
            if (ClockMode == "Source") { return ImmutableArray.Create(((decimal)StartTime.TotalMinutes, (decimal)EndTime.TotalMinutes)); }
            if (!Enabled) { return ImmutableArray<(decimal, decimal)>.Empty; }
            TimeZoneInfo source = TimeZoneInfo.FindSystemTimeZoneById(SourceTimeZone), ny = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
            ImmutableArray<(decimal, decimal)>.Builder intervals = ImmutableArray.CreateBuilder<(decimal, decimal)>();
            for (int offset = -1; offset <= 1; offset++)
            {
                DateTime nyDate = date.Date.AddDays(offset);
                if (nyDate.DayOfWeek == DayOfWeek.Saturday || nyDate.DayOfWeek == DayOfWeek.Sunday) { continue; }
                DateTime from = TimeZoneInfo.ConvertTimeFromUtc(TimeZoneInfo.ConvertTimeToUtc(nyDate.AddMinutes(510), ny), source);
                DateTime to = TimeZoneInfo.ConvertTimeFromUtc(TimeZoneInfo.ConvertTimeToUtc(nyDate.AddMinutes(570), ny), source);
                if (to <= date.Date || from >= date.Date.AddDays(1)) { continue; }
                intervals.Add((Math.Max(0, (decimal)(from - date.Date).TotalMinutes), Math.Min(1440, (decimal)(to - date.Date).TotalMinutes)));
            }
            return intervals.ToImmutable();
        }
        private static TimeRangeProfile Preset(string id, string name, int start, int end) =>
            new TimeRangeProfile { Id = id, Name = name, Kind = "Preset", StartTime = TimeSpan.FromMinutes(start), EndTime = TimeSpan.FromMinutes(end) };
    }

    /// <summary>Exact segmentation settings, separate from all post-formation filters and view state.</summary>
    internal sealed record FormationSpec(FormationMode Mode, decimal MinimumTickVolume, int MaximumGapMilliseconds,
        int MaximumRangeTicks, int ContextSeconds)
    {
        internal void Validate()
        {
            if (!Enum.IsDefined(Mode) || MinimumTickVolume < 0 || MaximumGapMilliseconds < 0 || MaximumGapMilliseconds > 86400000 ||
                MaximumRangeTicks < 0 || MaximumRangeTicks > 1000000 || ContextSeconds < 1 || ContextSeconds > 86400)
            { throw new ArgumentException("Некорректные параметры формирования Cloud."); }
        }
    }

    /// <summary>Frozen worker input. Resource bounds reject the whole run; no cells or rows are silently sampled away.</summary>
    internal sealed record CalibrationSpec
    {
        public const string Version = "cloud-calibration-1";
        public const string Formulas = "explorer-chain-1;source-range-1;nearest-rank-1;exact-pairs-delta-stack-1";
        public string InputPath { get; init; }
        public string OutputRootPath { get; init; }
        public string InputSha256 { get; init; }
        public bool TickDistributionOnly { get; init; }
        public DateTime? FromDate { get; init; }
        public DateTime? ToDate { get; init; }
        public decimal PriceStep { get; init; } = 1;
        public TimeRangeProfile Range { get; init; } = TimeRangeProfile.Presets()[1];
        public decimal MinimumTickVolume { get; init; } = 1;
        public int ContextSeconds { get; init; } = 30;
        public ImmutableArray<int> Gaps { get; init; } = ImmutableArray.Create(50, 100, 250, 500, 750, 1000, 1500, 2000);
        public ImmutableArray<int> Ranges { get; init; } = ImmutableArray.Create(1, 2, 3, 5, 8, 13, 20);
        public int MaximumCells { get; init; } = 128;
        public int MaximumBufferItems { get; init; } = 250000;
        public int MaximumMemoryMegabytes { get; init; } = 1024;
        public long MaximumCacheBytes { get; init; } = 64L * 1024 * 1024 * 1024;
        public DiagonalSettings Diagonal { get; init; } = new DiagonalSettings();
        [JsonIgnore] public string Hash => CalibrationIdentity.Hash(new { Version, Formulas, Parser = OrderFlowResearchSchema.ParserVersion,
            InputSha256, TickDistributionOnly, FromDate, ToDate, PriceStep, Range = Range.Identity, MinimumTickVolume, ContextSeconds, Gaps, Ranges,
            MaximumCells, MaximumBufferItems, MaximumMemoryMegabytes, MaximumCacheBytes, Diagonal });
        internal string FormationHash(FormationSpec formation) => CalibrationIdentity.Hash(new { Version, Formulas,
            Parser = OrderFlowResearchSchema.ParserVersion, InputSha256, FromDate, ToDate, PriceStep, Range = Range.Identity, formation });
        internal bool IncludesDate(DateTime time) => !FromDate.HasValue || time.Date >= FromDate.Value.Date && time.Date <= ToDate.Value.Date;
        internal void Validate()
        {
            Range.Validate(); Diagonal.Validate();
            if (PriceStep <= 0 || FromDate.HasValue != ToDate.HasValue || FromDate > ToDate ||
                !TickDistributionOnly && (Gaps.IsDefaultOrEmpty || Ranges.IsDefaultOrEmpty) || Gaps.IsDefault || Ranges.IsDefault ||
                Gaps.Distinct().Count() != Gaps.Length || Ranges.Distinct().Count() != Ranges.Length || MaximumCells < 1 || MaximumCells > 256 ||
                (long)Gaps.Length * Ranges.Length > MaximumCells || MaximumBufferItems < 16 || MaximumBufferItems > 2000000 ||
                MaximumMemoryMegabytes < 64 || MaximumMemoryMegabytes > 8192 || MaximumCacheBytes < 1024)
            { throw new ArgumentException("Проверьте даты, PriceStep, уникальные точки сетки и явные лимиты ресурсов (максимум 256 ячеек)."); }
            new FormationSpec(FormationMode.Single, MinimumTickVolume, 0, 0, ContextSeconds).Validate();
            foreach (int gap in Gaps) { foreach (int range in Ranges) { new FormationSpec(FormationMode.Chain, MinimumTickVolume, gap, range, ContextSeconds).Validate(); } }
        }
    }

    /// <summary>Nullable inclusive bounds: null disables that side; all enabled constraints are combined with AND.</summary>
    internal sealed record NumericFilter(decimal? Minimum = null, decimal? Maximum = null)
    {
        internal bool Passes(decimal value) => (!Minimum.HasValue || value >= Minimum) && (!Maximum.HasValue || value <= Maximum);
        internal void Validate() { if (Minimum > Maximum) { throw new ArgumentException("Минимум фильтра больше максимума."); } }
    }

    internal sealed record DiagonalSettings
    {
        public bool Enabled { get; init; }
        public decimal RatioThreshold { get; init; } = 300;
        public decimal MinimumDominantVolume { get; init; }
        public decimal MinimumDifference { get; init; }
        public int MinimumStackLength { get; init; } = 1;
        public FlowDirection Direction { get; init; }
        public DiagonalSource Source { get; init; }
        internal void Validate()
        {
            if (RatioThreshold < 100 || MinimumDominantVolume < 0 || MinimumDifference < 0 || MinimumStackLength < 1 ||
                !Enum.IsDefined(Direction) || !Enum.IsDefined(Source)) { throw new ArgumentException("Некорректный diagonal filter."); }
        }
    }

    /// <summary>Independent immutable post-filter contract. Evaluation only needs stored catalog evidence, never raw input.</summary>
    internal sealed record CloudFilters
    {
        public NumericFilter Volume { get; init; } = new NumericFilter();
        public NumericFilter TradeCount { get; init; } = new NumericFilter();
        public NumericFilter Duration { get; init; } = new NumericFilter();
        public NumericFilter RangeTicks { get; init; } = new NumericFilter();
        public NumericFilter LargestTick { get; init; } = new NumericFilter();
        public FlowDirection DeltaDirection { get; init; }
        public NumericFilter Delta { get; init; } = new NumericFilter();
        public NumericFilter AbsoluteDelta { get; init; } = new NumericFilter();
        public NumericFilter DeltaPercent { get; init; } = new NumericFilter();
        public NumericFilter AbsoluteDeltaPercent { get; init; } = new NumericFilter();
        public NumericFilter DiagonalDelta { get; init; } = new NumericFilter();
        public NumericFilter AbsoluteDiagonalDelta { get; init; } = new NumericFilter();
        public NumericFilter DiagonalDeltaPercent { get; init; } = new NumericFilter();
        public DiagonalSettings Diagonal { get; init; } = new DiagonalSettings();
        internal void Validate()
        {
            foreach (NumericFilter filter in new[] { Volume, TradeCount, Duration, RangeTicks, LargestTick, Delta, AbsoluteDelta,
                DeltaPercent, AbsoluteDeltaPercent, DiagonalDelta, AbsoluteDiagonalDelta, DiagonalDeltaPercent })
            { if (filter == null) { throw new ArgumentException("Отсутствует filter spec."); } filter.Validate(); }
            Diagonal.Validate();
            if (!Enum.IsDefined(DeltaDirection)) { throw new ArgumentException("Некорректное направление delta."); }
        }
    }

    /// <summary>Saved rule references a verified formation bundle; name, enabled and visibility never change semantic identity.</summary>
    internal sealed record CloudRule
    {
        public string Schema { get; init; } = "calibration-rule-1";
        public string Name { get; init; } = "Cloud";
        public bool Enabled { get; init; } = true;
        public bool Visible { get; init; } = true;
        public string BundlePath { get; init; }
        public CalibrationSpec Provenance { get; init; }
        public FormationSpec Formation { get; init; }
        public RuleKind Kind { get; init; }
        public CloudFilters Filters { get; init; } = new CloudFilters();
        [JsonIgnore] public string TimeRangeId => Provenance.Range.Id;
        [JsonIgnore] public string FormationHash => Provenance.FormationHash(Formation);
        [JsonIgnore] public string RuleId => CalibrationIdentity.Hash(new { Schema, FormationHash, Kind, Filters });
        internal void Validate()
        {
            if (Schema != "calibration-rule-1" || Provenance == null || Formation == null || !Enum.IsDefined(Kind))
            { throw new InvalidDataException("Неизвестная схема Cloud rule."); }
            Provenance.Validate(); Formation.Validate(); Filters.Validate();
        }
    }

    /// <summary>Completed catalog evidence; original Explorer evidence is retained unchanged for parity and anatomy.</summary>
    internal sealed record CalibrationEvent(string EventId, string TimeRangeId, string FormationHash, ExplorerCloud Evidence,
        decimal LargestTick, int PriceLevels, decimal TopLevelShare)
    {
        [JsonIgnore] public decimal Volume => Evidence.Volume;
        [JsonIgnore] public decimal Delta => OrderFlowVolumeComparison.AddExact(Evidence.Buy, -Evidence.Sell);
        [JsonIgnore] public decimal DeltaPercent => Volume == 0 ? 0 : Delta / Volume * 100;
        internal string CloudId(CloudRule rule) => rule.RuleId + "/" + Evidence.FirstSequence;
    }

    internal sealed record CalibrationQuality(string InputSha256, long Accepted, DateTime? First, DateTime? Last,
        int SourceDates, long DuplicateTimestamps, long ZeroMicroseconds, long BuyRows, long SellRows)
    {
        public decimal ZeroMicrosecondsPercent => Accepted == 0 ? 0 : 100m * ZeroMicroseconds / Accepted;
    }
}
