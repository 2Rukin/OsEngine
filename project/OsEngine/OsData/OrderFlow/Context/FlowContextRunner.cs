/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.Entity;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;

namespace OsEngine.OsData.OrderFlow.Context
{
    internal sealed class FlowContextRun
    {
        public string PayloadHash { get; set; }
        public FlowContextResult Data { get; set; }
        public OrderFlowResearchResult Prices { get; set; }
    }

    /// <summary>Display-only OHLC buffers at supported base periods. Tick calculations never use these averages.</summary>
    internal sealed class FlowContextPrices
    {
        private readonly Dictionary<OrderFlowDisplayTimeFrame, List<OrderFlowDisplayBar>> _bars = new Dictionary<OrderFlowDisplayTimeFrame, List<OrderFlowDisplayBar>>
        {
            [OrderFlowDisplayTimeFrame.Sec15] = new List<OrderFlowDisplayBar>(),
            [OrderFlowDisplayTimeFrame.Sec30] = new List<OrderFlowDisplayBar>(),
            [OrderFlowDisplayTimeFrame.Min1] = new List<OrderFlowDisplayBar>()
        };
        internal void Add(OrderFlowDeal tick)
        {
            foreach (KeyValuePair<OrderFlowDisplayTimeFrame, List<OrderFlowDisplayBar>> pair in _bars)
            {
                TimeSpan duration = OrderFlowChartTimeFrames.GetDuration(pair.Key);
                DateTime start = new DateTime(tick.Time.Ticks - tick.Time.Ticks % duration.Ticks);
                OrderFlowDisplayBar current = pair.Value.Count == 0 ? null : pair.Value[pair.Value.Count - 1];
                if (current == null || current.TimeStart != start)
                {
                    if (pair.Value.Count >= 1000000) { throw new InvalidDataException("Превышен лимит ценовых интервалов. Сократите период."); }
                    current = new OrderFlowDisplayBar { TimeFrame = pair.Key, TimeStart = start, TimeEnd = start + duration, HasTrades = true, Open = tick.Price, High = tick.Price, Low = tick.Price };
                    pair.Value.Add(current);
                }
                current.High = Math.Max(current.High, tick.Price); current.Low = Math.Min(current.Low, tick.Price); current.Close = tick.Price;
                current.Volume += tick.Volume; current.Delta += tick.Side == Side.Buy ? tick.Volume : -tick.Volume;
            }
        }
        internal OrderFlowResearchResult Result(OrderFlowTickInput metadata) => new OrderFlowResearchResult { Input = metadata, InputHash = metadata.Sha256, DeltaCalculated = false, CloudCalculated = false, Bars = _bars };
    }

