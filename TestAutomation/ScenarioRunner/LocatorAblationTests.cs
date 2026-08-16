using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LlmHealing;
using SelfHealing;
using UiModel;
using Xunit;

namespace ScenarioRunner
{
    public class LocatorAblationTests
    {
        private const string FixtureFileName = "HandBrake_1.8.2.tree.json";

        [Fact]
        public void Generate_ProducesMultiSignalScenariosPerLocator()
        {
            var root = SmallTree();

            var dataset = LocatorAblationGenerator.Generate(root, "StubApp", "1.0", "stub.json");

            // btnSave and lblTitle qualify (root is skipped); both have non-empty Name so each yields:
            // rename, name-drift, pos-shift, compound, and remove (5 scenarios each = 10 total).
            Assert.Equal(10, dataset.Scenarios.Count);
            Assert.Equal(2, dataset.Scenarios.Count(s => s.MutationKind == LocatorMutationKind.RenamedAutomationId));
            Assert.Equal(2, dataset.Scenarios.Count(s => s.MutationKind == LocatorMutationKind.NameDrift));
            Assert.Equal(2, dataset.Scenarios.Count(s => s.MutationKind == LocatorMutationKind.PositionShift));
            Assert.Equal(2, dataset.Scenarios.Count(s => s.MutationKind == LocatorMutationKind.CompoundDrift));
            Assert.Equal(2, dataset.Scenarios.Count(s => s.MutationKind == LocatorMutationKind.RemovedElement));

            // Opaque ID check: mutated automation ids do not leak original names or suffixes
            Assert.All(
                dataset.Scenarios.Where(s => s.ExpectedOutcome == LocatorExpectedOutcome.Successor),
                s =>
                {
                    Assert.NotNull(s.GroundTruth);
                    Assert.StartsWith("ablation-", s.MutatedAutomationId);
                    Assert.DoesNotContain(s.OriginalAutomationId, s.MutatedAutomationId);
                });

            Assert.All(
                dataset.Scenarios.Where(s => s.ExpectedOutcome == LocatorExpectedOutcome.NoSuccessor),
                s =>
                {
                    Assert.Null(s.GroundTruth);
                    Assert.Null(s.MutatedAutomationId);
                });
        }

        [Fact]
        public void Generate_SkipsNameDrift_WhenElementHasEmptyName()
        {
            var root = new UiElementInfo { ControlType = "Window", AutomationId = "MainWindow" };
            root.Children.Add(new UiElementInfo { ControlType = "Button", AutomationId = "btnNameless", Name = "" });

            var dataset = LocatorAblationGenerator.Generate(root, "StubApp", "1.0", "stub.json");

            // Element with empty Name produces: rename, pos-shift, and remove (3 scenarios), NOT NameDrift or CompoundDrift.
            Assert.Equal(3, dataset.Scenarios.Count);
            Assert.DoesNotContain(dataset.Scenarios, s => s.MutationKind == LocatorMutationKind.NameDrift);
            Assert.DoesNotContain(dataset.Scenarios, s => s.MutationKind == LocatorMutationKind.CompoundDrift);
        }

        [Fact]
        public void Generate_SkipsDuplicateAutomationIds_BecauseGroundTruthWouldBeAmbiguous()
        {
            var root = new UiElementInfo { ControlType = "Window", AutomationId = "MainWindow" };
            root.Children.Add(new UiElementInfo { ControlType = "Button", AutomationId = "dup", Name = "First" });
            root.Children.Add(new UiElementInfo { ControlType = "Button", AutomationId = "dup", Name = "Second" });
            root.Children.Add(new UiElementInfo { ControlType = "Button", AutomationId = "unique", Name = "Only" });

            var dataset = LocatorAblationGenerator.Generate(root, "StubApp", "1.0", "stub.json");

            Assert.DoesNotContain(dataset.Scenarios, s => s.OriginalAutomationId == "dup");
            Assert.Equal(5, dataset.Scenarios.Count);
        }

        [Fact]
        public void ApplyMutation_IsDeterministicAndLeavesTheSourceTreeUntouched()
        {
            var root = SmallTree();
            var dataset = LocatorAblationGenerator.Generate(root, "StubApp", "1.0", "stub.json");
            var rename = dataset.Scenarios.Single(s =>
                s.OriginalAutomationId == "btnSave" && s.MutationKind == LocatorMutationKind.RenamedAutomationId);

            var first = LocatorAblationGenerator.ApplyMutation(root, rename);
            var second = LocatorAblationGenerator.ApplyMutation(root, rename);

            Assert.Equal(UiTreeSerializer.ToJson(first), UiTreeSerializer.ToJson(second));
            Assert.Contains(Flatten(first), e => e.AutomationId == rename.MutatedAutomationId);

            // The source must survive intact, or scenario N would run against the damage from N-1.
            Assert.Contains(Flatten(root), e => e.AutomationId == "btnSave");
        }

        [Fact]
        public void ApplyMutation_RemovesTheWholeSubtree()
        {
            var root = SmallTree();
            var dataset = LocatorAblationGenerator.Generate(root, "StubApp", "1.0", "stub.json");
            var removal = dataset.Scenarios.Single(s =>
                s.OriginalAutomationId == "btnSave" && s.MutationKind == LocatorMutationKind.RemovedElement);

            var mutated = LocatorAblationGenerator.ApplyMutation(root, removal);

            Assert.DoesNotContain(Flatten(mutated), e => e.AutomationId == "btnSave");
            Assert.DoesNotContain(Flatten(mutated), e => e.Name == "Save icon");
        }

