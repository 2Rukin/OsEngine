/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OsEngine.Entity;

namespace OsEngine.OsTrader.Grids.Futures2
{
    /// <summary>Manual exact-step grid settings for Futures2GridSimple; no connections or trading effects.</summary>
    /// <remarks>Zones are numbered from one, lowest price first, independently of direction.
    /// Bounds, step and markup use raw price units, quantities use native contracts. THG-SIMPLE-017.</remarks>
    public sealed class Futures2SimpleSettings
    {
        /// <summary>Inclusive lower price; negative and zero are permitted.</summary>
        public decimal Low { get; set; }
        /// <summary>Inclusive upper price, strictly greater than Low.</summary>
        public decimal High { get; set; } = 10;
        /// <summary>Exact positive tick-aligned spacing; must divide the range width.</summary>
        public decimal Step { get; set; } = 1;
        /// <summary>Positive default profit distance from each lot's actual average entry price.</summary>
        public decimal Markup { get; set; } = 1;
        /// <summary>Positive default capacity per price level in native contract units.</summary>
        public decimal Lots { get; set; } = 1;
        /// <summary>Long buys grid levels; Short sells them. Hedge is not exposed.</summary>
        public Futures2Direction Direction { get; set; }
        /// <summary>Optional semicolon-separated one-based zone=value markup overrides.</summary>
        public string ZoneMarkups { get; set; } = "";
        /// <summary>Optional semicolon-separated one-based zone=value quantity overrides.</summary>
        public string ZoneLots { get; set; } = "";
        /// <summary>Positive manually allocated total collateral envelope, shared by held and pending entries.</summary>
        public decimal Funds { get; set; } = 100000;
        /// <summary>Positive manual collateral per native contract; must cover a known broker requirement.</summary>
        public decimal Collateral { get; set; } = 100;

        /// <summary>Builds a detached versioned plan; rejects malformed geometry, overrides and underfunding.</summary>
        /// <remarks>Requires contract-unit security metadata. Missing tick cost uses linear price times
        /// max(1, Security.Lot); no percentage helper is enabled by the Simple host.
        /// All validation completes before callers may stage the plan or send orders.</remarks>
        public Futures2Plan Build(Security security)
        {
            if (security == null) throw new ArgumentException("Select the instrument on the first tab.");
            SignedPriceMath.ValidateTick(security.PriceStep);
            if (!Enum.IsDefined(Direction) || Low >= High || Step <= 0 || !SignedPriceMath.HasFivePlaces(Low)
                || !SignedPriceMath.HasFivePlaces(High) || !SignedPriceMath.HasFivePlaces(Step)
                || !SignedPriceMath.IsOnTick(Low, security.PriceStep) || !SignedPriceMath.IsOnTick(High, security.PriceStep)
                || !SignedPriceMath.IsOnTick(Step, security.PriceStep) || (High - Low) % Step != 0)
                throw new ArgumentException("Bounds and step must align to the price tick; range width must divide exactly by step.");
            decimal intervals = (High - Low) / Step;
            if (intervals < 1 || intervals > 9999) throw new ArgumentException("The grid requires 2 to 10000 levels.");
            int count = (int)intervals + 1;
            if (!SignedPriceMath.IsNativeVolume(Lots, security)) throw new ArgumentException("Default lots violate native volume precision, step or minimum.");
            ValidateMarkup(Markup, security.PriceStep);
            if (Funds <= 0 || Collateral <= 0) throw new ArgumentException("Funds limit and collateral must be positive.");
            decimal brokerMargin = Direction == Futures2Direction.Long ? security.MarginBuy : security.MarginSell;
            if (brokerMargin > Collateral) throw new ArgumentException("Collateral per lot is below the known broker requirement.");
            Dictionary<int, decimal> lots = Parse(ZoneLots, count);
            Dictionary<int, decimal> markups = Parse(ZoneMarkups, count);
            decimal[] quantities = Enumerable.Range(1, count).Select(zone => lots.TryGetValue(zone, out decimal value) ? value : Lots).ToArray();
            if (quantities.Any(quantity => !SignedPriceMath.IsNativeVolume(quantity, security)))
                throw new ArgumentException("Zone lots violate native volume precision, step or minimum.");
            foreach (decimal markup in markups.Values) ValidateMarkup(markup, security.PriceStep);
            decimal reserve = checked(quantities.Sum() * Collateral);
            if (reserve > Funds) throw new ArgumentException("Funds limit must cover all configured zone lots and collateral.");
            decimal precisionStep = 1;
            for (int digit = 0; digit < security.DecimalsVolume; digit++) precisionStep /= 10;
            Futures2Plan plan = Futures2Plan.Build(new Futures2PlanInput
            {
                Instrument = security.Name, Currency = "RUB", Direction = Direction,
                Low = Low, High = High, Count = count, CountMode = Futures2CountMode.Manual,
                RequestedStep = Step, Tick = security.PriceStep,
                TickValue = security.PriceStepCost > 0 ? security.PriceStepCost : security.PriceStep * Math.Max(1, security.Lot),
                VolumeStep = security.VolumeStep > 0 ? security.VolumeStep : precisionStep,
                MinimumVolume = Math.Max(precisionStep, security.MinTradeAmount),
                FixedVolume = quantities.Min(), Budget = Funds, Collateral = Collateral,
                Markup = Markup, PercentBase = High - Low
            });
            foreach (Futures2Level level in plan.Levels)
            {
                int zone = Zone(plan, level);
                level.Volume = quantities[zone - 1];
                if (markups.TryGetValue(zone, out decimal markup)) level.Markup = markup;
            }
            plan.Reserved = reserve;
            return plan;
        }

        /// <summary>Maps a plan level ID to its stable, lowest-price-first one-based zone number.</summary>
        public static int Zone(Futures2Plan plan, Futures2Level level)
            => plan.Input.Direction == Futures2Direction.Long ? plan.Levels.Count - level.Id : level.Id + 1;

        private static void ValidateMarkup(decimal markup, decimal tick)
        {
            if (markup <= 0 || !SignedPriceMath.HasFivePlaces(markup) || !SignedPriceMath.IsOnTick(markup, tick))
                throw new ArgumentException("Markup must be positive, tick-aligned and have at most five decimal places.");
        }

        private static Dictionary<int, decimal> Parse(string text, int count)
        {
            Dictionary<int, decimal> result = new Dictionary<int, decimal>();
            if (string.IsNullOrWhiteSpace(text)) return result;
            foreach (string item in text.Split(';'))
            {
                string[] pair = item.Split('=');
                if (pair.Length != 2 || !int.TryParse(pair[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int zone)
                    || zone < 1 || zone > count || !decimal.TryParse(pair[1].Trim().Replace(',', '.'),
                        NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out decimal value)
                    || !result.TryAdd(zone, value))
                    throw new ArgumentException("Use unique zone=value pairs separated by semicolons, for example 1=2;3=4.");
            }
            return result;
        }
    }
}
