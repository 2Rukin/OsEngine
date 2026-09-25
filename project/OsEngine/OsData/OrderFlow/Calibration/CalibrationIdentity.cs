/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OsEngine.OsData.OrderFlow.Calibration
{
    /// <summary>Versioned calibration identities canonicalize decimal scale; view fields are excluded by the callers' explicit identity projections.</summary>
    internal static class CalibrationIdentity
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { Converters = { new CanonicalDecimal() } };
        internal static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, Options))).ToLowerInvariant();
        private sealed class CanonicalDecimal : JsonConverter<decimal>
        {
            public override decimal Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => reader.GetDecimal();
            public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) => writer.WriteRawValue(value.ToString("G29", CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Exact signed percentage bounds; display rounding never determines the passed set.</summary>
    internal static class CalibrationArithmetic
    {
        private static readonly BigInteger Unit = BigInteger.Pow(10, 28);
        internal static bool PercentPasses(decimal numerator, decimal denominator, NumericFilter filter, bool absolute = false)
        {
            if (!filter.Minimum.HasValue && !filter.Maximum.HasValue) { return true; }
            if (denominator == 0) { return filter.Passes(0); }
            BigInteger left = Units(numerator) * 100 * Unit;
            if (absolute) { left = BigInteger.Abs(left); }
            BigInteger divisor = Units(denominator);
            return (!filter.Minimum.HasValue || left >= Units(filter.Minimum.Value) * divisor) &&
                (!filter.Maximum.HasValue || left <= Units(filter.Maximum.Value) * divisor);
        }
        private static BigInteger Units(decimal value)
        {
            int[] bits = decimal.GetBits(value);
            BigInteger coefficient = (uint)bits[0] + ((BigInteger)(uint)bits[1] << 32) + ((BigInteger)(uint)bits[2] << 64);
            return coefficient * BigInteger.Pow(10, 28 - ((bits[3] >> 16) & 255)) * (value < 0 ? -1 : 1);
        }
    }
}
