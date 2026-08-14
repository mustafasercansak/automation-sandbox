using UiModel;

namespace WebDiscovery
{
    public static class WebElementMapper
    {
        public static UiElementInfo ToUiElementTree(WebElementInfo root)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            return Map(root, parentControlType: "", parentAutomationId: "", siblingIndex: 0, siblingCount: 1);
        }

        private static UiElementInfo Map(
            WebElementInfo element,
            string parentControlType,
            string parentAutomationId,
            int siblingIndex,
            int siblingCount)
        {
            var automationId = FirstNonEmpty(element.TestId, element.Id, element.NameAttribute);
            var node = new UiElementInfo
            {
                ControlType = ToControlType(element),
                Name = FirstNonEmpty(element.AccessibleName, element.Text),
                AutomationId = automationId,
                ClassName = ToClassName(element),
                BoundingRectangle = element.IsHidden
                    ? new BoundingRectangle(0, 0, 0, 0)
                    : element.BoundingRectangle,
                ParentControlType = parentControlType,
                ParentAutomationId = parentAutomationId,
                SiblingIndex = siblingIndex,
                SiblingCount = siblingCount,
            };

            for (var i = 0; i < element.Children.Count; i++)
            {
                node.Children.Add(Map(
                    element.Children[i],
                    node.ControlType,
                    node.AutomationId,
                    i,
                    element.Children.Count));
            }

            return node;
        }

        private static string ToControlType(WebElementInfo element)
        {
            if (!string.IsNullOrWhiteSpace(element.Role))
            {
                return NormalizeRole(element.Role);
            }

            return element.TagName.ToLowerInvariant() switch
            {
                "button" => "Button",
                "input" => "Edit",
                "textarea" => "Edit",
                "select" => "ComboBox",
                "table" => "DataGrid",
                "a" => "Hyperlink",
                "form" => "Form",
                "main" => "Document",
                "section" => "Group",
                "fieldset" => "Group",
                _ => string.IsNullOrWhiteSpace(element.TagName) ? "" : element.TagName.ToLowerInvariant(),
            };
        }

        private static string NormalizeRole(string role)
        {
            return role.Equals("textbox", StringComparison.OrdinalIgnoreCase)
                ? "Edit"
                : char.ToUpperInvariant(role[0]) + role.Substring(1).ToLowerInvariant();
        }

        private static string ToClassName(WebElementInfo element)
        {
            var className = FirstNonEmpty(element.ClassName, element.TagName.ToLowerInvariant());
            return string.IsNullOrWhiteSpace(element.TreeScope) || element.TreeScope == "light-dom"
                ? className
                : $"{className} [{element.TreeScope}]";
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return "";
        }
    }
}
