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
        public void SimilarityWeights_Default_ReturnsFreshInstanceEachTime()
        {
            var first = SimilarityWeights.Default;
            var second = SimilarityWeights.Default;
            first.MinimumConfidence = 0.99;

            Assert.NotSame(first, second);
            Assert.Equal(0.5, second.MinimumConfidence);
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
