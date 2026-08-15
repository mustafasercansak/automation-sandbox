using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using UiModel;

namespace ScenarioRunner
{
    public sealed class SurveyLocatorElementRecord
    {
        public string AutomationId { get; set; } = "";
        public string ControlType { get; set; } = "";
        public string Name { get; set; } = "";
        public string ClassName { get; set; } = "";

        // Human-readable breadcrumb for the report, e.g. Window['ShareX 15.0 Portable'] > Table['DataGridView'].
        public string AncestorPath { get; set; } = "";

        // The same chain as ControlTypes only, root first. Classification matches against this rather than
        // against AncestorPath: the readable path embeds element names, and a name can contain a control
        // type as a substring — "ShareX 15.0 Portable" contains "table", which made every ShareX element
        // look like a grid descendant.
        public List<string> AncestorControlTypes { get; set; } = new();

        public bool IsExcluded { get; set; }
        public string? ExclusionReason { get; set; }
    }

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

        // Set by EvaluateChain once every version in the chain is known: a capture far below the chain's
        // typical size is a dialog, an error window or an unhydrated tree, whichever direction it is
        // compared in. Hops touching one are excluded from the dataset.
        public bool IsUnreliableCapture { get; set; }
        public string? CaptureReliabilityReason { get; set; }

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

        // Valid, developer-authored broken locators (unexcluded)
        public List<SurveyLocatorElementRecord> RemovedLocators { get; set; } = new();
        public List<SurveyLocatorElementRecord> AddedLocators { get; set; } = new();

        // Audited volatile per-instance ids that were excluded
        public List<SurveyLocatorElementRecord> ExcludedLocators { get; set; } = new();

        // String projection for backward compatibility and concise logging
        [JsonIgnore]
        public List<string> RemovedAutomationIds => RemovedLocators.Select(r => r.AutomationId).ToList();

        [JsonIgnore]
        public List<string> AddedAutomationIds => AddedLocators.Select(a => a.AutomationId).ToList();

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
            Hops.Where(h => h.IsViableHop).Sum(h => h.RemovedLocators.Count);

        public int TotalExcludedLocatorsCount =>
            Hops.Sum(h => h.ExcludedLocators.Count);

