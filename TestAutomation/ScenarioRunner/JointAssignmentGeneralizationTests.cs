using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SelfHealing;
using UiModel;
using Xunit;

namespace ScenarioRunner
{
    public class JointAssignmentGeneralizationTests
    {
        [Theory]
        [InlineData(false, null, "<declined>")]
        [InlineData(true, "", "<empty AutomationId>")]
        [InlineData(true, "txtEmail", "txtEmail")]
        public void DisplayCandidate_FormatsDecisionAndAutomationId(
            bool engineAccepted,
            string? matchedAutomationId,
            string expected)
        {
            var result = new AblationScenarioResult
            {
                EngineAccepted = engineAccepted,
                MatchedAutomationId = matchedAutomationId,
            };

            Assert.Equal(expected, DisplayCandidate(result));
        }

        [Fact]
        public void ProductionBatchResolver_FrozenCrossApplicationDatasetMatchesOfflineDesign()
        {
            var fixtures = new[]
            {
                new { Application = "HandBrake", Version = "1.8.2", FileName = "HandBrake_1.8.2.tree.json" },
                new { Application = "ShareX", Version = "v21.0.0", FileName = "ShareX_v21.0.0.tree.json" },
            };
            var comparedLocators = 0;

            foreach (var fixture in fixtures)
            {
                var sourceRoot = LoadFixture(fixture.FileName);
                var dataset = JointAssignmentGeneralizationDataset.Generate(
                    sourceRoot,
                    fixture.Application,
                    fixture.Version,
                    fixture.FileName);
                var baseline = LocatorAblationHarness.RunMultiLocatorBaseline(dataset, sourceRoot);
                var design = JointLocatorAssignmentEvaluator.Evaluate(baseline);

                foreach (var scenario in dataset.Scenarios.Where(s => s.MutationKind == LocatorMutationKind.MultiLocator))
                {
                    var mutatedRoot = LocatorAblationGenerator.ApplyMutation(sourceRoot, scenario);
                    var requests = scenario.Mutations!
                        .Select(mutation => new BatchHealingRequest(
                            mutation.OriginalAutomationId,
                            LocatorAblationGenerator.FindExpectedElement(sourceRoot, mutation.OriginalAutomationId)!))
                        .ToList();
                    var production = SelfHealingResolver.ResolveBatch(requests, mutatedRoot, log: _ => { });
                    var expectedScenario = Assert.Single(design.Scenarios, s => s.ScenarioId == scenario.ScenarioId);

                    Assert.Equal(expectedScenario.LocatorResults.Count, production.Items.Count);
                    for (var i = 0; i < production.Items.Count; i++)
                    {
                        var expected = expectedScenario.LocatorResults[i];
                        var actual = production.Items[i];
                        Assert.Equal(expected.Baseline.EngineAccepted, actual.WasIndependentlyConfident);
                        Assert.Equal(expected.Joint.EngineAccepted, actual.Result.IsConfident);
                        Assert.Equal(expected.Disposition.ToString(), actual.ReconciliationDisposition.ToString());
                        if (actual.WasIndependentlyConfident)
                        {
                            // Includes the two observed incidental candidates whose AutomationId is empty.
                            Assert.False(string.IsNullOrEmpty(actual.CandidateIdentity));
                        }

                        comparedLocators++;
                    }
                }
            }

            Assert.Equal(135, comparedLocators);
        }

