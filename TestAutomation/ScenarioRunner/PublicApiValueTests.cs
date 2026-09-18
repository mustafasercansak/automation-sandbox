using System.Text.Json;
using AutomationSandbox.IntentAutomation;
using AutomationSandbox.UiModel;
using AutomationSandbox.WebDiscovery;

namespace ScenarioRunner
{
    public sealed class PublicApiValueTests
    {
        [Fact]
        public void ScoreComponents_PreserveMissingEvidenceThroughJson()
        {
            var original = new ScoreComponents(controlTypeScore: 1.0, nameScore: 0.0);

            var restored = JsonSerializer.Deserialize<ScoreComponents>(JsonSerializer.Serialize(original))!;

            Assert.Equal(1.0, restored.ControlTypeScore);
            Assert.Equal(0.0, restored.NameScore);
            Assert.Null(restored.ParentControlTypeScore);
            Assert.Null(restored.SiblingPositionScore);
            Assert.Null(restored.PositionScore);
        }

        [Fact]
        public void Candidate_KeepsItsLocatorSuggestionsWhenTheInputListChanges()
        {
            var suggestion = new PlaywrightLocatorSuggestion(
                strategy: "TestId", expression: "page.GetByTestId(\"save\")", confidence: 0.98);
            var suggestions = new List<PlaywrightLocatorSuggestion> { suggestion };
            var candidate = new IntentElementCandidate(locatorSuggestions: suggestions);

            suggestions.Clear();

            Assert.Same(suggestion, Assert.Single(candidate.LocatorSuggestions));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<PlaywrightLocatorSuggestion>)candidate.LocatorSuggestions).Clear());
        }

        [Fact]
        public void Candidate_JsonRoundTripPreservesTheProposedLocatorAndScores()
        {
            var candidate = new IntentElementCandidate(
                step: new IntentStep { TargetDescription = "save" },
                element: new WebElementInfo { TestId = "save" },
                score: 0.8,
                semanticScore: 0.7,
                locatorSuggestions: new[] { new PlaywrightLocatorSuggestion(expression: "page.GetByTestId(\"save\")") });

            var restored = JsonSerializer.Deserialize<IntentElementCandidate>(JsonSerializer.Serialize(candidate))!;

            Assert.Equal("save", restored.Step.TargetDescription);
            Assert.Equal("save", restored.Element.TestId);
            Assert.Equal(0.8, restored.Score);
            Assert.Equal(0.7, restored.SemanticScore);
            Assert.Equal("page.GetByTestId(\"save\")", Assert.Single(restored.LocatorSuggestions).Expression);
        }
    }
}