        [Fact]
        public void Harness_ScoresARenameAsCorrectHeal_AndARemovalAsCorrectDecline()
        {
            var root = SmallTree();
            var dataset = LocatorAblationGenerator.Generate(root, "StubApp", "1.0", "stub.json");

            var report = LocatorAblationHarness.Run(dataset, root);

            var rename = report.Results.Single(r =>
                r.OriginalAutomationId == "btnSave" && r.MutationKind == LocatorMutationKind.RenamedAutomationId);
            Assert.Equal(AblationOutcome.CorrectHeal, rename.Outcome);

            // Every result carries its score vector, so thresholds can be swept without re-running.
            Assert.NotEmpty(rename.Candidates);
        }

        [Fact]
        public void Metrics_SeparateAWrongMatchFromADeclineBecauseTheyCostDifferentThings()
        {
            var results = new[]
            {
                Result(LocatorMutationKind.RenamedAutomationId, AblationOutcome.CorrectHeal),
                Result(LocatorMutationKind.RenamedAutomationId, AblationOutcome.FalseHeal),
                Result(LocatorMutationKind.NameDrift, AblationOutcome.MissedHeal),
                Result(LocatorMutationKind.RemovedElement, AblationOutcome.CorrectDecline),
                Result(LocatorMutationKind.RemovedElement, AblationOutcome.FalseHealOnRemoved),
            };

            var metrics = LocatorAblationHarness.Summarize(results);

            Assert.Equal(1.0 / 3.0, metrics.AutoHealRecall, 5);
            Assert.Equal(1.0 / 3.0, metrics.Precision, 5);
            // Accepted = 1 correct + 1 wrong + 1 wrong-on-removed; two of three were wrong.
            Assert.Equal(2.0 / 3.0, metrics.FalseHealRate, 5);
            Assert.Equal(2.0 / 5.0, metrics.ManualReviewRate, 5);
        }

        [Fact]
        public void Dataset_RoundTripsThroughJson()
        {
            var dataset = LocatorAblationGenerator.Generate(SmallTree(), "StubApp", "1.0", "stub.json");

            var restored = LocatorAblationDatasetSerializer.FromJson(LocatorAblationDatasetSerializer.ToJson(dataset));

            Assert.Equal(dataset.Scenarios.Count, restored.Scenarios.Count);
            Assert.Equal(LocatorAblationDataset.CurrentSchemaVersion, restored.SchemaVersion);
            Assert.Equal(
                dataset.Scenarios.Select(s => s.ScenarioId),
                restored.Scenarios.Select(s => s.ScenarioId));
        }

        [Fact]
        public void Dataset_RejectsANewerSchemaRatherThanReadingItWrong()
        {
            var json = LocatorAblationDatasetSerializer
                .ToJson(new LocatorAblationDataset { SchemaVersion = LocatorAblationDataset.CurrentSchemaVersion + 1 });

            Assert.Throws<InvalidOperationException>(() => LocatorAblationDatasetSerializer.FromJson(json));
        }

        [Fact]
        public void HandBrakeFixture_YieldsAtLeastFortyScenariosFromRealAuthoredLocators()
        {
            var root = LoadFixture();

            var dataset = LocatorAblationGenerator.Generate(root, "HandBrake", "1.8.2", FixtureFileName);

            // 42 unique authored locators: each yields rename, pos-shift, remove, and if Name is present also name-drift and compound.
            Assert.True(
                dataset.Scenarios.Count >= 100,
                $"Expected at least 100 multi-signal scenarios from the HandBrake fixture, got {dataset.Scenarios.Count}.");
            Assert.Contains(dataset.Scenarios, s => s.OriginalAutomationId == "presetMenu");
            Assert.Contains(dataset.Scenarios, s => s.OriginalAutomationId == "SelectPresetsButton");
        }

        [Fact]
        public void HandBrakeFixture_RunsEndToEndAndReportsMetrics()
        {
            var root = LoadFixture();
            var dataset = LocatorAblationGenerator.Generate(root, "HandBrake", "1.8.2", FixtureFileName);

            var report = LocatorAblationHarness.Run(dataset, root);

            Console.WriteLine(LocatorAblationHarness.ToMarkdownSummary(report, "HandBrake 1.8.2"));

            // Deliberately asserts shape, not values: pinning today's accuracy would turn a genuine
            // regression into a test that has to be "updated" rather than investigated.
            Assert.Equal(dataset.Scenarios.Count, report.Results.Count);
            Assert.All(report.Results, r => Assert.NotEmpty(r.ScenarioId));
            Assert.Equal(
                report.Metrics.SuccessorScenarios,
                report.Metrics.CorrectHeals + report.Metrics.FalseHeals + report.Metrics.MissedHeals);
            Assert.Equal(
                report.Metrics.RemovalScenarios,
                report.Metrics.CorrectDeclines + report.Metrics.FalseHealsOnRemoved);
        }