        [Fact]
        public void FrozenCrossApplicationDataset_ReportsJointAssignmentGeneralization()
        {
            var measurements = new[]
            {
                Measure("HandBrake", "1.8.2", "HandBrake_1.8.2.tree.json"),
                Measure("ShareX", "v21.0.0", "ShareX_v21.0.0.tree.json"),
            };

            foreach (var measurement in measurements)
            {
                Print(measurement);
            }

            var allBaseline = measurements
                .SelectMany(m => m.Report.Scenarios)
                .SelectMany(s => s.LocatorResults)
                .Select(r => r.Baseline)
                .ToList();
            var allJoint = measurements
                .SelectMany(m => m.Report.Scenarios)
                .SelectMany(s => s.LocatorResults)
                .Select(r => r.Joint)
                .ToList();
            var aggregateBaseline = LocatorAblationHarness.Summarize(allBaseline);
            var aggregateJoint = LocatorAblationHarness.Summarize(allJoint);

            Console.WriteLine("=======================================================");
            Console.WriteLine("AGGREGATE");
            Console.WriteLine("=======================================================");
            PrintMetrics("Baseline", aggregateBaseline);
            PrintMetrics("Joint", aggregateJoint);
            Console.WriteLine($"Input collisions: {measurements.Sum(m => m.Report.InputScenariosWithContention)}");
            Console.WriteLine($"Unresolved collisions: {measurements.Sum(m => m.Report.UnresolvedSharedCandidateCollisions)}");
            Console.WriteLine($"Ambiguous ownership declines: {measurements.Sum(m => m.AmbiguousOwnershipDeclines)}");
            Console.WriteLine($"Contested removed false heals: {measurements.Sum(m => m.ContestedRemovedFalseHeals)}");
            Console.WriteLine($"Uncontested removed false heals: {measurements.Sum(m => m.UncontestedRemovedFalseHeals)}");
            Console.WriteLine($"Unidentified removed false-heal candidates: {measurements.Sum(m => m.UnidentifiedRemovedFalseHeals)}");

            AssertMeasurement(
                measurements[0],
                scenarioCount: 36,
                baselineCorrect: 63,
                baselineMissed: 9,
                baselineRemovedDeclines: 22,
                baselineRemovedFalse: 14,
                jointCorrect: 63,
                jointMissed: 9,
                jointRemovedDeclines: 23,
                jointRemovedFalse: 13,
                inputCollisions: 1,
                contestedRemovedFalse: 1,
                uncontestedRemovedFalse: 13,
                unidentifiedRemovedFalse: 2);
            AssertMeasurement(
                measurements[1],
                scenarioCount: 9,
                baselineCorrect: 16,
                baselineMissed: 2,
                baselineRemovedDeclines: 4,
                baselineRemovedFalse: 5,
                jointCorrect: 16,
                jointMissed: 2,
                jointRemovedDeclines: 7,
                jointRemovedFalse: 2,
                inputCollisions: 3,
                contestedRemovedFalse: 3,
                uncontestedRemovedFalse: 2,
                unidentifiedRemovedFalse: 0);

            AssertMetrics(aggregateBaseline, 79, 0, 11, 26, 19, 37.0 / 135.0);
            AssertMetrics(aggregateJoint, 79, 0, 11, 30, 15, 41.0 / 135.0);
            Assert.Equal(4, measurements.Sum(m => m.Report.InputScenariosWithContention));
            Assert.Equal(0, measurements.Sum(m => m.Report.UnresolvedSharedCandidateCollisions));
            Assert.Equal(0, measurements.Sum(m => m.AmbiguousOwnershipDeclines));
            Assert.Equal(4, measurements.Sum(m => m.ContestedRemovedFalseHeals));
            Assert.Equal(15, measurements.Sum(m => m.UncontestedRemovedFalseHeals));
            Assert.Equal(2, measurements.Sum(m => m.UnidentifiedRemovedFalseHeals));

            // These independently selected contentions are not the bidirectional HandBrake cases
            // used to motivate #141. In particular, ShareX's removed Close claims Maximize-Restore,
            // and one removed grid header claims a different surviving header.
            AssertContention(measurements[1], "Close", "Maximize-Restore");
            AssertContention(measurements[1], "4265926980", "4267017949");
            AssertEveryContestedRemovedFalseHealWasSafelyDeclined(measurements[0]);
            AssertEveryContestedRemovedFalseHealWasSafelyDeclined(measurements[1]);

            Assert.All(
                measurements.SelectMany(m => m.Report.Scenarios).SelectMany(s => s.LocatorResults),
                result =>
                {
                    if (result.Joint.EngineAccepted)
                    {
                        Assert.True(result.Baseline.EngineAccepted);
                        Assert.Equal(result.Baseline.MatchedAutomationId, result.Joint.MatchedAutomationId);
                    }
                });

            Assert.Equal(45, measurements.Sum(m => m.Report.Scenarios.Count));
            Assert.Equal(135, allBaseline.Count);
            Assert.Equal(135, allJoint.Count);
        }

