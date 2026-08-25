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
        private static readonly Regex EmailRegex = new(
            @"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}",
            RegexOptions.Compiled,
            RegexTimeout);

        // Credit / Debit card patterns (13 to 19 digits, formatted with spaces/dashes or continuous)
        private static readonly Regex CreditCardRegex = new(
            @"(?<!\d)(?:\d{4}[ -]\d{4}[ -]\d{4}[ -]\d{1,7}|\d{4}[ -]\d{6}[ -]\d{4,5}|\d{13,19})(?!\d)",
            RegexOptions.Compiled,
            RegexTimeout);

        // US Social Security Number pattern (XXX-XX-XXXX)
        private static readonly Regex SsnRegex = new(
            @"(?<!\d)\d{3}-\d{2}-\d{4}(?!\d)",
            RegexOptions.Compiled,
            RegexTimeout);

        // Bearer token pattern: Bearer <token of length >= 16>
        private static readonly Regex BearerTokenRegex = new(
            @"(?i)(Bearer\s+)[A-Za-z0-9_\-\.]{16,}",
            RegexOptions.Compiled,
            RegexTimeout);

        // Common prefixed API keys and tokens (OpenAI, GitHub, GitLab, Slack, AWS, JWT)
        private static readonly Regex PrefixedSecretRegex = new(
            @"(?<=^|[^a-zA-Z0-9])(?:sk|ghp|gho|ghu|ghs|ghr|glpat|xoxb|xoxp|AKIA|ASIA)[a-zA-Z0-9_\-]{16,}(?=$|[^a-zA-Z0-9])|(?<=^|[^a-zA-Z0-9])eyJ[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+(?=$|[^a-zA-Z0-9])",
            RegexOptions.Compiled,
            RegexTimeout);

        // Key-value secret patterns (password: secret, api_key=secret, access_token: secret, etc.)
        private static readonly Regex KeyValueSecretRegex = new(
            @"(?i)(password|passwd|secret|api[_-]?key|access[_-]?token|auth[_-]?token)\s*([:=])\s*([^\s,;""']+)",
            RegexOptions.Compiled,
            RegexTimeout);

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
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }

            var text = input!;

            // 1. Redact key-value secrets (e.g. password: xyz)
            text = KeyValueSecretRegex.Replace(text, "$1$2[REDACTED_SECRET]");

            // 2. Redact Bearer tokens
            text = BearerTokenRegex.Replace(text, "$1[REDACTED_SECRET]");

            // 3. Redact prefixed API keys and JWTs
            text = PrefixedSecretRegex.Replace(text, "[REDACTED_SECRET]");

            // 4. Redact email addresses
            text = EmailRegex.Replace(text, "[REDACTED_EMAIL]");

            // 5. Redact credit card numbers
            text = CreditCardRegex.Replace(text, "[REDACTED_CARD]");

            // 6. Redact SSNs
            text = SsnRegex.Replace(text, "[REDACTED_SSN]");

            return text;
        }
    }
}
