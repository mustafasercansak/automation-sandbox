using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using UiModel;

namespace ScenarioRunner
{
    public sealed class AppVersionSurveyRecord
    {
        public string Version { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public string ExecutableRelativePath { get; set; } = "";
        public string Arguments { get; set; } = "";
        public bool Downloaded { get; set; }
        public bool Launched { get; set; }
        public bool Settled { get; set; }
        public bool HydrationTimedOut { get; set; }
        public int SettlePassCount { get; set; }
        public string? SettleTelemetry { get; set; }
        public string? WindowTitle { get; set; }
        public string? RootClassName { get; set; }
        public string? RootControlType { get; set; }
        public string? WindowSelectionReason { get; set; }

        // Launch-time observations that change how a capture should be read: runtime roll-forward,
        // dismissed startup dialogs, missing frameworks. Rendered in the readiness table.
        public List<string> LaunchDiagnostics { get; set; } = new();

        public string? Error { get; set; }
        public TimeSpan DiscoveryElapsed { get; set; }
        public ApplicationTreeMetrics? Metrics { get; set; }
        public string? TreeJsonFileName { get; set; }
    }

    public sealed class AppHopSurveyRecord
    {
        public string FromVersion { get; set; } = "";
        public string ToVersion { get; set; } = "";
        public AppVersionSurveyRecord V1 { get; set; } = new();
        public AppVersionSurveyRecord V2 { get; set; } = new();
        public ApplicationTreeDiffResult Diff { get; set; } = new();
        public List<string> RemovedAutomationIds { get; set; } = new();
        public List<string> AddedAutomationIds { get; set; } = new();
        public bool IsSuspectCapture { get; set; }
        public string? SuspectReason { get; set; }
        public bool IsViableHop { get; set; }
        public string ViabilityReason { get; set; } = "";
    }

    public sealed class AppChainSurveyRecord
    {
        public string AppName { get; set; } = "";
        public string Toolkit { get; set; } = "";
        public List<AppVersionSurveyRecord> Versions { get; set; } = new();
        public List<AppHopSurveyRecord> Hops { get; set; } = new();
        public List<string> DistinctRemovedAutomationIds { get; set; } = new();
        public int TotalDistinctBrokenLocatorsCount => DistinctRemovedAutomationIds.Count;

        // Only viable hops count. A suspect or failed hop reports ids that are the difference between
        // two different windows rather than between two releases, and must not inflate the dataset size.
        public int TotalCumulativeBrokenLocatorsCount =>
            Hops.Where(h => h.IsViableHop).Sum(h => h.RemovedAutomationIds.Count);
        public bool IsViableBenchmarkTarget { get; set; }
        public string BenchmarkRecommendation { get; set; } = "";
    }

    public static class OpenSourceAppViabilityEvaluator
    {
        public const double MinimumEmptyAutomationIdFraction = 0.35;
        public const double MaxAllowedNodeDropRatio = 3.0;

