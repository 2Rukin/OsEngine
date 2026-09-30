/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Generic;
using System.Linq;

namespace OsEngine.OsTrader.Grids.Futures2
{
    /// <summary>Causal HJ range state, including explicit initialization and bounded index history.</summary>
    /// <remarks>Updated by the campaign serial stream even when entry/exit filtering is disabled. THG-MATH-001, THG-PRICE-001.</remarks>
    public sealed class Futures2Hj
    {
        /// <summary>Presence of an established origin, including an origin of zero.</summary>
        public bool Initialized { get; set; }
        /// <summary>Positive independent HJ zone width.</summary>
        public decimal Width { get; set; }
        /// <summary>Fixed origin price.</summary>
        public decimal Origin { get; set; }
        /// <summary>Most recent zone index.</summary>
        public int Last { get; set; } = 5;
        /// <summary>Most recent fixed index.</summary>
        public int LastFixed { get; set; } = 5;
        /// <summary>Index at the start of the current directional run.</summary>
        public int Base { get; set; } = 5;
        /// <summary>Current triangular run depth.</summary>
        public int Depth { get; set; }
        /// <summary>Index movement orientation.</summary>
        public int Orientation { get; set; }
        /// <summary>Whether a blocking range is established.</summary>
        public bool Active { get; set; }
        /// <summary>Strict lower blocked boundary.</summary>
        public decimal From { get; set; }
        /// <summary>Strict upper blocked boundary.</summary>
        public decimal To { get; set; }
        /// <summary>Last fifty recorded fixed/provisional indices.</summary>
        public List<int> History { get; set; } = new List<int>();

        /// <summary>Resets HJ at a known signed origin without treating zero as missing.</summary>
        public void Reset(decimal origin, decimal width)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            Initialized = true; Width = width; Origin = origin;
            Last = LastFixed = Base = 5; Depth = Orientation = 0; Active = false;
            History.Clear();
        }

        /// <summary>Updates the triangular movement range and one-zone reversal hysteresis.</summary>
        public void Update(decimal price)
        {
            if (!Initialized) throw new InvalidOperationException("HJ needs a known origin.");
            checked
            {
                decimal up = Origin + 5 * Width;
                decimal index = (up - price) / Width;
                int count = History.Count == 0 ? 5 : Math.Max(5, Math.Max(History.Max() - 5, 5 - History.Min()));
                int current;
                if (Orientation == 0)
                    current = decimal.Ceiling(index) != count && decimal.Floor(index) != count
                        ? (int)(price < Origin ? decimal.Floor(index) : decimal.Ceiling(index)) : count;
                else if (Orientation > 0) { current = (int)decimal.Floor(index); if (Last > current) current++; }
                else { current = (int)decimal.Ceiling(index); if (Last < current) current--; }
                if (current == Last) return;
                int move = current > Last ? 1 : -1;
                if (Orientation == 0) Orientation = move;
                int delta = current - LastFixed;
                if (delta * Orientation > 0)
                {
                    int remainder = delta;
                    while (remainder * Orientation >= Depth)
                    {
                        Depth++;
                        int triangular = Depth * (Depth - 1) / 2 + 1;
                        remainder = delta - triangular * Orientation;
                        LastFixed = Base + triangular * Orientation;
                        UpdateRange(up);
                        Add(LastFixed);
                    }
                }
                if ((current - Last) * Orientation < 0)
                {
                    Depth = 1; LastFixed = current; Base = current - move;
                    UpdateRange(up); Add(LastFixed);
                }
                Last = current; Orientation = move;
                if (current != LastFixed) Add(current);
            }
        }

        private void Add(int index)
        {
            History.Add(index);
            if (History.Count > 50) History.RemoveAt(0);
        }

        private void UpdateRange(decimal up)
        {
            Active = Depth > 1;
            if (!Active) return;
            decimal a = checked(up - LastFixed * Width);
            decimal b = checked(a - (Depth - 1) * Orientation * Width);
            From = Math.Min(a, b); To = Math.Max(a, b);
            if (Orientation > 0) From = checked(From - Width); else To = checked(To + Width);
        }

