using System;
using System.Collections.Generic;
using System.Linq;
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

        // The raw score vector, kept so thresholds can be swept offline without re-running the harness
        // (#15 asks for this directly).
        public IReadOnlyList<CandidateScore> Candidates { get; set; } = new List<CandidateScore>();

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

            return string.Join(Environment.NewLine, lines);
        }
    }
}