        public static AppHopSurveyRecord EvaluateHop(
            AppVersionSurveyRecord v1,
            AppVersionSurveyRecord v2,
            ApplicationTreeDiffResult diff)
        {
            var hop = new AppHopSurveyRecord
            {
                FromVersion = v1.Version,
                ToVersion = v2.Version,
                V1 = v1,
                V2 = v2,
                Diff = diff,
            };

            // Extract removed / added AutomationIds from diff details
            ExtractAutomationIdDeltas(diff, hop.RemovedAutomationIds, hop.AddedAutomationIds);

            if (!v1.Downloaded || !v1.Launched || v1.Metrics == null)
            {
                hop.IsViableHop = false;
                hop.ViabilityReason = $"Older version ({v1.Version}) failed to launch/capture: {v1.Error ?? "Unknown error"}";
                return hop;
            }

            if (!v2.Downloaded || !v2.Launched || v2.Metrics == null)
            {
                hop.IsViableHop = false;
                hop.ViabilityReason = $"Newer version ({v2.Version}) failed to launch/capture: {v2.Error ?? "Unknown error"}";
                return hop;
            }

            // Directional order-of-magnitude node drop check (N1 > 3.0 * N2)
            if (v1.Metrics.TotalNodes > 0 && v2.Metrics.TotalNodes > 0)
            {
                var dropRatio = (double)v1.Metrics.TotalNodes / v2.Metrics.TotalNodes;
                if (dropRatio >= MaxAllowedNodeDropRatio)
                {
                    hop.IsSuspectCapture = true;
                    hop.SuspectReason = $"Order-of-magnitude node drop: {v1.Metrics.TotalNodes} → {v2.Metrics.TotalNodes} ({ReportFormatting.Number(dropRatio, 1)}x decrease). Suspect unhydrated/splash capture.";
                    hop.IsViableHop = false;
                    hop.ViabilityReason = $"Rejected: {hop.SuspectReason}";
                    return hop;
                }
            }

            if (!v1.Settled)
            {
                hop.IsViableHop = false;
                hop.ViabilityReason = $"Older version ({v1.Version}) did not settle cleanly ({v1.SettleTelemetry})";
                return hop;
            }

            if (!v2.Settled)
            {
                hop.IsViableHop = false;
                hop.ViabilityReason = $"Newer version ({v2.Version}) did not settle cleanly ({v2.SettleTelemetry})";
                return hop;
            }

            var maxEmptyFraction = Math.Max(v1.Metrics.EmptyAutomationIdFraction, v2.Metrics.EmptyAutomationIdFraction);
            if (maxEmptyFraction < MinimumEmptyAutomationIdFraction)
            {
                hop.IsViableHop = false;
                hop.ViabilityReason = $"Empty AutomationId fraction too low ({ReportFormatting.Percent(maxEmptyFraction)} < {ReportFormatting.Percent(MinimumEmptyAutomationIdFraction)})";
                return hop;
            }

            if (hop.RemovedAutomationIds.Count > 0)
            {
                hop.IsViableHop = true;
                hop.ViabilityReason = $"Viable hop: {hop.RemovedAutomationIds.Count} removed AutomationId(s) [{string.Join(", ", hop.RemovedAutomationIds)}] with {ReportFormatting.Percent(maxEmptyFraction)} empty IDs";
                return hop;
            }

            if (diff.HasStructuralDrift)
            {
                hop.IsViableHop = true;
                hop.ViabilityReason = $"Viable hop: structural drift ({diff.DriftSignal}) with {ReportFormatting.Percent(maxEmptyFraction)} empty IDs";
                return hop;
            }

            hop.IsViableHop = false;
            hop.ViabilityReason = "No locator-breaking or structural drift detected on this hop";
            return hop;
        }

        public static void EvaluateChain(AppChainSurveyRecord chain)
        {
            var distinctRemovedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var hop in chain.Hops)
            {
                // Ids from a non-viable hop describe a bad capture, not maintainer-driven locator drift.
                if (!hop.IsViableHop)
                {
                    continue;
                }

                foreach (var id in hop.RemovedAutomationIds)
                {
                    distinctRemovedIds.Add(id);
                }
            }

            chain.DistinctRemovedAutomationIds = distinctRemovedIds.OrderBy(id => id, StringComparer.Ordinal).ToList();

            var viableHopsCount = chain.Hops.Count(h => h.IsViableHop);
            var suspectHopsCount = chain.Hops.Count(h => h.IsSuspectCapture);

            if (suspectHopsCount > 0 && viableHopsCount == 0)
            {
                chain.IsViableBenchmarkTarget = false;
                chain.BenchmarkRecommendation = $"Chain contains {suspectHopsCount} suspect capture(s) and 0 viable hops";
                return;
            }

            if (chain.DistinctRemovedAutomationIds.Count > 0)
            {
                chain.IsViableBenchmarkTarget = true;
                chain.BenchmarkRecommendation = $"Viable benchmark target: {chain.DistinctRemovedAutomationIds.Count} distinct broken locator(s) across {viableHopsCount}/{chain.Hops.Count} viable hops (Cumulative: {chain.TotalCumulativeBrokenLocatorsCount})";
                return;
            }

            if (viableHopsCount > 0)
            {
                chain.IsViableBenchmarkTarget = true;
                chain.BenchmarkRecommendation = $"Viable benchmark target: {viableHopsCount}/{chain.Hops.Count} viable hops with structural drift";
                return;
            }

