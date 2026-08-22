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
        public void WebElementMapper_HiddenElementsUseZeroRectangle_OffscreenElementsRetainRectangle()
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
                BoundingRectangle = new BoundingRectangle(100, 3000, 220, 32),
            });

            var tree = WebElementMapper.ToUiElementTree(root);

            // Hidden element loses geometry
            Assert.Equal(0, tree.Children[0].BoundingRectangle.Width);
            Assert.Equal(0, tree.Children[0].BoundingRectangle.Height);

            // Offscreen / below-the-fold element retains geometry
            Assert.Equal(220, tree.Children[1].BoundingRectangle.Width);
            Assert.Equal(32, tree.Children[1].BoundingRectangle.Height);
            Assert.Equal(100, tree.Children[1].BoundingRectangle.X);
            Assert.Equal(3000, tree.Children[1].BoundingRectangle.Y);
        }

        [Fact]
        public void WebElementMapper_OffscreenElementRetainsGeometry_ParticipatesInPositionScoring()
        {
            // Issue #7 side-effect pin: retaining bounding box on offscreen elements ensures
            // SimilarityScorer computes a non-null PositionScore and contributes evidence coverage.
            var offscreenElement = new WebElementInfo
            {
                TagName = "button",
                Role = "button",
                AccessibleName = "Checkout",
                TestId = "btn-checkout",
                IsOffscreen = true,
                IsHidden = false,
                BoundingRectangle = new BoundingRectangle(100, 3000, 120, 36),
            };

            var tree = WebElementMapper.ToUiElementTree(new WebElementInfo
            {
                TagName = "body",
                Children = new List<WebElementInfo> { offscreenElement }
            });

            var expected = new UiElementInfo
            {
                ControlType = "Button",
                Name = "Checkout",
                AutomationId = "btn-checkout",
                BoundingRectangle = new BoundingRectangle(100, 3000, 120, 36),
            };

            var healResult = SelfHealing.SelfHealingResolver.Resolve(expected, tree);

            Assert.NotNull(healResult.ScoreBreakdown);
            Assert.NotNull(healResult.ScoreBreakdown!.PositionScore);
            Assert.Equal(1.0, healResult.ScoreBreakdown!.PositionScore!.Value);
            Assert.True(healResult.EvidenceCoverage >= 0.80);
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
            Assert.Contains("#customer\\\\.email", suggestions[2].Expression);
        }

        [Fact]
        public void PlaywrightLocatorEmitter_EscapesControlCharactersForCSharpSource()
        {
            var element = new WebElementInfo
            {
                TagName = "input",
                Role = "textbox",
                AccessibleName = "Save\r\nDraft\tNow",
                TestId = "save\r\n\tdraft",
                NameAttribute = "profile\r\n\talias",
                CssSelector = "input[data-label=\"Save\r\nDraft\tNow\"]",
                FrameAncestry = new List<string> { "iframe\r\n\t#details" },
            };

            var suggestions = PlaywrightLocatorEmitter.Suggest(element);

            Assert.All(suggestions, suggestion =>
            {
                Assert.DoesNotContain("\r", suggestion.Expression);
                Assert.DoesNotContain("\n", suggestion.Expression);
                Assert.DoesNotContain("\t", suggestion.Expression);
            });
            Assert.Equal(
                "page.FrameLocator(\"iframe\\r\\n\\t#details\").GetByTestId(\"save\\r\\n\\tdraft\")",
                suggestions.Single(s => s.Strategy == "TestId").Expression);
            Assert.Contains(
                "Name = \"Save\\r\\nDraft\\tNow\"",
                suggestions.Single(s => s.Strategy == "Role").Expression);
            Assert.Contains("\\\\D ", suggestions.Single(s => s.Strategy == "NameAttribute").Expression);
            Assert.Contains("\\\\A ", suggestions.Single(s => s.Strategy == "NameAttribute").Expression);
            Assert.Contains("\\\\9 ", suggestions.Single(s => s.Strategy == "NameAttribute").Expression);
        }

        [Theory]
        [InlineData("invoice#[draft] item", "page.Locator(\"#invoice\\\\#\\\\[draft\\\\]\\\\ item\")")]
        [InlineData("123item", "page.Locator(\"#\\\\31 23item\")")]
        [InlineData("-1item", "page.Locator(\"#-\\\\31 item\")")]
        [InlineData("-", "page.Locator(\"#\\\\-\")")]
        [InlineData("control\u001Fid", "page.Locator(\"#control\\\\1f id\")")]
        [InlineData("null\0id", "page.Locator(\"#null\uFFFDid\")")]
        [InlineData("café", "page.Locator(\"#café\")")]
        public void PlaywrightLocatorEmitter_IdStrategy_MatchesCssEscapeSemantics(string id, string expectedExpression)
        {
            var element = new WebElementInfo
            {
                TagName = "div",
                Id = id,
            };

            var suggestion = Assert.Single(PlaywrightLocatorEmitter.Suggest(element));

            Assert.Equal(expectedExpression, suggestion.Expression);
        }

        [Fact]
        public void PlaywrightLocatorEmitter_NameAttributeWithDoubleQuote_EscapesCSharpStringLiteral()
        {
            var element = new WebElementInfo
            {
                TagName = "input",
                NameAttribute = "profile\"o'brien",
            };

            var suggestion = Assert.Single(PlaywrightLocatorEmitter.Suggest(element));

            Assert.Equal("page.Locator(\"[name='profile\\\"o\\\\'brien']\")", suggestion.Expression);
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
        public void PlaywrightLocatorEmitter_WithFrameAncestry_GeneratesFrameLocatorChain()
        {
            var element = new WebElementInfo
            {
                TagName = "button",
                Role = "button",
                AccessibleName = "Save Details",
                TestId = "save-btn",
                FrameAncestry = new List<string> { "iframe[name='details']" },
            };

            var suggestions = PlaywrightLocatorEmitter.Suggest(element);

            Assert.Equal("page.FrameLocator(\"iframe[name='details']\").GetByTestId(\"save-btn\")", suggestions[0].Expression);
            Assert.Equal("page.FrameLocator(\"iframe[name='details']\").GetByRole(AriaRole.Button, new() { Name = \"Save Details\" })", suggestions[1].Expression);
        }

        [Fact]
        public void PlaywrightLocatorEmitter_WithNestedFrameAncestry_GeneratesChainedFrameLocators()
        {
            var element = new WebElementInfo
            {
                TagName = "input",
                Role = "textbox",
                AccessibleName = "Nested Input",
                TestId = "nested-input",
                FrameAncestry = new List<string> { "iframe#parent", "iframe#child" },
            };

            var suggestions = PlaywrightLocatorEmitter.Suggest(element);

            Assert.Equal("page.FrameLocator(\"iframe#parent\").FrameLocator(\"iframe#child\").GetByTestId(\"nested-input\")", suggestions[0].Expression);
        }

        [Fact]
        public void PlaywrightDomCaptureScript_ExposesAPlaywrightEvaluateFunction()
        {
            Assert.Contains("document.body", PlaywrightDomCaptureScript.JavaScript);
            Assert.Contains("data-testid", PlaywrightDomCaptureScript.JavaScript);
            Assert.Contains("InputType", PlaywrightDomCaptureScript.JavaScript);
            Assert.Contains("BoundingRectangle", PlaywrightDomCaptureScript.JavaScript);
            Assert.Contains("shadowRoot", PlaywrightDomCaptureScript.JavaScript);
            Assert.Contains("contentDocument", PlaywrightDomCaptureScript.JavaScript);
            Assert.Contains("IsHidden", PlaywrightDomCaptureScript.JavaScript);
            Assert.Contains("IsOffscreen", PlaywrightDomCaptureScript.JavaScript);
            Assert.Contains("FrameAncestry", PlaywrightDomCaptureScript.JavaScript);
            Assert.Contains("frameSelectorOf", PlaywrightDomCaptureScript.JavaScript);
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
