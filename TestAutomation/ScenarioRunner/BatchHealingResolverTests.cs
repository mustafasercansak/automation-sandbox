using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LlmHealing;
using SelfHealing;
using UiModel;
using Xunit;

namespace ScenarioRunner
{
    public class BatchHealingResolverTests
    {
        [Fact]
        public void ResolveBatch_StrongerAcceptedClaimWinsContention()
        {
            var candidate = Element("Button", "Save", automationId: "live-save");
            var tree = Tree(candidate);
            var exact = Element("Button", "Save", automationId: "stale-exact");
            var weaker = Element("Button", "Save draft", automationId: "stale-weaker");

            var batch = SelfHealingResolver.ResolveBatch(
                new[]
                {
                    new BatchHealingRequest("save.exact", exact),
                    new BatchHealingRequest("save.weaker", weaker),
                },
                tree,
                log: _ => { });

            Assert.Equal(1, batch.ContestedCandidateCount);
            Assert.Equal(1, batch.ReconciliationDeclineCount);
            Assert.True(batch.Items[0].Result.IsConfident);
            Assert.Equal(BatchReconciliationDisposition.WonContention, batch.Items[0].ReconciliationDisposition);
            Assert.False(batch.Items[1].Result.IsConfident);
            Assert.True(batch.Items[1].Result.RejectedByReconciliation);
            Assert.Equal(HealResolutionStatus.OwnershipConflict, batch.Items[1].Result.ResolutionStatus);
            Assert.Equal(BatchReconciliationDisposition.DeclinedByStrongerClaim, batch.Items[1].ReconciliationDisposition);
        }

        [Fact]
        public void ResolveBatch_AmbiguousOwnershipDeclinesEveryClaimantDeterministically()
        {
            var candidate = Element("Button", "Save", automationId: "live-save");
            var tree = Tree(candidate);
            var expected = Element("Button", "Save", automationId: "stale-save");

            var batch = SelfHealingResolver.ResolveBatch(
                new[]
                {
                    new BatchHealingRequest("z-locator", expected),
                    new BatchHealingRequest("a-locator", UiElementSnapshot.Capture(expected)),
                },
                tree,
                log: _ => { });

            Assert.All(batch.Items, item =>
            {
                Assert.True(item.WasIndependentlyConfident);
                Assert.False(item.Result.IsConfident);
                Assert.Equal(BatchReconciliationDisposition.DeclinedAmbiguousContention, item.ReconciliationDisposition);
            });
            Assert.Equal(2, batch.ReconciliationDeclineCount);
        }

        [Fact]
        public void ResolveBatch_UncontestedAcceptedClaimIsPreserved()
        {
            // The batch guard has no absence signal. Even if this candidate is only an
            // incidental survivor for a deleted locator, one accepted claimant is preserved.
            var incidentalCandidate = Element("Button", "Save", automationId: "");
            var batch = SelfHealingResolver.ResolveBatch(
                new[] { new BatchHealingRequest("deleted.save", Element("Button", "Save", "old-save")) },
                Tree(incidentalCandidate),
                log: _ => { });

            var item = Assert.Single(batch.Items);
            Assert.True(item.Result.IsConfident);
            Assert.Equal(BatchReconciliationDisposition.PreservedUncontested, item.ReconciliationDisposition);
            Assert.Equal("r/0", item.CandidateIdentity);
        }

        [Fact]
        public void ResolveBatch_EmptyAutomationIdCandidateStillHasUniqueOwnershipIdentity()
        {
            var candidate = Element("Button", "Save", automationId: "");
            var expected = Element("Button", "Save", automationId: "old-save");

            var batch = SelfHealingResolver.ResolveBatch(
                new[]
                {
                    new BatchHealingRequest("save.one", expected),
                    new BatchHealingRequest("save.two", UiElementSnapshot.Capture(expected)),
                },
                Tree(candidate),
                log: _ => { });

            Assert.Equal(1, batch.ContestedCandidateCount);
            Assert.All(batch.Items, item => Assert.Equal("r/0", item.CandidateIdentity));
            Assert.All(batch.Items, item => Assert.False(item.Result.IsConfident));
        }

