using System.Collections.Generic;
using UiModel;

namespace ScenarioRunner
{
    // A plain class rather than a positional record on purpose: ScenarioRunner also targets
    // net48, where a record's generated `init` accessors need
    // System.Runtime.CompilerServices.IsExternalInit - a type that only exists from .NET 5
    // onwards. Using a record here compiles cleanly on net8.0 (so it passes on the Linux CI
    // leg) and fails the Windows leg with six CS0518 errors. Every other DTO in this codebase
    // is a plain class for the same cross-platform reason.
    public sealed class EvaluationScenario
    {
        public EvaluationScenario(
            string Name,
            string Platform,
            UiElementInfo Expected,
            UiElementInfo CurrentTreeRoot,
            string GroundTruthAutomationId,
            string Description)
        {
            this.Name = Name;
            this.Platform = Platform;
            this.Expected = Expected;
            this.CurrentTreeRoot = CurrentTreeRoot;
            this.GroundTruthAutomationId = GroundTruthAutomationId;
            this.Description = Description;
        }

        public string Name { get; }
        public string Platform { get; }
        public UiElementInfo Expected { get; }
        public UiElementInfo CurrentTreeRoot { get; }
        public string GroundTruthAutomationId { get; }
        public string Description { get; }
    }

    public static class EvaluationScenarios
    {
        public static IReadOnlyList<EvaluationScenario> All => new[]
        {
            CreateAmbiguousSiblingTabsScenario(),
            CreateShiftedAndRenamedPanelScenario(),
            CreateDynamicClassAndIdModalButtonScenario(),
            CreateAmbiguousNavigationLinksScenario(),
        };

        // Scenario 1: Ambiguous desktop sibling tab buttons with close margins (< 0.05)
        public static EvaluationScenario CreateAmbiguousSiblingTabsScenario()
        {
            var root = new UiElementInfo
            {
                ControlType = "Window",
                AutomationId = "MainAppWindow",
                Name = "Application",
            };

            var tabControl = new UiElementInfo
            {
                ControlType = "TabControl",
                AutomationId = "tabHost",
                ParentControlType = root.ControlType,
                ParentAutomationId = root.AutomationId,
                BoundingRectangle = new BoundingRectangle(10, 10, 500, 300),
            };
            root.Children.Add(tabControl);

            var tabGeneral = new UiElementInfo
            {
                ControlType = "TabItem",
                AutomationId = "tabItem_0",
                Name = "Settings",
                ParentControlType = tabControl.ControlType,
                ParentAutomationId = tabControl.AutomationId,
                SiblingIndex = 0,
                SiblingCount = 2,
                BoundingRectangle = new BoundingRectangle(100, 10, 90, 30),
            };

            // Target
            var tabSettings = new UiElementInfo
            {
                ControlType = "TabItem",
                AutomationId = "tabItem_1",
                Name = "Preferences",
                ParentControlType = tabControl.ControlType,
                ParentAutomationId = tabControl.AutomationId,
                SiblingIndex = 1,
                SiblingCount = 2,
                BoundingRectangle = new BoundingRectangle(105, 10, 90, 30),
            };

            tabControl.Children.Add(tabGeneral);
            tabControl.Children.Add(tabSettings);

            // Stale expected snapshot: had "Settings & Preferences", stale ID tabSettingsPrefs
            // Equidistant in geometry and close in Levenshtein score (margin < 0.05)
            var expected = new UiElementInfo
            {
                ControlType = "TabItem",
                AutomationId = "tabSettingsPrefs",
                Name = "Settings & Preferences",
                ParentControlType = "TabControl",
                ParentAutomationId = "tabHost",
                BoundingRectangle = new BoundingRectangle(102, 10, 90, 30),
            };

            return new EvaluationScenario(
                Name: "Desktop_AmbiguousSiblingTabs",
                Platform: "windows-uia",
                Expected: expected,
                CurrentTreeRoot: root,
                GroundTruthAutomationId: "tabItem_1",
                Description: "Ambiguous desktop sibling tab items with close heuristic candidate margins (< 0.05).");
        }

        // Scenario 2: Desktop panel with shifted coordinates (> 300px) and renamed container
        public static EvaluationScenario CreateShiftedAndRenamedPanelScenario()
        {
            var root = new UiElementInfo
            {
                ControlType = "Window",
                AutomationId = "MainWindow",
                Name = "Dashboard",
            };

            var sidebar = new UiElementInfo
            {
                ControlType = "Group",
                AutomationId = "sidebarContainer",
                ParentControlType = root.ControlType,
                ParentAutomationId = root.AutomationId,
                BoundingRectangle = new BoundingRectangle(600, 400, 300, 400),
            };
            root.Children.Add(sidebar);

            // Target
            var targetInput = new UiElementInfo
            {
                ControlType = "Edit",
                AutomationId = "queryInputField",
                Name = "Filter Query",
                ParentControlType = sidebar.ControlType,
                ParentAutomationId = sidebar.AutomationId,
                SiblingIndex = 0,
                SiblingCount = 2,
                BoundingRectangle = new BoundingRectangle(610, 420, 250, 30),
            };

            var decoyInput = new UiElementInfo
            {
                ControlType = "Edit",
                AutomationId = "tagFilterField",
                Name = "Tag Filter",
                ParentControlType = sidebar.ControlType,
                ParentAutomationId = sidebar.AutomationId,
                SiblingIndex = 1,
                SiblingCount = 2,
                BoundingRectangle = new BoundingRectangle(610, 460, 250, 30),
            };

            sidebar.Children.Add(targetInput);
            sidebar.Children.Add(decoyInput);

            // Stale expected snapshot was in top toolbar (10, 10) - distance is ~700px (position score = 0)
            var expected = new UiElementInfo
            {
                ControlType = "Edit",
                AutomationId = "txtSearchFilter",
                Name = "Search Query",
                ParentControlType = "ToolBar",
                ParentAutomationId = "topToolbar",
                BoundingRectangle = new BoundingRectangle(10, 10, 200, 30),
            };

            return new EvaluationScenario(
                Name: "Desktop_ShiftedAndRenamedPanel",
                Platform: "windows-uia",
                Expected: expected,
                CurrentTreeRoot: root,
                GroundTruthAutomationId: "queryInputField",
                Description: "Desktop input shifted by > 300px with renamed container and ID (heuristic score < 0.50).");
        }

