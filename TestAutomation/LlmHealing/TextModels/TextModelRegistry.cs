using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;

namespace AutomationSandbox.LlmHealing
{
    /// <summary>The set of <see cref="ITextModel" />s a run can choose from, looked up by name. A name is unique and
    /// compared ignoring case, so a label such as <c>Model::mistral</c> can be resolved without guessing.</summary>
    public sealed class TextModelRegistry
    {
        private readonly Dictionary<string, ITextModel> _models = new Dictionary<string, ITextModel>(StringComparer.OrdinalIgnoreCase);
        private readonly List<ITextModel> _ordered = new List<ITextModel>();

        /// <summary>Every registered model in registration order, available or not.</summary>
        public IReadOnlyList<ITextModel> Models => _ordered;

        /// <summary>Registers <paramref name="model" />. A name that is already taken is rejected rather than replaced,
        /// because a silent replacement would send a task to a different model than the label named.</summary>
        public void Add(ITextModel model)
        {
            if (model == null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            if (_models.ContainsKey(model.Name))
            {
                throw new ArgumentException($"A text model named \"{model.Name}\" is already registered.", nameof(model));
            }

            _models.Add(model.Name, model);
            _ordered.Add(model);
        }

        /// <summary>The model called <paramref name="name" />, ignoring case, or null when there is none.</summary>
        public ITextModel? Find(string name)
        {
            return name != null && _models.TryGetValue(name.Trim(), out var model) ? model : null;
        }

        /// <summary>Builds the registry from the same environment variables <see cref="LlmProviderFactory" /> reads:
        /// <c>ANTHROPIC_API_KEY</c> (and optional <c>ANTHROPIC_MODEL</c>), <c>GEMINI_API_KEY</c> (and optional
        /// <c>GEMINI_MODEL</c>), <c>OPENAI_API_KEY</c> (and optional <c>OPENAI_MODEL</c>), and the key-plus-required-model
        /// pairs <c>MISTRAL_API_KEY</c>/<c>MISTRAL_MODEL</c>, <c>NVIDIA_API_KEY</c>/<c>NVIDIA_MODEL</c> and
        /// <c>OLLAMA_CLOUD_API_KEY</c>/<c>OLLAMA_CLOUD_MODEL</c> (<c>OllamaCloud</c>, the hosted endpoint). A local Ollama
        /// daemon (<c>Ollama</c>) is registered when <c>OLLAMA_MODEL</c> is set; its host is <c>OLLAMA_HOST</c> (default
        /// <c>http://localhost:11434</c>) and it needs no key. The two Ollama variable sets are separate on purpose: a cloud
        /// model name must never be pointed at a local daemon. A model whose key or required model is missing is left
        /// out: a guessed model name would authenticate and then fail every request; so is a local host that is not a
        /// URL, or is plain http beyond a loopback address, because nothing is sent in clear text across a network.
        /// Cloudflare is not included yet.</summary>
        public static TextModelRegistry CreateFromEnvironment(HttpClient? httpClient = null, Func<string, string?>? getEnv = null)
        {
            getEnv ??= Environment.GetEnvironmentVariable;
            string? Env(string key)
            {
                var value = getEnv(key);
                return string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
            }

            var registry = new TextModelRegistry();

            var claudeKey = Env("ANTHROPIC_API_KEY");
            if (claudeKey != null)
            {
                registry.Add(new ClaudeTextModel(claudeKey, Env("ANTHROPIC_MODEL"), httpClient: httpClient));
            }

            var geminiKey = Env("GEMINI_API_KEY");
            if (geminiKey != null)
            {
                registry.Add(new GeminiTextModel(geminiKey, Env("GEMINI_MODEL"), httpClient: httpClient));
            }

            var openAiKey = Env("OPENAI_API_KEY");
            if (openAiKey != null)
            {
                registry.Add(new ChatCompletionsTextModel("OpenAI", openAiKey, Env("OPENAI_MODEL") ?? "gpt-4o-mini", httpClient: httpClient));
            }

            var mistralKey = Env("MISTRAL_API_KEY");
            var mistralModel = Env("MISTRAL_MODEL");
            if (mistralKey != null && mistralModel != null)
            {
                registry.Add(new ChatCompletionsTextModel("Mistral", mistralKey, mistralModel, "https://api.mistral.ai/v1", httpClient));
            }

            var nvidiaKey = Env("NVIDIA_API_KEY");
            var nvidiaModel = Env("NVIDIA_MODEL");
            if (nvidiaKey != null && nvidiaModel != null)
            {
                registry.Add(new ChatCompletionsTextModel("Nvidia", nvidiaKey, nvidiaModel, "https://integrate.api.nvidia.com/v1", httpClient));
            }

            var ollamaCloudKey = Env("OLLAMA_CLOUD_API_KEY");
            var ollamaCloudModel = Env("OLLAMA_CLOUD_MODEL");
            if (ollamaCloudKey != null && ollamaCloudModel != null)
            {
                registry.Add(new ChatCompletionsTextModel("OllamaCloud", ollamaCloudKey, ollamaCloudModel, "https://ollama.com/v1", httpClient));
            }

            var ollamaModel = Env("OLLAMA_MODEL");
            if (ollamaModel != null)
            {
                try
                {
                    registry.Add(new ChatCompletionsTextModel("Ollama", null, ollamaModel, (Env("OLLAMA_HOST") ?? "http://localhost:11434").TrimEnd('/') + "/v1", httpClient, requireApiKey: false));
                }
                catch (ArgumentException)
                {
                    // An unusable host leaves the local model out.
                }
            }

            return registry;
        }
    }
}
