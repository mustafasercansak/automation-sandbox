using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutomationSandbox.LlmHealing;
using AutomationSandbox.SelfHealing;
using AutomationSandbox.UiModel;
using Xunit;

namespace ScenarioRunner
{
    public class LiveConsensusGateTests
    {
        private const string GateEnvironmentVariable = "LIVE_CONSENSUS_GATE";
        private const string ScenarioName = "Desktop_AmbiguousSiblingTabs";

        // Mirrors SimilarityWeights' MinimumConsensusVotes default. The gate asserts the same
        // quorum rule the production resolver applies; it does not get a stricter one. A gate
        // that names specific providers (the pre-#388 Groq+Mistral pair) goes red whenever one
        // free tier runs out of quota, even while the rest of the pool agrees correctly - three
        // consecutive red nights in 2026-09 came from exactly that.
        private const int MinimumQuorumVotes = 2;

        [Fact]
        public void LiveConsensusGateFixture_ExistsAndHasGroundTruth()
        {
            var scenario = Assert.Single(EvaluationScenarios.All, s => s.Name == ScenarioName);
            Assert.False(string.IsNullOrWhiteSpace(scenario.GroundTruthAutomationId));
        }

        [SkippableFact]
        public async Task LiveConsensusGate_TwoOfConfiguredPoolAgreeOnCorrectCandidate()
        {
            if (!IsGateEnabled())
            {
                Console.WriteLine($"[LiveConsensusGate] {GateEnvironmentVariable}=1 is not set - skipping live consensus gate.");
                Skip.If(true, $"{GateEnvironmentVariable}=1 is not set.");
            }

            var configured = LlmProviderFactory.CreateConfiguredProviders();
            if (configured.Count == 0)
            {
                Console.WriteLine("[LiveConsensusGate] No provider credentials are configured - skipping live consensus gate.");
                AppendStepSummary("⏭️ Skipped", configured, null, "No provider credentials are configured.");
                Skip.If(true, "No provider credentials are configured.");
            }

            IReadOnlyList<ILlmHealingProvider> pool;
            try
            {
                pool = SelectGateProviders(configured);
            }
            catch (InvalidOperationException ex)
            {
                AppendStepSummary("❌ Failed", configured, null, ex.Message);
                throw;
            }

            var scenario = Assert.Single(EvaluationScenarios.All, s => s.Name == ScenarioName);

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
            var recorders = pool.Select(p => new RecordingProvider(p)).ToList();

            var result = await SelfHealingResolver.ResolveAsync(
                scenario.Expected,
                scenario.CurrentTreeRoot,
                recorders,
                platform: scenario.Platform,
                log: message => Console.WriteLine("[LiveConsensusGate] " + message));

            // A provider that fails (rate limit, timeout) or whose vote is discarded by the
            // hallucination guard contributes no usable vote; it does not fail the gate by
            // itself. Only what remains counts toward the quorum, exactly as the resolver
            // filters votes before counting them.
            var votes = recorders
                .Select(r => new ProviderVote(r.Name, UsableVote(r.LastResult, shortlistIds)))
                .ToList();
            var verdict = EvaluateQuorum(votes, expectedCandidate.CandidateId);

            var detail = BuildResultDetail(recorders, result, expectedCandidate.CandidateId, verdict.WinningCandidateId);
            AppendStepSummary(verdict.Passed ? "✅ Passed" : "❌ Failed", pool, verdict.WinningCandidateId, detail);
            Console.WriteLine("[LiveConsensusGate] " + detail);

            Assert.True(
                verdict.UsableVoteCount >= MinimumQuorumVotes,
                $"Fewer than {MinimumQuorumVotes} providers returned a usable shortlist-valid vote - quorum is not demonstrable. " + detail);
            Assert.True(verdict.Passed, "The pool must reach quorum on the ground-truth candidate. " + detail);
            Assert.Equal(HealSource.Llm, result.Source);
            Assert.True(
                result.AgreedProviders.Count >= MinimumQuorumVotes,
                $"Live consensus requires at least {MinimumQuorumVotes} agreeing providers. " + detail);
            Assert.Equal(scenario.GroundTruthAutomationId, result.Matched!.AutomationId);
        }

