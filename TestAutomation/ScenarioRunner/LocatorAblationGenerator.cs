using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UiModel;

namespace ScenarioRunner
{
    // Turns a captured UI tree into ground-truth benchmark scenarios by breaking one locator at a time.
    public static class LocatorAblationGenerator
    {
        public const string RenameSuffix = "_ablated";

        public static LocatorAblationDataset Generate(
            UiElementInfo sourceRoot,
            string applicationName,
            string sourceVersion,
            string sourceTreeFileName)
        {
            if (sourceRoot == null)
            {
                throw new ArgumentNullException(nameof(sourceRoot));
            }

            var dataset = new LocatorAblationDataset();
            var targets = EnumerateLocatorTargets(sourceRoot);

            foreach (var target in targets)
            {
                // A rename keeps the element, so the correct answer is that element.
                dataset.Scenarios.Add(new LocatorAblationScenario
                {
                    ScenarioId = BuildScenarioId(applicationName, sourceVersion, target.AutomationId, LocatorMutationKind.RenamedAutomationId),
                    ApplicationName = applicationName,
                    SourceVersion = sourceVersion,
                    SourceTreeFileName = sourceTreeFileName,
                    MutationKind = LocatorMutationKind.RenamedAutomationId,
                    ExpectedOutcome = LocatorExpectedOutcome.Successor,
                    OriginalAutomationId = target.AutomationId,
                    MutatedAutomationId = target.AutomationId + RenameSuffix,
                    GroundTruth = Fingerprint(target.Element, target.AncestorPath),
                });

                // A removal takes the element away, so the correct answer is to decline. Without these
                // the harness could only measure recall, and a resolver that heals everything would
                // score perfectly.
                dataset.Scenarios.Add(new LocatorAblationScenario
                {
                    ScenarioId = BuildScenarioId(applicationName, sourceVersion, target.AutomationId, LocatorMutationKind.RemovedElement),
                    ApplicationName = applicationName,
                    SourceVersion = sourceVersion,
                    SourceTreeFileName = sourceTreeFileName,
                    MutationKind = LocatorMutationKind.RemovedElement,
                    ExpectedOutcome = LocatorExpectedOutcome.NoSuccessor,
                    OriginalAutomationId = target.AutomationId,
                    MutatedAutomationId = null,
                    GroundTruth = null,
                });
            }

            return dataset;
        }

        // The element as the test knew it before the break: the snapshot a locator repository would hold.
        public static UiElementInfo? FindExpectedElement(UiElementInfo sourceRoot, string automationId)
        {
            return EnumerateLocatorTargets(sourceRoot)
                .Where(t => string.Equals(t.AutomationId, automationId, StringComparison.Ordinal))
                .Select(t => Clone(t.Element))
                .FirstOrDefault();
        }

        // Rebuilds the broken tree from the recipe. Deterministic: the same scenario always produces
        // the same tree, which is what makes results reproducible without re-capturing the application.
        public static UiElementInfo ApplyMutation(UiElementInfo sourceRoot, LocatorAblationScenario scenario)
        {
            if (sourceRoot == null)
            {
                throw new ArgumentNullException(nameof(sourceRoot));
            }

            if (scenario == null)
            {
                throw new ArgumentNullException(nameof(scenario));
            }

            var clone = Clone(sourceRoot);

            switch (scenario.MutationKind)
            {
                case LocatorMutationKind.RenamedAutomationId:
                    var renamed = RenameFirst(clone, scenario.OriginalAutomationId, scenario.MutatedAutomationId ?? "");
                    if (!renamed)
                    {
                        throw new InvalidOperationException(
                            $"Scenario '{scenario.ScenarioId}' targets AutomationId '{scenario.OriginalAutomationId}', which is not in the source tree.");
                    }

                    return clone;

                case LocatorMutationKind.RemovedElement:
                    if (string.Equals(clone.AutomationId, scenario.OriginalAutomationId, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Scenario '{scenario.ScenarioId}' would remove the root element, leaving no tree to search.");
                    }

                    if (!RemoveFirst(clone, scenario.OriginalAutomationId))
                    {
                        throw new InvalidOperationException(
                            $"Scenario '{scenario.ScenarioId}' targets AutomationId '{scenario.OriginalAutomationId}', which is not in the source tree.");
                    }

                    return clone;

                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario), scenario.MutationKind, "Unknown mutation kind.");
            }
        }

