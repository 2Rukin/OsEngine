using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using OsEngine.Entity;

namespace OsEngine.OsTrader.AdaptivePositionManager
{
    /// <summary>Predeclared chronological native phase; synthetic qualification is not an untouched historical holdout.</summary>
    public sealed record ApmStudyPhase(string Name, DateTime Start, DateTime End, bool OutOfSample);

    /// <summary>One planned combination, retained even if the native executor never starts or completes it.</summary>
    public sealed record ApmStudyTrial(string Phase, string PolicyHash, ApmPolicy Policy);

    /// <summary>
    /// APM research accounting around the existing Optimizer. This class plans and records experiments;
    /// only the native executor enumerates and runs bots. It never starts a server or changes risk locks.
    /// </summary>
    public sealed class ApmOptimizerStudy
    {
        private static readonly string[] Searchable = { "Add scale price", "Reduce scale price", "Inventory gamma", "Rearm volatility factor" };
        private readonly ApmStudyPhase[] _phases;
        private readonly ApmStudyTrial[] _trials;
        private readonly string _scheduleHash;
        private readonly string _datasetHash;

        /// <summary>Detached planned trials. Policies are immutable; no native mutable parameter is retained.</summary>
        public ReadOnlyCollection<ApmStudyTrial> Trials => Array.AsReadOnly(_trials);

        /// <summary>
        /// Freeze selected current values before native BotCount/ReloadAllParam can mutate them.
        /// Search permits at most three declared numeric behavior axes, five values each, 125 combinations.
        /// All candidate policies and whole-campaign phase boundaries are validated before execution.
        /// </summary>
        public ApmOptimizerStudy(ApmSchedule schedule, IReadOnlyList<ApmStudyPhase> phases,
            List<IIStrategyParameter> parameters, List<bool> flags)
        {
            if (schedule == null || phases == null || phases.Count == 0 || parameters == null
                || flags == null || flags.Count != parameters.Count) throw new ArgumentException("Missing study inputs.");
            _phases = phases.ToArray(); _scheduleHash = schedule.Hash; _datasetHash = schedule.DatasetHash;
            ApmStudyPhase previousTraining = null;
            ApmStudyPhase previousPhase = null;
            DateTime previousTestEnd = DateTime.MinValue;
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            HashSet<(DateTime Start, DateTime End)> intervals = new HashSet<(DateTime, DateTime)>();
            foreach (ApmStudyPhase phase in _phases)
            {
                if (string.IsNullOrWhiteSpace(phase.Name) || !names.Add(phase.Name) || !intervals.Add((phase.Start, phase.End)))
                    throw new ArgumentException("Study phases need unique names and native interval identities.");
                if (phase.OutOfSample)
                {
                    if (previousTraining == null || previousPhase.OutOfSample || phase.Start <= previousTraining.End
                        || phase.Start <= previousTestEnd) throw new ArgumentException("OOS must follow its past training phase without overlapping previous OOS.");
                    previousTestEnd = phase.End;
                }
                else
                {
                    if (previousTraining != null && (phase.Start < previousTraining.Start || phase.End <= previousTraining.End
                        || phase.End < previousTestEnd)) throw new ArgumentException("Rolling training windows must advance chronologically.");
                    previousTraining = phase;
                }
                schedule.SelectPhase(phase.Start, phase.End, 30);
                previousPhase = phase;
            }
            List<int> axes = new List<int>();
            List<decimal[]> values = new List<decimal[]>();
            int combinations = 1;
            for (int i = 0; i < parameters.Count; i++)
            {
                if (flags[i])
                {
                    if (!Searchable.Contains(parameters[i].Name) || parameters[i] is not StrategyParameterDecimal axis
                        || axis.StepType != StrategyParameterStepType.Absolute || axis.ValueDecimalStep <= 0
                        || axis.ValueDecimalStart > axis.ValueDecimalStop)
                        throw new ArgumentException("Search is limited to explicitly supported absolute decimal behavior axes.");
                    List<decimal> range = new List<decimal>();
                    for (decimal value = axis.ValueDecimalStart; value <= axis.ValueDecimalStop; value += axis.ValueDecimalStep)
                    {
                        range.Add(value);
                        if (range.Count > 5) throw new ArgumentException("More than five values on one axis.");
                    }
                    if (range[range.Count - 1] != axis.ValueDecimalStop)
                        throw new ArgumentException("Search stop must be on the declared step grid.");
                    axes.Add(i); values.Add(range.ToArray()); combinations *= range.Count;
                }
                else if (parameters[i] is StrategyParameterDecimal fixedValue)
                {
                    // Native UI's fixed column is Defolt. Keep that contract local to this explicit headless study.
                    parameters[i] = new StrategyParameterDecimal(fixedValue.Name, fixedValue.ValueDecimal,
                        fixedValue.ValueDecimalStart, fixedValue.ValueDecimalStop, fixedValue.ValueDecimalStep);
                }
            }
            if (axes.Count > 3 || combinations > 125) throw new ArgumentException("Initial search exceeds 3 axes / 125 trials.");
            List<ApmStudyTrial> planned = new List<ApmStudyTrial>();
            ApmPolicy selected = Policy(parameters);
            for (int combination = 0; combination < combinations; combination++)
            {
                int remainder = combination;
                ApmPolicy policy = selected;
                for (int axis = 0; axis < axes.Count; axis++)
                {
                    decimal value = values[axis][remainder % values[axis].Length];
                    remainder /= values[axis].Length;
                    policy = Set(policy, parameters[axes[axis]].Name, value);
                }
                foreach (ApmCampaignSpec campaign in schedule.Campaigns) policy.Validate(campaign);
                foreach (ApmStudyPhase phase in _phases)
                    planned.Add(new ApmStudyTrial(phase.Name, ApmTickReader.HashJson(policy), policy));
            }
            _trials = planned.ToArray();
            if (axes.Count == 0)
            {
                // Native executor requires one checked numeric axis even for a single fixed pass.
                int index = parameters.FindIndex(p => p.Name == "Add scale price");
                decimal value = selected.AddScale;
                parameters[index] = new StrategyParameterDecimal("Add scale price", value, value, value, 1);
                flags[index] = true;
            }
        }

