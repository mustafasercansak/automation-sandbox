using System;
using System.Collections.Generic;
using System.Linq;
using SelfHealing;

namespace ScenarioRunner
{
    public enum JointAssignmentDisposition
    {
        BaselineDecline,
        PreservedUncontested,
        WonContention,
        DeclinedByStrongerClaim,
        DeclinedAmbiguousContention,
    }

    public sealed class JointAssignmentLocatorResult
    {
        public AblationScenarioResult Baseline { get; set; } = new AblationScenarioResult();
        public AblationScenarioResult Joint { get; set; } = new AblationScenarioResult();
        public JointAssignmentDisposition Disposition { get; set; }
        public double AssignmentUtility { get; set; }
    }

    public sealed class JointAssignmentScenarioResult
    {
        public string ScenarioId { get; set; } = "";
        public List<JointAssignmentLocatorResult> LocatorResults { get; set; } = new();
        public int InputSharedCandidateClaims { get; set; }
        public int UnresolvedSharedCandidateClaims { get; set; }
    }

    public sealed class JointAssignmentReport
    {
        public List<JointAssignmentScenarioResult> Scenarios { get; set; } = new();
        public AblationMetrics BaselineMetrics { get; set; } = new AblationMetrics();
        public AblationMetrics JointMetrics { get; set; } = new AblationMetrics();
        public int InputScenariosWithContention => Scenarios.Count(s => s.InputSharedCandidateClaims > 0);
        public int UnresolvedSharedCandidateCollisions => Scenarios.Sum(s => s.UnresolvedSharedCandidateClaims);
        public double RemovedLocatorCorrectDeclineRate => JointMetrics.RemovalScenarios == 0
            ? 0.0
            : (double)JointMetrics.CorrectDeclines / JointMetrics.RemovalScenarios;
    }

    // Offline-only experiment for #141. It reconciles the current resolver's independently accepted
    // top claims; it is deliberately not a production batch resolver and does not promote runner-up
    // candidates. That isolates the value of one-to-one ownership without adding a new false-match path.
    public static class JointLocatorAssignmentEvaluator
    {
        public static JointAssignmentReport Evaluate(
            MultiLocatorBaselineReport baseline,
            SimilarityWeights? weights = null)
        {
            if (baseline == null)
            {
                throw new ArgumentNullException(nameof(baseline));
            }

            var w = weights ?? SimilarityWeights.Default;
            w.Validate();

            var report = new JointAssignmentReport
            {
                BaselineMetrics = LocatorAblationHarness.Summarize(
                    baseline.Scenarios.SelectMany(s => s.LocatorResults)),
            };

            foreach (var baselineScenario in baseline.Scenarios)
            {
                var scenario = new JointAssignmentScenarioResult
                {
                    ScenarioId = baselineScenario.ScenarioId,
                    InputSharedCandidateClaims = baselineScenario.SharedCandidateClaims,
                    LocatorResults = baselineScenario.LocatorResults
                        .Select(r => new JointAssignmentLocatorResult
                        {
                            Baseline = Clone(r),
                            Joint = Clone(r),
                            Disposition = r.EngineAccepted
                                ? JointAssignmentDisposition.PreservedUncontested
                                : JointAssignmentDisposition.BaselineDecline,
                            // assign utility = score - MinimumConfidence; unmatched utility = 0.
                            // Existing resolver gates already excluded ineligible claims.
                            AssignmentUtility = r.EngineAccepted ? r.Score - w.MinimumConfidence : 0.0,
                        })
                        .ToList(),
                };

                var competingClaims = scenario.LocatorResults
                    .Where(r => r.Joint.EngineAccepted && !string.IsNullOrEmpty(r.Joint.MatchedAutomationId))
                    .GroupBy(r => r.Joint.MatchedAutomationId!, StringComparer.Ordinal)
                    .Where(g => g.Count() > 1)
                    .ToList();

                foreach (var claimGroup in competingClaims)
                {
                    // Heuristic Score and LLM-consensus AgreedProviders vote counts are incommensurable
                    // scales (#268) - mirror the production BatchHealing.Reconcile split: rank same-source
                    // contentions on their own scale, and decline mixed-source contentions outright rather
                    // than comparing a heuristic AssignmentUtility against an LLM claim's unrelated score.
                    var allHeuristic = claimGroup.All(r => r.Joint.Source == HealSource.Heuristic);
                    var allLlm = claimGroup.All(r => r.Joint.Source == HealSource.Llm);

                    if (allHeuristic)
                    {
                        var ranked = claimGroup
                            .OrderByDescending(r => r.AssignmentUtility)
                            .ThenBy(r => r.Joint.OriginalAutomationId, StringComparer.Ordinal)
                            .ToList();
                        var claimantMargin = ranked[0].AssignmentUtility - ranked[1].AssignmentUtility;

                        if (claimantMargin >= w.MinimumCandidateMargin)
                        {
                            ranked[0].Disposition = JointAssignmentDisposition.WonContention;
                            foreach (var loser in ranked.Skip(1))
                            {
                                Decline(loser, JointAssignmentDisposition.DeclinedByStrongerClaim);
                            }
                        }
                        else
                        {
                            // The ordinal ordering above exists only to keep reports deterministic. It
                            // must never decide a near-tie: every claimant is declined when evidence does
                            // not separate their ownership by the same margin the resolver already uses.
                            foreach (var claimant in ranked)
                            {
                                Decline(claimant, JointAssignmentDisposition.DeclinedAmbiguousContention);
                            }
                        }
                    }
                    else if (allLlm)
                    {
                        var ranked = claimGroup
                            .OrderByDescending(r => r.Joint.AgreedProviders.Count)
                            .ThenBy(r => r.Joint.OriginalAutomationId, StringComparer.Ordinal)
                            .ToList();
                        var voteMargin = ranked[0].Joint.AgreedProviders.Count - ranked[1].Joint.AgreedProviders.Count;

                        if (voteMargin >= 1)
                        {
                            ranked[0].Disposition = JointAssignmentDisposition.WonContention;
                            foreach (var loser in ranked.Skip(1))
                            {
                                Decline(loser, JointAssignmentDisposition.DeclinedByStrongerClaim);
                            }
                        }
                        else
                        {
                            foreach (var claimant in ranked)
                            {
                                Decline(claimant, JointAssignmentDisposition.DeclinedAmbiguousContention);
                            }
                        }
                    }
                    else
                    {
                        foreach (var claimant in claimGroup)
                        {
                            Decline(claimant, JointAssignmentDisposition.DeclinedAmbiguousContention);
                        }
                    }
                }

                scenario.UnresolvedSharedCandidateClaims = scenario.LocatorResults
                    .Where(r => r.Joint.EngineAccepted && !string.IsNullOrEmpty(r.Joint.MatchedAutomationId))
                    .GroupBy(r => r.Joint.MatchedAutomationId!, StringComparer.Ordinal)
                    .Count(g => g.Count() > 1);
                report.Scenarios.Add(scenario);
            }

            report.JointMetrics = LocatorAblationHarness.Summarize(
                report.Scenarios.SelectMany(s => s.LocatorResults).Select(r => r.Joint));
            return report;
        }

