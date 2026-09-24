/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;

namespace OsEngine.OsData.OrderFlow.Explorer
{
    /// <summary>Versioned, bounded, outcome-independent grammar. Enumeration is simplest-first and fully auditable.</summary>
    internal static class ExplorerPatternGrammar
    {
        internal static IEnumerable<ExplorerPatternRule> Rules(ExplorerPatternSpec plan)
        {
            List<ExplorerPatternCondition> atoms = new List<ExplorerPatternCondition>();
            if (plan.Context != "Week")
            {
                foreach (string feature in new[] { "DayDelta", "CloudDelta", "VwapDistance" })
                { foreach (int sign in new[] { 1, -1 }) { atoms.Add(new ExplorerPatternCondition(feature, feature == "VwapDistance" ? 0 : .25m, sign)); } }
                atoms.Add(new ExplorerPatternCondition("RelativeVolume", 1)); atoms.Add(new ExplorerPatternCondition("DayEpisodes", 1));
            }
            if (plan.Context != "Day") { foreach (int sign in new[] { 1, -1 }) { atoms.Add(new ExplorerPatternCondition("WeekDelta", .25m, sign)); } }
            foreach (string order in new[] { "SellBeforeLow", "BuyBeforeHigh" })
            { foreach (int window in new[] { 5, 30, 120 }) { atoms.Add(new ExplorerPatternCondition(order, 1, 1, window)); } }
            foreach (ExplorerPatternCondition atom in atoms) { yield return Rule(ImmutableArray.Create(atom)); }
            for (int i = 0; i < atoms.Count; i++)
            {
                for (int j = i + 1; j < atoms.Count; j++)
                { if (atoms[i].Feature != atoms[j].Feature) { yield return Rule(ImmutableArray.Create(atoms[i], atoms[j])); } }
            }
            for (int i = 0; i < atoms.Count; i++)
            {
                for (int j = i + 1; j < atoms.Count; j++)
                {
                    for (int k = j + 1; k < atoms.Count; k++)
                    {
                        if (atoms[i].Feature != atoms[j].Feature && atoms[i].Feature != atoms[k].Feature && atoms[j].Feature != atoms[k].Feature)
                        { yield return Rule(ImmutableArray.Create(atoms[i], atoms[j], atoms[k])); }
                    }
                }
            }
        }
        private static ExplorerPatternRule Rule(ImmutableArray<ExplorerPatternCondition> conditions) => new ExplorerPatternRule(
            ExplorerRunSpec.Hash(new { ExplorerPatternSpec.Grammar, Conditions = conditions }), conditions, string.Join("; и ", conditions.Select(Text)));
        internal static string Text(ExplorerPatternCondition c)
        {
            if (c.Feature == "SellBeforeLow") { return $"продажи Cloud → подтверждение минимума за {c.WindowMinutes} мин"; }
            if (c.Feature == "BuyBeforeHigh") { return $"покупки Cloud → подтверждение максимума за {c.WindowMinutes} мин"; }
            string name = c.Feature switch { "DayDelta" => "дельта текущего дня", "WeekDelta" => "дельта известной части недели", "CloudDelta" => "дельта Cloud/эпизода",
                "VwapDistance" => "расстояние от дневного VWAP, ATR", "RelativeVolume" => "объём / предыдущий относительный порог", "DayEpisodes" => "закрытые эпизоды дня", _ => c.Feature };
            return name + (c.Sign > 0 ? " ≥ " : " ≤ −") + c.Threshold.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        internal static bool? Matches(ExplorerPatternRule rule, ExplorerPatternSnapshot snapshot)
        {
            bool unknown = false;
            foreach (ExplorerPatternCondition c in rule.Conditions)
            {
                decimal? value = c.Feature switch
                {
                    "DayDelta" => snapshot.DayDelta, "WeekDelta" => snapshot.WeekStatus == "IncompleteWeek" ? null : snapshot.WeekDelta,
                    "CloudDelta" => snapshot.Kind == "CloudPrefix" || snapshot.Kind == "EpisodeClosed" ? snapshot.CloudDelta : null,
                    "VwapDistance" => snapshot.VwapDistance, "RelativeVolume" => snapshot.RelativeVolume, "DayEpisodes" => snapshot.DayEpisodes,
                    "SellBeforeLow" => Ordered(snapshot.LastSellSequence, snapshot.LastLowSequence, snapshot.LastSellTime, snapshot.LastLowTime, snapshot, c.WindowMinutes) ? 1 : 0,
                    "BuyBeforeHigh" => Ordered(snapshot.LastBuySequence, snapshot.LastHighSequence, snapshot.LastBuyTime, snapshot.LastHighTime, snapshot, c.WindowMinutes) ? 1 : 0,
                    _ => null
                };
                if (!value.HasValue) { unknown = true; }
                else if (c.Sign * value.Value < c.Threshold) { return false; }
            }
            return unknown ? null : true;
        }
        private static bool Ordered(long first, long second, DateTime? firstTime, DateTime? secondTime, ExplorerPatternSnapshot snapshot, int minutes) =>
            first > 0 && first < second && second <= snapshot.KnownSequence && firstTime.HasValue && secondTime.HasValue &&
            firstTime >= snapshot.KnownAt.AddMinutes(-minutes) && secondTime <= snapshot.KnownAt;
    }

