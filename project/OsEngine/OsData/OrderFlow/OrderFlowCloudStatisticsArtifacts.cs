/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace OsEngine.OsData.OrderFlow
{
    /// <summary>Publishes an immutable, separately identified statistics bundle beside the source calculation; original artifacts are never changed.</summary>
    /// <remarks>Single caller owns staging and publication. Failure/cancellation removes only this invocation's staging; matching bundles are verified byte-for-byte. JSON serialization is synchronous; cancellation resumes at its boundary. ORDER-FLOW-MVP-RUNBOOK-001 defines the study format and evidence limits.</remarks>
    internal static class OrderFlowCloudStatisticsArtifacts
    {
        /// <summary>Returns the published directory only after all study files are written and verified. Existing mismatched content is never overwritten.</summary>
        public static string Write(string outputRoot, OrderFlowCloudStatisticsResult result, CancellationToken cancellation)
        {
            string root = Path.GetFullPath(outputRoot);
            string target = Path.Combine(root, "cloud-study-" + result.StudyHash);
            string staging = target + ".staging-" + Guid.NewGuid().ToString("N");
            cancellation.ThrowIfCancellationRequested();
            Directory.CreateDirectory(root); Directory.CreateDirectory(staging);
            try
            {
                JsonSerializerOptions options = new JsonSerializerOptions { WriteIndented = true };
                options.Converters.Add(new JsonStringEnumConverter());
                using (FileStream json = File.Create(Path.Combine(staging, "study.json"))) { JsonSerializer.Serialize(json, result, options); }
                cancellation.ThrowIfCancellationRequested();
                using (StreamWriter csv = new StreamWriter(Path.Combine(staging, "reactions.csv"), false, new UTF8Encoding(false)))
                {
                    csv.WriteLine("cloud_id,layer,completion_time,completion_sequence,reference_price,atr,feature_start,part,sampled,horizon_minutes,reversal,complete,future_trades,outcome,first_touch_sequence,first_touch_seconds,mfe_atr,mae_atr,end_return_atr");
                    foreach (OrderFlowCloudStudyEvent item in result.Events)
                    foreach (OrderFlowCloudReaction reaction in item.Reactions)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        csv.WriteLine(string.Join(",", item.CloudId, item.Layer, T(item.Time), item.SourceSequence, F(item.ReferencePrice), F(item.Atr), T(item.FeatureStart), item.Part,
                            item.Sampled, reaction.HorizonMinutes, reaction.Reversal, reaction.IsComplete, reaction.FutureTrades, reaction.Outcome,
                            reaction.FirstTouchSequence?.ToString(CultureInfo.InvariantCulture), reaction.FirstTouchSeconds.HasValue ? F(reaction.FirstTouchSeconds.Value) : "",
                            F(reaction.MfeAtr), F(reaction.MaeAtr), F(reaction.EndReturnAtr)));
                    }
                }
                File.WriteAllText(Path.Combine(staging, "README.txt"),
                    "Exploratory completed-Cloud market-path study; no orders, fills, fees or profit qualification.\n" +
                    "ATR is the arithmetic mean of preceding completed observed one-minute true ranges; previous close requires one additional bar.\n" +
                    "Reference price is the completion tick, not the earlier drawn Cloud anchor. Future rows use strict source ordinal order, including equal timestamps.\n" +
                    "All horizons share a fixed, layer-local non-overlapping sample selected before rule filtering. The two layers are not pooled as independent evidence.\n" +
                    "Volatility thirds and volume/difference quantiles use fit only. Day split purges both features and future labels.\n" +
                    "A fixed single-coordinate rule grid is ranked on fit Wilson lower bound. The chronological check never selects a rule.\n" +
                    "Wilson bounds are approximate descriptive ranking aids, not multiple-testing-adjusted significance. Reusing the check to retune invalidates untouched-check interpretation.\n" +
                    "Incomplete/no-future-trade horizons, unfinished Clouds and neutral Cloud volume delta are excluded from ranking.\n" +
                    "MatchingCloudIds may include overlapping, unscored or purged events for visual exploration; only complete sampled cohorts contribute metrics.\n", new UTF8Encoding(false));
                cancellation.ThrowIfCancellationRequested();
                if (Directory.Exists(target))
                {
                    if (!EqualDirectories(staging, target, cancellation)) { throw new InvalidOperationException("A different statistics bundle already has this identity."); }
                    Directory.Delete(staging, true);
                }
                else { Directory.Move(staging, target); }
                return target;
            }
            catch
            {
                if (Directory.Exists(staging)) { Directory.Delete(staging, true); }
                throw;
            }
        }

        private static bool EqualDirectories(string first, string second, CancellationToken cancellation)
        {
            string[] names = Directory.GetFiles(first).Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal).ToArray();
            if (!names.SequenceEqual(Directory.GetFiles(second).Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal))) { return false; }
            byte[] a = new byte[65536]; byte[] b = new byte[65536];
            foreach (string name in names)
            {
                using FileStream left = File.OpenRead(Path.Combine(first, name)); using FileStream right = File.OpenRead(Path.Combine(second, name));
                if (left.Length != right.Length) { return false; }
                int count;
                while ((count = left.Read(a, 0, a.Length)) > 0)
                {
                    cancellation.ThrowIfCancellationRequested(); int read = 0;
                    while (read < count) { int block = right.Read(b, read, count - read); if (block == 0) { return false; } read += block; }
                    if (!a.AsSpan(0, count).SequenceEqual(b.AsSpan(0, count))) { return false; }
                }
            }
            return true;
        }

        private static string F(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);
        private static string T(DateTime value) => value.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture);
    }
}
