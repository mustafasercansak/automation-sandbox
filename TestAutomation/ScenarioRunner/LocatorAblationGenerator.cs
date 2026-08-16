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
                var originalId = target.AutomationId;
                var hasName = !string.IsNullOrWhiteSpace(target.Element.Name);

                // 1. Pure AutomationId rename (baseline) with opaque ID (no _ablated leak to LLMs)
                var renameId = BuildScenarioId(applicationName, sourceVersion, originalId, LocatorMutationKind.RenamedAutomationId);
                dataset.Scenarios.Add(new LocatorAblationScenario
                {
                    ScenarioId = renameId,
                    ApplicationName = applicationName,
                    SourceVersion = sourceVersion,
                    SourceTreeFileName = sourceTreeFileName,
                    MutationKind = LocatorMutationKind.RenamedAutomationId,
                    ExpectedOutcome = LocatorExpectedOutcome.Successor,
                    OriginalAutomationId = originalId,
                    MutatedAutomationId = OpaqueId(renameId),
                    GroundTruth = Fingerprint(target.Element, target.AncestorPath),
                });

                // 2. Name drift: only generated when element has a non-empty Name
                if (hasName)
                {
                    var nameDriftId = BuildScenarioId(applicationName, sourceVersion, originalId, LocatorMutationKind.NameDrift);
                    var mutatedName = PerturbName(target.Element.Name!);
                    dataset.Scenarios.Add(new LocatorAblationScenario
                    {
                        ScenarioId = nameDriftId,
                        ApplicationName = applicationName,
                        SourceVersion = sourceVersion,
                        SourceTreeFileName = sourceTreeFileName,
                        MutationKind = LocatorMutationKind.NameDrift,
                        ExpectedOutcome = LocatorExpectedOutcome.Successor,
                        OriginalAutomationId = originalId,
                        MutatedAutomationId = OpaqueId(nameDriftId),
                        MutatedName = mutatedName,
                        GroundTruth = FingerprintWithName(target.Element, target.AncestorPath, mutatedName),
                    });
                }

                // 3. Position shift: layout coordinate translation
                var posShiftId = BuildScenarioId(applicationName, sourceVersion, originalId, LocatorMutationKind.PositionShift);
                dataset.Scenarios.Add(new LocatorAblationScenario
                {
                    ScenarioId = posShiftId,
                    ApplicationName = applicationName,
                    SourceVersion = sourceVersion,
                    SourceTreeFileName = sourceTreeFileName,
                    MutationKind = LocatorMutationKind.PositionShift,
                    ExpectedOutcome = LocatorExpectedOutcome.Successor,
                    OriginalAutomationId = originalId,
                    MutatedAutomationId = OpaqueId(posShiftId),
                    ShiftX = 140.0,
                    ShiftY = 80.0,
                    GroundTruth = Fingerprint(target.Element, target.AncestorPath),
                });

                // 4. Compound drift: combines Name drift and Position shift (only when Name is present)
                if (hasName)
                {
                    var compoundId = BuildScenarioId(applicationName, sourceVersion, originalId, LocatorMutationKind.CompoundDrift);
                    var mutatedName = PerturbName(target.Element.Name!);
                    dataset.Scenarios.Add(new LocatorAblationScenario
                    {
                        ScenarioId = compoundId,
                        ApplicationName = applicationName,
                        SourceVersion = sourceVersion,
                        SourceTreeFileName = sourceTreeFileName,
                        MutationKind = LocatorMutationKind.CompoundDrift,
                        ExpectedOutcome = LocatorExpectedOutcome.Successor,
                        OriginalAutomationId = originalId,
                        MutatedAutomationId = OpaqueId(compoundId),
                        MutatedName = mutatedName,
                        ShiftX = 140.0,
                        ShiftY = 80.0,
                        GroundTruth = FingerprintWithName(target.Element, target.AncestorPath, mutatedName),
                    });
                }

                // 5. Complete removal: element and subtree deleted -> correct outcome is to decline
                var removalId = BuildScenarioId(applicationName, sourceVersion, originalId, LocatorMutationKind.RemovedElement);
                dataset.Scenarios.Add(new LocatorAblationScenario
                {
                    ScenarioId = removalId,
                    ApplicationName = applicationName,
                    SourceVersion = sourceVersion,
                    SourceTreeFileName = sourceTreeFileName,
                    MutationKind = LocatorMutationKind.RemovedElement,
                    ExpectedOutcome = LocatorExpectedOutcome.NoSuccessor,
                    OriginalAutomationId = originalId,
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
                case LocatorMutationKind.NameDrift:
                case LocatorMutationKind.PositionShift:
                case LocatorMutationKind.CompoundDrift:
                    var mutated = MutateFirst(clone, scenario);
                    if (!mutated)
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

        public static ElementFingerprint FingerprintWithName(UiElementInfo element, string ancestorPath, string? name) =>
            new ElementFingerprint
            {
                ControlType = element.ControlType ?? "",
                Name = name ?? element.Name ?? "",
                ClassName = element.ClassName ?? "",
                AncestorPath = ancestorPath,
            };

        public static string OpaqueId(string seed)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(seed));
                var hex = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
                return "ablation-" + hex.Substring(0, 8);
            }
        }

        public static string PerturbName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            // Realistic UI text drift: e.g. "Presets" -> "Presets...", "Start" -> "Start Encoding", "Cancel" -> "Cancel..."
            if (name.EndsWith("...", StringComparison.Ordinal))
            {
                return name.Substring(0, name.Length - 3);
            }

            if (name.Length <= 4)
            {
                return name + "...";
            }

            return name + " Options";
        }

        private static string BuildScenarioId(string app, string version, string automationId, LocatorMutationKind kind)
        {
            var kindTag = kind switch
            {
                LocatorMutationKind.RenamedAutomationId => "rename",
                LocatorMutationKind.NameDrift => "name-drift",
                LocatorMutationKind.PositionShift => "pos-shift",
                LocatorMutationKind.CompoundDrift => "compound",
                LocatorMutationKind.RemovedElement => "remove",
                _ => kind.ToString().ToLowerInvariant(),
            };

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}@{1}#{2}#{3}",
                app,
                version,
                automationId,
                kindTag);
        }

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

        private static bool MutateFirst(UiElementInfo node, LocatorAblationScenario scenario)
        {
            if (string.Equals(node.AutomationId ?? "", scenario.OriginalAutomationId, StringComparison.Ordinal))
            {
                node.AutomationId = scenario.MutatedAutomationId ?? "";
                if (scenario.MutatedName != null)
                {
                    node.Name = scenario.MutatedName;
                }
                if (scenario.ShiftX != 0.0 || scenario.ShiftY != 0.0)
                {
                    var r = node.BoundingRectangle;
                    if (r.IsUsable)
                    {
                        node.BoundingRectangle = new BoundingRectangle(r.X + scenario.ShiftX, r.Y + scenario.ShiftY, r.Width, r.Height);
                    }
                }
                return true;
            }

            foreach (var child in node.Children)
            {
                if (MutateFirst(child, scenario))
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
