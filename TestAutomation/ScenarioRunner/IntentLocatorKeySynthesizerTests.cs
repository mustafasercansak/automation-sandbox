using System;
using System.Collections.Generic;
using IntentAutomation;
using UiModel;
using WebDiscovery;
using Xunit;

namespace ScenarioRunner
{
    public class IntentLocatorKeySynthesizerTests
    {
        [Fact]
        public void Synthesize_ReturnsEmpty_ForNullOrNavigateOrUnknownStep()
        {
            Assert.Equal("", IntentLocatorKeySynthesizer.Synthesize(null!));
            Assert.Equal("", IntentLocatorKeySynthesizer.Synthesize(new IntentStep { ActionType = IntentActionType.Navigate, Value = "https://example.com" }));
            Assert.Equal("", IntentLocatorKeySynthesizer.Synthesize(new IntentStep { ActionType = IntentActionType.Unknown, TargetDescription = "something" }));
        }

        [Theory]
        [InlineData(IntentActionType.Fill, "first name", "Field.FirstName")]
        [InlineData(IntentActionType.Select, "record type", "Field.RecordType")]
        [InlineData(IntentActionType.Check, "newsletter", "Field.Newsletter")]
        [InlineData(IntentActionType.Uncheck, "terms", "Field.Terms")]
        [InlineData(IntentActionType.UploadFile, "resume file", "Field.ResumeFile")]
        public void Synthesize_GeneratesFieldPrefix_ForInputActions(IntentActionType actionType, string target, string expected)
        {
            var step = new IntentStep { ActionType = actionType, TargetDescription = target };
            Assert.Equal(expected, IntentLocatorKeySynthesizer.Synthesize(step));
        }

        [Theory]
        [InlineData("primary submit or save action", "Action.PrimarySubmit")]
        [InlineData("save button", "Action.PrimarySubmit")]
        [InlineData("submit form", "Action.PrimarySubmit")]
        [InlineData("primary action", "Action.PrimarySubmit")]
        [InlineData("delete customer", "Action.Click.DeleteCustomer")]
        [InlineData("checkout", "Action.Click.Checkout")]
        public void Synthesize_GeneratesClickKeys_Correctly(string target, string expected)
        {
            var step = new IntentStep { ActionType = IntentActionType.Click, TargetDescription = target };
            Assert.Equal(expected, IntentLocatorKeySynthesizer.Synthesize(step));
        }

        [Theory]
        [InlineData(IntentActionType.Hover, "menu item", "Action.Hover.MenuItem")]
        [InlineData(IntentActionType.Wait, "confirmation message", "Action.Wait.ConfirmationMessage")]
        [InlineData(IntentActionType.PressKey, "search key", "Action.PressKey.SearchKey")]
        public void Synthesize_GeneratesActionDirectPrefix_ForInteractions(IntentActionType actionType, string target, string expected)
        {
            var step = new IntentStep { ActionType = actionType, TargetDescription = target };
            Assert.Equal(expected, IntentLocatorKeySynthesizer.Synthesize(step));
        }

        [Theory]
        [InlineData("result records or confirmation area", "Assert.ResultVisible")]
        [InlineData("result visible", "Assert.ResultVisible")]
        [InlineData("order total", "Assert.OrderTotal")]
        [InlineData("customer email", "Assert.CustomerEmail")]
        public void Synthesize_GeneratesAssertKeys_Correctly(string target, string expected)
        {
            var step = new IntentStep { ActionType = IntentActionType.Assert, TargetDescription = target };
            Assert.Equal(expected, IntentLocatorKeySynthesizer.Synthesize(step));
        }

        [Fact]
        public void Synthesize_FallsBackToWebCandidate_WhenTargetDescriptionIsEmpty()
        {
            var step = new IntentStep { ActionType = IntentActionType.Fill, TargetDescription = "" };
            var candidate = new IntentElementCandidate
            {
                Element = new WebElementInfo { AccessibleName = "Email Address", TestId = "email-box" }
            };

            Assert.Equal("Field.EmailAddress", IntentLocatorKeySynthesizer.Synthesize(step, candidate));

            var candidateOnlyTestId = new IntentElementCandidate
            {
                Element = new WebElementInfo { TestId = "customer-id" }
            };
            Assert.Equal("Field.CustomerId", IntentLocatorKeySynthesizer.Synthesize(step, candidateOnlyTestId));
        }

        [Fact]
        public void Synthesize_FallsBackToDesktopCandidate_WhenTargetDescriptionIsEmpty()
        {
            var step = new IntentStep { ActionType = IntentActionType.Fill, TargetDescription = "" };
            var candidate = new IntentDesktopElementCandidate
            {
                Element = new UiElementInfo { Name = "First Name", AutomationId = "txtFirst" }
            };

            Assert.Equal("Field.FirstName", IntentLocatorKeySynthesizer.Synthesize(step, candidate));

            var candidateOnlyAutomationId = new IntentDesktopElementCandidate
            {
                Element = new UiElementInfo { AutomationId = "txtLastName" }
            };
            Assert.Equal("Field.TxtLastName", IntentLocatorKeySynthesizer.Synthesize(step, candidateOnlyAutomationId));
        }

        [Theory]
        [InlineData("first_name", "FirstName")]
        [InlineData("first-name", "FirstName")]
        [InlineData("first.name", "FirstName")]
        [InlineData("  multiple   spaces  ", "MultipleSpaces")]
        [InlineData("", "")]
        [InlineData("   ", "")]
        [InlineData(null, "")]
        public void ToPascalKey_NormalizesSeparatorsAndWhitespace(string? input, string expected)
        {
            Assert.Equal(expected, IntentLocatorKeySynthesizer.ToPascalKey(input!));
        }
    }
}
