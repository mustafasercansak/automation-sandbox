using UiModel;
using SelfHealing;

namespace ScenarioRunner
{
    public class SelfHealingResolverExplainabilityTests
    {
        [Fact]
        public void Resolve_ScoreBreakdown_ReflectsPerComponentScores()
        {
            var expected = new UiElementInfo
            {
                ControlType = "Edit",
                Name = "Email",
                ParentControlType = "Window",
                SiblingIndex = 0,
                SiblingCount = 2,
                BoundingRectangle = new BoundingRectangle(100, 100, 50, 20),
            };

            var root = new UiElementInfo { ControlType = "Window" };
            root.Children.Add(new UiElementInfo
            {
                ControlType = "Edit",
                AutomationId = "renamed",
                Name = "Zzzzz", // deliberately different name - everything else matches exactly
                ParentControlType = "Window",
                SiblingIndex = 0,
                SiblingCount = 2,
                BoundingRectangle = new BoundingRectangle(100, 100, 50, 20),
            });

            var result = SelfHealingResolver.Resolve(expected, root, log: _ => { });

            Assert.NotNull(result.ScoreBreakdown);
            Assert.Equal(1.0, result.ScoreBreakdown!.ControlTypeScore);
            Assert.Equal(1.0, result.ScoreBreakdown.ParentControlTypeScore);
            Assert.Equal(1.0, result.ScoreBreakdown.SiblingPositionScore);
            Assert.Equal(1.0, result.ScoreBreakdown.PositionScore);
            Assert.True(result.ScoreBreakdown.NameScore < 1.0, "Name score should reflect the mismatched Name.");
        }

        [Fact]
        public void Resolve_ScoreBreakdown_PositionScoreIsNull_WhenBoundingRectangleUnusable()
        {
            var expected = new UiElementInfo
            {
                ControlType = "Edit",
                Name = "Email",
                ParentControlType = "Window",
                SiblingIndex = 0,
                SiblingCount = 1,
                BoundingRectangle = new BoundingRectangle(0, 0, 0, 0),
            };

            var root = new UiElementInfo { ControlType = "Window" };
            root.Children.Add(new UiElementInfo
            {
                ControlType = "Edit",
                AutomationId = "renamed",
                Name = "Email",
                ParentControlType = "Window",
                SiblingIndex = 0,
                SiblingCount = 1,
                BoundingRectangle = new BoundingRectangle(0, 0, 0, 0),
            });

            var result = SelfHealingResolver.Resolve(expected, root, log: _ => { });

            Assert.NotNull(result.ScoreBreakdown);
            Assert.Null(result.ScoreBreakdown!.PositionScore);
            // Position is excluded from the weighted average entirely (not penalized to 0) -
            // the remaining components all match exactly, so the total should still be 1.0.
            Assert.Equal(1.0, result.Score);
        }

        [Fact]
        public void Resolve_ExcludesNearZeroCandidates_FromCandidateCount()
        {
            var expected = new UiElementInfo
            {
                ControlType = "Edit",
                Name = "Email",
                ParentControlType = "Window",
                SiblingIndex = 0,
                SiblingCount = 2,
                BoundingRectangle = new BoundingRectangle(100, 100, 50, 20),
            };

            // Root's SiblingIndex/SiblingCount are explicitly set to non-matching values -
            // Flatten() includes the root itself as a scoreable candidate, and its untouched
            // default (0, 0) would otherwise coincidentally match expected's (0, 2) on the
            // sibling-position signal alone, keeping it above MinCandidateScore too.
            var root = new UiElementInfo { ControlType = "Window", SiblingIndex = 999, SiblingCount = 1 };
            root.Children.Add(new UiElementInfo
            {
                ControlType = "Edit",
                AutomationId = "goodMatch",
                Name = "Email",
                ParentControlType = "Window",
                SiblingIndex = 0,
                SiblingCount = 2,
                BoundingRectangle = new BoundingRectangle(100, 100, 50, 20),
            });
            // Mismatched on every signal (type, parent, sibling position, name, and far outside
            // the position tolerance radius) - should score 0 and be pruned below
            // MinCandidateScore (0.05), not just rank last.
            root.Children.Add(new UiElementInfo
            {
                ControlType = "Button",
                AutomationId = "badMatch",
                Name = "CompletelyUnrelatedLongName",
                ParentControlType = "SomeOtherPanel",
                SiblingIndex = 1000,
                SiblingCount = 1000,
                BoundingRectangle = new BoundingRectangle(100000, 100000, 50, 20),
            });

            var result = SelfHealingResolver.Resolve(expected, root, log: _ => { });

            Assert.Equal("goodMatch", result.Matched!.AutomationId);
            Assert.Equal(1, result.CandidateCount);
        }
    }
}
