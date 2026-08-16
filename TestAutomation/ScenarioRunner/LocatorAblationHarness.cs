using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LlmHealing;
using SelfHealing;
using UiModel;

namespace ScenarioRunner
{
    // What the engine did with a scenario whose correct answer is known.
    public enum AblationOutcome
    {
        // Rename scenarios: the engine accepted a match and it is the right element.
        CorrectHeal,

        // Rename scenarios: the engine accepted a match and it is the wrong element. The dangerous one —
        // the test goes green against something nobody intended.
        FalseHeal,

        // Rename scenarios: the engine declined, so a human has to look. Safe but not free.
        MissedHeal,

        // Removal scenarios: the engine declined, which is correct because there is no successor.
        CorrectDecline,

        // Removal scenarios: the engine accepted a neighbour for an element that no longer exists.
        FalseHealOnRemoved,
    }

    // How the independent providers behaved on one scenario. This — not the heal outcome — is what
    // #97 asks about: when the element is gone there is no correct answer, so the hypothesis is that
    // providers scatter. Agreement on a decoy would be the opposite, and worse than a heuristic miss,
    // because consensus is what the engine trusts most.
    public enum ConsensusVotePattern
    {
        // The LLM path never ran; the heuristic answered on its own.
        NotEvaluated,

        // Every provider that answered named the same candidate.
        Unanimous,

        // Providers answered but named different candidates.
        Scattered,

        // Every provider declined to name a candidate.
        AllDeclined,

        // Some answered, some declined. Not consensus, but not scatter either.
        Partial,

        // At least one provider failed to produce a usable answer. Measurement noise, not a finding.
        ProviderFailure,
    }

    public sealed class AblationScenarioResult
    {
        public string ScenarioId { get; set; } = "";
        public LocatorMutationKind MutationKind { get; set; }
        public AblationOutcome Outcome { get; set; }
        public string OriginalAutomationId { get; set; } = "";

        public bool EngineAccepted { get; set; }
        public double Score { get; set; }
        public double EvidenceCoverage { get; set; }
        public int CandidateCount { get; set; }
        public HealResolutionStatus ResolutionStatus { get; set; }
        public HealSource Source { get; set; } = HealSource.Heuristic;
        public IReadOnlyList<string> AgreedProviders { get; set; } = Array.Empty<string>();
        public IReadOnlyDictionary<string, int> ProviderAttempts { get; set; } = new Dictionary<string, int>();
        public IReadOnlyDictionary<string, string> ProviderErrors { get; set; } = new Dictionary<string, string>();
        public IReadOnlyDictionary<string, string?> ProviderVotes { get; set; } = new Dictionary<string, string?>();
        public IReadOnlyDictionary<string, LlmHealingResult> ProviderResults { get; set; } = new Dictionary<string, LlmHealingResult>();
        public double? LlmConfidence { get; set; }
        public string? LlmReasoning { get; set; }

        // The raw score vector, kept so thresholds can be swept offline without re-running the harness
        // (#15 asks for this directly).
        public IReadOnlyList<CandidateScore> Candidates { get; set; } = new List<CandidateScore>();

        public ConsensusVotePattern VotePattern { get; set; } = ConsensusVotePattern.NotEvaluated;

        public string? ExpectedElement { get; set; }
        public string? MatchedElement { get; set; }
    }

    public sealed class AblationMetrics
    {
        public int SuccessorScenarios { get; set; }
        public int RenameScenarios { get; set; }
        public int NameDriftScenarios { get; set; }
        public int PositionShiftScenarios { get; set; }
        public int CompoundDriftScenarios { get; set; }

        public int CorrectHeals { get; set; }
        public int FalseHeals { get; set; }
        public int MissedHeals { get; set; }

        public int RemovalScenarios { get; set; }
        public int CorrectDeclines { get; set; }
        public int FalseHealsOnRemoved { get; set; }

        public int LlmHeals { get; set; }
        public int ConsensusDeclines { get; set; }
        public int ProviderErrors { get; set; }

