namespace IntentAutomation
{
    /// <summary>
    /// Categorizes the expected outcome of an assertion step in an intent-driven test scenario.
    /// </summary>
    public enum AssertionKind
    {
        /// <summary>
        /// No explicit assertion kind identified; defaults to presence or review check depending on AssertGenerationMode.
        /// </summary>
        None,

        /// <summary>
        /// Asserts that the target UI element is visible in the active DOM or desktop UI tree. (Platform-neutral)
        /// </summary>
        Visible,

        /// <summary>
        /// Asserts that the target UI element is not visible / detached / hidden. (Platform-neutral)
        /// </summary>
        NotVisible,

        /// <summary>
        /// Asserts that the target element's label/text exactly matches the expected text. (Platform-neutral)
        /// </summary>
        TextEquals,

        /// <summary>
        /// Asserts that the target element's label/text contains the expected substring. (Platform-neutral)
        /// </summary>
        TextContains,

        /// <summary>
        /// Asserts that an input field or control's current value matches the expected value. (Platform-neutral)
        /// </summary>
        ValueEquals,

        /// <summary>
        /// Asserts that the current browser page URL exactly equals the expected URL. (Web-only)
        /// </summary>
        UrlEquals,

        /// <summary>
        /// Asserts that the current browser page URL contains the expected URL substring. (Web-only)
        /// </summary>
        UrlContains,
    }

    /// <summary>
    /// Platform classification and compatibility helpers for <see cref="AssertionKind"/>.
    /// </summary>
    public static class AssertionKindExtensions
    {
        /// <summary>
        /// Returns true if this assertion kind represents a browser/web-only concept (e.g. page URL).
        /// </summary>
        public static bool IsWebOnly(this AssertionKind kind)
        {
            return kind == AssertionKind.UrlEquals || kind == AssertionKind.UrlContains;
        }

        /// <summary>
        /// Returns true if this assertion kind is natively supported on desktop targets (e.g. FlaUI).
        /// </summary>
        public static bool IsSupportedOnDesktop(this AssertionKind kind)
        {
            return !kind.IsWebOnly();
        }

        /// <summary>
        /// Returns true if this assertion kind is platform-neutral across web and desktop.
        /// </summary>
        public static bool IsPlatformNeutral(this AssertionKind kind)
        {
            return !kind.IsWebOnly();
        }
    }
}

