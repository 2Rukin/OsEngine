/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Generic;
using System.Linq;

namespace OsEngine.OsTrader.Grids.Futures2
{
    /// <summary>Optional inclusive price region, with explicit enable independent of zero boundaries.</summary>
    public sealed class Futures2Region
    {
        /// <summary>Whether the region participates in policy.</summary>
        public bool Enabled { get; set; }
        /// <summary>Inclusive lower bound.</summary>
        public decimal From { get; set; }
        /// <summary>Inclusive upper bound.</summary>
        public decimal To { get; set; }
        /// <summary>Tests membership; a malformed enabled region fails closed.</summary>
        public bool Contains(decimal price) => Enabled && (From > To || (price >= From && price <= To));
    }

    /// <summary>Runtime policy snapshot; updating it must first drain conflicting working orders.</summary>
    /// <remarks>Units are explicit. These settings do not override emergency ownership or authorize real trading. THG-EXECUTION-001.</remarks>
    public sealed class Futures2Policy
    {
        /// <summary>Both manual session reference prices are known; zero is an admissible reference.</summary>
        public bool GapReferencesKnown { get; set; }
        /// <summary>Previous session close for the gap filter, in raw signed price units.</summary>
        public decimal PreviousSessionClose { get; set; }
        /// <summary>Current session open for the gap filter, in raw signed price units.</summary>
        public decimal CurrentSessionOpen { get; set; }
        /// <summary>Profit exit basis and scope.</summary>
        public Futures2ExitMode ExitMode { get; set; }
        /// <summary>Common whole-position profit distance; zero uses the active plan's initial markup.</summary>
        public decimal WholeMarkup { get; set; }
        /// <summary>Prevent all increases, including preorders.</summary>
        public bool ForbidEntries { get; set; }
        /// <summary>Prevent ordinary reductions; emergency ignores it.</summary>
        public bool ForbidExits { get; set; }
        /// <summary>Prevent entries in the logical Long branch, including physical short in hedge mode.</summary>
        public bool ForbidLong { get; set; }
        /// <summary>Prevent entries in the logical Short branch, including physical long in hedge mode.</summary>
        public bool ForbidShort { get; set; }
        /// <summary>Allow resting entries before activation; rejected when the accepted plan uses IsHedge.</summary>
        public bool PreEntries { get; set; }
        /// <summary>Allow resting exits before activation; rejected when the accepted plan uses IsHedge.</summary>
        public bool PreExits { get; set; }
        /// <summary>Nonnegative preorder proximity.</summary>
        public decimal PreDistance { get; set; }
        /// <summary>Preorder proximity units.</summary>
        public Futures2DistanceUnit PreDistanceUnit { get; set; }
        /// <summary>Native market rather than price-bounded ordinary immediate orders.</summary>
        public bool MarketOrders { get; set; }
        /// <summary>Maximum aggressive displacement in ticks.</summary>
        public int SlippageTicks { get; set; } = 2;
        /// <summary>Adjusted quote trigger displacement in ticks.</summary>
        public int ThresholdTicks { get; set; }
        /// <summary>Maximum submitted native orders per decision pass.</summary>
        public int MaxActions { get; set; } = 1;
        /// <summary>Wait for existing orders before ordinary new actions.</summary>
        public bool SequentialLiquidity { get; set; }
        /// <summary>Combine compatible same-price entry allocations.</summary>
        public bool GroupOrders { get; set; }
        /// <summary>Traverse entry levels and ordinary exit lots by ascending level price without remapping plan IDs or volumes.</summary>
        /// <remarks>Default false retains plan/book order. Equal-price exits preserve book order.
        /// Uses the runtime cancellation barrier and checkpoint schema5. THG-EXECUTION-OPTIONS-013.</remarks>
        public bool AscendingLevelPriority { get; set; }
        /// <summary>Price reached ordinary limits at physical quote plus/minus ThresholdTicks, without extra SlippageTicks.</summary>
        /// <remarks>Default false retains existing pricing. Logical triggers and targets are unchanged;
        /// manual, funded, replacement, protective and unreached preorder paths retain their prices.
        /// Applies to either hedge orientation; market reference price is not a fill guarantee.
        /// Runtime cancellation barrier and schema5 required. THG-EXECUTION-OPTIONS-013.</remarks>
        public bool QuoteOrdinaryLimits { get; set; }
        /// <summary>Stop automatic re-entry after the campaign becomes empty.</summary>
        public bool StopAfterExit { get; set; }
        /// <summary>Clamp ordinary level target toward the middle of the active range.</summary>
        public bool ExitAfterAcross { get; set; }
        /// <summary>Entry blocking interval.</summary>
        public Futures2Region BlockEnter { get; set; } = new Futures2Region();
        /// <summary>Continuous blocking; false allows one release after leaving the interval.</summary>
        public bool BlockEnterAlways { get; set; } = true;
        /// <summary>First ordinary exit blocking interval.</summary>
        public Futures2Region BlockExit { get; set; } = new Futures2Region();
        /// <summary>Second ordinary exit blocking interval.</summary>
        public Futures2Region BlockExit2 { get; set; } = new Futures2Region();
        /// <summary>Rule text: entry/exit; interval/from/to/gap; from; to; any/empty/full; optional file. One rule per line.</summary>
        public string Rules { get; set; } = "";
        /// <summary>Use HJ range to filter entry level prices.</summary>
        public bool HjEntries { get; set; }
        /// <summary>Use HJ range to filter exit target prices.</summary>
        public bool HjExits { get; set; }
        /// <summary>Positive HJ zone width in raw price units.</summary>
        public decimal HjWidth { get; set; } = 20;
        /// <summary>Zero disables the daily positive collateral spending limit.</summary>
        public decimal DayLimit { get; set; }
        /// <summary>Credit confirmed exit collateral against daily spending.</summary>
        public bool CreditDayExits { get; set; }
        /// <summary>Clear daily counter on confirmed empty campaign.</summary>
        public bool ResetDayWhenFlat { get; set; }
        /// <summary>Per-filled-unit monetary fee, charged on each side.</summary>
        public decimal FeePerUnit { get; set; }
        /// <summary>Quote freshness limit in seconds of the decision clock.</summary>
        public int FreshnessSeconds { get; set; } = 10;
        /// <summary>Elapsed working-order lifetime before requesting cancellation.</summary>
        public int OrderLifeSeconds { get; set; } = 30;
        /// <summary>Submit/cancel unknown-outcome threshold; it does not release reservations.</summary>
        public int UnknownAfterSeconds { get; set; } = 30;
        /// <summary>Minimum interval between ordinary decision passes in milliseconds.</summary>
        public int IntervalMilliseconds { get; set; } = 250;
        /// <summary>Zero disables the confirmed-rejection pause threshold.</summary>
        public int RejectionLimit { get; set; } = 10;
        /// <summary>Session rules: allow/disallow;HH:mm;HH:mm, one per line, in event-clock time.</summary>
        public string Sessions { get; set; } = "";
        /// <summary>Signed automatic range shift after a completed flat cycle.</summary>
        public decimal ShiftAfterFlat { get; set; }
        /// <summary>Expand a flat grid when the quote leaves the range.</summary>
        public bool WidenWhenFlat { get; set; }
        /// <summary>Enable portfolio/campaign drawdown trailing decisions.</summary>
        public bool Trailing { get; set; }
        /// <summary>Use maximum observed return rather than zero as the loss origin.</summary>
        public bool TrailFromMax { get; set; } = true;
        /// <summary>Drawdown step in percent of fixed positive campaign budget.</summary>
        public decimal TrailStep { get; set; } = 1;
        /// <summary>Profit threshold selecting target-step behavior.</summary>
        public decimal TrailTarget { get; set; }
        /// <summary>Drawdown step after profit target.</summary>
        public decimal TrailTargetStep { get; set; } = 1;
        /// <summary>Scale stop step with progress toward target.</summary>
        public bool TrailDynamic { get; set; }
        /// <summary>Lower positive bound for dynamic drawdown step.</summary>
        public decimal TrailMinimum { get; set; } = 0.1m;
        /// <summary>Enable early-loss close; MiniLimited selects a one-shot time window.</summary>
        public bool MiniStop { get; set; }
        /// <summary>Early-loss threshold percent.</summary>
        public decimal MiniPercent { get; set; } = 0.5m;
        /// <summary>Early-loss start delay in minutes.</summary>
        public int MiniMinutes { get; set; } = 1;
        /// <summary>Restrict early-loss evaluation to the subsequent one-minute window.</summary>
        public bool MiniLimited { get; set; }
        /// <summary>Zero disables elapsed-time close.</summary>
        public int TimeStopMinutes { get; set; }
        /// <summary>Zero disables helper stop after an empty period.</summary>
        public int EmptyStopMinutes { get; set; }
        /// <summary>Opt-in native owner removal after EmptyStop, only live, never active and never coordinated.</summary>
        /// <remarks>Default false. Tester/Optimizer keep the robot for reporting; unknown legacy history
        /// blocks removal. Does not enable Trailing or EmptyStopMinutes. THG-EMPTY-012.</remarks>
        public bool RemoveEmptyRobot { get; set; }
        /// <summary>Allow helper recovery through cancel-confirm; it leaves entries paused and never revokes emergency.</summary>
        public bool DeferredClose { get; set; }
        /// <summary>Retain this monetary collateral volume during voluntary reductions; emergency always targets zero.</summary>
        public decimal RetainCollateral { get; set; }
        /// <summary>Equalize selected portfolio held collateral using a common retained amount.</summary>
        public bool EqualizeVolumes { get; set; }
        /// <summary>Minimum collateral slice removed when equalizing; independent of signed price.</summary>
        public decimal PieVolume { get; set; }

