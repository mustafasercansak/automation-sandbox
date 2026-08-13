using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
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
                        throw new ElementNotFoundException("Element not found with old automation ID!");
                    }

                    return Task.FromResult("Clicked: " + element.AutomationId);
                },
                captureTreeRoot: () => currentTree);

            Assert.Equal(2, attemptCount);
            Assert.Equal("Clicked: btnSubmit_Renamed", resultText);

            var record = repository.Find("submit_btn");
            Assert.NotNull(record);
            Assert.Equal("btnSubmit_Renamed", record!.Snapshot.AutomationId);
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
                    LlmProviderName = "FakeLlm"
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
        public void HealingReportFileSink_RejectsReportFromNewerSchema()
        {
            File.WriteAllText(_tempReportPath, @"{ ""SchemaVersion"": 99, ""Events"": [] }");
            var sink = new HealingReportFileSink(_tempReportPath, htmlFilePath: null);

            Assert.Throws<NotSupportedException>(() => sink.Record(new HealingReportEntry { LocatorKey = "x" }));
        }
    }
}
