using System.Net;
using System.Net.Http;
using UiModel;
using SelfHealing;
using LlmHealing;
namespace ScenarioRunner
{
    public class SelfHealingResolverTests
    {
        [Fact]

        public void Resolve_FindsRenamedControl_ByStructuralSimilarity()
        {
            // "Previous" snapshot: txtEmail's state while its AutomationId was still correct.
            var expected = new UiElementInfo
            {
                ControlType = "Edit",
                Name = "",
                AutomationId = "txtEmail",
                ParentControlType = "Window",
                ParentAutomationId = "MainForm",
                SiblingIndex = 2,
                SiblingCount = 7,
                BoundingRectangle = new BoundingRectangle(112, 70, 200, 23),
            };

            // "Current" tree: after a refactor, txtEmail's AutomationId became "textBox1",
            // but its position and sibling context stayed the same.
            var currentTree = BuildCurrentMainFormTree(renamedEmailAutomationId: "textBox1");
            var result = SelfHealingResolver.Resolve(expected, currentTree, log: _ => { });
            Assert.NotNull(result.Matched);
            Assert.Equal("textBox1", result.Matched!.AutomationId);
            Assert.True(result.IsConfident, $"Expected a confident match, but the score was: {result.Score}");
        }

        [Fact]

        public void Resolve_CanUseStructurallySimilarCandidate_WhenControlTypeChanged()
        {
            var expected = new UiElementInfo
            {
                ControlType = "Button",
                Name = "Save",
                AutomationId = "btnSave",
                ParentControlType = "Window",
                ParentAutomationId = "MainForm",
                SiblingIndex = 5,
                SiblingCount = 7,
                BoundingRectangle = new BoundingRectangle(112, 178, 100, 30),
            };
            var currentTree = BuildCurrentMainFormTree(renamedEmailAutomationId: "txtEmail", saveControlType: "Hyperlink");
            var result = SelfHealingResolver.Resolve(expected, currentTree, log: _ => { });
            Assert.NotNull(result.Matched);
            Assert.Equal("btnSave", result.Matched!.AutomationId);
            Assert.True(result.Score > 0.0);
        }

        [Fact]

        public void Resolve_WithCustomWeights_ChangesWinningCandidate()
        {
            var expected = new UiElementInfo
            {
                ControlType = "Edit",
                Name = "Email",
                ParentControlType = "Window",
                SiblingIndex = 0,
                SiblingCount = 2,
                BoundingRectangle = new BoundingRectangle(100, 100, 50, 20),
            };
            var root = new UiElementInfo { ControlType = "Window" };
            var closeButWrongName = new UiElementInfo
            {
                ControlType = "Edit",
                AutomationId = "closeButWrongName",
                Name = "Zzzzz",
                ParentControlType = "Window",
                SiblingIndex = 0,
                SiblingCount = 2,
                BoundingRectangle = new BoundingRectangle(100, 100, 50, 20),
            };
            var farButRightName = new UiElementInfo
            {
                ControlType = "Edit",
                AutomationId = "farButRightName",
                Name = "Email",
                ParentControlType = "Window",
                SiblingIndex = 0,
                SiblingCount = 2,
                BoundingRectangle = new BoundingRectangle(900, 900, 50, 20),
            };
            root.Children.Add(closeButWrongName);
            root.Children.Add(farButRightName);
            var defaultResult = SelfHealingResolver.Resolve(expected, root, log: _ => { });
            Assert.Equal("closeButWrongName", defaultResult.Matched!.AutomationId);
            var nameOnlyWeights = new SimilarityWeights
            {
                ControlTypeWeight = 0,
                ParentControlTypeWeight = 0,
                SiblingPositionWeight = 0,
                NameWeight = 1.0,
                PositionWeight = 0,
            };
            var customResult = SelfHealingResolver.Resolve(expected, root, weights: nameOnlyWeights, log: _ => { });
            Assert.Equal("farButRightName", customResult.Matched!.AutomationId);
        }

        [Fact]

        public void Resolve_DoesNotTreatEmptyBoundingRectanglesAsPerfectPositionMatches()
        {
            var expected = new UiElementInfo
            {
                ControlType = "Edit",
                Name = "Email",
                ParentControlType = "Window",
                SiblingIndex = 0,
                SiblingCount = 2,
                BoundingRectangle = new BoundingRectangle(100, 100, 50, 20),
            };
            var root = new UiElementInfo { ControlType = "Window" };
            root.Children.Add(new UiElementInfo
            {
                ControlType = "Edit",
                AutomationId = "emptyRectWrongName",
                Name = "Completely Different",
                ParentControlType = "Window",
                SiblingIndex = 0,
                SiblingCount = 2,
                BoundingRectangle = new BoundingRectangle(0, 0, 0, 0),
            });
            root.Children.Add(new UiElementInfo
            {
                ControlType = "Edit",
                AutomationId = "validRectRightName",
                Name = "Email",
                ParentControlType = "Window",
                SiblingIndex = 0,
                SiblingCount = 2,
                BoundingRectangle = new BoundingRectangle(105, 105, 50, 20),
            });
            var result = SelfHealingResolver.Resolve(expected, root, log: _ => { });
            Assert.Equal("validRectRightName", result.Matched!.AutomationId);
        }

        [Fact]

        public async Task ResolveAsync_HeuristicConfident_NeverCallsLlmProvider()
        {
            var expected = new UiElementInfo
            {
                ControlType = "Edit",
                Name = "",
                AutomationId = "txtEmail",
                ParentControlType = "Window",
                ParentAutomationId = "MainForm",
                SiblingIndex = 2,
                SiblingCount = 7,
                BoundingRectangle = new BoundingRectangle(112, 70, 200, 23),
            };
            var currentTree = BuildCurrentMainFormTree(renamedEmailAutomationId: "textBox1");
            var callCount = 0;
            var provider = new FakeProvider("Fake", isAvailable: true, resolve: () => { callCount++; throw new InvalidOperationException("Should not be called."); });
            var result = await SelfHealingResolver.ResolveAsync(expected, currentTree, new[] { provider }, log: _ => { });
            Assert.Equal(0, callCount);
            Assert.Equal(HealSource.Heuristic, result.Source);
            Assert.Equal("textBox1", result.Matched!.AutomationId);
        }

        [Fact]

        public async Task ResolveAsync_HeuristicLowConfidence_FallsBackToLlmProvider_AndReturnsLlmSourcedResult()
        {
            var (expected, currentTree) = BuildLowConfidenceScenario();
            var result = await SelfHealingResolver.ResolveAsync(
                expected, currentTree, AgreeingProviders("c0", confidence: 0.9, reasoning: "structural match"), log: _ => { });
            Assert.Equal(HealSource.Llm, result.Source);
            Assert.Equal("textBoxFar", result.Matched!.AutomationId);
            Assert.Equal("AlphaLlm", result.LlmProviderName);
            Assert.Equal(0.9, result.LlmConfidence);
            Assert.Equal("structural match", result.LlmReasoning);
            Assert.Equal(new[] { "AlphaLlm", "BetaLlm" }, result.AgreedProviders);
            Assert.True(result.IsConfident);
        }

        [Fact]
        public async Task ResolveAsync_PropagatesPlatformParameterToLlmProvider()
        {
            var (expected, currentTree) = BuildLowConfidenceScenario();
            var provider = new FakeProvider("Fake", isAvailable: true, resolve: () =>
                new LlmHealingResult { ProviderName = "Fake", Success = true, MatchedCandidateId = "c0", Confidence = 0.9, Reasoning = "match" });

            await SelfHealingResolver.ResolveAsync(
                expected,
                currentTree,
                new[] { provider },
                platform: "web-playwright",
                log: _ => { });

            Assert.Equal("web-playwright", provider.LastPlatform);
        }

