using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using UiModel;

namespace ScenarioRunner
{
    public sealed class ApplicationTreeMetrics
    {
        public int TotalNodes { get; set; }
        public int MaxDepth { get; set; }
        public int EmptyAutomationIdCount { get; set; }
        public double EmptyAutomationIdFraction { get; set; }
        public int EmptyNameCount { get; set; }
        public double EmptyNameFraction { get; set; }
        public int NeitherIdNorNameCount { get; set; }
        public double NeitherIdNorNameFraction { get; set; }
        public int UnusableBoundingRectangleCount { get; set; }
        public double UnusableBoundingRectangleFraction { get; set; }
        public Dictionary<string, int> ControlTypeDistribution { get; set; } = new();
    }

    public static class TreeMetricsCalculator
    {
        public static ApplicationTreeMetrics Calculate(UiElementInfo? root)
        {
            if (root == null)
            {
                return new ApplicationTreeMetrics();
            }

            var totalNodes = 0;
            var maxDepth = 0;
            var emptyIdCount = 0;
            var emptyNameCount = 0;
            var neitherCount = 0;
            var unusableRectCount = 0;
            var controlTypeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            void Walk(UiElementInfo node, int depth)
            {
                totalNodes++;
                if (depth > maxDepth)
                {
                    maxDepth = depth;
                }

                var hasEmptyId = string.IsNullOrEmpty(node.AutomationId);
                var hasEmptyName = string.IsNullOrEmpty(node.Name);

                if (hasEmptyId) emptyIdCount++;
                if (hasEmptyName) emptyNameCount++;
                if (hasEmptyId && hasEmptyName) neitherCount++;

                var rect = node.BoundingRectangle;
                if (!rect.IsUsable || rect.IsEmpty)
                {
                    unusableRectCount++;
                }

                var cType = string.IsNullOrEmpty(node.ControlType) ? "Unknown" : node.ControlType;
                if (controlTypeCounts.TryGetValue(cType, out var count))
                {
                    controlTypeCounts[cType] = count + 1;
                }
                else
                {
                    controlTypeCounts[cType] = 1;
                }

                foreach (var child in node.Children)
                {
                    Walk(child, depth + 1);
                }
            }

            Walk(root, 0);

            return new ApplicationTreeMetrics
            {
                TotalNodes = totalNodes,
                MaxDepth = maxDepth,
                EmptyAutomationIdCount = emptyIdCount,
                EmptyAutomationIdFraction = totalNodes > 0 ? (double)emptyIdCount / totalNodes : 0.0,
                EmptyNameCount = emptyNameCount,
                EmptyNameFraction = totalNodes > 0 ? (double)emptyNameCount / totalNodes : 0.0,
                NeitherIdNorNameCount = neitherCount,
                NeitherIdNorNameFraction = totalNodes > 0 ? (double)neitherCount / totalNodes : 0.0,
                UnusableBoundingRectangleCount = unusableRectCount,
                UnusableBoundingRectangleFraction = totalNodes > 0 ? (double)unusableRectCount / totalNodes : 0.0,
                ControlTypeDistribution = controlTypeCounts,
            };
        }
    }

    public sealed class ApplicationSurveyRecord
    {
        public string AppName { get; set; } = "";
        public string Executable { get; set; } = "";
        public string Arguments { get; set; } = "";
        public bool Launched { get; set; }
        public string? LaunchError { get; set; }
        public TimeSpan DiscoveryElapsed { get; set; }
        public ApplicationTreeMetrics? Metrics { get; set; }
        public string? TreeJsonFileName { get; set; }
    }

    public sealed class ApplicationSurveyReport
    {
        public int SchemaVersion { get; set; } = 1;
        public string ImageName { get; set; } = "";
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public List<ApplicationSurveyRecord> Applications { get; set; } = new();

        public string ToMarkdownSummary()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"## 🔍 Windows Application Survey: `{ImageName}`");
            sb.AppendLine();
            sb.AppendLine($"**Captured At:** {Timestamp:yyyy-MM-dd HH:mm:ss UTC} | **Total Candidates:** {Applications.Count}");
            sb.AppendLine();
            sb.AppendLine("| Application | Status | Nodes | Max Depth | Empty ID % | Empty Name % | Neither % | Unusable Rect % | Top Control Types | Discovery |");
            sb.AppendLine("| :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :--- | :---: |");

