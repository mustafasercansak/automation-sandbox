namespace UiModel
{
    // Captures a single locator's structural state for persistence, without the bloat of
    // serializing its entire live descendant subtree. A snapshot is just a UiElementInfo
    // with Children cleared - reuses UiTreeSerializer rather than inventing a new format.

    public static class UiElementSnapshot
    {
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
        };

        public static UiElementInfo? CaptureByAutomationId(UiElementInfo treeRoot, string automationId)
        {
            var found = FindFirst(treeRoot, node => node.AutomationId == automationId);
            return found is null ? null : Capture(found);
        }

        public static UiElementInfo? CaptureFirst(UiElementInfo treeRoot, Predicate<UiElementInfo> predicate)
        {
            if (predicate == null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            var found = FindFirst(treeRoot, predicate);
            return found is null ? null : Capture(found);
        }

        public static string ToJson(UiElementInfo node) => UiTreeSerializer.ToJson(Capture(node));
        public static UiElementInfo FromJson(string json) => UiTreeSerializer.FromJson(json);

        public static UiElementInfo? FindByAutomationId(UiElementInfo node, string automationId)
        {
            return FindFirst(node, candidate => candidate.AutomationId == automationId);
        }

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
