using System;
using System.Linq;
using System.Net.Http;
using Discovery;
using LlmHealing;
using SelfHealing;
using UiModel;
namespace ScenarioRunner
{
    // Comparison harness, not a production fallback path: runs every configured LLM
    // provider against the same live broken-locator scenario and prints their answers
    // side by side, so we can judge which one (if any) is worth wiring into
    // SelfHealingResolver as a fallback for low-confidence heuristic matches.
    //
    // Providers without an API key configured are skipped automatically - set
    // ANTHROPIC_API_KEY and/or GEMINI_API_KEY as environment variables (or GitHub
    // Actions secrets) to include them. With neither set, this test is a no-op by
    // design - it is not part of the required, always-green suite.

    public class LlmHealingEvaluationTests : IDisposable
    {
        private const string WinFormsAppRelativePath = @"..\..\..\..\..\WinFormsApp\bin\Debug\net48\WinFormsApp.exe";
        private readonly ApplicationConnector _connector;

        public LlmHealingEvaluationTests()
        {
            var exePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, WinFormsAppRelativePath));
            _connector = ApplicationConnector.Launch(exePath);
        }

        [Fact]

        public async Task CompareProviders_OnLiveBrokenLocator()
        {
            using var httpClient = new HttpClient();
            ILlmHealingProvider[] providers =
            {
                new ClaudeHealingProvider(httpClient),
                new GeminiHealingProvider(httpClient),
            };
            var available = providers.Where(p => p.IsAvailable).ToList();
            if (available.Count == 0)
            {
                Console.WriteLine("[LlmHealingEvaluation] No provider API keys configured (ANTHROPIC_API_KEY / GEMINI_API_KEY) - skipping.");
                return;
            }

            var window = _connector.GetMainWindow();
            var currentTree = UiTreeWalker.BuildTree(window);

            // Same stale-locator scenario as SelfHealing_BrokenAutomationId_FindsCorrectElementInLiveApp:
            // the correct answer is "txtEmail" - kept here as ground truth for eyeballing the printed
            // comparison, not asserted on, since model answers are not guaranteed deterministic.
            var staleExpected = UiElementSnapshot.CaptureByAutomationId(currentTree, "txtEmail")
                ?? throw new InvalidOperationException("txtEmail was not found in the live tree, test data is invalid.");
            staleExpected.AutomationId = "txtEmailAddress";

            // Same shortlist-building SelfHealingResolver.ResolveAsync uses internally - bounds
            // what's sent to the LLM instead of the whole tree (see SimilarityWeights.MaxCandidatesForLlm).
            var weights = SimilarityWeights.Default;
            var shortlist = SelfHealingResolver.ScoreCandidates(staleExpected, currentTree, weights)
                .Take(weights.MaxCandidatesForLlm)
                .ToList();
            for (var i = 0; i < shortlist.Count; i++)
            {
                shortlist[i].CandidateId = "c" + i;
            }

            var results = await LlmHealingEvaluator.EvaluateAsync(available, staleExpected, shortlist);
            Console.WriteLine("[LlmHealingEvaluation] Ground truth AutomationId: txtEmail");
            foreach (var result in results)
            {
                Console.WriteLine(result.Success
                    ? $"[LlmHealingEvaluation] {result.ProviderName}: matched='{result.MatchedAutomationId}', confidence={result.Confidence:F2}, elapsed={result.Elapsed.TotalMilliseconds:F0}ms, reasoning=\"{result.Reasoning}\""
                    : $"[LlmHealingEvaluation] {result.ProviderName}: FAILED - {result.ErrorMessage}");
            }

            Assert.NotEmpty(results);
        }

        public void Dispose()
        {
            _connector.Dispose();
        }
    }
}
