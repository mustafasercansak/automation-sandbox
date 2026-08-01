using FlaUI.Core.AutomationElements;

namespace Discovery
{
    public static class UiTreeWalker
    {
        public static UiElementInfo BuildTree(AutomationElement element)
        {
            return BuildNode(element, parentControlType: "", parentAutomationId: "", siblingIndex: 0, siblingCount: 1);
        }

        private static UiElementInfo BuildNode(
            AutomationElement element,
            string parentControlType,
            string parentAutomationId,
            int siblingIndex,
            int siblingCount)
        {
            var node = new UiElementInfo
            {
                ControlType = element.ControlType.ToString(),
                Name = element.Name ?? "",
                AutomationId = element.AutomationId ?? "",
                ClassName = element.ClassName ?? "",
                BoundingRectangle = ToBoundingRectangle(element),
                ParentControlType = parentControlType,
                ParentAutomationId = parentAutomationId,
                SiblingIndex = siblingIndex,
                SiblingCount = siblingCount,
            };

            var children = element.FindAllChildren();
            for (var i = 0; i < children.Length; i++)
            {
                node.Children.Add(BuildNode(children[i], node.ControlType, node.AutomationId, i, children.Length));
            }

            return node;
        }

        private static BoundingRectangle ToBoundingRectangle(AutomationElement element)
        {
            var rect = element.BoundingRectangle;
            return new BoundingRectangle(rect.X, rect.Y, rect.Width, rect.Height);
        }
    }
}