        // Scenario 3: Web modal button with generated dynamic IDs and hashed class names
        public static EvaluationScenario CreateDynamicClassAndIdModalButtonScenario()
        {
            var root = new UiElementInfo
            {
                ControlType = "div",
                AutomationId = "app-root",
                ClassName = "app-container",
            };

            var modal = new UiElementInfo
            {
                ControlType = "dialog",
                AutomationId = "checkout-modal",
                ParentControlType = root.ControlType,
                ParentAutomationId = root.AutomationId,
                BoundingRectangle = new BoundingRectangle(200, 150, 400, 300),
            };
            root.Children.Add(modal);

            // Target
            var confirmBtn = new UiElementInfo
            {
                ControlType = "button",
                AutomationId = "dyn_btn_89234",
                Name = "Confirm Payment",
                ClassName = "btn btn-primary c-89234",
                ParentControlType = modal.ControlType,
                ParentAutomationId = modal.AutomationId,
                SiblingIndex = 0,
                SiblingCount = 2,
                BoundingRectangle = new BoundingRectangle(220, 380, 140, 40),
            };

            var cancelBtn = new UiElementInfo
            {
                ControlType = "button",
                AutomationId = "dyn_btn_89235",
                Name = "Continue Shopping",
                ClassName = "btn btn-secondary c-89235",
                ParentControlType = modal.ControlType,
                ParentAutomationId = modal.AutomationId,
                SiblingIndex = 1,
                SiblingCount = 2,
                BoundingRectangle = new BoundingRectangle(380, 380, 140, 40),
            };

            modal.Children.Add(confirmBtn);
            modal.Children.Add(cancelBtn);

            // Stale expected snapshot: had static ID "btn-checkout-confirm" and old class
            var expected = new UiElementInfo
            {
                ControlType = "button",
                AutomationId = "btn-checkout-confirm",
                Name = "Complete & Confirm Order",
                ClassName = "btn-confirm-checkout-legacy",
                ParentControlType = "div",
                ParentAutomationId = "checkout-dialog-old",
                BoundingRectangle = new BoundingRectangle(50, 50, 150, 40),
            };

            return new EvaluationScenario(
                Name: "Web_DynamicClassAndIdModalButton",
                Platform: "web-playwright",
                Expected: expected,
                CurrentTreeRoot: root,
                GroundTruthAutomationId: "dyn_btn_89234",
                Description: "Web modal button with dynamically regenerated IDs and CSS classes.");
        }

        // Scenario 4: Web navigation items differing subtly in semantic intent
        public static EvaluationScenario CreateAmbiguousNavigationLinksScenario()
        {
            var root = new UiElementInfo
            {
                ControlType = "nav",
                AutomationId = "main-navbar",
                ClassName = "navbar navbar-dark",
            };

            var linkDocs = new UiElementInfo
            {
                ControlType = "a",
                AutomationId = "nav_link_0",
                Name = "Product Documentation",
                ParentControlType = root.ControlType,
                ParentAutomationId = root.AutomationId,
                SiblingIndex = 0,
                SiblingCount = 3,
                BoundingRectangle = new BoundingRectangle(50, 10, 100, 30),
            };

            // Target
            var linkApi = new UiElementInfo
            {
                ControlType = "a",
                AutomationId = "nav_link_1",
                Name = "API Reference & Specs",
                ParentControlType = root.ControlType,
                ParentAutomationId = root.AutomationId,
                SiblingIndex = 1,
                SiblingCount = 3,
                BoundingRectangle = new BoundingRectangle(160, 10, 100, 30),
            };

            var linkGuides = new UiElementInfo
            {
                ControlType = "a",
                AutomationId = "nav_link_2",
                Name = "Developer Guides",
                ParentControlType = root.ControlType,
                ParentAutomationId = root.AutomationId,
                SiblingIndex = 2,
                SiblingCount = 3,
                BoundingRectangle = new BoundingRectangle(270, 10, 100, 30),
            };

            root.Children.Add(linkDocs);
            root.Children.Add(linkApi);
            root.Children.Add(linkGuides);

            // Stale expected snapshot: "REST API Documentation"
            var expected = new UiElementInfo
            {
                ControlType = "a",
                AutomationId = "link-api-documentation",
                Name = "REST API Documentation",
                ParentControlType = "nav",
                ParentAutomationId = "main-nav",
                SiblingIndex = 1,
                SiblingCount = 3,
                BoundingRectangle = new BoundingRectangle(150, 10, 110, 30),
            };

            return new EvaluationScenario(
                Name: "Web_AmbiguousNavigationLinks",
                Platform: "web-playwright",
                Expected: expected,
                CurrentTreeRoot: root,
                GroundTruthAutomationId: "nav_link_1",
                Description: "Web navigation links with ambiguous sibling semantic similarity.");
        }
    }
}
