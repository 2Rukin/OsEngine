/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Generic;
using System.Numerics;
using OsEngine.Entity;

namespace OsEngine.OsTrader.Grids.Futures2
{
    /// <summary>Logical grid direction; independent of the sign of a quoted price.</summary>
    public enum Futures2Direction { Long, Short }
    /// <summary>Manual count, affordable count, or count derived from a positive requested price spacing.</summary>
    public enum Futures2CountMode { Manual, Budget, Step }
    /// <summary>Basis and ownership scope of ordinary profit exits.</summary>
    public enum Futures2ExitMode { LevelFromEnter, LevelFromPlan, WholePosition }
    /// <summary>Units of a nonnegative price distance.</summary>
    public enum Futures2DistanceUnit { Price, Ticks, Zones, PercentBase }
    /// <summary>Campaign state; a paused or faulted campaign still owns its outstanding orders.</summary>
    public enum Futures2State { Draft, Ready, Active, PausingEntries, PausedEntries, Liquidating, Reconciling, Faulted, FlatConfirmed, Stopped }

    /// <summary>Manual immutable-by-convention inputs to a validated plan.</summary>
    /// <remarks>Copy before publication. Collateral is a planning reserve, not proof of available broker margin. THG-PRICE-001.</remarks>
    public sealed class Futures2PlanInput
    {
        /// <summary>Instrument identity fixed for the plan.</summary>
        public string Instrument { get; set; } = "";
        /// <summary>Quote currency identity, required for transfer compatibility.</summary>
        public string Currency { get; set; } = "";
        /// <summary>Logical geometry, trigger and markup direction; IsHedge may invert actual inventory.</summary>
        public Futures2Direction Direction { get; set; }
        /// <summary>Inverts actual orders/inventory only; logical targets may realize a loss. Default false.</summary>
        public bool IsHedge { get; set; }
        /// <summary>Inclusive signed lower bound.</summary>
        public decimal Low { get; set; }
        /// <summary>Inclusive signed upper bound.</summary>
        public decimal High { get; set; }
        /// <summary>Number of active levels.</summary>
        public int Count { get; set; } = 5;
        /// <summary>How the active count is selected before tick interpolation.</summary>
        public Futures2CountMode CountMode { get; set; }
        /// <summary>Positive desired minimum price spacing for Step count mode.</summary>
        public decimal RequestedStep { get; set; } = 0.0001m;
        /// <summary>Positive instrument price increment.</summary>
        public decimal Tick { get; set; } = 0.00001m;
        /// <summary>Positive monetary value of one tick per unit quantity.</summary>
        public decimal TickValue { get; set; } = 1;
        /// <summary>Native quantity increment.</summary>
        public decimal VolumeStep { get; set; } = 1;
        /// <summary>Minimum admissible order quantity.</summary>
        public decimal MinimumVolume { get; set; } = 1;
        /// <summary>Usable campaign budget, after fees/stress and previous-plan obligations.</summary>
        public decimal Budget { get; set; } = 1000;
        /// <summary>Positive planning collateral per unit quantity.</summary>
        public decimal Collateral { get; set; } = 100;
        /// <summary>Zero selects equal-budget allocation; positive selects fixed volume at each level.</summary>
        public decimal FixedVolume { get; set; }
        /// <summary>Positive markup in raw price units.</summary>
        public decimal Markup { get; set; } = 0.00010m;
        /// <summary>Positive explicitly chosen reference for percentage distances.</summary>
        public decimal PercentBase { get; set; } = 1;
        /// <summary>Whether the lower liquidation boundary is enabled.</summary>
        public bool LowerStopEnabled { get; set; }
        /// <summary>Signed lower liquidation price, including a valid zero.</summary>
        public decimal LowerStop { get; set; }
        /// <summary>Whether the upper liquidation boundary is enabled.</summary>
        public bool UpperStopEnabled { get; set; }
        /// <summary>Signed upper liquidation price, including a valid zero.</summary>
        public decimal UpperStop { get; set; }
    }

    /// <summary>Versioned level with stable identity independent of its price.</summary>
    public sealed class Futures2Level
    {
        /// <summary>Zero-based stable level ordinal within a plan version.</summary>
        public int Id { get; set; }
        /// <summary>Validated tick-aligned entry price.</summary>
        public decimal Price { get; set; }
        /// <summary>Maximum filled plus potentially fillable entry quantity.</summary>
        public decimal Volume { get; set; }
        /// <summary>Profit distance in price units.</summary>
        public decimal Markup { get; set; }
        /// <summary>Manual entry participation; existing reservations are retained when disabled.</summary>
        public bool EntryEnabled { get; set; } = true;
        /// <summary>Manual ordinary exit participation; emergency reductions ignore it.</summary>
        public bool ExitEnabled { get; set; } = true;
    }

    /// <summary>Validated plan snapshot published as a whole before any submit.</summary>
    public sealed class Futures2Plan
    {
        /// <summary>Persistent plan identity.</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        /// <summary>Native server/account/instrument/profile identity hash fixed before acceptance.</summary>
        public string EndpointIdentity { get; set; } = "";
        /// <summary>Plan uses the native emulator route; fills have its exact TestPaper identity.</summary>
        public bool PaperExecution { get; set; }
        /// <summary>Accepted input snapshot.</summary>
        public Futures2PlanInput Input { get; set; }
        /// <summary>Actual inventory side, derived from the accepted logical branch and hedge flag.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsLongInventory => (Input.Direction == Futures2Direction.Long) != Input.IsHedge;
        /// <summary>Whether both plans have the same logical geometry direction and physical inversion.</summary>
        public bool SameOrientation(Futures2Plan other) => Input.Direction == other.Input.Direction && Input.IsHedge == other.Input.IsHedge;
        /// <summary>Logical direction-ordered active levels.</summary>
        public List<Futures2Level> Levels { get; set; } = new List<Futures2Level>();
        /// <summary>Budget reserved by a fully populated plan.</summary>
        public decimal Reserved { get; set; }
        /// <summary>Per-level replacement-allocation carry template; only replacement lots inherit it, never later ordinary entries.</summary>
        public Dictionary<int, decimal> ExitCarry { get; set; } = new Dictionary<int, decimal>();
        /// <summary>Ideal step before integer-tick apportionment.</summary>
        public decimal IdealStep => checked((Input.High - Input.Low) / (Input.Count - 1));