        // Of the locators that survived (with rename, drift, or shift), how many were found.
        public double AutoHealRecall => SuccessorScenarios == 0 ? 0.0 : (double)CorrectHeals / SuccessorScenarios;

        // Of everything the engine accepted, how much was the true element.
        public double Precision
        {
            get
            {
                var accepted = CorrectHeals + FalseHeals + FalseHealsOnRemoved;
                return accepted == 0 ? 1.0 : (double)CorrectHeals / accepted;
            }
        }

        // Of everything the engine accepted, how much was wrong (misdirected or accepted on deleted element).
        public double FalseHealRate
        {
            get
            {
                var accepted = CorrectHeals + FalseHeals + FalseHealsOnRemoved;
                return accepted == 0 ? 0.0 : (double)(FalseHeals + FalseHealsOnRemoved) / accepted;
            }
        }

        // How often a human is asked to decide (declined because of low confidence, low evidence, or ambiguity).
        public double ManualReviewRate
        {
            get
            {
                var total = SuccessorScenarios + RemovalScenarios;
                return total == 0 ? 0.0 : (double)(MissedHeals + CorrectDeclines) / total;
            }
        }
    }

    public sealed class AblationRunReport
    {
        public List<AblationScenarioResult> Results { get; set; } = new();
        public AblationMetrics Metrics { get; set; } = new();
    }

    public static class LocatorAblationHarness
    {
        public static AblationRunReport Run(
            LocatorAblationDataset dataset,
            UiElementInfo sourceRoot,
            SimilarityWeights? weights = null)
        {
            if (dataset == null)
            {
                throw new ArgumentNullException(nameof(dataset));
            }

            var report = new AblationRunReport();

            foreach (var scenario in dataset.Scenarios)
            {
                var expected = LocatorAblationGenerator.FindExpectedElement(sourceRoot, scenario.OriginalAutomationId);
                if (expected == null)
                {
                    throw new InvalidOperationException(
                        $"Scenario '{scenario.ScenarioId}' references AutomationId '{scenario.OriginalAutomationId}', absent from the source tree.");
                }

                var mutatedRoot = LocatorAblationGenerator.ApplyMutation(sourceRoot, scenario);
                var heal = SelfHealingResolver.Resolve(expected, mutatedRoot, weights, log: _ => { });

                var accepted = heal.IsConfident && heal.Matched != null;
                var matchedPath = heal.Matched == null
                    ? null
                    : LocatorAblationGenerator.AncestorPathOf(mutatedRoot, heal.Matched);

                var result = new AblationScenarioResult
                {
                    ScenarioId = scenario.ScenarioId,
                    MutationKind = scenario.MutationKind,
                    OriginalAutomationId = scenario.OriginalAutomationId,
                    EngineAccepted = accepted,
                    Score = heal.Score,
                    EvidenceCoverage = heal.EvidenceCoverage,
                    CandidateCount = heal.CandidateCount,
                    ResolutionStatus = heal.ResolutionStatus,
                    Source = heal.Source,
                    AgreedProviders = heal.AgreedProviders ?? Array.Empty<string>(),
                    ProviderAttempts = heal.ProviderAttempts ?? new Dictionary<string, int>(),
                    ProviderErrors = heal.ProviderErrors ?? new Dictionary<string, string>(),
                    LlmConfidence = heal.LlmConfidence,
                    LlmReasoning = heal.LlmReasoning,
                    Candidates = heal.Candidates ?? new List<CandidateScore>(),
                    ExpectedElement = scenario.GroundTruth?.ToString(),
                    MatchedElement = heal.Matched == null
                        ? null
                        : LocatorAblationGenerator.Fingerprint(heal.Matched, matchedPath ?? "").ToString(),
                    Outcome = Classify(scenario, heal, accepted, matchedPath),
                };

                report.Results.Add(result);
            }

            report.Metrics = Summarize(report.Results);
            return report;
        }