        [Fact]
        public void HandBrakeFixture_ThresholdSweep()
        {
            var root = LoadFixture();
            var dataset = LocatorAblationGenerator.Generate(root, "HandBrake", "1.8.2", FixtureFileName);

            Console.WriteLine("| MinConfidence | Precision | Recall | FalseHealRate | ManualReviewRate | CorrectHeals | FalseHeals | FalseOnRemoved | Missed | CorrectDeclines |");
            Console.WriteLine("| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");

            for (double conf = 0.50; conf <= 0.951; conf += 0.05)
            {
                var weights = new SimilarityWeights
                {
                    MinimumConfidence = conf,
                };
                var report = LocatorAblationHarness.Run(dataset, root, weights);
                var m = report.Metrics;
                var accepted = m.CorrectHeals + m.FalseHeals + m.FalseHealsOnRemoved;
                var precision = accepted == 0 ? 1.0 : (double)m.CorrectHeals / accepted;
                Console.WriteLine($"| {conf:F2} | {precision:P1} | {m.AutoHealRecall:P1} | {m.FalseHealRate:P1} | {m.ManualReviewRate:P1} | {m.CorrectHeals} | {m.FalseHeals} | {m.FalseHealsOnRemoved} | {m.MissedHeals} | {m.CorrectDeclines} |");
            }

            // Inspect false heals on removed elements at 0.50 and 0.80
            var report050 = LocatorAblationHarness.Run(dataset, root, new SimilarityWeights { MinimumConfidence = 0.50 });
            // Per-mutation kind breakdown at default weights (0.50)
            Console.WriteLine("\nPer-mutation breakdown at default weights (0.50):");
            foreach (LocatorMutationKind kind in Enum.GetValues(typeof(LocatorMutationKind)))
            {
                var subset = report050.Results.Where(r => r.MutationKind == kind).ToList();
                if (subset.Count == 0) continue;
                var correct = subset.Count(r => r.Outcome == AblationOutcome.CorrectHeal || r.Outcome == AblationOutcome.CorrectDecline);
                var falseHeals = subset.Count(r => r.Outcome == AblationOutcome.FalseHeal || r.Outcome == AblationOutcome.FalseHealOnRemoved);
                var missed = subset.Count(r => r.Outcome == AblationOutcome.MissedHeal);
                var minScore = subset.Count > 0 ? subset.Min(r => r.Score) : 0;
                var maxScore = subset.Count > 0 ? subset.Max(r => r.Score) : 0;
                var avgScore = subset.Count > 0 ? subset.Average(r => r.Score) : 0;
                Console.WriteLine($"  {kind,-20} (n={subset.Count,2}): Correct={correct,2}, False={falseHeals,2}, Missed={missed,2} | Score Range: [{minScore:F3} - {maxScore:F3}], Avg: {avgScore:F3}");
            }

            // Offline Absence Signal Investigation across the 176 HandBrake scenarios
            Console.WriteLine("\n=======================================================");
            Console.WriteLine("OFFLINE EVALUATION OF CANDIDATE ABSENCE SIGNALS (#95)");
            Console.WriteLine("=======================================================");

            // 1. Margin threshold sweep (0.05 to 0.20)
            Console.WriteLine("\n[Hypothesis 1] Runner-Up Margin Gating (at MinimumConfidence = 0.50):");
            Console.WriteLine("| MinMargin | Compound Recall (n=25) | Removed False Heals (n=42) | Total Precision | AutoHeal Recall |");
            Console.WriteLine("| ---: | ---: | ---: | ---: | ---: |");
            foreach (var minMargin in new[] { 0.00, 0.05, 0.08, 0.10, 0.15, 0.20 })
            {
                var w = new SimilarityWeights { MinimumConfidence = 0.50, MinimumCandidateMargin = minMargin };
                var r = LocatorAblationHarness.Run(dataset, root, w);
                var compoundCorrect = r.Results.Count(x => x.MutationKind == LocatorMutationKind.CompoundDrift && x.Outcome == AblationOutcome.CorrectHeal);
                var falseRemoved = r.Results.Count(x => x.Outcome == AblationOutcome.FalseHealOnRemoved);
                Console.WriteLine($"| {minMargin:F2} | {compoundCorrect,2}/25 ({(double)compoundCorrect/25:P1}) | {falseRemoved,2}/42 ({(double)falseRemoved/42:P1}) | {r.Metrics.Precision:P1} | {r.Metrics.AutoHealRecall:P1} |");
            }

            // 2. ControlType Invariance (Strict ControlType match)
            Console.WriteLine("\n[Hypothesis 2] Strict ControlType Gating (ControlTypeScore == 1.0 required):");
            {
                var r = LocatorAblationHarness.Run(dataset, root, new SimilarityWeights { MinimumConfidence = 0.50 });
                // Filter results where top candidate has ControlTypeScore == 1.0
                var compoundCorrectStrict = r.Results.Count(x => x.MutationKind == LocatorMutationKind.CompoundDrift && x.Outcome == AblationOutcome.CorrectHeal && x.Candidates.Count > 0 && x.Candidates[0].Components.ControlTypeScore == 1.0);
                var falseRemovedStrict = r.Results.Count(x => x.Outcome == AblationOutcome.FalseHealOnRemoved && x.Candidates.Count > 0 && x.Candidates[0].Components.ControlTypeScore == 1.0);
                Console.WriteLine($"  Compound Recall: {compoundCorrectStrict}/25 ({(double)compoundCorrectStrict/25:P1}), Removed False Heals: {falseRemovedStrict}/42 ({(double)falseRemovedStrict/42:P1})");
            }

            // 3. Cluster Density / Decoy Count (Candidates within 0.10 of top candidate)
            Console.WriteLine("\n[Hypothesis 3] Candidate Cluster Density (Score within 0.10 of Top Candidate):");
            {
                var r = LocatorAblationHarness.Run(dataset, root, new SimilarityWeights { MinimumConfidence = 0.50 });
                var falseRemovedClusters = r.Results
                    .Where(x => x.Outcome == AblationOutcome.FalseHealOnRemoved)
                    .Select(x => x.Candidates.Count(c => c.TotalScore >= x.Candidates[0].TotalScore - 0.10))
                    .ToList();
                var compoundClusters = r.Results
                    .Where(x => x.MutationKind == LocatorMutationKind.CompoundDrift)
                    .Select(x => x.Candidates.Count(c => c.TotalScore >= (x.Candidates.Count > 0 ? x.Candidates[0].TotalScore - 0.10 : 0.0)))
                    .ToList();
                Console.WriteLine($"  Removed False Heals avg cluster size: {(falseRemovedClusters.Count > 0 ? falseRemovedClusters.Average() : 0):F2} (range {falseRemovedClusters.Min()} - {falseRemovedClusters.Max()})");
                Console.WriteLine($"  Compound Drift avg cluster size: {(compoundClusters.Count > 0 ? compoundClusters.Average() : 0):F2} (range {compoundClusters.Min()} - {compoundClusters.Max()})");
            }

            // 4. Combined Margin + Strict ControlType + Position Filter
            Console.WriteLine("\n[Hypothesis 4] Combined Filter (Strict ControlType + Margin >= 0.08):");
            {
                var w = new SimilarityWeights { MinimumConfidence = 0.50, MinimumCandidateMargin = 0.08 };
                var r = LocatorAblationHarness.Run(dataset, root, w);
                var compoundCorrect = r.Results.Count(x => x.MutationKind == LocatorMutationKind.CompoundDrift && x.Outcome == AblationOutcome.CorrectHeal && x.Candidates[0].Components.ControlTypeScore == 1.0);
                var falseRemoved = r.Results.Count(x => x.Outcome == AblationOutcome.FalseHealOnRemoved && x.Candidates[0].Components.ControlTypeScore == 1.0);
                Console.WriteLine($"  Compound Recall: {compoundCorrect}/25 ({(double)compoundCorrect/25:P1}), Removed False Heals: {falseRemoved}/42 ({(double)falseRemoved/42:P1})");
            }
        }

