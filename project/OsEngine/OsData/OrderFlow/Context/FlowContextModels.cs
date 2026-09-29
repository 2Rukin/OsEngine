/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OsEngine.OsData.OrderFlow.Context
{
    /// <summary>Versioned instrument-specific offline settings. A run owns a serialized copy; edits affect the next run.</summary>
    internal sealed class FlowContextSettings
    {
        public string Version { get; set; } = "flow-context-1";
        public string Instrument { get; set; } = "SR";
        public decimal PriceStep { get; set; } = 1;
        public List<FlowContextScale> Scales { get; set; } = new List<FlowContextScale>
        {
            new FlowContextScale { Name = "Локальный", CloudVolume = 100, MinimumClouds = 1, RegionVolume = 100, RegionRangeTicks = 10, FormationSeconds = 120, LifetimeSeconds = 1800 },
            new FlowContextScale { Name = "Рабочий", CloudVolume = 300, MinimumClouds = 2, RegionVolume = 700, RegionRangeTicks = 30, FormationSeconds = 900, LifetimeSeconds = 7200 },
            new FlowContextScale { Name = "Старший", CloudVolume = 600, MinimumClouds = 2, RegionVolume = 1800, RegionRangeTicks = 70, FormationSeconds = 3600, LifetimeSeconds = 28800 }
        };
        public List<FlowContextSession> Sessions { get; set; } = new List<FlowContextSession> { new FlowContextSession() };
        public int MaximumGapSeconds { get; set; } = 60;
        public int SampleSeconds { get; set; } = 10;
        public int WindowSeconds { get; set; } = 5;
        public int BaselineSeconds { get; set; } = 300;
        public int MinimumTrades { get; set; } = 10;
        public decimal MinimumEventVolume { get; set; } = 100;
        public decimal SameSizeShare { get; set; } = .5m;
        public decimal TpsMultiplier { get; set; } = 3;
        public decimal AggressionShare { get; set; } = .6m;
        public decimal MaximumProgressTicks { get; set; } = 2;
        public decimal ExhaustionRatio { get; set; } = .5m;
        public int EventCooldownSeconds { get; set; } = 30;
        public int WarmupSeconds { get; set; } = 1800;
        public List<int> HorizonsSeconds { get; set; } = new List<int> { 900, 3600, 10800 };
        public bool EndOfDay { get; set; } = true;
        public int TargetTicks { get; set; } = 20;
        public int StopTicks { get; set; } = 10;
        public int RetainedActivePerScale { get; set; } = 8;
        public int MaximumRegions { get; set; } = 20000;
        public int MaximumEvents { get; set; } = 100000;
        public int MaximumSamples { get; set; } = 1000000;
        public List<FlowManualAnchor> ManualAnchors { get; set; } = new List<FlowManualAnchor>();

        /// <summary>Rejects ambiguous calendars, excessive budgets and invalid units before any calculation or file publication.</summary>
        public void Validate()
        {
            if (Version != "flow-context-1" || string.IsNullOrWhiteSpace(Instrument) || PriceStep <= 0) { throw new ArgumentException("Проверьте версию, инструмент и положительный шаг цены."); }
            if (Scales == null || Scales.Count != 3 || Scales.Any(scale => scale == null)) { throw new ArgumentException("Нужны ровно три масштаба: локальный, рабочий, старший."); }
            foreach (FlowContextScale scale in Scales) { scale.Validate(); }
            if (Sessions == null || Sessions.Count == 0 || Sessions.Count > 12 || Sessions.Any(session => session == null)) { throw new ArgumentException("Задайте от 1 до 12 интервалов торгового времени."); }
            Sessions = Sessions.OrderBy(session => session.StartMinute).ToList();
            for (int index = 0; index < Sessions.Count; index++)
            {
                FlowContextSession session = Sessions[index];
                if (session.StartMinute < 0 || session.EndMinute > 1440 || session.EndMinute <= session.StartMinute || (index > 0 && session.StartMinute < Sessions[index - 1].EndMinute))
                { throw new ArgumentException("Интервалы сессии должны быть непересекающимися минутами внутри суток, например 600–840."); }
            }
            if (MaximumGapSeconds < 1 || MaximumGapSeconds > 3600 || SampleSeconds < 1 || SampleSeconds > 3600 || WindowSeconds < 1 || WindowSeconds > 3600 || BaselineSeconds < WindowSeconds || BaselineSeconds > 86400 || WarmupSeconds < 0 || WarmupSeconds > 86400)
            { throw new ArgumentException("Некорректные окна, прогрев или допустимая пауза данных."); }
            if (MinimumTrades < 2 || MinimumTrades > 1000000 || MinimumEventVolume <= 0 || SameSizeShare <= 0 || SameSizeShare > 1 || TpsMultiplier <= 1 || AggressionShare <= 0 || AggressionShare > 1 || MaximumProgressTicks < 0 || ExhaustionRatio <= 0 || ExhaustionRatio >= 1 || EventCooldownSeconds < 0 || TargetTicks <= 0 || StopTicks <= 0)
            { throw new ArgumentException("Проверьте пороги событий, доли в диапазоне 0…1 и барьеры в шагах цены."); }
            if (HorizonsSeconds == null || HorizonsSeconds.Count > 8 || HorizonsSeconds.Any(value => value < 1 || value > 86400) || (HorizonsSeconds.Count == 0 && !EndOfDay))
            { throw new ArgumentException("Нужны горизонты 1…86400 секунд (до восьми) либо исход до конца дня."); }
            HorizonsSeconds = HorizonsSeconds.Distinct().OrderBy(value => value).ToList();
            if (RetainedActivePerScale < 1 || RetainedActivePerScale > 32 || MaximumRegions < 1 || MaximumRegions > 100000 || MaximumEvents < 1 || MaximumEvents > 500000 || MaximumSamples < 1 || MaximumSamples > 2000000)
            { throw new ArgumentException("Лимиты памяти выходят за поддерживаемые пределы."); }
            if (ManualAnchors == null || ManualAnchors.Count > 100) { throw new ArgumentException("Допускается до 100 ручных областей."); }
            if (ManualAnchors.Where(anchor => anchor != null).GroupBy(anchor => (anchor.Scale, anchor.Start, anchor.KnownAt, anchor.Low, anchor.High)).Any(group => group.Count() > 1))
            { throw new ArgumentException("Этот ручной якорь уже добавлен в профиль."); }
            foreach (FlowManualAnchor anchor in ManualAnchors)
            {
                if (anchor == null || anchor.Scale < 0 || anchor.Scale > 2 || anchor.Start >= anchor.KnownAt || anchor.Start.Date != anchor.KnownAt.Date || anchor.Low <= 0 || anchor.High < anchor.Low || SessionIndex(anchor.Start) < 0 || SessionIndex(anchor.Start) != SessionIndex(anchor.KnownAt))
                { throw new ArgumentException("Ручная область требует начала раньше момента подтверждения в тех же сутках и корректных границ цены."); }
            }
        }
        public FlowContextSettings Copy() => JsonSerializer.Deserialize<FlowContextSettings>(JsonSerializer.Serialize(this));
        public string Hash() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this)))).ToLowerInvariant();
        public int SessionIndex(DateTime time) => Sessions.FindIndex(session => time.TimeOfDay.TotalMinutes >= session.StartMinute && time.TimeOfDay.TotalMinutes < session.EndMinute);
        public DateTime DayEnd(DateTime time) => time.Date.AddMinutes(Sessions[Sessions.Count - 1].EndMinute);
    }

    /// <summary>Independent cloud detector, area formation and confirmed-structure thresholds for one scale.</summary>
    internal sealed class FlowContextScale
    {
        public string Name { get; set; } = "Масштаб";
        public bool Enabled { get; set; } = true;
        public decimal MinimumTickVolume { get; set; } = 1;
        public decimal CloudVolume { get; set; } = 100;
        public int CloudGapMilliseconds { get; set; } = 1000;
        public int CloudRangeTicks { get; set; } = 5;
        public int MinimumClouds { get; set; } = 2;
        public decimal RegionVolume { get; set; } = 300;
        public int RegionRangeTicks { get; set; } = 20;
        public int CloudPauseSeconds { get; set; } = 120;
        public int FormationSeconds { get; set; } = 600;
        public int LifetimeSeconds { get; set; } = 7200;
        public int BreakoutTicks { get; set; } = 2;
        public int HoldSeconds { get; set; } = 2;
        public int SwingTicks { get; set; } = 10;
        public decimal SwingVolatilityFactor { get; set; }
        public int VolatilityLookbackSeconds { get; set; } = 300;
        public decimal RegionVolatilityFactor { get; set; }
        public int MaximumAdaptiveRangeTicks { get; set; } = 200;
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Name) || MinimumTickVolume <= 0 || CloudVolume <= 0 || CloudGapMilliseconds < 0 || CloudGapMilliseconds > 60000 || CloudRangeTicks < 0 || MinimumClouds < 1 || MinimumClouds > 1000 || RegionVolume <= 0 || RegionRangeTicks < 1 || CloudPauseSeconds < 0 || FormationSeconds < 1 || FormationSeconds > 86400 || LifetimeSeconds < 1 || LifetimeSeconds > 86400 || BreakoutTicks < 1 || HoldSeconds < 0 || SwingTicks < 1 || SwingVolatilityFactor < 0 || RegionVolatilityFactor < 0 || MaximumAdaptiveRangeTicks < RegionRangeTicks || VolatilityLookbackSeconds < 1 || VolatilityLookbackSeconds > 86400)
            { throw new ArgumentException("Недопустимые параметры масштаба «" + Name + "»."); }
        }
    }
    internal sealed class FlowContextSession { public int StartMinute { get; set; } public int EndMinute { get; set; } = 1440; }
    internal sealed class FlowManualAnchor { public int Scale { get; set; } = 1; public DateTime Start { get; set; } public DateTime KnownAt { get; set; } public decimal Low { get; set; } public decimal High { get; set; } }
    internal sealed class FlowContextTick { public DateTime Time { get; set; } public long Sequence { get; set; } public decimal Price { get; set; } public decimal Volume { get; set; } public bool Buy { get; set; } }

    /// <summary>Completed Cloud evidence from the existing detector. Its final geometry is released only at KnownSequence.</summary>
    internal sealed class FlowContextSeed
    {
        public int Scale { get; set; }
        public long FirstSequence { get; set; }
        public long KnownSequence { get; set; }
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public DateTime KnownAt { get; set; }
        public decimal Low { get; set; }
        public decimal High { get; set; }
        public decimal Volume { get; set; }
        public decimal Notional { get; set; }
    }
    internal sealed class FlowContextPoint
    {
        public long Sequence { get; set; }
        public DateTime Time { get; set; }
        public decimal Vwap { get; set; }
        public decimal? Twap { get; set; }
        public decimal Sigma { get; set; }
        public decimal Volume { get; set; }
        public decimal Delta { get; set; }
        public int Position { get; set; }
        public int Retests { get; set; }
        public decimal? LastHigh { get; set; }
        public decimal? LastLow { get; set; }
        public int Structure { get; set; }
    }
    /// <summary>Frozen source rectangle and causal ancestry. Later lifecycle/line evidence is append-only and sequence-gated.</summary>
    internal sealed class FlowContextRegion
    {
        public string Id { get; set; }
        public int Scale { get; set; }
        public string ParentId { get; set; }
        public bool Manual { get; set; }
        public DateTime Start { get; set; }
        public long FirstSequence { get; set; }
        public DateTime KnownAt { get; set; }
        public long KnownSequence { get; set; }
        public decimal Low { get; set; }
        public decimal High { get; set; }
        public decimal InitialMean { get; set; }
        public decimal CloudMean { get; set; }
        public decimal SourceVolume { get; set; }
        public int CloudCount { get; set; }
        public decimal PriorRangeTicks { get; set; }
        public decimal PriorChangeTicks { get; set; }
        public DateTime? End { get; set; }
        public long? EndSequence { get; set; }
        public string EndReason { get; set; }
        public List<FlowContextPoint> Points { get; set; } = new List<FlowContextPoint>();
    }
    internal sealed class FlowContextCoordinate
    {
        public string RegionId { get; set; }
        public int Scale { get; set; }
        public decimal VwapTicks { get; set; }
        public decimal? TwapTicks { get; set; }
        public decimal? SigmaDistance { get; set; }
        public decimal? VwapTwapTicks { get; set; }
        public decimal VwapSlopeTicksPerMinute { get; set; }
        public decimal AgeSeconds { get; set; }
        public decimal Volume { get; set; }
        public decimal LowDistanceTicks { get; set; }
        public decimal HighDistanceTicks { get; set; }
        public int Position { get; set; }
        public int Retests { get; set; }
        public int Structure { get; set; }
    }
    internal sealed class FlowContextEvent
    {
        public string Id { get; set; }
        public string Kind { get; set; }
        public string RegionId { get; set; }
        public long Sequence { get; set; }
        public DateTime Time { get; set; }
        public DateTime ObservedAt { get; set; }
        public decimal Price { get; set; }
        public decimal ObservedPrice { get; set; }
        public int Direction { get; set; }
        public decimal Volume { get; set; }
        public decimal Delta { get; set; }
        public int Trades { get; set; }
        public decimal ProgressTicks { get; set; }
        public decimal SameSizeShare { get; set; }
        public decimal TpsRatio { get; set; }
        public bool WarmedUp { get; set; }
        public List<FlowContextCoordinate> Coordinates { get; set; } = new List<FlowContextCoordinate>();
    }
    /// <summary>Future market path, separate from features. Incomplete horizons have no completed-return claim.</summary>
    internal sealed class FlowContextOutcome
    {
        public string EventId { get; set; }
        public string Horizon { get; set; }
        public DateTime Due { get; set; }
        public string Status { get; set; } = "Incomplete";
        public decimal? ChangeTicks { get; set; }
        public decimal MfeTicks { get; set; }
        public decimal MaeTicks { get; set; }
        public string Barrier { get; set; } = "Neither";
        public long? KnownSequence { get; set; }
        public DateTime? BarrierTime { get; set; }
    }
    internal sealed class FlowContextResult
    {
        public string Version { get; set; } = "flow-context-1";
        public string SourceHash { get; set; }
        public string SettingsHash { get; set; }
        public string SourcePath { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public FlowContextSettings Settings { get; set; }
        public List<FlowContextRegion> Regions { get; set; } = new List<FlowContextRegion>();
        public List<FlowContextEvent> Events { get; set; } = new List<FlowContextEvent>();
        public List<FlowContextOutcome> Outcomes { get; set; } = new List<FlowContextOutcome>();
        public long LastSequence { get; set; }
        public long TickCount { get; set; }
        public long ExcludedTicks { get; set; }
        public int GapCount { get; set; }
    }
}
