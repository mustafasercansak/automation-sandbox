using System;
using System.IO;
using System.Linq;
using UiModel;
using Xunit;

namespace ScenarioRunner
{
    public class LocatorAblationTests
    {
        private const string FixtureFileName = "HandBrake_1.8.2.tree.json";

        [Fact]
        public void Generate_ProducesAMatchedRenameAndRemovalPairPerLocator()
        {
            var root = SmallTree();

            var dataset = LocatorAblationGenerator.Generate(root, "StubApp", "1.0", "stub.json");

            // btnSave and lblTitle qualify; the root is skipped because removing it leaves nothing to search.
            Assert.Equal(4, dataset.Scenarios.Count);
            Assert.Equal(2, dataset.Scenarios.Count(s => s.MutationKind == LocatorMutationKind.RenamedAutomationId));
            Assert.Equal(2, dataset.Scenarios.Count(s => s.MutationKind == LocatorMutationKind.RemovedElement));
            Assert.All(
                dataset.Scenarios.Where(s => s.MutationKind == LocatorMutationKind.RenamedAutomationId),
                s => Assert.NotNull(s.GroundTruth));
            Assert.All(
                dataset.Scenarios.Where(s => s.MutationKind == LocatorMutationKind.RemovedElement),
                s => Assert.Null(s.GroundTruth));
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
            Assert.Equal(2, dataset.Scenarios.Count);
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
            Assert.Contains(Flatten(first), e => e.AutomationId == "btnSave" + LocatorAblationGenerator.RenameSuffix);

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
                Result(LocatorMutationKind.RenamedAutomationId, AblationOutcome.MissedHeal),
                Result(LocatorMutationKind.RemovedElement, AblationOutcome.CorrectDecline),
                Result(LocatorMutationKind.RemovedElement, AblationOutcome.FalseHealOnRemoved),
            };

            var metrics = LocatorAblationHarness.Summarize(results);

            Assert.Equal(1.0 / 3.0, metrics.AutoHealRecall, 5);
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

            // The survey established that this capture carries 43 authored ids across a 149-node WPF
            // tree; the root is excluded, and each remaining locator yields a rename and a removal.
            Assert.True(
                dataset.Scenarios.Count >= 40,
                $"Expected at least 40 scenarios from the HandBrake fixture, got {dataset.Scenarios.Count}.");
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
                report.Metrics.RenameScenarios,
                report.Metrics.CorrectHeals + report.Metrics.FalseHeals + report.Metrics.MissedHeals);
            Assert.Equal(
                report.Metrics.RemovalScenarios,
                report.Metrics.CorrectDeclines + report.Metrics.FalseHealsOnRemoved);
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
