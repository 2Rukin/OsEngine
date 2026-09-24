/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace OsEngine.OsData.OrderFlow.Explorer
{
    /// <summary>Persists completion/cancellation/error reasons outside immutable research bundles; never stores raw source rows.</summary>
    internal static class ExplorerAttempt
    {
        internal static ExplorerRun Run(ExplorerRunSpec spec, CancellationToken cancellation, Action<ExplorerProgress> progress)
        {
            Stopwatch watch = Stopwatch.StartNew(); string status = "Complete", reason = null; ExplorerProgress last = null;
            void Report(ExplorerProgress value) { last = value; progress?.Invoke(value); }
            try
            {
                ExplorerRun run = ExplorerRunner.Catalog(spec, cancellation, Report);
                run = ExplorerEpisodeRunner.Run(run, cancellation, Report);
                return ExplorerStudyRunner.Run(run, cancellation, Report);
            }
            catch (OperationCanceledException) { status = "Cancelled"; reason = "Owner cancellation"; throw; }
            catch (Exception error) { status = "Error"; reason = error.Message; throw; }
            finally
            {
                try
                {
                    string directory = Path.Combine(spec.OutputRootPath, "cloud-explorer-attempts"); Directory.CreateDirectory(directory);
                    File.WriteAllText(Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json"), JsonSerializer.Serialize(new { Status = status, Reason = reason,
                        Progress = last, Seconds = watch.Elapsed.TotalSeconds, ManagedBytes = GC.GetTotalMemory(false), PeakWorkingSet = Process.GetCurrentProcess().PeakWorkingSet64 }));
                }
                catch (Exception error) { OsEngine.Market.ServerMaster.SendNewLogMessage(error.ToString(), OsEngine.Logging.LogMessageType.System); }
            }
        }
    }
}
