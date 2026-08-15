using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LlmHealing;
using SelfHealing;
using UiModel;
using Xunit;

namespace ScenarioRunner
{
    public class LiveConsensusGateTests
    {
        private const string GateEnvironmentVariable = "LIVE_CONSENSUS_GATE";
        private const string ScenarioName = "Desktop_AmbiguousSiblingTabs";
        private static readonly string[] RequiredProviderNames = { "Gemini", "Groq" };

        [Fact]
        public void LiveConsensusGateFixture_ExistsAndHasGroundTruth()
        {
            var scenario = Assert.Single(EvaluationScenarios.All, s => s.Name == ScenarioName);
            Assert.False(string.IsNullOrWhiteSpace(scenario.GroundTruthAutomationId));
        }

        [Fact]
        public async Task LiveConsensusGate_GeminiAndGroqAgreeOnCorrectCandidate()
        {
            if (!IsGateEnabled())
            {
                Console.WriteLine($"[LiveConsensusGate] {GateEnvironmentVariable}=1 is not set - skipping live consensus gate.");
                return;
            }

            var configured = LlmProviderFactory.CreateConfiguredProviders();
            if (configured.Count == 0)
            {
                Console.WriteLine("[LiveConsensusGate] Gemini and Groq credentials are not configured - skipping live consensus gate.");
                AppendStepSummary("⏭️ Skipped", configured, null, "Gemini and Groq credentials are absent.");
                return;
            }

            IReadOnlyList<ILlmHealingProvider> selected;
            try
            {
                selected = SelectRequiredProviders(configured);
            }
            catch (InvalidOperationException ex)
            {
                AppendStepSummary("❌ Failed", configured, null, ex.Message);
                throw;
            }

            var scenario = Assert.Single(EvaluationScenarios.All, s => s.Name == ScenarioName);
            Assert.IsType<GeminiHealingProvider>(Assert.Single(selected, p => p.Name == "Gemini"));
            var groqProvider = Assert.IsType<OpenAiHealingProvider>(Assert.Single(selected, p => p.Name == "Groq"));
            Assert.StartsWith("https://api.groq.com/", groqProvider.ApiUrl);

            // This intentionally mirrors SelfHealingResolver's deterministic shortlist order and
            // synthetic "c" + index ids so the raw live votes can be audited. A resolver change
            // that makes this copy stale must break the gate loudly rather than compare wrong ids.
            var shortlist = SelfHealingResolver
                .ScoreCandidates(scenario.Expected, scenario.CurrentTreeRoot)
                .Take(SimilarityWeights.Default.MaxCandidatesForLlm)
                .ToList();
            for (var i = 0; i < shortlist.Count; i++)
            {
                shortlist[i].CandidateId = "c" + i;
            }

            var expectedCandidate = Assert.Single(
                shortlist,
                c => string.Equals(c.Candidate.AutomationId, scenario.GroundTruthAutomationId, StringComparison.Ordinal));
            var shortlistIds = new HashSet<string>(shortlist.Select(c => c.CandidateId), StringComparer.Ordinal);
            var recorders = selected.Select(p => new RecordingProvider(p)).ToList();

            var result = await SelfHealingResolver.ResolveAsync(
                scenario.Expected,
                scenario.CurrentTreeRoot,
                recorders,
                platform: scenario.Platform,
                log: message => Console.WriteLine("[LiveConsensusGate] " + message));

            var providerResults = recorders.Select(r => r.LastResult).ToList();
            var allAnswered = providerResults.All(r => r != null && r.Success && !string.IsNullOrEmpty(r.MatchedCandidateId));
            var allVotesInShortlist = allAnswered && providerResults.All(r => shortlistIds.Contains(r!.MatchedCandidateId!));
            var agreedCandidateIds = providerResults
                .Where(r => r != null && r.Success && !string.IsNullOrEmpty(r.MatchedCandidateId))
                .Select(r => r!.MatchedCandidateId!)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var agreedCandidateId = agreedCandidateIds.Count == 1 ? agreedCandidateIds[0] : null;
            var passed = allAnswered &&
                allVotesInShortlist &&
                string.Equals(agreedCandidateId, expectedCandidate.CandidateId, StringComparison.Ordinal) &&
                result.Source == HealSource.Llm &&
                result.AgreedProviders.Count >= 2 &&
                RequiredProviderNames.All(name => result.AgreedProviders.Contains(name));

            var detail = BuildResultDetail(recorders, result, expectedCandidate.CandidateId, agreedCandidateId);
            AppendStepSummary(passed ? "✅ Passed" : "❌ Failed", selected, agreedCandidateId, detail);
            Console.WriteLine("[LiveConsensusGate] " + detail);

            Assert.True(allAnswered, "Both live providers must return a parsed answer. " + detail);
            Assert.True(allVotesInShortlist, "The hallucination guard must not discard either live provider's vote. " + detail);
            Assert.Single(agreedCandidateIds);
            Assert.Equal(expectedCandidate.CandidateId, agreedCandidateId);
            Assert.Equal(HealSource.Llm, result.Source);
            Assert.True(result.AgreedProviders.Count >= 2, "Live consensus requires at least two agreeing providers. " + detail);
            Assert.Contains("Gemini", result.AgreedProviders);
            Assert.Contains("Groq", result.AgreedProviders);
            Assert.Equal(scenario.GroundTruthAutomationId, result.Matched!.AutomationId);
        }

