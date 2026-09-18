using AutomationSandbox.UiModel;

namespace AutomationSandbox.WebDiscovery
{
    /// <summary>Maps DOM snapshots to the framework-independent structural UI model.</summary>
    public static class WebElementMapper
    {
        /// <summary>Converts the web tree and its contextual metadata into a tree that the healing scorer can evaluate.</summary>
        public static UiElementInfo ToUiElementTree(WebElementInfo root)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            return Map(root, parentControlType: "", parentAutomationId: "", siblingIndex: 0, siblingCount: 1);
        }

        // Framework-generated DOM markup (component-per-wrapper-div frameworks especially)
        // can nest hundreds of levels deep with no bound comparable to
        // Discovery.DiscoveryOptions.MaxDepth on the desktop side (#426). This is a generous
        // backstop - well beyond that - not an operational parameter callers are expected to
        // hit; it exists only to turn a pathological DOM into a truncated (still-mapped)
        // result instead of a stack overflow.
        private const int MaxMapDepth = 5000;

        private static UiElementInfo Map(
            WebElementInfo element,
            string parentControlType,
            string parentAutomationId,
            int siblingIndex,
            int siblingCount)
        {
            var root = BuildNode(element, parentControlType, parentAutomationId, siblingIndex, siblingCount);

            // Iterative (an explicit stack, not recursion) so traversal depth is bounded by
            // heap space rather than call-stack space, and MaxMapDepth below can therefore
            // stop a pathologically deep branch instead of overflowing first (#426). Each
            // child is mapped and attached to its parent's Children list as soon as it is
            // built, so list order matches the source DOM regardless of stack processing
            // order. A node at the cap is still mapped; only its deeper descendants are
            // skipped - the same truncation shape UiTreeWalker uses for HitMaxDepth.
            var stack = new Stack<(WebElementInfo Source, UiElementInfo Mapped, int Depth)>();
            stack.Push((element, root, 0));
            while (stack.Count > 0)
            {
                var (sourceElement, mappedNode, depth) = stack.Pop();
                if (depth >= MaxMapDepth)
                {
                    continue;
                }

                var childCount = sourceElement.Children.Count;
                for (var i = 0; i < childCount; i++)
                {
                    var childSource = sourceElement.Children[i];
                    var childNode = BuildNode(childSource, mappedNode.ControlType, mappedNode.AutomationId, i, childCount);
                    mappedNode.Children.Add(childNode);
                    stack.Push((childSource, childNode, depth + 1));
                }
            }

            return root;
        }

        private static UiElementInfo BuildNode(
            WebElementInfo element,
            string parentControlType,
            string parentAutomationId,
            int siblingIndex,
            int siblingCount)
        {
            var automationId = FirstNonEmpty(element.TestId, element.Id, element.NameAttribute);
            return new UiElementInfo
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
