using System.Collections.Generic;
using System.Linq;
using AutomationSandbox.SelfHealing;
using AutomationSandbox.SelfHealingCli;
using AutomationSandbox.UiModel;
using Xunit;

namespace ScenarioRunner
{
    // #668: the compact result is shared by `automation-sandbox-heal --compact` and the MCP heal_locator tool. It must keep the decision exactly
    // and shrink only what makes the answer as large as the page.
    public class HealResultCompactorTests
    {
        // A page-sized tree: one window with many sibling fields. Every node is scored, so the full result grows with it.
        internal static UiElementInfo BuildLargeTree(int fieldCount)
        {
            var root = new UiElementInfo
            {
                ControlType = "Window",
                Name = "Main",
                AutomationId = "MainForm",
                BoundingRectangle = new BoundingRectangle(0, 0, 2000, 2000),
                ParentControlType = "",
            };
            for (var i = 0; i < fieldCount; i++)
            {
                root.Children.Add(new UiElementInfo
                {
                    ControlType = "Edit",
                    Name = "Field " + i,
                    AutomationId = "field" + i,
                    BoundingRectangle = new BoundingRectangle(10 + i, 20 + i, 200, 23),
                    ParentControlType = "Window",
                    ParentAutomationId = "MainForm",
                    SiblingIndex = i,
                    SiblingCount = fieldCount,
                    Children = new List<UiElementInfo> { new UiElementInfo { ControlType = "Text", Name = "label " + i, ParentControlType = "Edit" } },
                });
            }

            return root;
        }

        internal static UiElementInfo StaleEdit() => new UiElementInfo
        {
            ControlType = "Edit",
            Name = "Field 7",
            AutomationId = "txtEmail",
            BoundingRectangle = new BoundingRectangle(17, 27, 200, 23),
            ParentControlType = "Window",
            SiblingIndex = 7,
            SiblingCount = 120,
        };

        private static HealResult Resolve(UiElementInfo tree) =>
            SelfHealingResolver.Resolve(StaleEdit(), tree, SimilarityWeights.Balanced, log: _ => { });

        [Fact]
        public void Compact_KeepsTheDecisionExactly()
        {
            var tree = BuildLargeTree(120);
            var result = Resolve(tree);
            var confident = result.IsConfident;
            var score = result.Score;
            var status = result.ResolutionStatus;
            var runnerUp = result.RunnerUpScore;
            var matchedName = result.Matched?.Name;

            HealResultCompactor.Compact(result);

            Assert.Equal(confident, result.IsConfident);
            Assert.Equal(score, result.Score);
            Assert.Equal(status, result.ResolutionStatus);
            Assert.Equal(runnerUp, result.RunnerUpScore);
            Assert.Equal(matchedName, result.Matched?.Name);
        }

        [Fact]
        public void Compact_ListsOnlyTheBestFiveCandidates_AndStillReportsHowManyWereScored()
        {
            var result = Resolve(BuildLargeTree(120));
            var scored = result.CandidateCount;
            var bestFive = result.Candidates!.OrderByDescending(c => c.TotalScore).Take(HealResultCompactor.CandidateLimit).Select(c => c.TotalScore).ToList();
            Assert.True(result.Candidates!.Count > HealResultCompactor.CandidateLimit, "The precondition: more candidates than the limit.");

            HealResultCompactor.Compact(result);

            Assert.Equal(scored, result.CandidateCount);
            Assert.Equal(bestFive, result.Candidates!.Select(c => c.TotalScore).ToList());
        }

        [Fact]
        public void Compact_DropsTheSubtreeOfEveryElement_ButNotFromTheLiveTree()
        {
            var tree = BuildLargeTree(120);
            var result = Resolve(tree);
            Assert.NotEmpty(result.Matched!.Children);

            HealResultCompactor.Compact(result);

            Assert.Empty(result.Matched!.Children);
            Assert.All(result.Candidates!, c => Assert.Empty(c.Candidate.Children));
            // The resolver was handed this tree: compacting the result must not edit it.
            Assert.Equal(120, tree.Children.Count);
            Assert.All(tree.Children, child => Assert.Single(child.Children));
        }
    }
}
