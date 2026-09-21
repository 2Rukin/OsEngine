/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace OsEngine.OsData.OrderFlow
{
    /// <summary>
    /// Workbench boundary that runs the offline engine and writes a
    /// write-once deterministic artifact bundle.
    /// </summary>
    /// <remarks>
    /// A caller thread owns one synchronous invocation from replay through
    /// export and must not mutate the request concurrently. The runner changes
    /// no application settings or positions and creates no orders, fills or
    /// PnL. Contract: ORDER-FLOW-DATA-001, ORDER-FLOW-RESEARCH-001 and
    /// ORDER-FLOW-QUALIFICATION-001.
    /// </remarks>
    internal sealed class OrderFlowResearchRunner
    {
        /// <summary>
        /// Runs the offline engine and publishes one deterministic artifact bundle.
        /// </summary>
        /// <remarks>
        /// Execution is synchronous on the calling thread. Cancellation is
        /// observed during replay and immediately before export; once export
        /// starts, filesystem completion is not cooperatively cancelled.
        /// The returned bundle is research evidence, not execution or PnL.
        /// </remarks>
        /// <param name="request">Validated local-file request and output root.</param>
        /// <param name="cancellationToken">Cancellation observed by replay and immediately before export.</param>
        /// <returns>The research result with <c>ArtifactDirectory</c> assigned.</returns>
        /// <exception cref="OperationCanceledException">Cancellation is requested before export starts.</exception>
        /// <exception cref="IOException">Input or artifact files cannot be read or written.</exception>
        /// <exception cref="InvalidOperationException">An existing bundle with the same identity is not byte-identical.</exception>
        public OrderFlowResearchResult RunAndExport(OrderFlowResearchRequest request,
            CancellationToken cancellationToken)
        {
            OrderFlowResearchEngine engine = new OrderFlowResearchEngine();
            OrderFlowResearchResult result = engine.Run(request, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            OrderFlowResearchArtifactWriter writer = new OrderFlowResearchArtifactWriter();
            result.ArtifactDirectory = writer.Write(request, result);
            return result;
        }
    }

    /// <summary>
    /// Writes causal observations and future labels to separate files and
    /// refuses to overwrite a non-identical bundle with the same identity.
    /// </summary>
    /// <remarks>
    /// One caller thread owns each invocation. Concurrent writers targeting the
    /// same identity are not coordinated and must be serialized by their caller.
    /// Existing target bundles are never changed; on failure the invocation
    /// attempts to remove only its staging content. Contract:
    /// ORDER-FLOW-DATA-001 and ORDER-FLOW-RESEARCH-001.
    /// </remarks>
    internal sealed class OrderFlowResearchArtifactWriter
    {
        private static readonly UTF8Encoding _utf8NoBom = new UTF8Encoding(false);

        /// <summary>
        /// Writes causal observations and future labels into separate files via
        /// a staging directory, then publishes the bundle by directory move.
        /// </summary>
        /// <remarks>
        /// The caller must provide a completed result and serialize competing
        /// writes for the same identity. Existing content is never overwritten;
        /// the method attempts to delete invocation-owned staging before
        /// propagating failure. It writes research evidence only.
        /// </remarks>
        /// <param name="request">Run request containing the output root and canonical spec.</param>
        /// <param name="result">Completed deterministic engine result.</param>
        /// <returns>The existing byte-identical bundle or the newly published directory.</returns>
        /// <exception cref="IOException">The filesystem operation fails.</exception>
        /// <exception cref="InvalidOperationException">The identity already exists with different bytes.</exception>
        public string Write(OrderFlowResearchRequest request, OrderFlowResearchResult result)
        {
            string tradingDate = result.DealsHeader == null
                ? "unknown-date"
                : result.DealsHeader.TradingDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            string inputPrefix = GetHashPrefix(result.InputHash);
            string specPrefix = GetHashPrefix(result.ResearchSpecHash);
            string directoryName = "order-flow-" + tradingDate + "-" + inputPrefix + "-" + specPrefix;
            string outputRoot = Path.GetFullPath(request.OutputRootPath);
            string targetDirectory = Path.Combine(outputRoot, directoryName);
            string stagingDirectory = targetDirectory + ".staging-" + Guid.NewGuid().ToString("N");

            Directory.CreateDirectory(outputRoot);
            Directory.CreateDirectory(stagingDirectory);

            try
            {
                WriteBundle(stagingDirectory, request, result);

                if (Directory.Exists(targetDirectory))
                {
                    if (DirectoriesEqual(stagingDirectory, targetDirectory) == false)
                    {
                        throw new InvalidOperationException(
                            "An artifact bundle with the same input/spec identity already exists but has different content.");
                    }

                    Directory.Delete(stagingDirectory, true);
                    return targetDirectory;
                }

                Directory.Move(stagingDirectory, targetDirectory);
                return targetDirectory;
            }
            catch
            {
                if (Directory.Exists(stagingDirectory))
                {
                    Directory.Delete(stagingDirectory, true);
                }

                throw;
            }
        }

        private static void WriteBundle(string directory, OrderFlowResearchRequest request,
            OrderFlowResearchResult result)
        {
            JsonSerializerOptions jsonOptions = new JsonSerializerOptions();
            jsonOptions.WriteIndented = true;
            jsonOptions.Converters.Add(new JsonStringEnumConverter());

            OrderFlowArtifactManifest manifest = new OrderFlowArtifactManifest();
            manifest.ArtifactSchemaVersion = OrderFlowResearchSchema.ArtifactSchemaVersion;
            manifest.ParserVersion = OrderFlowResearchSchema.ParserVersion;
            manifest.NormalizerVersion = OrderFlowResearchSchema.NormalizerVersion;
            manifest.FeatureSchemaVersion = OrderFlowResearchSchema.FeatureSchemaVersion;
            manifest.CandidateDetectorVersion = OrderFlowResearchSchema.CandidateDetectorVersion;
            manifest.LabelSchemaVersion = OrderFlowResearchSchema.LabelSchemaVersion;
            manifest.InputHash = result.InputHash;
            manifest.ResearchSpecHash = result.ResearchSpecHash;
            manifest.NormalizedEventHash = result.NormalizedEventHash;
            manifest.FeatureHash = result.FeatureHash;
            manifest.CandidateHash = result.CandidateHash;
            manifest.Deals = result.DealsHeader;
            manifest.Quotes = result.QuotesHeader;
            manifest.FeatureWindowSeconds = request.FeatureWindowSeconds;
            manifest.MinimumAbsoluteDelta = request.MinimumAbsoluteDelta;
            manifest.MinimumPriceChangeTicks = request.MinimumPriceChangeTicks;
            manifest.TopBookLevels = request.TopBookLevels;
            manifest.MaximumBookAgeMilliseconds = request.MaximumBookAgeMilliseconds;
            manifest.CandidateCooldownMilliseconds = request.CandidateCooldownMilliseconds;
            manifest.BackgroundSampleSeconds = request.BackgroundSampleSeconds;
            manifest.LabelHorizonsSeconds = request.LabelHorizonsSeconds;
            manifest.TargetTicks = request.TargetTicks;
            manifest.InvalidationTicks = request.InvalidationTicks;
            manifest.ObservationCount = result.Observations.Count;
            manifest.CandidateCount = result.Candidates.Count;
            manifest.LabelCount = result.Labels.Count;
            manifest.ResearchAccepted = result.Quality.ResearchAccepted;
            manifest.EvidenceBoundary = "Research-only market-path evidence. No orders, fills, execution PnL or profitability qualification.";

            WriteText(directory, "manifest.json", JsonSerializer.Serialize(manifest, jsonOptions));
            WriteText(directory, "quality.json", JsonSerializer.Serialize(result.Quality, jsonOptions));
            WriteText(directory, "observations.csv", BuildObservationsCsv(result.Observations));
            WriteText(directory, "candidates.csv", BuildCandidatesCsv(result.Candidates));
            WriteText(directory, "market-path-labels.csv", BuildLabelsCsv(result.Labels));
            WriteText(directory, "event-journal.csv", BuildJournalCsv(result.Journal));
            WriteText(directory, "bars-sec15.csv", BuildBarsCsv(result.Bars, OrderFlowDisplayTimeFrame.Sec15));
            WriteText(directory, "bars-sec30.csv", BuildBarsCsv(result.Bars, OrderFlowDisplayTimeFrame.Sec30));
            WriteText(directory, "bars-min1.csv", BuildBarsCsv(result.Bars, OrderFlowDisplayTimeFrame.Min1));
            WriteText(directory, "README.txt", BuildBoundaryReadme());
        }

        private static string BuildObservationsCsv(List<OrderFlowObservation> observations)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("observation_key,observation_type,candidate_id,direction,snapshot_id,bucket_sequence,time,reference_price,buy_volume,sell_volume,delta,trade_count,price_change,price_response,book_available,book_stale,book_time,book_age_ms,spread,book_imbalance,data_quality_code\n");

            for (int i = 0; i < observations.Count; i++)
            {
                OrderFlowObservation observation = observations[i];
                OrderFlowFeatureSnapshot feature = observation.Features;
                AppendCsvRow(builder, new string[]
                {
                    observation.ObservationKey,
                    observation.ObservationType.ToString(),
                    observation.CandidateId,
                    observation.Direction.ToString(),
                    feature.SnapshotId,
                    feature.BucketSequence.ToString(CultureInfo.InvariantCulture),
                    FormatTime(feature.Time),
                    FormatDecimal(feature.ReferencePrice),
                    FormatDecimal(feature.BuyVolume),
                    FormatDecimal(feature.SellVolume),
                    FormatDecimal(feature.Delta),
                    feature.TradeCount.ToString(CultureInfo.InvariantCulture),
                    FormatDecimal(feature.PriceChange),
                    FormatDecimal(feature.PriceResponse),
                    feature.BookAvailable ? "true" : "false",
                    feature.BookStale ? "true" : "false",
                    feature.BookTime.HasValue ? FormatTime(feature.BookTime.Value) : string.Empty,
                    feature.BookAgeMilliseconds.ToString(CultureInfo.InvariantCulture),
                    FormatDecimal(feature.Spread),
                    FormatDecimal(feature.BookImbalance),
                    feature.DataQualityCode
                });
            }

            return builder.ToString();
        }

        private static string BuildCandidatesCsv(List<OrderFlowCandidate> candidates)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("candidate_id,observation_key,time,direction,reason_code,data_quality_code,reference_price,snapshot_id\n");

            for (int i = 0; i < candidates.Count; i++)
            {
                OrderFlowCandidate candidate = candidates[i];
                AppendCsvRow(builder, new string[]
                {
                    candidate.CandidateId,
                    candidate.ObservationKey,
                    FormatTime(candidate.Time),
                    candidate.Direction.ToString(),
                    candidate.ReasonCode,
                    candidate.DataQualityCode,
                    FormatDecimal(candidate.ReferencePrice),
                    candidate.SnapshotId
                });
            }

            return builder.ToString();
        }

        private static string BuildLabelsCsv(List<OrderFlowMarketPathLabel> labels)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("candidate_id,horizon_seconds,candidate_time,horizon_time,is_complete,outcome,signed_return,mfe,mae,time_to_mfe_ms,time_to_mae_ms,time_to_target_ms,time_to_invalidation_ms,future_trade_count\n");

            for (int i = 0; i < labels.Count; i++)
            {
                OrderFlowMarketPathLabel label = labels[i];
                AppendCsvRow(builder, new string[]
                {
                    label.CandidateId,
                    label.HorizonSeconds.ToString(CultureInfo.InvariantCulture),
                    FormatTime(label.CandidateTime),
                    FormatTime(label.HorizonTime),
                    label.IsComplete ? "true" : "false",
                    label.Outcome.ToString(),
                    FormatDecimal(label.SignedReturn),
                    FormatDecimal(label.MaximumFavorableExcursion),
                    FormatDecimal(label.MaximumAdverseExcursion),
                    label.TimeToMfeMilliseconds.ToString(CultureInfo.InvariantCulture),
                    label.TimeToMaeMilliseconds.ToString(CultureInfo.InvariantCulture),
                    label.TimeToTargetMilliseconds.ToString(CultureInfo.InvariantCulture),
                    label.TimeToInvalidationMilliseconds.ToString(CultureInfo.InvariantCulture),
                    label.FutureTradeCount.ToString(CultureInfo.InvariantCulture)
                });
            }

            return builder.ToString();
        }

        private static string BuildJournalCsv(List<OrderFlowJournalEntry> entries)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("time,kind,correlation_id,reason_code,message\n");

            for (int i = 0; i < entries.Count; i++)
            {
                OrderFlowJournalEntry entry = entries[i];
                AppendCsvRow(builder, new string[]
                {
                    entry.Time == DateTime.MinValue ? string.Empty : FormatTime(entry.Time),
                    entry.Kind.ToString(),
                    entry.CorrelationId,
                    entry.ReasonCode,
                    entry.Message
                });
            }

            return builder.ToString();
        }

        private static string BuildBarsCsv(Dictionary<OrderFlowDisplayTimeFrame, List<OrderFlowDisplayBar>> bars,
            OrderFlowDisplayTimeFrame timeFrame)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("time_start,time_end,open,high,low,close,volume,delta,price_response,book_imbalance,book_age_ms,book_available,book_stale\n");

            List<OrderFlowDisplayBar> frameBars;
            if (bars.TryGetValue(timeFrame, out frameBars) == false)
            {
                return builder.ToString();
            }

            for (int i = 0; i < frameBars.Count; i++)
            {
                OrderFlowDisplayBar bar = frameBars[i];
                AppendCsvRow(builder, new string[]
                {
                    FormatTime(bar.TimeStart),
                    FormatTime(bar.TimeEnd),
                    FormatDecimal(bar.Open),
                    FormatDecimal(bar.High),
                    FormatDecimal(bar.Low),
                    FormatDecimal(bar.Close),
                    FormatDecimal(bar.Volume),
                    FormatDecimal(bar.Delta),
                    FormatDecimal(bar.PriceResponse),
                    FormatDecimal(bar.BookImbalance),
                    bar.BookAgeMilliseconds.ToString(CultureInfo.InvariantCulture),
                    bar.BookAvailable ? "true" : "false",
                    bar.BookStale ? "true" : "false"
                });
            }

            return builder.ToString();
        }

        private static string BuildBoundaryReadme()
        {
            return "Order Flow Research MVP artifact bundle\n\n" +
                "observations.csv contains only information available at or before each closed timestamp bucket.\n" +
                "market-path-labels.csv contains future market outcomes and must never be joined into live features.\n" +
                "candidates.csv contains broad research hypotheses, not confirmed signals or trades.\n" +
                "No file in this bundle contains an order, fill, execution simulation, net PnL or profitability qualification.\n";
        }

        private static void AppendCsvRow(StringBuilder builder, string[] fields)
        {
            for (int i = 0; i < fields.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                builder.Append(EscapeCsv(fields[i]));
            }

            builder.Append('\n');
        }

        private static string EscapeCsv(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }

            return value;
        }

        private static string FormatTime(DateTime time)
        {
            return time.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture);
        }

        private static string FormatDecimal(decimal value)
        {
            return value.ToString("G29", CultureInfo.InvariantCulture);
        }

        private static string GetHashPrefix(string hash)
        {
            if (string.IsNullOrEmpty(hash))
            {
                return "nohash";
            }

            return hash.Length <= 12 ? hash : hash.Substring(0, 12);
        }

        private static void WriteText(string directory, string fileName, string content)
        {
            File.WriteAllText(Path.Combine(directory, fileName), content, _utf8NoBom);
        }

        private static bool DirectoriesEqual(string firstDirectory, string secondDirectory)
        {
            string[] firstFiles = Directory.GetFiles(firstDirectory)
                .Select(path => Path.GetFileName(path))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            string[] secondFiles = Directory.GetFiles(secondDirectory)
                .Select(path => Path.GetFileName(path))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            if (firstFiles.SequenceEqual(secondFiles, StringComparer.Ordinal) == false)
            {
                return false;
            }

            for (int i = 0; i < firstFiles.Length; i++)
            {
                string firstHash = OrderFlowFileHash.Calculate(Path.Combine(firstDirectory, firstFiles[i]));
                string secondHash = OrderFlowFileHash.Calculate(Path.Combine(secondDirectory, secondFiles[i]));
                if (string.Equals(firstHash, secondHash, StringComparison.Ordinal) == false)
                {
                    return false;
                }
            }

            return true;
        }
    }

    internal sealed class OrderFlowArtifactManifest
    {
        public string ArtifactSchemaVersion { get; set; }

        public string ParserVersion { get; set; }

        public string NormalizerVersion { get; set; }

        public string FeatureSchemaVersion { get; set; }

        public string CandidateDetectorVersion { get; set; }

        public string LabelSchemaVersion { get; set; }

        public string InputHash { get; set; }

        public string ResearchSpecHash { get; set; }

        public string NormalizedEventHash { get; set; }

        public string FeatureHash { get; set; }

        public string CandidateHash { get; set; }

        public OrderFlowQshHeader Deals { get; set; }

        public OrderFlowQshHeader Quotes { get; set; }

        public int FeatureWindowSeconds { get; set; }

        public decimal MinimumAbsoluteDelta { get; set; }

        public int MinimumPriceChangeTicks { get; set; }

        public int TopBookLevels { get; set; }

        public int MaximumBookAgeMilliseconds { get; set; }

        public int CandidateCooldownMilliseconds { get; set; }

        public int BackgroundSampleSeconds { get; set; }

        public List<int> LabelHorizonsSeconds { get; set; }

        public int TargetTicks { get; set; }

        public int InvalidationTicks { get; set; }

        public int ObservationCount { get; set; }

        public int CandidateCount { get; set; }

        public int LabelCount { get; set; }

        public bool ResearchAccepted { get; set; }

        public string EvidenceBoundary { get; set; }
    }
}
