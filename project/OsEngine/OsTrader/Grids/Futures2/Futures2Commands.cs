/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using OsEngine.Entity;

namespace OsEngine.OsTrader.Grids.Futures2
{
    /// <summary>Pure plan transformations for explicit range, rollover and operator controls.</summary>
    public static class Futures2Commands
    {
        /// <summary>Translates a complete grid and its enabled boundaries, validating before publication.</summary>
        /// <remarks>Preserves per-level markup and entry/exit controls by ordinal without mutating the
        /// old plan or held lots. Caller stages the returned plan through the native cancellation barrier.
        /// Throws before any publication for invalid geometry or ambiguous count/override mapping. THG-RANGE-014.</remarks>
        public static Futures2Plan Shift(Futures2Plan plan, decimal displacement)
        {
            Futures2PlanInput input = Copy(plan.Input);
            checked
            {
                input.Low += displacement; input.High += displacement;
                if (input.LowerStopEnabled) input.LowerStop += displacement;
                if (input.UpperStopEnabled) input.UpperStop += displacement;
            }
            Futures2Plan replacement = Futures2Plan.Build(input);
            PreserveRangeLevelSettings(plan, replacement);
            replacement.EndpointIdentity = plan.EndpointIdentity; replacement.PaperExecution = plan.PaperExecution;
            return replacement;
        }
        /// <summary>Expands a grid to include the current quote, preserving positive distances to stop boundaries.</summary>
        /// <remarks>Preserves markup/entry/exit controls when count is unchanged. A count change with
        /// customized controls is rejected before staging; an untouched grid retains its count-mode sizing.
        /// Old plan/lots remain unchanged. This is target geometry, not literal source Widen. THG-RANGE-014.</remarks>
        public static Futures2Plan Widen(Futures2Plan plan, decimal bid, decimal ask)
        {
            Futures2PlanInput input = Copy(plan.Input);
            decimal low = SignedPriceMath.Quantize(Math.Min(input.Low, bid), input.Tick, false);
            decimal high = SignedPriceMath.Quantize(Math.Max(input.High, ask), input.Tick, true);
            if (input.LowerStopEnabled) input.LowerStop = checked(low - (input.Low - input.LowerStop));
            if (input.UpperStopEnabled) input.UpperStop = checked(high + (input.UpperStop - input.High));
            input.Low = low; input.High = high;
            Futures2Plan replacement = Futures2Plan.Build(input);
            PreserveRangeLevelSettings(plan, replacement);
            replacement.EndpointIdentity = plan.EndpointIdentity; replacement.PaperExecution = plan.PaperExecution;
            return replacement;
        }
        private static void PreserveRangeLevelSettings(Futures2Plan plan, Futures2Plan replacement)
        {
            if (plan.Levels.Count != replacement.Levels.Count)
            {
                if (plan.Levels.Any(level => level.Markup != plan.Input.Markup || !level.EntryEnabled || !level.ExitEnabled))
                    throw new InvalidOperationException("Range change alters level count with custom controls; explicitly apply a new grid and review level settings.");
                return;
            }
            Dictionary<int, Futures2Level> previous = plan.Levels.ToDictionary(level => level.Id);
            foreach (Futures2Level level in replacement.Levels)
            {
                Futures2Level old = previous[level.Id];
                level.Markup = old.Markup; level.EntryEnabled = old.EntryEnabled; level.ExitEnabled = old.ExitEnabled;
            }
        }
        /// <summary>Resolves an explicit CSV set, or the legacy scalar/all fallback, before any command mutation.</summary>
        public static List<int> SelectLevels(Futures2Plan plan, string csv, int fallback)
        {
            List<int> levels = string.IsNullOrWhiteSpace(csv)
                ? (fallback == -1 ? plan.Levels.Select(l => l.Id).ToList() : new List<int> { fallback })
                : csv.Split(',').Select(v => int.Parse(v.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture)).ToList();
            return CheckLevels(plan, levels);
        }

        /// <summary>Validates the complete nonempty distinct selection and returns a detached copy.</summary>
        public static List<int> CheckLevels(Futures2Plan plan, IEnumerable<int> selection)
        {
            List<int> levels = selection?.ToList();
            if (plan == null || levels == null || levels.Count == 0 || levels.Distinct().Count() != levels.Count
                || levels.Any(id => !plan.Levels.Any(l => l.Id == id))) throw new ArgumentException("Invalid or duplicate selected levels.");
            return levels;
        }

        /// <summary>Queues one plan-bound entry attempt per selected level; ordinary gates and funding still apply.</summary>
        /// <remarks>Caller holds adapter serialization and persists before Pump. Completed items are removed at
        /// intent recording, never retried by this batch after partial fill/cancel. THG-BATCH-011.</remarks>
        public static void EnterLevels(Futures2Checkpoint data, string planId, IEnumerable<int> selection)
        {
            List<int> levels = CheckLevels(data.Plans[planId], selection);
            if (planId != data.ActivePlan || data.PendingPlan != null || data.Emergency || data.EntryBatch != null || data.ManualEntry.HasValue
                || data.Rollover != null && data.Rollover.State != "Applied" && data.Rollover.State != "Canceled")
                throw new InvalidOperationException("An active stable plan and no pending entry command are required.");
            data.EntryBatch = new Futures2EntryBatch { Plan = planId, Levels = levels }; data.Schema = Math.Max(data.Schema, 4);
        }

