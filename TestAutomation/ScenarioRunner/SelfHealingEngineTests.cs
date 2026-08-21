using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using LlmHealing;
using SelfHealing;
using UiModel;
using Xunit;

namespace ScenarioRunner
{
    public class SelfHealingEngineTests : IDisposable
    {
        private readonly string _tempRepoPath;
        private readonly string _tempReportPath;
        private readonly string _tempHtmlReportPath;

        public SelfHealingEngineTests()
        {
            _tempRepoPath = Path.Combine(Path.GetTempPath(), "SelfHealingEngineTest_" + Guid.NewGuid().ToString("N") + ".locator.json");
            _tempReportPath = Path.Combine(Path.GetTempPath(), "SelfHealingEngineTest_" + Guid.NewGuid().ToString("N") + ".healing-report.json");
            _tempHtmlReportPath = Path.ChangeExtension(_tempReportPath, ".html");
        }

        public void Dispose()
        {
            if (File.Exists(_tempRepoPath))
            {
                File.Delete(_tempRepoPath);
            }

            var lockPath = _tempRepoPath + ".lock";
            if (File.Exists(lockPath))
            {
                File.Delete(lockPath);
            }

            if (File.Exists(_tempReportPath))
            {
                File.Delete(_tempReportPath);
            }

            var reportLockPath = _tempReportPath + ".lock";
            if (File.Exists(reportLockPath))
            {
                File.Delete(reportLockPath);
            }

            if (File.Exists(_tempHtmlReportPath))
            {
                File.Delete(_tempHtmlReportPath);
            }
        }

        [Fact]
        public async Task SelfHealingEngine_ResolveAndRecordAsync_UpsertsHealedLocatorToRepository()
        {
            var repository = new LocatorRepository(_tempRepoPath);
            var engine = new SelfHealingEngine(repository);

            var expected = new UiElementInfo
            {
                ControlType = "Edit",
                AutomationId = "old_id",
                Name = "Email",
                BoundingRectangle = new BoundingRectangle(10, 10, 100, 30),
            };

            var currentTree = new UiElementInfo
            {
                ControlType = "Window",
                Children =
                {
                    new UiElementInfo
                    {
                        ControlType = "Edit",
                        AutomationId = "new_healed_id",
                        Name = "Email",
                        BoundingRectangle = new BoundingRectangle(10, 10, 100, 30),
                    }
                }
            };

            var healResult = await engine.ResolveAndRecordAsync("email_field", expected, currentTree);

            Assert.True(healResult.IsConfident);
            Assert.Equal("new_healed_id", healResult.Matched!.AutomationId);

            var record = repository.Find("email_field");
            Assert.NotNull(record);
            Assert.Equal("new_healed_id", record!.Snapshot.AutomationId);
            Assert.Single(record.HealingHistory);
            Assert.Equal("heuristic", record.HealingHistory[0].Source);
        }

        [Fact]
        public async Task SelfHealingEngine_ExecuteWithHealingAsync_RetriesActionWithHealedElementWhenInitialFails()
        {
            var repository = new LocatorRepository(_tempRepoPath);
            var engine = new SelfHealingEngine(repository, reportSink: new HealingReportFileSink(_tempReportPath));

            var expected = new UiElementInfo
            {
                ControlType = "Button",
                AutomationId = "btnSubmit_Old",
                Name = "Submit",
                BoundingRectangle = new BoundingRectangle(50, 50, 80, 30),
            };

            var currentTree = new UiElementInfo
            {
                ControlType = "Window",
                Children =
                {
                    new UiElementInfo
                    {
                        ControlType = "Button",
                        AutomationId = "btnSubmit_Renamed",
                        Name = "Submit",
                        BoundingRectangle = new BoundingRectangle(50, 50, 80, 30),
                    }
                }
            };

            var attemptCount = 0;
            var resultText = await engine.ExecuteWithHealingAsync(
                "submit_btn",
                expected,
                action: element =>
                {
                    attemptCount++;
                    if (element.AutomationId == "btnSubmit_Old")
                    {
                        throw new ElementNotFoundException("Element not found with old automation ID!");
                    }

                    Assert.Null(repository.Find("submit_btn"));
                    Assert.False(File.Exists(_tempReportPath));
                    return Task.FromResult("Clicked: " + element.AutomationId);
                },
                captureTreeRoot: () => currentTree);

            Assert.Equal(2, attemptCount);
            Assert.Equal("Clicked: btnSubmit_Renamed", resultText);

            var record = repository.Find("submit_btn");
            Assert.NotNull(record);
            Assert.Equal("btnSubmit_Renamed", record!.Snapshot.AutomationId);
            Assert.Single(record.HealingHistory);

            var report = JsonSerializer.Deserialize<HealingReportDocument>(File.ReadAllText(_tempReportPath));
            Assert.NotNull(report);
            Assert.Single(report!.Events);
            Assert.Equal("submit_btn", report.Events[0].LocatorKey);
            Assert.Equal(HealingReportEntry.AcceptedOutcome, report.Events[0].Outcome);
            Assert.Single(report.AcceptedEvents);
        }

        [Fact]
        public async Task SelfHealingEngine_ExecuteWithHealingAsync_DoesNotPersistHealWhenRetriedActionFails()
        {
            var repository = new LocatorRepository(_tempRepoPath);
            var expected = new UiElementInfo
            {
                ControlType = "Button",
                AutomationId = "btnSubmit_Old",
                Name = "Submit",
                BoundingRectangle = new BoundingRectangle(50, 50, 80, 30),
            };
            repository.Upsert("submit_btn", expected, applicationName: "CustomerApp", platform: "windows-uia");
            var repositoryBytesBeforeHeal = File.ReadAllBytes(_tempRepoPath);
            var engine = new SelfHealingEngine(repository, reportSink: new HealingReportFileSink(_tempReportPath));
            var currentTree = new UiElementInfo
            {
                ControlType = "Window",
                Children =
                {
                    new UiElementInfo
                    {
                        ControlType = "Button",
                        AutomationId = "btnSubmit_Renamed",
                        Name = "Submit",
                        BoundingRectangle = new BoundingRectangle(50, 50, 80, 30),
                    }
                }
            };
            var originalException = new ElementNotFoundException("Element not found with old automation ID!");
            var retryException = new InvalidOperationException("The healed element could not be invoked.");
            var attemptCount = 0;

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                engine.ExecuteWithHealingAsync<string>(
                    "submit_btn",
                    expected,
                    action: element =>
                    {
                        attemptCount++;
                        if (element.AutomationId == "btnSubmit_Old")
                        {
                            throw originalException;
                        }

                        throw retryException;
                    },
                    captureTreeRoot: () => currentTree));

