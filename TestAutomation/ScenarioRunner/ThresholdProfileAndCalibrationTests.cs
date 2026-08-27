using System;
using System.IO;
using System.Linq;
using SelfHealing;
using UiModel;
using Xunit;

namespace ScenarioRunner
{
    public class ThresholdProfileAndCalibrationTests
    {
        [Fact]
        public void ProfilePresets_ConfigureExpectedThresholds()
        {
            var balanced = SimilarityWeights.Balanced;
            Assert.Equal(0.75, balanced.MinimumConfidence);
            Assert.Equal(0.05, balanced.MinimumCandidateMargin);
            Assert.Equal(0.40, balanced.MinimumEvidenceWeight);
            balanced.Validate();

            var conservative = SimilarityWeights.Conservative;
            Assert.Equal(0.90, conservative.MinimumConfidence);
            Assert.Equal(0.08, conservative.MinimumCandidateMargin);
            Assert.Equal(0.50, conservative.MinimumEvidenceWeight);
            conservative.Validate();

            var aggressive = SimilarityWeights.Aggressive;
            Assert.Equal(0.50, aggressive.MinimumConfidence);
            Assert.Equal(0.03, aggressive.MinimumCandidateMargin);
            Assert.Equal(0.30, aggressive.MinimumEvidenceWeight);
            aggressive.Validate();
        }

        [Fact]
        public void SelfHealingEngine_CreateWithProfile_AppliesPresetWeights()
        {
            var engine = SelfHealingEngine.Create(ThresholdProfile.Conservative);
            Assert.Equal(0.90, engine.Weights.MinimumConfidence);
            Assert.Equal(0.08, engine.Weights.MinimumCandidateMargin);
            Assert.Equal(0.50, engine.Weights.MinimumEvidenceWeight);
        }

        [Fact]
        public void TreeCalibrator_ProducesActionableReport_OnSyntheticTree()
        {
            var root = new UiElementInfo
            {
                ControlType = "Window",
                AutomationId = "MainWindow",
                Name = "Main Window",
                BoundingRectangle = new BoundingRectangle(0, 0, 800, 600)
            };

            for (var i = 0; i < 5; i++)
            {
                root.Children.Add(new UiElementInfo
                {
                    ControlType = "Button",
                    AutomationId = $"btnAction{i}",
                    Name = $"Action Button {i}",
                    ParentControlType = "Window",
                    ParentAutomationId = "MainWindow",
                    BoundingRectangle = new BoundingRectangle(10 + (i * 100), 50, 80, 30),
                    SiblingIndex = i,
                    SiblingCount = 5
                });
            }

            var report = TreeCalibrator.Calibrate(root, "SyntheticApp");

            Assert.NotNull(report);
            Assert.Equal("SyntheticApp", report.ApplicationName);
            Assert.Equal(6, report.TotalTreeElements);
            Assert.Equal(5, report.ProbedElementsCount);
            Assert.True(report.TotalScenariosEvaluated > 0);
            Assert.Equal(3, report.ProfileResults.Count);

            var md = report.ToMarkdownReport();
            Assert.Contains("UI Tree Calibration Report: SyntheticApp", md);
            Assert.Contains("Recommended Profile", md);
            Assert.Contains("Profile Performance Comparison", md);
            Assert.Contains("SelfHealingEngine.Create(ThresholdProfile.", md);
        }

        [Fact]
        public void HandBrakeFixture_ConservativeProfile_ReducesFalseHealsOnRemovedControls()
        {
            var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "HandBrake_1.8.2.tree.json");
            Assert.True(File.Exists(fixturePath), $"Fixture missing at '{fixturePath}'.");

            var tree = UiTreeSerializer.FromJson(File.ReadAllText(fixturePath));
            Assert.NotNull(tree);

            var dataset = LocatorAblationGenerator.Generate(tree!, "HandBrake", "1.8.2", "HandBrake_1.8.2.tree.json");

            var defaultReport = LocatorAblationHarness.Run(dataset, tree!, SimilarityWeights.Default);
            var conservativeReport = LocatorAblationHarness.Run(dataset, tree!, SimilarityWeights.Conservative);

            // Conservative profile (0.90 confidence) substantially reduces false heals on removed controls
            Assert.True(conservativeReport.Metrics.FalseHealsOnRemoved < defaultReport.Metrics.FalseHealsOnRemoved,
                $"Expected Conservative ({conservativeReport.Metrics.FalseHealsOnRemoved}) to have fewer false heals on removed controls than Default ({defaultReport.Metrics.FalseHealsOnRemoved}).");

            // Precision is higher under Conservative
            Assert.True(conservativeReport.Metrics.Precision >= defaultReport.Metrics.Precision);
        }
    }
}
