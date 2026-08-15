using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
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
        public void AllEvaluationFixtures_GroundTruthMustBePresentInScoredCandidates()
        {
            var scenarios = EvaluationScenarios.All;
            Assert.NotEmpty(scenarios);

            foreach (var s in scenarios)
            {
                if (string.IsNullOrEmpty(s.GroundTruthAutomationId))
                {
                    continue;
                }

                var candidates = SelfHealingResolver.ScoreCandidates(s.Expected, s.CurrentTreeRoot, SimilarityWeights.Default);
                Assert.Contains(
                    candidates,
                    c => string.Equals(c.Candidate.AutomationId, s.GroundTruthAutomationId, StringComparison.OrdinalIgnoreCase));
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
        public void LlmProviderFactory_SkipsGroqAndOpenRouter_WhenTheirModelIsNotConfigured()
        {
            // A key alone is not enough for these two. Without an explicit model,
            // OpenAiHealingProvider would fall back to OPENAI_MODEL - set here for a different
            // vendor entirely - and then to "gpt-4o-mini", quietly asking Groq for an OpenAI
            // model. Being absent from the report is the honest outcome; being present and
            // wrong is how "grok-2-latest" failed silently every night.
            var env = new Dictionary<string, string>
            {
                ["GROQ_API_KEY"] = "groq-test-key",
                ["OPENROUTER_API_KEY"] = "openrouter-test-key",
                ["OPENAI_MODEL"] = "gpt-4o-mini",
            };

            var providers = LlmProviderFactory.CreateConfiguredProviders(getEnv: key => env.TryGetValue(key, out var val) ? val : null);

            Assert.DoesNotContain(providers, p => p.Name == "Groq");
            Assert.DoesNotContain(providers, p => p.Name == "OpenRouter");
        }

        [Fact]
        public void LlmProviderFactory_ConfiguresGroqAndOpenRouter_WhenModelsAreSet()
        {
            var env = new Dictionary<string, string>
            {
                ["GROQ_API_KEY"] = "groq-test-key",
                ["GROQ_MODEL"] = "llama-3.3-70b-versatile",
                ["OPENROUTER_API_KEY"] = "openrouter-test-key",
                ["OPENROUTER_MODEL"] = "qwen/qwen3-32b:free",
            };

            var providers = LlmProviderFactory.CreateConfiguredProviders(getEnv: key => env.TryGetValue(key, out var val) ? val : null);

            Assert.Equal(2, providers.Count);

            // Groq and Grok are different companies whose names differ by one letter. Their votes
            // must stay distinguishable, which is what the Name uniqueness contract is for.
            var groq = Assert.Single(providers, p => p.Name == "Groq");
            var openRouter = Assert.Single(providers, p => p.Name == "OpenRouter");
            Assert.Equal("https://api.groq.com/openai/v1/chat/completions", ((OpenAiHealingProvider)groq).ApiUrl);
            Assert.Equal("https://openrouter.ai/api/v1/chat/completions", ((OpenAiHealingProvider)openRouter).ApiUrl);
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
                    new()
                    {
                        ScenarioName = "Desktop_UndecidableSplitExportAction",
                        Platform = "windows-uia",
                        GroundTruthAutomationId = null,
                        ConsensusWinnerAutomationId = null,
                        HeuristicCandidateAutomationId = "btn_csv",
                        ConsensusReached = false,
                        IsCorrect = null,
                        Outcome = ConsensusOutcome.Disagreement,
                        AgreedProviders = new List<string>(),
                        ProviderAttempts = new Dictionary<string, int> { ["Gemini"] = 1, ["Grok"] = 1, ["Kimi"] = 1 },
                    },
                },
                Summary = new ConsensusEvaluationSummary
                {
                    TotalScenarios = 2,
                    ConsensusCount = 1,
                    CorrectCount = 1,
                    DecidableScenariosCount = 1,
                    DecidableConsensusCount = 1,
                    UndecidableScenariosCount = 1,
                    UndecidableConsensusCount = 0,
                    SplitVoteCount = 1,
                    TotalProviderAttempts = new Dictionary<string, int> { ["Gemini"] = 2, ["Grok"] = 2, ["Kimi"] = 2 },
                    TotalProviderAnswered = new Dictionary<string, int> { ["Gemini"] = 2, ["Grok"] = 2, ["Kimi"] = 2 },
                    TotalProviderFailed = new Dictionary<string, int> { ["Gemini"] = 0, ["Grok"] = 0, ["Kimi"] = 0 },
                    TotalProviderInConsensus = new Dictionary<string, int> { ["Gemini"] = 1, ["Grok"] = 1, ["Kimi"] = 0 },
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
            Assert.Contains("\"Desktop_UndecidableSplitExportAction\"", json);

            var roundtripped = ConsensusEvaluationSerializer.FromJson(json);
            Assert.Equal(doc.SchemaVersion, roundtripped.SchemaVersion);
            Assert.Equal(2, roundtripped.Scenarios.Count);
            Assert.Equal("tabItem_1", roundtripped.Scenarios[0].ConsensusWinnerAutomationId);
            Assert.Null(roundtripped.Scenarios[1].GroundTruthAutomationId);
            Assert.Equal(1, roundtripped.Summary.DecidableScenariosCount);
            Assert.Equal(1, roundtripped.Summary.UndecidableScenariosCount);
            Assert.Equal(0, roundtripped.Summary.UndecidableConsensusCount);

            var markdown = doc.ToMarkdownStepSummary();
            Assert.Contains("Multi-Provider Consensus Evaluation Summary", markdown);
            Assert.Contains("Desktop_AmbiguousSiblingTabs", markdown);
            Assert.Contains("Desktop_UndecidableSplitExportAction", markdown);
            Assert.Contains("Consensus (Correct)", markdown);
            Assert.Contains("*(undecidable)*", markdown);
            Assert.Contains("✅ No Consensus (Expected)", markdown);
            Assert.Contains("**Accuracy (on Decidable Consensus):** 1/1 (100%)", markdown);
            Assert.Contains("**Consensus on Undecidable:** 0/1", markdown);
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

            var doc = await EvaluateScenariosAsync(providers, EvaluationScenarios.All, verbose: true);

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

            var doc = await EvaluateScenariosAsync(providers, EvaluationScenarios.All, verbose: false);

            Assert.Equal(5, doc.Scenarios.Count);
            Assert.Equal(5, doc.Summary.ConsensusCount);
            Assert.Equal(4, doc.Summary.DecidableScenariosCount);
            Assert.Equal(1, doc.Summary.UndecidableScenariosCount);
            Assert.Equal(1, doc.Summary.UndecidableConsensusCount);

            var markdown = doc.ToMarkdownStepSummary();
            Assert.Contains("Multi-Provider Consensus Evaluation Summary", markdown);
            Assert.Contains("**Consensus Rate:** 5/5 (100%)", markdown);
            Assert.Contains("**Consensus on Undecidable:** 1/1", markdown);

            // Per-provider outcomes are recorded even on the happy path, so the mocked run
            // exercises the same classification the nightly depends on rather than a parallel
            // implementation of it.
            Assert.All(doc.Scenarios, s =>
            {
                Assert.Equal(3, s.ProviderResults.Count);
                Assert.All(s.ProviderResults, r => Assert.Equal(ProviderOutcome.Answered, r.Outcome));
                Assert.Equal(ConsensusOutcome.ConsensusReached, s.Outcome);
            });
            Assert.All(doc.ConfiguredProviders, p => Assert.Equal(5, doc.Summary.TotalProviderAnswered[p]));
            Assert.All(doc.ConfiguredProviders, p => Assert.Equal(0, doc.Summary.TotalProviderFailed[p]));
        }

        // Shared by the live nightly run and the mocked test on purpose: the mocked test is only
        // a meaningful check of the nightly if it exercises the same evaluation and classification
        // code, not a second copy of it.
        private static async Task<ConsensusEvaluationDocument> EvaluateScenariosAsync(
            IReadOnlyList<ILlmHealingProvider> providers,
            IReadOnlyList<EvaluationScenario> scenarios,
            bool verbose)
        {
            var doc = new ConsensusEvaluationDocument
            {
                Timestamp = DateTimeOffset.UtcNow,
                ConfiguredProviders = providers.Select(p => p.Name).ToList(),
            };

            var pairAgreement = new Dictionary<string, Dictionary<string, int>>();
            var totalAttempts = new Dictionary<string, int>();
            var totalAnswered = new Dictionary<string, int>();
            var totalFailed = new Dictionary<string, int>();
            var totalDiscarded = new Dictionary<string, int>();
            var totalInConsensus = new Dictionary<string, int>();
            foreach (var p1 in doc.ConfiguredProviders)
            {
                pairAgreement[p1] = new Dictionary<string, int>();
                foreach (var p2 in doc.ConfiguredProviders)
                {
                    pairAgreement[p1][p2] = 0;
                }

                totalAttempts[p1] = 0;
                totalAnswered[p1] = 0;
                totalFailed[p1] = 0;
                totalDiscarded[p1] = 0;
                totalInConsensus[p1] = 0;
            }

            var consensusCount = 0;
            var correctCount = 0;
            var decidableScenariosCount = 0;
            var decidableConsensusCount = 0;
            var undecidableScenariosCount = 0;
            var undecidableConsensusCount = 0;
            var disagreementCount = 0;
            var tooFewVotesCount = 0;
            var noneAnsweredCount = 0;

            foreach (var s in scenarios)
            {
                if (verbose)
                {
                    Console.WriteLine($"[ConsensusEvaluation] Evaluating scenario: {s.Name} ({s.Platform})");
                }

                var isDecidable = !string.IsNullOrEmpty(s.GroundTruthAutomationId);
                if (isDecidable)
                {
                    decidableScenariosCount++;
                }
                else
                {
                    undecidableScenariosCount++;
                }

                // Each provider is wrapped so its raw LlmHealingResult can be observed while the
                // resolver drives it. Calling LlmHealingEvaluator separately would double the API
                // calls and burn the free-tier quota this nightly depends on; decorating costs
                // nothing extra and still measures the canonical ResolveAsync path.
                var recorders = providers.Select(p => new RecordingProvider(p)).ToList();

                var healResult = await SelfHealingResolver.ResolveAsync(
                    expected: s.Expected,
                    currentTreeRoot: s.CurrentTreeRoot,
                    llmProviders: recorders,
                    platform: s.Platform);

                var shortlistIds = new HashSet<string>(
                    SelfHealingResolver
                        .ScoreCandidates(s.Expected, s.CurrentTreeRoot)
                        .Take(SimilarityWeights.Default.MaxCandidatesForLlm)
                        .Select((_, i) => "c" + i),
                    StringComparer.Ordinal);

                var isConsensus = healResult.Source == HealSource.Llm && healResult.AgreedProviders.Count >= 2;
                var consensusWinner = isConsensus ? healResult.Matched?.AutomationId : null;
                var heuristicCandidate = healResult.HeuristicMatched?.AutomationId
                    ?? (healResult.Source == HealSource.Heuristic ? healResult.Matched?.AutomationId : null);
                var isCorrect = isConsensus && !string.IsNullOrEmpty(consensusWinner) && isDecidable
                    ? (bool?)string.Equals(consensusWinner, s.GroundTruthAutomationId, StringComparison.OrdinalIgnoreCase)
                    : (bool?)null;

                var providerResults = new List<ProviderOutcomeRecord>();
                foreach (var recorder in recorders)
                {
                    var r = recorder.LastResult;
                    string outcome;
                    if (r == null || !r.Success || string.IsNullOrEmpty(r.MatchedCandidateId))
                    {
                        outcome = ProviderOutcome.Failed;
                    }
                    else if (!shortlistIds.Contains(r.MatchedCandidateId!))
                    {
                        outcome = ProviderOutcome.Discarded;
                    }
                    else
                    {
                        outcome = ProviderOutcome.Answered;
                    }

                    var agreed = healResult.AgreedProviders.Contains(recorder.Name);
                    providerResults.Add(new ProviderOutcomeRecord
                    {
                        ProviderName = recorder.Name,
                        Outcome = outcome,
                        MatchedCandidateId = r?.MatchedCandidateId,
                        Confidence = outcome == ProviderOutcome.Answered ? r!.Confidence : (double?)null,
                        ElapsedMs = r?.Elapsed.TotalMilliseconds ?? 0,
                        AttemptCount = r?.AttemptCount ?? 0,
                        AgreedWithConsensus = agreed,
                        Error = r?.ErrorMessage,
                    });

                    totalAttempts[recorder.Name] += r?.AttemptCount ?? 0;
                    if (outcome == ProviderOutcome.Answered) totalAnswered[recorder.Name]++;
                    else if (outcome == ProviderOutcome.Discarded) totalDiscarded[recorder.Name]++;
                    else totalFailed[recorder.Name]++;
                    if (agreed) totalInConsensus[recorder.Name]++;
                }

                var answered = providerResults.Count(r => r.Outcome == ProviderOutcome.Answered);
                string outcomeClass;
                if (isConsensus)
                {
                    outcomeClass = ConsensusOutcome.ConsensusReached;
                    consensusCount++;
                    if (isDecidable)
                    {
                        decidableConsensusCount++;
                        if (isCorrect == true) correctCount++;
                    }
                    else
                    {
                        undecidableConsensusCount++;
                    }
                }
                else if (answered == 0)
                {
                    // Nobody voted. Reporting this as disagreement - which the first version did -
                    // claims the models were split when in fact none of them answered.
                    outcomeClass = ConsensusOutcome.NoProviderAnswered;
                    noneAnsweredCount++;
                }
                else if (answered < SimilarityWeights.Default.MinimumConsensusVotes)
                {
                    outcomeClass = ConsensusOutcome.TooFewUsableVotes;
                    tooFewVotesCount++;
                }
                else
                {
                    outcomeClass = ConsensusOutcome.Disagreement;
                    disagreementCount++;
                }

                for (var i = 0; i < healResult.AgreedProviders.Count; i++)
                {
                    for (var j = 0; j < healResult.AgreedProviders.Count; j++)
                    {
                        if (i == j) continue;
                        var p1 = healResult.AgreedProviders[i];
                        var p2 = healResult.AgreedProviders[j];
                        if (pairAgreement.TryGetValue(p1, out var sub) && sub.ContainsKey(p2))
                        {
                            sub[p2]++;
                        }
                    }
                }

                doc.Scenarios.Add(new ScenarioEvaluationRecord
                {
                    ScenarioName = s.Name,
                    Platform = s.Platform,
                    GroundTruthAutomationId = s.GroundTruthAutomationId,
                    ConsensusWinnerAutomationId = consensusWinner,
                    HeuristicCandidateAutomationId = heuristicCandidate,
                    ConsensusReached = isConsensus,
                    IsCorrect = isCorrect,
                    Outcome = outcomeClass,
                    AgreedProviders = healResult.AgreedProviders.ToList(),
                    ProviderResults = providerResults,
                    // ToDictionary rather than the copy constructor: Dictionary's
                    // IEnumerable<KeyValuePair<,>> overload arrived in .NET Core 2.0, so on the
                    // net48 leg an IReadOnlyDictionary argument binds to Dictionary(int capacity)
                    // instead and fails with CS1503.
                    ProviderAttempts = healResult.ProviderAttempts != null
                        ? healResult.ProviderAttempts.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
                        : new Dictionary<string, int>(),
                });
            }

            doc.Summary = new ConsensusEvaluationSummary
            {
                TotalScenarios = scenarios.Count,
                ConsensusCount = consensusCount,
                CorrectCount = correctCount,
                DecidableScenariosCount = decidableScenariosCount,
                DecidableConsensusCount = decidableConsensusCount,
                UndecidableScenariosCount = undecidableScenariosCount,
                UndecidableConsensusCount = undecidableConsensusCount,
                SplitVoteCount = disagreementCount,
                InsufficientProvidersCount = tooFewVotesCount,
                NoProviderAnsweredCount = noneAnsweredCount,
                TotalProviderAttempts = totalAttempts,
                TotalProviderAnswered = totalAnswered,
                TotalProviderFailed = totalFailed,
                TotalProviderDiscarded = totalDiscarded,
                TotalProviderInConsensus = totalInConsensus,
                AgreementMatrix = pairAgreement,
            };

            return doc;
        }

        // Forwards to the real provider and keeps the result it returned. The resolver stays the
        // sole caller, so nothing extra is sent to any API.
        private sealed class RecordingProvider : ILlmHealingProvider
        {
            private readonly ILlmHealingProvider _inner;

            public RecordingProvider(ILlmHealingProvider inner) => _inner = inner;

            public string Name => _inner.Name;
            public bool IsAvailable => _inner.IsAvailable;
            public LlmHealingResult? LastResult { get; private set; }

            public async Task<LlmHealingResult> ResolveAsync(
                UiElementInfo expected,
                IReadOnlyList<CandidateScore> candidates,
                string? platform = null,
                CancellationToken cancellationToken = default)
            {
                var result = await _inner.ResolveAsync(expected, candidates, platform, cancellationToken).ConfigureAwait(false);
                LastResult = result;
                return result;
            }
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
