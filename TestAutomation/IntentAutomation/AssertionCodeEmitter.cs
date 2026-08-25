using System;
using System.Text;
using System.Text.RegularExpressions;

namespace IntentAutomation
{
    public static class AssertionCodeEmitter
    {
        public static bool IsLocatorRequired(AssertionKind kind, AssertGenerationMode mode)
        {
            // Web-only assertions (e.g. page URL) are evaluated against the page, not an element,
            // so they never require a recorded element locator.
            if (kind.IsWebOnly())
            {
                return false;
            }

            if (kind == AssertionKind.None)
            {
                // In Strict mode, an unmapped assertion emits an inconclusive/failure statement
                // and does not require a recorded element locator. In Lenient/Fallback mode,
                // it falls back to a visibility/presence check that does require a locator.
                return mode != AssertGenerationMode.Strict;
            }

            return true;
        }

        public static void EmitPlaywrightCSharp(
            IntentStep step,
            string? locatorExpression,
            AssertGenerationMode mode,
            StringBuilder code)
        {
            switch (step.AssertionKind)
            {
                case AssertionKind.Visible:
                    code.AppendLine($"            await Expect({locatorExpression}).ToBeVisibleAsync();");
                    break;
                case AssertionKind.NotVisible:
                    code.AppendLine($"            await Expect({locatorExpression}).ToBeHiddenAsync();");
                    break;
                case AssertionKind.TextEquals:
                    code.AppendLine($"            await Expect({locatorExpression}).ToHaveTextAsync(\"{CodeGenerationUtilities.EscapeString(step.ExpectedValue)}\");");
                    break;
                case AssertionKind.TextContains:
                    code.AppendLine($"            await Expect({locatorExpression}).ToContainTextAsync(\"{CodeGenerationUtilities.EscapeString(step.ExpectedValue)}\");");
                    break;
                case AssertionKind.ValueEquals:
                    code.AppendLine($"            await Expect({locatorExpression}).ToHaveValueAsync(\"{CodeGenerationUtilities.EscapeString(step.ExpectedValue)}\");");
                    break;
                case AssertionKind.UrlEquals:
                    code.AppendLine($"            await Expect(Page).ToHaveURLAsync(\"{CodeGenerationUtilities.EscapeString(step.ExpectedValue)}\");");
                    break;
                case AssertionKind.UrlContains:
                    code.AppendLine($"            await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(\"{CodeGenerationUtilities.EscapeRegex(step.ExpectedValue)}\"));");
                    break;
                default:
                    switch (mode)
                    {
                        case AssertGenerationMode.Strict:
                            code.AppendLine($"            Assert.Inconclusive(\"Review: Unmapped assertion outcome '{CodeGenerationUtilities.EscapeString(step.ExpectedOutcome)}'.\");");
                            break;
                        case AssertGenerationMode.Lenient:
                            code.AppendLine($"            // TODO: Review unmapped expected outcome: {CodeGenerationUtilities.EscapeComment(step.ExpectedOutcome)}");
                            code.AppendLine($"            await Expect({locatorExpression}).ToBeVisibleAsync();");
                            break;
                        case AssertGenerationMode.Fallback:
                            code.AppendLine($"            await Expect({locatorExpression}).ToBeVisibleAsync();");
                            break;
                    }
                    break;
            }
        }

