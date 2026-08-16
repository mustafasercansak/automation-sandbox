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

            // 6. Groq - deliberately listed next to Grok because the names collide by one letter
            // and they are unrelated companies. Separate keys, separate endpoints, separate
            // provider names; ILlmHealingProvider.Name uniqueness is what keeps their votes
            // distinguishable when both are configured.
            // Both of these require their model to be configured explicitly, and are skipped
            // otherwise. There is no safe default to fall back on: a guessed id is worse than
            // none - "grok-2-latest" was a guess and was never once valid - and leaving it null
            // is worse still, because OpenAiHealingProvider would then fall back to OPENAI_MODEL
            // (set for a different vendor in the same run) and finally to "gpt-4o-mini", sending
            // an OpenAI model name to Groq. Skipping is the loud failure: the provider is simply
            // absent from ConfiguredProviders in the report, rather than present and wrong.
            var groqKey = Env("GROQ_API_KEY");
            var groqModel = Env("GROQ_MODEL");
            if (groqKey != null && groqModel != null)
            {
                providers.Add(new OpenAiHealingProvider(
                    httpClient: httpClient,
                    apiKey: groqKey,
                    model: groqModel,
                    endpoint: Env("GROQ_ENDPOINT") ?? "https://api.groq.com/openai/v1",
                    name: "Groq"));
            }

            // 7. OpenRouter - a router rather than a model vendor, so the model id decides which
            // model actually answers. Choose it from a different family than the other configured
            // providers: two providers proxying the same model are one voter with two names, and
            // consensus between them is meaningless (#19).
            var openRouterKey = Env("OPENROUTER_API_KEY");
            var openRouterModel = Env("OPENROUTER_MODEL");
            if (openRouterKey != null && openRouterModel != null)
            {
                providers.Add(new OpenAiHealingProvider(
                    httpClient: httpClient,
                    apiKey: openRouterKey,
                    model: openRouterModel,
                    endpoint: Env("OPENROUTER_ENDPOINT") ?? "https://openrouter.ai/api/v1",
                    name: "OpenRouter"));
            }

            // 8. Cloudflare Workers AI - the account id is part of the endpoint, and model ids
            // are account/plan dependent. Requiring all three values avoids constructing a
            // provider that can only fail with a malformed URL or an unavailable guessed model.
            var cloudflareKey = Env("CLOUDFLARE_API_TOKEN");
            var cloudflareAccountId = Env("CLOUDFLARE_ACCOUNT_ID");
            var cloudflareModel = Env("CLOUDFLARE_MODEL");
            if (cloudflareKey != null && cloudflareAccountId != null && cloudflareModel != null)
            {
                providers.Add(new OpenAiHealingProvider(
                    httpClient: httpClient,
                    apiKey: cloudflareKey,
                    model: cloudflareModel,
                    endpoint: $"https://api.cloudflare.com/client/v4/accounts/{cloudflareAccountId}/ai/v1",
                    name: "Cloudflare"));
            }

            // 9. Mistral - OpenAI-compatible, so no provider class of its own. Both values are
            // required for the same reason as Cloudflare: a guessed model name produces a provider
            // that authenticates and then fails every request, which is worse than no provider.
            var mistralKey = Env("MISTRAL_API_KEY");
            var mistralModel = Env("MISTRAL_MODEL");
            if (mistralKey != null && mistralModel != null)
            {
                providers.Add(new OpenAiHealingProvider(
                    httpClient: httpClient,
                    apiKey: mistralKey,
                    model: mistralModel,
                    endpoint: "https://api.mistral.ai/v1",
                    name: "Mistral"));
            }

            // 10. NVIDIA NIM - OpenAI-compatible, so no provider class of its own. Both values are
            // required for the same reason as Cloudflare and Mistral.
            var nvidiaKey = Env("NVIDIA_API_KEY");
            var nvidiaModel = Env("NVIDIA_MODEL");
            if (nvidiaKey != null && nvidiaModel != null)
            {
                providers.Add(new OpenAiHealingProvider(
                    httpClient: httpClient,
                    apiKey: nvidiaKey,
                    model: nvidiaModel,
                    endpoint: "https://integrate.api.nvidia.com/v1",
                    name: "Nvidia"));
            }

            // 11. Ollama Cloud - a hosted OpenAI-compatible endpoint, entirely separate from the
            // local daemon below. The variables are deliberately not shared: OLLAMA_MODEL pointing at
            // a cloud model would build an OllamaHealingProvider aimed at localhost:11434, which does
            // not exist on a CI runner. That provider would then fail every request while still
            // counting toward the two-provider consensus threshold - the opposite of the reason for
            // adding it. See #114.
            var ollamaCloudKey = Env("OLLAMA_CLOUD_API_KEY");
            var ollamaCloudModel = Env("OLLAMA_CLOUD_MODEL");
            if (ollamaCloudKey != null && ollamaCloudModel != null)
            {
                providers.Add(new OpenAiHealingProvider(
                    httpClient: httpClient,
                    apiKey: ollamaCloudKey,
                    model: ollamaCloudModel,
                    endpoint: "https://ollama.com/v1",
                    name: "OllamaCloud"));
            }

            // 12. Ollama (local daemon)
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

            // 13. Custom providers via LLM_CUSTOM_PROVIDERS JSON array
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