        private static void AssertMeasurement(
            GeneralizationMeasurement measurement,
            int scenarioCount,
            int baselineCorrect,
            int baselineMissed,
            int baselineRemovedDeclines,
            int baselineRemovedFalse,
            int jointCorrect,
            int jointMissed,
            int jointRemovedDeclines,
            int jointRemovedFalse,
            int inputCollisions,
            int contestedRemovedFalse,
            int uncontestedRemovedFalse,
            int unidentifiedRemovedFalse)
        {
            var locatorCount = scenarioCount * 3;
            Assert.Equal(scenarioCount, measurement.Report.Scenarios.Count);
            Assert.Equal(locatorCount, measurement.Report.Scenarios.Sum(s => s.LocatorResults.Count));
            AssertMetrics(
                measurement.Report.BaselineMetrics,
                baselineCorrect,
                0,
                baselineMissed,
                baselineRemovedDeclines,
                baselineRemovedFalse,
                (double)(baselineMissed + baselineRemovedDeclines) / locatorCount);
            AssertMetrics(
                measurement.Report.JointMetrics,
                jointCorrect,
                0,
                jointMissed,
                jointRemovedDeclines,
                jointRemovedFalse,
                (double)(jointMissed + jointRemovedDeclines) / locatorCount);
            Assert.Equal(inputCollisions, measurement.Report.InputScenariosWithContention);
            Assert.Equal(0, measurement.Report.UnresolvedSharedCandidateCollisions);
            Assert.Equal(0, measurement.AmbiguousOwnershipDeclines);
            Assert.Equal(contestedRemovedFalse, measurement.ContestedRemovedFalseHeals);
            Assert.Equal(uncontestedRemovedFalse, measurement.UncontestedRemovedFalseHeals);
            Assert.Equal(unidentifiedRemovedFalse, measurement.UnidentifiedRemovedFalseHeals);
        }

        private static void AssertMetrics(
            AblationMetrics metrics,
            int correct,
            int falseHeals,
            int missed,
            int removedDeclines,
            int removedFalse,
            double manualReviewRate)
        {
            Assert.Equal(correct, metrics.CorrectHeals);
            Assert.Equal(falseHeals, metrics.FalseHeals);
            Assert.Equal(missed, metrics.MissedHeals);
            Assert.Equal(removedDeclines, metrics.CorrectDeclines);
            Assert.Equal(removedFalse, metrics.FalseHealsOnRemoved);
            Assert.Equal(manualReviewRate, metrics.ManualReviewRate, 5);
        }

        private static void AssertContention(
            GeneralizationMeasurement measurement,
            string removedAutomationId,
            string winningAutomationId)
        {
            var scenario = Assert.Single(measurement.Report.Scenarios, s => s.LocatorResults.Any(r =>
                r.Baseline.OriginalAutomationId == removedAutomationId &&
                r.Baseline.Outcome == AblationOutcome.FalseHealOnRemoved));
            var removed = Assert.Single(scenario.LocatorResults, r =>
                r.Baseline.OriginalAutomationId == removedAutomationId);
            var winner = Assert.Single(scenario.LocatorResults, r =>
                r.Baseline.OriginalAutomationId == winningAutomationId);
            Assert.Equal(removed.Baseline.MatchedAutomationId, winner.Baseline.MatchedAutomationId);
            Assert.Equal(JointAssignmentDisposition.DeclinedByStrongerClaim, removed.Disposition);
            Assert.Equal(JointAssignmentDisposition.WonContention, winner.Disposition);
        }

