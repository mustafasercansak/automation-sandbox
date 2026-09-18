using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AutomationSandbox.SelfHealing;
using AutomationSandbox.UiModel;
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
        public void TreeCalibrator_DuplicateAutomationIds_ProbesAndRemovesTheCorrectSibling()
        {
            // Regression test: TreeCalibrator used to match probe targets in a cloned tree by
            // AutomationId/attribute value, so two elements sharing an AutomationId (the exact
            // scenario self-healing exists for) made every probe for the second duplicate mutate
            // or remove the FIRST duplicate instead. For a "Removed" probe this left the intended
            // target untouched in the mutated tree, so the resolver found it unchanged, accepted
            // it, and the probe was wrongly recorded as a false heal on a removed control - even
            // though nothing was actually removed.
            var root = new UiElementInfo
            {
                ControlType = "Window",
                AutomationId = "MainWindow",
                Name = "Main Window",
                BoundingRectangle = new BoundingRectangle(0, 0, 800, 600)
            };

            root.Children.Add(new UiElementInfo
            {
                ControlType = "Button",
                AutomationId = "dupBtn",
                Name = "First",
                ParentControlType = "Window",
                ParentAutomationId = "MainWindow",
                BoundingRectangle = new BoundingRectangle(10, 50, 80, 30),
                SiblingIndex = 0,
                SiblingCount = 2
            });
            root.Children.Add(new UiElementInfo
            {
                ControlType = "Button",
                AutomationId = "dupBtn",
                Name = "Second",
                ParentControlType = "Window",
                ParentAutomationId = "MainWindow",
                BoundingRectangle = new BoundingRectangle(500, 400, 80, 30),
                SiblingIndex = 1,
                SiblingCount = 2
            });

            var report = TreeCalibrator.Calibrate(root, "DuplicateIdApp");

            // One removal probe per duplicate sibling.
            var aggressive = report.ProfileResults.Single(r => r.Profile == ThresholdProfile.Aggressive);
            Assert.Equal(2, aggressive.RemovalScenarios);

            // Each duplicate's own removal probe must remove that specific sibling, not always
            // the first one - so both are correctly declined and neither is falsely healed as
            // "still present" under even the most permissive (Aggressive) profile.
            Assert.Equal(0, aggressive.FalseHealsOnRemoved);
            Assert.Equal(2, aggressive.CorrectDeclines);
        }

        [Fact]
        public void GenerateProbes_PopulatesChildControlTypeSignature_ForContainerElement()
        {
            // Regression test for #418: TreeCalibrator.CloneElementShallow used to omit
            // ChildControlTypeSignature, unlike UiElementSnapshot.Capture which always computes
            // it. GenerateProbes builds every CalibrationProbe.ExpectedTarget via
            // CloneElementShallow, so expected.ChildControlTypeSignature was always null during
            // calibration - making expectedWasContainer false in SelfHealingResolver.Resolve and
            // silently disabling the child-signature false-heal gate for every calibration run.
            var root = new UiElementInfo
            {
                ControlType = "Window",
                AutomationId = "MainWindow",
                Name = "Main Window",
                BoundingRectangle = new BoundingRectangle(0, 0, 800, 600)
            };

            var container = new UiElementInfo
            {
                ControlType = "Panel",
                AutomationId = "containerPanel",
                Name = "Container Panel",
                ParentControlType = "Window",
                ParentAutomationId = "MainWindow",
                BoundingRectangle = new BoundingRectangle(10, 50, 200, 100),
                SiblingIndex = 0,
                SiblingCount = 1
            };
            container.Children.Add(new UiElementInfo { ControlType = "Button", Name = "OK" });
            container.Children.Add(new UiElementInfo { ControlType = "Button", Name = "Cancel" });
            container.Children.Add(new UiElementInfo { ControlType = "Edit", Name = "Input" });
            root.Children.Add(container);

            var expectedSignature = UiElementSnapshot.ComputeChildControlTypeSignature(container);
            Assert.Equal("Button:2|Edit:1", expectedSignature);

            // GenerateProbes/CalibrationProbe are private implementation details of
            // TreeCalibrator; reflection is used here deliberately because the acceptance
            // criterion for #418 is specifically that CalibrationProbe.ExpectedTarget carries the
            // signature, not merely that some public aggregate metric shifts.
            var generateProbes = typeof(TreeCalibrator).GetMethod(
                "GenerateProbes", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(generateProbes);

            var probes = (System.Collections.IEnumerable)generateProbes!.Invoke(
                null, new object[] { root, new List<UiElementInfo> { container } })!;

            var probeList = probes.Cast<object>().ToList();
            Assert.NotEmpty(probeList);

            var expectedTargetProperty = probeList[0].GetType().GetProperty("ExpectedTarget");
            Assert.NotNull(expectedTargetProperty);

            foreach (var probe in probeList)
            {
                var expectedTarget = (UiElementInfo)expectedTargetProperty!.GetValue(probe)!;
                Assert.Equal(expectedSignature, expectedTarget.ChildControlTypeSignature);
            }
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

        [Fact]
        public void HandBrakeFixture_BalancedProfile_FalseHealRate_MeetsThe1_0SafetyBound()
        {
            // versioning-and-stability.md §3, "Benchmark Safety Bound": the 1.0 GA checklist
            // requires the heuristic false-heal rate on the HandBrake 1.8.2 tree to stay at or
            // below 10% under ThresholdProfile.Balanced. This locks that criterion so a scorer
            // or profile change cannot regress past it unnoticed.
            var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "HandBrake_1.8.2.tree.json");
            Assert.True(File.Exists(fixturePath), $"Fixture missing at '{fixturePath}'.");

            var tree = UiTreeSerializer.FromJson(File.ReadAllText(fixturePath));
            Assert.NotNull(tree);

            var report = TreeCalibrator.Calibrate(tree!, "HandBrake");
            var balanced = report.ProfileResults.Single(r => r.Profile == ThresholdProfile.Balanced);

            Assert.True(balanced.FalseHealRate <= 0.10,
                $"HandBrake Balanced false-heal rate {balanced.FalseHealRate:P1} exceeds the 1.0 GA safety bound of 10%.");
        }
    }
}