        [Fact]
        public void HandBrakeFixture_AbsenceSignalEvaluation_RunnerUpMarginDoesNotSeparateBands()
        {
            var root = LoadFixture();
            var dataset = LocatorAblationGenerator.Generate(root, "HandBrake", "1.8.2", FixtureFileName);

            // Baseline at default margin (0.05)
            var report005 = LocatorAblationHarness.Run(dataset, root, new SimilarityWeights { MinimumConfidence = 0.50, MinimumCandidateMargin = 0.05 });
            var compound005 = report005.Results.Count(r => r.MutationKind == LocatorMutationKind.CompoundDrift && r.Outcome == AblationOutcome.CorrectHeal);
            var removed005 = report005.Results.Count(r => r.Outcome == AblationOutcome.FalseHealOnRemoved);

            // Tight margin (0.10)
            var report010 = LocatorAblationHarness.Run(dataset, root, new SimilarityWeights { MinimumConfidence = 0.50, MinimumCandidateMargin = 0.10 });
            var compound010 = report010.Results.Count(r => r.MutationKind == LocatorMutationKind.CompoundDrift && r.Outcome == AblationOutcome.CorrectHeal);
            var removed010 = report010.Results.Count(r => r.Outcome == AblationOutcome.FalseHealOnRemoved);

            // Ultra-tight margin (0.20)
            var report020 = LocatorAblationHarness.Run(dataset, root, new SimilarityWeights { MinimumConfidence = 0.50, MinimumCandidateMargin = 0.20 });
            var compound020 = report020.Results.Count(r => r.MutationKind == LocatorMutationKind.CompoundDrift && r.Outcome == AblationOutcome.CorrectHeal);
            var removed020 = report020.Results.Count(r => r.Outcome == AblationOutcome.FalseHealOnRemoved);

            // Finding: Margin gating filters compound drift faster than it eliminates deleted false heals.
            // At 0.10, compound recall drops 67% (6 -> 2) while 7 false heals on removed elements still survive.
            Assert.True(compound010 < compound005);
            Assert.True(removed010 > 0);

            // At 0.20, compound recall is completely destroyed (0) while false heals on removed elements still persist (2).
            Assert.Equal(0, compound020);
            Assert.True(removed020 > 0);
        }

        [Fact]
        public void HandBrakeFixture_AbsenceSignalEvaluation_ClusterDensityCannotDetectAbsence()
        {
            var root = LoadFixture();
            var dataset = LocatorAblationGenerator.Generate(root, "HandBrake", "1.8.2", FixtureFileName);
            var report = LocatorAblationHarness.Run(dataset, root, new SimilarityWeights { MinimumConfidence = 0.50 });

            // Decoy cluster density: number of candidates scored within 0.10 of the best candidate
            var falseRemovedClusters = report.Results
                .Where(x => x.Outcome == AblationOutcome.FalseHealOnRemoved && x.Candidates.Count > 0)
                .Select(x => x.Candidates.Count(c => c.TotalScore >= x.Candidates[0].TotalScore - 0.10))
                .ToList();

            var compoundClusters = report.Results
                .Where(x => x.MutationKind == LocatorMutationKind.CompoundDrift && x.Candidates.Count > 0)
                .Select(x => x.Candidates.Count(c => c.TotalScore >= x.Candidates[0].TotalScore - 0.10))
                .ToList();

            // Finding: Compound drift has higher cluster density than removed elements (true moved element competes with neighbors in the new location)
            Assert.True(compoundClusters.Average() > falseRemovedClusters.Average(),
                $"Expected compound drift average cluster size ({compoundClusters.Average():F2}) to exceed removed elements ({falseRemovedClusters.Average():F2}).");
        }

