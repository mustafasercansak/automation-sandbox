using System.Text.RegularExpressions;
using AutomationSandbox.UiModel;

namespace AutomationSandbox.SelfHealingCli;

// Builds a partial UiElementInfo from the selector string a broken Playwright test still
// carries, for a suite that has no snapshot recorded while it was green (#616). Only the
// signals the selector actually states are filled in (test id -> AutomationId, role ->
// ControlType, accessible name/text/label -> Name); geometry, parent and sibling context
// stay empty, so the resolver scores the element with the evidence it really has and its
// coverage gate decides whether that is enough.
internal static class PlaywrightSelectorParser
{
    // Same ARIA-role-to-ControlType table as examples/js-playwright-capture/capture.mjs, so
    // a role in a selector lands on the ControlType the captured live tree uses.
    private static readonly Dictionary<string, string> RoleToControlType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["alert"] = "Text", ["alertdialog"] = "Window", ["article"] = "Document", ["banner"] = "Header",
        ["button"] = "Button", ["cell"] = "DataItem", ["checkbox"] = "CheckBox", ["columnheader"] = "HeaderItem",
        ["combobox"] = "ComboBox", ["dialog"] = "Window", ["document"] = "Document", ["form"] = "Group",
        ["grid"] = "DataGrid", ["gridcell"] = "DataItem", ["group"] = "Group", ["heading"] = "Text",
        ["img"] = "Image", ["link"] = "Hyperlink", ["list"] = "List", ["listbox"] = "List",
        ["listitem"] = "ListItem", ["main"] = "Document", ["menu"] = "Menu", ["menubar"] = "MenuBar",
        ["menuitem"] = "MenuItem", ["menuitemcheckbox"] = "MenuItem", ["menuitemradio"] = "MenuItem",
        ["navigation"] = "Group", ["option"] = "ListItem", ["progressbar"] = "ProgressBar",
        ["radio"] = "RadioButton", ["radiogroup"] = "Group", ["region"] = "Group", ["row"] = "DataItem",
        ["rowheader"] = "HeaderItem", ["scrollbar"] = "ScrollBar", ["search"] = "Group",
        ["searchbox"] = "Edit", ["separator"] = "Separator", ["slider"] = "Slider", ["spinbutton"] = "Spinner",
        ["status"] = "StatusBar", ["switch"] = "CheckBox", ["tab"] = "TabItem", ["table"] = "DataGrid",
        ["tablist"] = "Tab", ["tabpanel"] = "Pane", ["textbox"] = "Edit", ["toolbar"] = "ToolBar",
        ["tooltip"] = "ToolTip", ["tree"] = "Tree", ["treegrid"] = "DataGrid", ["treeitem"] = "TreeItem",
    };

    private const string Quoted = @"(?:'((?:[^'\\]|\\.)*)'|""((?:[^""\\]|\\.)*)""|`((?:[^`\\]|\\.)*)`)";

    private static readonly Regex TestIdCall = new(@"^getByTestId\(\s*" + Quoted + @"\s*\)$", RegexOptions.Compiled);
    private static readonly Regex TextCall = new(@"^getBy(?:Text|Label|Placeholder|Title|AltText)\(\s*" + Quoted + @"\s*(?:,\s*\{[^}]*\}\s*)?\)$", RegexOptions.Compiled);
    private static readonly Regex RoleCall = new(@"^getByRole\(\s*" + Quoted + @"\s*(?:,\s*\{(.*)\}\s*)?\)$", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex NameOption = new(@"\bname\s*:\s*" + Quoted, RegexOptions.Compiled);

    public static bool TryParse(string selector, out UiElementInfo expected, out string error)
    {
        expected = new UiElementInfo();
        error = "";

        // Accept the form copied out of a test: "page.getByRole(...)" and a trailing ".first()" etc. is
        // not understood, so only the leading locator call is read; chained calls are rejected loudly.
        var text = selector.Trim();
        if (text.StartsWith("page.", StringComparison.Ordinal))
        {
            text = text["page.".Length..];
        }

        var testId = TestIdCall.Match(text);
        if (testId.Success)
        {
            expected.AutomationId = Unescape(Pick(testId, 1));
            return true;
        }

        var byText = TextCall.Match(text);
        if (byText.Success)
        {
            expected.Name = Unescape(Pick(byText, 1));
            return true;
        }

        var byRole = RoleCall.Match(text);
        if (byRole.Success)
        {
            var role = Unescape(Pick(byRole, 1));
            if (!RoleToControlType.TryGetValue(role.Trim(), out var controlType))
            {
                error = $"Unsupported role '{role}' in selector. Supported roles: {string.Join(", ", RoleToControlType.Keys.Order())}.";
                return false;
            }

            expected.ControlType = controlType;
            var options = byRole.Groups[4].Value;
            var name = NameOption.Match(options);
            if (name.Success)
            {
                expected.Name = Unescape(Pick(name, 1));
            }

            return true;
        }

        error = $"Unsupported selector '{selector}'. Supported forms: getByTestId('...'), getByRole('...', {{ name: '...' }}), getByText('...'), getByLabel('...'), getByPlaceholder('...'), getByTitle('...'), getByAltText('...').";
        return false;
    }

    private static string Pick(Match match, int firstGroup) =>
        match.Groups[firstGroup].Success ? match.Groups[firstGroup].Value
        : match.Groups[firstGroup + 1].Success ? match.Groups[firstGroup + 1].Value
        : match.Groups[firstGroup + 2].Value;

    private static string Unescape(string value) => Regex.Replace(value, @"\\(.)", "$1");
}
