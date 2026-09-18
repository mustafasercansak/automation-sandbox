namespace AutomationSandbox.UiModel
{
    // Captures a single locator's structural state for persistence, without the bloat of
    // serializing its entire live descendant subtree. A snapshot is just a UiElementInfo
    // with Children cleared - reuses UiTreeSerializer rather than inventing a new format.

    /// <summary>Captures detached locator snapshots and finds nodes in captured trees.</summary>
    public static class UiElementSnapshot
    {
        /// <summary>Copies one node&apos;s locator evidence, including its direct-child type signature, without copying the descendant tree.</summary>
        public static UiElementInfo Capture(UiElementInfo node) => new()
        {
            ControlType = node.ControlType,
            Name = node.Name,
            AutomationId = node.AutomationId,
            ClassName = node.ClassName,
            BoundingRectangle = node.BoundingRectangle,
            ParentControlType = node.ParentControlType,
            ParentAutomationId = node.ParentAutomationId,
            SiblingIndex = node.SiblingIndex,
            SiblingCount = node.SiblingCount,
            TestIntent = node.TestIntent,
            ChildControlTypeSignature = ComputeChildControlTypeSignature(node),
        };

        // Deterministic "Type:count|Type:count" summary of a node's DIRECT children, ordered
        // by ControlType. Empty string for a leaf. Persisted on the snapshot (Children
        // themselves are dropped) so the healer can compare what a stale container held
        // against what a candidate holds now - the one signal the five-component scorer,
        // which only ever looks at the element itself, cannot see (#375).
        /// <summary>Deterministic &quot;Type:count|Type:count&quot; summary of a node&apos;s DIRECT children, ordered by ControlType. Empty string for a leaf. Persisted on the snapshot (Children themselves are dropped) so the healer can compare what a stale container held against what a candidate holds now - the one signal the five-component scorer, which only ever looks at the element itself, cannot see (#375).</summary>
        public static string ComputeChildControlTypeSignature(UiElementInfo node)
        {
            if (node?.Children == null || node.Children.Count == 0)
            {
                return "";
            }

            return string.Join("|", node.Children
                .GroupBy(c => c.ControlType ?? "", StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => g.Key + ":" + g.Count()));
        }

        /// <summary>Captures the first node with the supplied automation identifier; duplicate identifiers are not disambiguated.</summary>
        public static UiElementInfo? CaptureByAutomationId(UiElementInfo treeRoot, string automationId)
        {
            var found = FindFirst(treeRoot, node => node.AutomationId == automationId);
            return found is null ? null : Capture(found);
        }

        /// <summary>Captures a detached snapshot of the first pre-order node matching the predicate; returns null when no node matches.</summary>
        public static UiElementInfo? CaptureFirst(UiElementInfo treeRoot, Predicate<UiElementInfo> predicate)
        {
            if (predicate == null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            var found = FindFirst(treeRoot, predicate);
            return found is null ? null : Capture(found);
        }

        /// <summary>Serializes the supplied document to the package&apos;s JSON representation.</summary>
        public static string ToJson(UiElementInfo node) => UiTreeSerializer.ToJson(Capture(node));
        /// <summary>Deserializes JSON into the package&apos;s editable document model.</summary>
        public static UiElementInfo FromJson(string json) => UiTreeSerializer.FromJson(json);

        /// <summary>Returns the first node with the supplied automation identifier, or null when none matches.</summary>
        public static UiElementInfo? FindByAutomationId(UiElementInfo node, string automationId)
        {
            return FindFirst(node, candidate => candidate.AutomationId == automationId);
        }

        /// <summary>Returns the first pre-order node matching the predicate, or null when none matches.</summary>
        public static UiElementInfo? FindFirst(UiElementInfo node, Predicate<UiElementInfo> predicate)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            if (predicate == null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            if (predicate(node))
            {
                return node;
            }

            foreach (var child in node.Children)
            {
                var found = FindFirst(child, predicate);
                if (found is not null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