            Assert.Equal(2, attemptCount);
            Assert.Same(originalException, exception.InnerException);
            Assert.Contains("The healed element could not be invoked.", exception.Message);
            Assert.Same(retryException, exception.Data[SelfHealingEngine.RetryExceptionDataKey]);
            Assert.NotNull(retryException.StackTrace);
            Assert.Equal(repositoryBytesBeforeHeal, File.ReadAllBytes(_tempRepoPath));

            var record = repository.Find("submit_btn");
            Assert.NotNull(record);
            Assert.Equal("btnSubmit_Old", record!.Snapshot.AutomationId);
            Assert.Empty(record.HealingHistory);

            var report = JsonSerializer.Deserialize<HealingReportDocument>(File.ReadAllText(_tempReportPath));
            Assert.NotNull(report);
            var entry = Assert.Single(report!.Events);
            Assert.Equal(HealingReportEntry.RetryFailedOutcome, entry.Outcome);
            Assert.Null(entry.AcceptedSnapshot);
            Assert.Equal("btnSubmit_Renamed", entry.ProposedSnapshot!.AutomationId);
            Assert.Empty(report.AcceptedEvents);
        }

        [Fact]
        public async Task SelfHealingEngine_ExecuteWithHealingAsync_DoesNotHealOrRetryNonLocatorExceptions()
        {
            // A non-idempotent action (e.g. placing an order) must never be re-run when an
            // unrelated failure occurs after the side effect already happened.
            var repository = new LocatorRepository(_tempRepoPath);
            var engine = new SelfHealingEngine(repository);

            var expected = new UiElementInfo
            {
                ControlType = "Button",
                AutomationId = "btnPlaceOrder",
                Name = "Place Order",
                BoundingRectangle = new BoundingRectangle(50, 50, 80, 30),
            };

            var currentTree = new UiElementInfo
            {
                ControlType = "Window",
                Children =
                {
                    new UiElementInfo
                    {
                        ControlType = "Button",
                        AutomationId = "btnPlaceOrder",
                        Name = "Place Order",
                        BoundingRectangle = new BoundingRectangle(50, 50, 80, 30),
                    }
                }
            };

            var clickCount = 0;
            var treeCaptureCount = 0;
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                engine.ExecuteWithHealingAsync<string>(
                    "place_order_btn",
                    expected,
                    action: element =>
                    {
                        clickCount++; // The click (side effect) succeeds...
                        throw new InvalidOperationException("Could not parse the order confirmation."); // ...but a later step fails.
                    },
                    captureTreeRoot: () =>
                    {
                        treeCaptureCount++;
                        return currentTree;
                    }));