        [Fact]

        public async Task ResolveAsync_NoProvidersConfigured_FallsBackToHeuristicResult()
        {
            var (expected, currentTree) = BuildLowConfidenceScenario();
            var result = await SelfHealingResolver.ResolveAsync(expected, currentTree, llmProviders: null, log: _ => { });
            Assert.Equal(HealSource.Heuristic, result.Source);
        }

        [Fact]

        public async Task ResolveAsync_AllProvidersUnavailable_NeverInvokesThem()
        {
            var (expected, currentTree) = BuildLowConfidenceScenario();
            var callCount = 0;
            var provider = new FakeProvider("Fake", isAvailable: false, resolve: () => { callCount++; throw new InvalidOperationException("Should not be called."); });
            var result = await SelfHealingResolver.ResolveAsync(expected, currentTree, new[] { provider }, log: _ => { });
            Assert.Equal(0, callCount);
            Assert.Equal(HealSource.Heuristic, result.Source);
        }

        [Fact]

        public async Task ResolveAsync_LlmReturnsCandidateIdNotInShortlist_FallsBackToHeuristicResult()
        {
            var (expected, currentTree) = BuildLowConfidenceScenario();
            var provider = new FakeProvider("Fake", isAvailable: true, resolve: () =>
                new LlmHealingResult { ProviderName = "Fake", Success = true, MatchedCandidateId = "doesNotExist", Confidence = 0.95 });
            var result = await SelfHealingResolver.ResolveAsync(expected, currentTree, new[] { provider }, log: _ => { });
            Assert.Equal(HealSource.Heuristic, result.Source);
        }

        [Fact]

        public async Task ResolveAsync_SingleProvider_IsNeverAcceptedNoMatterHowConfident()
        {
            // Consensus (#10/#19): one provider cannot agree with itself. Even a 0.99
            // self-report is a single uncalibrated opinion, so the pick is not accepted and
            // Matched stays the heuristic's own candidate rather than the LLM's guess.
            var (expected, currentTree) = BuildLowConfidenceScenario();
            var provider = new FakeProvider("Solo", isAvailable: true, resolve: () =>
                new LlmHealingResult { ProviderName = "Solo", Success = true, MatchedCandidateId = "c0", Confidence = 0.99 });
            var result = await SelfHealingResolver.ResolveAsync(expected, currentTree, new[] { provider }, log: _ => { });

            Assert.Equal(HealSource.Heuristic, result.Source);
            Assert.Equal("textBoxFar", result.Matched!.AutomationId);
            Assert.Empty(result.AgreedProviders);
            Assert.NotNull(result.ProviderAttempts);
            Assert.Equal(1, result.ProviderAttempts["Solo"]);
        }

        [Fact]
        public async Task ResolveAsync_LlmConsensus_IsConfidentEvenWhenSelfReportedConfidenceIsLow()
        {
            // The acceptance rule is agreement, not self-reported confidence (#19). Two
            // providers converging at 0.2 each is stronger evidence than one at 0.99.
            var (expected, currentTree) = BuildLowConfidenceScenario();
            var weights = new SimilarityWeights
            {
                MinimumConfidence = 0.8,
            };

            var result = await SelfHealingResolver.ResolveAsync(
                expected, currentTree, AgreeingProviders("c0", confidence: 0.2), weights, log: _ => { });

            Assert.Equal(HealSource.Llm, result.Source);
            Assert.Equal(0.2, result.LlmConfidence);
            Assert.True(result.IsConfident);
            Assert.NotNull(result.ProviderAttempts);
            Assert.Equal(1, result.ProviderAttempts["AlphaLlm"]);
            Assert.Equal(1, result.ProviderAttempts["BetaLlm"]);

            // Regression guard (#253): an LLM-sourced result's ConfidenceThreshold must not
            // silently carry the heuristic's MinimumConfidence (0.8 here). That would make an
            // unrelated setting look like the LLM's acceptance bar in reports/persisted
            // locator history, when consensus (AgreedProviders vs ConsensusThreshold) is what
            // actually decided this result.
            Assert.NotEqual(weights.MinimumConfidence, result.ConfidenceThreshold);
            Assert.Equal(0.0, result.ConfidenceThreshold);
        }

        [Fact]
        public async Task ResolveAsync_LlmConfidence_ReportsMeanOfAgreeingProviders()
        {
            // Recorded for the report and for #15's measurement, never thresholded.
            var (expected, currentTree) = BuildLowConfidenceScenario();
            var providers = new ILlmHealingProvider[]
            {
                new FakeProvider("AlphaLlm", isAvailable: true, resolve: () =>
                    new LlmHealingResult { ProviderName = "AlphaLlm", Success = true, MatchedCandidateId = "c0", Confidence = 0.4 }),
                new FakeProvider("BetaLlm", isAvailable: true, resolve: () =>
                    new LlmHealingResult { ProviderName = "BetaLlm", Success = true, MatchedCandidateId = "c0", Confidence = 0.8 }),
            };

            var result = await SelfHealingResolver.ResolveAsync(expected, currentTree, providers, log: _ => { });

            Assert.Equal(HealSource.Llm, result.Source);
            Assert.Equal(0.6, result.LlmConfidence!.Value, 6);
        }

        [Fact]

        public async Task ResolveAsync_LlmMatchesCandidateWithEmptyAutomationId_StillSucceeds()
        {
            // The exact scenario this framework exists to heal: a real, legitimately-matched
            // element whose AutomationId is empty (common in WPF/legacy UI). MatchedAutomationId
            // being empty must not cause a valid MatchedCandidateId match to be discarded.
            var expected = new UiElementInfo
            {
                ControlType = "Group",
                Name = "Company",
                ParentControlType = "Window",
                SiblingIndex = 0,
                SiblingCount = 1,
                BoundingRectangle = new BoundingRectangle(0, 0, 100, 100),
            };
            var root = new UiElementInfo { ControlType = "Window" };
            root.Children.Add(new UiElementInfo
            {
                ControlType = "Group",
                AutomationId = "", // deliberately empty
                Name = "CompletelyDifferentName",
                ParentControlType = "SomeOtherPanel",
                SiblingIndex = 50,
                SiblingCount = 100,
                BoundingRectangle = new BoundingRectangle(9000, 9000, 100, 100),
            });
            var result = await SelfHealingResolver.ResolveAsync(
                expected, root, AgreeingProviders("c0", reasoning: "structural match"), log: _ => { });
            Assert.Equal(HealSource.Llm, result.Source);
            Assert.NotNull(result.Matched);
            Assert.Equal("", result.Matched!.AutomationId);
            Assert.True(result.IsConfident);
        }

        [Fact]

        public async Task ResolveAsync_LlmProviderThrows_FallsBackGracefullyWithoutPropagating()
        {
            var (expected, currentTree) = BuildLowConfidenceScenario();
            var provider = new FakeProvider("Fake", isAvailable: true, resolve: () => throw new InvalidOperationException("boom"));
            var result = await SelfHealingResolver.ResolveAsync(expected, currentTree, new[] { provider }, log: _ => { });
            Assert.Equal(HealSource.Heuristic, result.Source);
        }

        [Fact]

