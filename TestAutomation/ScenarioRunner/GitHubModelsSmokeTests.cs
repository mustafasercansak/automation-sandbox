using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LlmHealing;
using SelfHealing;
using UiModel;
using Xunit;

namespace ScenarioRunner
{
    // Live smoke test against GitHub Models (https://models.github.ai/inference).
    // Executed on-demand via .github/workflows/llm-smoke.yml with GITHUB_TOKEN (models: read).
    //
    // Design rules:
    // 1. If GITHUB_TOKEN / OPENAI_API_KEY is not set (e.g. standard local test runs), skips cleanly.
    // 2. If GITHUB_TOKEN is present (CI smoke run), asserts hard on Success - network or auth failures fail the test.
    // 3. Asserts the integration, never the model's judgement. Which candidate the model prefers, and how
    //    confident it says it is, vary run to run; pinning those would turn a nightly nobody is watching
    //    into a flaky signal that gets ignored - and a red run that is ignored hides the real breakage this
    //    test exists to catch. So: the request went out, the response parsed, and the hallucination guard
    //    held (the returned id belongs to the shortlist we sent). Model quality is #15's job, not this test's.
    public class GitHubModelsSmokeTests
    {
        private static bool IsTokenConfigured()
        {
            return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_TOKEN"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPENAI_API_KEY"));
        }

        [SkippableFact]
        public async Task GitHubModels_LiveSmoke_CompletesRoundTripAndHoldsHallucinationGuard()
        {
            if (!IsTokenConfigured())
            {
                Console.WriteLine("[GitHubModelsSmoke] No GITHUB_TOKEN or OPENAI_API_KEY configured - skipping live smoke test.");
                Skip.If(true, "No GITHUB_TOKEN or OPENAI_API_KEY configured.");
            }

            var endpoint = Environment.GetEnvironmentVariable("OPENAI_ENDPOINT") ?? "https://models.github.ai/inference";
            var model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-mini";

            var provider = new OpenAiHealingProvider(endpoint: endpoint, model: model);
            Assert.True(provider.IsAvailable, "Provider must be available when token is configured.");

            var (expected, root, candidates) = BuildTestScenario();

            // 1. Verify direct provider resolution
            var directResult = await provider.ResolveAsync(expected, candidates);
            Assert.True(directResult.Success, $"GitHub Models direct resolution failed: {directResult.ErrorMessage}");
            Assert.False(string.IsNullOrWhiteSpace(directResult.Reasoning));

            // Hallucination guard: whichever candidate the model picked, the id has to be one we sent.
            var sentIds = candidates.ConvertAll(c => c.CandidateId);
            Assert.False(string.IsNullOrWhiteSpace(directResult.MatchedCandidateId));
            Assert.Contains(directResult.MatchedCandidateId, sentIds);

            // Confidence is only checked for being a parsed, in-range number - not for being high.
            Assert.InRange(directResult.Confidence, 0.0, 1.0);

            // 2. Verify the resolver integrates with the provider and applies consensus (#10).
            // One provider cannot form a consensus, so the correct outcome here is a clean
            // fall back to the heuristic result - the resolver still called the provider (the
            // direct check above proves the wire works), it just refuses to act on a single
            // uncalibrated opinion.
            //
            // Consensus itself is not exercised here on purpose. Faking it with two instances
            // pointed at the same endpoint would be the same model voting twice - not the
            // independent agreement the rule is about - while doubling the live API cost of
            // every smoke run. Consensus behaviour is covered by SelfHealingResolverTests
            // against mocked providers, which is where this issue's test strategy puts it.
            var weights = new SimilarityWeights { MinimumConfidence = 0.99 }; // Force LLM fallback
            var healResult = await SelfHealingResolver.ResolveAsync(
                expected,
                root,
                new[] { provider },
                weights: weights,
                log: msg => Console.WriteLine($"[GitHubModelsSmoke] {msg}"));

            Assert.Equal(HealSource.Heuristic, healResult.Source);
            Assert.Empty(healResult.AgreedProviders);
            Assert.NotNull(healResult.Matched);
            Assert.Contains(healResult.Matched!.AutomationId, root.Children.ConvertAll(c => c.AutomationId));

            Console.WriteLine(
                $"[GitHubModelsSmoke] {provider.Name} ({model}) picked '{directResult.MatchedCandidateId}' " +
                $"with confidence {directResult.Confidence:F2} in {directResult.Elapsed.TotalMilliseconds:F0}ms; " +
                $"single provider so the resolver fell back to '{healResult.Matched.AutomationId}'. " +
                $"Reasoning: {directResult.Reasoning}");
        }

        private static (UiElementInfo Expected, UiElementInfo Root, List<CandidateScore> Candidates) BuildTestScenario()
        {
            var expected = new UiElementInfo
            {
                ControlType = "Edit",
                Name = "Email Address",
                AutomationId = "txtEmailAddress",
                ParentControlType = "Window",
                ParentAutomationId = "MainForm",
                SiblingIndex = 0,
                SiblingCount = 2,
                BoundingRectangle = new BoundingRectangle(100, 50, 200, 24),
            };

            var root = new UiElementInfo { ControlType = "Window", AutomationId = "MainForm", Name = "Main Window" };

            var emailCandidate = new UiElementInfo
            {
                ControlType = "Edit",
                AutomationId = "txtEmail",
                Name = "Email",
                ParentControlType = "Window",
                ParentAutomationId = "MainForm",
                SiblingIndex = 0,
                SiblingCount = 2,
                BoundingRectangle = new BoundingRectangle(100, 50, 200, 24),
            };

            var saveCandidate = new UiElementInfo
            {
                ControlType = "Button",
                AutomationId = "btnSave",
                Name = "Save",
                ParentControlType = "Window",
                ParentAutomationId = "MainForm",
                SiblingIndex = 1,
                SiblingCount = 2,
                BoundingRectangle = new BoundingRectangle(100, 100, 80, 24),
            };

            root.Children.Add(emailCandidate);
            root.Children.Add(saveCandidate);

            var candidates = new List<CandidateScore>
            {
                new()
                {
                    CandidateId = "c0",
                    Candidate = emailCandidate,
                    TotalScore = 0.85,
                    Components = new ScoreComponents
                    {
                        ControlTypeScore = 1.0,
                        ParentControlTypeScore = 1.0,
                        SiblingPositionScore = 1.0,
                        NameScore = 0.8,
                        PositionScore = 1.0,
                    },
                },
                new()
                {
                    CandidateId = "c1",
                    Candidate = saveCandidate,
                    TotalScore = 0.10,
                    Components = new ScoreComponents
                    {
                        ControlTypeScore = 0.0,
                        ParentControlTypeScore = 1.0,
                        SiblingPositionScore = 0.5,
                        NameScore = 0.0,
                        PositionScore = 0.5,
                    },
                },
            };

            return (expected, root, candidates);
        }
    }
}
