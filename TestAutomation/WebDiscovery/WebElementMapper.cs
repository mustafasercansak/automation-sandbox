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
                    ? BoundingRectangle.Empty
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
                "form" => "Group",
                "main" => "Document",
                "section" => "Group",
                "fieldset" => "Group",
                _ => string.IsNullOrWhiteSpace(element.TagName) ? "" : element.TagName.ToLowerInvariant(),
            };
        }

        private static readonly Dictionary<string, string> AriaRoleToControlType =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "alert", "Text" },
                { "alertdialog", "Window" },
                { "appbar", "AppBar" },
                { "article", "Document" },
                { "banner", "Header" },
                { "button", "Button" },
                { "calendar", "Calendar" },
                { "cell", "DataItem" },
                { "checkbox", "CheckBox" },
                { "columnheader", "HeaderItem" },
                { "combobox", "ComboBox" },
                { "custom", "Custom" },
                { "datagrid", "DataGrid" },
                { "dataitem", "DataItem" },
                { "dialog", "Window" },
                { "document", "Document" },
                { "edit", "Edit" },
                { "form", "Group" },
                { "grid", "DataGrid" },
                { "gridcell", "DataItem" },
                { "group", "Group" },
                { "header", "Header" },
                { "headeritem", "HeaderItem" },
                { "heading", "Text" },
                { "hyperlink", "Hyperlink" },
                { "image", "Image" },
                { "img", "Image" },
                { "link", "Hyperlink" },
                { "list", "List" },
                { "listbox", "List" },
                { "listitem", "ListItem" },
                { "log", "StatusBar" },
                { "main", "Document" },
                { "menu", "Menu" },
                { "menubar", "MenuBar" },
                { "menuitem", "MenuItem" },
                { "menuitemcheckbox", "MenuItem" },
                { "menuitemradio", "MenuItem" },
                { "navigation", "Group" },
                { "option", "ListItem" },
                { "pane", "Pane" },
                { "progressbar", "ProgressBar" },
                { "radio", "RadioButton" },
                { "radiobutton", "RadioButton" },
                { "radiogroup", "Group" },
                { "region", "Group" },
                { "row", "DataItem" },
                { "rowheader", "HeaderItem" },
                { "scrollbar", "ScrollBar" },
                { "search", "Group" },
                { "searchbox", "Edit" },
                { "semanticzoom", "SemanticZoom" },
                { "separator", "Separator" },
                { "slider", "Slider" },
                { "spinbutton", "Spinner" },
                { "spinner", "Spinner" },
                { "splitbutton", "SplitButton" },
                { "status", "StatusBar" },
                { "statusbar", "StatusBar" },
                { "tab", "TabItem" },
                { "tabitem", "TabItem" },
                { "tablist", "Tab" },
                { "tabpanel", "Pane" },
                { "switch", "CheckBox" },
                { "table", "DataGrid" },
                { "text", "Text" },
                { "textbox", "Edit" },
                { "thumb", "Thumb" },
                { "timer", "StatusBar" },
                { "titlebar", "TitleBar" },
                { "toolbar", "ToolBar" },
                { "tooltip", "ToolTip" },
                { "tree", "Tree" },
                { "treegrid", "DataGrid" },
                { "treeitem", "TreeItem" },
                { "window", "Window" },
            };

        private static string NormalizeRole(string role)
        {
            var trimmed = role.Trim();
            if (AriaRoleToControlType.TryGetValue(trimmed, out var controlType))
            {
                return controlType;
            }

            return char.ToUpperInvariant(trimmed[0]) + trimmed.Substring(1).ToLowerInvariant();
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