        /// <summary>Builds and validates a complete plan without changing a running campaign.</summary>
        /// <remarks>Throws for invalid quantities, funding, precision or overflow. No connector, settings or journal is accessed.</remarks>
        public static Futures2Plan Build(Futures2PlanInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (input.Budget <= 0 || input.Collateral <= 0 || input.TickValue <= 0 || input.VolumeStep <= 0
                || input.MinimumVolume <= 0 || input.FixedVolume < 0 || input.Markup <= 0 || input.PercentBase <= 0)
                throw new ArgumentException("Budget, collateral, tick value, quantities and distances must be positive.");
            if (string.IsNullOrWhiteSpace(input.Instrument) || string.IsNullOrWhiteSpace(input.Currency))
                throw new ArgumentException("Instrument and currency must be explicit.");
            input = Futures2Commands.Copy(input);
            if (input.CountMode == Futures2CountMode.Budget)
            {
                decimal minimum = input.FixedVolume > 0 ? input.FixedVolume : checked(decimal.Ceiling(input.MinimumVolume / input.VolumeStep) * input.VolumeStep);
                BigInteger funded = SignedPriceMath.FloorRatio(new[] { input.Budget }, new[] { input.Collateral, minimum });
                BigInteger ticks = SignedPriceMath.FloorRatio(new[] { checked(input.High - input.Low) }, new[] { input.Tick }) + 1;
                input.Count = checked((int)BigInteger.Min(funded, ticks));
            }
            else if (input.CountMode == Futures2CountMode.Step)
            {
                if (input.RequestedStep <= 0) throw new ArgumentException("Requested grid spacing must be positive.");
                input.Count = checked((int)SignedPriceMath.FloorRatio(new[] { checked(input.High - input.Low) }, new[] { input.RequestedStep }) + 1);
            }
            decimal[] prices = SignedPriceMath.BuildGrid(input.Low, input.High, input.Count, input.Tick);
            if (input.FixedVolume > 0 && input.FixedVolume % input.VolumeStep != 0)
                throw new ArgumentException("Fixed volume is off step.");
            if ((input.LowerStopEnabled && (input.LowerStop >= input.Low || !SignedPriceMath.IsOnTick(input.LowerStop, input.Tick)))
                || (input.UpperStopEnabled && (input.UpperStop <= input.High || !SignedPriceMath.IsOnTick(input.UpperStop, input.Tick))))
                throw new ArgumentException("Enabled liquidation boundaries must be outside the grid and on tick.");
            // A detached copy prevents later UI edits from modifying an accepted plan.
            Futures2PlanInput copy = System.Text.Json.JsonSerializer.Deserialize<Futures2PlanInput>(System.Text.Json.JsonSerializer.Serialize(input));
            Futures2Plan plan = new Futures2Plan { Input = copy };
            BigInteger previous = BigInteger.Zero;
            checked
            {
                for (int i = 0; i < input.Count; i++)
                {
                    BigInteger total = SignedPriceMath.FloorRatio(new[] { i + 1m, input.Budget },
                        new[] { (decimal)input.Count, input.Collateral, input.VolumeStep });
                    decimal volume = input.FixedVolume > 0 ? input.FixedVolume : (decimal)(total - previous) * input.VolumeStep;
                    previous = total;
                    if (volume < input.MinimumVolume || volume % input.VolumeStep != 0)
                        throw new ArgumentException("Insufficient budget for an admissible volume at every level.");
                    plan.Reserved += volume * input.Collateral;
                    plan.Levels.Add(new Futures2Level { Id = i, Price = prices[input.Direction == Futures2Direction.Long ? input.Count - i - 1 : i], Volume = volume, Markup = input.Markup });
                }
                if (plan.Reserved > input.Budget) throw new ArgumentException("The plan exceeds its budget.");
            }
            return plan;
        }

        /// <summary>Converts a manually selected distance using the accepted metadata and positive percentage base.</summary>
        public decimal Distance(decimal value, Futures2DistanceUnit unit)
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
            checked
            {
                switch (unit)
                {
                    case Futures2DistanceUnit.Ticks: return value * Input.Tick;
                    case Futures2DistanceUnit.Zones: return value * IdealStep;
                    case Futures2DistanceUnit.PercentBase: return value * Input.PercentBase / 100;
                    default: return value;
                }
            }
        }

        /// <summary>Calculates the logical exit target (not necessarily a hedge profit); a legitimate average of zero remains a price.</summary>
        public decimal Target(Futures2Level level, decimal average, Futures2ExitMode mode, bool exitAtMiddle)
        {
            bool buy = Input.Direction == Futures2Direction.Long;
            decimal basis = mode == Futures2ExitMode.LevelFromPlan ? (buy ? Math.Max(average, level.Price) : Math.Min(average, level.Price)) : average;
            decimal target = checked(basis + (buy ? level.Markup : -level.Markup));
            if (exitAtMiddle)
            {
                decimal middle = checked(Input.Low + (Input.High - Input.Low) / 2);
                target = buy ? Math.Min(target, middle) : Math.Max(target, middle);
            }
            return SignedPriceMath.Quantize(target, Input.Tick, buy);
        }
    }
}
