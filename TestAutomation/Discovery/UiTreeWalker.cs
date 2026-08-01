using FlaUI.Core.AutomationElements;
using UiModel;

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
            // Properties.X.ValueOrDefault is used because some legacy native WinForms
            // controls (e.g. DataGridView's internal cells) don't support every UIA
            // property - shortcuts like element.AutomationId throw
            // PropertyNotSupportedException in that case, while ValueOrDefault returns "" instead.
            var node = new UiElementInfo
            {
                ControlType = element.Properties.ControlType.ValueOrDefault.ToString(),
                Name = element.Properties.Name.ValueOrDefault ?? "",
                AutomationId = element.Properties.AutomationId.ValueOrDefault ?? "",
                ClassName = element.Properties.ClassName.ValueOrDefault ?? "",
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
            var rect = element.Properties.BoundingRectangle.ValueOrDefault;
            return new BoundingRectangle(rect.X, rect.Y, rect.Width, rect.Height);
        }
    }
}
