using UiModel;
namespace ScenarioRunner
{
    public class UiElementSnapshotTests
    {
        [Fact]

        public void Capture_ClearsChildren_ButKeepsAllOtherFields()
        {
            var node = new UiElementInfo
            {
                ControlType = "Edit",
                Name = "Email",
                AutomationId = "txtEmail",
                ClassName = "TextBox",
                ParentControlType = "Window",
                ParentAutomationId = "MainForm",
                SiblingIndex = 2,
                SiblingCount = 7,
                BoundingRectangle = new BoundingRectangle(112, 70, 200, 23),
            };
            node.Children.Add(new UiElementInfo { ControlType = "Label", AutomationId = "lblChild" });
            var snapshot = UiElementSnapshot.Capture(node);
            Assert.Equal("Edit", snapshot.ControlType);
            Assert.Equal("Email", snapshot.Name);
            Assert.Equal("txtEmail", snapshot.AutomationId);
            Assert.Equal("TextBox", snapshot.ClassName);
            Assert.Equal("Window", snapshot.ParentControlType);
            Assert.Equal("MainForm", snapshot.ParentAutomationId);
            Assert.Equal(2, snapshot.SiblingIndex);
            Assert.Equal(7, snapshot.SiblingCount);
            Assert.Equal(112, snapshot.BoundingRectangle.X);
            Assert.Empty(snapshot.Children);
        }

        [Fact]

        public void CaptureByAutomationId_FindsNestedElement_AndClearsItsChildren()
        {
            var root = new UiElementInfo { ControlType = "Window", AutomationId = "MainForm" };
            var email = new UiElementInfo { ControlType = "Edit", AutomationId = "txtEmail" };
            email.Children.Add(new UiElementInfo { ControlType = "Popup", AutomationId = "autocompletePopup" });
            root.Children.Add(email);
            var snapshot = UiElementSnapshot.CaptureByAutomationId(root, "txtEmail");
            Assert.NotNull(snapshot);
            Assert.Equal("txtEmail", snapshot!.AutomationId);
            Assert.Empty(snapshot.Children);
        }

        [Fact]

        public void CaptureByAutomationId_ReturnsNull_WhenNotFound()
        {
            var root = new UiElementInfo { ControlType = "Window", AutomationId = "MainForm" };
            var snapshot = UiElementSnapshot.CaptureByAutomationId(root, "doesNotExist");
            Assert.Null(snapshot);
        }

        [Fact]

        public void CaptureFirst_CanFindElementWithoutAutomationId()
        {
            var root = new UiElementInfo { ControlType = "Window", AutomationId = "MainForm" };
            root.Children.Add(new UiElementInfo
            {
                ControlType = "Group",
                AutomationId = "",
                Name = "Company",
                ParentControlType = "Window",
                SiblingIndex = 0,
                SiblingCount = 1,
            });

            var snapshot = UiElementSnapshot.CaptureFirst(root, node =>
                node.ControlType == "Group" && node.Name == "Company");

            Assert.NotNull(snapshot);
            Assert.Equal("", snapshot!.AutomationId);
            Assert.Equal("Company", snapshot.Name);
            Assert.Empty(snapshot.Children);
        }

        [Fact]
        public void ToJson_ThenFromJson_RoundTrips_WithoutChildrenBloat()
        {
            var root = new UiElementInfo { ControlType = "Window", AutomationId = "MainForm" };
            var email = new UiElementInfo
            {
                ControlType = "Edit",
                AutomationId = "txtEmail",
                SiblingIndex = 2,
                SiblingCount = 2,
                BoundingRectangle = new BoundingRectangle(112, 70, 200, 23),
            };
            var sibling = new UiElementInfo { ControlType = "Button", AutomationId = "btnSave" };
            root.Children.Add(email);
            root.Children.Add(sibling);
            var json = UiElementSnapshot.ToJson(email);
            Assert.DoesNotContain("btnSave", json);
            var roundTripped = UiElementSnapshot.FromJson(json);
            Assert.Equal("txtEmail", roundTripped.AutomationId);
            Assert.Equal(2, roundTripped.SiblingIndex);
            Assert.Equal(112, roundTripped.BoundingRectangle.X);
            Assert.Equal(70, roundTripped.BoundingRectangle.Y);
            Assert.Equal(200, roundTripped.BoundingRectangle.Width);
            Assert.Equal(23, roundTripped.BoundingRectangle.Height);
            Assert.Empty(roundTripped.Children);
        }

        [Fact]
        public void UiTreeSerializer_RoundTrips_PreservesBoundingRectangle()
        {
            var root = new UiElementInfo
            {
                ControlType = "Window",
                AutomationId = "MainWindow",
                BoundingRectangle = new BoundingRectangle(0, 0, 800, 600),
            };
            var button = new UiElementInfo
            {
                ControlType = "Button",
                AutomationId = "btnSubmit",
                BoundingRectangle = new BoundingRectangle(150, 450, 100, 30),
            };
            root.Children.Add(button);

            var json = UiTreeSerializer.ToJson(root);
            var roundTripped = UiTreeSerializer.FromJson(json);

            Assert.Equal(0, roundTripped.BoundingRectangle.X);
            Assert.Equal(0, roundTripped.BoundingRectangle.Y);
            Assert.Equal(800, roundTripped.BoundingRectangle.Width);
            Assert.Equal(600, roundTripped.BoundingRectangle.Height);

            var roundTrippedButton = roundTripped.Children.Single();
            Assert.Equal(150, roundTrippedButton.BoundingRectangle.X);
            Assert.Equal(450, roundTrippedButton.BoundingRectangle.Y);
            Assert.Equal(100, roundTrippedButton.BoundingRectangle.Width);
            Assert.Equal(30, roundTrippedButton.BoundingRectangle.Height);
        }

        [Fact]
        public void LocatorRepositoryDocument_Defaults_AreVersionedAndUseLocatorKeyIdentity()
        {
            var document = new LocatorRepositoryDocument { ApplicationName = "DemoApp" };
            document.Locators.Add(new LocatorRecord
            {
                LocatorKey = "CustomerForm.Company",
                Snapshot = new UiElementInfo
                {
                    ControlType = "Group",
                    AutomationId = "",
                    Name = "Company",
                    BoundingRectangle = new BoundingRectangle(10, 20, 300, 400),
                },
            });

            var json = LocatorRepositorySerializer.ToJson(document);
            var roundTripped = LocatorRepositorySerializer.FromJson(json);

            Assert.Equal(LocatorRepositoryDocument.CurrentSchemaVersion, document.SchemaVersion);
            Assert.Equal("CustomerForm.Company", roundTripped.Locators[0].LocatorKey);
            Assert.Equal("", roundTripped.Locators[0].Snapshot.AutomationId);
            Assert.Equal(10, roundTripped.Locators[0].Snapshot.BoundingRectangle.X);
            Assert.Equal(20, roundTripped.Locators[0].Snapshot.BoundingRectangle.Y);
            Assert.Equal(300, roundTripped.Locators[0].Snapshot.BoundingRectangle.Width);
            Assert.Equal(400, roundTripped.Locators[0].Snapshot.BoundingRectangle.Height);
            Assert.Contains("SchemaVersion", json);
            Assert.Contains("LocatorKey", json);
        }

        [Fact]
        public void LocatorRepositorySerializer_RejectsUnsupportedSchemaVersion()
        {
            var document = new LocatorRepositoryDocument { SchemaVersion = 999 };

            Assert.Throws<NotSupportedException>(() => LocatorRepositorySerializer.ToJson(document));
        }
    }
}
