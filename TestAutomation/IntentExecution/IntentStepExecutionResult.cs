using AutomationSandbox.IntentAutomation;

namespace AutomationSandbox.IntentExecution
{
    /// <summary>The outcome of actually performing one <see cref="IntentStep" /> against a live session.</summary>
    public sealed class IntentStepExecutionResult
    {
        /// <summary>Creates a step execution result.</summary>
        public IntentStepExecutionResult(IntentStep step, bool success, string? cssSelector, string diagnostic)
        {
            Step = step;
            Success = success;
            CssSelector = cssSelector;
            Diagnostic = diagnostic;
        }

        /// <summary>The scenario step this result is for.</summary>
        public IntentStep Step { get; }
        /// <summary>Whether the step's action (or assertion) succeeded.</summary>
        public bool Success { get; }
        /// <summary>The CSS selector the executor acted on, or null for a step that needed no element
        /// (<c>Navigate</c>, and <c>Assert</c> with a page-level <see cref="AssertionKind" />).</summary>
        public string? CssSelector { get; }
        /// <summary>Human-readable explanation of the outcome.</summary>
        public string Diagnostic { get; }
    }
}
