namespace AutomationSandbox.LlmHealing
{
    // DTO for configuring an LLM provider dynamically from JSON or configuration sources.
    /// <summary>DTO for configuring an LLM provider dynamically from JSON or configuration sources.</summary>
    public class LlmProviderConfiguration
    {
        /// <summary>Unique provider name used to associate independent votes and diagnostics within an evaluation.</summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>Provider endpoint override, used for compatible or self-hosted services.</summary>
        public string? Endpoint { get; set; }
        /// <summary>Provider-specific model identifier sent with each request.</summary>
        public string? Model { get; set; }
        /// <summary>Explicit credential for this provider; prefer environment-backed configuration and never persist credentials in source control.</summary>
        public string? ApiKey { get; set; }
        /// <summary>Name of the environment variable supplying this provider&apos;s credential.</summary>
        public string? ApiKeyEnvVar { get; set; }
        /// <summary>Optional per-attempt deadline in seconds.</summary>
        public int? TimeoutSeconds { get; set; }
        /// <summary>Optional deadline in seconds for the complete operation, including retries.</summary>
        public int? TotalTimeoutSeconds { get; set; }
        /// <summary>Maximum number of retries after the initial attempt; zero disables retries.</summary>
        public int? MaxRetries { get; set; }
    }
}