        public bool IsViableBenchmarkTarget { get; set; }
        public string BenchmarkRecommendation { get; set; } = "";
    }

    public static class VolatileLocatorClassifier
    {
        // Generated accessibility name patterns on dynamic grid cells/headers
        private static readonly Regex GeneratedRowPattern = new(@"\bRow\s+\d+\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex GeneratedColPattern = new(@"\bColumn\s+\d+\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // UIA control types whose children are rows and cells generated from data rather than authored
        // by a developer. Matched as whole control types, never as substrings of a path.
        private static readonly HashSet<string> DynamicContainerControlTypes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Table",
                "DataGrid",
                "DataGridView",
                "DataItem",
                "List",
                "ListView",
                "Tree",
            };

        public static bool IsVolatile(SurveyLocatorElementRecord element, out string reason)
        {
            if (element.AncestorControlTypes.Count == 0)
            {
                reason = "No ancestor metadata available";
                return false;
            }

            var containerType = element.AncestorControlTypes
                .FirstOrDefault(t => DynamicContainerControlTypes.Contains(t));
            var isContainerDescendant = containerType != null;

            if (!isContainerDescendant)
            {
                reason = "Not a dynamic container descendant";
                return false;
            }

            // NumberStyles.None + invariant culture: an id is "numeric" only when it is bare digits.
            // Leading signs, whitespace and culture-specific separators must not qualify.
            var isNumericId = long.TryParse(
                element.AutomationId,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out _);
            var name = element.Name ?? "";

            var isGeneratedName =
                GeneratedRowPattern.IsMatch(name) ||
                GeneratedColPattern.IsMatch(name) ||
                name.IndexOf("Top Row", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Not sorted", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Header Row", StringComparison.OrdinalIgnoreCase) >= 0;

            if (isNumericId && isGeneratedName)
            {
                reason = $"Numeric id ({element.AutomationId}) under {containerType} with generated accessibility name ('{name}')";
                return true;
            }

            if (isNumericId)
            {
                reason = $"Purely numeric id ({element.AutomationId}) on a {element.ControlType} under {containerType}";
                return true;
            }

            if (isGeneratedName)
            {
                reason = $"Generated accessibility cell name ('{name}') under {containerType}";
                return true;
            }

            reason = "Container descendant but carries non-generated named locator";
            return false;
        }
    }

    public static class SurveyTreeExtractor
    {
        public static List<SurveyLocatorElementRecord> ExtractLocatorElements(UiElementInfo? root)
        {
            var list = new List<SurveyLocatorElementRecord>();
            if (root == null) return list;

            void Walk(UiElementInfo node, string currentPath, List<string> ancestorTypes)
            {
                var cType = string.IsNullOrEmpty(node.ControlType) ? "Unknown" : node.ControlType;
                var nodeDesc = string.IsNullOrEmpty(node.Name)
                    ? cType
                    : $"{cType}['{node.Name}']";

                var newPath = string.IsNullOrEmpty(currentPath) ? nodeDesc : $"{currentPath} > {nodeDesc}";

                if (!string.IsNullOrEmpty(node.AutomationId))
                {
                    list.Add(new SurveyLocatorElementRecord
                    {
                        AutomationId = node.AutomationId,
                        ControlType = cType,
                        Name = node.Name ?? "",
                        ClassName = node.ClassName ?? "",
                        AncestorPath = currentPath,

                        // Copied, not shared: each record keeps the chain as it stood at its own depth.
                        AncestorControlTypes = new List<string>(ancestorTypes),
                    });
                }

                if (node.Children.Count == 0)
                {
                    return;
                }

                ancestorTypes.Add(cType);
                foreach (var child in node.Children)
                {
                    Walk(child, newPath, ancestorTypes);
                }

                ancestorTypes.RemoveAt(ancestorTypes.Count - 1);
            }

            Walk(root, "", new List<string>());
            return list;
        }
    }

    public static class OpenSourceAppViabilityEvaluator
    {
        public const double MinimumEmptyAutomationIdFraction = 0.35;
        public const double MaxAllowedNodeDropRatio = 3.0;

        // A capture smaller than this fraction of its chain's median is not the application.
        public const double MinimumCaptureNodeFractionOfMedian = 1.0 / 3.0;

        public static AppHopSurveyRecord EvaluateHop(
            AppVersionSurveyRecord v1,
            AppVersionSurveyRecord v2,
            ApplicationTreeDiffResult diff,
            UiElementInfo? tree1 = null,
            UiElementInfo? tree2 = null)
        {
            var hop = new AppHopSurveyRecord
            {
                FromVersion = v1.Version,
                ToVersion = v2.Version,
                V1 = v1,
                V2 = v2,
                Diff = diff,
            };

            // 1. Extract and classify locators
            if (tree1 != null && tree2 != null)
            {
                var elementsV1 = SurveyTreeExtractor.ExtractLocatorElements(tree1);
                var elementsV2 = SurveyTreeExtractor.ExtractLocatorElements(tree2);

                var mapV2 = elementsV2
                    .GroupBy(e => e.AutomationId, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

                var mapV1 = elementsV1
                    .GroupBy(e => e.AutomationId, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

                // Check removed locators (in V1 but missing in V2)
                foreach (var el in elementsV1)
                {
                    if (!mapV2.ContainsKey(el.AutomationId))
                    {
                        if (VolatileLocatorClassifier.IsVolatile(el, out var reason))
                        {
                            el.IsExcluded = true;
                            el.ExclusionReason = reason;
                            hop.ExcludedLocators.Add(el);
                        }
                        else
                        {
                            hop.RemovedLocators.Add(el);
                        }
                    }
                }

                // Check added locators (in V2 but missing in V1)
                foreach (var el in elementsV2)
                {
                    if (!mapV1.ContainsKey(el.AutomationId))
                    {
                        if (VolatileLocatorClassifier.IsVolatile(el, out var reason))
                        {
                            el.IsExcluded = true;
                            el.ExclusionReason = reason;
                            hop.ExcludedLocators.Add(el);
                        }
                        else
                        {
                            hop.AddedLocators.Add(el);
                        }
                    }
                }
            }
            else
            {
                // Fallback for tree-less synthetic tests: parse diff.Details bare strings and default to unexcluded
                var removedIds = new List<string>();
                var addedIds = new List<string>();
                ExtractAutomationIdDeltas(diff, removedIds, addedIds);

                foreach (var id in removedIds)
                {
                    hop.RemovedLocators.Add(new SurveyLocatorElementRecord
                    {
                        AutomationId = id,
                        ControlType = "Unknown",
                    });
                }

                foreach (var id in addedIds)
                {
                    hop.AddedLocators.Add(new SurveyLocatorElementRecord
                    {
                        AutomationId = id,
                        ControlType = "Unknown",
                    });
                }
            }

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

            if (hop.RemovedLocators.Count > 0)
            {
                hop.IsViableHop = true;
                hop.ViabilityReason = $"Viable hop: {hop.RemovedLocators.Count} valid removed AutomationId(s) [{string.Join(", ", hop.RemovedLocators.Select(r => r.AutomationId))}] with {ReportFormatting.Percent(maxEmptyFraction)} empty IDs";
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
            MarkUnreliableCaptures(chain);
            InvalidateHopsTouchingUnreliableCaptures(chain);

            var distinctRemovedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var hop in chain.Hops)
            {
                if (!hop.IsViableHop)
                {
                    continue;
                }

                foreach (var loc in hop.RemovedLocators)
                {
                    distinctRemovedIds.Add(loc.AutomationId);
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

        private static void MarkUnreliableCaptures(AppChainSurveyRecord chain)
        {
            foreach (var version in chain.Versions)
            {
                version.IsUnreliableCapture = false;
                version.CaptureReliabilityReason = null;
            }

            var captured = chain.Versions
                .Where(v => v.Launched && v.Metrics != null && v.Metrics.TotalNodes > 0)
                .ToList();

            if (captured.Count == 0)
            {
                return;
            }

            var median = MedianNodeCount(captured.Select(v => v.Metrics!.TotalNodes));
            var floor = median * MinimumCaptureNodeFractionOfMedian;

            foreach (var version in captured)
            {
                var nodes = version.Metrics!.TotalNodes;

                if (version.HydrationTimedOut)
                {
                    version.IsUnreliableCapture = true;
                    version.CaptureReliabilityReason = $"Hydration timed out at {nodes} node(s)";
                    continue;
                }

                if (nodes < floor)
                {
                    version.IsUnreliableCapture = true;
                    version.CaptureReliabilityReason =
                        $"Capture of {nodes} node(s) is below {ReportFormatting.Number(floor, 1)}, " +
                        $"a third of the {ReportFormatting.Number(median, 1)} node chain median";
                }
            }
        }

        private static void InvalidateHopsTouchingUnreliableCaptures(AppChainSurveyRecord chain)
        {
            foreach (var hop in chain.Hops)
            {
                var culprit = hop.V1.IsUnreliableCapture ? hop.V1
                    : hop.V2.IsUnreliableCapture ? hop.V2
                    : null;

                if (culprit == null)
                {
                    continue;
                }

                hop.IsSuspectCapture = true;
                hop.SuspectReason = $"Touches unreliable capture {culprit.Version}: {culprit.CaptureReliabilityReason}";
                hop.IsViableHop = false;
                hop.ViabilityReason = $"Rejected: {hop.SuspectReason}";
            }
        }

        private static double MedianNodeCount(IEnumerable<int> nodeCounts)
        {
            var ordered = nodeCounts.OrderBy(n => n).ToList();
            var middle = ordered.Count / 2;

            return ordered.Count % 2 == 1
                ? ordered[middle]
                : (ordered[middle - 1] + ordered[middle]) / 2.0;
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
        public int SchemaVersion { get; set; } = 3;
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public List<AppChainSurveyRecord> Chains { get; set; } = new();

        public int TotalDistinctBrokenLocatorsAcrossAllChains =>
            Chains.Sum(c => c.TotalDistinctBrokenLocatorsCount);

        public int TotalExcludedLocatorsAcrossAllChains =>
            Chains.Sum(c => c.TotalExcludedLocatorsCount);

        public string ToMarkdownSummary()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# 📦 Open-Source App Version Chains Benchmark Survey (Issue #71 & #78)");
            sb.AppendLine();
            sb.AppendLine($"**Captured At:** {Timestamp:yyyy-MM-dd HH:mm:ss UTC} | **Chains Evaluated:** {Chains.Count} | **Total Distinct Broken Locators:** **{TotalDistinctBrokenLocatorsAcrossAllChains}** | **Excluded Volatile Identifiers:** {TotalExcludedLocatorsAcrossAllChains}");
            sb.AppendLine();

            foreach (var chain in Chains)
            {
                sb.AppendLine($"## 🔗 `{chain.AppName}` Version Chain ({chain.Toolkit})");
                sb.AppendLine();
                sb.AppendLine($"- **Total Releases Probed:** {chain.Versions.Count}");
                sb.AppendLine($"- **Consecutive Hops:** {chain.Hops.Count}");
                sb.AppendLine($"- **Distinct Broken Locators (Deduplicated):** **{chain.TotalDistinctBrokenLocatorsCount}**");
                sb.AppendLine($"- **Cumulative Broken Locators:** {chain.TotalCumulativeBrokenLocatorsCount}");
                sb.AppendLine($"- **Excluded Volatile Identifiers:** {chain.TotalExcludedLocatorsCount}");
                sb.AppendLine($"- **Viable Benchmark Target:** {(chain.IsViableBenchmarkTarget ? "✅ **Yes**" : "❌ No")} ({chain.BenchmarkRecommendation})");
                sb.AppendLine();

                sb.AppendLine("### 🔀 Consecutive Hop Drift Telemetry");
                sb.AppendLine();
                sb.AppendLine("| Hop | Older Version | Newer Version | Drift Signal | Valid Broken Locators | Excluded Volatile IDs | Status |");
                sb.AppendLine("| :--- | :--- | :--- | :---: | :--- | :---: | :---: |");

                foreach (var hop in chain.Hops)
                {
                    var v1Text = hop.V1.Launched && hop.V1.Metrics != null
                        ? $"`{hop.V1.Version}` ({hop.V1.Metrics.TotalNodes} nodes, {ReportFormatting.Percent(hop.V1.Metrics.EmptyAutomationIdFraction, 0)} empty ID)"
                        : $"`{hop.V1.Version}` ❌ {hop.V1.Error ?? "Failed"}";

                    var v2Text = hop.V2.Launched && hop.V2.Metrics != null
                        ? $"`{hop.V2.Version}` ({hop.V2.Metrics.TotalNodes} nodes, {ReportFormatting.Percent(hop.V2.Metrics.EmptyAutomationIdFraction, 0)} empty ID)"
                        : $"`{hop.V2.Version}` ❌ {hop.V2.Error ?? "Failed"}";

                    var removedStr = hop.RemovedLocators.Count > 0
                        ? string.Join(", ", hop.RemovedLocators.Select(r => $"`{r.AutomationId}` ({r.ControlType})"))
                        : "–";

                    var excludedStr = hop.ExcludedLocators.Count > 0
                        ? $"{hop.ExcludedLocators.Count} excluded"
                        : "0";

                    var statusBadge = hop.IsSuspectCapture
                        ? $"⚠️ Suspect ({hop.SuspectReason})"
                        : hop.IsViableHop ? "✅ Viable" : "–";

                    sb.AppendLine($"| `{hop.FromVersion}` → `{hop.ToVersion}` | {v1Text} | {v2Text} | **{hop.Diff.DriftSignal}** | {removedStr} | {excludedStr} | {statusBadge} |");
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
                    if (v.IsUnreliableCapture)
                    {
                        reason += $" | ⚠️ Unreliable capture: {v.CaptureReliabilityReason}";
                    }

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
                        var loc = chain.Hops.SelectMany(h => h.RemovedLocators).FirstOrDefault(l => l.AutomationId == id);
                        var desc = loc != null && !string.IsNullOrEmpty(loc.Name) ? $" (`{loc.ControlType}`, Name: '{loc.Name}', Path: `{loc.AncestorPath}`)" : "";
                        sb.AppendLine($"- `{id}`{desc}");
                    }
                }

                var allExcluded = chain.Hops.SelectMany(h => h.ExcludedLocators).ToList();
                if (allExcluded.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("### 🛡️ Audited Excluded Volatile Identifiers");
                    sb.AppendLine();
                    sb.AppendLine("| Identifier | ControlType | Name | Ancestor Path | Reason for Exclusion |");
                    sb.AppendLine("| :--- | :---: | :--- | :--- | :--- |");

                    foreach (var ex in allExcluded)
                    {
                        var name = string.IsNullOrEmpty(ex.Name) ? "*(empty)*" : $"'{ex.Name}'";
                        var path = string.IsNullOrEmpty(ex.AncestorPath) ? "–" : $"`{ex.AncestorPath}`";
                        sb.AppendLine($"| `{ex.AutomationId}` | `{ex.ControlType}` | {name} | {path} | _{ex.ExclusionReason}_ |");
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
                sb.AppendLine($"> **Dataset adequacy verified**: Found **{TotalDistinctBrokenLocatorsAcrossAllChains} distinct broken locators** across candidate version chains (excluding {TotalExcludedLocatorsAcrossAllChains} volatile IDs). This provides an organic, maintainer-authored dataset of sufficient scale to host the #15 false-heal rate benchmark.");
            }
            else if (TotalDistinctBrokenLocatorsAcrossAllChains > 0)
            {
                sb.AppendLine($"> [!NOTE]");
                sb.AppendLine($"> **Moderate broken locator dataset**: Found **{TotalDistinctBrokenLocatorsAcrossAllChains} distinct broken locators** across version chains (excluding {TotalExcludedLocatorsAcrossAllChains} volatile IDs). Usable for initial benchmark scenarios in #15.");
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
