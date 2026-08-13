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
            var provider = new FakeProvider("Fake", isAvailable: true, resolve: () =>
                new LlmHealingResult { ProviderName = "Fake", Success = true, MatchedCandidateId = "c0", Confidence = 0.9, Reasoning = "structural match" });
            var result = await SelfHealingResolver.ResolveAsync(expected, currentTree, new[] { provider }, log: _ => { });
            Assert.Equal(HealSource.Llm, result.Source);
            Assert.Equal("textBoxFar", result.Matched!.AutomationId);
            Assert.Equal("Fake", result.LlmProviderName);
            Assert.Equal(0.9, result.LlmConfidence);
            Assert.Equal("structural match", result.LlmReasoning);
            Assert.True(result.IsConfident);
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

        public async Task ResolveAsync_LlmConfidenceBelowMinimumLlmConfidence_FallsBackToHeuristicResult()
        {
            var (expected, currentTree) = BuildLowConfidenceScenario();
            var provider = new FakeProvider("Fake", isAvailable: true, resolve: () =>
                new LlmHealingResult { ProviderName = "Fake", Success = true, MatchedCandidateId = "c0", Confidence = 0.1 });
            var result = await SelfHealingResolver.ResolveAsync(expected, currentTree, new[] { provider }, log: _ => { });

            // A low-confidence LLM pick must not silently replace the heuristic's own result -
            // Matched should stay the heuristic's pick, not switch to whatever the LLM guessed.
            Assert.Equal(HealSource.Heuristic, result.Source);
            Assert.Equal("textBoxFar", result.Matched!.AutomationId);
        }

        [Fact]
        public async Task ResolveAsync_LlmResult_UsesMinimumLlmConfidenceForIsConfident()
        {
            var (expected, currentTree) = BuildLowConfidenceScenario();
            var weights = new SimilarityWeights
            {
                MinimumConfidence = 0.8,
                MinimumLlmConfidence = 0.5,
            };
            var provider = new FakeProvider("Fake", isAvailable: true, resolve: () =>
                new LlmHealingResult { ProviderName = "Fake", Success = true, MatchedCandidateId = "c0", Confidence = 0.6 });

            var result = await SelfHealingResolver.ResolveAsync(expected, currentTree, new[] { provider }, weights, log: _ => { });

            Assert.Equal(HealSource.Llm, result.Source);
            Assert.Equal(weights.MinimumLlmConfidence, result.ConfidenceThreshold);
            Assert.True(result.IsConfident);
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
            var provider = new FakeProvider("Fake", isAvailable: true, resolve: () =>
                new LlmHealingResult { ProviderName = "Fake", Success = true, MatchedCandidateId = "c0", Confidence = 0.9, Reasoning = "structural match" });
            var result = await SelfHealingResolver.ResolveAsync(expected, root, new[] { provider }, log: _ => { });
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

        public async Task ResolveAsync_MultipleProvidersDisagree_PicksHighestConfidenceResult()
        {
            var (expected, currentTree) = BuildLowConfidenceScenario();
            var lowConfidenceProvider = new FakeProvider("Low", isAvailable: true, resolve: () =>
                new LlmHealingResult { ProviderName = "Low", Success = true, MatchedCandidateId = "c0", Confidence = 0.4 });
            var highConfidenceProvider = new FakeProvider("High", isAvailable: true, resolve: () =>
                new LlmHealingResult { ProviderName = "High", Success = true, MatchedCandidateId = "c0", Confidence = 0.85 });
            var result = await SelfHealingResolver.ResolveAsync(expected, currentTree, new[] { lowConfidenceProvider, highConfidenceProvider }, log: _ => { });
            Assert.Equal("High", result.LlmProviderName);
            Assert.Equal(0.85, result.LlmConfidence);
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

            var provider = new FakeProvider("Fake", isAvailable: true, resolve: () =>
                new LlmHealingResult { ProviderName = "Fake", Success = true, MatchedCandidateId = "c0", Confidence = 0.9 });
            var result = await SelfHealingResolver.ResolveAsync(expected, root, new[] { provider }, log: _ => { });
            Assert.Equal(HealSource.Llm, result.Source);
            Assert.NotNull(provider.LastCandidates);
            Assert.Equal(SimilarityWeights.Default.MaxCandidatesForLlm, provider.LastCandidates!.Count);
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

            var provider = new FakeProvider("Fake", isAvailable: true, resolve: () =>
                new LlmHealingResult { ProviderName = "Fake", Success = true, MatchedCandidateId = "c1", Confidence = 0.9 });
            var result = await SelfHealingResolver.ResolveAsync(expected, root, new[] { provider }, log: _ => { });

            Assert.Equal(HealSource.Llm, result.Source);
            Assert.Equal("zzzWider", result.Matched!.AutomationId);
            Assert.Equal(0.35, result.EvidenceCoverage, 6);
            Assert.False(result.IsConfident, "The evidence gate applies to LLM picks too - thin evidence stays thin.");
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
            public Task<LlmHealingResult> ResolveAsync(UiElementInfo expected, IReadOnlyList<CandidateScore> candidates, CancellationToken cancellationToken = default)
            {
                LastCandidates = candidates;
                return Task.FromResult(_resolve());
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