        public async Task ResolveAsync_MajorityAgrees_AcceptsAgreedCandidateAndRecordsVoters()
        {
            // Issue #10's worked example: two providers name c0, a third names c1. The
            // majority pick wins and AgreedProviders records exactly who agreed - the
            // dissenter is not listed.
            var (expected, currentTree) = BuildLowConfidenceScenario();
            var providers = new ILlmHealingProvider[]
            {
                new FakeProvider("AlphaLlm", isAvailable: true, resolve: () =>
                    new LlmHealingResult { ProviderName = "AlphaLlm", Success = true, MatchedCandidateId = "c0", Confidence = 0.4 }),
                new FakeProvider("BetaLlm", isAvailable: true, resolve: () =>
                    new LlmHealingResult { ProviderName = "BetaLlm", Success = true, MatchedCandidateId = "c0", Confidence = 0.45 }),
                // Higher self-reported confidence than either agreeing provider, and it still
                // loses: confidence never outvotes agreement.
                new FakeProvider("GammaLlm", isAvailable: true, resolve: () =>
                    new LlmHealingResult { ProviderName = "GammaLlm", Success = true, MatchedCandidateId = "c1", Confidence = 0.99 }),
            };

            var result = await SelfHealingResolver.ResolveAsync(expected, currentTree, providers, log: _ => { });

            Assert.Equal(HealSource.Llm, result.Source);
            Assert.Equal("textBoxFar", result.Matched!.AutomationId);
            Assert.Equal(new[] { "AlphaLlm", "BetaLlm" }, result.AgreedProviders);
        }

        [Fact]
        public async Task ResolveAsync_AllProvidersNameDifferentCandidates_FallsBackToHeuristicResult()
        {
            // Three providers, three answers, every candidate on one vote. That is the LLM
            // layer saying "we do not know", not a close call to be settled by whoever
            // sounded most certain.
            var (expected, currentTree) = BuildLowConfidenceScenario();
            var providers = new ILlmHealingProvider[]
            {
                new FakeProvider("AlphaLlm", isAvailable: true, resolve: () =>
                    new LlmHealingResult { ProviderName = "AlphaLlm", Success = true, MatchedCandidateId = "c0", Confidence = 0.72 }),
                new FakeProvider("BetaLlm", isAvailable: true, resolve: () =>
                    new LlmHealingResult { ProviderName = "BetaLlm", Success = true, MatchedCandidateId = "c1", Confidence = 0.95 }),
                new FakeProvider("GammaLlm", isAvailable: true, resolve: () =>
                    new LlmHealingResult { ProviderName = "GammaLlm", Success = true, MatchedCandidateId = "c2", Confidence = 0.81 }),
            };

            var result = await SelfHealingResolver.ResolveAsync(expected, currentTree, providers, log: _ => { });

            Assert.Equal(HealSource.Heuristic, result.Source);
            Assert.Empty(result.AgreedProviders);
            Assert.NotNull(result.ProviderAttempts);
            Assert.Equal(3, result.ProviderAttempts.Count);
            Assert.Equal(1, result.ProviderAttempts["AlphaLlm"]);
            Assert.Equal(1, result.ProviderAttempts["BetaLlm"]);
            Assert.Equal(1, result.ProviderAttempts["GammaLlm"]);
        }

        [Fact]
        public async Task ResolveAsync_VoteTiedBetweenTwoCandidates_FallsBackToHeuristicResult()
        {
            // 2-2. Breaking this by confidence would reinstate exactly the cross-provider
            // comparison #19 removed, so a tie is treated as disagreement.
            var (expected, currentTree) = BuildLowConfidenceScenario();
            var providers = new ILlmHealingProvider[]
            {
                new FakeProvider("AlphaLlm", isAvailable: true, resolve: () =>
                    new LlmHealingResult { ProviderName = "AlphaLlm", Success = true, MatchedCandidateId = "c0", Confidence = 0.9 }),
                new FakeProvider("BetaLlm", isAvailable: true, resolve: () =>
                    new LlmHealingResult { ProviderName = "BetaLlm", Success = true, MatchedCandidateId = "c0", Confidence = 0.9 }),
                new FakeProvider("GammaLlm", isAvailable: true, resolve: () =>
                    new LlmHealingResult { ProviderName = "GammaLlm", Success = true, MatchedCandidateId = "c1", Confidence = 0.9 }),
                new FakeProvider("DeltaLlm", isAvailable: true, resolve: () =>
                    new LlmHealingResult { ProviderName = "DeltaLlm", Success = true, MatchedCandidateId = "c1", Confidence = 0.9 }),
            };

            var result = await SelfHealingResolver.ResolveAsync(expected, currentTree, providers, log: _ => { });

            Assert.Equal(HealSource.Heuristic, result.Source);
            Assert.Empty(result.AgreedProviders);
            Assert.NotNull(result.ProviderAttempts);
            Assert.Equal(4, result.ProviderAttempts.Count);
        }

        [Fact]
        public async Task ResolveAsync_OneProviderFails_RemainingTwoStillReachConsensus()
        {
            // A failed or timed-out provider does not cast a vote, but it must not prevent
            // the providers that did answer from reaching quorum.
            var (expected, currentTree) = BuildLowConfidenceScenario();
            var providers = new ILlmHealingProvider[]
            {
                new FakeProvider("AlphaLlm", isAvailable: true, resolve: () =>
                    new LlmHealingResult { ProviderName = "AlphaLlm", Success = true, MatchedCandidateId = "c0", Confidence = 0.7 }),
                new FakeProvider("BetaLlm", isAvailable: true, resolve: () =>
                    new LlmHealingResult { ProviderName = "BetaLlm", Success = true, MatchedCandidateId = "c0", Confidence = 0.7 }),
                new FakeProvider("GammaLlm", isAvailable: true, resolve: () =>
                    new LlmHealingResult { ProviderName = "GammaLlm", Success = false, ErrorMessage = "429 rate limited" }),
            };

            var result = await SelfHealingResolver.ResolveAsync(expected, currentTree, providers, log: _ => { });

            Assert.Equal(HealSource.Llm, result.Source);
            Assert.Equal(new[] { "AlphaLlm", "BetaLlm" }, result.AgreedProviders);
        }

        [Fact]
        public async Task ResolveAsync_HallucinatedVoteIsDiscardedBeforeCounting_AndDoesNotSinkValidVotes()
        {
            // The hallucination guard runs before the count, not after the winner is picked.
            // A provider naming a candidateId that was never in the shortlist must lose its
            // own vote without taking the other providers' valid votes down with it.
            var (expected, currentTree) = BuildLowConfidenceScenario();
            var providers = new ILlmHealingProvider[]
            {
                new FakeProvider("AlphaLlm", isAvailable: true, resolve: () =>
                    new LlmHealingResult { ProviderName = "AlphaLlm", Success = true, MatchedCandidateId = "c0", Confidence = 0.7 }),
                new FakeProvider("BetaLlm", isAvailable: true, resolve: () =>
                    new LlmHealingResult { ProviderName = "BetaLlm", Success = true, MatchedCandidateId = "c0", Confidence = 0.7 }),
                new FakeProvider("GammaLlm", isAvailable: true, resolve: () =>
                    new LlmHealingResult { ProviderName = "GammaLlm", Success = true, MatchedCandidateId = "c99", Confidence = 0.99 }),
            };

            var result = await SelfHealingResolver.ResolveAsync(expected, currentTree, providers, log: _ => { });

            Assert.Equal(HealSource.Llm, result.Source);
            Assert.Equal(new[] { "AlphaLlm", "BetaLlm" }, result.AgreedProviders);
        }

        [Fact]
        public async Task ResolveAsync_TwoProvidersHallucinate_LeavingTooFewVotes_FallsBackToHeuristicResult()
        {
            var (expected, currentTree) = BuildLowConfidenceScenario();
            var providers = new ILlmHealingProvider[]
            {
                new FakeProvider("AlphaLlm", isAvailable: true, resolve: () =>
                    new LlmHealingResult { ProviderName = "AlphaLlm", Success = true, MatchedCandidateId = "c0", Confidence = 0.7 }),
                new FakeProvider("BetaLlm", isAvailable: true, resolve: () =>
                    new LlmHealingResult { ProviderName = "BetaLlm", Success = true, MatchedCandidateId = "nope", Confidence = 0.9 }),
                new FakeProvider("GammaLlm", isAvailable: true, resolve: () =>
                    new LlmHealingResult { ProviderName = "GammaLlm", Success = true, MatchedCandidateId = "alsoNope", Confidence = 0.9 }),
            };

            var result = await SelfHealingResolver.ResolveAsync(expected, currentTree, providers, log: _ => { });

            Assert.Equal(HealSource.Heuristic, result.Source);
        }