        /// <summary>Validates policy before replacing a running snapshot.</summary>
        public void Validate()
        {
            if (WholeMarkup < 0 || SlippageTicks < 0 || ThresholdTicks < 0 || MaxActions < 1 || MaxActions > 10000 || PreDistance < 0
                || HjWidth <= 0 || DayLimit < 0 || FeePerUnit < 0 || FreshnessSeconds < 1 || OrderLifeSeconds < 1
                || UnknownAfterSeconds < 1 || IntervalMilliseconds < 0 || RejectionLimit < 0 || TrailStep <= 0
                || TrailTarget < 0 || TrailTargetStep <= 0 || TrailMinimum <= 0 || TrailMinimum > TrailStep
                || PieVolume < 0 || MiniPercent < 0 || MiniMinutes < 0 || TimeStopMinutes < 0 || EmptyStopMinutes < 0 || RetainCollateral < 0)
                throw new ArgumentException("Invalid runtime policy.");
            foreach (Futures2Region region in new[] { BlockEnter, BlockExit, BlockExit2 })
                if (region == null || (region.Enabled && region.From > region.To)) throw new ArgumentException("Invalid price interval.");
            Futures2Rules.Parse(Rules);
            Futures2Rules.InSession(Sessions, DateTime.Today);
        }
    }

