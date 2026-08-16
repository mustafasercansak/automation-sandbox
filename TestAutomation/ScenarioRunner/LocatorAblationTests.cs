using System;
using System.IO;
using System.Linq;
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
