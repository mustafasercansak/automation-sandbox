namespace LlmHealing
{
    // DTO for configuring an LLM provider dynamically from JSON or configuration sources.
    public class LlmProviderConfiguration
    {
        public string Name { get; set; } = string.Empty;
        public string? Endpoint { get; set; }
        public string? Model { get; set; }
        public string? ApiKey { get; set; }
        public string? ApiKeyEnvVar { get; set; }
        public int? TimeoutSeconds { get; set; }
        public int? TotalTimeoutSeconds { get; set; }
        public int? MaxRetries { get; set; }
    }
}
