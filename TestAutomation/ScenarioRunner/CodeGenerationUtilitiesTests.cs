using IntentAutomation;
using Xunit;

namespace ScenarioRunner
{
    public class CodeGenerationUtilitiesTests
    {
        [Fact]
        public void EscapeString_EscapesSpecialCharactersAndNewlines()
        {
            var input = "line1\r\nline2\t\"quoted\"\\backslash";
            var escaped = CodeGenerationUtilities.EscapeString(input);

            Assert.Equal("line1\\r\\nline2\\t\\\"quoted\\\"\\\\backslash", escaped);
        }

        [Fact]
        public void EscapeString_HandlesNullAndEmpty()
        {
            Assert.Equal("", CodeGenerationUtilities.EscapeString(null));
            Assert.Equal("", CodeGenerationUtilities.EscapeString(""));
        }

        [Fact]
        public void EscapeSingleQuoted_EscapesSingleQuotesAndSpecialCharacters()
        {
            var input = "it's\r\na\ttest\\path";
            var escaped = CodeGenerationUtilities.EscapeSingleQuoted(input);

            Assert.Equal("it\\'s\\r\\na\\ttest\\\\path", escaped);
        }

        [Fact]
        public void EscapeVerbatimString_EscapesQuotesForVerbatimLiterals()
        {
            var input = "path\\with\\\"quotes\"";
            var escaped = CodeGenerationUtilities.EscapeVerbatimString(input);

            Assert.Equal("path\\with\\\"\"quotes\"\"", escaped);
        }

        [Fact]
        public void EscapeComment_ReplacesNewlinesAndTabsWithSpaces()
        {
            var input = "Line 1\r\nLine 2\twith tab";
            var escaped = CodeGenerationUtilities.EscapeComment(input);

            Assert.Equal("Line 1  Line 2 with tab", escaped);
        }

        [Fact]
        public void EscapeRegex_EscapesPatternSpecialCharacters()
        {
            var input = "https://example.test/items?id=1&name=test";
            var escaped = CodeGenerationUtilities.EscapeRegex(input);

            Assert.Equal(@"https://example\.test/items\?id=1&name=test", escaped);
        }

        [Fact]
        public void ToIdentifier_GeneratesValidPascalCaseIdentifiers()
        {
            Assert.Equal("CreateACustomerRecord", CodeGenerationUtilities.ToIdentifier("create a customer record", "Default"));
            Assert.Equal("BtnSubmitOrder", CodeGenerationUtilities.ToIdentifier("btn_submit-order", "Default"));
            Assert.Equal("_123NumericStart", CodeGenerationUtilities.ToIdentifier("123-numeric-start", "Default"));
            Assert.Equal("FallbackName", CodeGenerationUtilities.ToIdentifier("", "FallbackName"));
            Assert.Equal("FallbackName", CodeGenerationUtilities.ToIdentifier("   ", "FallbackName"));
        }

        [Theory]
        [InlineData("Button", true, "Button")]
        [InlineData("button", true, "Button")]
        [InlineData("BUTTON", true, "Button")]
        [InlineData("Edit", true, "Edit")]
        [InlineData("edit", true, "Edit")]
        [InlineData("Pane", true, "Pane")]
        [InlineData("Custom", true, "Custom")]
        [InlineData("DataGrid", true, "DataGrid")]
        [InlineData("datagrid", true, "DataGrid")]
        [InlineData("NonExistentType", false, "")]
        [InlineData("CustomGridWidget", false, "")]
        [InlineData("", false, "")]
        [InlineData(null, false, "")]
        public void TryGetCanonicalFlaUiControlType_ValidatesKnownTypesCaseInsensitively(
            string? input,
            bool expectedSuccess,
            string expectedCanonical)
        {
            var success = CodeGenerationUtilities.TryGetCanonicalFlaUiControlType(input, out var canonical);

            Assert.Equal(expectedSuccess, success);
            Assert.Equal(expectedCanonical, canonical);
        }

        [Fact]
        public void KnownFlaUiControlTypes_ContainsExpectedCanonicalControlTypes()
        {
            var types = CodeGenerationUtilities.KnownFlaUiControlTypes;

            Assert.Equal(41, types.Count);
            Assert.Contains("Button", types);
            Assert.Contains("Edit", types);
            Assert.Contains("Window", types);
            Assert.Contains("DataGrid", types);
            Assert.Contains("Pane", types);
        }

#if NETFRAMEWORK
        [Fact]
        public void CanonicalFlaUiControlTypes_MatchesAllFlaUiCoreEnumMembersBidirectionally()
        {
            var enumNames = System.Enum.GetNames(typeof(FlaUI.Core.Definitions.ControlType));
            var knownTypes = CodeGenerationUtilities.KnownFlaUiControlTypes;

            // 1. Cardinality check
            Assert.Equal(enumNames.Length, knownTypes.Count);

            var enumSet = new System.Collections.Generic.HashSet<string>(enumNames, System.StringComparer.OrdinalIgnoreCase);

            // 2. Forward check: enum -> dictionary
            foreach (var name in enumNames)
            {
                var success = CodeGenerationUtilities.TryGetCanonicalFlaUiControlType(name, out var canonical);
                Assert.True(success, $"FlaUI.Core.Definitions.ControlType.{name} is missing from CodeGenerationUtilities.CanonicalFlaUiControlTypes.");
                Assert.Equal(name, canonical);
            }

            // 3. Reverse check: dictionary -> enum (ensures no spurious/typo entries)
            foreach (var known in knownTypes)
            {
                Assert.True(enumSet.Contains(known), $"CodeGenerationUtilities.KnownFlaUiControlTypes contains '{known}' which does not exist in FlaUI.Core.Definitions.ControlType.");
            }
        }
#endif
    }
}
