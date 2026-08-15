using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using LlmHealing;
using SelfHealing;
using UiModel;
using Xunit;

namespace ScenarioRunner
{
    public class ConsensusEvaluationTests
    {
        [Fact]
        public void AllEvaluationFixtures_MustBeHeuristicallyNonConfidentAndProduceCandidates()
        {
            var scenarios = EvaluationScenarios.All;
            Assert.NotEmpty(scenarios);

            foreach (var s in scenarios)
            {
                var heuristic = SelfHealingResolver.Resolve(s.Expected, s.CurrentTreeRoot);
                Assert.False(
                    heuristic.IsConfident,
                    $"Scenario '{s.Name}' must NOT heal heuristically so it tests LLM fallback (was confident with score {heuristic.Score:F2}).");

                var candidates = SelfHealingResolver.ScoreCandidates(s.Expected, s.CurrentTreeRoot, SimilarityWeights.Default);
                Assert.True(
                    candidates.Count >= 2,
                    $"Scenario '{s.Name}' must produce at least 2 scored candidates above MinCandidateScore for LLM evaluation (produced {candidates.Count}).");
            }
        }

        [Fact]
        public void LlmProviderFactory_DiscoversConfiguredProviders_AndHandlesMissingGracefully()
        {
            var env = new Dictionary<string, string>
            {
                ["GEMINI_API_KEY"] = "gemini-test-key",
                ["GROK_API_KEY"] = "grok-test-key",
                ["KIMI_API_KEY"] = "kimi-test-key",
            };

            var providers = LlmProviderFactory.CreateConfiguredProviders(getEnv: key => env.TryGetValue(key, out var val) ? val : null);

            Assert.Equal(3, providers.Count);
            Assert.Contains(providers, p => p.Name == "Gemini");
            Assert.Contains(providers, p => p.Name == "Grok");
            Assert.Contains(providers, p => p.Name == "Kimi");
        }

        [Fact]
        public void LlmProviderFactory_ThrowsOnDuplicateProviderNames()
        {
            var env = new Dictionary<string, string>
            {
                ["GEMINI_API_KEY"] = "gemini-test-key",
                ["LLM_CUSTOM_PROVIDERS"] = "[{\"Name\": \"Gemini\", \"ApiKey\": \"custom-key\", \"Endpoint\": \"https://example.com/v1\"}]",
            };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                LlmProviderFactory.CreateConfiguredProviders(getEnv: key => env.TryGetValue(key, out var val) ? val : null));

            Assert.Contains("Duplicate LLM provider names detected: Gemini", ex.Message);
        }

        [Fact]
        public void LlmProviderFactory_ParsesCustomProvidersJson()
        {
            var env = new Dictionary<string, string>
            {
                ["DEEPSEEK_SECRET"] = "deepseek-secret-value",
                ["LLM_CUSTOM_PROVIDERS"] = "[{\"Name\": \"DeepSeek\", \"ApiKeyEnvVar\": \"DEEPSEEK_SECRET\", \"Endpoint\": \"https://api.deepseek.com/v1\", \"Model\": \"deepseek-chat\", \"TimeoutSeconds\": 20, \"TotalTimeoutSeconds\": 50, \"MaxRetries\": 3}]",
            };

            var providers = LlmProviderFactory.CreateConfiguredProviders(getEnv: key => env.TryGetValue(key, out var val) ? val : null);

            Assert.Single(providers);
            var ds = Assert.IsAssignableFrom<HttpLlmHealingProvider>(providers[0]);
            Assert.Equal("DeepSeek", ds.Name);
            Assert.Equal(TimeSpan.FromSeconds(20), ds.Timeout);
            Assert.Equal(TimeSpan.FromSeconds(50), ds.TotalTimeout);
            Assert.Equal(3, ds.MaxRetries);
        }