        [Fact]
        public void HandBrakeFixture_ThresholdSweep_DemonstratesTradeOff()
        {
            var root = LoadFixture();
            var dataset = LocatorAblationGenerator.Generate(root, "HandBrake", "1.8.2", FixtureFileName);

            var report050 = LocatorAblationHarness.Run(dataset, root, new SimilarityWeights { MinimumConfidence = 0.50 });
            var report080 = LocatorAblationHarness.Run(dataset, root, new SimilarityWeights { MinimumConfidence = 0.80 });
            var report095 = LocatorAblationHarness.Run(dataset, root, new SimilarityWeights { MinimumConfidence = 0.95 });

            // 1. Monotonic trade-off: higher threshold reduces false heals on removed elements while increasing manual review rate
            Assert.True(report095.Metrics.FalseHealsOnRemoved < report050.Metrics.FalseHealsOnRemoved,
                $"Expected false heals on removed elements to decrease at 0.95 ({report095.Metrics.FalseHealsOnRemoved}) vs 0.50 ({report050.Metrics.FalseHealsOnRemoved}).");

            Assert.True(report095.Metrics.ManualReviewRate > report050.Metrics.ManualReviewRate,
                $"Expected manual review rate to increase at 0.95 ({report095.Metrics.ManualReviewRate:P1}) vs 0.50 ({report050.Metrics.ManualReviewRate:P1}).");

            Assert.True(report095.Metrics.Precision > report050.Metrics.Precision,
                $"Expected precision to increase at 0.95 ({report095.Metrics.Precision:P1}) vs 0.50 ({report050.Metrics.Precision:P1}).");

            // 2. Non-trivial recall curve: multi-signal drift does NOT remain artificially flat at 1.0
            Assert.True(report095.Metrics.AutoHealRecall < report050.Metrics.AutoHealRecall,
                $"Expected auto-heal recall to drop as threshold tightens ({report095.Metrics.AutoHealRecall:P1} at 0.95 vs {report050.Metrics.AutoHealRecall:P1} at 0.50).");

            // 3. Empirical score distribution overlap: max score of false heal on removed elements overlaps with drifted true elements
            var maxFalseOnRemoved = report050.Results
                .Where(r => r.Outcome == AblationOutcome.FalseHealOnRemoved)
                .Max(r => r.Score);

            var minCompoundCorrect = report050.Results
                .Where(r => r.MutationKind == LocatorMutationKind.CompoundDrift && r.Outcome == AblationOutcome.CorrectHeal)
                .Min(r => r.Score);

            Assert.True(maxFalseOnRemoved > minCompoundCorrect,
                $"Empirical score overlap expected: max false heal on removed ({maxFalseOnRemoved:F3}) > min compound correct heal ({minCompoundCorrect:F3}).");
        }

        [Fact]
        public void HandBrakeFixture_AblationPrompt_ContainsNoAutomationIdOrAblationMarkers()
        {
            var root = LoadFixture();
            var dataset = LocatorAblationGenerator.Generate(root, "HandBrake", "1.8.2", FixtureFileName);
            var opaqueIdPattern = new System.Text.RegularExpressions.Regex(@"^ablation-[0-9a-f]{8}$");

            foreach (var scenario in dataset.Scenarios)
            {
                var expected = LocatorAblationGenerator.FindExpectedElement(root, scenario.OriginalAutomationId)!;
                var mutatedRoot = LocatorAblationGenerator.ApplyMutation(root, scenario);
                var shortlist = SelfHealingResolver.ScoreCandidates(expected, mutatedRoot, SimilarityWeights.Default)
                    .Take(SimilarityWeights.Default.MaxCandidatesForLlm)
                    .ToList();
                for (var i = 0; i < shortlist.Count; i++)
                {
                    shortlist[i].CandidateId = "c" + i;
                }

                var prompt = LlmHealingPrompt.Build(expected, shortlist, platform: "windows-desktop");

                // 1. Expected's original AutomationId must be redacted to empty string
                Assert.Contains("\"AutomationId\": \"\"", prompt);
                if (!string.IsNullOrEmpty(scenario.OriginalAutomationId))
                {
                    Assert.DoesNotContain($"\"AutomationId\": \"{scenario.OriginalAutomationId}\"", prompt);
                }

                // 2. All candidate AutomationIds in the mutated tree must have the exact same opaque format
                // so no candidate is distinguishable by its identifier structure (#97).
                foreach (var c in shortlist)
                {
                    var id = c.Candidate.AutomationId;
                    if (!string.IsNullOrEmpty(id))
                    {
                        Assert.Matches(opaqueIdPattern, id);
                    }
                }
            }
        }

