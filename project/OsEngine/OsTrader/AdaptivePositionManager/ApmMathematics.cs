/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using System;

namespace OsEngine.OsTrader.AdaptivePositionManager
{
    /// <summary>
    /// Pure decimal target and risk arithmetic. No clock, broker or mutable shared state.
    /// Statistical square roots are checked before conversion back to decimal.
    /// </summary>
    /// <remarks>Contract: APM-MATH-001. Overflow rejects the decision; no numerical fallback permits trading.</remarks>
    public static class ApmMathematics
    {
        /// <summary>Nearest lot; exact ties choose the smaller unsigned exposure.</summary>
        public static decimal Quantize(decimal volume, decimal lot)
        {
            if (volume < 0 || lot <= 0) throw new ArgumentOutOfRangeException(nameof(volume));
            decimal lower = Math.Floor(volume / lot) * lot;
            return volume - lower > lot / 2 ? lower + lot : lower;
        }

        /// <summary>Conservative lot-grid risk cap.</summary>
        public static decimal Floor(decimal volume, decimal lot)
        {
            if (volume < 0 || lot <= 0) throw new ArgumentOutOfRangeException(nameof(volume));
            return Math.Floor(volume / lot) * lot;
        }

        /// <summary>Checked square root for nonnegative variance; returns price per square-root second.</summary>
        public static decimal Sqrt(decimal value)
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
            double result = Math.Sqrt((double)value);
            if (!double.IsFinite(result)) throw new ArithmeticException("Non-finite APM statistic.");
            return checked((decimal)result);
        }

        /// <summary>Reversible linear target at fixed anchor; no consumed-level memory.</summary>
        public static decimal Curve(ApmCampaignSpec spec, decimal anchor, decimal initialFilled,
            decimal price, decimal addScale, decimal reduceScale)
        {
            if (addScale <= 0 || reduceScale <= 0 || initialFilled <= 0 || initialFilled > spec.MaxVolume)
                throw new ArgumentException("Invalid APM curve inputs.");
            decimal x = (int)spec.Direction * (price - anchor);
            return x < 0 ? initialFilled + (spec.MaxVolume - initialFilled) * Math.Min(-x / addScale, 1)
                : initialFilled * Math.Max(1 - x / reduceScale, 0);
        }

        /// <summary>Dimensionless inventory penalty at the current remaining risk horizon.</summary>
        public static decimal Kappa(ApmCampaignSpec spec, ApmPolicy policy, ApmMarket market)
        {
            if (market.SlowVariance < 0) throw new ArgumentOutOfRangeException(nameof(market));
            decimal horizon = Math.Max(0, Math.Min(policy.RiskHorizonSeconds,
                (decimal)(spec.SessionExitTime - market.Time).TotalSeconds));
            decimal normalizedExposure = spec.MoneyPerPriceUnit * spec.MaxVolume / spec.RiskBudgetCurrency;
            return policy.InventoryPenalty * normalizedExposure * normalizedExposure * market.SlowVariance * horizon;
        }

        /// <summary>Direct bounded minimizer with frozen activation normalization, not recursive inventory feedback.</summary>
        public static decimal Target(decimal curve, decimal maximum, decimal kappaStart, decimal kappa)
        {
            if (curve < 0 || maximum <= 0 || kappaStart < 0 || kappa < 0)
                throw new ArgumentException("Invalid APM minimizer inputs.");
            return Math.Clamp(curve * (1 + kappaStart) / (1 + kappa), 0, maximum);
        }

        /// <summary>Compute price scales from the declared fixed or volatility profile.</summary>
        public static (decimal Add, decimal Reduce) Scales(ApmCampaignSpec spec, ApmPolicy policy, ApmMarket market)
        {
            if (policy.FixedScales) return (policy.AddScale, policy.ReduceScale);
            decimal displacement = Sqrt(market.SlowVariance * policy.SpeedWindowSeconds);
            decimal addMultiplier = Math.Min(policy.MaximumScaleMultiplier,
                1 + policy.SpeedScaleFactor * Math.Max(0, -market.Speed));
            decimal reduceMultiplier = Math.Min(policy.MaximumScaleMultiplier,
                1 + policy.SpeedScaleFactor * Math.Max(0, market.Speed));
            return (Math.Max(policy.MinimumScaleTicks * spec.PriceStep, policy.AddVolatilityFactor * displacement) * addMultiplier,
                Math.Max(policy.MinimumScaleTicks * spec.PriceStep, policy.ReduceVolatilityFactor * displacement) * reduceMultiplier);
        }

        /// <summary>Conservative per-contract stop distance in campaign currency; excludes fees.</summary>
        public static decimal UnitStopRisk(ApmCampaignSpec spec, decimal entryPrice)
        {
            decimal stress = spec.HardStopPrice - (int)spec.Direction * spec.StopSlippageReserveTicks * spec.PriceStep;
            return spec.MoneyPerPriceUnit * Math.Max((int)spec.Direction * (entryPrice - stress), 0);
        }
    }
}