        [Fact]
        public void LiveConsensusGate_FewerThanTwoConfiguredProvidersFailsValidation()
        {
            var providers = new ILlmHealingProvider[] { new StubProvider("Groq") };

            var exception = Assert.Throws<InvalidOperationException>(() => SelectGateProviders(providers));

            Assert.Contains("Groq", exception.Message);
            Assert.Contains("at least", exception.Message);
        }

        [Fact]
        public void LiveConsensusGate_StepSummaryDescribesThePoolRule()
        {
            var providers = new ILlmHealingProvider[]
            {
                new StubProvider("Cloudflare"),
                new StubProvider("Groq"),
                new StubProvider("Mistral"),
            };

            var markdown = BuildStepSummary("✅ Passed", providers, "c0", "Two providers agreed on the ground-truth candidate.");

            Assert.Contains("at least 2", markdown);
            Assert.Contains("Cloudflare", markdown);
            Assert.Contains("Groq", markdown);
            Assert.Contains("Mistral", markdown);
            Assert.Contains("Desktop_AmbiguousSiblingTabs", markdown);
            Assert.Contains("`c0`", markdown);
            Assert.Contains("✅ Passed", markdown);
        }

        // --- Pure quorum logic. The live test above is one caller; these tests replay recorded
        // nightly vote sets so the rule itself is regression-tested without live HTTP. ---

        [Fact]
        public void EvaluateQuorum_TwoOfThreeAgreeOnExpected_Passes()
        {
            // Replays 2026-09-04 with Mistral forced to a 429: the pool still agrees, so a single
            // provider's rate limit must not fail the night. This is the #388 regression.
            var votes = new[]
            {
                new ProviderVote("Cloudflare", "c0"),
                new ProviderVote("Groq", "c0"),
                new ProviderVote("Mistral", null),
            };

            var verdict = EvaluateQuorum(votes, expectedCandidateId: "c0");

            Assert.True(verdict.Passed);
            Assert.Equal(2, verdict.UsableVoteCount);
            Assert.Equal("c0", verdict.WinningCandidateId);
            Assert.Equal(2, verdict.WinningVoteCount);
            Assert.False(verdict.TiedForLead);
        }

        [Fact]
        public void EvaluateQuorum_SingleUsableVote_FailsQuorumNotDemonstrable()
        {
            // Replays the 2026-09-05/06 gate inputs (Groq=c0, Mistral=429). One provider agreeing
            // with itself is not a quorum, so the gate must still fail - just no longer because a
            // *named* provider was missing.
            var votes = new[]
            {
                new ProviderVote("Groq", "c0"),
                new ProviderVote("Mistral", null),
            };

            var verdict = EvaluateQuorum(votes, expectedCandidateId: "c0");

            Assert.False(verdict.Passed);
            Assert.Equal(1, verdict.UsableVoteCount);
        }

        [Fact]
        public void EvaluateQuorum_ThreeWaySplit_Fails()
        {
            var votes = new[]
            {
                new ProviderVote("Cloudflare", "c0"),
                new ProviderVote("Groq", "c1"),
                new ProviderVote("Mistral", "c2"),
            };

            var verdict = EvaluateQuorum(votes, expectedCandidateId: "c0");

            Assert.False(verdict.Passed);
            Assert.Equal(3, verdict.UsableVoteCount);
        }

        [Fact]
        public void EvaluateQuorum_QuorumOnWrongCandidate_Fails()
        {
            var votes = new[]
            {
                new ProviderVote("Cloudflare", "c1"),
                new ProviderVote("Groq", "c0"),
                new ProviderVote("Mistral", "c1"),
            };

            var verdict = EvaluateQuorum(votes, expectedCandidateId: "c0");

            Assert.False(verdict.Passed);
            Assert.Equal("c1", verdict.WinningCandidateId);
            Assert.Equal(2, verdict.WinningVoteCount);
        }

        [Fact]
        public void EvaluateQuorum_TieForLead_Fails()
        {
            // Mirrors the resolver's "tie for the lead means no consensus": two candidates with
            // two votes each must not pass, even when one of them is the ground truth.
            var votes = new[]
            {
                new ProviderVote("Cloudflare", "c0"),
                new ProviderVote("Gemini", "c1"),
                new ProviderVote("Groq", "c0"),
                new ProviderVote("Mistral", "c1"),
            };

            var verdict = EvaluateQuorum(votes, expectedCandidateId: "c0");

            Assert.False(verdict.Passed);
            Assert.True(verdict.TiedForLead);
        }