        [Fact]
        public async Task ResolveAsync_AgreedProviders_IsOrdinallySortedRegardlessOfProviderOrder()
        {
            // The report must not depend on which provider happened to be listed - or to
            // answer - first.
            var (expected, currentTree) = BuildLowConfidenceScenario();
            LlmHealingResult Pick(string name) =>
                new LlmHealingResult { ProviderName = name, Success = true, MatchedCandidateId = "c0", Confidence = 0.7 };
            var zeta = new FakeProvider("ZetaLlm", isAvailable: true, resolve: () => Pick("ZetaLlm"));
            var alpha = new FakeProvider("AlphaLlm", isAvailable: true, resolve: () => Pick("AlphaLlm"));

            var forward = await SelfHealingResolver.ResolveAsync(expected, currentTree, new ILlmHealingProvider[] { zeta, alpha }, log: _ => { });
            var reversed = await SelfHealingResolver.ResolveAsync(expected, currentTree, new ILlmHealingProvider[] { alpha, zeta }, log: _ => { });

            Assert.Equal(new[] { "AlphaLlm", "ZetaLlm" }, forward.AgreedProviders);
            Assert.Equal(forward.AgreedProviders, reversed.AgreedProviders);
            Assert.Equal("AlphaLlm", forward.LlmProviderName);
            Assert.Equal("AlphaLlm", reversed.LlmProviderName);
        }

        [Fact]
        public void Validate_RejectsMinimumConsensusVotesBelowTwo()
        {
            var weights = new SimilarityWeights { MinimumConsensusVotes = 1 };
            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => weights.Validate());
            Assert.Equal("MinimumConsensusVotes", ex.ParamName);
        }

        [Fact]
        public void HealResult_IsConfident_RespectsConfiguredConsensusThreshold()
        {
            var result = new HealResult
            {
                Matched = new UiElementInfo { ControlType = "Button", Name = "Submit" },
                Source = HealSource.Llm,
                EvidenceCoverage = 1.0,
                EvidenceThreshold = 0.4,
                ConsensusThreshold = 3,
                AgreedProviders = new[] { "AlphaLlm", "BetaLlm" },
            };

            // 2 votes < 3 threshold -> false
            Assert.False(result.IsConfident);

            // 3 votes >= 3 threshold -> true
            result.AgreedProviders = new[] { "AlphaLlm", "BetaLlm", "GammaLlm" };
            Assert.True(result.IsConfident);
        }

        [Fact]
        public async Task ResolveAsync_CarriesConfiguredMinimumConsensusVotes_IntoConsensusThreshold()
        {
            var (expected, currentTree) = BuildLowConfidenceScenario();
            var providers = new ILlmHealingProvider[]
            {
                new FakeProvider("AlphaLlm", isAvailable: true, resolve: () =>
                    new LlmHealingResult { ProviderName = "AlphaLlm", Success = true, MatchedCandidateId = "c0", Confidence = 0.8 }),
                new FakeProvider("BetaLlm", isAvailable: true, resolve: () =>
                    new LlmHealingResult { ProviderName = "BetaLlm", Success = true, MatchedCandidateId = "c0", Confidence = 0.8 }),
                new FakeProvider("GammaLlm", isAvailable: true, resolve: () =>
                    new LlmHealingResult { ProviderName = "GammaLlm", Success = true, MatchedCandidateId = "c0", Confidence = 0.8 }),
            };

            var weights = new SimilarityWeights { MinimumConsensusVotes = 3 };
            var result = await SelfHealingResolver.ResolveAsync(expected, currentTree, providers, weights, log: _ => { });

            Assert.Equal(HealSource.Llm, result.Source);
            Assert.Equal(3, result.ConsensusThreshold);
            Assert.Equal(new[] { "AlphaLlm", "BetaLlm", "GammaLlm" }, result.AgreedProviders);
            Assert.True(result.IsConfident);
        }

        [Fact]
        public async Task ResolveAsync_FailsConsensusWhenAgreedProvidersBelowCustomMinimumConsensusVotes()
        {
            var (expected, currentTree) = BuildLowConfidenceScenario();
            var providers = new ILlmHealingProvider[]
            {
                new FakeProvider("AlphaLlm", isAvailable: true, resolve: () =>
                    new LlmHealingResult { ProviderName = "AlphaLlm", Success = true, MatchedCandidateId = "c0", Confidence = 0.8 }),
                new FakeProvider("BetaLlm", isAvailable: true, resolve: () =>
                    new LlmHealingResult { ProviderName = "BetaLlm", Success = true, MatchedCandidateId = "c0", Confidence = 0.8 }),
            };

            var weights = new SimilarityWeights { MinimumConsensusVotes = 3 };
            var result = await SelfHealingResolver.ResolveAsync(expected, currentTree, providers, weights, log: _ => { });

            Assert.Equal(HealSource.Heuristic, result.Source);
            Assert.Equal(HealResolutionStatus.NoConsensus, result.ResolutionStatus);
            Assert.Equal(3, result.ConsensusThreshold);
            Assert.False(result.IsConfident);
        }

        [Fact]
        public async Task ResolveAsync_CapsShortlistAtMaxCandidatesForLlm_EvenWhenManyMoreCandidatesQualify()
        {
            // 30 candidates that all individually score below MinimumConfidence but above
            // MinCandidateScore, so all 30 would qualify as candidates - but only the top
            // MaxCandidatesForLlm (20 by default) should actually reach the LLM provider.
            var expected = new UiElementInfo
            {
                ControlType = "Edit",
                Name = "Email Address",
                ParentControlType = "Window",
                SiblingIndex = 2,
                SiblingCount = 7,
                BoundingRectangle = new BoundingRectangle(112, 70, 200, 23),
            };
            var root = new UiElementInfo { ControlType = "Window" };
            for (var i = 0; i < 30; i++)
            {
                root.Children.Add(new UiElementInfo
                {
                    ControlType = "Edit",
                    AutomationId = $"far{i}",
                    Name = "Some Other Field",
                    ParentControlType = "Panel",
                    SiblingIndex = 9,
                    SiblingCount = 10,
                    BoundingRectangle = new BoundingRectangle(900, 900, 200, 23),
                });
            }

            var observed = new FakeProvider("AlphaLlm", isAvailable: true, resolve: () =>
                new LlmHealingResult { ProviderName = "AlphaLlm", Success = true, MatchedCandidateId = "c0", Confidence = 0.9 });
            var second = new FakeProvider("BetaLlm", isAvailable: true, resolve: () =>
                new LlmHealingResult { ProviderName = "BetaLlm", Success = true, MatchedCandidateId = "c0", Confidence = 0.9 });
            var result = await SelfHealingResolver.ResolveAsync(expected, root, new ILlmHealingProvider[] { observed, second }, log: _ => { });
            Assert.Equal(HealSource.Llm, result.Source);
            Assert.NotNull(observed.LastCandidates);
            Assert.Equal(SimilarityWeights.Default.MaxCandidatesForLlm, observed.LastCandidates!.Count);
        }