    /// <summary>Two pinned, hash-matched passes: existing Cloud detectors, then causal context on every admitted raw trade.</summary>
    /// <remarks>Clouds still open at a session boundary/EOF are not declared completed. Read errors/cancellation publish no result. No network or trading operations.</remarks>
    internal static class FlowContextRunner
    {
        internal static FlowContextRun Run(string path, DateTime? from, DateTime? to, FlowContextSettings settings, CancellationToken cancellation, Action<string> progress = null)
        {
            FlowContextSettings frozen = settings.Copy(); frozen.Validate();
            if (from.HasValue != to.HasValue || from?.Date > to?.Date) { throw new ArgumentException("Укажите обе даты по возрастанию или оставьте обе пустыми."); }
            List<FlowContextSeed> seeds = new List<FlowContextSeed>();
            OrderFlowTickInput metadata = new OrderFlowTickInput();
            OrderFlowCloudAccumulator[] detectors = new OrderFlowCloudAccumulator[3];
            List<OrderFlowCloud>[] completed = { new List<OrderFlowCloud>(), new List<OrderFlowCloud>(), new List<OrderFlowCloud>() };
            DateTime previousDate = default; int previousSession = -1; long count = 0;
            using (OrderFlowTickReader reader = new OrderFlowTickReader(path, metadata, cancellation))
            {
                while (reader.TryRead(out OrderFlowDeal tick))
                {
                    if (!Includes(tick.Time, from, to)) { continue; }
                    int session = frozen.SessionIndex(tick.Time);
                    if (tick.Time.Date != previousDate || session != previousSession)
                    {
                        for (int scale = 0; scale < 3; scale++)
                        {
                            FlowContextScale rule = frozen.Scales[scale]; completed[scale].Clear();
                            detectors[scale] = new OrderFlowCloudAccumulator(new OrderFlowCloudSettings { MinimumTickVolume = rule.MinimumTickVolume,
                                MinimumSumVolume = rule.CloudVolume, MaximumGapMilliseconds = rule.CloudGapMilliseconds, MaximumRangeTicks = rule.CloudRangeTicks }, frozen.PriceStep, completed[scale]);
                        }
                        previousDate = tick.Time.Date; previousSession = session;
                    }
                    if (session < 0) { continue; }
                    for (int scale = 0; scale < 3; scale++)
                    {
                        if (!frozen.Scales[scale].Enabled) { continue; }
                        detectors[scale].Add(tick, cancellation);
                        foreach (OrderFlowCloud cloud in completed[scale])
                        {
                            if (!cloud.CompletionSourceSequence.HasValue) { continue; }
                            seeds.Add(new FlowContextSeed { Scale = scale, FirstSequence = cloud.FirstSourceSequence, KnownSequence = cloud.CompletionSourceSequence.Value,
                                Start = cloud.StartTime, End = cloud.Time, KnownAt = cloud.CompletedAt.Value, Low = cloud.Low, High = cloud.High, Volume = cloud.Volume, Notional = cloud.Notional });
                        }
                        completed[scale].Clear();
                    }
                    if (seeds.Count > Math.Min(1000000L, frozen.MaximumRegions * 100L)) { throw new InvalidDataException("Превышен лимит исходных клаудов. Сократите даты или увеличьте пороги."); }
                    if (++count % 16384 == 0) { progress?.Invoke("Cloud — прочитано " + count.ToString("N0") + ", завершено " + seeds.Count.ToString("N0")); }
                }
            }
            cancellation.ThrowIfCancellationRequested();
            FlowContextEngine engine = new FlowContextEngine(frozen, seeds);
            FlowContextPrices prices = new FlowContextPrices();
            OrderFlowTickInput second = new OrderFlowTickInput(); count = 0;
            using (OrderFlowTickReader reader = new OrderFlowTickReader(path, second, cancellation))
            {
                if (metadata.Sha256 != second.Sha256) { throw new InvalidDataException("Исходный файл изменился между проходами. Повторите расчёт."); }
                while (reader.TryRead(out OrderFlowDeal tick))
                {
                    if (!Includes(tick.Time, from, to)) { continue; }
                    prices.Add(tick);
                    engine.Add(new FlowContextTick { Time = tick.Time, Sequence = tick.SourceSequence, Price = tick.Price, Volume = tick.Volume, Buy = tick.Side == Side.Buy }, cancellation);
                    if (++count % 8192 == 0) { progress?.Invoke("Контекст — " + count.ToString("N0") + " тиков, " + engine.Result.Regions.Count + " областей, " + engine.Result.Events.Count + " событий"); }
                }
            }
            engine.Complete();
            if (engine.Result.TickCount == 0) { throw new InvalidDataException("Нет сделок в выбранных датах и интервалах сессии."); }
            engine.Result.SourceHash = metadata.Sha256; engine.Result.SourcePath = Path.GetFullPath(path); engine.Result.FromDate = from?.Date; engine.Result.ToDate = to?.Date;
            FlowContextRun run = new FlowContextRun { Data = engine.Result, Prices = prices.Result(metadata) };
            run.PayloadHash = PayloadHash(run); return run;
        }
        private static bool Includes(DateTime time, DateTime? from, DateTime? to) => !from.HasValue || (time.Date >= from.Value.Date && time.Date <= to.Value.Date);

