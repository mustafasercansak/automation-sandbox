using System;
using System.IO;
using System.Linq;
using System.Net.Http;
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

            // A much harder corruption than the txtEmailAddress case above: parent, sibling
            // position, name, and bounding box are all wrong too. This is
            // deliberately pushed below MinimumConfidence so the heuristic result alone isn't
            // confident, forcing SelfHealingResolver.ResolveAsync to actually reach the LLM
            // fallback branch instead of trivially returning the heuristic match.
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
