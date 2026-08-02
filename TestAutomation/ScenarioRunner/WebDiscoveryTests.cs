using SelfHealing;
using UiModel;
using WebDiscovery;

namespace ScenarioRunner
{
    public class WebDiscoveryTests
    {
        [Fact]
        public void WebElementMapper_MapsDomTreeToUiElementTree_WithParentAndSiblingContext()
        {
            var root = new WebElementInfo
            {
                TagName = "body",
                BoundingRectangle = new BoundingRectangle(0, 0, 1024, 768),
            };
            root.Children.Add(new WebElementInfo
            {
                TagName = "input",
                Role = "textbox",
                AccessibleName = "Email",
                TestId = "email-input",
                Id = "email",
                NameAttribute = "email",
                ClassName = "field",
                TreeScope = "shadow-dom",
                BoundingRectangle = new BoundingRectangle(100, 80, 220, 32),
            });
            root.Children.Add(new WebElementInfo
            {
                TagName = "button",
                Role = "button",
                AccessibleName = "Save",
                TestId = "save-button",
                BoundingRectangle = new BoundingRectangle(100, 130, 120, 36),
            });

            var tree = WebElementMapper.ToUiElementTree(root);

            Assert.Equal("body", tree.ControlType);
            Assert.Equal(2, tree.Children.Count);
            Assert.Equal("Edit", tree.Children[0].ControlType);
            Assert.Equal("Email", tree.Children[0].Name);
            Assert.Equal("email-input", tree.Children[0].AutomationId);
            Assert.Equal("field [shadow-dom]", tree.Children[0].ClassName);
            Assert.Equal("body", tree.Children[0].ParentControlType);
            Assert.Equal(0, tree.Children[0].SiblingIndex);
            Assert.Equal(2, tree.Children[0].SiblingCount);
            Assert.Equal("Button", tree.Children[1].ControlType);
        }

        [Fact]
        public void WebElementMapper_HiddenOrOffscreenElementsUseZeroRectangle()
        {
            var root = new WebElementInfo { TagName = "body" };
            root.Children.Add(new WebElementInfo
            {
                TagName = "button",
                Role = "button",
                AccessibleName = "Save",
                TestId = "save-button",
                IsHidden = true,
                BoundingRectangle = new BoundingRectangle(100, 130, 120, 36),
            });
            root.Children.Add(new WebElementInfo
            {
                TagName = "input",
                Role = "textbox",
                AccessibleName = "Email",
                TestId = "email-input",
                IsOffscreen = true,
                BoundingRectangle = new BoundingRectangle(-2000, 80, 220, 32),
            });

            var tree = WebElementMapper.ToUiElementTree(root);

            Assert.Equal(0, tree.Children[0].BoundingRectangle.Width);
            Assert.Equal(0, tree.Children[0].BoundingRectangle.Height);
            Assert.Equal(0, tree.Children[1].BoundingRectangle.Width);
            Assert.Equal(0, tree.Children[1].BoundingRectangle.Height);
        }

        [Fact]
        public void PlaywrightLocatorEmitter_PrefersTestIdThenRoleThenIdThenNameThenCss()
        {
            var element = new WebElementInfo
            {
                TagName = "input",
                Role = "textbox",
                AccessibleName = "Email",
                TestId = "email-input",
                Id = "customer.email",
                NameAttribute = "email",
                CssSelector = "input[name=\"email\"]",
            };

            var suggestions = PlaywrightLocatorEmitter.Suggest(element);

            Assert.Equal(new[] { "TestId", "Role", "Id", "NameAttribute", "Css" }, suggestions.Select(s => s.Strategy).ToArray());
            Assert.Equal("page.GetByTestId(\"email-input\")", suggestions[0].Expression);
            Assert.Contains("AriaRole.Textbox", suggestions[1].Expression);
            Assert.Contains("#customer\\.email", suggestions[2].Expression);
        }

        [Fact]
        public void SelfHealingResolver_CanHealWebElementMappedThroughUiModel()
        {
            var expected = new WebElementInfo
            {
                TagName = "input",
                Role = "textbox",
                AccessibleName = "Email",
                TestId = "old-email-input",
                BoundingRectangle = new BoundingRectangle(100, 80, 220, 32),
            };
            var currentRoot = new WebElementInfo { TagName = "body" };
            currentRoot.Children.Add(new WebElementInfo
            {
                TagName = "input",
                Role = "textbox",
                AccessibleName = "Email",
                TestId = "email-input",
                BoundingRectangle = new BoundingRectangle(100, 80, 220, 32),
            });
            currentRoot.Children.Add(new WebElementInfo
            {
                TagName = "button",
                Role = "button",
                AccessibleName = "Save",
                TestId = "save-button",
                BoundingRectangle = new BoundingRectangle(100, 130, 120, 36),
            });

            var expectedSnapshot = WebElementMapper.ToUiElementTree(expected);
            var currentTree = WebElementMapper.ToUiElementTree(currentRoot);
            var result = SelfHealingResolver.Resolve(expectedSnapshot, currentTree, log: _ => { });

            Assert.True(result.IsConfident, $"Expected a confident web heal, but score was {result.Score}");
            Assert.Equal("email-input", result.Matched!.AutomationId);
        }

        [Fact]
        public void PlaywrightDomCaptureScript_ExposesAPlaywrightEvaluateFunction()
        {
            Assert.Contains("document.body", PlaywrightDomCaptureScript.JavaScript);
            Assert.Contains("data-testid", PlaywrightDomCaptureScript.JavaScript);
            Assert.Contains("BoundingRectangle", PlaywrightDomCaptureScript.JavaScript);
            Assert.Contains("shadowRoot", PlaywrightDomCaptureScript.JavaScript);
            Assert.Contains("contentDocument", PlaywrightDomCaptureScript.JavaScript);
            Assert.Contains("IsHidden", PlaywrightDomCaptureScript.JavaScript);
            Assert.Contains("IsOffscreen", PlaywrightDomCaptureScript.JavaScript);
        }

        [Fact]
        public void PlaywrightApplicationConnector_ParsesJsonToUiElementTree()
        {
            var json = @"{
                ""TagName"": ""body"",
                ""Children"": [
                    {
                        ""TagName"": ""button"",
                        ""Role"": ""button"",
                        ""AccessibleName"": ""Submit"",
                        ""TestId"": ""submit-btn""
                    }
                ]
            }";

            var tree = PlaywrightApplicationConnector.ParseJson(json);
            Assert.Equal("body", tree.ControlType);
            Assert.Single(tree.Children);
            Assert.Equal("Button", tree.Children[0].ControlType);
            Assert.Equal("submit-btn", tree.Children[0].AutomationId);
        }
    }
}