    /// <summary>Fit-only strata and rank, then one frozen Test application. Matching uses first causal anchors, not outcomes.</summary>
    internal static class ExplorerPatternEvaluation
    {
        internal sealed record Strata(decimal ActivityLow, decimal ActivityHigh, decimal AtrLow, decimal AtrHigh)
        {
            internal string Key(ExplorerPatternSnapshot s) => s.KnownAt.Hour + "/" + Band(s.Activity, ActivityLow, ActivityHigh) + "/" + Band(s.Atr.Value, AtrLow, AtrHigh);
            private static int Band(decimal value, decimal low, decimal high) => value <= low ? 0 : value <= high ? 1 : 2;
        }
        private sealed class Cell
        {
            internal long Cases, Controls, RemainingCases, RemainingControls;
        }
        private sealed class Counter
        {
            internal long Cases, Success, Controls, ControlSuccess, Unknown, Incomplete, BeforeCases, BeforeControls, Unmatched, BalanceExcluded, Purged;
            internal readonly HashSet<DateTime> Dates = new HashSet<DateTime>(), Weeks = new HashSet<DateTime>();
            internal readonly HashSet<DateTime> ControlDates = new HashSet<DateTime>(), ControlWeeks = new HashSet<DateTime>();
            internal readonly Dictionary<DateTime, (long Count, long Success)> Weekly = new Dictionary<DateTime, (long, long)>();
            internal string SuccessId, FailureId, ControlId;
            internal void Add(ExplorerPatternRow row, bool scenario)
            {
                bool success = row.Label.FirstHit == "Target";
                if (scenario)
                {
                    Cases++; if (success) { Success++; SuccessId ??= row.Snapshot.Id; } else { FailureId ??= row.Snapshot.Id; }
                    Dates.Add(row.Snapshot.KnownAt.Date); DateTime week = ExplorerPatternSpec.Week(row.Snapshot.KnownAt); Weeks.Add(week);
                    Weekly.TryGetValue(week, out (long Count, long Success) previous); Weekly[week] = (previous.Count + 1, previous.Success + (success ? 1 : 0));
                }
                else { Controls++; if (success) { ControlSuccess++; } ControlId ??= row.Snapshot.Id; ControlDates.Add(row.Snapshot.KnownAt.Date); ControlWeeks.Add(ExplorerPatternSpec.Week(row.Snapshot.KnownAt)); }
            }
            internal ExplorerPatternMetrics Result()
            {
                decimal? rate = Cases == 0 ? null : (decimal)Success / Cases, control = Controls == 0 ? null : (decimal)ControlSuccess / Controls;
                decimal[] weeks = Weekly.Values.Select(v => (decimal)v.Success / v.Count).ToArray();
                return new ExplorerPatternMetrics(Cases, Success, Controls, ControlSuccess, Dates.Count, Weeks.Count, rate, control, rate - control,
                    weeks.Length == 0 ? null : weeks.Min(), weeks.Length == 0 ? null : weeks.Max(), Unknown, Incomplete, SuccessId, FailureId, ControlId)
                    { ControlDates = ControlDates.Count, ControlWeeks = ControlWeeks.Count, BeforeMatchingCases = BeforeCases, BeforeMatchingControls = BeforeControls,
                        Unmatched = Unmatched, BalanceExcluded = BalanceExcluded, Purged = Purged };
            }
        }
        private sealed class Candidate
        {
            internal ExplorerPatternRule Rule;
            internal string Direction;
            internal int Minutes;
            internal readonly Dictionary<string, Cell> Cells = new Dictionary<string, Cell>();
            internal readonly Counter Counter = new Counter();
        }
        internal static DateTime? Split(IEnumerable<DateTime> dates)
        {
            DateTime[] weeks = dates.Select(ExplorerPatternSpec.Week).Distinct().OrderBy(w => w).ToArray();
            return weeks.Length < 2 ? null : weeks[Math.Clamp((int)(weeks.Length * .7), 1, weeks.Length - 1)];
        }
        internal static string Partition(ExplorerPatternRow row, DateTime? boundary)
        {
            if (!boundary.HasValue) { return "Fit"; }
            if (row.Snapshot.KnownAt < boundary && row.Label.End >= boundary || row.Snapshot.KnownAt >= boundary && row.Snapshot.HistoryStart < boundary) { return "Purged"; }
            return row.Snapshot.KnownAt < boundary ? "Fit" : "Test";
        }
        internal static Strata TrainStrata(IEnumerable<ExplorerPatternSnapshot> snapshots, DateTime? boundary, int seed, CancellationToken token)
        {
            const int capacity = 8192; List<(decimal Activity, decimal Atr)> sample = new List<(decimal, decimal)>(); Random random = new Random(seed); long count = 0;
            foreach (ExplorerPatternSnapshot s in snapshots)
            {
                token.ThrowIfCancellationRequested(); if (!s.Atr.HasValue || boundary.HasValue && s.KnownAt >= boundary) { continue; }
                count++; if (sample.Count < capacity) { sample.Add((s.Activity, s.Atr.Value)); }
                else { long index = random.NextInt64(count); if (index < capacity) { sample[(int)index] = (s.Activity, s.Atr.Value); } }
            }
            decimal[] activity = sample.Select(s => s.Activity).OrderBy(v => v).ToArray(), atr = sample.Select(s => s.Atr).OrderBy(v => v).ToArray();
            return sample.Count == 0 ? new Strata(0, 0, 0, 0) : new Strata(activity[(sample.Count - 1) / 3], activity[2 * (sample.Count - 1) / 3], atr[(sample.Count - 1) / 3], atr[2 * (sample.Count - 1) / 3]);
        }
        internal static Dictionary<string, ExplorerPatternMetrics> Measure(Func<IEnumerable<ExplorerPatternRow>> rows,
            IReadOnlyList<(ExplorerPatternRule Rule, string Direction, int Minutes)> rules, string partition, DateTime? boundary, Strata strata, CancellationToken token,
            Action<ExplorerPatternMembership> membership = null)
        {
            Candidate[] candidates = rules.Select(r => new Candidate { Rule = r.Rule, Direction = r.Direction, Minutes = r.Minutes }).ToArray();
            foreach (ExplorerPatternRow row in rows())
            {
                token.ThrowIfCancellationRequested(); string actualPartition = Partition(row, boundary);
                if (actualPartition != partition && actualPartition != "Purged") { continue; }
                foreach (Candidate c in candidates)
                {
                    if (c.Minutes != row.Label.Minutes || c.Direction != row.Label.Direction) { continue; }
                    if (actualPartition == "Purged")
                    {
                        if ((row.Snapshot.KnownAt < boundary ? "Fit" : "Test") == partition)
                        { c.Counter.Purged++; membership?.Invoke(new ExplorerPatternMembership(c.Rule.Id, row.Snapshot.Id, partition, c.Direction, c.Minutes, "Unknown", "Purged")); }
                        continue;
                    }
                    bool? matches = ExplorerPatternGrammar.Matches(c.Rule, row.Snapshot);
                    if (!matches.HasValue || row.Label.Status == "NoAtr") { c.Counter.Unknown++; continue; }
                    if (row.Label.Sampling != "Selected") { continue; }
                    if (row.Label.Status != "Complete") { c.Counter.Incomplete++; continue; }
                    string key = strata.Key(row.Snapshot);
                    if (!c.Cells.TryGetValue(key, out Cell cell)) { cell = new Cell(); c.Cells.Add(key, cell); }
                    if (matches.Value) { cell.Cases++; } else { cell.Controls++; }
                }
            }
            foreach (Candidate c in candidates)
            {
                foreach (Cell cell in c.Cells.Values)
                {
                    cell.RemainingCases = cell.RemainingControls = Math.Min(cell.Cases, cell.Controls);
                    c.Counter.BeforeCases += cell.Cases; c.Counter.BeforeControls += cell.Controls;
                    if (cell.Cases == 0 || cell.Controls == 0) { c.Counter.Unmatched += cell.Cases + cell.Controls; }
                    else { c.Counter.BalanceExcluded += Math.Abs(cell.Cases - cell.Controls); }
                }
            }
            // This second pass consumes each stratum's frozen first-N quotas; no label value participates in selection.
            foreach (ExplorerPatternRow row in rows())
            {
                token.ThrowIfCancellationRequested(); if (Partition(row, boundary) != partition || row.Label.Status != "Complete" || row.Label.Sampling != "Selected") { continue; }
                foreach (Candidate c in candidates)
                {
                    if (c.Minutes != row.Label.Minutes || c.Direction != row.Label.Direction) { continue; }
                    bool? matches = ExplorerPatternGrammar.Matches(c.Rule, row.Snapshot);
                    if (!matches.HasValue || !c.Cells.TryGetValue(strata.Key(row.Snapshot), out Cell cell)) { continue; }
                    string status;
                    if (matches.Value && cell.RemainingCases > 0) { cell.RemainingCases--; c.Counter.Add(row, true); status = "Matched"; }
                    else if (!matches.Value && cell.RemainingControls > 0) { cell.RemainingControls--; c.Counter.Add(row, false); status = "Matched"; }
                    else { status = cell.Cases == 0 || cell.Controls == 0 ? "Unmatched" : "BalanceExcluded"; }
                    membership?.Invoke(new ExplorerPatternMembership(c.Rule.Id, row.Snapshot.Id, partition, c.Direction, c.Minutes, matches.Value ? "Scenario" : "Control", status));
                }
            }
            return candidates.ToDictionary(c => Key(c.Rule, c.Direction, c.Minutes), c => c.Counter.Result());
        }
        internal static string Key(ExplorerPatternRule rule, string direction, int minutes) => rule.Id + "/" + direction + "/" + minutes;
        internal static bool Supported(ExplorerPatternMetrics metrics, ExplorerPatternSpec plan) => metrics.Dates >= plan.MinimumDates && metrics.Weeks >= plan.MinimumWeeks && metrics.ControlDates >= plan.MinimumDates && metrics.ControlWeeks >= plan.MinimumWeeks;
        internal static string Status(ExplorerPatternMetrics test, ExplorerPatternSpec plan) => test.Dates < plan.MinimumTestDates || test.Weeks < 1 || test.ControlDates < plan.MinimumTestDates || test.ControlWeeks < 1
            ? "Недостаточно данных Test" : test.Difference > 0 ? "Эффект повторился в Test; исследовательский результат, не торговый сигнал" : "Не подтвердился в Test";
    }
}