        [Fact]
        public async Task HandBrakeFixture_LlmConsensus_HarnessPlumbingWorksWithMockProviders()
        {
            var root = LoadFixture();
            var dataset = LocatorAblationGenerator.Generate(root, "HandBrake", "1.8.2", FixtureFileName);

            // Filter to 2 compound drift scenarios where heuristic confidence is low
            var subset = new LocatorAblationDataset
            {
                Scenarios = dataset.Scenarios.Where(s => s.MutationKind == LocatorMutationKind.CompoundDrift).Take(2).ToList(),
            };

            var p1 = new MockAblationProvider("MockClaude", (exp, cands) => (cands.Count > 0 ? cands[0].CandidateId : null, 0.9, "matched"));
            var p2 = new MockAblationProvider("MockGemini", (exp, cands) => (cands.Count > 0 ? cands[0].CandidateId : null, 0.85, "matched"));

            var report = await LocatorAblationHarness.RunAsync(
                subset,
                root,
                new[] { p1, p2 },
                new SimilarityWeights { MinimumConfidence = 0.90 }); // High heuristic threshold forces LLM fallback

            Assert.Equal(2, report.Results.Count);
            Assert.All(report.Results, r =>
            {
                Assert.Equal(HealSource.Llm, r.Source);
                Assert.Equal(2, r.AgreedProviders.Count);
                Assert.Contains("MockClaude", r.AgreedProviders);
                Assert.Contains("MockGemini", r.AgreedProviders);
                Assert.Equal(2, r.ProviderVotes.Count);
                Assert.Equal("c0", r.ProviderVotes["MockClaude"]);
                Assert.Equal("c0", r.ProviderVotes["MockGemini"]);
                Assert.NotNull(r.ProviderResults["MockClaude"]);
                Assert.NotNull(r.ProviderResults["MockGemini"]);
            });

            // Verify scattered votes: p1 -> c0, p2 -> c1 yields NoConsensus but preserves raw votes
            var scatterP1 = new MockAblationProvider("MockClaude", (exp, cands) => ("c0", 0.9, "reason1"));
            var scatterP2 = new MockAblationProvider("MockGemini", (exp, cands) => ("c1", 0.85, "reason2"));
            var scatterReport = await LocatorAblationHarness.RunAsync(
                subset,
                root,
                new[] { scatterP1, scatterP2 },
                new SimilarityWeights { MinimumConfidence = 0.90 });

            Assert.All(scatterReport.Results, r =>
            {
                Assert.Equal(HealResolutionStatus.NoConsensus, r.ResolutionStatus);
                Assert.Empty(r.AgreedProviders);
                Assert.Equal("c0", r.ProviderVotes["MockClaude"]);
                Assert.Equal("c1", r.ProviderVotes["MockGemini"]);
            });
        }

