using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;

namespace LlmHealing
{
    // Environment-driven factory for constructing configured LLM healing providers.
    // Discovers well-known providers from environment variables and supports arbitrary
    // OpenAI-compatible endpoints via the LLM_CUSTOM_PROVIDERS JSON array.
    public static class LlmProviderFactory
    {
        public static IReadOnlyList<ILlmHealingProvider> CreateConfiguredProviders(
            HttpClient? httpClient = null,
            Func<string, string?>? getEnv = null)
        {
            getEnv ??= Environment.GetEnvironmentVariable;

            string? Env(string key)
            {
                var val = getEnv(key);
                return string.IsNullOrWhiteSpace(val) ? null : val!.Trim();
            }

            var providers = new List<ILlmHealingProvider>();

            // 1. Claude
            var anthropicKey = Env("ANTHROPIC_API_KEY");
            if (anthropicKey != null)
            {
                providers.Add(new ClaudeHealingProvider(
                    httpClient: httpClient,
                    apiKey: anthropicKey,
                    model: Env("ANTHROPIC_MODEL")));
            }

            // 2. Gemini
            var geminiKey = Env("GEMINI_API_KEY");
            if (geminiKey != null)
            {
                providers.Add(new GeminiHealingProvider(
                    httpClient: httpClient,
                    apiKey: geminiKey,
                    model: Env("GEMINI_MODEL")));
            }

            // 3. OpenAI
            var openAiKey = Env("OPENAI_API_KEY");
            if (openAiKey != null)
            {
                providers.Add(new OpenAiHealingProvider(
                    httpClient: httpClient,
                    apiKey: openAiKey,
                    model: Env("OPENAI_MODEL"),
                    endpoint: Env("OPENAI_ENDPOINT"),
                    name: "OpenAI"));
            }

            // 4. Grok (xAI)
            var grokKey = Env("GROK_API_KEY") ?? Env("XAI_API_KEY");
            if (grokKey != null)
            {
                providers.Add(new OpenAiHealingProvider(
                    httpClient: httpClient,
                    apiKey: grokKey,
                    model: Env("GROK_MODEL") ?? Env("XAI_MODEL") ?? "grok-2-latest",
                    endpoint: Env("GROK_ENDPOINT") ?? Env("XAI_ENDPOINT") ?? "https://api.x.ai/v1",
                    name: "Grok"));
            }

            // 5. Kimi (Moonshot)
            var kimiKey = Env("KIMI_API_KEY") ?? Env("MOONSHOT_API_KEY");
            if (kimiKey != null)
            {
                providers.Add(new OpenAiHealingProvider(
                    httpClient: httpClient,
                    apiKey: kimiKey,
                    model: Env("KIMI_MODEL") ?? Env("MOONSHOT_MODEL") ?? "moonshot-v1-8k",
                    endpoint: Env("KIMI_ENDPOINT") ?? Env("MOONSHOT_ENDPOINT") ?? "https://api.moonshot.cn/v1",
                    name: "Kimi"));
            }

            // 6. Ollama
            var ollamaEnabled = string.Equals(Env("OLLAMA_ENABLED"), "true", StringComparison.OrdinalIgnoreCase)
                || Env("OLLAMA_HOST") != null
                || Env("OLLAMA_MODEL") != null;

            if (ollamaEnabled)
            {
                providers.Add(new OllamaHealingProvider(
                    httpClient: httpClient,
                    host: Env("OLLAMA_HOST"),
                    model: Env("OLLAMA_MODEL")));
            }

            // 7. Custom providers via LLM_CUSTOM_PROVIDERS JSON array
            var customJson = Env("LLM_CUSTOM_PROVIDERS");
            if (customJson != null)
            {
                var customConfigs = ParseCustomProviders(customJson);
                foreach (var config in customConfigs)
                {
                    if (string.IsNullOrWhiteSpace(config.Name))
                    {
                        throw new ArgumentException("Custom LLM provider configuration must specify a non-empty Name.");
                    }

                    var apiKey = config.ApiKey ?? (config.ApiKeyEnvVar != null ? Env(config.ApiKeyEnvVar) : null);
                    if (apiKey != null)
                    {
                        TimeSpan? timeout = config.TimeoutSeconds.HasValue
                            ? TimeSpan.FromSeconds(config.TimeoutSeconds.Value)
                            : null;
                        TimeSpan? totalTimeout = config.TotalTimeoutSeconds.HasValue
                            ? TimeSpan.FromSeconds(config.TotalTimeoutSeconds.Value)
                            : null;

                        providers.Add(new OpenAiHealingProvider(
                            httpClient: httpClient,
                            apiKey: apiKey,
                            model: config.Model,
                            endpoint: config.Endpoint,
                            name: config.Name.Trim(),
                            timeout: timeout,
                            totalTimeout: totalTimeout,
                            maxRetries: config.MaxRetries));
                    }
                }
            }

            // Keep only available providers
            var available = providers.Where(p => p.IsAvailable).ToList();

            // Validate that every provider has a unique Name within the run
            var duplicates = available
                .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicates.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Duplicate LLM provider names detected: {string.Join(", ", duplicates)}. " +
                    "Each configured provider must have a unique Name within a run.");
            }

            return available;
        }

        private static List<LlmProviderConfiguration> ParseCustomProviders(string json)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            };

            return JsonSerializer.Deserialize<List<LlmProviderConfiguration>>(json, options)
                ?? new List<LlmProviderConfiguration>();
        }
    }
}
