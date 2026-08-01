using FlaUI.Core.AutomationElements;
using System.Runtime.InteropServices;
using UiModel;
namespace Discovery
{
    public static class UiTreeWalker
    {
        private const int DefaultMaxDepth = 25;
        private const int DefaultMaxElements = 5000;
        public static UiElementInfo BuildTree(AutomationElement element, int maxDepth = DefaultMaxDepth, int maxElements = DefaultMaxElements)
        {
            var visitedElements = 0;
            return BuildNode(element, parentControlType: "", parentAutomationId: "", siblingIndex: 0, siblingCount: 1, depth: 0, maxDepth, maxElements, ref visitedElements);
        }

        private static UiElementInfo BuildNode(
            AutomationElement element,
            string parentControlType,
            string parentAutomationId,
            int siblingIndex,
            int siblingCount,
            int depth,
            int maxDepth,
            int maxElements,
            ref int visitedElements)
        {
            visitedElements++;

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
            if (depth >= maxDepth || visitedElements >= maxElements)
            {
                return node;
            }

            var children = FindChildrenSafely(element);
            for (var i = 0; i < children.Length; i++)
            {
                if (visitedElements >= maxElements)
                {
                    break;
                }

                try
                {
                    node.Children.Add(BuildNode(children[i], node.ControlType, node.AutomationId, i, children.Length, depth + 1, maxDepth, maxElements, ref visitedElements));
                }

                catch (COMException)
                {
                    // Live UIA trees can change while being walked. Skip the stale
                    // child and keep the rest of the snapshot usable.
                }

                catch (InvalidOperationException)
                {
                }

                catch (UnauthorizedAccessException)
                {
                }
            }

            return node;
        }

        private static AutomationElement[] FindChildrenSafely(AutomationElement element)
        {
            try
            {
                return element.FindAllChildren();
            }

            catch (COMException)
            {
                return Array.Empty<AutomationElement>();
            }

            catch (InvalidOperationException)
            {
                return Array.Empty<AutomationElement>();
            }

            catch (UnauthorizedAccessException)
            {
                return Array.Empty<AutomationElement>();
            }
        }

        private static BoundingRectangle ToBoundingRectangle(AutomationElement element)
        {
            var rect = element.Properties.BoundingRectangle.ValueOrDefault;
            return new BoundingRectangle(rect.X, rect.Y, rect.Width, rect.Height);
        }
    }
}