    /// <summary>Explicit quote presence and event/receive clock snapshot.</summary>
    public sealed class Futures2Quote
    {
        /// <summary>Whether a bid is present.</summary>
        public bool HasBid { get; set; }
        /// <summary>Signed bid, including valid zero.</summary>
        public decimal Bid { get; set; }
        /// <summary>Whether an ask is present.</summary>
        public bool HasAsk { get; set; }
        /// <summary>Signed ask, including valid zero.</summary>
        public decimal Ask { get; set; }
        /// <summary>Receive time on the campaign decision clock.</summary>
        public DateTime Time { get; set; }
        /// <summary>Whether the native execution endpoint is ready and within its trading session.</summary>
        public bool Ready { get; set; }
        /// <summary>Optional positive known broker margin per unit in the plan currency.</summary>
        public decimal? Margin { get; set; }
        /// <summary>Optional established account free margin in the plan currency.</summary>
        public decimal? FreeMargin { get; set; }
        /// <summary>Gap between two known session reference prices; absent is distinct from zero.</summary>
        public decimal? Gap { get; set; }
        /// <summary>Checks presence, spread and freshness without a positive-price assumption.</summary>
        public bool Valid(DateTime now, int seconds) => Ready && HasBid && HasAsk && Bid <= Ask && Time <= now && now - Time <= TimeSpan.FromSeconds(seconds);
    }