        [Fact]
        public async Task ResolveAsync_BuildsShortlistFromHeuristicCandidates_PreservingOrderAndIdentity()
        {
            var (expected, currentTree) = BuildLowConfidenceScenario();
            var heuristicResult = SelfHealingResolver.Resolve(expected, currentTree, log: _ => { });

            var observed = new FakeProvider("AlphaLlm", isAvailable: true, resolve: () =>
                new LlmHealingResult { ProviderName = "AlphaLlm", Success = true, MatchedCandidateId = "c0", Confidence = 0.9 });
            var second = new FakeProvider("BetaLlm", isAvailable: true, resolve: () =>
                new LlmHealingResult { ProviderName = "BetaLlm", Success = true, MatchedCandidateId = "c0", Confidence = 0.9 });

            var result = await SelfHealingResolver.ResolveAsync(expected, currentTree, new ILlmHealingProvider[] { observed, second }, log: _ => { });

            Assert.Equal(HealSource.Llm, result.Source);
            Assert.NotNull(observed.LastCandidates);
            var expectedCandidates = heuristicResult.Candidates!
                .Where(c => c.TotalScore >= SimilarityWeights.Default.MinCandidateScore)
                .Take(SimilarityWeights.Default.MaxCandidatesForLlm)
                .ToList();

            Assert.Equal(expectedCandidates.Count, observed.LastCandidates!.Count);
            for (var i = 0; i < expectedCandidates.Count; i++)
            {
                Assert.Equal("c" + i, observed.LastCandidates[i].CandidateId);
                Assert.Equal(expectedCandidates[i].Candidate.AutomationId, observed.LastCandidates[i].Candidate.AutomationId);
                Assert.Equal(expectedCandidates[i].TotalScore, observed.LastCandidates[i].TotalScore, precision: 6);
            }
        }

        [Fact]
        public async Task ResolveAsync_WhenNoCandidatesPassMinScore_ReturnsHeuristicResultWithoutCallingLlm()
        {
            var expected = new UiElementInfo
            {
                ControlType = "Button",
                Name = "CompletelyUnrelatedName",
                ParentControlType = "CompletelyUnrelatedParent",
                BoundingRectangle = new BoundingRectangle(1000, 1000, 10, 10),
            };
            var currentTree = new UiElementInfo
            {
                ControlType = "Window",
                Children =
                {
                    new UiElementInfo
                    {
                        ControlType = "Text",
                        Name = "XYZ",
                        ParentControlType = "Window",
                        BoundingRectangle = new BoundingRectangle(0, 0, 10, 10),
                    },
                },
            };

            var called = false;
            var provider = new FakeProvider("AlphaLlm", isAvailable: true, resolve: () =>
            {
                called = true;
                return new LlmHealingResult { ProviderName = "AlphaLlm", Success = true, MatchedCandidateId = "c0", Confidence = 0.9 };
            });

            // Set MinCandidateScore very high so no candidate qualifies
            var weights = new SimilarityWeights { MinCandidateScore = 0.99 };
            var result = await SelfHealingResolver.ResolveAsync(expected, currentTree, new[] { provider }, weights, log: _ => { });

            Assert.Equal(HealSource.Heuristic, result.Source);
            Assert.False(called, "LLM evaluator should not be called when shortlist is empty.");
        }

        [Fact]
        public void ScoreCandidates_UsesDeterministicTieBreakers_WhenScoresAreEqual()
        {
            var expected = new UiElementInfo
            {
                ControlType = "Edit",
                Name = "Email",
                ParentControlType = "Window",
                SiblingIndex = 0,
                SiblingCount = 2,
                BoundingRectangle = new BoundingRectangle(0, 0, 0, 0),
            };
            var root = new UiElementInfo { ControlType = "Window", SiblingIndex = 99, SiblingCount = 1 };
            root.Children.Add(new UiElementInfo
            {
                ControlType = "Edit",
                AutomationId = "zSecond",
                Name = "Email",
                ParentControlType = "Window",
                SiblingIndex = 0,
                SiblingCount = 2,
                BoundingRectangle = new BoundingRectangle(0, 0, 0, 0),
            });
            root.Children.Add(new UiElementInfo
            {
                ControlType = "Edit",
                AutomationId = "aFirst",
                Name = "Email",
                ParentControlType = "Window",
                SiblingIndex = 0,
                SiblingCount = 2,
                BoundingRectangle = new BoundingRectangle(0, 0, 0, 0),
            });

            var candidates = SelfHealingResolver.ScoreCandidates(expected, root);

            Assert.Equal("aFirst", candidates[0].Candidate.AutomationId);
            Assert.Equal("zSecond", candidates[1].Candidate.AutomationId);
        }

        [Fact]
        public void Resolve_WithInvalidWeights_ThrowsBeforeScoring()
        {
            var expected = new UiElementInfo { ControlType = "Edit" };
            var root = new UiElementInfo { ControlType = "Window" };
            var weights = new SimilarityWeights { MaxCandidatesForLlm = 0 };

            Assert.Throws<ArgumentOutOfRangeException>(() => SelfHealingResolver.Resolve(expected, root, weights, log: _ => { }));
        }

