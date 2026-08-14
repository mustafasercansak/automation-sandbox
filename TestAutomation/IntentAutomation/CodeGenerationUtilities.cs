using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace IntentAutomation
{
    public static class CodeGenerationUtilities
    {
        // Explicitly mirrors FlaUI.Core.Definitions.ControlType enum members to avoid a hard dependency
        // on FlaUI.Core in IntentAutomation, keeping the library fully cross-platform (netstandard2.0;net8.0).
        // Validated against the real FlaUI.Core enum in ScenarioRunner's net48 test suite.
        private static readonly Dictionary<string, string> CanonicalFlaUiControlTypes =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "AppBar", "AppBar" },
                { "Button", "Button" },
                { "Calendar", "Calendar" },
                { "CheckBox", "CheckBox" },
                { "ComboBox", "ComboBox" },
                { "Custom", "Custom" },
                { "DataGrid", "DataGrid" },
                { "DataItem", "DataItem" },
                { "Document", "Document" },
                { "Edit", "Edit" },
                { "Group", "Group" },
                { "Header", "Header" },
                { "HeaderItem", "HeaderItem" },
                { "Hyperlink", "Hyperlink" },
                { "Image", "Image" },
                { "List", "List" },
                { "ListItem", "ListItem" },
                { "Menu", "Menu" },
                { "MenuBar", "MenuBar" },
                { "MenuItem", "MenuItem" },
                { "Pane", "Pane" },
                { "ProgressBar", "ProgressBar" },
                { "RadioButton", "RadioButton" },
                { "ScrollBar", "ScrollBar" },
                { "SemanticZoom", "SemanticZoom" },
                { "Separator", "Separator" },
                { "Slider", "Slider" },
                { "Spinner", "Spinner" },
                { "SplitButton", "SplitButton" },
                { "StatusBar", "StatusBar" },
                { "Tab", "Tab" },
                { "TabItem", "TabItem" },
                { "Table", "Table" },
                { "Text", "Text" },
                { "Thumb", "Thumb" },
                { "TitleBar", "TitleBar" },
                { "ToolBar", "ToolBar" },
                { "ToolTip", "ToolTip" },
                { "Tree", "Tree" },
                { "TreeItem", "TreeItem" },
                { "Window", "Window" }
            };

        public static IReadOnlyCollection<string> KnownFlaUiControlTypes => CanonicalFlaUiControlTypes.Keys;

        public static bool TryGetCanonicalFlaUiControlType(string? controlType, out string canonicalControlType)
        {
            if (!string.IsNullOrWhiteSpace(controlType) &&
                CanonicalFlaUiControlTypes.TryGetValue(controlType!.Trim(), out var canonical))
            {
                canonicalControlType = canonical;
                return true;
            }

            canonicalControlType = "";
            return false;
        }

        public static string EscapeString(string? value)
        {
            return (value ?? "")
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }

        public static string EscapeSingleQuoted(string? value)
        {
            return (value ?? "")
                .Replace("\\", "\\\\")
                .Replace("'", "\\'")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }

        public static string EscapeVerbatimString(string? value)
        {
            return (value ?? "").Replace("\"", "\"\"");
        }

        public static string EscapeComment(string? value)
        {
            return (value ?? "")
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Replace("\t", " ");
        }

        public static string EscapeRegex(string? value)
        {
            return Regex.Escape(value ?? "");
        }

        public static string ToIdentifier(string? value, string fallback)
        {
            var parts = (value ?? "")
                .Split(new[] { ' ', '-', '_', '.', '/', '\\', ':', ';', ',', '(', ')', '[', ']', '{', '}', '\'', '"' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(CleanIdentifierPart)
                .Where(part => part.Length > 0)
                .ToList();
            var identifier = string.Concat(parts);
            if (identifier.Length == 0)
            {
                identifier = fallback;
            }

            if (!char.IsLetter(identifier[0]) && identifier[0] != '_')
            {
                identifier = "_" + identifier;
            }

            return identifier;
        }

        public static string CleanIdentifierPart(string? value)
        {
            var chars = (value ?? "").Where(char.IsLetterOrDigit).ToArray();
            if (chars.Length == 0)
            {
                return "";
            }

            return char.ToUpperInvariant(chars[0]) + new string(chars.Skip(1).ToArray());
        }
    }
}
