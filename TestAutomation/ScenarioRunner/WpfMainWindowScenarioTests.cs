using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Discovery;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;
using LlmHealing;
using SelfHealing;
using UiModel;
namespace ScenarioRunner
{
    // Same Discovery/SelfHealing code as MainFormScenarioTests, pointed at the WPF app
    // instead of WinForms - this is the actual proof that the architecture is
    // framework-agnostic, not just a claim in a comment.
    public class WpfMainWindowScenarioTests : IDisposable
    {
        private const string WpfAppRelativePath = @"..\..\..\..\..\WpfApp\bin\Debug\net8.0-windows\WpfApp.exe";
        private readonly ApplicationConnector _connector;

        public WpfMainWindowScenarioTests()
        {
            var exePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, WpfAppRelativePath));
            _connector = ApplicationConnector.Launch(exePath);
        }

        [Fact]
        public void CreatingRecord_WhenRequiredFieldsAreFilled_AddsRowToDataGrid()
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
        public void WhenCorporateIsSelected_CompanyPanelBecomesVisible()
        {
            var window = _connector.GetMainWindow();
            var comboElement = Retry.WhileNull(
                () => window.FindFirstDescendant(cf => cf.ByAutomationId("cmbRecordType")),
                timeout: TimeSpan.FromSeconds(5)
            ).Result ?? throw new InvalidOperationException("cmbRecordType combo box was not found in WPF main window.");
            var combo = comboElement.AsComboBox();
            combo.Select("Corporate");

            // CompanyPanel deliberately has no AutomationProperties.AutomationId set in
            // MainWindow.xaml. Assert that the brittle id-based locator cannot see the
            // panel, then verify the visible child field instead of depending on WPF's
            // headerless GroupBox automation peer being materialized on every CI run.
            Assert.Null(window.FindFirstDescendant(cf => cf.ByAutomationId("CompanyPanel")));
            var companyNameField = Retry.WhileNull(
                () => window.FindFirstDescendant(cf => cf.ByAutomationId("txtCompanyName")),
                timeout: TimeSpan.FromSeconds(5)
            ).Result ?? throw new InvalidOperationException("txtCompanyName field was not found after selecting Corporate in WPF main window.");
            var rect = companyNameField.Properties.BoundingRectangle.ValueOrDefault;
            Assert.True(rect.Width > 0 && rect.Height > 0, $"Company panel should be visible but its bounding rectangle came back empty: {rect}");
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
            var currentTree = UiTreeWalker.BuildTree(window);
            var staleExpected = UiElementSnapshot.CaptureByAutomationId(currentTree, "txtEmail")
                ?? throw new InvalidOperationException("txtEmail was not found in the live tree, test data is invalid.");
            staleExpected.AutomationId = "txtEmailAddress";
            var directHit = window.FindFirstDescendant(cf => cf.ByAutomationId("txtEmailAddress"));
            Assert.Null(directHit);
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

            // End-to-end: a real heal against the live WPF app, persisted to an actual repository
            // file on disk and read back - the WPF-flavored counterpart of the same proof in
            // MainFormScenarioTests, confirming the repository is as framework-agnostic as Discovery/SelfHealing.
            var repositoryPath = Path.Combine(Path.GetTempPath(), "WpfLocatorRepository_" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var repository = new LocatorRepository(repositoryPath);
                var entry = LocatorHealingHistoryEntryFactory.FromHealResult(healResult, previousSnapshot: staleExpected);
                repository.Upsert("MainWindow.Email", healResult.Matched!, entry, applicationName: "WpfApp", platform: "windows-uia");

                var persisted = repository.Find("MainWindow.Email");
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

            // Same harder corruption as MainFormScenarioTests' equivalent test: forces the
            // heuristic score below MinimumConfidence (0.50) so ResolveAsync reaches the LLM fallback
            // branch while keeping txtEmail as top shortlist candidate c0. Use a deterministic
            // fake provider so CI doesn't depend on live API quota or billing state.
            var staleExpected = UiElementSnapshot.CaptureByAutomationId(currentTree, "txtEmail")
                ?? throw new InvalidOperationException("txtEmail was not found in the live tree, test data is invalid.");
            staleExpected.AutomationId = "txtEmailAddress";
            staleExpected.Name = "Unrelated Label Text";
            staleExpected.ParentControlType = "UnrelatedParentType";
            staleExpected.BoundingRectangle = new BoundingRectangle(99999, 99999, 50, 20);
            ILlmHealingProvider[] providers = { new DeterministicProvider("FakeLlm", "txtEmail", 0.85) };
            var healResult = await SelfHealingResolver.ResolveAsync(staleExpected, currentTree, providers);
            Assert.NotNull(healResult.Matched);
            Assert.Equal("txtEmail", healResult.Matched!.AutomationId);
            Assert.Equal(HealSource.Llm, healResult.Source);
            Assert.True(healResult.IsConfident, $"Expected a confident LLM-sourced match, but got: {healResult.LlmProviderName}, confidence={healResult.LlmConfidence}");
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

            public Task<LlmHealingResult> ResolveAsync(UiElementInfo expected, IReadOnlyList<CandidateScore> candidates, CancellationToken cancellationToken = default)
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