        private static void AssertEveryContestedRemovedFalseHealWasSafelyDeclined(
            GeneralizationMeasurement measurement)
        {
            var contested = measurement.Report.Scenarios
                .SelectMany(s => s.LocatorResults
                    .Where(r =>
                        r.Baseline.Outcome == AblationOutcome.FalseHealOnRemoved &&
                        !string.IsNullOrEmpty(r.Baseline.MatchedAutomationId) &&
                        s.LocatorResults.Any(other =>
                            !ReferenceEquals(other, r) &&
                            other.Baseline.EngineAccepted &&
                            string.Equals(
                                other.Baseline.MatchedAutomationId,
                                r.Baseline.MatchedAutomationId,
                                StringComparison.Ordinal)))
                    .Select(r => new { Scenario = s, Removed = r }))
                .ToList();

            Assert.Equal(measurement.ContestedRemovedFalseHeals, contested.Count);
            Assert.All(contested, item =>
            {
                Assert.Equal(AblationOutcome.CorrectDecline, item.Removed.Joint.Outcome);
                Assert.Equal(JointAssignmentDisposition.DeclinedByStrongerClaim, item.Removed.Disposition);
                Assert.Contains(item.Scenario.LocatorResults, other =>
                    other.Baseline.Outcome == AblationOutcome.CorrectHeal &&
                    other.Joint.Outcome == AblationOutcome.CorrectHeal &&
                    other.Disposition == JointAssignmentDisposition.WonContention &&
                    string.Equals(
                        other.Baseline.MatchedAutomationId,
                        item.Removed.Baseline.MatchedAutomationId,
                        StringComparison.Ordinal));
            });
        }

        private static GeneralizationMeasurement Measure(
            string applicationName,
            string sourceVersion,
            string fixtureFileName)
        {
            var root = LoadFixture(fixtureFileName);
            var dataset = JointAssignmentGeneralizationDataset.Generate(
                root,
                applicationName,
                sourceVersion,
                fixtureFileName);
            var baseline = LocatorAblationHarness.RunMultiLocatorBaseline(dataset, root);
            var report = JointLocatorAssignmentEvaluator.Evaluate(baseline);
            var contestedRemovedFalseHeals = baseline.Scenarios.Sum(s =>
                s.LocatorResults
                    .Where(r => r.Outcome == AblationOutcome.FalseHealOnRemoved)
                    .Count(removed => s.LocatorResults.Any(other =>
                        !ReferenceEquals(other, removed) &&
                        !string.IsNullOrEmpty(removed.MatchedAutomationId) &&
                        other.EngineAccepted &&
                        string.Equals(other.MatchedAutomationId, removed.MatchedAutomationId, StringComparison.Ordinal))));

            return new GeneralizationMeasurement
            {
                ApplicationName = applicationName,
                Report = report,
                ContestedRemovedFalseHeals = contestedRemovedFalseHeals,
                UncontestedRemovedFalseHeals = report.BaselineMetrics.FalseHealsOnRemoved - contestedRemovedFalseHeals,
                UnidentifiedRemovedFalseHeals = baseline.Scenarios
                    .SelectMany(s => s.LocatorResults)
                    .Count(r =>
                        r.Outcome == AblationOutcome.FalseHealOnRemoved &&
                        string.IsNullOrEmpty(r.MatchedAutomationId)),
                AmbiguousOwnershipDeclines = report.Scenarios
                    .SelectMany(s => s.LocatorResults)
                    .Count(r => r.Disposition == JointAssignmentDisposition.DeclinedAmbiguousContention),
            };
        }