        [Fact]
        public async Task ResolveBatchAsync_ProviderFailureOnOneLocatorDoesNotEraseOtherResults()
        {
            var strongCandidate = Element("Button", "Save", "live-save");
            var thinCandidate = new UiElementInfo { ControlType = "TextBox", AutomationId = "live-input" };
            var provider = new ThrowingProvider();

            var batch = await SelfHealingResolver.ResolveBatchAsync(
                new[]
                {
                    new BatchHealingRequest("thin", new UiElementInfo { ControlType = "TextBox", AutomationId = "old-input" }),
                    new BatchHealingRequest("strong", Element("Button", "Save", "old-save")),
                },
                Tree(strongCandidate, thinCandidate),
                new[] { provider },
                log: _ => { });

            Assert.Equal(1, provider.CallCount);
            Assert.Equal(HealResolutionStatus.ProviderError, batch.Items[0].Result.ResolutionStatus);
            Assert.Contains("provider failed", batch.Items[0].Result.ProviderErrors![provider.Name]);
            Assert.True(batch.Items[1].Result.IsConfident);
            Assert.Equal(BatchReconciliationDisposition.PreservedUncontested, batch.Items[1].ReconciliationDisposition);
        }

        [Fact]
        public async Task ResolveBatchAsync_UsesExistingPerLocatorConsensusWithoutConfidenceGating()
        {
            var first = Element("Button", "Save", "a-live");
            var second = Element("Button", "Save", "b-live");
            var providers = new ILlmHealingProvider[]
            {
                new FixedVoteProvider("Alpha", "c0", confidence: 0.01),
                new FixedVoteProvider("Beta", "c0", confidence: 0.99),
            };

            var batch = await SelfHealingResolver.ResolveBatchAsync(
                new[] { new BatchHealingRequest("save", Element("Button", "Save", "old-save")) },
                Tree(first, second),
                providers,
                log: _ => { });

            var item = Assert.Single(batch.Items);
            Assert.Equal(HealSource.Llm, item.Result.Source);
            Assert.True(item.Result.IsConfident);
            Assert.Same(first, item.Result.Matched);
            Assert.Equal(new[] { "Alpha", "Beta" }, item.Result.AgreedProviders);
            Assert.Equal(0.5, item.Result.LlmConfidence);
            Assert.Equal(BatchReconciliationDisposition.PreservedUncontested, item.ReconciliationDisposition);
        }

        [Fact]
        public async Task ResolveBatchAsync_MixedSourceContention_DeclinesAllClaimantsAsAmbiguous()
        {
            // Issue #268: A confident heuristic claim and an independently confident LLM consensus claim
            // contest the same live candidate. Because heuristic score (e.g. 0.55) and LLM consensus
            // (2 provider votes) operate on incompatible scales, the heuristic claim must not win
            // purely by score subtraction. Both claims must be declined for manual review.
            var live1 = new UiElementInfo
            {
                ControlType = "Button",
                Name = "Submit",
                AutomationId = "live-submit-1",
                ParentControlType = "Window",
                ParentAutomationId = "root",
                SiblingIndex = 0,
                SiblingCount = 2,
                BoundingRectangle = new BoundingRectangle(100, 100, 100, 30),
            };
            var live2 = new UiElementInfo
            {
                ControlType = "Button",
                Name = "Submit",
                AutomationId = "live-submit-2",
                ParentControlType = "Window",
                ParentAutomationId = "root",
                SiblingIndex = 1,
                SiblingCount = 2,
                BoundingRectangle = new BoundingRectangle(600, 600, 100, 30),
            };

            // Heuristic expected matches live1 with high margin over live2 due to position
            var heuristicExpected = new UiElementInfo
            {
                ControlType = "Button",
                Name = "Submit",
                AutomationId = "old-submit-heuristic",
                ParentControlType = "Window",
                ParentAutomationId = "root",
                SiblingIndex = 0,
                SiblingCount = 2,
                BoundingRectangle = new BoundingRectangle(100, 100, 100, 30),
            };

            // LLM expected has low heuristic similarity (0.467 < 0.50) due to ControlType mismatch, triggering LLM fallback
            var llmExpected = new UiElementInfo
            {
                ControlType = "Custom",
                Name = "Order",
                AutomationId = "old-order-llm",
                ParentControlType = "Window",
                ParentAutomationId = "root",
                SiblingIndex = 0,
                SiblingCount = 2,
            };

            // Providers vote for c0 (live1)
            var providers = new ILlmHealingProvider[]
            {
                new FixedVoteProvider("Alpha", "c0", confidence: 0.90),
                new FixedVoteProvider("Beta", "c0", confidence: 0.85),
            };

            var batch = await SelfHealingResolver.ResolveBatchAsync(
                new[]
                {
                    new BatchHealingRequest("heuristic.claim", heuristicExpected),
                    new BatchHealingRequest("llm.claim", llmExpected),
                },
                Tree(live1, live2),
                providers,
                log: _ => { });

            var heuristicItem = batch.Items[0];
            var llmItem = batch.Items[1];

            Assert.True(heuristicItem.WasIndependentlyConfident, "heuristic item was independently confident");
            Assert.Equal(HealSource.Heuristic, heuristicItem.Result.Source);
            Assert.Same(live1, heuristicItem.Result.Matched);
            Assert.False(heuristicItem.Result.IsConfident);
            Assert.True(heuristicItem.Result.RejectedByReconciliation);
            Assert.Equal(HealResolutionStatus.OwnershipConflict, heuristicItem.Result.ResolutionStatus);
            Assert.Equal(BatchReconciliationDisposition.DeclinedAmbiguousContention, heuristicItem.ReconciliationDisposition);

            Assert.True(llmItem.WasIndependentlyConfident, "llm item was independently confident");
            Assert.Equal(HealSource.Llm, llmItem.Result.Source);
            Assert.Same(live1, llmItem.Result.Matched);
            Assert.False(llmItem.Result.IsConfident);
            Assert.True(llmItem.Result.RejectedByReconciliation);
            Assert.Equal(HealResolutionStatus.OwnershipConflict, llmItem.Result.ResolutionStatus);
            Assert.Equal(BatchReconciliationDisposition.DeclinedAmbiguousContention, llmItem.ReconciliationDisposition);

            Assert.Equal(1, batch.ContestedCandidateCount);
            Assert.Equal(2, batch.ReconciliationDeclineCount);
        }

