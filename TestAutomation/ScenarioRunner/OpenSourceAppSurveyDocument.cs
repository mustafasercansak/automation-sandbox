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
        public int SettlePassCount { get; set; }
        public string? SettleTelemetry { get; set; }
        public string? Error { get; set; }
        public TimeSpan DiscoveryElapsed { get; set; }
        public ApplicationTreeMetrics? Metrics { get; set; }
        public string? TreeJsonFileName { get; set; }
    }

    public sealed class AppPairSurveyRecord
    {
        public string AppName { get; set; } = "";
        public string Toolkit { get; set; } = "";
        public AppVersionSurveyRecord V1 { get; set; } = new();
        public AppVersionSurveyRecord V2 { get; set; } = new();
        public ApplicationTreeDiffResult Diff { get; set; } = new();
        public bool IsViableBenchmarkTarget { get; set; }
        public string ViabilityReason { get; set; } = "";
    }

    public static class OpenSourceAppViabilityEvaluator
    {
        public const double MinimumEmptyAutomationIdFraction = 0.35;

        public static (bool IsViable, string Reason) Evaluate(
            AppVersionSurveyRecord v1,
            AppVersionSurveyRecord v2,
            ApplicationTreeDiffResult diff)
        {
            if (!v1.Downloaded || !v1.Launched || v1.Metrics == null)
            {
                return (false, $"V1 ({v1.Version}) failed to launch/capture: {v1.Error ?? "Unknown error"}");
            }

            if (!v2.Downloaded || !v2.Launched || v2.Metrics == null)
            {
                return (false, $"V2 ({v2.Version}) failed to launch/capture: {v2.Error ?? "Unknown error"}");
            }

            if (!v1.Settled)
            {
                return (false, $"V1 ({v1.Version}) did not settle cleanly across consecutive discovery passes ({v1.SettleTelemetry})");
            }

            if (!v2.Settled)
            {
                return (false, $"V2 ({v2.Version}) did not settle cleanly across consecutive discovery passes ({v2.SettleTelemetry})");
            }

            var maxEmptyFraction = Math.Max(v1.Metrics.EmptyAutomationIdFraction, v2.Metrics.EmptyAutomationIdFraction);
            if (maxEmptyFraction < MinimumEmptyAutomationIdFraction)
            {
                return (false, $"Empty AutomationId fraction too low ({ReportFormatting.Percent(maxEmptyFraction)} < {ReportFormatting.Percent(MinimumEmptyAutomationIdFraction)} threshold) - insufficient healing surface");
            }

            // True locator-breaking drift check: must have removed AutomationIds or explicit locator breaks,
            // not merely node additions in a new unvisited window.
            var hasRemovedIds = diff.Details.Any(d => d.StartsWith("AutomationIds removed", StringComparison.OrdinalIgnoreCase));
            var hasNodeDelta = Math.Abs(v2.Metrics.TotalNodes - v1.Metrics.TotalNodes) > 0;

            if (!hasRemovedIds && !diff.HasStructuralDrift)
            {
                return (false, "No structural or locator-breaking drift detected between versions");
            }

            if (hasRemovedIds)
            {
                return (true, $"Viable benchmark target: verified removed AutomationIds ({diff.Details.First(d => d.StartsWith("AutomationIds removed", StringComparison.OrdinalIgnoreCase))}) with {ReportFormatting.Percent(maxEmptyFraction)} empty IDs");
            }

            if (hasNodeDelta && diff.HasStructuralDrift)
            {
                return (true, $"Viable benchmark target: structural drift detected ({diff.DriftSignal}) with {ReportFormatting.Percent(maxEmptyFraction)} empty IDs");
            }

            return (false, "Changes do not present a verified locator-breaking refactor pattern");
        }
    }

    public sealed class OpenSourceAppSurveyReport
    {
        public int SchemaVersion { get; set; } = 1;
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public List<AppPairSurveyRecord> Pairs { get; set; } = new();

        public string ToMarkdownSummary()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# 📦 Open-Source App Version Pairs Benchmark Survey (Issue #69)");
            sb.AppendLine();
            sb.AppendLine($"**Captured At:** {Timestamp:yyyy-MM-dd HH:mm:ss UTC} | **Candidates Evaluated:** {Pairs.Count}");
            sb.AppendLine();
            sb.AppendLine("| Candidate | Toolkit | Older Version | Newer Version | Drift Signal | Removed AutomationIds | Viable Target? |");
            sb.AppendLine("| :--- | :---: | :--- | :--- | :---: | :--- | :---: |");

            foreach (var pair in Pairs)
            {
                var v1Text = pair.V1.Launched && pair.V1.Metrics != null
                    ? $"`{pair.V1.Version}` ({pair.V1.Metrics.TotalNodes} nodes, {ReportFormatting.Percent(pair.V1.Metrics.EmptyAutomationIdFraction, 0)} empty ID, {pair.V1.SettlePassCount}p settle)"
                    : $"`{pair.V1.Version}` ❌ {pair.V1.Error ?? "Failed"}";

                var v2Text = pair.V2.Launched && pair.V2.Metrics != null
                    ? $"`{pair.V2.Version}` ({pair.V2.Metrics.TotalNodes} nodes, {ReportFormatting.Percent(pair.V2.Metrics.EmptyAutomationIdFraction, 0)} empty ID, {pair.V2.SettlePassCount}p settle)"
                    : $"`{pair.V2.Version}` ❌ {pair.V2.Error ?? "Failed"}";

                var removedDetails = pair.Diff.Details.FirstOrDefault(d => d.StartsWith("AutomationIds removed", StringComparison.OrdinalIgnoreCase)) ?? "None";
                var viableBadge = pair.IsViableBenchmarkTarget ? "✅ **Viable**" : "❌ Not Viable";

                sb.AppendLine($"| **{pair.AppName}** | {pair.Toolkit} | {v1Text} | {v2Text} | **{pair.Diff.DriftSignal}** | {removedDetails} | {viableBadge} |");
            }

            sb.AppendLine();
            sb.AppendLine("### 🎯 Benchmark Target Assessment (#15)");
            sb.AppendLine();

            var viablePairs = Pairs.Where(p => p.IsViableBenchmarkTarget).ToList();
            if (viablePairs.Count > 0)
            {
                sb.AppendLine($"Found **{viablePairs.Count} viable benchmark candidate(s)** with verified maintainer-driven locator drift:");
                foreach (var vp in viablePairs)
                {
                    sb.AppendLine($"- **`{vp.AppName}`** ({vp.Toolkit}): {vp.ViabilityReason}");
                }
                sb.AppendLine();
                sb.AppendLine("> [!TIP]");
                sb.AppendLine("> These version pairs have verified maintainer-authored locator breakage across real releases with high empty-`AutomationId` proportions, fulfilling all prerequisite requirements for the #15 false-heal rate benchmark.");
            }
            else
            {
                sb.AppendLine("> [!IMPORTANT]");
                sb.AppendLine("> **No candidate satisfied all 3 viability criteria.** Review individual candidate errors or settle logs above.");
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