            Assert.Equal("Could not parse the order confirmation.", exception.Message);
            Assert.Equal(1, clickCount);
            Assert.Equal(0, treeCaptureCount);
        }

        [Fact]
        public async Task SelfHealingEngine_ExecuteWithHealingAsync_DoesNotHealBackendExceptionsWithLocatorLikeNames()
        {
            // Guards against substring false-positives: a backend/state exception whose name
            // merely contains a locator-related word (but isn't one of the exact recognized
            // locator-resolution exception types) must not be classified as healable.
            var repository = new LocatorRepository(_tempRepoPath);
            var engine = new SelfHealingEngine(repository);

            var expected = new UiElementInfo
            {
                ControlType = "Button",
                AutomationId = "btnSubmit",
                Name = "Submit",
                BoundingRectangle = new BoundingRectangle(50, 50, 80, 30),
            };

            var attemptCount = 0;
            var treeCaptureCount = 0;
            var exception = await Assert.ThrowsAsync<AutomationElementBackendException>(() =>
                engine.ExecuteWithHealingAsync<string>(
                    "submit_btn",
                    expected,
                    action: element =>
                    {
                        attemptCount++;
                        throw new AutomationElementBackendException("Backend rejected the automation element state.");
                    },
                    captureTreeRoot: () =>
                    {
                        treeCaptureCount++;
                        return new UiElementInfo { ControlType = "Window" };
                    }));

            Assert.Equal("Backend rejected the automation element state.", exception.Message);
            Assert.Equal(1, attemptCount);
            Assert.Equal(0, treeCaptureCount);
        }

        [Fact]
        public async Task SelfHealingEngine_ExecuteWithHealingAsync_LogsClassificationBeforeTreeCaptureAndRetry()
        {
            var repository = new LocatorRepository(_tempRepoPath);
            var engine = new SelfHealingEngine(repository);

            var expected = new UiElementInfo
            {
                ControlType = "Button",
                AutomationId = "btnSubmit_Old",
                Name = "Submit",
                BoundingRectangle = new BoundingRectangle(50, 50, 80, 30),
            };

            var currentTree = new UiElementInfo
            {
                ControlType = "Window",
                Children =
                {
                    new UiElementInfo
                    {
                        ControlType = "Button",
                        AutomationId = "btnSubmit_Renamed",
                        Name = "Submit",
                        BoundingRectangle = new BoundingRectangle(50, 50, 80, 30),
                    }
                }
            };

            var events = new List<string>();
            var attemptCount = 0;
            await engine.ExecuteWithHealingAsync(
                "submit_btn",
                expected,
                action: element =>
                {
                    attemptCount++;
                    if (element.AutomationId == "btnSubmit_Old")
                    {
                        throw new ElementNotFoundException("Element not found with old automation ID!");
                    }

                    return Task.FromResult("Clicked: " + element.AutomationId);
                },
                captureTreeRoot: () =>
                {
                    events.Add("captureTreeRoot");
                    return currentTree;
                },
                log: message =>
                {
                    if (message.Contains("classified as a locator-resolution failure"))
                    {
                        events.Add("classificationLog");
                    }
                });

            Assert.Equal(2, attemptCount);
            var classificationIndex = events.IndexOf("classificationLog");
            var captureIndex = events.IndexOf("captureTreeRoot");
            Assert.True(classificationIndex >= 0, "Expected the classification log entry to be recorded.");
            Assert.True(captureIndex >= 0, "Expected captureTreeRoot to be invoked.");
            Assert.True(classificationIndex < captureIndex,
                "Exception classification must be logged before the tree is captured for healing/retry.");
        }

        [Fact]
        public async Task SelfHealingEngine_ExecuteWithHealingAsync_HonorsCustomShouldHealPolicy()
        {
            var repository = new LocatorRepository(_tempRepoPath);
            var engine = new SelfHealingEngine(repository);

            var expected = new UiElementInfo
            {
                ControlType = "Button",
                AutomationId = "btnSubmit_Old",
                Name = "Submit",
                BoundingRectangle = new BoundingRectangle(50, 50, 80, 30),
            };

            var currentTree = new UiElementInfo
            {
                ControlType = "Window",
                Children =
                {
                    new UiElementInfo
                    {
                        ControlType = "Button",
                        AutomationId = "btnSubmit_Renamed",
                        Name = "Submit",
                        BoundingRectangle = new BoundingRectangle(50, 50, 80, 30),
                    }
                }
            };

            var attemptCount = 0;
            var resultText = await engine.ExecuteWithHealingAsync(
                "submit_btn",
                expected,
                action: element =>
                {
                    attemptCount++;
                    if (element.AutomationId == "btnSubmit_Old")
                    {
                        throw new InvalidOperationException("Simulated locator failure the caller wants to heal.");
                    }

                    return Task.FromResult("Clicked: " + element.AutomationId);
                },
                captureTreeRoot: () => currentTree,
                shouldHeal: ex => ex is InvalidOperationException);

            Assert.Equal(2, attemptCount);
            Assert.Equal("Clicked: btnSubmit_Renamed", resultText);
        }

        [Fact]
        public async Task SelfHealingEngine_ExecuteWithHealingAsync_CustomPolicyCanRejectLocatorExceptions()
        {
            var repository = new LocatorRepository(_tempRepoPath);
            var engine = new SelfHealingEngine(repository);

            var expected = new UiElementInfo
            {
                ControlType = "Button",
                AutomationId = "btnSubmit_Old",
                Name = "Submit",
                BoundingRectangle = new BoundingRectangle(50, 50, 80, 30),
            };

            var attemptCount = 0;
            await Assert.ThrowsAsync<ElementNotFoundException>(() =>
                engine.ExecuteWithHealingAsync<string>(
                    "submit_btn",
                    expected,
                    action: element =>
                    {
                        attemptCount++;
                        throw new ElementNotFoundException("Element not found with old automation ID!");
                    },
                    captureTreeRoot: () => new UiElementInfo { ControlType = "Window" },
                    shouldHeal: ex => false));

            Assert.Equal(1, attemptCount);
        }

        // Stands in for the exception a UI framework throws when a locator no longer
        // resolves (FlaUI's ElementNotAvailableException, Playwright/Selenium-style
        // NoSuchElement errors). The default healing policy matches by type name, so this
        // fake lets the tests exercise that path without any UI-framework dependency.
        private sealed class ElementNotFoundException : Exception
        {
            public ElementNotFoundException(string message) : base(message)
            {
            }
        }

        // A backend/state exception whose name happens to contain locator-related words
        // ("AutomationElement") without being one of the exact recognized locator-resolution
        // exception types - exercises the false-positive substring-match this default policy
        // must not fall into.
        private sealed class AutomationElementBackendException : Exception
        {
            public AutomationElementBackendException(string message) : base(message)
            {
            }
        }

        [Fact]
        public async Task SelfHealingEngine_ResolveAndRecordAsync_WritesHealingReport()
        {
            var repository = new LocatorRepository(_tempRepoPath);
            var engine = new SelfHealingEngine(repository, reportSink: new HealingReportFileSink(_tempReportPath));

            var expected = new UiElementInfo
            {
                ControlType = "Edit",
                AutomationId = "legacy_email",
                Name = "Email",
                BoundingRectangle = new BoundingRectangle(10, 10, 100, 30),
                TestIntent = "Enter the customer email address"
            };

            var currentTree = new UiElementInfo
            {
                ControlType = "Window",
                Children =
                {
                    new UiElementInfo
                    {
                        ControlType = "Edit",
                        AutomationId = "email",
                        Name = "Email",
                        BoundingRectangle = new BoundingRectangle(10, 10, 100, 30),
                    }
                }
            };

            var healResult = await engine.ResolveAndRecordAsync("CustomerForm.Email", expected, currentTree);

            Assert.True(healResult.IsConfident);
            Assert.True(File.Exists(_tempReportPath));

            var report = JsonSerializer.Deserialize<HealingReportDocument>(File.ReadAllText(_tempReportPath));
            Assert.NotNull(report);
            var entry = Assert.Single(report!.Events);
            Assert.Equal("CustomerForm.Email", entry.LocatorKey);
            Assert.Equal("heuristic", entry.Source);
            Assert.Equal("accepted", entry.ReviewStatus);
            Assert.Equal(HealingReportEntry.AcceptedUnverifiedOutcome, entry.Outcome);
            Assert.Equal("legacy_email", entry.PreviousSnapshot!.AutomationId);
            Assert.Equal("email", entry.AcceptedSnapshot!.AutomationId);
            Assert.Equal("Enter the customer email address", entry.AcceptedSnapshot.TestIntent);
            Assert.True(entry.Score >= entry.ConfidenceThreshold);
            Assert.True(entry.CandidateCount > 0);

            Assert.True(File.Exists(_tempHtmlReportPath));
            var html = File.ReadAllText(_tempHtmlReportPath);
            Assert.Contains("Self-Healing Report", html);
            Assert.Contains("CustomerForm.Email", html);
            Assert.Contains("legacy_email", html);
            Assert.Contains("email", html);
            Assert.Contains("accepted", html);
        }

        [Fact]
        public async Task SelfHealingEngine_ResolveAndRecordAsync_ReportsAmbiguousWithoutUpdatingRepository()
        {
            var repository = new LocatorRepository(_tempRepoPath);
            var engine = new SelfHealingEngine(repository, reportSink: new HealingReportFileSink(_tempReportPath));
            BuildAmbiguousResolutionScenario(out var expected, out var currentTree);

            var result = await engine.ResolveAndRecordAsync(
                "CustomerForm.Email",
                expected,
                currentTree,
                platform: "web-playwright");

            Assert.False(result.IsConfident);
            Assert.Equal(HealResolutionStatus.Ambiguous, result.ResolutionStatus);
            Assert.Null(repository.Find("CustomerForm.Email"));

            var report = JsonSerializer.Deserialize<HealingReportDocument>(File.ReadAllText(_tempReportPath));
            Assert.NotNull(report);
            var entry = Assert.Single(report!.Events);
            Assert.Equal(HealingReportEntry.AmbiguousOutcome, entry.Outcome);
            Assert.Equal("web-playwright", entry.Platform);
            Assert.Null(entry.AcceptedSnapshot);
            Assert.NotNull(entry.ProposedSnapshot);
            Assert.NotNull(entry.ScoreBreakdown);
            Assert.NotEmpty(entry.Candidates!);
            Assert.Empty(report.AcceptedEvents);
        }

        [Fact]
        public async Task SelfHealingEngine_ResolveAndRecordAsync_ReportsNoConsensusExactlyOnce()
        {
            var repository = new LocatorRepository(_tempRepoPath);
            BuildAmbiguousResolutionScenario(out var expected, out var currentTree);
            var providers = new ILlmHealingProvider[]
            {
                SuccessfulProvider("AlphaLlm", "c0"),
                SuccessfulProvider("BetaLlm", "c1"),
                SuccessfulProvider("GammaLlm", "c2"),
            };
            var engine = new SelfHealingEngine(repository, llmProviders: providers, reportSink: new HealingReportFileSink(_tempReportPath));

            var result = await engine.ResolveAndRecordAsync("CustomerForm.Email", expected, currentTree);

            Assert.False(result.IsConfident);
            Assert.Equal(HealResolutionStatus.NoConsensus, result.ResolutionStatus);
            Assert.Null(repository.Find("CustomerForm.Email"));

            var report = JsonSerializer.Deserialize<HealingReportDocument>(File.ReadAllText(_tempReportPath));
            Assert.NotNull(report);
            var entry = Assert.Single(report!.Events);
            Assert.Equal(HealingReportEntry.NoConsensusOutcome, entry.Outcome);
            Assert.Equal(3, entry.ProviderAttempts!.Count);
            Assert.Empty(entry.ProviderErrors!);
            Assert.NotEmpty(entry.Candidates!);
            Assert.Empty(report.AcceptedEvents);
        }

        [Fact]
        public async Task SelfHealingEngine_ResolveAndRecordAsync_ReportsProviderErrorWithProviderNames()
        {
            var repository = new LocatorRepository(_tempRepoPath);
            BuildAmbiguousResolutionScenario(out var expected, out var currentTree);
            var providers = new ILlmHealingProvider[]
            {
                new FakeEngineLlmProvider("AlphaLlm", isAvailable: true, resolve: () => throw new InvalidOperationException("quota exhausted")),
                new FakeEngineLlmProvider("BetaLlm", isAvailable: true, resolve: () =>
                    new LlmHealingResult { Success = false, ErrorMessage = "provider timed out", AttemptCount = 2 }),
            };
            var engine = new SelfHealingEngine(repository, llmProviders: providers, reportSink: new HealingReportFileSink(_tempReportPath));

            var result = await engine.ResolveAndRecordAsync("CustomerForm.Email", expected, currentTree);

            Assert.False(result.IsConfident);
            Assert.Equal(HealResolutionStatus.ProviderError, result.ResolutionStatus);
            Assert.Null(repository.Find("CustomerForm.Email"));

            var report = JsonSerializer.Deserialize<HealingReportDocument>(File.ReadAllText(_tempReportPath));
            Assert.NotNull(report);
            var entry = Assert.Single(report!.Events);
            Assert.Equal(HealingReportEntry.ProviderErrorOutcome, entry.Outcome);
            Assert.Contains("quota exhausted", entry.ProviderErrors!["AlphaLlm"]);
            Assert.Contains("provider timed out", entry.ProviderErrors["BetaLlm"]);
            Assert.Equal(2, entry.ProviderAttempts!["BetaLlm"]);
            Assert.NotEmpty(entry.Candidates!);
            Assert.Empty(report.AcceptedEvents);
        }

        [Fact]
        public void HealingReportEntry_FromHealResult_ClassifiesLlmAndBorderlineMatchesForReview()
        {
            var previous = new UiElementInfo { ControlType = "Button", AutomationId = "old_submit" };
            var accepted = new UiElementInfo { ControlType = "Button", AutomationId = "submit" };

            var strongHeuristic = HealingReportEntry.FromHealResult(
                "Submit",
                previous,
                accepted,
                new HealResult
                {
                    Matched = accepted,
                    Source = HealSource.Heuristic,
                    Score = 0.92,
                    ConfidenceThreshold = 0.50
                });

            var borderlineHeuristic = HealingReportEntry.FromHealResult(
                "Submit",
                previous,
                accepted,
                new HealResult
                {
                    Matched = accepted,
                    Source = HealSource.Heuristic,
                    Score = 0.55,
                    ConfidenceThreshold = 0.50
                });

            var llmMatch = HealingReportEntry.FromHealResult(
                "Submit",
                previous,
                accepted,
                new HealResult
                {
                    Matched = accepted,
                    Source = HealSource.Llm,
                    Score = 0.35,
                    ConfidenceThreshold = 0.50,
                    LlmConfidence = 0.86,
                    LlmProviderName = "AlphaLlm",
                    // An accepted LLM pick is one two providers agreed on (#10); without
                    // AgreedProviders this would classify as manual-review, not accepted-with-llm.
                    AgreedProviders = new[] { "AlphaLlm", "BetaLlm" },
                });

            Assert.Equal(HealingReportEntry.AcceptedStatus, strongHeuristic.ReviewStatus);
            Assert.Equal(HealingReportEntry.ManualReviewStatus, borderlineHeuristic.ReviewStatus);
            Assert.Equal(HealingReportEntry.AcceptedWithLlmStatus, llmMatch.ReviewStatus);
        }

        [Fact]
        public void HealingReportFileSink_LoadsAndUpgradesV1Report_InsteadOfThrowing()
        {
            // A v1 report left on disk by an older build must not break Record(): v2 only
            // added fields, so the old file upgrades in place. (The sink serializes with
            // PascalCase property names - the fixture must match.)
            File.WriteAllText(_tempReportPath, @"{
  ""SchemaVersion"": 1,
  ""GeneratedAt"": ""2026-01-01T00:00:00+00:00"",
  ""Events"": [
    { ""LocatorKey"": ""old"", ""Source"": ""heuristic"", ""ReviewStatus"": ""accepted"", ""Score"": 0.9, ""ConfidenceThreshold"": 0.5, ""CandidateCount"": 1 }
  ]
}");
            var sink = new HealingReportFileSink(_tempReportPath, htmlFilePath: null);

            sink.Record(new HealingReportEntry
            {
                LocatorKey = "new",
                Source = "heuristic",
                ReviewStatus = "accepted",
                ScoreBreakdown = new ScoreComponents { ControlTypeScore = 1.0 }, // other components stay null
            });

            using var doc = JsonDocument.Parse(File.ReadAllText(_tempReportPath));
            var root = doc.RootElement;
            Assert.Equal(HealingReportDocument.CurrentSchemaVersion, root.GetProperty("SchemaVersion").GetInt32());
            Assert.Equal(2, root.GetProperty("Events").GetArrayLength());
            // Null components must round-trip as real JSON nulls - "no evidence" is
            // information the report must not lose.
            var newEvent = root.GetProperty("Events")[1];
            Assert.Equal(JsonValueKind.Null, newEvent.GetProperty("ScoreBreakdown").GetProperty("NameScore").ValueKind);
        }

        [Fact]
        public void HealingReportDocument_AcceptedEvents_IncludesLegacyAndAcceptedOutcomesOnly()
        {
            var report = new HealingReportDocument();
            report.Events.Add(new HealingReportEntry { LocatorKey = "legacy", Outcome = null });
            report.Events.Add(new HealingReportEntry { LocatorKey = "accepted", Outcome = HealingReportEntry.AcceptedOutcome });
            report.Events.Add(new HealingReportEntry { LocatorKey = "unverified", Outcome = HealingReportEntry.AcceptedUnverifiedOutcome });
            report.Events.Add(new HealingReportEntry { LocatorKey = "declined", Outcome = HealingReportEntry.AmbiguousOutcome });

            Assert.Equal(new[] { "legacy", "accepted", "unverified" }, report.AcceptedEvents.Select(e => e.LocatorKey));
        }

        [Fact]
        public void HealingReportEntry_OutcomeFromResolutionStatus_DoesNotMisclassifyUnspecifiedAsLowConfidence()
        {
            Assert.Equal(
                HealingReportEntry.UnspecifiedOutcome,
                HealingReportEntry.OutcomeFromResolutionStatus(HealResolutionStatus.Unspecified));
        }

        [Fact]
        public void HealingReportFileSink_UpgradesV4Report_LeavingAgreedProvidersNull()
        {
            // v5 (#10) added AgreedProviders, v6 (#11) added ProviderAttempts, v7 (#82)
            // added outcome telemetry and v8 (#144) added reconciliation telemetry. An
            // older v4 file upgrades with new fields left null.
            File.WriteAllText(_tempReportPath, @"{
  ""SchemaVersion"": 4,
  ""GeneratedAt"": ""2026-01-01T00:00:00+00:00"",
  ""Events"": [
    { ""LocatorKey"": ""old"", ""Source"": ""Claude"", ""ReviewStatus"": ""accepted-with-llm"", ""Score"": 0.4, ""ConfidenceThreshold"": 0.5, ""CandidateCount"": 3, ""LlmConfidence"": 0.9, ""LlmProviderName"": ""Claude"" }
  ]
}");
            var sink = new HealingReportFileSink(_tempReportPath, htmlFilePath: null);

            sink.Record(HealingReportEntry.FromHealResult(
                "new",
                new UiElementInfo { ControlType = "Edit", AutomationId = "txtOld" },
                new UiElementInfo { ControlType = "Edit", AutomationId = "txtNew" },
                new HealResult
                {
                    Matched = new UiElementInfo { ControlType = "Edit", AutomationId = "txtNew" },
                    Source = HealSource.Llm,
                    Score = 0.4,
                    LlmConfidence = 0.7,
                    LlmProviderName = "AlphaLlm",
                    AgreedProviders = new[] { "AlphaLlm", "BetaLlm" },
                    ProviderAttempts = new Dictionary<string, int> { { "AlphaLlm", 1 }, { "BetaLlm", 2 } },
                }));

            using var doc = JsonDocument.Parse(File.ReadAllText(_tempReportPath));
            var root = doc.RootElement;
            Assert.Equal(8, HealingReportDocument.CurrentSchemaVersion);
            Assert.Equal(HealingReportDocument.CurrentSchemaVersion, root.GetProperty("SchemaVersion").GetInt32());

            var upgraded = root.GetProperty("Events")[0];
            Assert.Equal(JsonValueKind.Null, upgraded.GetProperty("AgreedProviders").ValueKind);
            Assert.Equal(JsonValueKind.Null, upgraded.GetProperty("ProviderAttempts").ValueKind);
            Assert.Equal(JsonValueKind.Null, upgraded.GetProperty("Outcome").ValueKind);
            Assert.Equal(JsonValueKind.Null, upgraded.GetProperty("Platform").ValueKind);
            Assert.Equal(JsonValueKind.Null, upgraded.GetProperty("ProviderErrors").ValueKind);
            Assert.Equal(JsonValueKind.Null, upgraded.GetProperty("ProposedSnapshot").ValueKind);
            Assert.Equal(JsonValueKind.Null, upgraded.GetProperty("CandidateIdentity").ValueKind);
            Assert.Equal(JsonValueKind.Null, upgraded.GetProperty("ReconciliationDisposition").ValueKind);

            var recorded = root.GetProperty("Events")[1];
            var agreed = recorded.GetProperty("AgreedProviders").EnumerateArray().Select(e => e.GetString()).ToArray();
            Assert.Equal(new[] { "AlphaLlm", "BetaLlm" }, agreed);
            var attempts = recorded.GetProperty("ProviderAttempts");
            Assert.Equal(1, attempts.GetProperty("AlphaLlm").GetInt32());
            Assert.Equal(2, attempts.GetProperty("BetaLlm").GetInt32());
        }

        [Fact]
        public void HealingReportFileSink_UpgradesV5Report_LeavingProviderAttemptsNull()
        {
            // v6 (#11) adds ProviderAttempts, v7 (#82) adds outcome telemetry and v8 (#144)
            // adds reconciliation telemetry, so a v5 file upgrades in place with unknowns null.
            File.WriteAllText(_tempReportPath, @"{
  ""SchemaVersion"": 5,
  ""GeneratedAt"": ""2026-01-01T00:00:00+00:00"",
  ""Events"": [
    { ""LocatorKey"": ""old"", ""Source"": ""Claude"", ""ReviewStatus"": ""accepted-with-llm"", ""Score"": 0.4, ""ConfidenceThreshold"": 0.5, ""CandidateCount"": 3, ""LlmConfidence"": 0.9, ""LlmProviderName"": ""Claude"", ""AgreedProviders"": [""Claude"", ""Gemini""] }
  ]
}");
            var sink = new HealingReportFileSink(_tempReportPath, htmlFilePath: null);

            sink.Record(HealingReportEntry.FromHealResult(
                "new",
                new UiElementInfo { ControlType = "Edit", AutomationId = "txtOld" },
                new UiElementInfo { ControlType = "Edit", AutomationId = "txtNew" },
                new HealResult
                {
                    Matched = new UiElementInfo { ControlType = "Edit", AutomationId = "txtNew" },
                    Source = HealSource.Llm,
                    Score = 0.4,
                    LlmConfidence = 0.7,
                    LlmProviderName = "Claude",
                    AgreedProviders = new[] { "Claude", "Gemini" },
                    ProviderAttempts = new Dictionary<string, int> { { "Claude", 1 }, { "Gemini", 2 } },
                }));

            using var doc = JsonDocument.Parse(File.ReadAllText(_tempReportPath));
            var root = doc.RootElement;
            Assert.Equal(8, HealingReportDocument.CurrentSchemaVersion);

            var upgraded = root.GetProperty("Events")[0];
            Assert.Equal(JsonValueKind.Array, upgraded.GetProperty("AgreedProviders").ValueKind);
            Assert.Equal(JsonValueKind.Null, upgraded.GetProperty("ProviderAttempts").ValueKind);
            Assert.Equal(JsonValueKind.Null, upgraded.GetProperty("Outcome").ValueKind);
            Assert.Equal(JsonValueKind.Null, upgraded.GetProperty("ProviderErrors").ValueKind);
            Assert.Equal(JsonValueKind.Null, upgraded.GetProperty("CandidateIdentity").ValueKind);
            Assert.Equal(JsonValueKind.Null, upgraded.GetProperty("ReconciliationDisposition").ValueKind);

            var recorded = root.GetProperty("Events")[1];
            var attempts = recorded.GetProperty("ProviderAttempts");
            Assert.Equal(1, attempts.GetProperty("Claude").GetInt32());
            Assert.Equal(2, attempts.GetProperty("Gemini").GetInt32());
        }

        [Fact]
        public void HealingReportFileSink_UpgradesV7Report_LeavingReconciliationTelemetryNull()
        {
            File.WriteAllText(_tempReportPath, @"{
  ""SchemaVersion"": 7,
  ""GeneratedAt"": ""2026-01-01T00:00:00+00:00"",
  ""Events"": [
    { ""LocatorKey"": ""old"", ""Source"": ""heuristic"", ""ReviewStatus"": ""accepted"", ""Outcome"": ""accepted"", ""Score"": 0.9, ""ConfidenceThreshold"": 0.5, ""CandidateCount"": 2 }
  ]
}");
            var sink = new HealingReportFileSink(_tempReportPath, htmlFilePath: null);

            sink.Record(HealingReportEntry.FromHealResult(
                "new",
                new UiElementInfo { ControlType = "Edit", AutomationId = "txtOld" },
                new UiElementInfo { ControlType = "Edit", AutomationId = "txtNew" },
                new HealResult
                {
                    Matched = new UiElementInfo { ControlType = "Edit", AutomationId = "txtNew" },
                    Score = 0.9,
                    ResolutionStatus = HealResolutionStatus.Confident,
                }));

            using var doc = JsonDocument.Parse(File.ReadAllText(_tempReportPath));
            Assert.Equal(8, doc.RootElement.GetProperty("SchemaVersion").GetInt32());
            var upgraded = doc.RootElement.GetProperty("Events")[0];
            Assert.Equal(JsonValueKind.Null, upgraded.GetProperty("CandidateIdentity").ValueKind);
            Assert.Equal(JsonValueKind.Null, upgraded.GetProperty("ReconciliationDisposition").ValueKind);
        }

        [Fact]
        public void HealingReportHtmlRenderer_ShowsWhoAgreed_OnConsensusEntries()
        {
            var accepted = new UiElementInfo { ControlType = "Edit", AutomationId = "txtNew" };
            var entry = HealingReportEntry.FromHealResult(
                "LoginPage.Email",
                new UiElementInfo { ControlType = "Edit", AutomationId = "txtOld" },
                accepted,
                new HealResult
                {
                    Matched = accepted,
                    Source = HealSource.Llm,
                    Score = 0.4,
                    LlmConfidence = 0.7,
                    LlmProviderName = "Claude",
                    LlmReasoning = "same field, renamed",
                    AgreedProviders = new[] { "Claude", "Gemini" },
                });

            var doc = new HealingReportDocument();
            doc.Events.Add(entry);
            var html = HealingReportHtmlRenderer.Render(doc);

            Assert.Contains("Consensus: Claude + Gemini", html);
            Assert.Contains("same field, renamed", html);

            // A v4-era entry has no record of who agreed; the renderer must not invent one.
            var legacy = new HealingReportDocument();
            legacy.Events.Add(new HealingReportEntry { LocatorKey = "old", Source = "Claude", ReviewStatus = "accepted-with-llm", LlmReasoning = "legacy" });
            Assert.DoesNotContain("Consensus:", HealingReportHtmlRenderer.Render(legacy));
        }

        [Fact]
        public void HealingReportEntry_FromHealResult_LeavesAgreedProvidersNullOnHeuristicResults()
        {
            var entry = HealingReportEntry.FromHealResult(
                "HeuristicOnly",
                new UiElementInfo { ControlType = "Edit", AutomationId = "txtOld" },
                new UiElementInfo { ControlType = "Edit", AutomationId = "txtNew" },
                new HealResult
                {
                    Matched = new UiElementInfo { ControlType = "Edit", AutomationId = "txtNew" },
                    Source = HealSource.Heuristic,
                    Score = 0.9,
                });

            Assert.Null(entry.AgreedProviders);
        }

        [Fact]
        public void HealingReportFileSink_RejectsReportFromNewerSchema()
        {
            File.WriteAllText(_tempReportPath, @"{ ""SchemaVersion"": 99, ""Events"": [] }");
            var sink = new HealingReportFileSink(_tempReportPath, htmlFilePath: null);

            Assert.Throws<NotSupportedException>(() => sink.Record(new HealingReportEntry { LocatorKey = "x" }));
        }

        [Fact]
        public void HealingReportEntry_FromHealResult_CapturesDivergenceAndHeuristicSnapshot()
        {
            var previous = new UiElementInfo { ControlType = "Button", AutomationId = "btnSubmit" };
            var heuristic = new UiElementInfo { ControlType = "Button", AutomationId = "btnOld", Name = "Old Submit" };
            var accepted = new UiElementInfo { ControlType = "Edit", AutomationId = "txtSubmit", Name = "Submit Input" };

            var healResult = new HealResult
            {
                Matched = accepted,
                Source = HealSource.Llm,
                Score = 0.38,
                ConfidenceThreshold = 0.50,
                LlmConfidence = 0.90,
                LlmProviderName = "Claude",
                LlmReasoning = "Matched submit input field",
                HeuristicMatched = heuristic,
                HeuristicScore = 0.45,
                DivergedFromHeuristic = true,
                ScoreBreakdown = new ScoreComponents { ControlTypeScore = 0.0, NameScore = 0.8 },
            };

            var entry = HealingReportEntry.FromHealResult("SubmitAction", previous, accepted, healResult);

            Assert.True(entry.DivergedFromHeuristic);
            Assert.NotNull(entry.HeuristicSnapshot);
            Assert.Equal("btnOld", entry.HeuristicSnapshot!.AutomationId);
            Assert.Equal(0.45, entry.HeuristicScore);
            Assert.Equal(0.38, entry.Score);
            Assert.Equal(0.0, entry.ScoreBreakdown!.ControlTypeScore);

            // Test HTML rendering contains divergence note
            var doc = new HealingReportDocument();
            doc.Events.Add(entry);
            var html = HealingReportHtmlRenderer.Render(doc);
            Assert.Contains("Diverged from heuristic", html);
            Assert.Contains("btnOld", html);
        }

        [Fact]
        public void LocatorHealingHistoryEntryFactory_FromHealResult_RecordsDivergenceFlag()
        {
            var accepted = new UiElementInfo { ControlType = "Button", AutomationId = "btnAccept" };

            // Heuristic source: LLM not involved -> divergence is N/A (null)
            var heuristicResult = new HealResult
            {
                Matched = accepted,
                Source = HealSource.Heuristic,
                Score = 0.90,
                ScoreBreakdown = new ScoreComponents { ControlTypeScore = 1.0 },
            };
            var heuristicEntry = LocatorHealingHistoryEntryFactory.FromHealResult(heuristicResult, previousSnapshot: null);
            Assert.Null(heuristicEntry.DivergedFromHeuristic);

            // LLM source: records explicit divergence flag
            var llmResult = new HealResult
            {
                Matched = accepted,
                Source = HealSource.Llm,
                Score = 0.35,
                DivergedFromHeuristic = true,
                ScoreBreakdown = new ScoreComponents { ControlTypeScore = 0.2 },
            };
            var llmEntry = LocatorHealingHistoryEntryFactory.FromHealResult(llmResult, previousSnapshot: null);
            Assert.True(llmEntry.DivergedFromHeuristic);
            Assert.Equal(0.35, llmEntry.Score);
            Assert.Equal(0.2, llmEntry.ScoreBreakdown!.ControlTypeScore);
        }

        [Fact]
        public async Task SelfHealingEngine_ResolveAndRecordAsync_PropagatesPlatformParameterToLlmProviderAndRepository()
        {
            var repository = new LocatorRepository(_tempRepoPath);
            // Consensus (#10) needs two agreeing providers before an LLM pick is accepted;
            // `provider` is the one whose LastPlatform this test inspects.
            var provider = new FakeEngineLlmProvider("FakeEngine", isAvailable: true, resolve: () =>
                new LlmHealingResult { ProviderName = "FakeEngine", Success = true, MatchedCandidateId = "c0", Confidence = 0.92, Reasoning = "matched" });
            var seconder = new FakeEngineLlmProvider("SecondEngine", isAvailable: true, resolve: () =>
                new LlmHealingResult { ProviderName = "SecondEngine", Success = true, MatchedCandidateId = "c0", Confidence = 0.88, Reasoning = "matched" });

            var engine = new SelfHealingEngine(repository, llmProviders: new ILlmHealingProvider[] { provider, seconder });

            // Stale expected that triggers low heuristic confidence so LLM fallback is used
            var expected = new UiElementInfo
            {
                ControlType = "Edit",
                AutomationId = "old_stale_id",
                Name = "Email Address",
                BoundingRectangle = new BoundingRectangle(10, 10, 100, 30),
            };

            var currentTree = new UiElementInfo
            {
                ControlType = "Window",
                Children =
                {
                    new UiElementInfo
                    {
                        ControlType = "Edit",
                        AutomationId = "healed_email",
                        Name = "Different Label",
                        BoundingRectangle = new BoundingRectangle(500, 500, 100, 30),
                    }
                }
            };

            var result = await engine.ResolveAndRecordAsync(
                "LoginPage.Email",
                expected,
                currentTree,
                platform: "web-playwright");

            Assert.Equal(HealSource.Llm, result.Source);
            Assert.Equal("web-playwright", provider.LastPlatform);

            var doc = repository.Load();
            Assert.Equal("web-playwright", doc.Platform);
        }

        [Fact]
        public async Task SelfHealingEngine_ExecuteWithHealingAsync_PropagatesPlatformParameter()
        {
            var repository = new LocatorRepository(_tempRepoPath);
            var provider = new FakeEngineLlmProvider("FakeEngine", isAvailable: true, resolve: () =>
                new LlmHealingResult { ProviderName = "FakeEngine", Success = true, MatchedCandidateId = "c0", Confidence = 0.95, Reasoning = "matched" });
            var seconder = new FakeEngineLlmProvider("SecondEngine", isAvailable: true, resolve: () =>
                new LlmHealingResult { ProviderName = "SecondEngine", Success = true, MatchedCandidateId = "c0", Confidence = 0.9, Reasoning = "matched" });

            var engine = new SelfHealingEngine(repository, llmProviders: new ILlmHealingProvider[] { provider, seconder });

            var expected = new UiElementInfo
            {
                ControlType = "Edit",
                AutomationId = "old_stale_id",
                Name = "Email Address",
                BoundingRectangle = new BoundingRectangle(10, 10, 100, 30),
            };

            var currentTree = new UiElementInfo
            {
                ControlType = "Window",
                Children =
                {
                    new UiElementInfo
                    {
                        ControlType = "Edit",
                        AutomationId = "healed_email",
                        Name = "Different Label",
                        BoundingRectangle = new BoundingRectangle(500, 500, 100, 30),
                    }
                }
            };

            var attempts = 0;
            var executed = await engine.ExecuteWithHealingAsync(
                "LoginPage.Email",
                expected,
                action: el =>
                {
                    attempts++;
                    if (attempts == 1)
                    {
                        throw new ElementNotFoundException("Element was not found at runtime");
                    }

                    return Task.FromResult(el.AutomationId);
                },
                captureTreeRoot: () => currentTree,
                platform: "web-playwright");

            Assert.Equal("healed_email", executed);
            Assert.Equal(2, attempts);
            Assert.Equal("web-playwright", provider.LastPlatform);
        }

        private sealed class FakeEngineLlmProvider : ILlmHealingProvider
        {
            private readonly Func<LlmHealingResult> _resolve;

            public FakeEngineLlmProvider(string name, bool isAvailable, Func<LlmHealingResult> resolve)
            {
                Name = name;
                IsAvailable = isAvailable;
                _resolve = resolve;
            }

            public string Name { get; }
            public bool IsAvailable { get; }
            public string? LastPlatform { get; private set; }

            public Task<LlmHealingResult> ResolveAsync(
                UiElementInfo expected,
                IReadOnlyList<CandidateScore> candidates,
                string? platform = null,
                CancellationToken cancellationToken = default)
            {
                LastPlatform = platform;
                return Task.FromResult(_resolve());
            }
        }

        private static FakeEngineLlmProvider SuccessfulProvider(string name, string candidateId)
        {
            return new FakeEngineLlmProvider(
                name,
                isAvailable: true,
                resolve: () => new LlmHealingResult
                {
                    ProviderName = name,
                    Success = true,
                    MatchedCandidateId = candidateId,
                    Confidence = 0.9,
                    AttemptCount = 1,
                });
        }

        private static void BuildAmbiguousResolutionScenario(out UiElementInfo expected, out UiElementInfo currentTree)
        {
            expected = new UiElementInfo
            {
                ControlType = "Edit",
                AutomationId = "legacy_email",
                Name = "Email",
                BoundingRectangle = new BoundingRectangle(10, 10, 100, 30),
            };
            currentTree = new UiElementInfo
            {
                ControlType = "Window",
                Children =
                {
                    new UiElementInfo
                    {
                        ControlType = "Edit",
                        AutomationId = "email_primary",
                        Name = "Email",
                        BoundingRectangle = new BoundingRectangle(10, 10, 100, 30),
                    },
                    new UiElementInfo
                    {
                        ControlType = "Edit",
                        AutomationId = "email_secondary",
                        Name = "Email",
                        BoundingRectangle = new BoundingRectangle(10, 10, 100, 30),
                    },
                }
            };
        }
    }
}