        private static void Decline(
            JointAssignmentLocatorResult locator,
            JointAssignmentDisposition disposition)
        {
            locator.Joint.EngineAccepted = false;
            locator.Joint.MatchedElement = null;
            locator.Joint.MatchedAutomationId = null;
            locator.Joint.ResolutionStatus = HealResolutionStatus.Ambiguous;
            locator.Joint.Outcome = locator.Joint.MutationKind == LocatorMutationKind.RemovedElement
                ? AblationOutcome.CorrectDecline
                : AblationOutcome.MissedHeal;
            locator.Disposition = disposition;
            locator.AssignmentUtility = 0.0;
        }

        private static AblationScenarioResult Clone(AblationScenarioResult source) =>
            new AblationScenarioResult
            {
                ScenarioId = source.ScenarioId,
                MutationKind = source.MutationKind,
                Outcome = source.Outcome,
                OriginalAutomationId = source.OriginalAutomationId,
                EngineAccepted = source.EngineAccepted,
                Score = source.Score,
                EvidenceCoverage = source.EvidenceCoverage,
                CandidateCount = source.CandidateCount,
                ResolutionStatus = source.ResolutionStatus,
                Source = source.Source,
                AgreedProviders = source.AgreedProviders,
                ProviderAttempts = source.ProviderAttempts,
                ProviderErrors = source.ProviderErrors,
                ProviderVotes = source.ProviderVotes,
                ProviderResults = source.ProviderResults,
                RespondingProviders = source.RespondingProviders,
                LlmConfidence = source.LlmConfidence,
                LlmReasoning = source.LlmReasoning,
                Candidates = source.Candidates,
                VotePattern = source.VotePattern,
                ExpectedElement = source.ExpectedElement,
                MatchedElement = source.MatchedElement,
                MatchedAutomationId = source.MatchedAutomationId,
            };
    }
}
