/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Numerics;

namespace OsEngine.Entity
{
    /// <summary>Exact decimal tick arithmetic for explicitly selected signed-price strategies.</summary>
    /// <remarks>
    /// No market data, orders or shared settings are changed. Zero is a successful price,
    /// never an error sentinel. Invalid inputs and overflow throw before an external effect.
    /// Callers choose rounding by order purpose; this does not establish venue capability.
    /// Contract: THG-PRICE-001.
    /// </remarks>
    public static class SignedPriceMath
    {
        /// <summary>Checks positive native contract quantity against volume precision, step and minimum.</summary>
        /// <remarks>Currency-notional minimums require a separate venue-specific model; signed futures use contract units.</remarks>
        public static bool IsNativeVolume(decimal quantity, Security security)
        {
            if (security == null || quantity <= 0 || security.DecimalsVolume < 0 || security.DecimalsVolume > 28
                || security.MinTradeAmountType != MinTradeAmountType.Contract) return false;
            if (decimal.Round(quantity, security.DecimalsVolume) != quantity || quantity < security.MinTradeAmount) return false;
            return security.VolumeStep <= 0 || quantity % security.VolumeStep == 0;
        }

        /// <summary>Requires a positive price tick representable with at most five decimal places.</summary>
        public static void ValidateTick(decimal tick)
        {
            if (tick <= 0 || !HasFivePlaces(tick))
                throw new ArgumentOutOfRangeException(nameof(tick), "Price tick must be positive with at most five decimal places.");
        }

        /// <summary>Tests numeric precision; redundant trailing zeroes do not change the result.</summary>
        public static bool HasFivePlaces(decimal price)
        {
            Fraction(price, out BigInteger numerator, out BigInteger denominator);
            return numerator * 100000 % denominator == 0;
        }

        /// <summary>Tests exact tick membership, including negative and zero prices.</summary>
        public static bool IsOnTick(decimal price, decimal tick)
        {
            ValidateTick(tick);
            Fraction(price, out BigInteger pn, out BigInteger pd);
            Fraction(tick, out BigInteger tn, out BigInteger td);
            return pn * td % (pd * tn) == 0;
        }

        /// <summary>Rounds mathematically down or up to a tick; negative values never truncate toward zero.</summary>
        public static decimal Quantize(decimal price, decimal tick, bool up)
        {
            ValidateTick(tick);
            Fraction(price, out BigInteger pn, out BigInteger pd);
            Fraction(tick, out BigInteger tn, out BigInteger td);
            BigInteger numerator = pn * td;
            BigInteger denominator = pd * tn;
            BigInteger ticks = up ? -Floor(-numerator, denominator) : Floor(numerator, denominator);
            return FromFraction(ticks * tn, td);
        }

        /// <summary>Returns floor(product(numerators)/product(denominators)) without intermediate decimal division.</summary>
        /// <remarks>Every denominator must be positive. Used for cumulative budget apportionment, not account margin approval.</remarks>
        public static BigInteger FloorRatio(decimal[] numerators, decimal[] denominators)
        {
            BigInteger numerator = BigInteger.One;
            BigInteger denominator = BigInteger.One;
            foreach (decimal value in numerators)
            {
                Fraction(value, out BigInteger n, out BigInteger d);
                numerator *= n;
                denominator *= d;
            }
            foreach (decimal value in denominators)
            {
                if (value <= 0) throw new ArgumentOutOfRangeException(nameof(denominators));
                Fraction(value, out BigInteger n, out BigInteger d);
                numerator *= d;
                denominator *= n;
            }
            return Floor(numerator, denominator);
        }

        /// <summary>Builds inclusive ascending prices. Nonuniform tick intervals differ by at most one tick.</summary>
        public static decimal[] BuildGrid(decimal low, decimal high, int count, decimal tick)
        {
            ValidateTick(tick);
            if (low >= high || count < 2 || count > 10000 || !HasFivePlaces(low) || !HasFivePlaces(high)
                || !IsOnTick(low, tick) || !IsOnTick(high, tick))
                throw new ArgumentException("Invalid grid bounds/count/tick alignment.");
            BigInteger first = FloorRatio(new[] { low }, new[] { tick });
            BigInteger last = FloorRatio(new[] { high }, new[] { tick });
            BigInteger span = last - first;
            if (span < count - 1) throw new ArgumentException("Too few ticks for distinct levels.");
            Fraction(tick, out BigInteger tn, out BigInteger td);
            decimal[] result = new decimal[count];
            for (int i = 0; i < count; i++)
                result[i] = FromFraction((first + i * span / (count - 1)) * tn, td);
            return result;
        }

        private static BigInteger Floor(BigInteger numerator, BigInteger denominator)
        {
            BigInteger quotient = BigInteger.DivRem(numerator, denominator, out BigInteger remainder);
            return remainder.Sign < 0 ? quotient - 1 : quotient;
        }

        private static void Fraction(decimal value, out BigInteger numerator, out BigInteger denominator)
        {
            int[] bits = decimal.GetBits(value);
            numerator = (BigInteger)(uint)bits[0] | ((BigInteger)(uint)bits[1] << 32) | ((BigInteger)(uint)bits[2] << 64);
            if ((bits[3] & int.MinValue) != 0) numerator = -numerator;
            denominator = BigInteger.Pow(10, (bits[3] >> 16) & 0x7f);
        }

        private static decimal FromFraction(BigInteger numerator, BigInteger denominator)
        {
            BigInteger divisor = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
            numerator /= divisor;
            denominator /= divisor;
            return checked((decimal)numerator / (decimal)denominator);
        }
    }
}