        [Fact]
        public void ConsensusEvaluationDocument_SerializationAndMarkdownSummary()
        {
            var doc = new ConsensusEvaluationDocument
            {
                Timestamp = new DateTimeOffset(2026, 8, 15, 2, 0, 0, TimeSpan.Zero),
                ConfiguredProviders = new List<string> { "Gemini", "Grok", "Kimi" },
                Scenarios = new List<ScenarioEvaluationRecord>
                {
                    new()
                    {
                        ScenarioName = "Desktop_AmbiguousSiblingTabs",
                        Platform = "windows-uia",
                        GroundTruthAutomationId = "tabItem_1",
                        ConsensusWinnerAutomationId = "tabItem_1",
                        HeuristicCandidateAutomationId = "tabItem_0",
                        ConsensusReached = true,
                        IsCorrect = true,
                        AgreedProviders = new List<string> { "Gemini", "Grok" },
                        ProviderAttempts = new Dictionary<string, int> { ["Gemini"] = 1, ["Grok"] = 1, ["Kimi"] = 1 },
                    },
                },
                Summary = new ConsensusEvaluationSummary
                {
                    TotalScenarios = 1,
                    ConsensusCount = 1,
                    CorrectCount = 1,
                    TotalProviderAttempts = new Dictionary<string, int> { ["Gemini"] = 1, ["Grok"] = 1, ["Kimi"] = 1 },
                    TotalProviderSuccesses = new Dictionary<string, int> { ["Gemini"] = 1, ["Grok"] = 1, ["Kimi"] = 1 },
                    TotalProviderFailures = new Dictionary<string, int> { ["Gemini"] = 0, ["Grok"] = 0, ["Kimi"] = 0 },
                    AgreementMatrix = new Dictionary<string, Dictionary<string, int>>
                    {
                        ["Gemini"] = new() { ["Grok"] = 1, ["Kimi"] = 0 },
                        ["Grok"] = new() { ["Gemini"] = 1, ["Kimi"] = 0 },
                        ["Kimi"] = new() { ["Gemini"] = 0, ["Grok"] = 0 },
                    },
                },
            };

            var json = ConsensusEvaluationSerializer.ToJson(doc);
            Assert.Contains("\"SchemaVersion\": 1", json);
            Assert.Contains("\"Desktop_AmbiguousSiblingTabs\"", json);

            var roundtripped = ConsensusEvaluationSerializer.FromJson(json);
            Assert.Equal(doc.SchemaVersion, roundtripped.SchemaVersion);
            Assert.Single(roundtripped.Scenarios);
            Assert.Equal("tabItem_1", roundtripped.Scenarios[0].ConsensusWinnerAutomationId);

            var markdown = doc.ToMarkdownStepSummary();
            Assert.Contains("Multi-Provider Consensus Evaluation Summary", markdown);
            Assert.Contains("Desktop_AmbiguousSiblingTabs", markdown);
            Assert.Contains("Consensus (Correct)", markdown);
        }