        public static void EmitPlaywrightTypeScript(
            IntentStep step,
            string? locatorExpression,
            AssertGenerationMode mode,
            StringBuilder code)
        {
            switch (step.AssertionKind)
            {
                case AssertionKind.Visible:
                    code.AppendLine($"  await expect({locatorExpression}).toBeVisible();");
                    break;
                case AssertionKind.NotVisible:
                    code.AppendLine($"  await expect({locatorExpression}).toBeHidden();");
                    break;
                case AssertionKind.TextEquals:
                    code.AppendLine($"  await expect({locatorExpression}).toHaveText('{CodeGenerationUtilities.EscapeSingleQuoted(step.ExpectedValue)}');");
                    break;
                case AssertionKind.TextContains:
                    code.AppendLine($"  await expect({locatorExpression}).toContainText('{CodeGenerationUtilities.EscapeSingleQuoted(step.ExpectedValue)}');");
                    break;
                case AssertionKind.ValueEquals:
                    code.AppendLine($"  await expect({locatorExpression}).toHaveValue('{CodeGenerationUtilities.EscapeSingleQuoted(step.ExpectedValue)}');");
                    break;
                case AssertionKind.UrlEquals:
                    code.AppendLine($"  await expect(page).toHaveURL('{CodeGenerationUtilities.EscapeSingleQuoted(step.ExpectedValue)}');");
                    break;
                case AssertionKind.UrlContains:
                    code.AppendLine($"  await expect(page).toHaveURL(new RegExp('{CodeGenerationUtilities.EscapeRegex(step.ExpectedValue)}'));");
                    break;
                default:
                    switch (mode)
                    {
                        case AssertGenerationMode.Strict:
                            code.AppendLine($"  test.skip(true, 'Review: Unmapped assertion outcome {CodeGenerationUtilities.EscapeSingleQuoted(step.ExpectedOutcome)}.');");
                            break;
                        case AssertGenerationMode.Lenient:
                            code.AppendLine($"  // TODO: Review unmapped expected outcome: {CodeGenerationUtilities.EscapeComment(step.ExpectedOutcome)}");
                            code.AppendLine($"  await expect({locatorExpression}).toBeVisible();");
                            break;
                        case AssertGenerationMode.Fallback:
                            code.AppendLine($"  await expect({locatorExpression}).toBeVisible();");
                            break;
                    }
                    break;
            }
        }

        public static void EmitFlaUiCSharp(
            IntentStep step,
            string? findExpression,
            AssertGenerationMode mode,
            StringBuilder code)
        {
            if (step.AssertionKind.IsWebOnly())
            {
                switch (mode)
                {
                    case AssertGenerationMode.Strict:
                        code.AppendLine("            Assert.True(false, \"Review: URL assertions are not supported on desktop targets.\");");
                        break;
                    case AssertGenerationMode.Lenient:
                        code.AppendLine($"            // TODO: Review unmapped desktop URL assertion: {CodeGenerationUtilities.EscapeComment(step.ExpectedOutcome)}");
                        code.AppendLine("            Assert.NotNull(window);");
                        break;
                    case AssertGenerationMode.Fallback:
                        code.AppendLine("            Assert.NotNull(window);");
                        break;
                }
                return;
            }

            switch (step.AssertionKind)
            {
                case AssertionKind.Visible:
                    code.AppendLine($"            Assert.NotNull(window.{findExpression});");
                    break;
                case AssertionKind.NotVisible:
                    code.AppendLine($"            Assert.Null(window.{findExpression});");
                    break;
                case AssertionKind.TextEquals:
                    // In UIA, Name surfaces static label text, headers, and element names.
                    code.AppendLine($"            Assert.Equal(\"{CodeGenerationUtilities.EscapeString(step.ExpectedValue)}\", window.{findExpression}!.Name);");
                    break;
                case AssertionKind.TextContains:
                    code.AppendLine($"            Assert.Contains(\"{CodeGenerationUtilities.EscapeString(step.ExpectedValue)}\", window.{findExpression}!.Name);");
                    break;
                case AssertionKind.ValueEquals:
                    // In FlaUI, AsTextBox().Text accesses editable input field values.
                    code.AppendLine($"            Assert.Equal(\"{CodeGenerationUtilities.EscapeString(step.ExpectedValue)}\", window.{findExpression}!.AsTextBox().Text);");
                    break;
                default:
                    switch (mode)
                    {
                        case AssertGenerationMode.Strict:
                            code.AppendLine($"            Assert.True(false, \"Review: Unmapped assertion outcome '{CodeGenerationUtilities.EscapeString(step.ExpectedOutcome)}'.\");");
                            break;
                        case AssertGenerationMode.Lenient:
                            code.AppendLine($"            // TODO: Review unmapped expected outcome: {CodeGenerationUtilities.EscapeComment(step.ExpectedOutcome)}");
                            code.AppendLine($"            Assert.NotNull(window.{findExpression});");
                            break;
                        case AssertGenerationMode.Fallback:
                            code.AppendLine($"            Assert.NotNull(window.{findExpression});");
                            break;
                    }
                    break;
            }
        }
    }
}
