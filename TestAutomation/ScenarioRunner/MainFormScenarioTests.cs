using Discovery;
using UiModel;
using FlaUI.Core.AutomationElements;
using SelfHealing;

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
            var realEmailNode = FindByAutomationId(currentTree, "txtEmail")
                ?? throw new InvalidOperationException("txtEmail was not found in the live tree, test data is invalid.");

            // Simulate a locator recorded "last sprint": back then the AutomationId was
            // "txtEmailAddress", later renamed to "txtEmail" in a refactor. All other
            // structural information comes from the real tree - only the AutomationId
            // is deliberately stale/wrong.
            var staleExpected = new UiElementInfo
            {
                ControlType = realEmailNode.ControlType,
                Name = realEmailNode.Name,
                AutomationId = "txtEmailAddress",
                ParentControlType = realEmailNode.ParentControlType,
                ParentAutomationId = realEmailNode.ParentAutomationId,
                SiblingIndex = realEmailNode.SiblingIndex,
                SiblingCount = realEmailNode.SiblingCount,
                BoundingRectangle = realEmailNode.BoundingRectangle,
            };

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

        private static UiElementInfo? FindByAutomationId(UiElementInfo node, string automationId)
        {
            if (node.AutomationId == automationId)
            {
                return node;
            }

            foreach (var child in node.Children)
            {
                var found = FindByAutomationId(child, automationId);
                if (found is not null)
                {
                    return found;
                }
            }

            return null;
        }

        public void Dispose()
        {
            _connector.Dispose();
        }
    }
}
