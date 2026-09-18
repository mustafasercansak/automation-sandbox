using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using AutomationSandbox.LlmHealing;
using AutomationSandbox.SelfHealing;
using AutomationSandbox.UiModel;
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

            var reportTempPath = _tempReportPath + ".tmp";
            if (File.Exists(reportTempPath))
            {
                File.Delete(reportTempPath);
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

        // HealingReportFileSink persists entries as JSON Lines (#424): one JSON object per
        // line, appended without reading or re-serializing prior history. This reads them back
        // through the sink's own supported LoadReport() API rather than treating the file as a
        // single JSON document - see the "upgrades"/"forward-compatible" tests below for the
        // format itself.
        private static HealingReportDocument ReadReport(string path)
            => new HealingReportFileSink(path, htmlFilePath: null).LoadReport();

        [Fact]
        public void SelfHealingEngine_DefaultsToReviewMode()
        {
            var engine = new SelfHealingEngine();
            Assert.Equal(HealingMode.Review, engine.Mode);
        }

        [Fact]
        public async Task SelfHealingEngine_ReviewMode_DoesNotAutoPersistOrRetryAction_RecordsManualReview()
        {
            var repository = new LocatorRepository(_tempRepoPath);
            var engine = new SelfHealingEngine(repository, reportSink: new HealingReportFileSink(_tempReportPath), mode: HealingMode.Review);

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

            var originalException = new ElementNotFoundException("Element not found with old automation ID!");
            var attemptCount = 0;

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                engine.ExecuteWithHealingAsync<string>(
                    "submit_btn",
                    expected,
                    action: element =>
                    {
                        attemptCount++;
                        throw originalException;
                    },
                    captureTreeRoot: () => currentTree));

            Assert.Equal(1, attemptCount);
            Assert.Same(originalException, exception.InnerException);
            Assert.Contains("Healing mode is Review", exception.Message);
            Assert.Contains("btnSubmit_Renamed", exception.Message);

            Assert.Null(repository.Find("submit_btn"));

            var report = ReadReport(_tempReportPath);
            Assert.NotNull(report);
            var entry = Assert.Single(report!.Events);
            Assert.Equal("submit_btn", entry.LocatorKey);
            Assert.Equal(HealingReportEntry.ManualReviewOutcome, entry.Outcome);
            Assert.Equal(HealingReportEntry.ManualReviewStatus, entry.ReviewStatus);
            Assert.Equal("btnSubmit_Renamed", entry.ProposedSnapshot!.AutomationId);
            Assert.Null(entry.AcceptedSnapshot);
            Assert.Empty(report.AcceptedEvents);
        }

        [Fact]
        public async Task SelfHealingEngine_ObserveMode_DoesNotAutoPersistOrRetryAction_RecordsObserved()
        {
            var repository = new LocatorRepository(_tempRepoPath);
            var engine = new SelfHealingEngine(repository, reportSink: new HealingReportFileSink(_tempReportPath), mode: HealingMode.Observe);

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

            var originalException = new ElementNotFoundException("Element not found with old automation ID!");
            var attemptCount = 0;

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                engine.ExecuteWithHealingAsync<string>(
                    "submit_btn",
                    expected,
                    action: element =>
                    {
                        attemptCount++;
                        throw originalException;
                    },
                    captureTreeRoot: () => currentTree));

            Assert.Equal(1, attemptCount);
            Assert.Same(originalException, exception.InnerException);
            Assert.Contains("healing mode is Observe", exception.Message);

            Assert.Null(repository.Find("submit_btn"));

            var report = ReadReport(_tempReportPath);
            Assert.NotNull(report);
            var entry = Assert.Single(report!.Events);
            Assert.Equal("submit_btn", entry.LocatorKey);
            Assert.Equal(HealingReportEntry.ObservedOutcome, entry.Outcome);
            Assert.Equal("btnSubmit_Renamed", entry.ProposedSnapshot!.AutomationId);
            Assert.Null(entry.AcceptedSnapshot);
            Assert.Empty(report.AcceptedEvents);
        }

        [Fact]
        public async Task SelfHealingEngine_FailClosedMode_DoesNotCaptureTreeOrHeal_ThrowsOriginalExceptionImmediately()
        {
            var repository = new LocatorRepository(_tempRepoPath);
            var engine = new SelfHealingEngine(repository, reportSink: new HealingReportFileSink(_tempReportPath), mode: HealingMode.FailClosed);

            var expected = new UiElementInfo
            {
                ControlType = "Button",
                AutomationId = "btnSubmit_Old",
                Name = "Submit",
                BoundingRectangle = new BoundingRectangle(50, 50, 80, 30),
            };

            var treeCaptureCount = 0;
            var originalException = new ElementNotFoundException("Element not found with old automation ID!");

            var thrown = await Assert.ThrowsAsync<ElementNotFoundException>(() =>
                engine.ExecuteWithHealingAsync<string>(
                    "submit_btn",
                    expected,
                    action: element => throw originalException,
                    captureTreeRoot: () =>
                    {
                        treeCaptureCount++;
                        return new UiElementInfo { ControlType = "Window" };
                    }));

            Assert.Same(originalException, thrown);
            Assert.Equal(0, treeCaptureCount);
            Assert.Null(repository.Find("submit_btn"));

            var report = ReadReport(_tempReportPath);
            Assert.NotNull(report);
            var entry = Assert.Single(report!.Events);
            Assert.Equal(HealingReportEntry.FailClosedOutcome, entry.Outcome);
            Assert.Null(entry.ProposedSnapshot);
            Assert.Null(entry.AcceptedSnapshot);
            Assert.Empty(report.AcceptedEvents);
        }

        [Fact]
        public async Task SelfHealingEngine_ResolveAndRecordAsync_RespectsHealingModes()
        {
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

            // 1. Review mode (default): resolves candidate, records manual-review, does NOT persist to repo.
            var repoReview = new LocatorRepository(_tempRepoPath);
            var engineReview = new SelfHealingEngine(repoReview, reportSink: new HealingReportFileSink(_tempReportPath));
            var resultReview = await engineReview.ResolveAndRecordAsync("email_review", expected, currentTree);
            Assert.True(resultReview.IsConfident);
            Assert.Null(repoReview.Find("email_review"));

            var reportReview = ReadReport(_tempReportPath);
            var eventReview = Assert.Single(reportReview!.Events);
            Assert.Equal(HealingReportEntry.ManualReviewOutcome, eventReview.Outcome);

            // 2. Observe mode: resolves candidate, records observed, does NOT persist to repo.
            File.Delete(_tempReportPath);
            var repoObserve = new LocatorRepository(_tempRepoPath);
            var engineObserve = new SelfHealingEngine(repoObserve, reportSink: new HealingReportFileSink(_tempReportPath), mode: HealingMode.Observe);
            var resultObserve = await engineObserve.ResolveAndRecordAsync("email_observe", expected, currentTree);
            Assert.True(resultObserve.IsConfident);
            Assert.Null(repoObserve.Find("email_observe"));

            var reportObserve = ReadReport(_tempReportPath);
            var eventObserve = Assert.Single(reportObserve!.Events);
            Assert.Equal(HealingReportEntry.ObservedOutcome, eventObserve.Outcome);

            // 3. FailClosed mode: skips resolution, returns unconfident, records fail-closed.
            File.Delete(_tempReportPath);
            var repoFail = new LocatorRepository(_tempRepoPath);
            var engineFail = new SelfHealingEngine(repoFail, reportSink: new HealingReportFileSink(_tempReportPath), mode: HealingMode.FailClosed);
            var resultFail = await engineFail.ResolveAndRecordAsync("email_fail", expected, currentTree);
            Assert.False(resultFail.IsConfident);
            Assert.Null(repoFail.Find("email_fail"));

            var reportFail = ReadReport(_tempReportPath);
            var eventFail = Assert.Single(reportFail!.Events);
            Assert.Equal(HealingReportEntry.FailClosedOutcome, eventFail.Outcome);

            // 4. AutoHeal mode: resolves candidate, persists to repo, records accepted-unverified.
            File.Delete(_tempReportPath);
            var repoAuto = new LocatorRepository(_tempRepoPath);
            var engineAuto = new SelfHealingEngine(repoAuto, reportSink: new HealingReportFileSink(_tempReportPath), mode: HealingMode.AutoHeal);
            var resultAuto = await engineAuto.ResolveAndRecordAsync("email_auto", expected, currentTree);
            Assert.True(resultAuto.IsConfident);
            Assert.NotNull(repoAuto.Find("email_auto"));
            Assert.Equal("new_healed_id", repoAuto.Find("email_auto")!.Snapshot.AutomationId);

            var reportAuto = ReadReport(_tempReportPath);
            var eventAuto = Assert.Single(reportAuto!.Events);
            Assert.Equal(HealingReportEntry.AcceptedUnverifiedOutcome, eventAuto.Outcome);
        }

        [Fact]
        public async Task SelfHealingEngine_ResolveAndRecordAsync_UpsertsHealedLocatorToRepository()
        {
            var repository = new LocatorRepository(_tempRepoPath);
            var engine = new SelfHealingEngine(repository, mode: HealingMode.AutoHeal);

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
            var engine = new SelfHealingEngine(repository, reportSink: new HealingReportFileSink(_tempReportPath), mode: HealingMode.AutoHeal);

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

            var report = ReadReport(_tempReportPath);
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
            var engine = new SelfHealingEngine(repository, reportSink: new HealingReportFileSink(_tempReportPath), mode: HealingMode.AutoHeal);
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

            var report = ReadReport(_tempReportPath);
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
            var engine = new SelfHealingEngine(repository, mode: HealingMode.AutoHeal);

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
            var engine = new SelfHealingEngine(repository, mode: HealingMode.AutoHeal);

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
            var engine = new SelfHealingEngine(repository, reportSink: new HealingReportFileSink(_tempReportPath), mode: HealingMode.AutoHeal);

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

            var report = ReadReport(_tempReportPath);
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

            var report = ReadReport(_tempReportPath);
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

            var report = ReadReport(_tempReportPath);
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

            var report = ReadReport(_tempReportPath);
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
        public void HealingReportFileSink_Record_AppendsWithoutRewritingPriorLines()
        {
            // The whole point of #424: Record() must not deserialize the file's existing
            // history, add to it in memory, and re-serialize everything back out. Seed the
            // file with one already-recorded line, then prove the new call only ever appends
            // by checking the original bytes are byte-for-byte untouched afterwards.
            var existingLine = JsonSerializer.Serialize(new HealingReportEntry
            {
                LocatorKey = "existing",
                Source = "heuristic",
                ReviewStatus = HealingReportEntry.AcceptedStatus,
                Score = 0.9,
                ConfidenceThreshold = 0.5,
                CandidateCount = 1,
            });
            File.WriteAllText(_tempReportPath, existingLine + "\n");
            var sink = new HealingReportFileSink(_tempReportPath, htmlFilePath: null);

            sink.Record(new HealingReportEntry
            {
                LocatorKey = "new",
                Source = "heuristic",
                ReviewStatus = HealingReportEntry.AcceptedStatus,
                ScoreBreakdown = new ScoreComponents(controlTypeScore: 1.0), // other components stay null
            });

            var lines = File.ReadAllLines(_tempReportPath);
            Assert.Equal(2, lines.Length);
            Assert.Equal(existingLine, lines[0]); // untouched: never re-read or re-serialized

            using var newLine = JsonDocument.Parse(lines[1]);
            Assert.Equal("new", newLine.RootElement.GetProperty("LocatorKey").GetString());
            // Null components must round-trip as real JSON nulls - "no evidence" is
            // information the report must not lose.
            Assert.Equal(JsonValueKind.Null, newLine.RootElement.GetProperty("ScoreBreakdown").GetProperty("NameScore").ValueKind);

            var report = ReadReport(_tempReportPath);
            Assert.Equal(new[] { "existing", "new" }, report.Events.Select(e => e.LocatorKey));
        }

        [Fact]
        public void HealingReportFileSink_Record_NeverReadsExistingContent_WhenNoHtmlConfigured()
        {
            // If Record() ever opened the file for reading - the old deserialize-append-
            // serialize cycle #424 removed - this would throw on the garbage bytes below.
            // Succeeding proves the append is blind to whatever is already on disk, which is
            // what makes it O(1) regardless of how much history has accumulated.
            File.WriteAllText(_tempReportPath, "not valid JSON at all {{{" + "\n");
            var sink = new HealingReportFileSink(_tempReportPath, htmlFilePath: null);

            sink.Record(new HealingReportEntry { LocatorKey = "after-garbage" });

            var lines = File.ReadAllLines(_tempReportPath);
            Assert.Equal(2, lines.Length);
            Assert.Equal("not valid JSON at all {{{", lines[0]);
            using var appended = JsonDocument.Parse(lines[1]);
            Assert.Equal("after-garbage", appended.RootElement.GetProperty("LocatorKey").GetString());
        }

        [Fact]
        public void HealingReportFileSink_AppendFailure_PreservesExistingReport()
        {
            var existingLine = JsonSerializer.Serialize(new HealingReportEntry
            {
                LocatorKey = "existing-history",
                Source = "heuristic",
                ReviewStatus = HealingReportEntry.AcceptedStatus,
                Outcome = HealingReportEntry.AcceptedOutcome,
            });
            File.WriteAllText(_tempReportPath, existingLine + "\n");
            var appendAttempted = false;
            var capturedLine = string.Empty;
            var sink = new HealingReportFileSink(
                _tempReportPath,
                htmlFilePath: null,
                replaceExistingFile: (tempPath, destinationPath) =>
                    throw new InvalidOperationException("HTML is not configured; this must never be invoked."),
                appendLine: (path, line) =>
                {
                    appendAttempted = true;
                    Assert.Equal(_tempReportPath, path);
                    capturedLine = line;
                    throw new IOException("Simulated interruption while appending.");
                });

            Assert.Throws<IOException>(() => sink.Record(new HealingReportEntry { LocatorKey = "new-attempt" }));

            Assert.True(appendAttempted);
            Assert.Contains("new-attempt", capturedLine);
            // The failed append never touched the file - it is exactly what was there before.
            Assert.Equal(existingLine + "\n", File.ReadAllText(_tempReportPath));
            var preserved = ReadReport(_tempReportPath);
            var entry = Assert.Single(preserved.Events);
            Assert.Equal("existing-history", entry.LocatorKey);
        }

        [Fact]
        public void HealingReportFileSink_SavesHtmlReportAtomically_AndCleansUpTempFiles()
        {
            var htmlPath = _tempReportPath + ".html";
            try
            {
                var sink = new HealingReportFileSink(_tempReportPath, htmlPath);
                sink.Record(new HealingReportEntry { LocatorKey = "btnSave", Outcome = "accepted" });

                Assert.True(File.Exists(htmlPath));
                var htmlContent = File.ReadAllText(htmlPath);
                Assert.Contains("<!doctype html>", htmlContent);

                // No leftover temp files for this sink. Scope the scan to this sink's own
                // path prefix - the report lives under the shared system temp dir, so a bare
                // "*.tmp" scan would trip over every other process's temp files.
                var prefix = Path.GetFileName(_tempReportPath);
                var leftoverTempFiles = Directory.GetFiles(Path.GetDirectoryName(htmlPath)!, prefix + "*.tmp");
                Assert.Empty(leftoverTempFiles);
            }
            finally
            {
                if (File.Exists(htmlPath))
                {
                    File.Delete(htmlPath);
                }
            }
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
        public void HealingReportFileSink_LoadReport_ToleratesOldShapedEntries_WithMissingFieldsNull()
        {
            // Each JSON Lines entry is deserialized independently (#424), so a line written by
            // an older build - before AgreedProviders (v5/#10), ProviderAttempts (v6/#11),
            // Outcome/Platform/ProviderErrors (v7/#82), or CandidateIdentity/
            // ReconciliationDisposition (v8/#144) existed - reads back fine with those fields
            // left null. This is the same nullable-field tolerance the old single-JSON-array
            // format relied on for its in-place schema upgrade, now expressed per line instead
            // of by rewriting the whole document on every Record() call.
            var v4ShapedLine = @"{ ""LocatorKey"": ""old-v4"", ""Source"": ""Claude"", ""ReviewStatus"": ""accepted-with-llm"", ""Score"": 0.4, ""ConfidenceThreshold"": 0.5, ""CandidateCount"": 3, ""LlmConfidence"": 0.9, ""LlmProviderName"": ""Claude"" }";
            var v7ShapedLine = @"{ ""LocatorKey"": ""old-v7"", ""Source"": ""heuristic"", ""ReviewStatus"": ""accepted"", ""Outcome"": ""accepted"", ""Score"": 0.9, ""ConfidenceThreshold"": 0.5, ""CandidateCount"": 2 }";
            File.WriteAllText(_tempReportPath, v4ShapedLine + "\n" + v7ShapedLine + "\n");

            var report = ReadReport(_tempReportPath);

            Assert.Equal(new[] { "old-v4", "old-v7" }, report.Events.Select(e => e.LocatorKey));
            var v4Entry = report.Events[0];
            Assert.Null(v4Entry.AgreedProviders);
            Assert.Null(v4Entry.ProviderAttempts);
            Assert.Null(v4Entry.Outcome);
            Assert.Null(v4Entry.Platform);
            Assert.Null(v4Entry.ProviderErrors);
            Assert.Null(v4Entry.CandidateIdentity);
            Assert.Null(v4Entry.ReconciliationDisposition);

            var v7Entry = report.Events[1];
            Assert.Equal("accepted", v7Entry.Outcome);
            Assert.Null(v7Entry.CandidateIdentity);
            Assert.Null(v7Entry.ReconciliationDisposition);
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
        public void HealingReportHtmlRenderer_ShowsConsensusInsteadOfANumber_ForLlmEntries()
        {
            // Regression guard (#253): an LLM row's threshold column must not print a number.
            // ConfidenceThreshold does not describe how an LLM pick was accepted (consensus
            // does) - printing it (even a deliberately non-meaningful 0.00 placeholder) would
            // still look like a real bar the pick had to clear.
            var accepted = new UiElementInfo { ControlType = "Edit", AutomationId = "txtNew" };
            var llmEntry = HealingReportEntry.FromHealResult(
                "LoginPage.Email",
                new UiElementInfo { ControlType = "Edit", AutomationId = "txtOld" },
                accepted,
                new HealResult
                {
                    Matched = accepted,
                    Source = HealSource.Llm,
                    Score = 0.4,
                    LlmConfidence = 0.7,
                    ConfidenceThreshold = 0.0,
                    AgreedProviders = new[] { "Claude", "Gemini" },
                });

            var heuristicEntry = HealingReportEntry.FromHealResult(
                "LoginPage.Password",
                new UiElementInfo { ControlType = "Edit", AutomationId = "txtOldPw" },
                accepted,
                new HealResult
                {
                    Matched = accepted,
                    Source = HealSource.Heuristic,
                    Score = 0.9,
                    ConfidenceThreshold = 0.5,
                });

            var doc = new HealingReportDocument();
            doc.Events.Add(llmEntry);
            doc.Events.Add(heuristicEntry);
            var html = HealingReportHtmlRenderer.Render(doc);

            Assert.Contains("0.70 / consensus", html);
            Assert.Contains("0.90 / 0.50", html);
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
        public void HealingReportFileSink_LoadReport_IgnoresUnknownFutureFields_InsteadOfThrowing()
        {
            // The retired single-JSON-array format threw NotSupportedException for a
            // document-wide SchemaVersion newer than the build understood. JSON Lines drops
            // that gate (#424): every entry is self-contained, and System.Text.Json already
            // ignores properties it does not recognize, so a line written by a future build
            // with an extra field this build has never heard of is read without error - the
            // unknown value is dropped rather than misread or rejected outright.
            var futureLine = @"{ ""LocatorKey"": ""from-the-future"", ""Source"": ""heuristic"", ""ReviewStatus"": ""accepted"", ""SomeFieldThisBuildDoesNotKnowAbout"": ""value"" }";
            File.WriteAllText(_tempReportPath, futureLine + "\n");
            var sink = new HealingReportFileSink(_tempReportPath, htmlFilePath: null);

            sink.Record(new HealingReportEntry { LocatorKey = "current" });

            var report = sink.LoadReport();
            Assert.Equal(new[] { "from-the-future", "current" }, report.Events.Select(e => e.LocatorKey));
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
                ScoreBreakdown = new ScoreComponents(controlTypeScore: 0.0, nameScore: 0.8),
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
                ScoreBreakdown = new ScoreComponents(controlTypeScore: 1.0),
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
                ScoreBreakdown = new ScoreComponents(controlTypeScore: 0.2),
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

            var engine = new SelfHealingEngine(repository, llmProviders: new ILlmHealingProvider[] { provider, seconder }, mode: HealingMode.AutoHeal);

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

            var engine = new SelfHealingEngine(repository, llmProviders: new ILlmHealingProvider[] { provider, seconder }, mode: HealingMode.AutoHeal);

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

        // --- #370: repository ownership reconciliation ------------------------------------------

        [Fact]
        public void SelfHealingEngine_ReconcileAgainstRepository_DefaultsOff()
        {
            Assert.False(new SelfHealingEngine().ReconcileAgainstRepository);
            Assert.False(SelfHealingEngine.Create(ThresholdProfile.Balanced).ReconcileAgainstRepository);
            Assert.True(new SelfHealingEngine(reconcileAgainstRepository: true).ReconcileAgainstRepository);
        }

        [Fact]
        public async Task ReconcileAgainstRepository_Off_HealsDeletedElementOntoAnotherLocatorsElement()
        {
            // The classic deleted-element false heal: 'titles_combo' points at a control that no
            // longer exists; the only structurally similar survivor is the element another
            // authored locator ('angle_combo') already owns. Without reconciliation the engine
            // accepts it.
            var (repository, expected, tree) = BuildOwnershipConflictScenario();
            var engine = new SelfHealingEngine(repository, SimilarityWeights.FromProfile(ThresholdProfile.Balanced),
                reportSink: new HealingReportFileSink(_tempReportPath), mode: HealingMode.Observe,
                reconcileAgainstRepository: false);

            var result = await engine.ResolveAndRecordAsync("titles_combo", expected, tree);

            Assert.True(result.IsConfident);
            Assert.Equal("angleCombo", result.Matched!.AutomationId);
            Assert.False(result.RejectedByReconciliation);
        }

        [Fact]
        public async Task ReconcileAgainstRepository_On_DeclinesHealOntoAnotherLocatorsElement()
        {
            var (repository, expected, tree) = BuildOwnershipConflictScenario();
            var engine = new SelfHealingEngine(repository, SimilarityWeights.FromProfile(ThresholdProfile.Balanced),
                reportSink: new HealingReportFileSink(_tempReportPath), mode: HealingMode.Observe,
                reconcileAgainstRepository: true);

            var result = await engine.ResolveAndRecordAsync("titles_combo", expected, tree);

            Assert.False(result.IsConfident);
            Assert.True(result.RejectedByReconciliation);
            Assert.Equal(HealResolutionStatus.OwnershipConflict, result.ResolutionStatus);
            Assert.Equal(BatchReconciliationDisposition.DeclinedByStrongerClaim, result.ReconciliationDisposition);
            // The proposed match is still available for diagnostics, just not acceptable.
            Assert.Equal("angleCombo", result.Matched!.AutomationId);
        }

        [Fact]
        public async Task ReconcileAgainstRepository_On_StillHealsGenuineDrift_NoOwnershipCost()
        {
            // Both combos still exist; 'titles_combo' only lost its AutomationId. No other
            // locator resolves onto its element, so reconciliation must leave the heal alone.
            var repository = new LocatorRepository(_tempRepoPath);
            var titles = ComboSnapshot("titlesCombo", 0);
            var angle = ComboSnapshot("angleCombo", 1);
            repository.Upsert("titles_combo", titles, platform: "windows-uia");
            repository.Upsert("angle_combo", angle, platform: "windows-uia");

            var tree = new UiElementInfo { ControlType = "Window", Children =
            {
                new UiElementInfo { ControlType = "ToolBar", Children =
                {
                    ComboSnapshot("titlesCombo_v2", 0),
                    ComboSnapshot("angleCombo", 1),
                }},
            }};

            var engine = new SelfHealingEngine(repository, SimilarityWeights.FromProfile(ThresholdProfile.Balanced),
                reportSink: new HealingReportFileSink(_tempReportPath), mode: HealingMode.Observe,
                reconcileAgainstRepository: true);

            var result = await engine.ResolveAndRecordAsync("titles_combo", titles, tree);

            Assert.True(result.IsConfident);
            Assert.False(result.RejectedByReconciliation);
            Assert.Equal("titlesCombo_v2", result.Matched!.AutomationId);
        }

        private static UiElementInfo ComboSnapshot(string automationId, int siblingIndex, int siblingCount = 2) => new()
        {
            ControlType = "ComboBox",
            AutomationId = automationId,
            ParentControlType = "ToolBar",
            SiblingIndex = siblingIndex,
            SiblingCount = siblingCount,
            BoundingRectangle = new BoundingRectangle(10, 10 + siblingIndex * 34, 120, 24),
        };

        private (LocatorRepository repository, UiElementInfo expected, UiElementInfo tree) BuildOwnershipConflictScenario()
        {
            var repository = new LocatorRepository(_tempRepoPath);
            // Two adjacent, structurally indistinguishable toolbar combo boxes. 'titlesCombo' is
            // then deleted and the survivor reflows into the single remaining slot, so it is now
            // a perfect structural match for BOTH stale locators - and it is 'angle_combo' that
            // still legitimately owns it.
            var stale = new UiElementInfo
            {
                ControlType = "ComboBox",
                AutomationId = "titlesCombo",
                ParentControlType = "ToolBar",
                SiblingIndex = 0,
                SiblingCount = 1,
                BoundingRectangle = new BoundingRectangle(10, 10, 120, 24),
            };
            var angle = new UiElementInfo
            {
                ControlType = "ComboBox",
                AutomationId = "angleCombo",
                ParentControlType = "ToolBar",
                SiblingIndex = 0,
                SiblingCount = 1,
                BoundingRectangle = new BoundingRectangle(10, 10, 120, 24),
            };
            repository.Upsert("titles_combo", stale, platform: "windows-uia");
            repository.Upsert("angle_combo", angle, platform: "windows-uia");

            var tree = new UiElementInfo { ControlType = "Window", Children =
            {
                new UiElementInfo { ControlType = "ToolBar", Children =
                {
                    new UiElementInfo
                    {
                        ControlType = "ComboBox",
                        AutomationId = "angleCombo",
                        ParentControlType = "ToolBar",
                        SiblingIndex = 0,
                        SiblingCount = 1,
                        BoundingRectangle = new BoundingRectangle(10, 10, 120, 24),
                    },
                }},
            }};

            return (repository, stale, tree);
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
