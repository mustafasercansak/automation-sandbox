using System;
using System.Text.RegularExpressions;

namespace UiModel
{
    /// <summary>
    /// Built-in text sanitizer that masks common sensitive patterns (emails, credit card numbers,
    /// bearer tokens, API keys, passwords/secrets, and social security numbers) before DOM/UI-tree
    /// and test intent data are transmitted to LLM providers.
    /// </summary>
    public static class SensitiveDataSanitizer
    {
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

        // Standard email address pattern
        internal const string EmailPattern = @"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}";
        private static readonly Regex EmailRegex = new(EmailPattern, RegexOptions.Compiled, RegexTimeout);

        // Credit / Debit card patterns (13 to 19 digits, formatted with spaces/dashes or continuous)
        internal const string CreditCardPattern = @"(?<!\d)(?:\d{4}[ -]\d{4}[ -]\d{4}[ -]\d{1,7}|\d{4}[ -]\d{6}[ -]\d{4,5}|\d{13,19})(?!\d)";
        private static readonly Regex CreditCardRegex = new(CreditCardPattern, RegexOptions.Compiled, RegexTimeout);

        // US Social Security Number pattern (XXX-XX-XXXX)
        internal const string SsnPattern = @"(?<!\d)\d{3}-\d{2}-\d{4}(?!\d)";
        private static readonly Regex SsnRegex = new(SsnPattern, RegexOptions.Compiled, RegexTimeout);

        // Bearer token pattern: Bearer <token of length >= 16>
        internal const string BearerTokenPattern = @"(?i)(Bearer\s+)[A-Za-z0-9_\-\.]{16,}";
        private static readonly Regex BearerTokenRegex = new(BearerTokenPattern, RegexOptions.Compiled, RegexTimeout);

        // Common prefixed API keys and tokens (OpenAI, GitHub, GitLab, Slack, AWS, JWT).
        // Each prefix requires its real-world separator (e.g. "sk-", "ghp_") rather than
        // matching bare on the 2-3 letter prefix alone - otherwise ordinary kebab-case
        // AutomationIds/Names like "skip-intro-button" or "ghost-mode-toggle" (which
        // start with "sk"/"gho" and are all letters/digits/hyphens, same as the original
        // charset) are false-positively redacted in full, destroying the exact identifying
        // text this sanitizer's own callers (e.g. LlmHealingPrompt's candidate list) need
        // to disambiguate elements. AWS AKIA/ASIA keys have no separator in the real format,
        // but requiring an uppercase-only suffix keeps them from matching lowercase identifiers.
        internal const string PrefixedSecretPattern =
            @"(?<=^|[^a-zA-Z0-9])(?:sk-[a-zA-Z0-9_\-]{16,}|gh[pousr]_[a-zA-Z0-9_\-]{16,}|glpat-[a-zA-Z0-9_\-]{16,}|xox[bp]-[a-zA-Z0-9_\-]{16,}|(?:AKIA|ASIA)[0-9A-Z]{16})(?=$|[^a-zA-Z0-9])|(?<=^|[^a-zA-Z0-9])eyJ[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+(?=$|[^a-zA-Z0-9])";
        private static readonly Regex PrefixedSecretRegex = new(PrefixedSecretPattern, RegexOptions.Compiled, RegexTimeout);

        // Key-value secret patterns (password: secret, api_key=secret, access_token: secret, etc.).
        // The value is captured to end-of-line rather than stopping at the first space/comma:
        // stopping early left a fragment of the real secret in plain text right next to the
        // redaction token (e.g. "password: my secret pass" -> "password:[REDACTED_SECRET] secret
        // pass"). This is a privacy-safety pass, so over-redacting the rest of the line is the
        // safer failure mode than leaking part of a multi-word or punctuated secret.
        internal const string KeyValueSecretPattern = @"(?i)(password|passwd|secret|api[_-]?key|access[_-]?token|auth[_-]?token)\s*([:=])\s*(.+)";
        private static readonly Regex KeyValueSecretRegex = new(KeyValueSecretPattern, RegexOptions.Compiled, RegexTimeout);

        /// <summary>
        /// Default redaction delegate that applies all built-in pattern masks.
        /// </summary>
        public static readonly Func<string, string> Default = text => Redact(text) ?? "";

        /// <summary>
        /// Pass-through delegate for consumers opting out of text redaction.
        /// </summary>
        public static readonly Func<string, string> PassThrough = text => text;

        /// <summary>
        /// Redacts sensitive patterns in the input text, replacing them with standard redaction tokens.
        /// </summary>
        /// <param name="input">The text to sanitize.</param>
        /// <returns>The sanitized text, or null if input is null.</returns>
        public static string? Redact(string? input)
        {
            return RedactCore(
                input,
                KeyValueSecretRegex,
                BearerTokenRegex,
                PrefixedSecretRegex,
                EmailRegex,
                CreditCardRegex,
                SsnRegex);
        }

        /// <summary>
        /// Test-only seam: runs the exact same redaction pipeline as <see cref="Redact"/>, but
        /// against ad-hoc <see cref="Regex"/> instances built with an injected timeout, so
        /// <see cref="RegexMatchTimeoutException"/> handling can be verified deterministically
        /// (a near-zero timeout reliably trips) instead of relying on a pathological/catastrophic
        /// -backtracking input to exceed the real 1-second production timeout.
        /// </summary>
        internal static string? RedactWithTimeout(string? input, TimeSpan timeout)
        {
            return RedactCore(
                input,
                new Regex(KeyValueSecretPattern, RegexOptions.None, timeout),
                new Regex(BearerTokenPattern, RegexOptions.None, timeout),
                new Regex(PrefixedSecretPattern, RegexOptions.None, timeout),
                new Regex(EmailPattern, RegexOptions.None, timeout),
                new Regex(CreditCardPattern, RegexOptions.None, timeout),
                new Regex(SsnPattern, RegexOptions.None, timeout));
        }

        private static string? RedactCore(
            string? input,
            Regex keyValueSecretRegex,
            Regex bearerTokenRegex,
            Regex prefixedSecretRegex,
            Regex emailRegex,
            Regex creditCardRegex,
            Regex ssnRegex)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }

            try
            {
                var text = input!;

                // 1. Redact key-value secrets (e.g. password: xyz)
                text = keyValueSecretRegex.Replace(text, "$1$2[REDACTED_SECRET]");

                // 2. Redact Bearer tokens
                text = bearerTokenRegex.Replace(text, "$1[REDACTED_SECRET]");

                // 3. Redact prefixed API keys and JWTs
                text = prefixedSecretRegex.Replace(text, "[REDACTED_SECRET]");

                // 4. Redact email addresses
                text = emailRegex.Replace(text, "[REDACTED_EMAIL]");

                // 5. Redact credit card numbers
                text = creditCardRegex.Replace(text, "[REDACTED_CARD]");

                // 6. Redact SSNs
                text = ssnRegex.Replace(text, "[REDACTED_SSN]");

                return text;
            }
            catch (RegexMatchTimeoutException)
            {
                // Fail safe, not fail open: a pathological input that times out one of the
                // patterns above must not fall back to returning the raw, unredacted text to
                // an LLM prompt. Suppress the whole string rather than risk sending it verbatim.
                return "[REDACTION_TIMEOUT]";
            }
        }
    }
}
