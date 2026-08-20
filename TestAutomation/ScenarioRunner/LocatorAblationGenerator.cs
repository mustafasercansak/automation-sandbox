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

        public static LocatorAblationDataset GenerateMultiLocator(
            UiElementInfo sourceRoot,
            string applicationName,
            string sourceVersion,
            string sourceTreeFileName,
            IEnumerable<IEnumerable<MultiLocatorMutationRequest>> groups)
        {
            if (sourceRoot == null)
            {
                throw new ArgumentNullException(nameof(sourceRoot));
            }

            if (groups == null)
            {
                throw new ArgumentNullException(nameof(groups));
            }

            var targets = EnumerateLocatorTargets(sourceRoot)
                .ToDictionary(t => t.AutomationId, StringComparer.Ordinal);
            var dataset = new LocatorAblationDataset();
            var groupIndex = 0;

            foreach (var group in groups)
            {
                var requests = group?.ToList()
                    ?? throw new ArgumentException("A multi-locator mutation group cannot be null.", nameof(groups));
                if (requests.Count < 2)
                {
                    throw new ArgumentException("A multi-locator mutation group must contain at least two locators.", nameof(groups));
                }

                var duplicate = requests
                    .GroupBy(r => r.OriginalAutomationId, StringComparer.Ordinal)
                    .FirstOrDefault(g => g.Count() > 1);
                if (duplicate != null)
                {
                    throw new ArgumentException(
                        $"Multi-locator mutation group {groupIndex} repeats AutomationId '{duplicate.Key}'.",
                        nameof(groups));
                }

                var scenarioId = BuildMultiScenarioId(applicationName, sourceVersion, groupIndex, requests);
                var mutations = new List<LocatorAblationMutation>();
                foreach (var request in requests)
                {
                    if (!targets.TryGetValue(request.OriginalAutomationId, out var target))
                    {
                        throw new ArgumentException(
                            $"Multi-locator mutation group {groupIndex} references AutomationId '{request.OriginalAutomationId}', absent or ambiguous in the source tree.",
                            nameof(groups));
                    }

                    mutations.Add(BuildMutation(target, request.MutationKind, scenarioId));
                }

                dataset.Scenarios.Add(new LocatorAblationScenario
                {
                    ScenarioId = scenarioId,
                    ApplicationName = applicationName,
                    SourceVersion = sourceVersion,
                    SourceTreeFileName = sourceTreeFileName,
                    MutationKind = LocatorMutationKind.MultiLocator,
                    Mutations = mutations,
                });
                groupIndex++;
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
            var mutations = scenario.MutationKind == LocatorMutationKind.MultiLocator
                ? scenario.Mutations
                : new List<LocatorAblationMutation> { ToMutation(scenario) };

            if (mutations == null || mutations.Count < 2 && scenario.MutationKind == LocatorMutationKind.MultiLocator)
            {
                throw new InvalidOperationException(
                    $"Multi-locator scenario '{scenario.ScenarioId}' must contain at least two mutation recipes.");
            }

            foreach (var mutation in mutations)
            {
                ApplyOneMutation(clone, scenario.ScenarioId, mutation);
            }

            // Anonymize all candidate AutomationIds in the mutated tree to the exact same opaque format
            // (ablation-XXXXXXXX) so that the target element cannot be distinguished by its identifier format (#97).
            // The target element retains scenario.MutatedAutomationId.
            MakeAllAutomationIdsOpaque(clone, scenario.ScenarioId, mutations);

            return clone;
        }

        private static void ApplyOneMutation(
            UiElementInfo clone,
            string scenarioId,
            LocatorAblationMutation mutation)
        {
            switch (mutation.MutationKind)
            {
                case LocatorMutationKind.RenamedAutomationId:
                case LocatorMutationKind.NameDrift:
                case LocatorMutationKind.PositionShift:
                case LocatorMutationKind.CompoundDrift:
                    if (!MutateFirst(clone, mutation))
                    {
                        throw new InvalidOperationException(
                            $"Scenario '{scenarioId}' targets AutomationId '{mutation.OriginalAutomationId}', which is not in the source tree.");
                    }
                    break;

                case LocatorMutationKind.RemovedElement:
                    if (string.Equals(clone.AutomationId, mutation.OriginalAutomationId, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Scenario '{scenarioId}' would remove the root element, leaving no tree to search.");
                    }

                    if (!RemoveFirst(clone, mutation.OriginalAutomationId))
                    {
                        throw new InvalidOperationException(
                            $"Scenario '{scenarioId}' targets AutomationId '{mutation.OriginalAutomationId}', which is not in the source tree.");
                    }
                    break;

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(mutation), mutation.MutationKind, "A member recipe must use a concrete mutation kind.");
            }
        }

        private static void MakeAllAutomationIdsOpaque(
            UiElementInfo root,
            string scenarioId,
            IReadOnlyCollection<LocatorAblationMutation> mutations)
        {
            var mutatedIds = new HashSet<string>(
                mutations.Where(m => !string.IsNullOrEmpty(m.MutatedAutomationId)).Select(m => m.MutatedAutomationId!),
                StringComparer.Ordinal);

            void Walk(UiElementInfo node, string path)
            {
                if (!string.IsNullOrEmpty(node.AutomationId))
                {
                    // Target elements already have their scenario-specific opaque ids. All other
                    // elements use the same format so identifier shape cannot leak ground truth.
                    if (!mutatedIds.Contains(node.AutomationId))
                    {
                        node.AutomationId = OpaqueId(scenarioId + "#" + path + "#" + node.AutomationId);
                    }
                }

                var next = Append(path, node);
                foreach (var child in node.Children)
                {
                    Walk(child, next);
                }
            }

            Walk(root, "");
        }

        private static LocatorAblationMutation ToMutation(LocatorAblationScenario scenario) =>
            new LocatorAblationMutation
            {
                MutationKind = scenario.MutationKind,
                ExpectedOutcome = scenario.ExpectedOutcome,
                OriginalAutomationId = scenario.OriginalAutomationId,
                MutatedAutomationId = scenario.MutatedAutomationId,
                MutatedName = scenario.MutatedName,
                ShiftX = scenario.ShiftX,
                ShiftY = scenario.ShiftY,
                GroundTruth = scenario.GroundTruth,
            };

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

        private static string BuildMultiScenarioId(
            string app,
            string version,
            int groupIndex,
            IReadOnlyList<MultiLocatorMutationRequest> requests)
        {
            var members = string.Join(",", requests.Select(r => r.OriginalAutomationId + ":" + r.MutationKind));
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}@{1}#multi-{2:D2}-{3}",
                app,
                version,
                groupIndex,
                OpaqueId(members).Substring("ablation-".Length));
        }

        private static LocatorAblationMutation BuildMutation(
            LocatorTarget target,
            LocatorMutationKind kind,
            string scenarioId)
        {
            if (kind == LocatorMutationKind.MultiLocator)
            {
                throw new ArgumentException("A multi-locator member must use a concrete mutation kind.", nameof(kind));
            }

            var hasName = !string.IsNullOrWhiteSpace(target.Element.Name);
            if ((kind == LocatorMutationKind.NameDrift || kind == LocatorMutationKind.CompoundDrift) && !hasName)
            {
                throw new ArgumentException(
                    $"AutomationId '{target.AutomationId}' cannot use {kind} because its Name is empty.",
                    nameof(kind));
            }

            var mutatedName = kind == LocatorMutationKind.NameDrift || kind == LocatorMutationKind.CompoundDrift
                ? PerturbName(target.Element.Name!)
                : null;
            var removed = kind == LocatorMutationKind.RemovedElement;
            var shifted = kind == LocatorMutationKind.PositionShift || kind == LocatorMutationKind.CompoundDrift;

            return new LocatorAblationMutation
            {
                MutationKind = kind,
                ExpectedOutcome = removed ? LocatorExpectedOutcome.NoSuccessor : LocatorExpectedOutcome.Successor,
                OriginalAutomationId = target.AutomationId,
                MutatedAutomationId = removed ? null : OpaqueId(scenarioId + "#" + target.AutomationId),
                MutatedName = mutatedName,
                ShiftX = shifted ? 140.0 : 0.0,
                ShiftY = shifted ? 80.0 : 0.0,
                GroundTruth = removed
                    ? null
                    : mutatedName == null
                        ? Fingerprint(target.Element, target.AncestorPath)
                        : FingerprintWithName(target.Element, target.AncestorPath, mutatedName),
            };
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

        private static bool MutateFirst(UiElementInfo node, LocatorAblationMutation mutation)
        {
            if (string.Equals(node.AutomationId ?? "", mutation.OriginalAutomationId, StringComparison.Ordinal))
            {
                node.AutomationId = mutation.MutatedAutomationId ?? "";
                if (mutation.MutatedName != null)
                {
                    node.Name = mutation.MutatedName;
                }
                if (mutation.ShiftX != 0.0 || mutation.ShiftY != 0.0)
                {
                    var r = node.BoundingRectangle;
                    if (r.IsUsable)
                    {
                        node.BoundingRectangle = new BoundingRectangle(r.X + mutation.ShiftX, r.Y + mutation.ShiftY, r.Width, r.Height);
                    }
                }
                return true;
            }

            foreach (var child in node.Children)
            {
                if (MutateFirst(child, mutation))
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
