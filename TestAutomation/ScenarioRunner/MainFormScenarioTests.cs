using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Discovery;
using FlaUI.Core.AutomationElements;
using LlmHealing;
using SelfHealing;
using UiModel;
namespace ScenarioRunner
{
    // These tests actually launch the compiled WinFormsApp.exe and talk to it via FlaUI.UIA3.
    // UIA3 relies on Windows' UI Automation COM APIs, so it only runs on Windows.
    public class MainFormScenarioTests : IDisposable
    {
        private const string WinFormsAppRelativePath = @"..\..\..\..\..\WinFormsApp\bin\Debug\net48\WinFormsApp.exe";
        private readonly ApplicationConnector _connector;

        public MainFormScenarioTests()
        {
            var exePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, WinFormsAppRelativePath));
            _connector = ApplicationConnector.Launch(exePath);
        }

        [Fact]
        public void Discover_MaxElements_StopsAndSetsHitMaxElements()
        {
            var window = _connector.GetMainWindow();
            var options = new DiscoveryOptions
            {
                MaxElements = 5,
                IncludeOffscreen = true
            };
            var result = UiTreeWalker.Discover(window, options);
            Assert.True(result.HitMaxElements, "Expected HitMaxElements to be true when MaxElements limit is hit.");
            Assert.True(result.CapturedCount <= 5, $"Expected CapturedCount <= 5, got {result.CapturedCount}");
        }

        [Fact]
        public void Discover_MaxDepth_SetsHitMaxDepth()
        {
            var window = _connector.GetMainWindow();
            var options = new DiscoveryOptions
            {
                MaxDepth = 1,
                IncludeOffscreen = true
            };
            var result = UiTreeWalker.Discover(window, options);
            Assert.True(result.HitMaxDepth, "Expected HitMaxDepth to be true when MaxDepth limit is 1.");
            Assert.NotNull(result.Root);
        }

        [Fact]
        public void Discover_IgnoredControlTypes_ExcludesNodes()
        {
            var window = _connector.GetMainWindow();
            var options = new DiscoveryOptions
            {
                IncludeOffscreen = true,
                IgnoredControlTypes = new HashSet<string> { "Button" }
            };
            var result = UiTreeWalker.Discover(window, options);
            Assert.True(result.SkippedCount > 0, "Expected SkippedCount > 0 when ignoring Button control types.");
            var allControls = Flatten(result.Root);
            Assert.DoesNotContain(allControls, c => c.ControlType.Equals("Button", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void Discover_Default_ReturnsTelemetryCounts()
        {
            var window = _connector.GetMainWindow();
            var options = new DiscoveryOptions
            {
                IncludeOffscreen = true
            };
            var result = UiTreeWalker.Discover(window, options);
            Assert.True(result.VisitedCount > 0);
            Assert.True(result.CapturedCount > 0);
            Assert.True(result.Elapsed > TimeSpan.Zero);
        }

        [Fact]
        public void Discover_PreCancelled_ReturnsCancelledResultWithoutReadingRoot()
        {
            var window = _connector.GetMainWindow();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var result = UiTreeWalker.Discover(window, cancellationToken: cancellation.Token);
            Assert.True(result.WasCancelled);
            Assert.NotNull(result.Root);
            Assert.Equal(0, result.VisitedCount);
            Assert.Equal(0, result.CapturedCount);
        }

        [Fact]
        public void Discover_RootRemainsTraversalAnchorWhenItsTypeIsIgnored()
        {
            var window = _connector.GetMainWindow();
            var options = new DiscoveryOptions
            {
                IncludeOffscreen = true,
                IgnoredControlTypes = new HashSet<string> { "Window" }
            };
            var result = UiTreeWalker.Discover(window, options);
            Assert.Equal("Window", result.Root.ControlType);
            Assert.True(result.CapturedCount > 0);
        }

        [Fact]
        public void Discover_InvalidLimits_AreRejected()
        {
            var window = _connector.GetMainWindow();
            Assert.Throws<ArgumentOutOfRangeException>(() => UiTreeWalker.Discover(
                window,
                new DiscoveryOptions { MaxDepth = -1 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => UiTreeWalker.Discover(
                window,
                new DiscoveryOptions { MaxElements = 0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => UiTreeWalker.Discover(
                window,
                new DiscoveryOptions { Timeout = TimeSpan.Zero }));
        }

        [Fact]
        public void CreatingRecord_WhenRequiredFieldsAreFilled_AddsRowToDataGridView()
        {
            var window = _connector.GetMainWindow();
            window.FindFirstDescendant(cf => cf.ByAutomationId("txtFirstName"))!.AsTextBox().Text = "Jane";
            window.FindFirstDescendant(cf => cf.ByAutomationId("txtLastName"))!.AsTextBox().Text = "Doe";
            window.FindFirstDescendant(cf => cf.ByAutomationId("txtEmail"))!.AsTextBox().Text = "jane.doe@example.com";
            window.FindFirstDescendant(cf => cf.ByAutomationId("btnSave"))!.AsButton().Invoke();
            var grid = window.FindFirstDescendant(cf => cf.ByAutomationId("dgvRecords"))!.AsDataGridView();
            Assert.Single(grid.Rows);
        }

        [Fact]
        public void WhenCorporateIsSelected_CompanyNamePanelBecomesVisible()
        {
            var window = _connector.GetMainWindow();
            var combo = window.FindFirstDescendant(cf => cf.ByAutomationId("cmbRecordType"))!.AsComboBox();
            combo.Select("Corporate");

            // panel1: control whose AutomationId is deliberately left meaningless (see MainForm.Designer.cs).
            // That's why we find it by ControlType instead of AutomationId - this is exactly the
            // problem the SelfHealing layer is meant to solve, demonstrated live here.
            var panel = window.FindFirstDescendant(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Pane))!;

            // IsOffscreen isn't supported on this native WinForms Panel (observed to throw
            // PropertyNotSupportedException in the real CI run) - bounding rectangle is always
            // supported and a more reliable visibility signal.
            var rect = panel.Properties.BoundingRectangle.ValueOrDefault;
            Assert.True(rect.Width > 0 && rect.Height > 0, $"Panel should be visible but its bounding rectangle came back empty: {rect}");
        }

        [Fact]
        public void UiTree_CanBeSerializedToJson_AndContainsExpectedControls()
        {
            var window = _connector.GetMainWindow();
            var tree = UiTreeWalker.BuildTree(window);
            var json = UiTreeSerializer.ToJson(tree);
            Assert.Contains("txtEmail", json);
            Assert.Contains("btnSave", json);
        }

        [Fact]
        public void SelfHealing_BrokenAutomationId_FindsCorrectElementInLiveApp()
        {
            var window = _connector.GetMainWindow();

            // Capture the real, live UI tree via Discovery - this is exactly what self-healing
            // operates on.
            var currentTree = UiTreeWalker.BuildTree(window);

            // Simulate a locator recorded "last sprint": back then the AutomationId was
            // "txtEmailAddress", later renamed to "txtEmail" in a refactor. All other
            // structural information comes from the real tree - only the AutomationId
            // is deliberately stale/wrong.
            var staleExpected = UiElementSnapshot.CaptureByAutomationId(currentTree, "txtEmail")
                ?? throw new InvalidOperationException("txtEmail was not found in the live tree, test data is invalid.");
            staleExpected.AutomationId = "txtEmailAddress";

            // A direct lookup with the stale id genuinely fails - a faithful simulation of a
            // live locator breaking.
            var directHit = window.FindFirstDescendant(cf => cf.ByAutomationId("txtEmailAddress"));
            Assert.Null(directHit);

            // Self-healing kicks in: finds the correct element on the same live tree using
            // structural similarity alone (never looking at AutomationId).
            var healResult = SelfHealingResolver.Resolve(staleExpected, currentTree);
            Assert.NotNull(healResult.Matched);
            Assert.Equal("txtEmail", healResult.Matched!.AutomationId);
            Assert.True(healResult.IsConfident, $"Expected a confident match, but the score was: {healResult.Score}");
        }

        [Fact]
        public void SelfHealing_PersistsHealedLocatorToRepository()
        {
            var window = _connector.GetMainWindow();
            var currentTree = UiTreeWalker.BuildTree(window);

            var staleExpected = UiElementSnapshot.CaptureByAutomationId(currentTree, "txtEmail")
                ?? throw new InvalidOperationException("txtEmail was not found in the live tree, test data is invalid.");
            staleExpected.AutomationId = "txtEmailAddress";

            var healResult = SelfHealingResolver.Resolve(staleExpected, currentTree);
            Assert.True(healResult.IsConfident, $"Expected a confident match, but the score was: {healResult.Score}");

            // End-to-end: a real heal against the live app, persisted to an actual repository
            // file on disk and read back - not just a synthetic in-memory round trip.
            var repositoryPath = Path.Combine(Path.GetTempPath(), "MainFormLocatorRepository_" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var repository = new LocatorRepository(repositoryPath);
                var entry = LocatorHealingHistoryEntryFactory.FromHealResult(healResult, previousSnapshot: staleExpected);
                repository.Upsert("MainForm.Email", healResult.Matched!, entry, applicationName: "WinFormsApp", platform: "windows-uia");

                var persisted = repository.Find("MainForm.Email");
                Assert.NotNull(persisted);
                Assert.Equal("txtEmail", persisted!.Snapshot.AutomationId);
                Assert.Single(persisted.HealingHistory);
                Assert.Equal("heuristic", persisted.HealingHistory[0].Source);
                Assert.Equal("txtEmailAddress", persisted.HealingHistory[0].PreviousSnapshot?.AutomationId);
            }
            finally
            {
                File.Delete(repositoryPath);
                File.Delete(repositoryPath + ".lock");
            }
        }

        [Fact]
        public async Task SelfHealing_LowConfidenceMatch_FallsBackToLlm()
        {
            var window = _connector.GetMainWindow();
            var currentTree = UiTreeWalker.BuildTree(window);

            // A much harder corruption than the txtEmailAddress case above: name, parent,
            // and bounding box are all wrong too. This is deliberately pushed below MinimumConfidence (0.50)
            // so the heuristic result alone isn't confident, forcing SelfHealingResolver.ResolveAsync
            // to reach the LLM fallback branch while keeping txtEmail as top shortlist candidate c0.
            // Use a deterministic fake provider here; real provider availability/quota is covered by
            // LlmHealingEvaluationTests as a non-gating comparison harness.
            var staleExpected = UiElementSnapshot.CaptureByAutomationId(currentTree, "txtEmail")
                ?? throw new InvalidOperationException("txtEmail was not found in the live tree, test data is invalid.");
            staleExpected.AutomationId = "txtEmailAddress";
            staleExpected.Name = "Unrelated Label Text";
            staleExpected.ParentControlType = "UnrelatedParentType";
            staleExpected.BoundingRectangle = new BoundingRectangle(99999, 99999, 50, 20);
            // Two providers, because consensus (#10) is what accepts an LLM pick - a single
            // provider is never acted on, however confident it claims to be.
            ILlmHealingProvider[] providers =
            {
                new DeterministicProvider("FakeLlmA", "txtEmail", 0.85),
                new DeterministicProvider("FakeLlmB", "txtEmail", 0.80),
            };
            var healResult = await SelfHealingResolver.ResolveAsync(staleExpected, currentTree, providers);
            Assert.NotNull(healResult.Matched);
            Assert.Equal("txtEmail", healResult.Matched!.AutomationId);
            Assert.Equal(HealSource.Llm, healResult.Source);
            Assert.True(healResult.IsConfident, $"Expected a confident LLM-sourced match, but got: {healResult.LlmProviderName}, confidence={healResult.LlmConfidence}");
        }

        private static List<UiElementInfo> Flatten(UiElementInfo root)
        {
            var list = new List<UiElementInfo> { root };
            foreach (var child in root.Children)
            {
                list.AddRange(Flatten(child));
            }
            return list;
        }

        public void Dispose()
        {
            _connector.Dispose();
        }

        private sealed class DeterministicProvider : ILlmHealingProvider
        {
            private readonly string _automationId;
            private readonly double _confidence;

            public DeterministicProvider(string name, string automationId, double confidence)
            {
                Name = name;
                _automationId = automationId;
                _confidence = confidence;
            }

            public string Name { get; }
            public bool IsAvailable => true;

            public Task<LlmHealingResult> ResolveAsync(
                UiElementInfo expected,
                IReadOnlyList<CandidateScore> candidates,
                string? platform = null,
                CancellationToken cancellationToken = default)
            {
                var matched = candidates.FirstOrDefault(c => c.Candidate.AutomationId == _automationId);
                return Task.FromResult(new LlmHealingResult
                {
                    ProviderName = Name,
                    Success = matched != null,
                    MatchedCandidateId = matched?.CandidateId,
                    MatchedAutomationId = matched?.Candidate.AutomationId,
                    Confidence = _confidence,
                    Reasoning = "deterministic test provider",
                });
            }
        }
    }
}