            foreach (var app in Applications)
            {
                if (!app.Launched || app.Metrics == null)
                {
                    var error = string.IsNullOrEmpty(app.LaunchError) ? "Failed to launch" : app.LaunchError;
                    sb.AppendLine($"| `{app.AppName}` | ❌ Not Captured | – | – | – | – | – | – | _{error}_ | – |");
                    continue;
                }

                var m = app.Metrics;
                var topTypes = string.Join(", ", m.ControlTypeDistribution
                    .OrderByDescending(kvp => kvp.Value)
                    .Take(3)
                    .Select(kvp => $"{kvp.Key}:{kvp.Value}"));

                sb.AppendLine(
                    $"| `{app.AppName}` | ✅ Captured | {m.TotalNodes} | {m.MaxDepth} | {ReportFormatting.Percent(m.EmptyAutomationIdFraction)} ({m.EmptyAutomationIdCount}) | {ReportFormatting.Percent(m.EmptyNameFraction)} ({m.EmptyNameCount}) | {ReportFormatting.Percent(m.NeitherIdNorNameFraction)} ({m.NeitherIdNorNameCount}) | {ReportFormatting.Percent(m.UnusableBoundingRectangleFraction)} ({m.UnusableBoundingRectangleCount}) | {topTypes} | {ReportFormatting.Number(app.DiscoveryElapsed.TotalSeconds)}s |");
            }