        /// <summary>Discards only unattempted manual entries; already recorded native intents remain owned.</summary>
        public static void CancelEntryBatch(Futures2Checkpoint data) { data.EntryBatch = null; data.ManualEntry = null; }

        /// <summary>Snapshots selected current lot identities for ordinary reduction, preserving partial-close reservations.</summary>
        public static void ExitLevels(Futures2Checkpoint data, string planId, IEnumerable<int> selection)
        {
            List<int> levels = CheckLevels(data.Plans[planId], selection);
            if (data.ManualExit.HasValue || data.ManualExitLots.Count > 0) throw new InvalidOperationException("Finish the pending manual exit first.");
            data.ManualExitPlan = planId;
            data.ManualExitLots = data.Book.Lots.Where(l => l.PlanId == planId && l.Quantity > 0 && levels.Contains(l.LevelId)).Select(l => l.Id).ToList();
        }

        /// <summary>Stages selected nullable fields behind the existing cancel/reconcile barrier; unselected fields stay unchanged.</summary>
        public static void EditLevels(Futures2Checkpoint data, string planId, IEnumerable<int> selection, decimal? markup, bool? entry, bool? exit)
        {
            List<int> levels = CheckLevels(data.Plans[planId], selection);
            if (data.LevelEdit != null || markup.HasValue && markup <= 0 || !markup.HasValue && !entry.HasValue && !exit.HasValue)
                throw new InvalidOperationException("Invalid or already pending selected edit.");
            data.LevelEdit = new Futures2LevelEdit { Plan = planId, Levels = levels, Markup = markup, Entry = entry, Exit = exit };
            data.Schema = Math.Max(data.Schema, 4);
        }

        /// <summary>Snapshots whole fillable native intents touching selected allocations, including every allocation of grouped orders.</summary>
        /// <remarks>Does not disable future ordinary grid entries. Unknown outcomes remain reserved. THG-BATCH-011.</remarks>
        public static void CancelLevels(Futures2Checkpoint data, string planId, IEnumerable<int> selection)
        {
            List<int> levels = CheckLevels(data.Plans[planId], selection);
            foreach (Futures2Intent intent in data.Book.Intents.Where(i => i.PlanId == planId && i.CanFill && i.Allocations.Any(a => levels.Contains(a.LevelId))))
                data.SelectedCancels.Add(intent.Id);
            data.Schema = Math.Max(data.Schema, 4);
        }

        /// <summary>Rejects corrupted or stale durable command references without changing the checkpoint.</summary>
        internal static void ValidateCommands(Futures2Checkpoint data)
        {
            if (data.EntryBatch != null)
            {
                if (data.ManualEntry.HasValue || data.EntryBatch.Plan != data.ActivePlan || !data.Plans.ContainsKey(data.EntryBatch.Plan))
                    throw new InvalidOperationException("Manual entry batch plan is not active.");
                CheckLevels(data.Plans[data.EntryBatch.Plan], data.EntryBatch.Levels);
            }
            if (data.LevelEdit?.Levels != null) CheckLevels(data.Plans[data.LevelEdit.Plan], data.LevelEdit.Levels);
            if (data.SelectedCancels.Any(id => !data.Book.Intents.Any(i => i.Id == id)))
                throw new InvalidOperationException("Selected cancellation references an unknown intent.");
        }

        /// <summary>Returns a detached serializable settings object.</summary>
        public static T Copy<T>(T input) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(input));
    }

    /// <summary>Durable plan-bound selection remaining to be attempted; owned by adapter serialization.</summary>
    public sealed class Futures2EntryBatch
    {
        /// <summary>Accepted plan identity; reconfiguration to another plan cancels this request.</summary>
        public string Plan { get; set; } = "";
        /// <summary>Unattempted distinct levels; traversal follows accepted plan order, not CSV order.</summary>
        public List<int> Levels { get; set; } = new List<int>();
    }

    /// <summary>One pending per-level edit, applied after cancel-confirm, never a synthetic fill.</summary>
    public sealed class Futures2LevelEdit
    {
        /// <summary>Owning plan identity, including plans retaining inventory.</summary>
        public string Plan { get; set; } = "";
        /// <summary>Selected level, or -1 for every level in that plan.</summary>
        public int Level { get; set; } = -1;
        /// <summary>Explicit selection; null retains the legacy scalar/all Level meaning. Requires schema4.</summary>
        public List<int> Levels { get; set; }
        /// <summary>New markup when supplied.</summary>
        public decimal? Markup { get; set; }
        /// <summary>New ordinary entry participation when supplied.</summary>
        public bool? Entry { get; set; }
        /// <summary>New ordinary exit participation when supplied.</summary>
        public bool? Exit { get; set; }
    }
}