        internal static string PayloadHash(FlowContextRun run)
        {
            using SHA256 hash = SHA256.Create();
            using CryptoStream stream = new CryptoStream(Stream.Null, hash, CryptoStreamMode.Write);
            JsonSerializer.Serialize(stream, new { run.Data, run.Prices }); stream.FlushFinalBlock();
            return Convert.ToHexString(hash.Hash).ToLowerInvariant();
        }
        /// <summary>Checks persisted payload identity and causal bounds before any saved evidence enters the chart.</summary>
        internal static void ValidateSaved(FlowContextRun run)
        {
            if (run?.Data?.Settings == null || run.Prices?.Bars == null || run.Data.Version != "flow-context-1" || run.Data.SourceHash?.Length != 64 || run.Data.SourceHash != run.Prices.InputHash)
            { throw new InvalidDataException("Неподдерживаемая или повреждённая схема исследования."); }
            run.Data.Settings.Validate();
            if (run.Data.Settings.Hash() != run.Data.SettingsHash || PayloadHash(run) != run.PayloadHash)
            { throw new InvalidDataException("Контрольная сумма исследования не совпадает."); }
            FlowContextResult data = run.Data;
            if (data.Regions == null || data.Events == null || data.Outcomes == null || data.Regions.Count > data.Settings.MaximumRegions || data.Events.Count > data.Settings.MaximumEvents || data.Regions.Sum(region => (long)region.Points.Count) > data.Settings.MaximumSamples)
            { throw new InvalidDataException("Нарушены лимиты исследования."); }
            Dictionary<string, FlowContextRegion> regions = data.Regions.ToDictionary(region => region.Id);
            foreach (FlowContextRegion region in data.Regions)
            {
                if (region.Scale < 0 || region.Scale > 2 || region.FirstSequence > region.KnownSequence || region.KnownSequence > data.LastSequence || region.Low > region.High || region.Points.Any(point => point.Sequence < region.KnownSequence || point.Sequence > data.LastSequence))
                { throw new InvalidDataException("Некорректные временные границы области."); }
                if (region.ParentId != null && (!regions.TryGetValue(region.ParentId, out FlowContextRegion parent) || parent.Scale <= region.Scale || parent.KnownSequence > region.KnownSequence))
                { throw new InvalidDataException("Нарушена причинная иерархия областей."); }
            }
            if (data.Events.Any(item => item.Sequence > data.LastSequence || item.Coordinates.Any(coordinate => !regions.TryGetValue(coordinate.RegionId, out FlowContextRegion region) || region.KnownSequence > item.Sequence)))
            { throw new InvalidDataException("В контексте события обнаружены будущие данные."); }
        }

        /// <summary>Writes a complete result to an explicitly chosen file by same-directory atomic replacement.</summary>
        internal static void Save<T>(string path, T value, CancellationToken cancellation = default)
        {
            string full = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(full));
            string temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { JsonSerializer.SerializeAsync(stream, value, new JsonSerializerOptions { WriteIndented = true }, cancellation).GetAwaiter().GetResult(); stream.Flush(true); if (stream.Length > 512L * 1024 * 1024) { throw new InvalidDataException("Исследование превышает 512 МБ. Сократите период или число сохраняемых событий/точек."); } }
                cancellation.ThrowIfCancellationRequested();
                File.Move(temporary, full, true);
            }
            finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
        }
        internal static FlowContextSettings LoadSettings(string path)
        {
            if (new FileInfo(path).Length > 1024 * 1024) { throw new InvalidDataException("Файл профиля слишком большой."); }
            using FileStream stream = File.OpenRead(path);
            FlowContextSettings settings = JsonSerializer.Deserialize<FlowContextSettings>(stream) ?? throw new InvalidDataException("Пустой профиль.");
            settings.Validate(); return settings;
        }
    }
}
