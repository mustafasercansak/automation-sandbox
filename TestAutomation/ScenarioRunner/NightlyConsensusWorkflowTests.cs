using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace ScenarioRunner
{
    // Guards .github/workflows/nightly-consensus.yml against silently falling behind
    // LlmProviderFactory's provider list.
    //
    // #121: Mistral (#116) and Ollama Cloud (#114) were wired into the factory and into
    // ablation-consensus.yml, but nightly-consensus.yml was never updated - a provider that is
    // never constructed looks exactly like a provider that had nothing to say, so the gap was
    // invisible in every report the nightly run produced. NVIDIA (#122) had the same gap by the
    // time this was caught, having been wired in after this workflow was last touched.
    public class NightlyConsensusWorkflowTests
    {
        // Every hosted provider LlmProviderFactory constructs from environment variables, minus:
        // - OLLAMA_HOST / OLLAMA_MODEL / OLLAMA_ENABLED, which target a local daemon on
        //   localhost:11434 that does not exist on a CI runner (#114's whole point).
        // - XAI_* / MOONSHOT_*, alternate names for GROK_* / KIMI_* rather than distinct providers.
        // - LLM_CUSTOM_PROVIDERS, an opt-in JSON array with no fixed variable names to check.
        private static readonly IReadOnlyList<string> ExpectedVariables = new[]
        {
            "ANTHROPIC_API_KEY", "ANTHROPIC_MODEL",
            "CLOUDFLARE_API_TOKEN", "CLOUDFLARE_ACCOUNT_ID", "CLOUDFLARE_MODEL",
            "GEMINI_API_KEY", "GEMINI_MODEL",
            "GROK_API_KEY", "GROK_MODEL", "GROK_ENDPOINT",
            "GROQ_API_KEY", "GROQ_MODEL", "GROQ_ENDPOINT",
            "KIMI_API_KEY", "KIMI_MODEL", "KIMI_ENDPOINT",
            "MISTRAL_API_KEY", "MISTRAL_MODEL",
            "NVIDIA_API_KEY", "NVIDIA_MODEL",
            "OLLAMA_CLOUD_API_KEY", "OLLAMA_CLOUD_MODEL",
            "OPENAI_API_KEY", "OPENAI_MODEL", "OPENAI_ENDPOINT",
            "OPENROUTER_API_KEY", "OPENROUTER_MODEL", "OPENROUTER_ENDPOINT",
        };

        [Fact]
        public void NightlyConsensus_PassesEveryHostedProviderCredential()
        {
            var text = File.ReadAllText(WorkflowPath());

            var missing = ExpectedVariables.Where(v => !text.Contains(v, StringComparison.Ordinal)).ToList();

            Assert.True(
                missing.Count == 0,
                "nightly-consensus.yml is missing credentials for: " + string.Join(", ", missing) +
                ". A provider LlmProviderFactory can construct but this workflow never passes " +
                "credentials for is silently absent from every nightly report - indistinguishable " +
                "from a provider that had nothing to say.");
        }

        [Fact]
        public void NightlyConsensus_DoesNotReferenceTheLocalOllamaDaemon()
        {
            // The inverse check: OLLAMA_HOST/OLLAMA_MODEL would build a provider aimed at
            // localhost:11434, which fails every request on a CI runner while still counting
            // toward the consensus threshold (#109) - worse than the provider being absent.
            var text = File.ReadAllText(WorkflowPath());

            Assert.DoesNotContain("OLLAMA_HOST", text, StringComparison.Ordinal);
            Assert.DoesNotContain("OLLAMA_ENABLED", text, StringComparison.Ordinal);
        }

        private static string WorkflowPath()
        {
            var path = Path.Combine(FindRepositoryRoot(), ".github", "workflows", "nightly-consensus.yml");
            Assert.True(File.Exists(path), $"Expected the nightly consensus workflow at {path}.");
            return path;
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AutomationSandbox.sln")))
            {
                directory = directory.Parent;
            }

            Assert.NotNull(directory);
            return directory!.FullName;
        }
    }
}
