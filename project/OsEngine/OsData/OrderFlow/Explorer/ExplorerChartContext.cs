/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace OsEngine.OsData.OrderFlow.Explorer
{
    /// <summary>One bounded, saved-file-only time slice shared by candles and all marker layers. Raw ticks are never opened.</summary>
    internal sealed record ExplorerChartContext(DateTime From, DateTime To, IReadOnlyList<ExplorerBar> Bars,
        ExplorerCloud[] Clouds, ExplorerPivot[] Pivots, ExplorerObservation[] Observations, ExplorerEpisode[] Episodes, bool Limited)
    {
        internal static ExplorerChartContext Load(ExplorerRun run, DateTime from, DateTime to, OrderFlowDisplayTimeFrame frame, CancellationToken token)
        {
            const int limit = 4000; bool limited = false;
            T[] Read<T>(string path, string table, Func<T, bool> includes)
            {
                if (path == null) { return Array.Empty<T>(); }
                List<T> rows = new List<T>();
                foreach (T item in ExplorerStorage.ReadRows<T>(path, table))
                {
                    token.ThrowIfCancellationRequested(); if (!includes(item)) { continue; }
                    if (rows.Count == limit) { limited = true; break; } rows.Add(item);
                }
                return rows.ToArray();
            }
            ExplorerCloud[] clouds = Read<ExplorerCloud>(run.CatalogPath, "catalog", c => c.Time >= from && c.StartTime <= to);
            ExplorerPivot[] pivots = Read<ExplorerPivot>(run.StudyPath, "pivots", p => p.ObservedAt >= from && p.ObservedAt <= to);
            ExplorerObservation[] observations = Read<ExplorerObservation>(run.StudyPath, "observations", o => o.Time >= from && o.Time <= to);
            ExplorerEpisode[] episodes = Read<ExplorerEpisode>(run.EpisodePath, "episodes", e => e.Time >= from && e.StartTime <= to);
            return new ExplorerChartContext(from, to, ExplorerBars.ReadRange(run.CatalogPath, from, to, frame, token), clouds, pivots, observations, episodes, limited);
        }
    }
}
