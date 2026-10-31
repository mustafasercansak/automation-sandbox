using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using AutomationSandbox.LlmHealing;

namespace ScenarioRunner
{
    public class TextModelOllamaTests
    {
        private const string Answer = "{\"choices\":[{\"message\":{\"content\":\"hello\"}}]}";

        private static Func<string, string?> Env(params (string, string?)[] values)
        {
            var map = values.ToDictionary(v => v.Item1, v => v.Item2);
            return key => map.TryGetValue(key, out var value) ? value : null;
        }

        private static async Task<(TextCompletionResult Result, ProjectBoardReaderTests.StubHandler Handler)> Ask(ITextModel model, ProjectBoardReaderTests.StubHandler handler)
        {
            return (await model.CompleteAsync(new TextCompletionRequest("hi")), handler);
        }

        [Fact]
        public async Task OllamaCloud_NeedsItsKeyAndItsModel_AndUsesTheHostedEndpointWithTheKey()
        {
            var handler = new ProjectBoardReaderTests.StubHandler(ProjectBoardReaderTests.Reply(Answer));
            var registry = TextModelRegistry.CreateFromEnvironment(new HttpClient(handler), Env(("OLLAMA_CLOUD_API_KEY", "cloud-key-ccc"), ("OLLAMA_CLOUD_MODEL", "gemma4:31b")));

            var model = Assert.Single(registry.Models);
            var (result, _) = await Ask(model, handler);

            Assert.Equal("OllamaCloud", model.Name);
            Assert.True(result.Success, result.ErrorMessage);
            var request = Assert.Single(handler.Requests);
            Assert.Equal("https://ollama.com/v1/chat/completions", request.Url);
            Assert.Equal("Bearer cloud-key-ccc", request.Auth);
            using var body = JsonDocument.Parse(request.Body);
            Assert.Equal("gemma4:31b", body.RootElement.GetProperty("model").GetString());
        }

        [Theory]
        [InlineData("key-only")]
        [InlineData("model-only")]
        public void OllamaCloud_WithOnlyOneOfTheTwoVariables_IsLeftOut(string which)
        {
            var env = which == "key-only" ? Env(("OLLAMA_CLOUD_API_KEY", "k")) : Env(("OLLAMA_CLOUD_MODEL", "m"));

            Assert.Empty(TextModelRegistry.CreateFromEnvironment(getEnv: env).Models);
        }

        [Fact]
        public async Task ALocalOllama_NeedsOnlyAModel_AndSendsNoAuthorizationHeader()
        {
            var handler = new ProjectBoardReaderTests.StubHandler(ProjectBoardReaderTests.Reply(Answer));
            var registry = TextModelRegistry.CreateFromEnvironment(new HttpClient(handler), Env(("OLLAMA_MODEL", "llama-local")));

            var model = Assert.Single(registry.Models);
            Assert.Equal("Ollama", model.Name);
            Assert.True(model.IsAvailable);
            var (result, _) = await Ask(model, handler);

            Assert.True(result.Success, result.ErrorMessage);
            var request = Assert.Single(handler.Requests);
            Assert.Equal("http://localhost:11434/v1/chat/completions", request.Url);
            Assert.Null(request.Auth);
        }

        [Theory]
        [InlineData("http://127.0.0.1:12345", "http://127.0.0.1:12345/v1/chat/completions")]
        [InlineData("http://localhost:11434/", "http://localhost:11434/v1/chat/completions")]
        [InlineData("https://ollama.example.test", "https://ollama.example.test/v1/chat/completions")]
        public async Task TheLocalHostIsConfigurable(string host, string expectedUrl)
        {
            var handler = new ProjectBoardReaderTests.StubHandler(ProjectBoardReaderTests.Reply(Answer));
            var registry = TextModelRegistry.CreateFromEnvironment(new HttpClient(handler), Env(("OLLAMA_MODEL", "m"), ("OLLAMA_HOST", host)));

            await Ask(Assert.Single(registry.Models), handler);

            Assert.Equal(expectedUrl, handler.Requests[0].Url);
        }

        [Theory]
        [InlineData("http://ollama.example.test:11434")]
        [InlineData("not a url")]
        [InlineData("ftp://ollama.example.test")]
        public void ALocalHostThatCouldSendInClearTextAcrossANetwork_OrIsNotAUrl_LeavesTheModelOut(string host)
        {
            var registry = TextModelRegistry.CreateFromEnvironment(getEnv: Env(("OLLAMA_MODEL", "m"), ("OLLAMA_HOST", host)));

            Assert.Empty(registry.Models);
        }

        [Fact]
        public void AHostWithoutAModel_IsLeftOut_AndTheTwoOllamaSetsStayApart()
        {
            Assert.Empty(TextModelRegistry.CreateFromEnvironment(getEnv: Env(("OLLAMA_HOST", "http://localhost:11434"))).Models);
            // A cloud model name never builds a local model, and a local model name never builds a cloud one.
            Assert.Equal(new[] { "OllamaCloud" }, TextModelRegistry.CreateFromEnvironment(getEnv: Env(("OLLAMA_CLOUD_API_KEY", "k"), ("OLLAMA_CLOUD_MODEL", "m"))).Models.Select(m => m.Name));
            Assert.Equal(new[] { "Ollama" }, TextModelRegistry.CreateFromEnvironment(getEnv: Env(("OLLAMA_MODEL", "m"))).Models.Select(m => m.Name));
            Assert.Equal(new[] { "OllamaCloud", "Ollama" }, TextModelRegistry.CreateFromEnvironment(getEnv: Env(("OLLAMA_CLOUD_API_KEY", "k"), ("OLLAMA_CLOUD_MODEL", "m"), ("OLLAMA_MODEL", "m2"))).Models.Select(m => m.Name));
        }

        [Fact]
        public void EveryOtherModelStillNeedsItsKey()
        {
            var keyless = new ChatCompletionsTextModel("Example", null, "m", "https://api.example.test/v1");
            var withKey = new ChatCompletionsTextModel("Example", "k", "m", "https://api.example.test/v1");
            var optOut = new ChatCompletionsTextModel("Local", null, "m", "http://localhost:1/v1", requireApiKey: false);

            Assert.False(keyless.IsAvailable);
            Assert.True(withKey.IsAvailable);
            Assert.True(optOut.IsAvailable);
        }

        [Fact]
        public async Task ARoutingLabelCanNameTheOllamaModels()
        {
            var registry = TextModelRegistry.CreateFromEnvironment(getEnv: Env(("OLLAMA_CLOUD_API_KEY", "k"), ("OLLAMA_CLOUD_MODEL", "m"), ("OLLAMA_MODEL", "m2")));

            var result = TextModelRouter.Route(TextTaskKinds.IssueTriage, new[] { "Model::OllamaCloud", "Review::Ollama" }, registry, TextModelRoutingConfig.Empty);

            Assert.True(result.Success, string.Join("; ", result.Problems));
            Assert.Equal("OllamaCloud", result.Drafter!.Name);
            Assert.Equal("Ollama", result.Reviewer!.Name);
            await Task.CompletedTask;
        }
    }
}