    /// <summary>Parsed user rule with invariant numeric syntax; file rules only inspect explicitly configured paths at runtime.</summary>
    internal sealed class Futures2Rule
    {
        internal bool Entry;
        internal string Kind;
        internal decimal From;
        internal decimal To;
        internal string Condition;
        internal string File;
    }

    internal static class Futures2Rules
    {
        internal static List<Futures2Rule> Parse(string text)
        {
            List<Futures2Rule> rules = new List<Futures2Rule>();
            foreach (string row in (text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] fields = row.Split(';');
                if (fields.Length < 5 || fields.Length > 6 || (fields[0] != "entry" && fields[0] != "exit")
                    || !new[] { "interval", "from", "to", "gap", "file" }.Contains(fields[1])
                    || !new[] { "any", "empty", "full" }.Contains(fields[4])) throw new ArgumentException("Invalid blocking rule: " + row);
                Futures2Rule rule = new Futures2Rule { Entry = fields[0] == "entry", Kind = fields[1],
                    From = decimal.Parse(fields[2], System.Globalization.CultureInfo.InvariantCulture),
                    To = decimal.Parse(fields[3], System.Globalization.CultureInfo.InvariantCulture), Condition = fields[4], File = fields.Length == 6 ? fields[5] : "" };
                if ((rule.Kind == "interval" || rule.Kind == "gap") && rule.From > rule.To) throw new ArgumentException("Reversed rule interval.");
                if (rule.Kind == "gap" && rule.From < 0) throw new ArgumentException("Gap rules compare absolute distance; use nonnegative bounds.");
                if (rule.Kind == "file" && string.IsNullOrWhiteSpace(rule.File)) throw new ArgumentException("File rule needs an explicit path.");
                rules.Add(rule);
            }
            return rules;
        }

        internal static bool Blocked(List<Futures2Rule> rules, bool entry, decimal price, decimal? gap, decimal held, decimal plan)
        {
            foreach (Futures2Rule rule in rules)
            {
                if (rule.Entry != entry || (rule.Condition == "empty" && held != 0) || (rule.Condition == "full" && held < plan)) continue;
                if (rule.Kind == "file")
                {
                    try { if (System.IO.File.Exists(rule.File)) return true; }
                    catch (System.IO.IOException) { return true; }
                    catch (UnauthorizedAccessException) { return true; }
                    continue;
                }
                if (rule.Kind == "gap" && !gap.HasValue) return true;
                decimal value = rule.Kind == "gap" ? Math.Abs(gap.Value) : price;
                if ((rule.Kind == "from" && value >= rule.From) || (rule.Kind == "to" && value <= rule.To)
                    || ((rule.Kind == "interval" || rule.Kind == "gap") && value >= rule.From && value <= rule.To)) return true;
            }
            return false;
        }

        internal static bool InSession(string text, DateTime now)
        {
            bool anyAllow = false;
            bool allowed = false;
            bool denied = false;
            foreach (string row in (text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] f = row.Split(';');
                if (f.Length != 3 || (f[0] != "allow" && f[0] != "disallow")) throw new ArgumentException("Invalid session rule.");
                TimeSpan from = TimeSpan.ParseExact(f[1], @"hh\:mm", System.Globalization.CultureInfo.InvariantCulture);
                TimeSpan to = TimeSpan.ParseExact(f[2], @"hh\:mm", System.Globalization.CultureInfo.InvariantCulture);
                bool match = from == to || (from < to ? now.TimeOfDay >= from && now.TimeOfDay <= to : now.TimeOfDay >= from || now.TimeOfDay <= to);
                if (f[0] == "allow") { anyAllow = true; allowed |= match; } else denied |= match;
            }
            return allowed || (!anyAllow && !denied);
        }
    }
}
