/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace OsEngine.OsData.OrderFlow.Explorer
{
    /// <summary>Offline discovery transaction: immutable catalog, causal rows, Fit freeze, Test once, then atomic publication.</summary>
    internal static class ExplorerPatternRunner
    {
        internal static ExplorerPatternRun Run(ExplorerRunSpec request, ExplorerPatternSpec plan, CancellationToken token, Action<ExplorerProgress> progress = null)
        {
            request.Validate(); plan.Validate(); Stopwatch clock = Stopwatch.StartNew();
            ExplorerRun run = ExplorerEpisodeRunner.Run(ExplorerRunner.Catalog(request, token, progress), token, progress);
            string hash = plan.Hash(run.Spec), destination = Path.Combine(run.Spec.OutputRootPath, "cloud-pattern-search-" + hash);
            if (Directory.Exists(destination)) { return new ExplorerPatternRun(run, plan, destination, ExplorerStorage.Verify(destination, ExplorerPatternSpec.Version, hash, token)); }
            string stage = ExplorerStorage.Stage(run.Spec.OutputRootPath, "cloud-pattern-search");
            try
            {
                Dictionary<string, long> reasons = new Dictionary<string, long>(); long full = 0, anchors = 0, selected = 0;
                using (ExplorerStorage.RowWriter<ExplorerPatternSnapshot> snapshots = new ExplorerStorage.RowWriter<ExplorerPatternSnapshot>(stage, "snapshots"))
                using (ExplorerStorage.RowWriter<ExplorerPatternLabel> labels = new ExplorerStorage.RowWriter<ExplorerPatternLabel>(stage, "labels"))
                using (ExplorerStorage.RowWriter<ExplorerPatternRow> evaluation = new ExplorerStorage.RowWriter<ExplorerPatternRow>(stage, "evaluation"))
                {
                    OrderFlowTickInput metadata = new OrderFlowTickInput();
                    using OrderFlowTickReader reader = new OrderFlowTickReader(run.Spec.InputPath, metadata, token);
                    if (metadata.Sha256 != run.Spec.InputSha256) { throw new InvalidDataException("Pattern source SHA mismatch."); }
                    ExplorerPatternKernel kernel = new ExplorerPatternKernel(run.Spec, plan, snapshot => { snapshots.Add(snapshot); anchors++; }, row =>
                    {
                        labels.Add(row.Label); if (row.Label.Sampling != "Overlap") { evaluation.Add(row); }
                        reasons.TryGetValue(row.Label.Status, out long count); reasons[row.Label.Status] = count + 1;
                        if (row.Label.Status == "Complete") { full++; }
                    });
                    while (reader.TryRead(out OrderFlowDeal tick))
                    {
                        if (!run.Spec.Includes(tick.Time)) { continue; }
                        kernel.Tick(tick, token); selected++;
                        if (selected % 8192 == 0) { ExplorerStorage.CheckMemory(run.Spec); progress?.Invoke(new ExplorerProgress("Поиск: причинные якоря и исходы", selected, anchors, tick.Time.Date, GC.GetTotalMemory(false), clock.Elapsed.TotalSeconds)); }
                    }
                    kernel.Complete();
                }
                DateTime? boundary = ExplorerPatternEvaluation.Split(run.Catalog.Dates);
                ExplorerPatternEvaluation.Strata strata = ExplorerPatternEvaluation.TrainStrata(ExplorerStorage.ReadRows<ExplorerPatternSnapshot>(stage, "snapshots"), boundary, plan.Seed, token);
                List<ExplorerPatternRule> considered = new List<ExplorerPatternRule>(); long grid = 0;
                using (ExplorerStorage.RowWriter<object> grammar = new ExplorerStorage.RowWriter<object>(stage, "grammar"))
                {
                    foreach (ExplorerPatternRule rule in ExplorerPatternGrammar.Rules(plan))
                    {
                        token.ThrowIfCancellationRequested(); grid++;
                        bool include = considered.Count < plan.CandidateBudget;
                        grammar.Add(new { Rule = rule, Status = include ? "Considered" : "BudgetExcluded" });
                        if (include) { considered.Add(rule); }
                    }
                }
                List<(ExplorerPatternRule Rule, string Direction, int Minutes)> hypotheses = new List<(ExplorerPatternRule, string, int)>();
                foreach (ExplorerPatternRule rule in considered)
                { foreach (string direction in plan.Direction == "Both" ? new[] { "Long", "Short" } : new[] { plan.Direction }) { foreach (int minutes in plan.HorizonsMinutes.Append(0)) { hypotheses.Add((rule, direction, minutes)); } } }
                void Report(string phase) => progress?.Invoke(new ExplorerProgress(phase, selected, anchors, run.Catalog.Dates.LastOrDefault(), GC.GetTotalMemory(false), clock.Elapsed.TotalSeconds));
                IEnumerable<ExplorerPatternRow> Rows()
                {
                    long count = 0;
                    foreach (ExplorerPatternRow row in ExplorerStorage.ReadRows<ExplorerPatternRow>(stage, "evaluation"))
                    { if (++count % 8192 == 0) { ExplorerStorage.CheckMemory(run.Spec); } yield return row; }
                }
                Report("Поиск: Fit, подбор и сопоставимый контроль");
                Dictionary<string, ExplorerPatternMetrics> fit;
                using (ExplorerStorage.RowWriter<ExplorerPatternMembership> membership = new ExplorerStorage.RowWriter<ExplorerPatternMembership>(stage, "fit-membership"))
                { fit = ExplorerPatternEvaluation.Measure(Rows, hypotheses, "Fit", boundary, strata, token, membership.Add); }
                List<(ExplorerPatternRule Rule, string Direction, int Minutes)> supported = hypotheses.Where(h => ExplorerPatternEvaluation.Supported(fit[ExplorerPatternEvaluation.Key(h.Rule, h.Direction, h.Minutes)], plan)).ToList();
                List<(ExplorerPatternRule Rule, string Direction, int Minutes)> frozen = supported.Where(h => fit[ExplorerPatternEvaluation.Key(h.Rule, h.Direction, h.Minutes)].Difference > 0)
                    .OrderByDescending(h => fit[ExplorerPatternEvaluation.Key(h.Rule, h.Direction, h.Minutes)].Difference)
                    .ThenBy(h => h.Rule.Conditions.Length).ThenBy(h => ExplorerPatternEvaluation.Key(h.Rule, h.Direction, h.Minutes), StringComparer.Ordinal).Take(plan.MaximumCards).ToList();
                HashSet<string> frozenKeys = frozen.Select(h => ExplorerPatternEvaluation.Key(h.Rule, h.Direction, h.Minutes)).ToHashSet();
                Write(stage, "frozen-fit.json", new { Version = ExplorerPatternSpec.Grammar, Boundary = boundary, Strata = strata, Seed = plan.Seed,
                    Sampling = plan.Overlap, MatchedControl = "same-source-hour;fit-tertile-activity;fit-tertile-ATR;first-N-per-stratum-1", Rules = frozen.Select(r => new { r.Rule, r.Direction, r.Minutes }).ToArray() });
                using (ExplorerStorage.RowWriter<object> fits = new ExplorerStorage.RowWriter<object>(stage, "fit-rules"))
                {
                    foreach ((ExplorerPatternRule rule, string direction, int minutes) in hypotheses)
                    {
                        string key = ExplorerPatternEvaluation.Key(rule, direction, minutes); ExplorerPatternMetrics metrics = fit[key];
                        fits.Add(new { Rule = rule, Direction = direction, Minutes = minutes, Metrics = metrics,
                            Status = frozenKeys.Contains(key) ? "FrozenForTest" : !ExplorerPatternEvaluation.Supported(metrics, plan) ? "InsufficientDistinctSupport" : metrics.Difference <= 0 ? "NoFitEffect" : "RankBudget" });
                    }
                }
                // No Test outcome was consulted before frozen-fit.json and the complete Fit ranking were written.
                Report("Поиск: единственная проверка замороженных правил на Test");
                Dictionary<string, ExplorerPatternMetrics> test;
                using (ExplorerStorage.RowWriter<ExplorerPatternMembership> membership = new ExplorerStorage.RowWriter<ExplorerPatternMembership>(stage, "test-membership"))
                { test = ExplorerPatternEvaluation.Measure(Rows, frozen, "Test", boundary, strata, token, membership.Add); }
                using (ExplorerStorage.RowWriter<ExplorerPatternCard> cards = new ExplorerStorage.RowWriter<ExplorerPatternCard>(stage, "cards"))
                using (ExplorerStorage.RowWriter<object> examples = new ExplorerStorage.RowWriter<object>(stage, "examples"))
                {
                    int rank = 0;
                    foreach ((ExplorerPatternRule rule, string direction, int minutes) in frozen)
                    {
                        string key = ExplorerPatternEvaluation.Key(rule, direction, minutes); ExplorerPatternMetrics tested = test[key];
                        cards.Add(new ExplorerPatternCard(key, rule, direction, minutes, ++rank, fit[key], tested, ExplorerPatternEvaluation.Status(tested, plan)));
                        foreach ((string kind, string id) in new[] { ("Success", tested.SuccessId ?? fit[key].SuccessId), ("Failure", tested.FailureId ?? fit[key].FailureId), ("Control", tested.ControlId ?? fit[key].ControlId) })
                        { examples.Add(new { CardId = key, Kind = kind, SnapshotId = id, Status = id == null ? "NoExampleInMatchedSample" : "Available" }); }
                    }
                }
                ExplorerPatternQuality quality = new ExplorerPatternQuality(run.Catalog.Dates.Length, run.Catalog.Dates.Select(ExplorerPatternSpec.Week).Distinct().Count(),
                    run.Catalog.Clouds, run.EpisodePath == null ? 0 : new FileInfo(Path.Combine(run.EpisodePath, "episodes.idx")).Length / 8,
                    anchors, full, grid, considered.Count, grid - considered.Count, supported.Count, frozen.Count, boundary, reasons,
                    "Fit/Test — поисковая проверка, не final OOS и не торговая рекомендация. Повторная настройка после просмотра Test требует нового неиспользованного периода. Время и сутки — только по исходной ленте.");
                Write(stage, "quality.json", quality); Write(stage, "pattern-spec.json", plan);
                Write(stage, "run-spec.json", run.Spec with { InputPath = "", OutputRootPath = "" });
                ExplorerManifest manifest = new ExplorerManifest(ExplorerPatternSpec.Version, hash, run.Spec.InputSha256,
                    JsonSerializer.SerializeToElement(new { run.Spec.CatalogSpecHash, run.Spec.EpisodeSpecHash, Grammar = ExplorerPatternSpec.Grammar, Plan = plan }),
                    run.Catalog.Rows, selected, anchors, run.Catalog.SecondsOnly, run.Catalog.Dates, null);
                manifest = ExplorerStorage.Publish(stage, destination, manifest, token); stage = null;
                Report("Поиск завершён; результат опубликован"); return new ExplorerPatternRun(run, plan, destination, manifest);
            }
            finally { if (stage != null && Directory.Exists(stage)) { Directory.Delete(stage, true); } }
        }
        private static void Write<T>(string directory, string name, T value) => File.WriteAllText(Path.Combine(directory, name), JsonSerializer.Serialize(value, ExplorerStorage.Json));

        internal static ExplorerPatternRun Open(string directory, string input, CancellationToken token)
        {
            ExplorerManifest identity = JsonSerializer.Deserialize<ExplorerManifest>(File.ReadAllText(Path.Combine(directory, "manifest.json")), ExplorerStorage.Json);
            ExplorerManifest manifest = ExplorerStorage.Verify(directory, ExplorerPatternSpec.Version, identity.Hash, token);
            ExplorerPatternSpec plan = JsonSerializer.Deserialize<ExplorerPatternSpec>(File.ReadAllText(Path.Combine(directory, "pattern-spec.json")), ExplorerStorage.Json);
            ExplorerRunSpec spec = JsonSerializer.Deserialize<ExplorerRunSpec>(File.ReadAllText(Path.Combine(directory, "run-spec.json")), ExplorerStorage.Json)
                with { InputPath = string.IsNullOrWhiteSpace(input) ? "Выберите исходник для реплея" : input, OutputRootPath = Path.GetDirectoryName(directory) };
            spec.Validate(); plan.Validate();
            if (plan.Hash(spec) != manifest.Hash || spec.InputSha256 != manifest.InputSha256) { throw new InvalidDataException("Pattern manifest identity mismatch."); }
            string catalogPath = Path.Combine(spec.OutputRootPath, "cloud-catalog-" + spec.CatalogSpecHash);
            ExplorerManifest catalog = ExplorerStorage.Verify(catalogPath, ExplorerRunSpec.CatalogVersion, spec.CatalogSpecHash, token);
            string episodePath = spec.EpisodeSpecHash == null ? null : Path.Combine(spec.OutputRootPath, "cloud-episodes-" + spec.EpisodeSpecHash);
            if (episodePath != null) { ExplorerStorage.Verify(episodePath, ExplorerRunSpec.EpisodeVersion, spec.EpisodeSpecHash, token); }
            return new ExplorerPatternRun(new ExplorerRun(spec, catalogPath, episodePath, null, catalog), plan, directory, manifest);
        }
    }
}
