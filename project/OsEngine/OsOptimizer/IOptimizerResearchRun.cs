using System.Collections.Generic;
using OsEngine.Entity;

namespace OsEngine.OsOptimizer
{
    /// <summary>
    /// Optional research provenance hook for a bot that needs whole-pass evidence beyond native position statistics.
    /// The native executor still owns enumeration, filters, servers and scheduling. Legacy bots are unaffected.
    /// </summary>
    public interface IOptimizerResearchRun
    {
        /// <summary>Called on the template before native parameter counting. Freeze the plan or throw before any pass starts.</summary>
        void PrepareResearchRun(List<IIStrategyParameter> parameters, List<bool> selected, IReadOnlyList<OptimizerFaze> phases, string nativeFilters);
        /// <summary>Called on a completed pass before native removal/clear so summaries cannot race final experiment collection. Must not send orders.</summary>
        void FinalizeResearchPass();
        /// <summary>Called once on the template after native phase processing, including explicit stop. Retain missing/failed trials; must not create passes.</summary>
        void CompleteResearchRun(IReadOnlyList<OptimizerFazeReport> reports);
    }
}