            chain.IsViableBenchmarkTarget = false;
            chain.BenchmarkRecommendation = "Insufficient maintainer-driven locator drift across chain";
        }

        private static void ExtractAutomationIdDeltas(
            ApplicationTreeDiffResult diff,
            List<string> removedIds,
            List<string> addedIds)
        {
            foreach (var detail in diff.Details)
            {
                if (detail.StartsWith("AutomationIds removed", StringComparison.OrdinalIgnoreCase))
                {
                    var idx = detail.IndexOf(':');
                    if (idx >= 0 && idx + 1 < detail.Length)
                    {
                        var raw = detail.Substring(idx + 1);
                        foreach (var token in raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            var trimmed = token.Trim();
                            if (!string.IsNullOrEmpty(trimmed) && !removedIds.Contains(trimmed))
                            {
                                removedIds.Add(trimmed);
                            }
                        }
                    }
                }
                else if (detail.StartsWith("AutomationIds added", StringComparison.OrdinalIgnoreCase))
                {
                    var idx = detail.IndexOf(':');
                    if (idx >= 0 && idx + 1 < detail.Length)
                    {
                        var raw = detail.Substring(idx + 1);
                        foreach (var token in raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            var trimmed = token.Trim();
                            if (!string.IsNullOrEmpty(trimmed) && !addedIds.Contains(trimmed))
                            {
                                addedIds.Add(trimmed);
                            }
                        }
                    }
                }
            }
        }
    }

    public sealed class OpenSourceAppSurveyReport
    {
        public int SchemaVersion { get; set; } = 2;
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public List<AppChainSurveyRecord> Chains { get; set; } = new();

        public int TotalDistinctBrokenLocatorsAcrossAllChains =>
            Chains.Sum(c => c.TotalDistinctBrokenLocatorsCount);

        public string ToMarkdownSummary()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# 📦 Open-Source App Version Chains Benchmark Survey (Issue #71)");
            sb.AppendLine();
            sb.AppendLine($"**Captured At:** {Timestamp:yyyy-MM-dd HH:mm:ss UTC} | **Chains Evaluated:** {Chains.Count} | **Total Distinct Broken Locators:** **{TotalDistinctBrokenLocatorsAcrossAllChains}**");
            sb.AppendLine();

