/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OsEngine.Indicators
{
    /// <summary>Meaning of a registered threshold; it does not imply that a data source supplies this metric.</summary>
    public enum ThresholdMetric { Volume, Delta, OpenInterest, Trades }

    /// <summary>Declares a numeric threshold which a calculation explicitly resolves through its time profiles.</summary>
    /// <param name="Key">Stable calculation key, distinct from its translated label.</param>
    /// <param name="Label">User-facing label.</param>
    /// <param name="Metric">Recorded metric, not a data-availability assertion.</param>
    /// <param name="Minimum">Smallest permitted threshold.</param>
    /// <param name="Maximum">Largest permitted threshold.</param>
    /// <param name="Integer">Whether fractional values are forbidden.</param>
    public sealed record ThresholdDefinition(string Key, string Label, ThresholdMetric Metric,
        decimal Minimum = 0, decimal Maximum = decimal.MaxValue, bool Integer = false)
    {
        /// <summary>Rejects values outside the declared range or integer domain.</summary>
        public void Validate(decimal value)
        {
            if (value < Minimum || value > Maximum || Integer && decimal.Truncate(value) != value)
            { throw new ArgumentException(Label + ": invalid threshold " + value.ToString(CultureInfo.InvariantCulture)); }
        }
    }

    /// <summary>One recurring interval in source-clock minutes, with both entire boundary minutes included.</summary>
    /// <remarks>Start greater than end means overnight. Equal bounds mean one minute. Overlapping rows in one schedule are rejected.</remarks>
    public sealed record ThresholdTimePeriod
    {
        /// <summary>Inclusive first minute of day, from zero through 1439.</summary>
        public int FromMinute { get; init; }
        /// <summary>Inclusive last minute of day, from zero through 1439.</summary>
        public int ToMinute { get; init; } = 1439;
        /// <summary>Overrides for registered numeric keys; omitted keys retain their base thresholds.</summary>
        public ImmutableDictionary<string, decimal> Values { get; init; } = ImmutableDictionary<string, decimal>.Empty;

        /// <summary>Tests a minute without converting the source timezone.</summary>
        public bool Includes(int minute) => FromMinute <= ToMinute
            ? minute >= FromMinute && minute <= ToMinute : minute >= FromMinute || minute <= ToMinute;
    }

    /// <summary>Opt-in immutable schedule shared by indicator thresholds and Order Flow calculations.</summary>
    /// <remarks>
    /// It neither infers metric dependencies nor supplies missing OI/trades. A consumer must explicitly
    /// resolve its thresholds and implement its own reset. Source timestamps are never converted.
    /// Disabled profiles preserve base behavior. Contract: INDICATOR-TIME-PROFILES-001.
    /// </remarks>
    [JsonConverter(typeof(ThresholdTimeProfilesJsonConverter))]
    public sealed record ThresholdTimeProfiles
    {
        /// <summary>Parses an exact decimal threshold with dot or comma, rejecting malformed text instead of treating it as zero.</summary>
        public static decimal ParseThreshold(string text)
        {
            string normalized = text?.Trim().Replace(',', '.');
            if (string.IsNullOrWhiteSpace(normalized) || !decimal.TryParse(normalized,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
                CultureInfo.InvariantCulture, out decimal value))
            { throw new FormatException("Invalid numeric threshold."); }
            if (DecimalIdentity(normalized) != DecimalIdentity(value.ToString("0.############################", CultureInfo.InvariantCulture)))
            { throw new FormatException("Threshold exceeds exact decimal precision."); }
            return value;
        }

        private static (bool Negative, string Digits, long Scale) DecimalIdentity(string text)
        {
            int exponentAt = text.IndexOfAny(new[] { 'e', 'E' });
            int exponent = exponentAt < 0 ? 0 : int.Parse(text.AsSpan(exponentAt + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            string mantissa = exponentAt < 0 ? text : text.Substring(0, exponentAt);
            bool negative = mantissa[0] == '-';
            if (mantissa[0] == '-' || mantissa[0] == '+') { mantissa = mantissa.Substring(1); }
            int point = mantissa.IndexOf('.');
            int fractional = point < 0 ? 0 : mantissa.Length - point - 1;
            string digits = mantissa.Replace(".", "").TrimStart('0');
            string significant = digits.TrimEnd('0');
            return significant.Length == 0 ? (false, "0", 0) : (negative, significant, (long)fractional - exponent - (digits.Length - significant.Length));
        }
        /// <summary>Enables the schedule. False leaves existing calculations unchanged.</summary>
        public bool Enabled { get; init; }
        /// <summary>True uses base thresholds outside all rows; false makes the consumer inactive there.</summary>
        public bool UseBaseOutside { get; init; } = true;
        /// <summary>Requests a consumer-owned state reset on the first observed event of each new interval occurrence.</summary>
        public bool ResetOnStart { get; init; } = true;
        /// <summary>Independent daily rows. No fixed row-count ceiling; intervals may not overlap.</summary>
        public ImmutableArray<ThresholdTimePeriod> Periods { get; init; } = ImmutableArray<ThresholdTimePeriod>.Empty;

        /// <summary>Checks supported keys, numeric domains and inclusive interval overlap before a run or save.</summary>
        public void Validate(IReadOnlyList<ThresholdDefinition> definitions)
        {
            if (Periods.IsDefault) { throw new ArgumentException("Time profiles are missing."); }
            Dictionary<string, ThresholdDefinition> known = definitions.ToDictionary(d => d.Key, StringComparer.Ordinal);
            bool[] occupied = new bool[1440];
            foreach (ThresholdTimePeriod period in Periods)
            {
                if (period == null || period.FromMinute < 0 || period.FromMinute > 1439 || period.ToMinute < 0 || period.ToMinute > 1439 || period.Values == null)
                { throw new ArgumentException("Time bounds must be between 00:00 and 23:59."); }
                for (int minute = 0; minute < occupied.Length; minute++)
                {
                    if (!period.Includes(minute)) { continue; }
                    if (occupied[minute]) { throw new ArgumentException("Time intervals overlap, including their boundary minutes."); }
                    occupied[minute] = true;
                }
                foreach (KeyValuePair<string, decimal> pair in period.Values)
                {
                    if (!known.TryGetValue(pair.Key, out ThresholdDefinition definition))
                    { throw new ArgumentException("Unsupported time threshold: " + pair.Key); }
                    definition.Validate(pair.Value);
                }
            }
            if (Enabled && Periods.Length == 0) { throw new ArgumentException("Add at least one time interval."); }
        }

        /// <summary>Stable semantic identity; row ordering and decimal scale do not change it. Disabled schedules have no identity suffix.</summary>
        public string CanonicalValue()
        {
            if (!Enabled) { return string.Empty; }
            StringBuilder text = new StringBuilder("threshold-time-1|").Append(UseBaseOutside ? '1' : '0').Append(ResetOnStart ? '1' : '0');
            foreach (ThresholdTimePeriod period in Periods.OrderBy(p => p.FromMinute))
            {
                text.Append('|').Append(period.FromMinute.ToString(CultureInfo.InvariantCulture)).Append('-').Append(period.ToMinute.ToString(CultureInfo.InvariantCulture));
                foreach (KeyValuePair<string, decimal> pair in period.Values.OrderBy(p => p.Key, StringComparer.Ordinal))
                { text.Append('|').Append(pair.Key.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(pair.Key).Append('=').Append(pair.Value.ToString("G29", CultureInfo.InvariantCulture)); }
            }
            return text.ToString();
        }

        /// <summary>Encodes settings for existing scalar parameter files without introducing their line or field delimiters.</summary>
        public string Encode() => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(this));

        /// <summary>Decodes persisted settings. Malformed content fails explicitly; it never silently enables base settings.</summary>
        public static ThresholdTimeProfiles Decode(string text) => string.IsNullOrEmpty(text) ? new ThresholdTimeProfiles() :
            JsonSerializer.Deserialize<ThresholdTimeProfiles>(Convert.FromBase64String(text)) ?? throw new ArgumentException("Invalid time profiles.");

        /// <summary>Parses an exact HH:mm source-clock bound; the entire minute participates.</summary>
        public static int ParseMinute(string text)
        {
            if (!TimeSpan.TryParseExact(text?.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out TimeSpan value) || value.TotalDays >= 1)
            { throw new ArgumentException("Use HH:mm between 00:00 and 23:59."); }
            return (int)value.TotalMinutes;
        }

        /// <summary>Formats an inclusive minute bound for the editor.</summary>
        public static string FormatMinute(int minute) => TimeSpan.FromMinutes(minute).ToString(@"hh\:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>Canonical profile JSON independent of row/key insertion order, decimal scale and process hash randomization.</summary>
    internal sealed class ThresholdTimeProfilesJsonConverter : JsonConverter<ThresholdTimeProfiles>
    {
        public override ThresholdTimeProfiles Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            JsonElement root = document.RootElement;
            return new ThresholdTimeProfiles
            {
                Enabled = root.TryGetProperty("Enabled", out JsonElement enabled) && enabled.GetBoolean(),
                UseBaseOutside = !root.TryGetProperty("UseBaseOutside", out JsonElement outside) || outside.GetBoolean(),
                ResetOnStart = !root.TryGetProperty("ResetOnStart", out JsonElement reset) || reset.GetBoolean(),
                Periods = root.TryGetProperty("Periods", out JsonElement periods)
                    ? periods.Deserialize<ImmutableArray<ThresholdTimePeriod>>(options) : ImmutableArray<ThresholdTimePeriod>.Empty
            };
        }

        public override void Write(Utf8JsonWriter writer, ThresholdTimeProfiles value, JsonSerializerOptions options)
        {
            writer.WriteStartObject(); writer.WriteBoolean("Enabled", value.Enabled); writer.WriteBoolean("UseBaseOutside", value.UseBaseOutside);
            writer.WriteBoolean("ResetOnStart", value.ResetOnStart); writer.WriteStartArray("Periods");
            foreach (ThresholdTimePeriod period in value.Periods.OrderBy(p => p.FromMinute))
            {
                writer.WriteStartObject(); writer.WriteNumber("FromMinute", period.FromMinute); writer.WriteNumber("ToMinute", period.ToMinute); writer.WriteStartObject("Values");
                foreach (KeyValuePair<string, decimal> pair in period.Values.OrderBy(p => p.Key, StringComparer.Ordinal))
                { writer.WritePropertyName(pair.Key); writer.WriteRawValue(pair.Value.ToString("G29", CultureInfo.InvariantCulture)); }
                writer.WriteEndObject(); writer.WriteEndObject();
            }
            writer.WriteEndArray(); writer.WriteEndObject();
        }
    }

    /// <summary>Resolved thresholds for one source event. Reset is a transition request, not a completed reset.</summary>
    public sealed record ThresholdSelection(bool Active, bool Reset, ThresholdTimePeriod Period)
    {
        /// <summary>Returns the selected override or the consumer's base threshold.</summary>
        public decimal Value(string key, decimal baseValue) => Period != null && Period.Values.TryGetValue(key, out decimal value) ? value : baseValue;
    }

    /// <summary>Single-consumer transition cursor for a validated immutable schedule; create a new cursor when replay restarts.</summary>
    /// <remarks>Repeated timestamps never request a second reset. Overnight occurrence belongs to its start date, even across a tick-free gap.</remarks>
    public sealed class ThresholdTimeCursor
    {
        private readonly ThresholdTimeProfiles _profiles;
        private ThresholdTimePeriod _previous;
        private long _occurrence = long.MinValue;

        /// <summary>Captures a previously validated immutable schedule, or disabled base behavior for null.</summary>
        public ThresholdTimeCursor(ThresholdTimeProfiles profiles) { _profiles = profiles ?? new ThresholdTimeProfiles(); }

        /// <summary>Selects thresholds using source-clock time; consumer controls event ordering and all reset side effects.</summary>
        public ThresholdSelection Select(DateTime time)
        {
            if (!_profiles.Enabled) { return new ThresholdSelection(true, false, null); }
            int minute = time.Hour * 60 + time.Minute;
            ThresholdTimePeriod selected = _profiles.Periods.FirstOrDefault(p => p.Includes(minute));
            long occurrence = time.Date.Ticks / TimeSpan.TicksPerDay;
            if (selected != null && selected.FromMinute > selected.ToMinute && minute <= selected.ToMinute) { occurrence--; }
            bool reset = selected != null && _profiles.ResetOnStart && (selected != _previous || occurrence != _occurrence);
            _previous = selected; _occurrence = occurrence;
            return new ThresholdSelection(selected != null || _profiles.UseBaseOutside, reset, selected);
        }
    }
}