        /// <summary>Permits both range endpoints; only the strict interior is blocked.</summary>
        public bool Allows(decimal price) => !Active || price <= From || price >= To;
    }

    /// <summary>Serializable trailing helper state; percentage input is based on positive fixed campaign capital.</summary>
    public sealed class Futures2Trail
    {
        /// <summary>Helper activation time.</summary>
        public DateTime? Started { get; set; }
        /// <summary>Time at which empty inventory began.</summary>
        public DateTime? EmptySince { get; set; }
        /// <summary>Maximum observed return, absent before the first valid update.</summary>
        public decimal? Maximum { get; set; }
        /// <summary>Minimum observed return.</summary>
        public decimal? Minimum { get; set; }
        /// <summary>Whether the one-shot mini evaluation has completed.</summary>
        public bool MiniConsumed { get; set; }
        /// <summary>Empty-stop latch; only explicit helper rearm clears it.</summary>
        public bool Disabled { get; set; }
        /// <summary>Earliest remaining lot start: native entry receipt, registration, or declared external increase time.</summary>
        public DateTime? FirstEntry { get; set; }
        /// <summary>Earliest selected creation time, when a portfolio scope supplies it.</summary>
        public DateTime? Created { get; set; }
        /// <summary>Whether selected campaigns have had owned lots, including explicit local registrations.</summary>
        public bool WasActivity { get; set; }
        /// <summary>Whether a voluntary stop has occurred since explicit rearm.</summary>
        public bool HasStopped { get; set; }
        /// <summary>Positive target has been reached since rearm.</summary>
        public bool ProfitReached { get; set; }
        /// <summary>Deferred stop recovery asks the campaign to forbid new entries.</summary>
        public bool ForbidEntries { get; set; }
        /// <summary>Whether a voluntary close is currently requested.</summary>
        public bool Closing { get; set; }
        /// <summary>Cause visible to the operator.</summary>
        public string Cause { get; set; } = "";

        /// <summary>Explicit rearm; it does not clear a campaign emergency latch or native reservations.</summary>
        public void Reset(DateTime now)
        {
            Started = now; EmptySince = null; Maximum = Minimum = null;
            Disabled = MiniConsumed = Closing = HasStopped = ProfitReached = ForbidEntries = false; Cause = "";
        }

        /// <summary>Evaluates only fresh, in-session return input; stale callers must not invoke it.</summary>
        /// <returns>True when voluntary reduction is requested; emergency interpretation belongs to the campaign.</returns>
        public bool Evaluate(Futures2Policy policy, decimal percent, bool empty, DateTime now)
        {
            if (!policy.Trailing || Disabled) return false;
            if (!Started.HasValue) Reset(now);
            Maximum = Math.Max(Maximum ?? percent, percent);
            Minimum = Math.Min(Minimum ?? percent, percent);
            if (percent > 0 && percent >= policy.TrailTarget && !ProfitReached)
            { ProfitReached = true; Cause = "Take profit"; }
            if (policy.EmptyStopMinutes > 0 && !WasActivity && now - (Created ?? Started.Value) > TimeSpan.FromMinutes(policy.EmptyStopMinutes))
            { Disabled = true; Closing = false; Cause = "Empty stop"; return false; }
            decimal stop = policy.TrailStep;
            if (policy.TrailDynamic && policy.TrailTarget > 0)
                stop = Math.Clamp(decimal.Round(policy.TrailStep - policy.TrailStep * Maximum.Value / policy.TrailTarget, 2), policy.TrailMinimum, policy.TrailStep);
            decimal actual = Maximum > 0 && Maximum >= policy.TrailTarget ? policy.TrailTargetStep : stop;
            decimal drawdown = policy.TrailFromMax || Maximum < 0 || Maximum >= policy.TrailTarget ? Maximum.Value - percent : -percent;
            if (!HasStopped && drawdown > actual)
            { HasStopped = Closing = true; if (!ProfitReached) Cause = "Drawdown"; }
            if (!HasStopped && policy.MiniStop && policy.MiniMinutes > 0 && (!policy.MiniLimited || !MiniConsumed)
                && FirstEntry.HasValue && now - FirstEntry.Value > TimeSpan.FromMinutes(policy.MiniMinutes))
            {
                if ((!policy.MiniLimited || now - FirstEntry.Value < TimeSpan.FromMinutes(policy.MiniMinutes + 1)) && percent < -policy.MiniPercent)
                { HasStopped = Closing = true; Cause = "Mini stop"; }
                MiniConsumed = true;
            }
            if (!HasStopped && policy.TimeStopMinutes > 0 && FirstEntry.HasValue
                && now - FirstEntry.Value > TimeSpan.FromMinutes(policy.TimeStopMinutes))
            { HasStopped = Closing = true; Cause = "Time stop"; }
            if (HasStopped && empty) { Disabled = true; Closing = false; return false; }
            if (HasStopped && policy.DeferredClose && policy.TimeStopMinutes == 0 && policy.EmptyStopMinutes == 0)
            {
                if (ProfitReached && Cause == "Take profit") Closing = percent >= policy.TrailTarget - policy.TrailTargetStep;
                else if (percent > policy.TrailTarget - policy.TrailTargetStep) Cause = "Take profit";
                if (drawdown >= stop) { Cause = "Drawdown"; Closing = true; }
                else if (Cause == "Drawdown") { Closing = false; ForbidEntries = true; }
            }
            return Closing;
        }
    }
}