        [Fact]
        public async Task ResolveBatchAsync_LlmConsensusContention_HigherQuorumWins()
        {
            var first = Element("Button", "Save", "a-live");
            var second = Element("Button", "Save", "b-live");

            var lowConfidence1 = Element("Button", "Save", "old-1");
            var lowConfidence2 = Element("Button", "Save", "old-2");

            var providerA = new SelectiveVoteProvider("Alpha", new Dictionary<string, string> { { "old-1", "c0" }, { "old-2", "c0" } });
            var providerB = new SelectiveVoteProvider("Beta", new Dictionary<string, string> { { "old-1", "c0" }, { "old-2", "c0" } });
            var providerC = new SelectiveVoteProvider("Gamma", new Dictionary<string, string> { { "old-1", "c0" } });

            var batch = await SelfHealingResolver.ResolveBatchAsync(
                new[]
                {
                    new BatchHealingRequest("three.votes", lowConfidence1),
                    new BatchHealingRequest("two.votes", lowConfidence2),
                },
                Tree(first, second),
                new ILlmHealingProvider[] { providerA, providerB, providerC },
                log: _ => { });

            Assert.Equal(1, batch.ContestedCandidateCount);
            Assert.Equal(1, batch.ReconciliationDeclineCount);

            Assert.True(batch.Items[0].Result.IsConfident);
            Assert.Equal(BatchReconciliationDisposition.WonContention, batch.Items[0].ReconciliationDisposition);
            Assert.Equal(3, batch.Items[0].Result.AgreedProviders.Count);

            Assert.False(batch.Items[1].Result.IsConfident);
            Assert.Equal(BatchReconciliationDisposition.DeclinedByStrongerClaim, batch.Items[1].ReconciliationDisposition);
            Assert.Equal(2, batch.Items[1].Result.AgreedProviders.Count);
        }

        [Fact]
        public void ResolveBatch_DuplicateLocatorKeysFailBeforeResolution()
        {
            var expected = Element("Button", "Save", "old-save");
            var requests = new[]
            {
                new BatchHealingRequest("duplicate", expected),
                new BatchHealingRequest("duplicate", UiElementSnapshot.Capture(expected)),
            };

            var exception = Assert.Throws<ArgumentException>(() =>
                SelfHealingResolver.ResolveBatch(requests, Tree(Element("Button", "Save", "live-save"))));

            Assert.Contains("unique", exception.Message);
        }

        [Fact]
        public void SingleLocatorResolve_RemainsUnreconciledAndBackwardCompatible()
        {
            var result = SelfHealingResolver.Resolve(
                Element("Button", "Save", "old-save"),
                Tree(Element("Button", "Save", "live-save")),
                log: _ => { });

            Assert.True(result.IsConfident);
            Assert.False(result.RejectedByReconciliation);
            Assert.Null(result.CandidateIdentity);
            Assert.Null(result.ReconciliationDisposition);
        }