        [Fact]
        public void LiveConsensusGate_DeliberateSingleProviderConfigurationFailsValidation()
        {
            var providers = new ILlmHealingProvider[] { new StubProvider("Gemini") };

            var exception = Assert.Throws<InvalidOperationException>(() => SelectRequiredProviders(providers));

            Assert.Contains("Gemini", exception.Message);
            Assert.Contains("Groq", exception.Message);
            Assert.Contains("exactly two", exception.Message);
        }

        [Fact]
        public void LiveConsensusGate_StepSummaryNamesBothIndependentProviders()
        {
            var providers = new ILlmHealingProvider[] { new StubProvider("Gemini"), new StubProvider("Groq") };

            var markdown = BuildStepSummary("✅ Passed", providers, "c0", "Both votes were shortlist-valid.");

            Assert.Contains("Gemini + Groq", markdown);
            Assert.Contains("Desktop_AmbiguousSiblingTabs", markdown);
            Assert.Contains("`c0`", markdown);
            Assert.Contains("✅ Passed", markdown);
        }

        private static bool IsGateEnabled()
        {
            var value = Environment.GetEnvironmentVariable(GateEnvironmentVariable);
            return value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static IReadOnlyList<ILlmHealingProvider> SelectRequiredProviders(IReadOnlyList<ILlmHealingProvider> configured)
        {
            var selected = configured
                .Where(p => RequiredProviderNames.Contains(p.Name, StringComparer.Ordinal))
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .ToList();
            if (selected.Count != RequiredProviderNames.Length ||
                RequiredProviderNames.Any(name => selected.All(p => p.Name != name)))
            {
                var configuredNames = configured.Count == 0
                    ? "none"
                    : string.Join(", ", configured.Select(p => p.Name).OrderBy(name => name, StringComparer.Ordinal));
                throw new InvalidOperationException(
                    $"Live consensus gate requires exactly two independent providers (Gemini and Groq); configured providers: {configuredNames}.");
            }

            return selected;
        }

        private static string BuildResultDetail(
            IReadOnlyList<RecordingProvider> recorders,
            HealResult result,
            string expectedCandidateId,
            string? agreedCandidateId)
        {
            var votes = string.Join(", ", recorders.Select(r =>
            {
                var value = r.LastResult;
                return value == null
                    ? r.Name + "=no result"
                    : value.Success
                        ? r.Name + "=" + (value.MatchedCandidateId ?? "no candidate")
                        : r.Name + "=failed (" + value.ErrorMessage + ")";
            }));
            var agreed = result.AgreedProviders.Count == 0 ? "none" : string.Join(" + ", result.AgreedProviders);
            return $"Scenario={ScenarioName}; expected={expectedCandidateId}; voted={agreedCandidateId ?? "none"}; votes=[{votes}]; AgreedProviders=[{agreed}].";
        }

        private static void AppendStepSummary(
            string status,
            IReadOnlyList<ILlmHealingProvider> providers,
            string? candidateId,
            string detail)
        {
            var summaryPath = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
            if (string.IsNullOrEmpty(summaryPath))
            {
                return;
            }

            File.AppendAllText(summaryPath, BuildStepSummary(status, providers, candidateId, detail));
        }

        private static string BuildStepSummary(
            string status,
            IReadOnlyList<ILlmHealingProvider> providers,
            string? candidateId,
            string detail)
        {
            var providerNames = providers.Count == 0
                ? "none"
                : string.Join(" + ", providers.Select(p => p.Name).OrderBy(name => name, StringComparer.Ordinal));
            return Environment.NewLine +
                "## Live Two-Provider Consensus Gate" + Environment.NewLine + Environment.NewLine +
                $"- **Status:** {status}" + Environment.NewLine +
                $"- **Required providers:** Gemini + Groq" + Environment.NewLine +
                $"- **Configured/selected providers:** {providerNames}" + Environment.NewLine +
                $"- **Scenario:** `{ScenarioName}`" + Environment.NewLine +
                $"- **Agreed candidate:** `{candidateId ?? "none"}`" + Environment.NewLine +
                $"- **Detail:** {detail}" + Environment.NewLine;
        }

        private sealed class RecordingProvider : ILlmHealingProvider
        {
            private readonly ILlmHealingProvider _inner;

            public RecordingProvider(ILlmHealingProvider inner)
            {
                _inner = inner;
            }

            public string Name => _inner.Name;
            public bool IsAvailable => _inner.IsAvailable;
            public LlmHealingResult? LastResult { get; private set; }

            public async Task<LlmHealingResult> ResolveAsync(
                UiElementInfo expected,
                IReadOnlyList<CandidateScore> candidates,
                string? platform = null,
                CancellationToken cancellationToken = default)
            {
                LastResult = await _inner.ResolveAsync(expected, candidates, platform, cancellationToken).ConfigureAwait(false);
                return LastResult;
            }
        }

        private sealed class StubProvider : ILlmHealingProvider
        {
            public StubProvider(string name)
            {
                Name = name;
            }

            public string Name { get; }
            public bool IsAvailable => true;

            public Task<LlmHealingResult> ResolveAsync(
                UiElementInfo expected,
                IReadOnlyList<CandidateScore> candidates,
                string? platform = null,
                CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }
        }
    }
}