        [Fact]
        public async Task RunConsensusEvaluation_LiveNightlyRun()
        {
            var optIn = Environment.GetEnvironmentVariable("CONSENSUS_EVALUATION");
            if (optIn != "1" && !string.Equals(optIn, "true", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("[ConsensusEvaluation] CONSENSUS_EVALUATION=1 is not set - skipping live nightly evaluation.");
                return;
            }

            var providers = LlmProviderFactory.CreateConfiguredProviders();
            if (providers.Count == 0)
            {
                Console.WriteLine("[ConsensusEvaluation] No available LLM providers configured - evaluation skipped.");
                return;
            }

            Console.WriteLine($"[ConsensusEvaluation] Running nightly consensus evaluation with {providers.Count} providers: {string.Join(", ", providers.Select(p => p.Name))}");

            var scenarios = EvaluationScenarios.All;
            var doc = new ConsensusEvaluationDocument
            {
                Timestamp = DateTimeOffset.UtcNow,
                ConfiguredProviders = providers.Select(p => p.Name).ToList(),
            };

            var pairAgreement = new Dictionary<string, Dictionary<string, int>>();
            foreach (var p1 in doc.ConfiguredProviders)
            {
                pairAgreement[p1] = new Dictionary<string, int>();
                foreach (var p2 in doc.ConfiguredProviders)
                {
                    pairAgreement[p1][p2] = 0;
                }
            }

            var totalAttempts = new Dictionary<string, int>();
            var totalSuccesses = new Dictionary<string, int>();
            var totalFailures = new Dictionary<string, int>();
            foreach (var p in doc.ConfiguredProviders)
            {
                totalAttempts[p] = 0;
                totalSuccesses[p] = 0;
                totalFailures[p] = 0;
            }

            var consensusCount = 0;
            var correctCount = 0;
            var splitVoteCount = 0;
            var insufficientCount = 0;

            foreach (var s in scenarios)
            {
                Console.WriteLine($"[ConsensusEvaluation] Evaluating scenario: {s.Name} ({s.Platform})");

                var healResult = await SelfHealingResolver.ResolveAsync(
                    expected: s.Expected,
                    currentTreeRoot: s.CurrentTreeRoot,
                    llmProviders: providers,
                    platform: s.Platform);

                var isConsensus = healResult.Source == HealSource.Llm && healResult.AgreedProviders.Count >= 2;
                var consensusWinner = isConsensus ? healResult.Matched?.AutomationId : null;
                var heuristicCandidate = healResult.HeuristicMatched?.AutomationId ?? (healResult.Source == HealSource.Heuristic ? healResult.Matched?.AutomationId : null);
                var isCorrect = isConsensus && !string.IsNullOrEmpty(consensusWinner)
                    ? string.Equals(consensusWinner, s.GroundTruthAutomationId, StringComparison.OrdinalIgnoreCase)
                    : (bool?)null;

                if (isConsensus)
                {
                    consensusCount++;
                    if (isCorrect == true) correctCount++;
                }
                else
                {
                    if (healResult.ProviderAttempts == null || healResult.ProviderAttempts.Count(p => p.Value > 0) < 2)
                    {
                        insufficientCount++;
                    }
                    else
                    {
                        splitVoteCount++;
                    }
                }

                // Update agreement matrix for pairs of agreed providers
                if (healResult.AgreedProviders.Count >= 2)
                {
                    for (var i = 0; i < healResult.AgreedProviders.Count; i++)
                    {
                        for (var j = 0; j < healResult.AgreedProviders.Count; j++)
                        {
                            if (i != j)
                            {
                                var p1 = healResult.AgreedProviders[i];
                                var p2 = healResult.AgreedProviders[j];
                                if (pairAgreement.TryGetValue(p1, out var sub) && sub.ContainsKey(p2))
                                {
                                    sub[p2]++;
                                }
                            }
                        }
                    }
                }

                if (healResult.ProviderAttempts != null)
                {
                    // Indexed rather than deconstructed: KeyValuePair.Deconstruct arrived in
                    // .NET Core 2.0, so `foreach (var (k, v) in dict)` does not compile for the
                    // net48 leg this project also targets.
                    foreach (var kvp in healResult.ProviderAttempts)
                    {
                        var providerName = kvp.Key;
                        var attempts = kvp.Value;
                        if (totalAttempts.ContainsKey(providerName))
                        {
                            totalAttempts[providerName] += attempts;
                        }

                        if (healResult.AgreedProviders.Contains(providerName))
                        {
                            if (totalSuccesses.ContainsKey(providerName))
                                totalSuccesses[providerName]++;
                        }
                        else if (attempts > 0)
                        {
                            if (totalFailures.ContainsKey(providerName))
                                totalFailures[providerName]++;
                        }
                    }
                }

                var record = new ScenarioEvaluationRecord
                {
                    ScenarioName = s.Name,
                    Platform = s.Platform,
                    GroundTruthAutomationId = s.GroundTruthAutomationId,
                    ConsensusWinnerAutomationId = consensusWinner,
                    HeuristicCandidateAutomationId = heuristicCandidate,
                    ConsensusReached = isConsensus,
                    IsCorrect = isCorrect,
                    AgreedProviders = healResult.AgreedProviders.ToList(),
                    ProviderAttempts = healResult.ProviderAttempts != null
                        ? new Dictionary<string, int>(healResult.ProviderAttempts)
                        : new Dictionary<string, int>(),
                };

                doc.Scenarios.Add(record);
            }

            doc.Summary = new ConsensusEvaluationSummary
            {
                TotalScenarios = scenarios.Count,
                ConsensusCount = consensusCount,
                CorrectCount = correctCount,
                SplitVoteCount = splitVoteCount,
                InsufficientProvidersCount = insufficientCount,
                TotalProviderAttempts = totalAttempts,
                TotalProviderSuccesses = totalSuccesses,
                TotalProviderFailures = totalFailures,
                AgreementMatrix = pairAgreement,
            };

            // Write output JSON into AppContext.BaseDirectory/TestResults
            var json = ConsensusEvaluationSerializer.ToJson(doc);
            var outDir = Path.Combine(AppContext.BaseDirectory, "TestResults");
            Directory.CreateDirectory(outDir);
            var outPath = Path.Combine(outDir, "consensus-evaluation.json");
            File.WriteAllText(outPath, json);
            Console.WriteLine($"[ConsensusEvaluation] Output written to {outPath}");

            // Write to $GITHUB_STEP_SUMMARY if available
            var stepSummaryFile = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
            if (!string.IsNullOrEmpty(stepSummaryFile))
            {
                try
                {
                    var md = doc.ToMarkdownStepSummary();
                    File.AppendAllText(stepSummaryFile, md + Environment.NewLine);
                    Console.WriteLine("[ConsensusEvaluation] Appended markdown summary to GITHUB_STEP_SUMMARY.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ConsensusEvaluation] Failed to write GITHUB_STEP_SUMMARY: {ex.Message}");
                }
            }

            Assert.NotNull(doc);
        }

        [Fact]
        public async Task RunConsensusEvaluation_AgainstMockedMultiProviders_ProducesCompleteDocument()
        {
            var openAiResponse = """
            {
              "choices": [
                {
                  "message": {
                    "role": "assistant",
                    "content": "{\"candidateId\": \"c0\", \"confidence\": 0.95, \"reasoning\": \"Best match\"}"
                  }
                }
              ]
            }
            """;

            var geminiResponse = """
            {
              "steps": [
                {
                  "type": "model_output",
                  "content": [
                    {
                      "type": "text",
                      "text": "{\"candidateId\": \"c0\", \"confidence\": 0.90, \"reasoning\": \"Matching element\"}"
                    }
                  ]
                }
              ]
            }
            """;

            using var httpClient = new HttpClient(new FakeHandler(req =>
            {
                var url = req.RequestUri?.ToString() ?? "";
                if (url.Contains("generativelanguage"))
                {
                    return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = new StringContent(geminiResponse, System.Text.Encoding.UTF8, "application/json"),
                    };
                }

                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(openAiResponse, System.Text.Encoding.UTF8, "application/json"),
                };
            }));

