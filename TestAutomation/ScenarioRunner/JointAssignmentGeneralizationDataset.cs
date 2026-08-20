using System;
using System.Collections.Generic;
using System.Linq;
using UiModel;

namespace ScenarioRunner
{
    // Frozen before #143's measurement. Selection uses only pristine-tree structure and ordinal
    // AutomationId order, never resolver scores or candidate outcomes, so the evaluation cannot
    // quietly select the cases that make joint ownership look favourable.
    public static class JointAssignmentGeneralizationDataset
    {
        public static LocatorAblationDataset Generate(
            UiElementInfo sourceRoot,
            string applicationName,
            string sourceVersion,
            string sourceTreeFileName)
        {
            var eligibleIds = SelectEligibleLocatorIds(sourceRoot);
            if (eligibleIds.Count < 3)
            {
                throw new ArgumentException(
                    "A generalization fixture must contain at least three eligible authored leaf locators.",
                    nameof(sourceRoot));
            }

            var groups = new List<List<MultiLocatorMutationRequest>>();
            for (var i = 0; i < eligibleIds.Count; i++)
            {
                groups.Add(new List<MultiLocatorMutationRequest>
                {
                    Mutation(eligibleIds[i], LocatorMutationKind.RemovedElement),
                    Mutation(eligibleIds[(i + 1) % eligibleIds.Count], LocatorMutationKind.RenamedAutomationId),
                    Mutation(eligibleIds[(i + 2) % eligibleIds.Count], LocatorMutationKind.PositionShift),
                });
            }

            return LocatorAblationGenerator.GenerateMultiLocator(
                sourceRoot,
                applicationName,
                sourceVersion,
                sourceTreeFileName,
                groups);
        }

        public static IReadOnlyList<string> SelectEligibleLocatorIds(UiElementInfo sourceRoot)
        {
            if (sourceRoot == null)
            {
                throw new ArgumentNullException(nameof(sourceRoot));
            }

            var nonRootNodes = new List<UiElementInfo>();
            foreach (var child in sourceRoot.Children)
            {
                Flatten(child, nonRootNodes);
            }

            var uniqueNodes = nonRootNodes
                .Where(n => !string.IsNullOrEmpty(n.AutomationId))
                .GroupBy(n => n.AutomationId, StringComparer.Ordinal)
                .Where(g => g.Count() == 1)
                .Select(g => g.Single())
                .ToList();
            var uniqueIds = new HashSet<string>(
                uniqueNodes.Select(n => n.AutomationId),
                StringComparer.Ordinal);

            return uniqueNodes
                // #78/#134 established DataItem rows as an inherently volatile repeated-grid class.
                // The exclusion predates this experiment and keeps both application samples comparable.
                .Where(n => !string.Equals(n.ControlType, "DataItem", StringComparison.Ordinal))
                // Removal must not erase either survivor's ground truth. Restricting every member to
                // an authored leaf guarantees that independently of how the cyclic groups are paired.
                .Where(n => !HasAuthoredDescendant(n, uniqueIds))
                .Select(n => n.AutomationId)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();
        }

        private static MultiLocatorMutationRequest Mutation(string automationId, LocatorMutationKind kind) =>
            new MultiLocatorMutationRequest
            {
                OriginalAutomationId = automationId,
                MutationKind = kind,
            };

        private static void Flatten(UiElementInfo node, List<UiElementInfo> nodes)
        {
            nodes.Add(node);
            foreach (var child in node.Children)
            {
                Flatten(child, nodes);
            }
        }

        private static bool HasAuthoredDescendant(UiElementInfo node, HashSet<string> uniqueIds)
        {
            foreach (var child in node.Children)
            {
                if (uniqueIds.Contains(child.AutomationId) || HasAuthoredDescendant(child, uniqueIds))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