        [Fact]
        public void SimilarityWeights_Validate_RejectsOutOfRangeMinimumEvidenceWeight()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SimilarityWeights { MinimumEvidenceWeight = 1.5 }.Validate());
            Assert.Throws<ArgumentOutOfRangeException>(() => new SimilarityWeights { MinimumEvidenceWeight = -0.1 }.Validate());
            Assert.Throws<ArgumentOutOfRangeException>(() => new SimilarityWeights { MinimumCandidateMargin = 1.5 }.Validate());
            Assert.Throws<ArgumentOutOfRangeException>(() => new SimilarityWeights { MinimumCandidateMargin = -0.1 }.Validate());
        }

        [Fact]
        public async Task ResolveAsync_LlmPick_OnThinEvidenceCandidate_IsNotConfident_AndReportsPickedCoverage()
        {
            // Expected shares everything-empty with both candidates; only candidate B adds
            // sibling metadata, so A has coverage 0.20 and B has 0.35. A scores 1.0 on
            // ControlType alone but fails the evidence gate, so the LLM runs - and its pick
            // must carry the PICKED candidate's coverage, not the heuristic winner's, and
            // must still fail the gate (0.35 < 0.40).
            var expected = new UiElementInfo
            {
                ControlType = "Button",
                Name = "",
                ParentControlType = "",
                SiblingIndex = 0,
                SiblingCount = 0,
                BoundingRectangle = new BoundingRectangle(0, 0, 0, 0),
            };
            var root = new UiElementInfo { ControlType = "Window" };
            root.Children.Add(new UiElementInfo
            {
                ControlType = "Button",
                AutomationId = "aaaThin",
                Name = "",
                ParentControlType = "",
                BoundingRectangle = new BoundingRectangle(0, 0, 0, 0),
            });
            root.Children.Add(new UiElementInfo
            {
                ControlType = "Button",
                AutomationId = "zzzWider",
                Name = "",
                ParentControlType = "",
                SiblingIndex = 1,
                SiblingCount = 3,
                BoundingRectangle = new BoundingRectangle(0, 0, 0, 0),
            });

            var result = await SelfHealingResolver.ResolveAsync(expected, root, AgreeingProviders("c1"), log: _ => { });

            Assert.Equal(HealSource.Llm, result.Source);
            Assert.Equal("zzzWider", result.Matched!.AutomationId);
            Assert.Equal(0.35, result.EvidenceCoverage, 6);
            Assert.False(result.IsConfident, "The evidence gate applies to LLM picks too - thin evidence stays thin.");
        }

        [Fact]
        public async Task ResolveAsync_LlmPicksDivergentCandidate_CarriesMatchedScoreBreakdownAndDivergenceFlag()
        {
            // Issue #6: Heuristic winner is c0 (ControlType="Button", higher heuristic score).
            // LLM fallback picks c1 (ControlType="Edit", lower heuristic score).
            // HealResult must carry c1's heuristic score and breakdown (not c0's),
            // and explicitly report DivergedFromHeuristic = true.
            var expected = new UiElementInfo
            {
                ControlType = "Button",
                Name = "Save Changes",
                AutomationId = "btnSave",
                ParentControlType = "Window",
                ParentAutomationId = "MainForm",
                BoundingRectangle = new BoundingRectangle(100, 100, 80, 30),
            };

            var root = new UiElementInfo { ControlType = "Window", AutomationId = "MainForm" };
            // c0: Button with ControlType matching Button -> ControlTypeScore = 1.0
            var buttonCandidate = new UiElementInfo
            {
                ControlType = "Button",
                AutomationId = "btnOldSave",
                Name = "Something Else",
                ParentControlType = "Panel",
                BoundingRectangle = new BoundingRectangle(800, 800, 80, 30),
            };
            // c1: Edit with ControlType Edit != Button -> ControlTypeScore = 0.0
            var editCandidate = new UiElementInfo
            {
                ControlType = "Edit",
                AutomationId = "txtSaveName",
                Name = "Save Input",
                ParentControlType = "Panel",
                BoundingRectangle = new BoundingRectangle(800, 850, 80, 30),
            };
            root.Children.Add(buttonCandidate);
            root.Children.Add(editCandidate);

            var heuristicResult = SelfHealingResolver.Resolve(expected, root, log: _ => { });
            Assert.Equal("btnOldSave", heuristicResult.Matched!.AutomationId);
            Assert.False(heuristicResult.IsConfident);

            var result = await SelfHealingResolver.ResolveAsync(
                expected, root, AgreeingProviders("c1", confidence: 0.92, reasoning: "contextual match"), log: _ => { });

            Assert.Equal(HealSource.Llm, result.Source);
            Assert.Equal("txtSaveName", result.Matched!.AutomationId);
            // Score must be c1's score, NOT c0's
            Assert.True(result.Score < heuristicResult.Score);
            Assert.NotNull(result.ScoreBreakdown);
            // ScoreBreakdown must reflect c1 (Edit != Button -> 0.0), NOT c0 (Button == Button -> 1.0)
            Assert.Equal(0.0, result.ScoreBreakdown!.ControlTypeScore);
            Assert.Equal(1.0, heuristicResult.ScoreBreakdown!.ControlTypeScore);
            // Divergence flags
            Assert.True(result.DivergedFromHeuristic);
            Assert.NotNull(result.HeuristicMatched);
            Assert.Equal("btnOldSave", result.HeuristicMatched!.AutomationId);
            Assert.Equal(heuristicResult.Score, result.HeuristicScore);
        }

        [Fact]
        public async Task ResolveAsync_LlmPicksHeuristicWinner_DivergedFromHeuristicIsFalse()
        {
            // When a competing alternative (c1) exists but the LLM picks c0 (the heuristic winner),
            // DivergedFromHeuristic must be false, and Score/ScoreBreakdown must match c0.
            var expected = new UiElementInfo
            {
                ControlType = "Button",
                Name = "Save Changes",
                AutomationId = "btnSave",
                ParentControlType = "Window",
                ParentAutomationId = "MainForm",
                BoundingRectangle = new BoundingRectangle(100, 100, 80, 30),
            };

            var root = new UiElementInfo { ControlType = "Window", AutomationId = "MainForm" };
            var buttonCandidate = new UiElementInfo
            {
                ControlType = "Button",
                AutomationId = "btnOldSave",
                Name = "Something Else",
                ParentControlType = "Panel",
                BoundingRectangle = new BoundingRectangle(800, 800, 80, 30),
            };
            var editCandidate = new UiElementInfo
            {
                ControlType = "Edit",
                AutomationId = "txtSaveName",
                Name = "Save Input",
                ParentControlType = "Panel",
                BoundingRectangle = new BoundingRectangle(800, 850, 80, 30),
            };
            root.Children.Add(buttonCandidate);
            root.Children.Add(editCandidate);

            var heuristicResult = SelfHealingResolver.Resolve(expected, root, log: _ => { });
            Assert.Equal("btnOldSave", heuristicResult.Matched!.AutomationId);
            Assert.False(heuristicResult.IsConfident);

            var result = await SelfHealingResolver.ResolveAsync(
                expected, root, AgreeingProviders("c0", confidence: 0.92, reasoning: "agrees with heuristic"), log: _ => { });

            Assert.Equal(HealSource.Llm, result.Source);
            Assert.Equal("btnOldSave", result.Matched!.AutomationId);
            Assert.False(result.DivergedFromHeuristic);
            Assert.NotNull(result.HeuristicMatched);
            Assert.Equal("btnOldSave", result.HeuristicMatched!.AutomationId);
            Assert.Equal(heuristicResult.Score, result.Score);
            Assert.Equal(1.0, result.ScoreBreakdown!.ControlTypeScore);
        }

        [Fact]
        public async Task ResolveAsync_WhenProviderTimesOut_FallsBackToHeuristicResult()
        {
            var (expected, currentTree) = BuildLowConfidenceScenario();
            var handler = new FakeSlowHttpMessageHandler();
            // delayAsync is stubbed out because a timed-out attempt is retried like any other
            // transient failure: with the real backoff this single test waits ~800ms.
            var slowProvider = new ClaudeHealingProvider(
                httpClient: new HttpClient(handler),
                apiKey: "sk-test-key",
                timeout: TimeSpan.FromMilliseconds(50),
                delayAsync: (_, _) => Task.CompletedTask);

            var result = await SelfHealingResolver.ResolveAsync(
                expected,
                currentTree,
                new[] { slowProvider },
                log: _ => { });

            Assert.NotNull(result.Matched);
            Assert.Equal("textBoxFar", result.Matched!.AutomationId);
            Assert.Equal(HealSource.Heuristic, result.Source);
            Assert.False(result.IsConfident);
        }

        private sealed class FakeSlowHttpMessageHandler : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                await Task.Delay(1000, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
        }

        [Fact]
        public void SimilarityWeights_Default_ReturnsFreshInstanceEachTime()
        {
            var first = SimilarityWeights.Default;
            var second = SimilarityWeights.Default;
            first.MinimumConfidence = 0.99;

            Assert.NotSame(first, second);
            Assert.Equal(0.5, second.MinimumConfidence);
        }

        [Fact]
        public void Resolve_ControlTypeOnlyMatch_HasThinEvidence_AndIsNotConfident()
        {
            // Issue #3's core case: expected and candidate share only ControlType=Button;
            // everything else is missing on both sides. The score may still be 1.0, but
            // with only 0.20 of the total weight backed by evidence it must not be confident.
            var expected = new UiElementInfo
            {
                ControlType = "Button",
                Name = "",
                ParentControlType = "",
                BoundingRectangle = new BoundingRectangle(0, 0, 0, 0),
            };
            var root = new UiElementInfo { ControlType = "Window" };
            root.Children.Add(new UiElementInfo
            {
                ControlType = "Button",
                AutomationId = "btnWhatever",
                Name = "",
                ParentControlType = "",
                BoundingRectangle = new BoundingRectangle(0, 0, 0, 0),
            });

            var result = SelfHealingResolver.Resolve(expected, root, log: _ => { });

            Assert.NotNull(result.Matched);
            Assert.Equal(1.0, result.Score);
            Assert.Equal(0.20, result.EvidenceCoverage, 6);
            Assert.False(result.IsConfident, "A ControlType-only 1.0 is thin evidence, not a confident match.");
            Assert.Equal(1.0, result.ScoreBreakdown!.ControlTypeScore);
            Assert.Null(result.ScoreBreakdown.NameScore);
            Assert.Null(result.ScoreBreakdown.ParentControlTypeScore);
            Assert.Null(result.ScoreBreakdown.SiblingPositionScore);
            Assert.Null(result.ScoreBreakdown.PositionScore);
        }

        [Fact]
        public void Resolve_FullEvidenceMatch_ReportsHighCoverage_AndStaysConfident()
        {
            var expected = new UiElementInfo
            {
                ControlType = "Edit",
                Name = "",
                AutomationId = "txtEmail",
                ParentControlType = "Window",
                ParentAutomationId = "MainForm",
                SiblingIndex = 2,
                SiblingCount = 7,
                BoundingRectangle = new BoundingRectangle(112, 70, 200, 23),
            };
            var currentTree = BuildCurrentMainFormTree(renamedEmailAutomationId: "textBox1");

            var result = SelfHealingResolver.Resolve(expected, currentTree, log: _ => { });

            // Name is empty on both sides (null signal): 0.20 + 0.20 + 0.15 + 0.25 of the
            // weight is backed by real evidence, well above MinimumEvidenceWeight (0.40).
            Assert.Equal(0.80, result.EvidenceCoverage, 6);
            Assert.True(result.IsConfident, $"Expected a confident match, but the score was: {result.Score}");
        }

        [Fact]
        public void HealingReport_PersistsEveryCandidate_WithComponentsAndEvidenceCoverage()
        {
            var expected = new UiElementInfo
            {
                ControlType = "Edit",
                Name = "Email",
                ParentControlType = "Window",
                SiblingIndex = 0,
                SiblingCount = 2,
                BoundingRectangle = new BoundingRectangle(100, 100, 50, 20),
            };
            var root = new UiElementInfo { ControlType = "Window", SiblingIndex = 999, SiblingCount = 1 };
            root.Children.Add(new UiElementInfo
            {
                ControlType = "Edit",
                AutomationId = "nearMatch",
                Name = "Email",
                ParentControlType = "Window",
                SiblingIndex = 0,
                SiblingCount = 2,
                BoundingRectangle = new BoundingRectangle(100, 100, 50, 20),
            });
            root.Children.Add(new UiElementInfo
            {
                ControlType = "Edit",
                AutomationId = "weakerMatch",
                Name = "Something Else",
                ParentControlType = "Window",
                SiblingIndex = 1,
                SiblingCount = 2,
                BoundingRectangle = new BoundingRectangle(400, 400, 50, 20),
            });

            var result = SelfHealingResolver.Resolve(expected, root, log: _ => { });
            var entry = HealingReportEntry.FromHealResult("email_field", expected, result.Matched!, result);

            // The report must carry every scored candidate (not just the winner) with its
            // TotalScore, Components, and EvidenceCoverage - and the list is UNPRUNED, so
            // even the 0-scored root Window shows up; the #15 benchmark needs this for
            // offline threshold sweeps below today's MinCandidateScore.
            Assert.NotNull(entry.Candidates);
            Assert.Equal(3, entry.Candidates!.Count);
            Assert.All(entry.Candidates, c =>
            {
                Assert.NotNull(c.Components);
                Assert.InRange(c.TotalScore, 0.0, 1.0);
                Assert.InRange(c.EvidenceCoverage, 0.0, 1.0);
            });
            Assert.Equal("nearMatch", entry.Candidates[0].AutomationId);
            Assert.Equal("weakerMatch", entry.Candidates[1].AutomationId);
            Assert.Equal(0.0, entry.Candidates[2].TotalScore); // the pruned root is still recorded
            Assert.Equal(result.EvidenceCoverage, entry.EvidenceCoverage!.Value);
            Assert.Equal(result.RunnerUpScore, entry.RunnerUpScore);
        }

        [Fact]
        public void Resolve_IdenticalTopCandidates_AreAmbiguous_AndNotConfident()
        {
            // Two candidates with identical structural evidence: margin = 0 < 0.05, so the
            // resolver must say "I don't know" instead of silently picking the tie-break winner.
            var expected = new UiElementInfo
            {
                ControlType = "Button",
                Name = "Save",
                ParentControlType = "Window",
                SiblingIndex = 0,
                SiblingCount = 2,
                BoundingRectangle = new BoundingRectangle(100, 100, 50, 20),
            };
            var root = new UiElementInfo { ControlType = "Window", SiblingIndex = 999, SiblingCount = 1 };
            foreach (var automationId in new[] { "candidateA", "candidateB" })
            {
                root.Children.Add(new UiElementInfo
                {
                    ControlType = "Button",
                    AutomationId = automationId,
                    Name = "Save",
                    ParentControlType = "Window",
                    SiblingIndex = 0,
                    SiblingCount = 2,
                    BoundingRectangle = new BoundingRectangle(100, 100, 50, 20),
                });
            }

            var result = SelfHealingResolver.Resolve(expected, root, log: _ => { });

            Assert.NotNull(result.Matched); // deterministic tie-break still picks one
            Assert.Equal(1.0, result.Score);
            Assert.Equal(1.0, result.RunnerUpScore);
            Assert.False(result.IsConfident, "A zero margin must not be confident even at score 1.0.");
        }

        [Fact]
        public void Resolve_ClearWinner_RecordsRunnerUp_AndStaysConfident()
        {
            var expected = new UiElementInfo
            {
                ControlType = "Edit",
                Name = "Email",
                ParentControlType = "Window",
                SiblingIndex = 0,
                SiblingCount = 2,
                BoundingRectangle = new BoundingRectangle(100, 100, 50, 20),
            };
            var root = new UiElementInfo { ControlType = "Window", SiblingIndex = 999, SiblingCount = 1 };
            root.Children.Add(new UiElementInfo
            {
                ControlType = "Edit",
                AutomationId = "clearWinner",
                Name = "Email",
                ParentControlType = "Window",
                SiblingIndex = 0,
                SiblingCount = 2,
                BoundingRectangle = new BoundingRectangle(100, 100, 50, 20),
            });
            root.Children.Add(new UiElementInfo
            {
                ControlType = "Edit",
                AutomationId = "distantRunnerUp",
                Name = "Something Else Entirely",
                ParentControlType = "Window",
                SiblingIndex = 1,
                SiblingCount = 2,
                BoundingRectangle = new BoundingRectangle(400, 400, 50, 20),
            });

            var result = SelfHealingResolver.Resolve(expected, root, log: _ => { });

            Assert.Equal("clearWinner", result.Matched!.AutomationId);
            Assert.True(result.IsConfident, $"Score {result.Score} with runner-up {result.RunnerUpScore} should clear the margin.");
            Assert.NotNull(result.RunnerUpScore);
            Assert.True(result.Score - result.RunnerUpScore!.Value >= 0.05, "Margin should be at least the default MinimumCandidateMargin.");
        }

        [Fact]
        public void Resolve_SingleCandidate_HasNoRunnerUp_AndMarginDoesNotBlock()
        {
            var expected = new UiElementInfo
            {
                ControlType = "Edit",
                Name = "Email",
                ParentControlType = "Window",
                SiblingIndex = 0,
                SiblingCount = 2,
                BoundingRectangle = new BoundingRectangle(100, 100, 50, 20),
            };
            var root = new UiElementInfo { ControlType = "Window", SiblingIndex = 999, SiblingCount = 1 };
            root.Children.Add(new UiElementInfo
            {
                ControlType = "Edit",
                AutomationId = "onlyCandidate",
                Name = "Email",
                ParentControlType = "Window",
                SiblingIndex = 0,
                SiblingCount = 2,
                BoundingRectangle = new BoundingRectangle(100, 100, 50, 20),
            });

            var result = SelfHealingResolver.Resolve(expected, root, log: _ => { });

            Assert.Equal("onlyCandidate", result.Matched!.AutomationId);
            Assert.Null(result.RunnerUpScore);
            Assert.True(result.IsConfident, "With no runner-up there is no competition - margin must not block.");
        }

        [Fact]

        public void CandidateMargin_SatisfiesBothCasesFromIssue4()
        {
            var minimumMargin = SimilarityWeights.Default.MinimumCandidateMargin;

            // Issue #4's two suggested cases, verbatim. They also pin down why the default is
            // 0.05 rather than the "e.g. 0.10" the issue text floats: 0.88 - 0.79 = 0.09, so a
            // 0.10 threshold would make the issue's own first case fail. Anything in
            // (0.001, 0.057] satisfies both cases and the WinForms demo margin below;
            // recalibration against real data is #15's job, not a reason to reopen #4.
            Assert.True(CandidateMargin.HasSufficientMargin(0.88, 0.79, minimumMargin), "0.88 vs 0.79 is a clear winner.");
            Assert.False(CandidateMargin.HasSufficientMargin(0.88, 0.879, minimumMargin), "0.88 vs 0.879 is a coin flip, not a match.");
            Assert.True(CandidateMargin.HasSufficientMargin(0.88, null, minimumMargin), "No runner-up means no competition.");
        }

        [Fact]

        public void Resolve_WinFormsDemoScenario_KeepsMarginAboveThreshold()
        {
            // Guard for the flagship demo (txtEmail -> textBox1). Its runner-up is txtLastName -
            // a sibling Edit one row up, structurally almost identical - so the margin is only
            // ~0.057 against a 0.05 threshold: barely 0.007 of headroom. Any weight or tolerance
            // change that eats it silently turns the demo AMBIGUOUS and diverts it to LLM
            // fallback. This test fails first, on every platform, with the actual number -
            // instead of the Windows-only live MainFormScenarioTests failing for reasons that
            // look unrelated.
            var expected = new UiElementInfo
            {
                ControlType = "Edit",
                Name = "",
                AutomationId = "txtEmail",
                ParentControlType = "Window",
                ParentAutomationId = "MainForm",
                SiblingIndex = 2,
                SiblingCount = 7,
                BoundingRectangle = new BoundingRectangle(112, 70, 200, 23),
            };
            var currentTree = BuildCurrentMainFormTree(renamedEmailAutomationId: "textBox1");
            var minimumMargin = SimilarityWeights.Default.MinimumCandidateMargin;

            var result = SelfHealingResolver.Resolve(expected, currentTree, log: _ => { });

            Assert.Equal("textBox1", result.Matched!.AutomationId);
            Assert.NotNull(result.RunnerUpScore);
            var margin = result.Score - result.RunnerUpScore!.Value;
            Assert.True(
                margin >= minimumMargin,
                $"The demo scenario's runner-up margin dropped to {margin:F4}, below MinimumCandidateMargin ({minimumMargin:F2}). " +
                "If a scoring weight changed deliberately, recalibrate the margin default against the #15 benchmark " +
                "rather than loosening this assertion.");
            Assert.True(result.IsConfident, $"The demo must heal confidently without LLM fallback (margin {margin:F4}).");
        }

        // A heuristic score deliberately pushed well below MinimumConfidence (0.5): mismatched
        // parent, distant sibling position, dissimilar name, and a screen position far outside
        // the 300px tolerance radius - only ControlType still matches, keeping it a candidate at all.

        private static (UiElementInfo Expected, UiElementInfo CurrentTree) BuildLowConfidenceScenario()
        {
            var expected = new UiElementInfo
            {
                ControlType = "Edit",
                Name = "Email Address",
                AutomationId = "txtEmail",
                ParentControlType = "Window",
                ParentAutomationId = "MainForm",
                SiblingIndex = 2,
                SiblingCount = 7,
                BoundingRectangle = new BoundingRectangle(112, 70, 200, 23),
            };
            var root = new UiElementInfo { ControlType = "Window", AutomationId = "MainForm" };
            root.Children.Add(new UiElementInfo
            {
                ControlType = "Edit",
                AutomationId = "textBoxFar",
                Name = "Some Other Field",
                ParentControlType = "Panel",
                ParentAutomationId = "somePanel",
                SiblingIndex = 9,
                SiblingCount = 10,
                BoundingRectangle = new BoundingRectangle(900, 900, 200, 23),
            });
            return (expected, root);
        }

        // Consensus acceptance (#10) needs at least two independent providers naming the same
        // candidate, so every test that expects an LLM pick to be accepted supplies a pair.
        // The names are deliberately ordinal-ordered: LlmProviderName and LlmReasoning take
        // the ordinal-first agreeing provider, so "AlphaLlm" is the deterministic winner.
        private static ILlmHealingProvider[] AgreeingProviders(string candidateId, double confidence = 0.9, string reasoning = "")
        {
            return new ILlmHealingProvider[]
            {
                new FakeProvider("AlphaLlm", isAvailable: true, resolve: () =>
                    new LlmHealingResult { ProviderName = "AlphaLlm", Success = true, MatchedCandidateId = candidateId, Confidence = confidence, Reasoning = reasoning }),
                new FakeProvider("BetaLlm", isAvailable: true, resolve: () =>
                    new LlmHealingResult { ProviderName = "BetaLlm", Success = true, MatchedCandidateId = candidateId, Confidence = confidence, Reasoning = reasoning }),
            };
        }

        private sealed class FakeProvider : ILlmHealingProvider
        {
            private readonly Func<LlmHealingResult> _resolve;

            public FakeProvider(string name, bool isAvailable, Func<LlmHealingResult> resolve)
            {
                Name = name;
                IsAvailable = isAvailable;
                _resolve = resolve;
            }

            public string Name { get; }
            public bool IsAvailable { get; }
            public IReadOnlyList<CandidateScore>? LastCandidates { get; private set; }
            public string? LastPlatform { get; private set; }
            public Task<LlmHealingResult> ResolveAsync(
                UiElementInfo expected,
                IReadOnlyList<CandidateScore> candidates,
                string? platform = null,
                CancellationToken cancellationToken = default)
            {
                LastCandidates = candidates;
                LastPlatform = platform;
                var res = _resolve();
                if (res.AttemptCount == 0)
                {
                    res.AttemptCount = 1;
                }
                return Task.FromResult(res);
            }
        }

        private static UiElementInfo BuildCurrentMainFormTree(string renamedEmailAutomationId, string saveControlType = "Button")
        {
            var root = new UiElementInfo { ControlType = "Window", Name = "Customer Registration Form", AutomationId = "MainForm" };
            var children = new[]
            {
                new UiElementInfo { ControlType = "Edit", AutomationId = "txtFirstName", BoundingRectangle = new BoundingRectangle(112, 12, 200, 23) },
                new UiElementInfo { ControlType = "Edit", AutomationId = "txtLastName", BoundingRectangle = new BoundingRectangle(112, 41, 200, 23) },
                new UiElementInfo { ControlType = "Edit", AutomationId = renamedEmailAutomationId, BoundingRectangle = new BoundingRectangle(112, 70, 200, 23) },
                new UiElementInfo { ControlType = "ComboBox", AutomationId = "cmbRecordType", BoundingRectangle = new BoundingRectangle(112, 99, 200, 23) },
                new UiElementInfo { ControlType = "Pane", AutomationId = "panel1", BoundingRectangle = new BoundingRectangle(12, 131, 300, 34) },
                new UiElementInfo { ControlType = saveControlType, Name = "Save", AutomationId = "btnSave", BoundingRectangle = new BoundingRectangle(112, 178, 100, 30) },
                new UiElementInfo { ControlType = "DataGrid", AutomationId = "dgvRecords", BoundingRectangle = new BoundingRectangle(12, 220, 400, 150) },
            };
            for (var i = 0; i < children.Length; i++)
            {
                children[i].ParentControlType = root.ControlType;
                children[i].ParentAutomationId = root.AutomationId;
                children[i].SiblingIndex = i;
                children[i].SiblingCount = children.Length;
                root.Children.Add(children[i]);
            }

            return root;
        }
    }
}