            var env = new Dictionary<string, string>
            {
                ["GEMINI_API_KEY"] = "mock-gemini-key",
                ["GROK_API_KEY"] = "mock-grok-key",
                ["KIMI_API_KEY"] = "mock-kimi-key",
            };

            var providers = LlmProviderFactory.CreateConfiguredProviders(
                httpClient: httpClient,
                getEnv: key => env.TryGetValue(key, out var val) ? val : null);

            Assert.Equal(3, providers.Count);

            var scenarios = EvaluationScenarios.All;
            var doc = new ConsensusEvaluationDocument
            {
                ConfiguredProviders = providers.Select(p => p.Name).ToList(),
            };

            foreach (var s in scenarios)
            {
                var healResult = await SelfHealingResolver.ResolveAsync(
                    expected: s.Expected,
                    currentTreeRoot: s.CurrentTreeRoot,
                    llmProviders: providers,
                    platform: s.Platform);

                var isConsensus = healResult.Source == HealSource.Llm && healResult.AgreedProviders.Count >= 2;
                doc.Scenarios.Add(new ScenarioEvaluationRecord
                {
                    ScenarioName = s.Name,
                    Platform = s.Platform,
                    GroundTruthAutomationId = s.GroundTruthAutomationId,
                    ConsensusWinnerAutomationId = isConsensus ? healResult.Matched?.AutomationId : null,
                    HeuristicCandidateAutomationId = healResult.HeuristicMatched?.AutomationId ?? healResult.Matched?.AutomationId,
                    ConsensusReached = isConsensus,
                    IsCorrect = isConsensus,
                    AgreedProviders = healResult.AgreedProviders.ToList(),
                    ProviderAttempts = healResult.ProviderAttempts != null
                        ? new Dictionary<string, int>(healResult.ProviderAttempts)
                        : new Dictionary<string, int>(),
                });
            }

            doc.Summary = new ConsensusEvaluationSummary
            {
                TotalScenarios = scenarios.Count,
                ConsensusCount = doc.Scenarios.Count(s => s.ConsensusReached),
            };

            Assert.Equal(4, doc.Scenarios.Count);
            Assert.Equal(4, doc.Summary.ConsensusCount);

            var markdown = doc.ToMarkdownStepSummary();
            Assert.Contains("Multi-Provider Consensus Evaluation Summary", markdown);
            Assert.Contains("**Consensus Rate:** 4/4 (100%)", markdown);
        }

        private sealed class FakeHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

            public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            {
                _responder = responder;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
            {
                return Task.FromResult(_responder(request));
            }
        }
    }
}
