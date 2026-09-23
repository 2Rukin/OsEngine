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
        /// Input access or decoding failures produce a rejected bundle with single-input provenance.
        /// The returned bundle is research evidence, not execution or PnL.
        /// </remarks>
        /// <param name="request">Validated local-file request and output root.</param>
        /// <param name="cancellationToken">Cancellation observed by replay and immediately before export.</param>
        /// <returns>The research result with <c>ArtifactDirectory</c> assigned.</returns>
        /// <exception cref="OperationCanceledException">Cancellation is requested before export starts.</exception>
        /// <exception cref="IOException">Artifact files cannot be read or written.</exception>
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
            string tradingDate = result.Quality.FirstEventTime?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? "empty";
            string inputPrefix = GetHashPrefix(result.InputHash);
            string specPrefix = GetHashPrefix(result.ResearchSpecHash);
            string directoryName = "tick-flow-" + tradingDate + "-" + inputPrefix + "-" + specPrefix;
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
            manifest.CloudVersion = OrderFlowResearchSchema.CloudVersion;
            manifest.CalculateDelta = request.CalculateDelta;
            manifest.CalculateCloud = request.CalculateCloud;
            manifest.CalculateCloud2 = request.CalculateCloud2;
            manifest.Cloud2 = request.Cloud2;
            manifest.Cloud2Hash = result.Cloud2Hash;
            manifest.Cloud2Count = result.Clouds2.Count;
            manifest.Cloud2PassedCount = result.Clouds2.Count(cloud => cloud.ImbalancePassed);
            manifest.CloudPassedCount = result.Clouds.Count(cloud => cloud.ImbalancePassed);
            manifest.Cloud = request.Cloud;
            manifest.CloudHash = result.CloudHash;
            manifest.CloudCount = result.Clouds.Count;
            manifest.InputHash = result.InputHash;
            manifest.ResearchSpecHash = result.ResearchSpecHash;
            manifest.NormalizedEventHash = result.NormalizedEventHash;
            manifest.FeatureHash = result.FeatureHash;
            manifest.CandidateHash = result.CandidateHash;
            manifest.Input = result.Input;
            manifest.PriceStep = request.PriceStep;
            manifest.FromDate = request.FromDate;
            manifest.ToDate = request.ToDate;
            manifest.FeatureWindowSeconds = request.FeatureWindowSeconds;
            manifest.MinimumAbsoluteDelta = request.MinimumAbsoluteDelta;
            manifest.MinimumPriceChangeTicks = request.MinimumPriceChangeTicks;
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
            WriteText(directory, "clouds.csv", BuildCloudsCsv(result.Clouds));
            WriteText(directory, "clouds2.csv", BuildCloudsCsv(result.Clouds2));
            WriteCloudPairs(directory, "cloud-pairs.csv", result.Clouds);
            WriteCloudPairs(directory, "cloud2-pairs.csv", result.Clouds2);
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

        private static void WriteCloudPairs(string directory, string name, List<OrderFlowCloud> clouds)
        {
            using StreamWriter writer = new StreamWriter(Path.Combine(directory, name), false, new UTF8Encoding(false));
            writer.WriteLine("cloud_id,profile,lower_price,buy_volume,sell_volume");
            foreach (OrderFlowCloud cloud in clouds)
            foreach ((string profile, OrderFlowImbalanceSnapshot snapshot) in new[] { ("inside", cloud.InsideImbalance), ("context", cloud.ContextImbalance) })
            {
                if (snapshot == null) { continue; }
                foreach (OrderFlowDiagonalPair pair in snapshot.Pairs.Values)
                { writer.WriteLine(string.Join(",", cloud.CloudId, profile, FormatDecimal(pair.LowerPrice), FormatDecimal(pair.Buy), FormatDecimal(pair.Sell))); }
            }
        }

        private static string ImbalanceCsvHeader(string prefix)
        {
            StringBuilder header = new StringBuilder("," + prefix + "_buy_volume," + prefix + "_sell_volume," + prefix + "_delta_percent," + prefix + "_comparable_pairs");
            foreach (string name in new[] { "best_buy", "best_sell", "eligible_buy", "eligible_sell" })
            { header.Append("," + prefix + "_" + name + "_lower_price," + prefix + "_" + name + "_buy_volume," + prefix + "_" + name + "_sell_volume," + prefix + "_" + name + "_ratio_percent"); }
            return header.ToString();
        }

        private static void AppendImbalanceCsv(List<string> fields, OrderFlowImbalanceSnapshot snapshot)
        {
            if (snapshot == null) { for (int i = 0; i < 20; i++) { fields.Add(null); } return; }
            fields.Add(FormatDecimal(snapshot.BuyVolume)); fields.Add(FormatDecimal(snapshot.SellVolume));
            fields.Add(FormatDecimal(snapshot.DeltaPercent)); fields.Add(snapshot.ComparablePairs.ToString(CultureInfo.InvariantCulture));
            OrderFlowDiagonalPair[] pairs = { snapshot.BestBuy, snapshot.BestSell, snapshot.EligibleBuy, snapshot.EligibleSell };
            for (int i = 0; i < pairs.Length; i++)
            {
                OrderFlowDiagonalPair pair = pairs[i];
                fields.Add(pair == null ? null : FormatDecimal(pair.LowerPrice));
                fields.Add(pair == null ? null : FormatDecimal(pair.Buy));
                fields.Add(pair == null ? null : FormatDecimal(pair.Sell));
                fields.Add(pair == null ? null : (i % 2 == 0 ? pair.BuyRatioPercent : pair.SellRatioPercent).ToString("R", CultureInfo.InvariantCulture));
            }
        }

        private static string BuildCloudsCsv(List<OrderFlowCloud> clouds)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("cloud_id,start_time,last_tick_time,completed_at,last_source_sequence,completion_source_sequence,completion_reason,price,low,high,volume,buy_volume,sell_volume,delta,buy_count,sell_count,side_percent,largest_tick,first_source_sequence,first_price,vwap,range_price,range_ticks,price_change,duration_ms,kind,qualified_time,qualified_source_sequence,qualified_price,qualified_low,qualified_high,qualified_volume,qualified_buy_volume,qualified_sell_volume,qualified_trade_count,qualified_vwap,delta_percent,imbalance_source,imbalance_pass" + ImbalanceCsvHeader("inside") + ImbalanceCsvHeader("context"));
            foreach (OrderFlowCloud cloud in clouds)
            {
                List<string> fields = new List<string>
                {
                    cloud.CloudId, FormatTime(cloud.StartTime), FormatTime(cloud.Time),
                    cloud.CompletedAt.HasValue ? FormatTime(cloud.CompletedAt.Value) : null,
                    cloud.LastSourceSequence.ToString(CultureInfo.InvariantCulture),
                    cloud.CompletionSourceSequence?.ToString(CultureInfo.InvariantCulture), cloud.CompletionReason,
                    FormatDecimal(cloud.Price), FormatDecimal(cloud.Low), FormatDecimal(cloud.High), FormatDecimal(cloud.Volume),
                    FormatDecimal(cloud.BuyVolume), FormatDecimal(cloud.SellVolume), FormatDecimal(cloud.Delta),
                    cloud.BuyCount.ToString(CultureInfo.InvariantCulture), cloud.SellCount.ToString(CultureInfo.InvariantCulture),
                    FormatDecimal(cloud.SidePercent), FormatDecimal(cloud.LargestTick),
                    cloud.FirstSourceSequence.ToString(CultureInfo.InvariantCulture),
                    FormatDecimal(cloud.FirstPrice), FormatDecimal(cloud.Vwap), FormatDecimal(cloud.RangePrice),
                    FormatDecimal(cloud.RangeTicks), FormatDecimal(cloud.PriceChange), FormatDecimal(cloud.DurationMilliseconds), cloud.Kind,
                    FormatTime(cloud.Qualified.Time), cloud.Qualified.SourceSequence.ToString(CultureInfo.InvariantCulture),
                    FormatDecimal(cloud.Qualified.Price), FormatDecimal(cloud.Qualified.Low), FormatDecimal(cloud.Qualified.High),
                    FormatDecimal(cloud.Qualified.Volume), FormatDecimal(cloud.Qualified.BuyVolume), FormatDecimal(cloud.Qualified.SellVolume),
                    cloud.Qualified.TradeCount.ToString(CultureInfo.InvariantCulture), FormatDecimal(cloud.Qualified.Vwap),
                    FormatDecimal(cloud.DeltaPercent), cloud.ImbalanceSource.ToString(), cloud.ImbalancePassed ? "true" : "false"
                };
                AppendImbalanceCsv(fields, cloud.InsideImbalance);
                AppendImbalanceCsv(fields, cloud.ContextImbalance);
                AppendCsvRow(builder, fields.ToArray());
            }
            return builder.ToString();
        }

        private static string BuildObservationsCsv(List<OrderFlowObservation> observations)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("observation_key,observation_type,candidate_id,direction,snapshot_id,bucket_sequence,time,reference_price,buy_volume,sell_volume,delta,trade_count,price_change,price_response,data_quality_code\n");

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
            builder.Append("candidate_id,horizon_seconds,candidate_time,horizon_time,is_complete,outcome,signed_return,mfe,mae,time_to_mfe_us,time_to_mae_us,time_to_target_us,time_to_invalidation_us,future_trade_count\n");

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
                    label.TimeToMfeMicroseconds.ToString(CultureInfo.InvariantCulture),
                    label.TimeToMaeMicroseconds.ToString(CultureInfo.InvariantCulture),
                    label.TimeToTargetMicroseconds.ToString(CultureInfo.InvariantCulture),
                    label.TimeToInvalidationMicroseconds.ToString(CultureInfo.InvariantCulture),
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
            builder.Append("time_start,time_end,open,high,low,close,volume,delta,price_response\n");

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
                    FormatDecimal(bar.PriceResponse)
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
                "cloud-pairs.csv and cloud2-pairs.csv retain all diagonal pairs for post-calculation filters. Display filters never rewrite this bundle.\n" +
                "clouds.csv and clouds2.csv keep each layer separate, with first qualification snapshots separate from final chain fields. Never use final values at qualification time.\n" +
                "Imbalance fields are frozen at the last included source row. Context uses all selected-period ticks, including below the Cloud size filter.\n" +
                "Ratios compare Buy at lower_price + manual PriceStep to Sell at lower_price. Missing/zero opponents are unavailable. 300% means 3:1.\n" +
                "Imbalance FAIL rows remain in CSV; display filtering never changes chains. Both mode requires the same passing side in both profiles.\n" +
                "Chain markers anchor at the last included tick, while CompletedAt may be later; OpenAtEnd is unconfirmed at EOF. SingleTick is complete at its own time/sequence.\n" +
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
            return time.ToString("yyyy-MM-ddTHH:mm:ss.ffffff", CultureInfo.InvariantCulture);
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
        public string CloudVersion { get; set; }
        public bool CalculateDelta { get; set; }
        public bool CalculateCloud { get; set; }
        public bool CalculateCloud2 { get; set; }
        public OrderFlowCloudSettings Cloud2 { get; set; }
        public int Cloud2Count { get; set; }
        public int Cloud2PassedCount { get; set; }
        public int CloudPassedCount { get; set; }
        public string Cloud2Hash { get; set; }
        public OrderFlowCloudSettings Cloud { get; set; }
        public int CloudCount { get; set; }
        public string CloudHash { get; set; }

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

        public OrderFlowTickInput Input { get; set; }

        public decimal PriceStep { get; set; }

        public DateTime? FromDate { get; set; }

        public DateTime? ToDate { get; set; }

        public int FeatureWindowSeconds { get; set; }

        public decimal MinimumAbsoluteDelta { get; set; }

        public int MinimumPriceChangeTicks { get; set; }

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