        [Fact]
        public void ReconciliationDecline_ReportRetainsProposalAndOwnershipTelemetry()
        {
            var candidate = Element("Button", "Save", "");
            var previous = Element("Button", "Save", "old-save");
            var batch = SelfHealingResolver.ResolveBatch(
                new[]
                {
                    new BatchHealingRequest("save.one", previous),
                    new BatchHealingRequest("save.two", UiElementSnapshot.Capture(previous)),
                },
                Tree(candidate),
                log: _ => { });
            var declined = batch.Items[0];
            var outcome = HealingReportEntry.OutcomeFromResolutionStatus(declined.Result.ResolutionStatus);

            var entry = HealingReportEntry.FromResolutionAttempt(
                declined.Request.LocatorKey,
                declined.Request.Expected,
                declined.Result,
                outcome);

            Assert.Equal(HealingReportEntry.OwnershipConflictOutcome, entry.Outcome);
            Assert.Equal("r/0", entry.CandidateIdentity);
            Assert.Equal("DeclinedAmbiguousContention", entry.ReconciliationDisposition);
            Assert.NotNull(entry.ProposedSnapshot);
            Assert.Null(entry.AcceptedSnapshot);
            Assert.False(entry.IsAccepted);

            var html = HealingReportHtmlRenderer.Render(new HealingReportDocument
            {
                Events = new List<HealingReportEntry> { entry },
            });
            Assert.Contains("ownership-conflict", html);
            Assert.Contains("DeclinedAmbiguousContention", html);
            Assert.Contains("candidate r/0", html);
        }

        private static UiElementInfo Tree(params UiElementInfo[] children) => new UiElementInfo
        {
            ControlType = "Window",
            Name = "Root",
            AutomationId = "root",
            ParentControlType = "Desktop",
            SiblingIndex = 0,
            SiblingCount = 1,
            BoundingRectangle = new BoundingRectangle(0, 0, 1000, 800),
            Children = new List<UiElementInfo>(children),
        };

        private static UiElementInfo Element(string controlType, string name, string automationId) => new UiElementInfo
        {
            ControlType = controlType,
            Name = name,
            AutomationId = automationId,
            ParentControlType = "Window",
            ParentAutomationId = "root",
            SiblingIndex = 0,
            SiblingCount = 1,
            BoundingRectangle = new BoundingRectangle(100, 100, 100, 30),
        };

        private sealed class ThrowingProvider : ILlmHealingProvider
        {
            public string Name => "Broken";
            public bool IsAvailable => true;
            public int CallCount { get; private set; }

            public Task<LlmHealingResult> ResolveAsync(
                UiElementInfo expected,
                IReadOnlyList<CandidateScore> candidates,
                string? platform = null,
                CancellationToken cancellationToken = default)
            {
                CallCount++;
                throw new InvalidOperationException("provider failed");
            }
        }

        private sealed class FixedVoteProvider : ILlmHealingProvider
        {
            private readonly string _candidateId;
            private readonly double _confidence;

            public FixedVoteProvider(string name, string candidateId, double confidence)
            {
                Name = name;
                _candidateId = candidateId;
                _confidence = confidence;
            }

            public string Name { get; }
            public bool IsAvailable => true;

            public Task<LlmHealingResult> ResolveAsync(
                UiElementInfo expected,
                IReadOnlyList<CandidateScore> candidates,
                string? platform = null,
                CancellationToken cancellationToken = default)
            {
                return Task.FromResult(new LlmHealingResult
                {
                    ProviderName = Name,
                    Success = true,
                    MatchedCandidateId = _candidateId,
                    Confidence = _confidence,
                    Reasoning = "fixed vote",
                    AttemptCount = 1,
                });
            }
        }

        private sealed class SelectiveVoteProvider : ILlmHealingProvider
        {
            private readonly IReadOnlyDictionary<string, string> _votesByAutomationId;

            public SelectiveVoteProvider(string name, IReadOnlyDictionary<string, string> votesByAutomationId)
            {
                Name = name;
                _votesByAutomationId = votesByAutomationId;
            }

            public string Name { get; }
            public bool IsAvailable => true;

            public Task<LlmHealingResult> ResolveAsync(
                UiElementInfo expected,
                IReadOnlyList<CandidateScore> candidates,
                string? platform = null,
                CancellationToken cancellationToken = default)
            {
                if (expected.AutomationId != null && _votesByAutomationId.TryGetValue(expected.AutomationId, out var candidateId))
                {
                    return Task.FromResult(new LlmHealingResult
                    {
                        ProviderName = Name,
                        Success = true,
                        MatchedCandidateId = candidateId,
                        Confidence = 0.9,
                        Reasoning = "selective vote match",
                        AttemptCount = 1,
                    });
                }

                return Task.FromResult(new LlmHealingResult
                {
                    ProviderName = Name,
                    Success = false,
                    ErrorMessage = "No vote mapped",
                    AttemptCount = 1,
                });
            }
        }
    }
}
