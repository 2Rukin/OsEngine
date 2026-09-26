/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OsEngine.OsTrader.AdaptivePositionManager
{
    /// <summary>Normalized tick; microseconds remain source metadata unless their clock semantics are certified.</summary>
    public sealed record ApmTick(string Source, string Instrument, string Session, DateTime Time,
        long ReceiveSequence, string Id, decimal Price, decimal Volume, int Side, int Microseconds);

    /// <summary>
    /// Strict wrapper for the documented seven-column native text format. Source order is preserved,
    /// Unknown Side remains zero, and identifiers never pass through floating point.
    /// </summary>
    /// <remarks>Contract: APM-DATA-001. Parsing does not supply spread, OFI or exchange queue evidence.</remarks>
    public static class ApmTickReader
    {
        /// <summary>Parse yyyyMMdd,HHmmss,decimal-dot-price,decimal-dot-contracts,Buy/Sell/None,microseconds,Id.</summary>
        public static ApmTick Parse(string line, string source, string instrument, long receiveSequence)
        {
            string[] fields = line?.Split(',');
            if (fields == null || fields.Length != 7 || receiveSequence <= 0 || string.IsNullOrWhiteSpace(source)
                || string.IsNullOrWhiteSpace(instrument)) throw new FormatException("Invalid APM tick shape or source.");
            DateTime time = DateTime.ParseExact(fields[0] + fields[1], "yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            decimal price = decimal.Parse(fields[2], NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
            decimal volume = decimal.Parse(fields[3], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
            int microseconds = int.Parse(fields[5], NumberStyles.None, CultureInfo.InvariantCulture);
            int side = fields[4].Trim() switch { "Buy" => 1, "Sell" => -1, "None" or "Unknown" or "" => 0,
                _ => throw new FormatException("Unknown APM Side token.") };
            if (volume <= 0 || microseconds < 0 || microseconds >= 1000000)
                throw new FormatException("Invalid tick quantity or fractional timestamp field.");
            return new ApmTick(source, instrument, fields[0], time, receiveSequence, fields[6], price, volume, side, microseconds);
        }

        /// <summary>Stream a caller-authorized market file without sorting or scanning future rows into current decisions.</summary>
        public static IEnumerable<ApmTick> Read(TextReader reader, string source, string instrument)
        {
            long sequence = 0;
            string line;
            while ((line = reader.ReadLine()) != null)
                yield return Parse(line, source, instrument, ++sequence);
        }

        /// <summary>Hash exact bytes for the run manifest; the caller authorizes and selects the dataset.</summary>
        public static string HashFile(string path)
        {
            using FileStream stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }

        /// <summary>Canonical JSON fingerprint for immutable settings and schedules on this schema/runtime.</summary>
        public static string HashJson<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    }

    /// <summary>
    /// Causal grid statistics for TradeOnly. One serialized owner, fixed-size speed window and bounded gap work.
    /// Earlier grid boundaries sample only earlier known prices; stale intervals restart warmup.
    /// </summary>
    /// <remarks>Uses event time exclusively. Timer events cannot manufacture fresh trades. Contract: APM-DATA-001, APM-MATH-001.</remarks>
    public sealed class ApmFeatures
    {
        private readonly ApmPolicy _policy;
        private readonly ApmDirection _direction;
        private readonly decimal _tick;
        private readonly Queue<decimal> _prices = new Queue<decimal>();
        private DateTime _lastTime;
        private DateTime _lastEventTime;
        private DateTime _nextGrid;
        private DateTime _warmupStart;
        private string _session;
        private long _sequence;
        private decimal _lastPrice;
        private decimal? _previousGridPrice;
        private decimal _sum;
        private int _samples;
        private decimal _slow;
        private decimal _fast;
        private decimal _speed;
        private bool _initialized;
        private bool _rejected;

        /// <summary>Construct an isolated feature stream for a validated campaign profile.</summary>
        public ApmFeatures(ApmCampaignSpec spec, ApmPolicy policy)
        {
            policy.Validate(spec);
            _policy = policy; _direction = spec.Direction; _tick = spec.PriceStep;
        }

        /// <summary>Reset source-session state; no remembered price or quote becomes a fresh observation.</summary>
        public void Reset()
        {
            _lastTime = default; _lastEventTime = default; _nextGrid = default; _warmupStart = default; _session = null;
            _sequence = 0; _prices.Clear(); _previousGridPrice = null; _samples = 0;
            _sum = 0; _slow = 0; _fast = 0; _speed = 0; _initialized = false; _rejected = false;
        }

        /// <summary>Reject non-monotone receive order and late event time; a rejected stream requires explicit reset.</summary>
        public ApmMarket OnTick(ApmTick tick)
        {
            if (tick == null || tick.Volume <= 0 || tick.Side < -1 || tick.Side > 1)
                throw new ArgumentException("Invalid normalized tick.");
            if (_rejected || (_lastEventTime != default && (tick.Time < _lastEventTime || tick.ReceiveSequence <= _sequence)))
            { _rejected = true; return Snapshot(tick.Time, tick.ReceiveSequence, _lastPrice, false, "OUT_OF_ORDER"); }
            if (_session != null && _session != tick.Session) Reset();
            _session = tick.Session;
            _sequence = tick.ReceiveSequence;
            _lastEventTime = tick.Time;
            if (_lastTime != default) Advance(tick.Time, false);
            _lastTime = tick.Time;
            _lastPrice = tick.Price;
            if (_nextGrid == default)
            {
                long gridTicks = TimeSpan.TicksPerSecond * (long)_policy.GridSeconds;
                _nextGrid = new DateTime(tick.Time.Ticks - tick.Time.Ticks % gridTicks, tick.Time.Kind);
                if (_nextGrid < tick.Time) _nextGrid = _nextGrid.AddSeconds(_policy.GridSeconds);
            }
            Advance(tick.Time, true);
            return Snapshot(tick.Time, tick.ReceiveSequence, tick.Price, Ready(tick.Time), tick.Side == 0 ? "UNKNOWN_SIDE" : "OK");
        }

        /// <summary>Advance explicit historical/live clock without changing last observed trade time.</summary>
        public ApmMarket OnTimer(DateTime time, long sequence)
        {
            if (_rejected || (_lastEventTime != default && (time < _lastEventTime || sequence <= _sequence)))
            { _rejected = true; return Snapshot(time, sequence, _lastPrice, false, "OUT_OF_ORDER"); }
            _sequence = sequence;
            _lastEventTime = time;
            if (_lastTime == default) return Snapshot(time, sequence, 0, false, "NO_PRICE");
            Advance(time, true);
            bool ready = !_rejected && Ready(time);
            return Snapshot(time, sequence, _lastPrice, ready, ready ? "OK" : "STALE_OR_WARMUP");
        }

        private void Advance(DateTime time, bool inclusive)
        {
            if (_nextGrid == default || _lastTime == default) return;
            if ((time - _nextGrid).TotalSeconds / _policy.GridSeconds > 4096)
            {
                ResetStatistics();
                _nextGrid = default;
                return;
            }
            while (_nextGrid < time || (inclusive && _nextGrid == time))
            {
                if ((_nextGrid - _lastTime).TotalSeconds > _policy.MaxPriceAgeSeconds)
                {
                    ResetStatistics();
                }
                else Sample(_nextGrid, _lastPrice);
                _nextGrid = _nextGrid.AddSeconds(_policy.GridSeconds);
            }
        }

        private void ResetStatistics()
        {
            _prices.Clear(); _previousGridPrice = null; _samples = 0; _sum = 0;
            _slow = 0; _fast = 0; _speed = 0; _initialized = false; _warmupStart = default;
        }

        private void Sample(DateTime time, decimal price)
        {
            if (_warmupStart == default) _warmupStart = time;
            if (_previousGridPrice.HasValue)
            {
                decimal change = price - _previousGridPrice.Value;
                decimal variance = change * change / _policy.GridSeconds;
                if (!_initialized)
                {
                    _sum += variance;
                    _samples++;
                    _slow = _fast = _sum / _samples;
                    if (_samples >= _policy.MinSamples && (time - _warmupStart).TotalSeconds >= _policy.WarmupSeconds)
                        _initialized = true;
                }
                else
                {
                    decimal aFast = (decimal)(1 - Math.Pow(2, -(double)_policy.GridSeconds / _policy.FastHalfLifeSeconds));
                    decimal aSlow = (decimal)(1 - Math.Pow(2, -(double)_policy.GridSeconds / _policy.SlowHalfLifeSeconds));
                    _fast = (1 - aFast) * _fast + aFast * variance;
                    _slow = (1 - aSlow) * _slow + aSlow * variance;
                }
            }
            _prices.Enqueue(price);
            int length = _policy.SpeedWindowSeconds / _policy.GridSeconds + 1;
            while (_prices.Count > length) _prices.Dequeue();
            _speed = _prices.Count == length ? (int)_direction * (price - _prices.Peek())
                / Math.Max(ApmMathematics.Sqrt(_slow * _policy.SpeedWindowSeconds), _tick) : 0;
            _previousGridPrice = price;
        }

        private bool Ready(DateTime time) => _initialized && !_rejected
            && _prices.Count == _policy.SpeedWindowSeconds / _policy.GridSeconds + 1
            && (time - _lastTime).TotalSeconds <= _policy.MaxPriceAgeSeconds;

        private ApmMarket Snapshot(DateTime time, long sequence, decimal price, bool ready, string quality)
            => new ApmMarket(time, sequence, price, ready, _slow, _fast, _speed, quality);
    }

    /// <summary>Bounded source-ID validator. Exhaustion explicitly blocks the stream instead of silently evicting deduplication evidence.</summary>
    public sealed class ApmTickIdentityGuard
    {
        private readonly Dictionary<string, ApmTick> _seen = new Dictionary<string, ApmTick>();
        private readonly int _capacity;
        private readonly bool _uniqueIdsCertified;

        /// <summary>Choose the identity guarantee from source evidence, never from matching price/size.</summary>
        public ApmTickIdentityGuard(bool uniqueIdsCertified, int capacity = 250000)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            _uniqueIdsCertified = uniqueIdsCertified; _capacity = capacity;
        }

        /// <summary>Return ACCEPT, DUPLICATE, CONFLICT, ID_UNCERTIFIED or CAPACITY; only certified exact duplicates may be dropped.</summary>
        public string Inspect(ApmTick tick)
        {
            if (!_uniqueIdsCertified || string.IsNullOrEmpty(tick.Id)) return "ID_UNCERTIFIED";
            string key = JsonSerializer.Serialize(new[] { tick.Source, tick.Session, tick.Instrument, tick.Id });
            if (_seen.TryGetValue(key, out ApmTick previous))
                return previous with { ReceiveSequence = tick.ReceiveSequence } == tick ? "DUPLICATE" : "CONFLICT";
            if (_seen.Count >= _capacity) return "CAPACITY";
            _seen.Add(key, tick);
            return "ACCEPT";
        }
    }
}
