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
            // Properties.X.ValueOrDefault kullanılıyor çünkü bazı legacy WinForms native
            // kontrolleri (ör. DataGridView'in iç hücreleri) UIA üzerinden her property'yi
            // desteklemiyor - element.AutomationId gibi kısayollar bu durumda
            // PropertyNotSupportedException fırlatır, ValueOrDefault fırlatmadan "" döner.
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