            return sb.ToString();
        }
    }

    public sealed class ApplicationTreeDiffResult
    {
        public bool HasStructuralDrift { get; set; }
        public string DriftSignal { get; set; } = "";
        public List<string> Details { get; set; } = new();
    }

    public static class ApplicationTreeDiff
    {
        public static ApplicationTreeDiffResult Compare(
            ApplicationSurveyRecord? r2022,
            ApplicationSurveyRecord? r2025,
            UiElementInfo? tree2022,
            UiElementInfo? tree2025)
        {
            var result = new ApplicationTreeDiffResult();

            var launched2022 = r2022?.Launched == true && r2022.Metrics != null;
            var launched2025 = r2025?.Launched == true && r2025.Metrics != null;

            if (!launched2022 && !launched2025)
            {
                result.HasStructuralDrift = false;
                result.DriftSignal = "❌ Both Failed";
                result.Details.Add("Application failed to launch or capture on both images.");
                return result;
            }

            if (launched2022 && !launched2025)
            {
                result.HasStructuralDrift = true;
                result.DriftSignal = "⚠️ 2022 Only";
                result.Details.Add($"Present in windows-2022 ({r2022!.Metrics!.TotalNodes} nodes), but missing/failed in windows-2025 ({r2025?.LaunchError ?? "Not launched"}).");
                return result;
            }

            if (!launched2022 && launched2025)
            {
                result.HasStructuralDrift = true;
                result.DriftSignal = "✨ 2025 Only";
                result.Details.Add($"Present in windows-2025 ({r2025!.Metrics!.TotalNodes} nodes), but missing/failed in windows-2022 ({r2022?.LaunchError ?? "Not launched"}).");
                return result;
            }

            var m2022 = r2022!.Metrics!;
            var m2025 = r2025!.Metrics!;

            var nodeDiff = m2025.TotalNodes - m2022.TotalNodes;
            var depthDiff = m2025.MaxDepth - m2022.MaxDepth;
            var emptyIdDiff = m2025.EmptyAutomationIdCount - m2022.EmptyAutomationIdCount;

            var structuralDifferences = new List<string>();

            if (nodeDiff != 0)
            {
                var sign = nodeDiff > 0 ? "+" : "";
                structuralDifferences.Add($"Total nodes changed: {m2022.TotalNodes} → {m2025.TotalNodes} ({sign}{nodeDiff})");
            }

            if (depthDiff != 0)
            {
                var sign = depthDiff > 0 ? "+" : "";
                structuralDifferences.Add($"Max depth changed: {m2022.MaxDepth} → {m2025.MaxDepth} ({sign}{depthDiff})");
            }

            if (emptyIdDiff != 0)
            {
                var sign = emptyIdDiff > 0 ? "+" : "";
                structuralDifferences.Add($"Empty AutomationId count changed: {m2022.EmptyAutomationIdCount} ({ReportFormatting.Percent(m2022.EmptyAutomationIdFraction)}) → {m2025.EmptyAutomationIdCount} ({ReportFormatting.Percent(m2025.EmptyAutomationIdFraction)}) ({sign}{emptyIdDiff})");
            }

            // Compare ControlType distributions
            var allTypes = new HashSet<string>(m2022.ControlTypeDistribution.Keys, StringComparer.OrdinalIgnoreCase);
            foreach (var k in m2025.ControlTypeDistribution.Keys)
            {
                allTypes.Add(k);
            }

            foreach (var type in allTypes.OrderBy(t => t, StringComparer.OrdinalIgnoreCase))
            {
                m2022.ControlTypeDistribution.TryGetValue(type, out var count2022);
                m2025.ControlTypeDistribution.TryGetValue(type, out var count2025);
                if (count2022 != count2025)
                {
                    var diff = count2025 - count2022;
                    var sign = diff > 0 ? "+" : "";
                    structuralDifferences.Add($"ControlType '{type}': {count2022} → {count2025} ({sign}{diff})");
                }
            }

            // Deep structural comparison if trees are provided
            if (tree2022 != null && tree2025 != null)
            {
                var ids2022 = ExtractAutomationIds(tree2022);
                var ids2025 = ExtractAutomationIds(tree2025);

                var removedIds = ids2022.Where(id => !ids2025.Contains(id)).Take(5).ToList();
                var addedIds = ids2025.Where(id => !ids2022.Contains(id)).Take(5).ToList();

                if (removedIds.Count > 0)
                {
                    structuralDifferences.Add($"AutomationIds removed in 2025: {string.Join(", ", removedIds)}");
                }
                if (addedIds.Count > 0)
                {
                    structuralDifferences.Add($"AutomationIds added in 2025: {string.Join(", ", addedIds)}");
                }
            }

            if (structuralDifferences.Count > 0)
            {
                result.HasStructuralDrift = true;
                var driftSummary = nodeDiff != 0
                    ? $"⚡ Drift ({nodeDiff:+0;-0;0} nodes, {m2022.TotalNodes} → {m2025.TotalNodes})"
                    : "⚡ Drift (Structural/Attributes)";
                result.DriftSignal = driftSummary;
                result.Details = structuralDifferences;
            }
            else
            {
                result.HasStructuralDrift = false;
                result.DriftSignal = $"Identical ({m2022.TotalNodes} nodes)";
                result.Details.Add("UI trees and metric distributions are structurally identical across both images.");
            }

            return result;
        }

        private static HashSet<string> ExtractAutomationIds(UiElementInfo root)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            void Collect(UiElementInfo node)
            {
                if (!string.IsNullOrEmpty(node.AutomationId))
                {
                    set.Add(node.AutomationId);
                }
                foreach (var child in node.Children)
                {
                    Collect(child);
                }
            }
            Collect(root);
            return set;
        }
    }

    public static class CrossImageSurveyComparison
    {
        public static string GenerateComparisonMarkdown(
            ApplicationSurveyReport report2022,
            ApplicationSurveyReport report2025,
            Func<string, string, UiElementInfo?>? treeLoader = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# 🏛️ Windows Runner Images Application Benchmark Survey (Issue #64)");
            sb.AppendLine();
            sb.AppendLine($"Comparison between **`{report2022.ImageName}`** ({report2022.Timestamp:yyyy-MM-dd}) and **`{report2025.ImageName}`** ({report2025.Timestamp:yyyy-MM-dd}).");
            sb.AppendLine();

            var map2022 = report2022.Applications.ToDictionary(a => a.AppName, a => a, StringComparer.OrdinalIgnoreCase);
            var map2025 = report2025.Applications.ToDictionary(a => a.AppName, a => a, StringComparer.OrdinalIgnoreCase);

            var allAppNames = new List<string>(map2022.Keys);
            foreach (var k in map2025.Keys)
            {
                if (!map2022.ContainsKey(k))
                {
                    allAppNames.Add(k);
                }
            }

            sb.AppendLine("| Candidate Application | windows-2022 (Nodes / Empty ID% / Depth) | windows-2025 (Nodes / Empty ID% / Depth) | Drift Signal | Structural Differences Summary |");
            sb.AppendLine("| :--- | :--- | :--- | :---: | :--- |");

            var candidateDriftList = new List<(string AppName, string Signal, ApplicationTreeDiffResult Diff)>();

            foreach (var appName in allAppNames)
            {
                map2022.TryGetValue(appName, out var r2022);
                map2025.TryGetValue(appName, out var r2025);

                UiElementInfo? t2022 = null;
                UiElementInfo? t2025 = null;
                if (treeLoader != null)
                {
                    if (r2022?.TreeJsonFileName != null) t2022 = treeLoader("windows-2022", r2022.TreeJsonFileName);
                    if (r2025?.TreeJsonFileName != null) t2025 = treeLoader("windows-2025", r2025.TreeJsonFileName);
                }

                var diff = ApplicationTreeDiff.Compare(r2022, r2025, t2022, t2025);
                candidateDriftList.Add((appName, diff.DriftSignal, diff));

                var col2022 = r2022?.Launched == true && r2022.Metrics != null
                    ? $"{r2022.Metrics.TotalNodes} nodes | {ReportFormatting.Percent(r2022.Metrics.EmptyAutomationIdFraction, 0)} empty ID | depth {r2022.Metrics.MaxDepth}"
                    : $"❌ {r2022?.LaunchError ?? "Not launched"}";

                var col2025 = r2025?.Launched == true && r2025.Metrics != null
                    ? $"{r2025.Metrics.TotalNodes} nodes | {ReportFormatting.Percent(r2025.Metrics.EmptyAutomationIdFraction, 0)} empty ID | depth {r2025.Metrics.MaxDepth}"
                    : $"❌ {r2025?.LaunchError ?? "Not launched"}";

                var detailsSummary = diff.Details.Count > 0 ? string.Join("; ", diff.Details) : "–";
                sb.AppendLine($"| `{appName}` | {col2022} | {col2025} | **{diff.DriftSignal}** | {detailsSummary} |");
            }

            sb.AppendLine();
            sb.AppendLine("### 🎯 Benchmark Target Assessment (#15)");
            sb.AppendLine();

            var driftingApps = candidateDriftList.Where(c => c.Diff.HasStructuralDrift).ToList();
            if (driftingApps.Count > 0)
            {
                sb.AppendLine($"Found **{driftingApps.Count} candidate(s)** with verified organic drift across runner images:");
                foreach (var d in driftingApps)
                {
                    sb.AppendLine($"- **`{d.AppName}`**: {d.Signal} - {string.Join("; ", d.Diff.Details)}");
                }
                sb.AppendLine();
                sb.AppendLine("> [!TIP]");
                sb.AppendLine("> These applications have organic Microsoft-authored UI drift between Server 2022 and 2025, making them viable zero-cost targets for the false-heal rate benchmark in #15.");
            }
            else
            {
                sb.AppendLine("> [!IMPORTANT]");
                sb.AppendLine("> **No structural drift detected across runner images.** Server SKUs are conservative; all tested candidate applications remained structurally identical. Per #64 design decision, #15 will need an external organic drift source (such as two released versions of a portable open-source WinForms/WPF app).");
            }

            return sb.ToString();
        }
    }

    public static class ApplicationSurveySerializer
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public static string ToJson(ApplicationSurveyReport report) => JsonSerializer.Serialize(report, Options);

        public static ApplicationSurveyReport FromJson(string json) =>
            JsonSerializer.Deserialize<ApplicationSurveyReport>(json, Options)
            ?? throw new JsonException("Failed to deserialize ApplicationSurveyReport from JSON.");
    }
}
