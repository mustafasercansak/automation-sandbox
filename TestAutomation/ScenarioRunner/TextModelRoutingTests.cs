using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutomationSandbox.LlmHealing;

namespace ScenarioRunner
{
    // Routing only reads labels, names and configuration. No model is ever called: the fake throws if one is.
    public class TextModelRoutingTests
    {
        private sealed class FakeModel : ITextModel
        {
            public FakeModel(string name, bool available = true)
            {
                Name = name;
                IsAvailable = available;
            }

            public string Name { get; }

            public bool IsAvailable { get; }

            public Task<TextCompletionResult> CompleteAsync(TextCompletionRequest request, CancellationToken cancellationToken = default)
            {
                throw new InvalidOperationException("Routing must not call a model.");
            }
        }

        private static TextModelRegistry Registry(params ITextModel[] models)
        {
            var registry = new TextModelRegistry();
            foreach (var model in models)
            {
                registry.Add(model);
            }

            return registry;
        }

        private static TextModelRegistry Default() => Registry(new FakeModel("Gemini"), new FakeModel("Mistral"), new FakeModel("Claude"), new FakeModel("Offline", available: false));

        private const string Config = @"{
          // which task kinds each model may serve
          ""models"": {
            ""Gemini"":  { ""tasks"": [""issue-triage"", ""docs""] },
            ""Mistral"": { ""tasks"": [""docs"", ""ci-failure""] },
            ""Claude"":  { ""tasks"": [""docs""], },
            ""Offline"": { ""tasks"": [""docs""] }
          },
          ""defaults"": {
            ""issue-triage"": { ""model"": ""Gemini"" },
            ""docs"":         { ""model"": ""Gemini"", ""reviewer"": ""Mistral"" },
          }
        }";

        [Fact]
        public void ALabelNamesTheDraftingModel_IgnoringCase()
        {
            var result = TextModelRouter.Route("docs", new[] { "bug", "model::cLAUDE" }, Default(), TextModelRoutingConfig.Parse(Config));

            Assert.True(result.Success, string.Join("; ", result.Problems));
            Assert.Equal("Claude", result.Drafter!.Name);
            Assert.Equal("Mistral", result.Reviewer!.Name);
        }

        [Fact]
        public void WithoutALabel_TheDefaultForTheTaskKindIsUsed_ReviewerIncluded()
        {
            var result = TextModelRouter.Route("docs", new string[0], Default(), TextModelRoutingConfig.Parse(Config));

            Assert.True(result.Success, string.Join("; ", result.Problems));
            Assert.Equal("Gemini", result.Drafter!.Name);
            Assert.Equal("Mistral", result.Reviewer!.Name);
        }

        [Fact]
        public void ALabelOverridesTheDefault_AndAReviewLabelOverridesTheDefaultReviewer()
        {
            var result = TextModelRouter.Route("docs", new[] { "Model::Mistral", "Review::Claude" }, Default(), TextModelRoutingConfig.Parse(Config));

            Assert.True(result.Success, string.Join("; ", result.Problems));
            Assert.Equal("Mistral", result.Drafter!.Name);
            Assert.Equal("Claude", result.Reviewer!.Name);
        }

        [Fact]
        public void ADefaultWithoutAReviewer_MeansNoReview()
        {
            var result = TextModelRouter.Route("issue-triage", new string[0], Default(), TextModelRoutingConfig.Parse(Config));

            Assert.True(result.Success, string.Join("; ", result.Problems));
            Assert.Equal("Gemini", result.Drafter!.Name);
            Assert.Null(result.Reviewer);
        }

        [Fact]
        public void WithAnEmptyConfiguration_NothingIsRestricted_ButALabelIsRequired()
        {
            var labelled = TextModelRouter.Route("ci-failure", new[] { "Model::Claude" }, Default(), TextModelRoutingConfig.Empty);
            var unlabelled = TextModelRouter.Route("ci-failure", new string[0], Default(), TextModelRoutingConfig.Empty);

            Assert.True(labelled.Success);
            Assert.Equal("Claude", labelled.Drafter!.Name);
            Assert.False(unlabelled.Success);
            Assert.Contains("No Model::<name> label", Assert.Single(unlabelled.Problems));
        }