        [Fact]
        public void EvaluateQuorum_QuorumWithDissenter_Passes()
        {
            // A lone wrong vote does not block a correct quorum; the resolver accepts 2-vs-1.
            var votes = new[]
            {
                new ProviderVote("Cloudflare", "c0"),
                new ProviderVote("Groq", "c0"),
                new ProviderVote("Mistral", "c1"),
            };

            var verdict = EvaluateQuorum(votes, expectedCandidateId: "c0");

            Assert.True(verdict.Passed);
            Assert.Equal(2, verdict.WinningVoteCount);
        }

        private static bool IsGateEnabled()
        {
            var value = Environment.GetEnvironmentVariable(GateEnvironmentVariable);
            return value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static IReadOnlyList<ILlmHealingProvider> SelectGateProviders(IReadOnlyList<ILlmHealingProvider> configured)
        {
            if (configured.Count < MinimumQuorumVotes)
            {
                var configuredNames = configured.Count == 0
                    ? "none"
                    : string.Join(", ", configured.Select(p => p.Name).OrderBy(name => name, StringComparer.Ordinal));
                throw new InvalidOperationException(
                    $"Live consensus gate requires at least {MinimumQuorumVotes} configured providers to demonstrate quorum; configured providers: {configuredNames}.");
            }

            return configured;
        }

        private static string? UsableVote(LlmHealingResult? result, HashSet<string> shortlistIds)
        {
            if (result == null || !result.Success || string.IsNullOrEmpty(result.MatchedCandidateId))
            {
                return null;
            }

            // The hallucination guard discards off-shortlist votes before counting; mirror that
            // here so a hallucinating provider costs itself a vote and nothing more.
            return shortlistIds.Contains(result.MatchedCandidateId) ? result.MatchedCandidateId : null;
        }

        internal static QuorumVerdict EvaluateQuorum(IReadOnlyList<ProviderVote> votes, string expectedCandidateId)
        {
            var usable = votes.Where(v => v.CandidateId != null).ToList();
            var groups = usable
                .GroupBy(v => v.CandidateId!, StringComparer.Ordinal)
                .Select(g => new { CandidateId = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .ToList();

            var leader = groups.Count > 0 ? groups[0] : null;
            var tiedForLead = groups.Count > 1 && groups[1].Count == leader!.Count;
            var passed = leader != null &&
                !tiedForLead &&
                leader.Count >= MinimumQuorumVotes &&
                string.Equals(leader.CandidateId, expectedCandidateId, StringComparison.Ordinal);

            return new QuorumVerdict(
                usable.Count,
                leader?.CandidateId,
                leader?.Count ?? 0,
                tiedForLead,
                passed);
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
                "## Live Multi-Provider Consensus Gate" + Environment.NewLine + Environment.NewLine +
                $"- **Status:** {status}" + Environment.NewLine +
                $"- **Quorum rule:** at least {MinimumQuorumVotes} configured providers must cast shortlist-valid votes agreeing on the ground-truth candidate" + Environment.NewLine +
                $"- **Configured pool:** {providerNames}" + Environment.NewLine +
                $"- **Scenario:** `{ScenarioName}`" + Environment.NewLine +
                $"- **Consensus candidate:** `{candidateId ?? "none"}`" + Environment.NewLine +
                $"- **Detail:** {detail}" + Environment.NewLine;
        }

        internal sealed class ProviderVote
        {
            public ProviderVote(string providerName, string? candidateId)
            {
                ProviderName = providerName;
                CandidateId = candidateId;
            }

            public string ProviderName { get; }

            /// <summary>The shortlist candidate this provider named, or null when the provider
            /// failed, timed out, or had its vote discarded by the hallucination guard.</summary>
            public string? CandidateId { get; }
        }

        internal sealed class QuorumVerdict
        {
            public QuorumVerdict(int usableVoteCount, string? winningCandidateId, int winningVoteCount, bool tiedForLead, bool passed)
            {
                UsableVoteCount = usableVoteCount;
                WinningCandidateId = winningCandidateId;
                WinningVoteCount = winningVoteCount;
                TiedForLead = tiedForLead;
                Passed = passed;
            }

            public int UsableVoteCount { get; }
            public string? WinningCandidateId { get; }
            public int WinningVoteCount { get; }
            public bool TiedForLead { get; }
            public bool Passed { get; }
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