        /// <summary>Write immutable pre-run provenance into a new file; owner datasets are identified by hash only.</summary>
        public void SavePlan(string path, string dataQualification, string nativeFilters)
        {
            using FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(stream, new { SchemaVersion = "APM-Study-v1", Qualification = "ResearchOnly",
                DataQualification = dataQualification, NativeFilters = nativeFilters,
                DatasetHash = _datasetHash, ScheduleHash = _scheduleHash, Phases = _phases, Trials = _trials,
                Execution = "Native OptimizerExecutor only; OOS executes preceding IS survivors" },
                new JsonSerializerOptions { WriteIndented = true });
            stream.Flush(true);
        }

        /// <summary>
        /// Account for every plan row, including absent/partial runs, independently of native result filters.
        /// Missing evidence stays NotObserved, never a zero-PnL success or an inferred rejection.
        /// </summary>
        public void WriteResults(string artifactsRoot, string outputFile)
        {
            Dictionary<string, List<JsonElement>> observed = new Dictionary<string, List<JsonElement>>(StringComparer.Ordinal);
            if (Directory.Exists(artifactsRoot))
                foreach (string file in Directory.EnumerateFiles(artifactsRoot, "run-summary.json", SearchOption.AllDirectories))
                {
                    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(file));
                    JsonElement row = document.RootElement;
                    if (row.GetProperty("SourceScheduleHash").GetString() != _scheduleHash
                        || row.GetProperty("DatasetHash").GetString() != _datasetHash) throw new InvalidDataException("Mixed study provenance.");
                    ApmStudyPhase phase = _phases.Single(p => p.Start == row.GetProperty("PhaseStart").GetDateTime()
                        && p.End == row.GetProperty("PhaseEnd").GetDateTime());
                    ApmPolicy policy = row.GetProperty("Parameters").Deserialize<ApmPolicy>();
                    string key = phase.Name + ":" + ApmTickReader.HashJson(policy);
                    if (!observed.TryGetValue(key, out List<JsonElement> runs)) observed.Add(key, runs = new List<JsonElement>());
                    runs.Add(row.Clone());
                }
            List<object> results = new List<object>();
            HashSet<string> expected = new HashSet<string>(StringComparer.Ordinal);
            foreach (ApmStudyTrial trial in _trials)
            {
                string key = trial.Phase + ":" + trial.PolicyHash;
                expected.Add(key);
                observed.TryGetValue(key, out List<JsonElement> runs);
                string status = runs == null ? "NotObserved" : runs.Count != 1 ? "DuplicatePass"
                    : runs[0].GetProperty("CompletionStatus").GetString();
                results.Add(new { trial.Phase, trial.PolicyHash, trial.Policy, Status = status, Runs = runs });
            }
            if (observed.Keys.Any(k => !expected.Contains(k))) throw new InvalidDataException("Native pass outside frozen plan.");
            using FileStream output = new FileStream(outputFile, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(output, new { SchemaVersion = "APM-AllTrials-v1", Results = results,
                EconomicVerdict = "ResearchOnly; no historical holdout, confidence interval or economic GO inferred" },
                new JsonSerializerOptions { WriteIndented = true });
            output.Flush(true);
        }

        /// <summary>Read selected behavior values; missing parameters fail explicitly, without fallback defaults.</summary>
        public static ApmPolicy Policy(IReadOnlyList<IIStrategyParameter> parameters)
        {
            decimal Number(string name) => ((StrategyParameterDecimal)parameters.Single(p => p.Name == name)).ValueDecimal;
            bool Flag(string name) => ((StrategyParameterBool)parameters.Single(p => p.Name == name)).ValueBool;
            ApmAcSettings research = null;
            if (parameters.SingleOrDefault(p => p.Name == "Research AC enabled") is StrategyParameterBool enabled && enabled.ValueBool)
                research = ApmAcSettings.Load(((StrategyParameterString)parameters.Single(p => p.Name == "Research AC settings file")).ValueString);
            return new ApmPolicy { ConstantInventory = Flag("B0 constant inventory"), FixedScales = !Flag("Volatility scales"),
                AddScale = Number("Add scale price"), ReduceScale = Number("Reduce scale price"),
                InventoryPenalty = Number("Inventory gamma"), FastEnabled = Flag("FAST enabled"),
                RearmVolatilityFactor = Number("Rearm volatility factor"), ResearchExecution = research };
        }

        private static ApmPolicy Set(ApmPolicy policy, string name, decimal value) => name switch
        {
            "Add scale price" => policy with { AddScale = value },
            "Reduce scale price" => policy with { ReduceScale = value },
            "Inventory gamma" => policy with { InventoryPenalty = value },
            "Rearm volatility factor" => policy with { RearmVolatilityFactor = value },
            _ => throw new ArgumentException("Unknown behavior axis.")
        };
    }
}
