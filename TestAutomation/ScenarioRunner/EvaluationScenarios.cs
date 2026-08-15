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
            string? GroundTruthAutomationId,
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
        public string? GroundTruthAutomationId { get; }
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
            CreateUndecidableSplitExportActionScenario(),
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
                ClassName = "GeneralSettingsTab",
                ParentControlType = tabControl.ControlType,
                ParentAutomationId = tabControl.AutomationId,
                BoundingRectangle = new BoundingRectangle(100, 10, 90, 30),
            };

            // Target
            var tabSettings = new UiElementInfo
            {
                ControlType = "TabItem",
                AutomationId = "tabItem_1",
                Name = "Preferences",
                ClassName = "PreferencesTab",
                ParentControlType = tabControl.ControlType,
                ParentAutomationId = tabControl.AutomationId,
                BoundingRectangle = new BoundingRectangle(195, 10, 90, 30),
            };

            tabControl.Children.Add(tabGeneral);
            tabControl.Children.Add(tabSettings);

            // Stale expected snapshot: had "Settings & Preferences", ClassName "PreferencesTab"
            // BoundingRectangle (147.5, 10, 90, 30) with center x=192.5 is exactly equidistant
            // (dx=47.5px) between tabItem_0 (center x=145) and tabItem_1 (center x=240), so
            // position similarity is identical (0.8417) on both candidates.
            var expected = new UiElementInfo
            {
                ControlType = "TabItem",
                AutomationId = "tabSettingsPrefs",
                Name = "Settings & Preferences",
                ClassName = "PreferencesTab",
                ParentControlType = "TabControl",
                ParentAutomationId = "tabHost",
                TestIntent = "Open the user preferences tab",
                BoundingRectangle = new BoundingRectangle(147.5, 10, 90, 30),
            };

            // Ground Truth Defensibility Audit:
            // 1. Primary distinguishing evidence: ClassName ("PreferencesTab" matching tabItem_1 vs "GeneralSettingsTab").
            //    ClassName is sent to the LLM in the prompt (LlmHealingPrompt.cs) but is NOT scored by the heuristic
            //    scorer (SimilarityScorer.cs), cleanly decoupling LLM disambiguation from the heuristic margin gate.
            // 2. Supporting metadata: TestIntent ("Open the user preferences tab") provides semantic intent context.
            // 3. Heuristic behavior: ControlType (1.0), ParentControlType (1.0), and Position similarity (0.8417)
            //    are identical on both candidates. Name similarity is 0.3636 for tabItem_0 and 0.5000 for tabItem_1.
            //    Total scores: tabItem_0 = 0.8037, tabItem_1 = 0.8358. Runner-up margin is exactly 0.0321
            //    (< 0.05 MinimumCandidateMargin), comfortably gating heuristic resolution and invoking LLM fallback.
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

            // Ground Truth Defensibility Audit:
            // 1. Primary distinguishing evidence: Semantic function and token overlap. The stale locator was the primary
            //    search input ("Search Query", ID "txtSearchFilter") in the top toolbar. In the redesigned sidebar,
            //    "queryInputField" ("Filter Query") is the primary query input field (sharing the query role and "Query" token),
            //    whereas "tagFilterField" ("Tag Filter") is a specialized secondary tag filter.
            // 2. Heuristic behavior: Distance shifted > 600px (position similarity = 0.0) and parent container changed
            //    from ToolBar to Group (parent score = 0.0). Candidate score is ~0.38 (< 0.50 MinimumConfidence),
            //    so the heuristic resolver fails the confidence gate and invokes LLM fallback.
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

            // Ground Truth Defensibility Audit:
            // 1. Primary distinguishing evidence: Action semantics and button hierarchy. The stale button was the
            //    affirmative action to complete the checkout ("Complete & Confirm Order", ID "btn-checkout-confirm").
            //    In the new modal dialog, "dyn_btn_89234" ("Confirm Payment", styled as "btn-primary") is the direct
            //    affirmative completion action, while "dyn_btn_89235" ("Continue Shopping", "btn-secondary") is the
            //    cancellation/dismissal action.
            // 2. Heuristic behavior: ID was regenerated as dynamic "dyn_btn_89234", class hashed to "c-89234", parent
            //    changed from "div" to "dialog", and position shifted > 350px (position score = 0.0). Total heuristic
            //    score is ~0.33 (< 0.50 MinimumConfidence), requiring LLM fallback.
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

            // Ground Truth Defensibility Audit:
            // 1. Primary distinguishing evidence: Subject-matter domain and structural position. The stale locator
            //    specifically targeted API documentation ("REST API Documentation", ID "link-api-documentation") at
            //    sibling index 1 of 3. "nav_link_1" ("API Reference & Specs") is the API-specific navigation item at
            //    the matching sibling index 1 of 3 (x=160 vs stale x=150, center dx=5px). Decoys target product docs
            //    ("nav_link_0") and developer guides ("nav_link_2").
            // 2. Heuristic behavior: "Product Documentation" shares a 14-char suffix (" Documentation") with "REST API Documentation",
            //    giving high Levenshtein similarity that closely competes with "nav_link_1" (margin < 0.05). Sibling index
            //    and geometry are also close, keeping candidate margin below 0.05 and triggering LLM fallback.
            return new EvaluationScenario(
                Name: "Web_AmbiguousNavigationLinks",
                Platform: "web-playwright",
                Expected: expected,
                CurrentTreeRoot: root,
                GroundTruthAutomationId: "nav_link_1",
                Description: "Web navigation links with ambiguous sibling semantic similarity.");
        }

        // Scenario 5: Explicitly undecidable scenario where a single generic action was split into equal-weight formats
        public static EvaluationScenario CreateUndecidableSplitExportActionScenario()
        {
            var root = new UiElementInfo
            {
                ControlType = "Window",
                AutomationId = "ReportWindow",
                Name = "Reports",
            };

            var toolbar = new UiElementInfo
            {
                ControlType = "Group",
                AutomationId = "exportToolbar",
                ParentControlType = root.ControlType,
                ParentAutomationId = root.AutomationId,
                BoundingRectangle = new BoundingRectangle(200, 80, 300, 80),
            };
            root.Children.Add(toolbar);

            var btnPdf = new UiElementInfo
            {
                ControlType = "Button",
                AutomationId = "btn_pdf",
                Name = "Download PDF",
                ParentControlType = toolbar.ControlType,
                ParentAutomationId = toolbar.AutomationId,
                BoundingRectangle = new BoundingRectangle(200, 100, 120, 40),
            };

            var btnCsv = new UiElementInfo
            {
                ControlType = "Button",
                AutomationId = "btn_csv",
                Name = "Download CSV",
                ParentControlType = toolbar.ControlType,
                ParentAutomationId = toolbar.AutomationId,
                BoundingRectangle = new BoundingRectangle(330, 100, 120, 40),
            };

            toolbar.Children.Add(btnPdf);
            toolbar.Children.Add(btnCsv);

            // Stale expected snapshot: had "Download Report", placed at midpoint (250, 100, 150, 40) with center x=325,
            // exactly dx=65px equidistant to btn_pdf (center x=260) and btn_csv (center x=390).
            var expected = new UiElementInfo
            {
                ControlType = "Button",
                AutomationId = "btnDownloadReport",
                Name = "Download Report",
                ParentControlType = "Group",
                ParentAutomationId = "exportToolbar",
                BoundingRectangle = new BoundingRectangle(250, 100, 150, 40),
            };

            // Ground Truth Defensibility Audit:
            // 1. Explicitly Undecidable: The stale locator "Download Report" was refactored into two equal-weight export
            //    formats ("Download PDF" and "Download CSV"). Neither candidate is uniquely justifiable over the other:
            //    both share the identical prefix "Download", identical ControlType "Button", identical parent container,
            //    identical dimensions (120x40), and symmetric geometry. GroundTruthAutomationId is deliberately null.
            // 2. Heuristic behavior: ControlType (1.0), Parent (1.0), Name similarity (9/15 = 0.6000), and Position
            //    similarity (1 - 65/300 = 0.7833) are identical on both candidates. Total scores are identical (0.8421),
            //    yielding an exact runner-up margin of 0.0000 (< 0.05 MinimumCandidateMargin), forcing LLM fallback.
            // 3. Expected LLM outcome: No consensus (models should refuse to guess between equal-weight options; agreement
            //    on either candidate is recorded in UndecidableConsensusCount to measure correlated hallucination).
            return new EvaluationScenario(
                Name: "Desktop_UndecidableSplitExportAction",
                Platform: "windows-uia",
                Expected: expected,
                CurrentTreeRoot: root,
                GroundTruthAutomationId: null,
                Description: "Explicitly undecidable split action button where no single candidate is the correct answer.");
        }
    }
}