        [Theory]
        [InlineData("docs", new[] { "Model::Nobody" }, "not a configured model")]
        [InlineData("docs", new[] { "Model::Offline" }, "no API key")]
        [InlineData("issue-triage", new[] { "Model::Mistral" }, "not allowed to serve")]
        [InlineData("ci-failure", new string[0], "No Model::<name> label")]
        [InlineData("docs", new[] { "Model::Gemini", "Model::Mistral" }, "More than one Model::")]
        [InlineData("docs", new[] { "Review::Mistral", "Review::Claude" }, "More than one Review::")]
        [InlineData("docs", new[] { "Model::" }, "no model name")]
        [InlineData("docs", new[] { "Model::Gemini", "Review::Gemini" }, "different model")]
        [InlineData("docs", new[] { "Model::gemini", "Review::GEMINI" }, "different model")]
        [InlineData("docs", new[] { "Review::Nobody" }, "not a configured model")]
        [InlineData("docs", new[] { "Review::Offline" }, "no API key")]
        [InlineData("issue-triage", new[] { "Review::Mistral" }, "not allowed to serve")]
        [InlineData("brainstorm", new[] { "Model::Gemini" }, "Unknown task kind")]
        public void ALabelThatCannotBeHonoured_StopsTheTaskAndSaysWhy(string kind, string[] labels, string expected)
        {
            var result = TextModelRouter.Route(kind, labels, Default(), TextModelRoutingConfig.Parse(Config));

            Assert.False(result.Success);
            Assert.Null(result.Drafter);
            Assert.Null(result.Reviewer);
            Assert.Contains(result.Problems, problem => problem.Contains(expected));
        }

        [Fact]
        public void AModelTheConfigurationDoesNotList_IsRejectedWhenTheFileIsNotEmpty()
        {
            var registry = Registry(new FakeModel("Gemini"), new FakeModel("Mistral"), new FakeModel("Stranger"));

            var result = TextModelRouter.Route("docs", new[] { "Model::Stranger" }, registry, TextModelRoutingConfig.Parse(Config));

            Assert.False(result.Success);
            Assert.Contains("not listed in the routing configuration", Assert.Single(result.Problems));
        }

        [Fact]
        public void ADefaultReviewerThatMatchesTheChosenDrafter_StopsAndSaysHowToFixIt()
        {
            var result = TextModelRouter.Route("docs", new[] { "Model::Mistral" }, Default(), TextModelRoutingConfig.Parse(Config));

            Assert.False(result.Success);
            var problem = Assert.Single(result.Problems);
            Assert.Contains("comes from the default for \"docs\"", problem);
            Assert.Contains("Review::<name>", problem);
            Assert.True(TextModelRouter.Route("docs", new[] { "Model::Mistral", "Review::Claude" }, Default(), TextModelRoutingConfig.Parse(Config)).Success);
        }

        [Fact]
        public void RepeatingTheSameLabelIsNotAConflict()
        {
            var result = TextModelRouter.Route("docs", new[] { "Model::Gemini", "model::GEMINI" }, Default(), TextModelRoutingConfig.Parse(Config));

            Assert.True(result.Success, string.Join("; ", result.Problems));
        }

        [Fact]
        public void EveryProblemIsReported_NotJustTheFirst()
        {
            var result = TextModelRouter.Route("docs", new[] { "Model::Nobody", "Review::Offline" }, Default(), TextModelRoutingConfig.Parse(Config));

            Assert.Equal(2, result.Problems.Count);
        }

        [Fact]
        public void TheProblemListNamesTheConfiguredModels_ButNeverAKey()
        {
            var result = TextModelRouter.Route("docs", new[] { "Model::Nobody" }, Default(), TextModelRoutingConfig.Empty);

            var problem = Assert.Single(result.Problems);
            Assert.Contains("Gemini, Mistral, Claude, Offline", problem);
        }

