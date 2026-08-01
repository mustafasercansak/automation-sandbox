using Discovery;
using UiModel;
using FlaUI.Core.AutomationElements;
using SelfHealing;

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

            var combo = window.FindFirstDescendant(cf => cf.ByAutomationId("cmbRecordType"))!.AsComboBox();
            combo.Select("Corporate");

            // CompanyPanel (a GroupBox): deliberately has no AutomationProperties.AutomationId
            // set in MainWindow.xaml. We find it by ControlType instead - the WPF-flavored
            // version of the exact same weak-locator problem SelfHealing is meant to solve.
            var companyPanel = window.FindFirstDescendant(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Group))!;

            var rect = companyPanel.Properties.BoundingRectangle.ValueOrDefault;
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
            var realEmailNode = FindByAutomationId(currentTree, "txtEmail")
                ?? throw new InvalidOperationException("txtEmail was not found in the live tree, test data is invalid.");

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

            var directHit = window.FindFirstDescendant(cf => cf.ByAutomationId("txtEmailAddress"));
            Assert.Null(directHit);

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