        // Walks the mutated tree the same way the generator walked the source, so a fingerprint recorded
        // at generation time can be compared against a candidate the resolver returned.
        public static string AncestorPathOf(UiElementInfo root, UiElementInfo element)
        {
            string? found = null;

            void Walk(UiElementInfo node, string path)
            {
                if (found != null)
                {
                    return;
                }

                if (ReferenceEquals(node, element))
                {
                    found = path;
                    return;
                }

                var next = Append(path, node);
                foreach (var child in node.Children)
                {
                    Walk(child, next);
                }
            }

            Walk(root, "");
            return found ?? "";
        }

        public static ElementFingerprint Fingerprint(UiElementInfo element, string ancestorPath) =>
            new ElementFingerprint
            {
                ControlType = element.ControlType ?? "",
                Name = element.Name ?? "",
                ClassName = element.ClassName ?? "",
                AncestorPath = ancestorPath,
            };

        private static string BuildScenarioId(string app, string version, string automationId, LocatorMutationKind kind) =>
            string.Format(
                CultureInfo.InvariantCulture,
                "{0}@{1}#{2}#{3}",
                app,
                version,
                automationId,
                kind == LocatorMutationKind.RenamedAutomationId ? "rename" : "remove");

        private sealed class LocatorTarget
        {
            public UiElementInfo Element { get; set; } = new UiElementInfo();
            public string AutomationId { get; set; } = "";
            public string AncestorPath { get; set; } = "";
        }

        private static List<LocatorTarget> EnumerateLocatorTargets(UiElementInfo root)
        {
            var targets = new List<LocatorTarget>();

            void Walk(UiElementInfo node, string path, bool isRoot)
            {
                var id = node.AutomationId ?? "";

                // The root is skipped: removing it leaves nothing to search, so it cannot carry a
                // matched pair of scenarios. Duplicate ids are skipped because a mutation targeting one
                // of them would have an ambiguous ground truth.
                // Every sighting is collected; duplicates are dropped below, after the walk. Skipping
                // the second sighting here would leave the first one looking unique.
                if (!isRoot && !string.IsNullOrEmpty(id))
                {
                    targets.Add(new LocatorTarget { Element = node, AutomationId = id, AncestorPath = path });
                }

                var next = Append(path, node);
                foreach (var child in node.Children)
                {
                    Walk(child, next, isRoot: false);
                }
            }

            Walk(root, "", isRoot: true);

            // An id that appeared more than once was added on its first sighting; drop it entirely.
            var duplicated = targets
                .GroupBy(t => t.AutomationId, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            return targets.Where(t => !duplicated.Contains(t.AutomationId, StringComparer.Ordinal)).ToList();
        }

        private static string Append(string path, UiElementInfo node)
        {
            var controlType = string.IsNullOrEmpty(node.ControlType) ? "Unknown" : node.ControlType;
            var descriptor = string.IsNullOrEmpty(node.Name) ? controlType : $"{controlType}['{node.Name}']";
            return string.IsNullOrEmpty(path) ? descriptor : path + " > " + descriptor;
        }

        private static bool RenameFirst(UiElementInfo node, string automationId, string replacement)
        {
            if (string.Equals(node.AutomationId ?? "", automationId, StringComparison.Ordinal))
            {
                node.AutomationId = replacement;
                return true;
            }

            foreach (var child in node.Children)
            {
                if (RenameFirst(child, automationId, replacement))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool RemoveFirst(UiElementInfo node, string automationId)
        {
            for (var i = 0; i < node.Children.Count; i++)
            {
                if (string.Equals(node.Children[i].AutomationId ?? "", automationId, StringComparison.Ordinal))
                {
                    node.Children.RemoveAt(i);
                    return true;
                }

                if (RemoveFirst(node.Children[i], automationId))
                {
                    return true;
                }
            }

            return false;
        }

        private static UiElementInfo Clone(UiElementInfo source)
        {
            var clone = new UiElementInfo
            {
                ControlType = source.ControlType,
                Name = source.Name,
                AutomationId = source.AutomationId,
                ClassName = source.ClassName,
                BoundingRectangle = source.BoundingRectangle,
                ParentControlType = source.ParentControlType,
                ParentAutomationId = source.ParentAutomationId,
                SiblingIndex = source.SiblingIndex,
                SiblingCount = source.SiblingCount,
                TestIntent = source.TestIntent,
            };

            foreach (var child in source.Children)
            {
                clone.Children.Add(Clone(child));
            }

            return clone;
        }
    }
}