        private static void Print(GeneralizationMeasurement measurement)
        {
            Console.WriteLine("=======================================================");
            Console.WriteLine($"{measurement.ApplicationName.ToUpperInvariant()} GENERALIZATION (#143)");
            Console.WriteLine("=======================================================");
            Console.WriteLine($"Scenarios: {measurement.Report.Scenarios.Count}");
            Console.WriteLine($"Locator resolutions: {measurement.Report.Scenarios.Sum(s => s.LocatorResults.Count)}");
            PrintMetrics("Baseline", measurement.Report.BaselineMetrics);
            PrintMetrics("Joint", measurement.Report.JointMetrics);
            Console.WriteLine($"Input collisions: {measurement.Report.InputScenariosWithContention}");
            Console.WriteLine($"Unresolved collisions: {measurement.Report.UnresolvedSharedCandidateCollisions}");
            Console.WriteLine($"Ambiguous ownership declines: {measurement.AmbiguousOwnershipDeclines}");
            Console.WriteLine($"Contested removed false heals: {measurement.ContestedRemovedFalseHeals}");
            Console.WriteLine($"Uncontested removed false heals: {measurement.UncontestedRemovedFalseHeals}");
            Console.WriteLine($"Unidentified removed false-heal candidates: {measurement.UnidentifiedRemovedFalseHeals}");

            foreach (var scenario in measurement.Report.Scenarios.Where(s =>
                s.InputSharedCandidateClaims > 0 ||
                s.LocatorResults.Any(r => r.Baseline.Outcome == AblationOutcome.FalseHealOnRemoved)))
            {
                Console.WriteLine($"  {scenario.ScenarioId}: shared={scenario.InputSharedCandidateClaims}, unresolved={scenario.UnresolvedSharedCandidateClaims}");
                foreach (var locator in scenario.LocatorResults)
                {
                    Console.WriteLine(
                        $"    {locator.Joint.OriginalAutomationId}: {locator.Baseline.Outcome} -> {locator.Joint.Outcome}; " +
                        $"matched={DisplayCandidate(locator.Baseline)}; score={locator.Baseline.Score:F3}; {locator.Disposition}");
                }
            }
        }

        private static void PrintMetrics(string label, AblationMetrics metrics)
        {
            Console.WriteLine(
                $"{label}: correct={metrics.CorrectHeals}, false={metrics.FalseHeals}, missed={metrics.MissedHeals}, " +
                $"removed-decline={metrics.CorrectDeclines}, removed-false={metrics.FalseHealsOnRemoved}, " +
                $"precision={metrics.Precision:P1}, manual={metrics.ManualReviewRate:P1}");
        }

        private static string DisplayCandidate(AblationScenarioResult result)
        {
            if (!result.EngineAccepted)
            {
                return "<declined>";
            }

            // The net48 reference assemblies do not annotate string.IsNullOrEmpty with
            // the nullable postcondition modern targets expose. Normalizing once keeps
            // the same display behavior while making the non-null return explicit.
            var matchedAutomationId = result.MatchedAutomationId ?? "";
            return matchedAutomationId.Length == 0
                ? "<empty AutomationId>"
                : matchedAutomationId;
        }

        private static UiElementInfo LoadFixture(string fixtureFileName)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixtureFileName);
            Assert.True(File.Exists(path), $"Ablation fixture not found at '{path}'.");
            var tree = UiTreeSerializer.FromJson(File.ReadAllText(path));
            Assert.NotNull(tree);
            return tree!;
        }

        private sealed class GeneralizationMeasurement
        {
            public string ApplicationName { get; set; } = "";
            public JointAssignmentReport Report { get; set; } = new JointAssignmentReport();
            public int ContestedRemovedFalseHeals { get; set; }
            public int UncontestedRemovedFalseHeals { get; set; }
            public int UnidentifiedRemovedFalseHeals { get; set; }
            public int AmbiguousOwnershipDeclines { get; set; }
        }
    }
}