        [Fact]
        public void ArgumentsAreValidated()
        {
            Assert.Throws<ArgumentNullException>(() => TextModelRouter.Route("docs", null!, Default(), TextModelRoutingConfig.Empty));
            Assert.Throws<ArgumentNullException>(() => TextModelRouter.Route("docs", new string[0], null!, TextModelRoutingConfig.Empty));
            Assert.Throws<ArgumentNullException>(() => TextModelRouter.Route("docs", new string[0], Default(), null!));
        }

        [Fact]
        public void TaskKinds_AreKnownIgnoringCase()
        {
            Assert.True(TextTaskKinds.IsKnown(" DOCS "));
            Assert.Equal("issue-triage", TextTaskKinds.Normalize("Issue-Triage"));
            Assert.False(TextTaskKinds.IsKnown("nope"));
            Assert.False(TextTaskKinds.IsKnown(null));
            Assert.Null(TextTaskKinds.Normalize(null));
        }
    }

    public class TextModelRoutingConfigTests
    {
        [Fact]
        public void AValidFile_WithCommentsAndTrailingCommas_IsRead()
        {
            var config = TextModelRoutingConfig.Parse("{ /* note */ \"models\": { \"A\": { \"tasks\": [\"docs\", \"DOCS\",], }, }, \"defaults\": { \"docs\": { \"model\": \"a\" }, } }");

            Assert.False(config.IsEmpty);
            Assert.True(config.Allows("a", "docs"));
            Assert.False(config.Allows("A", "ci-failure"));
            Assert.False(config.Allows("B", "docs"));
            Assert.Equal("a", config.DefaultFor("docs")!.Model);
            Assert.Null(config.DefaultFor("docs")!.Reviewer);
            Assert.Null(config.DefaultFor("ci-failure"));
        }

        [Fact]
        public void AnEmptyObject_IsAnEmptyConfiguration()
        {
            var config = TextModelRoutingConfig.Parse("{}");

            Assert.True(config.IsEmpty);
            Assert.True(config.Allows("anything", "docs"));
        }

        [Theory]
        [InlineData("", "empty")]
        [InlineData("   ", "empty")]
        [InlineData("not json", "not valid JSON")]
        [InlineData("[]", "must be a JSON object")]
        [InlineData("{\"models\": []}", "\"models\" must be an object")]
        [InlineData("{\"models\": {\"A\": {}}}", "needs a \"tasks\" array")]
        [InlineData("{\"models\": {\"A\": {\"tasks\": [\"cook\"]}}}", "unknown task kind")]
        [InlineData("{\"models\": {\"A\": {\"tasks\": [3]}}}", "unknown task kind")]
        [InlineData("{\"models\": {\"A\": {\"tasks\": []}, \"a\": {\"tasks\": []}}}", "listed twice")]
        [InlineData("{\"models\": {\" \": {\"tasks\": []}}}", "must not be empty")]
        [InlineData("{\"defaults\": []}", "\"defaults\" must be an object")]
        [InlineData("{\"defaults\": {\"cook\": {\"model\": \"A\"}}}", "unknown task kind")]
        [InlineData("{\"models\": {\"A\": {\"tasks\": [\"docs\"]}}, \"defaults\": {\"docs\": {}}}", "needs a \"model\"")]
        [InlineData("{\"defaults\": {\"docs\": {\"model\": \"A\"}}}", "not listed as allowed")]
        [InlineData("{\"models\": {\"A\": {\"tasks\": [\"ci-failure\"]}}, \"defaults\": {\"docs\": {\"model\": \"A\"}}}", "not listed as allowed")]
        [InlineData("{\"models\": {\"A\": {\"tasks\": [\"docs\"]}}, \"defaults\": {\"docs\": {\"model\": \"A\", \"reviewer\": \"B\"}}}", "default reviewer")]
        [InlineData("{\"models\": {\"A\": {\"tasks\": [\"docs\"]}}, \"defaults\": {\"docs\": {\"model\": \"A\", \"reviewer\": \"a\"}}}", "same model")]
        public void AnInvalidFile_IsRejectedWithAReason(string json, string expected)
        {
            var error = Assert.Throws<FormatException>(() => TextModelRoutingConfig.Parse(json));

            Assert.Contains(expected, error.Message);
        }
    }
}
