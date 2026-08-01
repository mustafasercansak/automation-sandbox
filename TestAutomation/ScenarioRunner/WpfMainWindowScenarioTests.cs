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
            ).Result;

            Assert.NotNull(comboElement);
            var combo = comboElement!.AsComboBox();
            combo.Select("Corporate");

            // CompanyPanel (a GroupBox): deliberately has no AutomationProperties.AutomationId
            // set in MainWindow.xaml. We find it by ControlType instead - the WPF-flavored
            // version of the exact same weak-locator problem SelfHealing is meant to solve.
            // Retry, not an immediate lookup: WPF doesn't necessarily finish re-rendering the
            // panel synchronously within combo.Select() returning.
            var companyPanel = Retry.WhileNull(
                () => window.FindFirstDescendant(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Group)),
                timeout: TimeSpan.FromSeconds(5)
            ).Result;

            Assert.NotNull(companyPanel);
            var rect = companyPanel!.Properties.BoundingRectangle.ValueOrDefault;
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
        public async Task SelfHealing_LowConfidenceMatch_FallsBackToLlm_WhenApiKeyConfigured()
        {
            using var httpClient = new HttpClient();
            ILlmHealingProvider[] providers = { new ClaudeHealingProvider(httpClient), new GeminiHealingProvider(httpClient) };
            if (!providers.Any(p => p.IsAvailable))
            {
                Console.WriteLine("[SelfHealing] No provider API keys configured - skipping LLM fallback live check.");
                return;
            }

            var window = _connector.GetMainWindow();
            var currentTree = UiTreeWalker.BuildTree(window);

            // Same harder corruption as MainFormScenarioTests' equivalent test: forces the
            // heuristic score below MinimumConfidence so ResolveAsync actually reaches the LLM
            // fallback branch, proving the fallback works on the WPF app too (not just WinForms).
            var staleExpected = UiElementSnapshot.CaptureByAutomationId(currentTree, "txtEmail")
                ?? throw new InvalidOperationException("txtEmail was not found in the live tree, test data is invalid.");
            staleExpected.AutomationId = "txtEmailAddress";
            staleExpected.Name = "Unrelated Label Text";
            staleExpected.ParentControlType = "UnrelatedParentType";
            staleExpected.SiblingIndex += 5;
            staleExpected.SiblingCount += 5;
            staleExpected.BoundingRectangle = new BoundingRectangle(99999, 99999, 50, 20);

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
    }
}