            foreach (var chain in Chains)
            {
                sb.AppendLine($"## 🔗 `{chain.AppName}` Version Chain ({chain.Toolkit})");
                sb.AppendLine();
                sb.AppendLine($"- **Total Releases Probed:** {chain.Versions.Count}");
                sb.AppendLine($"- **Consecutive Hops:** {chain.Hops.Count}");
                sb.AppendLine($"- **Distinct Broken Locators (Deduplicated):** **{chain.TotalDistinctBrokenLocatorsCount}**");
                sb.AppendLine($"- **Cumulative Broken Locators:** {chain.TotalCumulativeBrokenLocatorsCount}");
                sb.AppendLine($"- **Viable Benchmark Target:** {(chain.IsViableBenchmarkTarget ? "✅ **Yes**" : "❌ No")} ({chain.BenchmarkRecommendation})");
                sb.AppendLine();

                sb.AppendLine("### 🔀 Consecutive Hop Drift Telemetry");
                sb.AppendLine();
                sb.AppendLine("| Hop | Older Version | Newer Version | Drift Signal | Removed AutomationIds (Per-Hop) | Status |");
                sb.AppendLine("| :--- | :--- | :--- | :---: | :--- | :---: |");

                foreach (var hop in chain.Hops)
                {
                    var v1Text = hop.V1.Launched && hop.V1.Metrics != null
                        ? $"`{hop.V1.Version}` ({hop.V1.Metrics.TotalNodes} nodes, {ReportFormatting.Percent(hop.V1.Metrics.EmptyAutomationIdFraction, 0)} empty ID)"
                        : $"`{hop.V1.Version}` ❌ {hop.V1.Error ?? "Failed"}";

                    var v2Text = hop.V2.Launched && hop.V2.Metrics != null
                        ? $"`{hop.V2.Version}` ({hop.V2.Metrics.TotalNodes} nodes, {ReportFormatting.Percent(hop.V2.Metrics.EmptyAutomationIdFraction, 0)} empty ID)"
                        : $"`{hop.V2.Version}` ❌ {hop.V2.Error ?? "Failed"}";

                    var removedStr = hop.RemovedAutomationIds.Count > 0
                        ? string.Join(", ", hop.RemovedAutomationIds)
                        : "–";

                    var statusBadge = hop.IsSuspectCapture
                        ? $"⚠️ Suspect ({hop.SuspectReason})"
                        : hop.IsViableHop ? "✅ Viable" : "–";

                    sb.AppendLine($"| `{hop.FromVersion}` → `{hop.ToVersion}` | {v1Text} | {v2Text} | **{hop.Diff.DriftSignal}** | `{removedStr}` | {statusBadge} |");
                }

                sb.AppendLine();
                sb.AppendLine("### 🪟 Window Diagnostics & Capture-Readiness");
                sb.AppendLine();
                sb.AppendLine("| Version | Window Title | Root ClassName | Nodes | Settled | Hydration | Selection Rationale |");
                sb.AppendLine("| :--- | :--- | :--- | :---: | :---: | :---: | :--- |");

                foreach (var v in chain.Versions)
                {
                    var title = string.IsNullOrEmpty(v.WindowTitle) ? "*(empty)*" : $"`{v.WindowTitle}`";
                    var cls = string.IsNullOrEmpty(v.RootClassName) ? "–" : $"`{v.RootClassName}`";
                    var nodes = v.Metrics != null ? v.Metrics.TotalNodes.ToString() : "–";
                    var settled = v.Settled ? $"✅ ({v.SettlePassCount}p)" : "❌ No";
                    var hyd = v.HydrationTimedOut ? "⚠️ Timed Out" : "✅ Ready";
                    var reason = v.WindowSelectionReason ?? "–";
                    if (v.LaunchDiagnostics.Count > 0)
                    {
                        reason += " | " + string.Join(" | ", v.LaunchDiagnostics);
                    }

                    sb.AppendLine($"| `{v.Version}` | {title} | {cls} | {nodes} | {settled} | {hyd} | {reason} |");
                }

                if (chain.DistinctRemovedAutomationIds.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("### 📋 Deduplicated Broken Locators across Chain");
                    sb.AppendLine();
                    foreach (var id in chain.DistinctRemovedAutomationIds)
                    {
                        sb.AppendLine($"- `{id}`");
                    }
                }

                sb.AppendLine();
            }

            sb.AppendLine("---");
            sb.AppendLine("### 🎯 Benchmark Dataset Assessment (#15)");
            sb.AppendLine();

            if (TotalDistinctBrokenLocatorsAcrossAllChains >= 15)
            {
                sb.AppendLine($"> [!TIP]");
                sb.AppendLine($"> **Dataset adequacy verified**: Found **{TotalDistinctBrokenLocatorsAcrossAllChains} distinct broken locators** across all candidate version chains. This provides an organic, maintainer-authored dataset of sufficient scale to host the #15 false-heal rate benchmark.");
            }
            else if (TotalDistinctBrokenLocatorsAcrossAllChains > 0)
            {
                sb.AppendLine($"> [!NOTE]");
                sb.AppendLine($"> **Moderate broken locator dataset**: Found **{TotalDistinctBrokenLocatorsAcrossAllChains} distinct broken locators** across version chains. Usable for initial benchmark scenarios in #15.");
            }
            else
            {
                sb.AppendLine($"> [!IMPORTANT]");
                sb.AppendLine($"> **No distinct broken locators discovered across chains.** Review capture logs and readiness diagnostics above.");
            }

            return sb.ToString();
        }
    }

    public static class OpenSourceAppSurveySerializer
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public static string ToJson(OpenSourceAppSurveyReport report) => JsonSerializer.Serialize(report, Options);

        public static OpenSourceAppSurveyReport FromJson(string json) =>
            JsonSerializer.Deserialize<OpenSourceAppSurveyReport>(json, Options)
            ?? throw new JsonException("Failed to deserialize OpenSourceAppSurveyReport from JSON.");
    }
}
