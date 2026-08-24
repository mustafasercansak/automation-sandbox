using System;
using System.Collections.Generic;
using System.IO;
using IntentAutomation;
using UiModel;
using Xunit;

namespace ScenarioRunner
{
    public class IntentPlatformNeutralityTests
    {
        [Theory]
        [InlineData(AssertionKind.None, false, true, true)]
        [InlineData(AssertionKind.Visible, false, true, true)]
        [InlineData(AssertionKind.NotVisible, false, true, true)]
        [InlineData(AssertionKind.TextEquals, false, true, true)]
        [InlineData(AssertionKind.TextContains, false, true, true)]
        [InlineData(AssertionKind.ValueEquals, false, true, true)]
        [InlineData(AssertionKind.UrlEquals, true, false, false)]
        [InlineData(AssertionKind.UrlContains, true, false, false)]
        public void AssertionKindExtensions_CorrectlyClassifiesPlatformSupport(
            AssertionKind kind,
            bool expectedWebOnly,
            bool expectedDesktopSupported,
            bool expectedPlatformNeutral)
        {
            Assert.Equal(expectedWebOnly, kind.IsWebOnly());
            Assert.Equal(expectedDesktopSupported, kind.IsSupportedOnDesktop());
            Assert.Equal(expectedPlatformNeutral, kind.IsPlatformNeutral());
        }

        [Fact]
        public void IntentDesktopPlanningRequest_ConvertsToPlanningRequestWithEmptyTargetUrl()
        {
            var desktopRequest = new IntentDesktopPlanningRequest
            {
                Name = "Customer Registration",
                Goal = "Register a new corporate customer",
                ApplicationExecutablePath = @"C:\Apps\WinFormsApp.exe",
                TestData = new Dictionary<string, string>
                {
                    ["CompanyName"] = "Acme Corp",
                    ["ContactName"] = "Jane Doe",
                },
            };

            var planningRequest = desktopRequest.ToPlanningRequest();

            Assert.Equal("Customer Registration", planningRequest.Name);
            Assert.Equal("Register a new corporate customer", planningRequest.Goal);
            Assert.Equal("", planningRequest.TargetUrl);
            Assert.Equal(2, planningRequest.TestData.Count);
            Assert.Equal("Acme Corp", planningRequest.TestData["CompanyName"]);

            // Implicit operator conversion
            IntentPlanningRequest implicitRequest = desktopRequest;
            Assert.NotNull(implicitRequest);
            Assert.Equal("", implicitRequest.TargetUrl);
        }

        [Fact]
        public void IntentDesktopPlanningRequest_ImplicitConversion_ThrowsOnNull()
        {
            IntentDesktopPlanningRequest? nullRequest = null;
            Assert.Throws<ArgumentNullException>(() =>
            {
                IntentPlanningRequest req = nullRequest!;
            });
        }

        [Fact]
        public void IntentDesktopAutomationPipeline_Run_AcceptsDesktopPlanningRequestAndGoalString()
        {
            var desktopRoot = new UiElementInfo
            {
                ControlType = "Window",
                AutomationId = "MainForm",
                Name = "Customer Management",
                BoundingRectangle = new BoundingRectangle(0, 0, 800, 600),
                Children = new List<UiElementInfo>
                {
                    new UiElementInfo
                    {
                        ControlType = "Edit",
                        AutomationId = "txtCompanyName",
                        Name = "Company Name",
                        BoundingRectangle = new BoundingRectangle(50, 50, 200, 30),
                    },
                    new UiElementInfo
                    {
                        ControlType = "Button",
                        AutomationId = "btnSave",
                        Name = "Save",
                        BoundingRectangle = new BoundingRectangle(50, 100, 100, 30),
                    },
                },
            };

            var tempDir = Path.Combine(Path.GetTempPath(), "IntentPlatformTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var repoPath = Path.Combine(tempDir, "locators.json");
                var repo = new LocatorRepository(repoPath);
                var pipeline = new IntentDesktopAutomationPipeline();

                // 1. Run with IntentDesktopPlanningRequest
                var desktopRequest = new IntentDesktopPlanningRequest
                {
                    Name = "Save Customer",
                    Goal = "Fill company name and click save",
                    TestData = new Dictionary<string, string> { ["CompanyName"] = "Acme" },
                };
                var result1 = pipeline.Run(desktopRequest, desktopRoot, repo);
                Assert.NotNull(result1);
                Assert.Equal("Save Customer", result1.Planning.Scenario.Name);
                Assert.Contains("txtCompanyName", result1.FlaUiCSharpTestCode);
                Assert.Contains("btnSave", result1.FlaUiCSharpTestCode);

                // 2. Run with string goal overload
                var result2 = pipeline.Run("Click save", desktopRoot, repo);
                Assert.NotNull(result2);
                Assert.Equal("Click save", result2.Planning.Scenario.Goal);
                Assert.Contains("btnSave", result2.FlaUiCSharpTestCode);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }
    }
}