        public static async Task<AblationRunReport> RunAsync(
            LocatorAblationDataset dataset,
            UiElementInfo sourceRoot,
            IEnumerable<ILlmHealingProvider>? llmProviders = null,
            SimilarityWeights? weights = null,
            string? platform = null,
            Func<LocatorAblationScenario, bool>? scenarioFilter = null,
            CancellationToken cancellationToken = default)
        {
            if (dataset == null)
            {
                throw new ArgumentNullException(nameof(dataset));
            }

            var report = new AblationRunReport();
            var providersList = llmProviders?.ToList();

            foreach (var scenario in dataset.Scenarios)
            {
                if (scenarioFilter != null && !scenarioFilter(scenario))
                {
                    continue;
                }

                var expected = LocatorAblationGenerator.FindExpectedElement(sourceRoot, scenario.OriginalAutomationId);
                if (expected == null)
                {
                    throw new InvalidOperationException(
                        $"Scenario '{scenario.ScenarioId}' references AutomationId '{scenario.OriginalAutomationId}', absent from the source tree.");
                }

                var mutatedRoot = LocatorAblationGenerator.ApplyMutation(sourceRoot, scenario);
                var recorders = providersList?.Select(p => new RecordingProvider(p)).ToList();
                var heal = await SelfHealingResolver.ResolveAsync(
                    expected,
                    mutatedRoot,
                    recorders,
                    weights,
                    log: _ => { },
                    platform: platform ?? "windows-desktop",
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                var accepted = heal.IsConfident && heal.Matched != null;
                var matchedPath = heal.Matched == null
                    ? null
                    : LocatorAblationGenerator.AncestorPathOf(mutatedRoot, heal.Matched);

                var providerVotes = new SortedDictionary<string, string?>(StringComparer.Ordinal);
                var providerResults = new SortedDictionary<string, LlmHealingResult>(StringComparer.Ordinal);
                if (recorders != null)
                {
                    foreach (var rec in recorders)
                    {
                        if (rec.LastResult != null)
                        {
                            providerVotes[rec.Name] = rec.LastResult.Success ? rec.LastResult.MatchedCandidateId : null;
                            providerResults[rec.Name] = rec.LastResult;
                        }
                    }
                }

                var result = new AblationScenarioResult
                {
                    ScenarioId = scenario.ScenarioId,
                    MutationKind = scenario.MutationKind,
                    OriginalAutomationId = scenario.OriginalAutomationId,
                    EngineAccepted = accepted,
                    Score = heal.Score,
                    EvidenceCoverage = heal.EvidenceCoverage,
                    CandidateCount = heal.CandidateCount,
                    ResolutionStatus = heal.ResolutionStatus,
                    Source = heal.Source,
                    AgreedProviders = heal.AgreedProviders ?? Array.Empty<string>(),
                    ProviderAttempts = heal.ProviderAttempts ?? new Dictionary<string, int>(),
                    ProviderErrors = heal.ProviderErrors ?? new Dictionary<string, string>(),
                    ProviderVotes = providerVotes,
                    ProviderResults = providerResults,
                    VotePattern = ClassifyVotePattern(providerVotes, providerResults),
                    LlmConfidence = heal.LlmConfidence,
                    LlmReasoning = heal.LlmReasoning,
                    Candidates = heal.Candidates ?? new List<CandidateScore>(),
                    ExpectedElement = scenario.GroundTruth?.ToString(),
                    MatchedElement = heal.Matched == null
                        ? null
                        : LocatorAblationGenerator.Fingerprint(heal.Matched, matchedPath ?? "").ToString(),
                    Outcome = Classify(scenario, heal, accepted, matchedPath),
                };

                report.Results.Add(result);
            }

            report.Metrics = Summarize(report.Results);
            return report;
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

        private static AblationOutcome Classify(
            LocatorAblationScenario scenario,
            HealResult heal,
            bool accepted,
            string? matchedPath)
        {
            if (scenario.ExpectedOutcome == LocatorExpectedOutcome.NoSuccessor)
            {
                return accepted ? AblationOutcome.FalseHealOnRemoved : AblationOutcome.CorrectDecline;
            }

            if (!accepted)
            {
                return AblationOutcome.MissedHeal;
            }

            var isGroundTruth = heal.Matched != null &&
                (string.Equals(heal.Matched.AutomationId, scenario.MutatedAutomationId, StringComparison.Ordinal) ||
                 (scenario.GroundTruth != null && scenario.GroundTruth.Matches(heal.Matched, matchedPath ?? "")));

            return isGroundTruth ? AblationOutcome.CorrectHeal : AblationOutcome.FalseHeal;
        }


        // Reads the raw votes rather than the consensus verdict. HealResult only reports whether two
        // providers agreed; it cannot say whether the others scattered, declined, or failed - and that
        // distinction is the entire question in #97.
        private static ConsensusVotePattern ClassifyVotePattern(
            IReadOnlyDictionary<string, string?> votes,
            IReadOnlyDictionary<string, LlmHealingResult> results)
        {
            if (votes.Count == 0)
            {
                return ConsensusVotePattern.NotEvaluated;
            }

            foreach (var entry in results)
            {
                if (entry.Value == null || !entry.Value.Success)
                {
                    return ConsensusVotePattern.ProviderFailure;
                }
            }

            var answered = new List<string>();
            var declined = 0;
            foreach (var entry in votes)
            {
                if (string.IsNullOrEmpty(entry.Value))
                {
                    declined++;
                }
                else
                {
                    answered.Add(entry.Value!);
                }
            }

            if (answered.Count == 0)
            {
                return ConsensusVotePattern.AllDeclined;
            }

            var distinct = new HashSet<string>(answered, StringComparer.Ordinal).Count;

            if (declined > 0)
            {
                return ConsensusVotePattern.Partial;
            }

            return distinct == 1 ? ConsensusVotePattern.Unanimous : ConsensusVotePattern.Scattered;
        }

        public static AblationMetrics Summarize(IEnumerable<AblationScenarioResult> results)
        {
            var all = results.ToList();
            var successors = all.Where(r => r.MutationKind != LocatorMutationKind.RemovedElement).ToList();
            return new AblationMetrics
            {
                SuccessorScenarios = successors.Count,
                RenameScenarios = all.Count(r => r.MutationKind == LocatorMutationKind.RenamedAutomationId),
                NameDriftScenarios = all.Count(r => r.MutationKind == LocatorMutationKind.NameDrift),
                PositionShiftScenarios = all.Count(r => r.MutationKind == LocatorMutationKind.PositionShift),
                CompoundDriftScenarios = all.Count(r => r.MutationKind == LocatorMutationKind.CompoundDrift),
                CorrectHeals = all.Count(r => r.Outcome == AblationOutcome.CorrectHeal),
                FalseHeals = all.Count(r => r.Outcome == AblationOutcome.FalseHeal),
                MissedHeals = all.Count(r => r.Outcome == AblationOutcome.MissedHeal),
                RemovalScenarios = all.Count(r => r.MutationKind == LocatorMutationKind.RemovedElement),
                CorrectDeclines = all.Count(r => r.Outcome == AblationOutcome.CorrectDecline),
                FalseHealsOnRemoved = all.Count(r => r.Outcome == AblationOutcome.FalseHealOnRemoved),
                LlmHeals = all.Count(r => r.Source == HealSource.Llm && r.EngineAccepted),
                ConsensusDeclines = all.Count(r => r.ResolutionStatus == HealResolutionStatus.NoConsensus),
                ProviderErrors = all.Count(r => r.ResolutionStatus == HealResolutionStatus.ProviderError),
            };
        }

        public static string ToMarkdownSummary(AblationRunReport report, string datasetName)
        {
            var m = report.Metrics;
            var lines = new List<string>
            {
                $"### Locator ablation benchmark — {datasetName}",
                "",
                $"- Scenarios: **{report.Results.Count}** ({m.SuccessorScenarios} successor [{m.RenameScenarios} rename, {m.NameDriftScenarios} text drift, {m.PositionShiftScenarios} pos shift, {m.CompoundDriftScenarios} compound], {m.RemovalScenarios} removal)",
                "",
                "| Metric | Value | Reading |",
                "| :--- | ---: | :--- |",
                $"| Precision | {ReportFormatting.Percent(m.Precision)} | of everything accepted, how much was correct |",
                $"| Auto-heal recall | {ReportFormatting.Percent(m.AutoHealRecall)} | surviving locators found again |",
                $"| False-heal rate | {ReportFormatting.Percent(m.FalseHealRate)} | of everything accepted, how much was wrong |",
                $"| Manual-review rate | {ReportFormatting.Percent(m.ManualReviewRate)} | how often a human is asked |",
                "",
                "| Outcome | Count |",
                "| :--- | ---: |",
                $"| Correct heal | {m.CorrectHeals} |",
                $"| False heal (wrong element) | {m.FalseHeals} |",
                $"| Missed heal (declined, successor existed) | {m.MissedHeals} |",
                $"| Correct decline (no successor) | {m.CorrectDeclines} |",
                $"| False heal on removed element | {m.FalseHealsOnRemoved} |",
            };

            if (m.LlmHeals > 0 || m.ConsensusDeclines > 0 || m.ProviderErrors > 0)
            {
                lines.Add("");
                lines.Add("| LLM Consensus Telemetry | Count |");
                lines.Add("| :--- | ---: |");
                lines.Add($"| LLM-accepted heals | {m.LlmHeals} |");
                lines.Add($"| Disagreement / No-consensus declines | {m.ConsensusDeclines} |");
                lines.Add($"| Provider errors | {m.ProviderErrors} |");
            }

            var evaluated = report.Results.Where(r => r.VotePattern != ConsensusVotePattern.NotEvaluated).ToList();
            if (evaluated.Count > 0)
            {
                lines.Add("");
                lines.Add("#### Provider vote patterns");
                lines.Add("");
                lines.Add("Read from the raw per-provider votes, not from the consensus verdict. On removed elements");
                lines.Add("there is no correct answer, so scattering is the hypothesis and unanimity on a decoy is the");
                lines.Add("failure mode worth knowing about.");
                lines.Add("");
                lines.Add("| Mutation | Unanimous | Scattered | All declined | Partial | Provider failure |");
                lines.Add("| :--- | ---: | ---: | ---: | ---: | ---: |");

                foreach (var group in evaluated.GroupBy(r => r.MutationKind).OrderBy(g => g.Key.ToString(), StringComparer.Ordinal))
                {
                    lines.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "| `{0}` | {1} | {2} | {3} | {4} | {5} |",
                        group.Key,
                        group.Count(r => r.VotePattern == ConsensusVotePattern.Unanimous),
                        group.Count(r => r.VotePattern == ConsensusVotePattern.Scattered),
                        group.Count(r => r.VotePattern == ConsensusVotePattern.AllDeclined),
                        group.Count(r => r.VotePattern == ConsensusVotePattern.Partial),
                        group.Count(r => r.VotePattern == ConsensusVotePattern.ProviderFailure)));
                }

                var removed = evaluated.Where(r => r.MutationKind == LocatorMutationKind.RemovedElement).ToList();
                if (removed.Count > 0)
                {
                    var unanimousOnDecoy = removed.Count(r => r.VotePattern == ConsensusVotePattern.Unanimous);
                    lines.Add("");
                    lines.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "**Answer to the #97 question:** on {0} removed elements the providers agreed unanimously {1} time(s) — every one of those is agreement on an element that does not exist.",
                        removed.Count,
                        unanimousOnDecoy));
                }
            }

            return string.Join(Environment.NewLine, lines);
        }
    }
}