        [Fact]
        public async Task HandBrakeFixture_LlmConsensus_LiveEvaluation()
        {
            // Opt-in like every other live test here. Without the flag a workflow that merely has
            // provider credentials in scope would spend real API budget by accident.
            var optIn = Environment.GetEnvironmentVariable("RUN_ABLATION_CONSENSUS");
            if (optIn != "1" && !string.Equals(optIn, "true", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("[LlmConsensusEvaluation] RUN_ABLATION_CONSENSUS=1 is not set - skipping live evaluation.");
                return;
            }

            var configured = LlmProviderFactory.CreateConfiguredProviders();
            if (configured.Count < 2)
            {
                Console.WriteLine($"[LlmConsensusEvaluation] At least 2 configured LLM providers are required (found {configured.Count}) - skipping live evaluation.");
                return;
            }

            var root = LoadFixture();
            var dataset = LocatorAblationGenerator.Generate(root, "HandBrake", "1.8.2", FixtureFileName);

            // Target the 25 CompoundDrift and 42 RemovedElement scenarios (cost control)
            var report = await LocatorAblationHarness.RunAsync(
                dataset,
                root,
                configured,
                new SimilarityWeights { MinimumConfidence = 0.50 },
                scenarioFilter: s => s.MutationKind == LocatorMutationKind.CompoundDrift || s.MutationKind == LocatorMutationKind.RemovedElement);

            var markdown = LocatorAblationHarness.ToMarkdownSummary(report, "HandBrake 1.8.2 Live Consensus");
            Console.WriteLine(markdown);

            var summaryPath = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
            if (!string.IsNullOrEmpty(summaryPath))
            {
                File.AppendAllText(summaryPath, Environment.NewLine + markdown + Environment.NewLine);
            }

            // The run is not reproducible - providers are non-deterministic - so the raw votes are the
            // only auditable record of what was actually answered. Keep them next to the summary.
            var outputDir = Environment.GetEnvironmentVariable("ABLATION_OUTPUT_DIR");
            if (!string.IsNullOrEmpty(outputDir))
            {
                Directory.CreateDirectory(outputDir);
                var votes = report.Results.Select(r => new
                {
                    r.ScenarioId,
                    MutationKind = r.MutationKind.ToString(),
                    Outcome = r.Outcome.ToString(),
                    VotePattern = r.VotePattern.ToString(),
                    r.ProviderVotes,
                    AgreedProviders = r.AgreedProviders,

                    // Without these a failed run cannot be diagnosed at all: the first live run showed
                    // every scenario as a provider failure and the reason was nowhere on disk.
                    r.ProviderErrors,
                    ProviderMessages = r.ProviderResults.ToDictionary(
                        kvp => kvp.Key,
                        kvp => kvp.Value == null
                            ? "no result"
                            : (kvp.Value.Success ? "ok" : "failed: " + kvp.Value.ErrorMessage)),
                    r.Score,
                });
                File.WriteAllText(
                    Path.Combine(outputDir, "ablation-consensus-votes.json"),
                    System.Text.Json.JsonSerializer.Serialize(votes, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }

            Assert.NotEmpty(report.Results);

            // A run that produced no votes is not a green run. Reporting success here is how the first
            // live evaluation looked healthy while answering nothing.
            var usable = LocatorAblationHarness.UsableConsensusScenarios(report.Results);
            Assert.True(
                usable > 0,
                "The live evaluation produced no usable provider votes - every evaluated scenario failed at the provider. " +
                "See the uploaded ablation-consensus-votes.json for the per-provider error messages.");
        }

        [Fact]
        public async Task LlmConsensus_ScatteredVotes_AreDistinguishableFromMutualDecline()
        {
            // These two look identical in HealResult - both are "no consensus" - and they mean opposite
            // things for #97. Scattering supports the hypothesis; mutual decline is a different finding.
            var root = LoadFixture();
            var dataset = LocatorAblationGenerator.Generate(root, "HandBrake", "1.8.2", FixtureFileName);
            var subset = new LocatorAblationDataset
            {
                Scenarios = dataset.Scenarios.Where(x => x.MutationKind == LocatorMutationKind.RemovedElement).Take(1).ToList(),
            };
            var weights = new SimilarityWeights { MinimumConfidence = 0.99 };

            var scattered = await LocatorAblationHarness.RunAsync(
                subset,
                root,
                new ILlmHealingProvider[]
                {
                    new MockAblationProvider("A", (_, c) => (c.Count > 0 ? c[0].CandidateId : null, 0.9, "")),
                    new MockAblationProvider("B", (_, c) => (c.Count > 1 ? c[1].CandidateId : null, 0.9, "")),
                },
                weights);

            var declined = await LocatorAblationHarness.RunAsync(
                subset,
                root,
                new ILlmHealingProvider[]
                {
                    new MockAblationProvider("A", (_, _) => (null, 0.0, "no match")),
                    new MockAblationProvider("B", (_, _) => (null, 0.0, "no match")),
                },
                weights);

            Assert.Equal(ConsensusVotePattern.Scattered, scattered.Results[0].VotePattern);
            Assert.Equal(ConsensusVotePattern.AllDeclined, declined.Results[0].VotePattern);
        }

        [Fact]
        public async Task LlmConsensus_UnanimousVoteOnARemovedElement_IsRecordedAsSuch()
        {
            // The failure mode the issue exists to detect: both providers confidently name the same
            // neighbour for a control that no longer exists.
            var root = LoadFixture();
            var dataset = LocatorAblationGenerator.Generate(root, "HandBrake", "1.8.2", FixtureFileName);
            var subset = new LocatorAblationDataset
            {
                Scenarios = dataset.Scenarios.Where(x => x.MutationKind == LocatorMutationKind.RemovedElement).Take(1).ToList(),
            };

            var report = await LocatorAblationHarness.RunAsync(
                subset,
                root,
                new ILlmHealingProvider[]
                {
                    new MockAblationProvider("A", (_, c) => (c.Count > 0 ? c[0].CandidateId : null, 0.9, "")),
                    new MockAblationProvider("B", (_, c) => (c.Count > 0 ? c[0].CandidateId : null, 0.8, "")),
                },
                new SimilarityWeights { MinimumConfidence = 0.99 });

            Assert.Equal(ConsensusVotePattern.Unanimous, report.Results[0].VotePattern);
            Assert.Equal(AblationOutcome.FalseHealOnRemoved, report.Results[0].Outcome);

            var markdown = LocatorAblationHarness.ToMarkdownSummary(report, "unit");
            Assert.Contains("Provider vote patterns", markdown);
            Assert.Contains("Answer to the #97 question", markdown);
        }

        [Fact]
        public async Task LlmConsensus_WhenEveryProviderFails_TheSummaryRefusesToAnswer()
        {
            // The first live run (31931888890) reported "agreed unanimously 0 time(s)" while no provider
            // had answered at all. Zero agreement through silence reads identically to zero agreement
            // through scatter, and only one of them supports the hypothesis.
            var root = LoadFixture();
            var dataset = LocatorAblationGenerator.Generate(root, "HandBrake", "1.8.2", FixtureFileName);
            var subset = new LocatorAblationDataset
            {
                Scenarios = dataset.Scenarios.Where(x => x.MutationKind == LocatorMutationKind.RemovedElement).Take(2).ToList(),
            };

            var report = await LocatorAblationHarness.RunAsync(
                subset,
                root,
                new ILlmHealingProvider[]
                {
                    new FailingAblationProvider("A", "429 rate limited"),
                    new FailingAblationProvider("B", "429 rate limited"),
                },
                new SimilarityWeights { MinimumConfidence = 0.99 });

            Assert.All(report.Results, r => Assert.Equal(ConsensusVotePattern.ProviderFailure, r.VotePattern));
            Assert.Equal(0, LocatorAblationHarness.UsableConsensusScenarios(report.Results));

            var markdown = LocatorAblationHarness.ToMarkdownSummary(report, "unit");
            Assert.Contains("No usable data", markdown);
            Assert.DoesNotContain("Answer to the #97 question", markdown);
        }

        [Fact]
        public async Task LlmConsensus_WhenSomeProvidersFail_TheAnswerExcludesThemAndSaysSo()
        {
            var root = LoadFixture();
            var dataset = LocatorAblationGenerator.Generate(root, "HandBrake", "1.8.2", FixtureFileName);
            var removed = dataset.Scenarios.Where(x => x.MutationKind == LocatorMutationKind.RemovedElement).Take(2).ToList();
            var weights = new SimilarityWeights { MinimumConfidence = 0.99 };

            var good = await LocatorAblationHarness.RunAsync(
                new LocatorAblationDataset { Scenarios = removed.Take(1).ToList() },
                root,
                new ILlmHealingProvider[]
                {
                    new MockAblationProvider("A", (_, c) => (c.Count > 0 ? c[0].CandidateId : null, 0.9, "")),
                    new MockAblationProvider("B", (_, c) => (c.Count > 0 ? c[0].CandidateId : null, 0.9, "")),
                },
                weights);

            var bad = await LocatorAblationHarness.RunAsync(
                new LocatorAblationDataset { Scenarios = removed.Skip(1).Take(1).ToList() },
                root,
                new ILlmHealingProvider[]
                {
                    new FailingAblationProvider("A", "500"),
                    new FailingAblationProvider("B", "500"),
                },
                weights);

            var merged = new AblationRunReport { Results = good.Results.Concat(bad.Results).ToList() };
            merged.Metrics = LocatorAblationHarness.Summarize(merged.Results);

            var markdown = LocatorAblationHarness.ToMarkdownSummary(merged, "unit");
            Assert.Contains("Answer to the #97 question", markdown);
            Assert.Contains("failed at the provider and are excluded", markdown);
            Assert.Equal(1, LocatorAblationHarness.UsableConsensusScenarios(merged.Results));
        }

        private sealed class FailingAblationProvider : ILlmHealingProvider
        {
            private readonly string _error;

            public FailingAblationProvider(string name, string error)
            {
                Name = name;
                _error = error;
            }

            public string Name { get; }
            public bool IsAvailable => true;

            public Task<LlmHealingResult> ResolveAsync(
                UiElementInfo expected,
                IReadOnlyList<CandidateScore> candidates,
                string? platform = null,
                CancellationToken cancellationToken = default) =>
                Task.FromResult(new LlmHealingResult
                {
                    ProviderName = Name,
                    Success = false,
                    ErrorMessage = _error,
                });
        }

        private sealed class MockAblationProvider : ILlmHealingProvider
        {
            private readonly Func<UiElementInfo, IReadOnlyList<CandidateScore>, (string? CandidateId, double Confidence, string Reasoning)> _responder;

            public MockAblationProvider(string name, Func<UiElementInfo, IReadOnlyList<CandidateScore>, (string? CandidateId, double Confidence, string Reasoning)> responder)
            {
                Name = name;
                _responder = responder;
            }

            public string Name { get; }
            public bool IsAvailable => true;

            public Task<LlmHealingResult> ResolveAsync(
                UiElementInfo expected,
                IReadOnlyList<CandidateScore> candidates,
                string? platform = null,
                CancellationToken cancellationToken = default)
            {
                var (candId, conf, reasoning) = _responder(expected, candidates);
                var matched = candidates.FirstOrDefault(c => c.CandidateId == candId);
                return Task.FromResult(new LlmHealingResult
                {
                    ProviderName = Name,
                    Success = true,
                    MatchedCandidateId = candId,
                    MatchedAutomationId = matched?.Candidate.AutomationId,
                    Confidence = conf,
                    Reasoning = reasoning,
                    AttemptCount = 1,
                });
            }
        }

        private static AblationScenarioResult Result(LocatorMutationKind kind, AblationOutcome outcome) =>
            new AblationScenarioResult { MutationKind = kind, Outcome = outcome };

        private static UiElementInfo SmallTree()
        {
            var root = new UiElementInfo
            {
                ControlType = "Window",
                Name = "Stub",
                AutomationId = "MainWindow",
                BoundingRectangle = new BoundingRectangle(0, 0, 800, 600),
            };

            var save = new UiElementInfo
            {
                ControlType = "Button",
                Name = "Save",
                AutomationId = "btnSave",
                BoundingRectangle = new BoundingRectangle(10, 10, 80, 30),
            };
            save.Children.Add(new UiElementInfo
            {
                ControlType = "Image",
                Name = "Save icon",
                BoundingRectangle = new BoundingRectangle(12, 12, 16, 16),
            });

            root.Children.Add(save);
            root.Children.Add(new UiElementInfo
            {
                ControlType = "Text",
                Name = "Title",
                AutomationId = "lblTitle",
                BoundingRectangle = new BoundingRectangle(10, 60, 200, 20),
            });

            return root;
        }

        private static UiElementInfo LoadFixture()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", FixtureFileName);
            Assert.True(File.Exists(path), $"Ablation fixture not found at '{path}'.");
            var tree = UiTreeSerializer.FromJson(File.ReadAllText(path));
            Assert.NotNull(tree);
            return tree!;
        }

        private static System.Collections.Generic.IEnumerable<UiElementInfo> Flatten(UiElementInfo root)
        {
            yield return root;
            foreach (var child in root.Children)
            {
                foreach (var descendant in Flatten(child))
                {
                    yield return descendant;
                }
            }
        }
    }
}
