using System.Collections.Generic;
using IntentAutomation;
using UiModel;
using Xunit;

namespace ScenarioRunner
{
    public class IntentRecordingLookupTableTests
    {
        [Fact]
        public void TryFindRecording_MatchesDirectStepReference()
        {
            var step = new IntentStep { Order = 1, ActionType = IntentActionType.Fill, TargetDescription = "Email" };
            var recording = new IntentLocatorRecordingResult
            {
                Step = step,
                LocatorKey = "Field.Email",
                Recorded = true,
            };

            var table = IntentRecordingLookupTable.Create(new[] { recording });

            var found = table.TryFindRecording(step, out var matched);

            Assert.True(found);
            Assert.Same(recording, matched);
        }

        [Fact]
        public void TryFindRecording_MatchesByOrderWhenStepInstanceDiffers()
        {
            var registeredStep = new IntentStep { Order = 2, ActionType = IntentActionType.Click, TargetDescription = "Submit" };
            var queryStep = new IntentStep { Order = 2, ActionType = IntentActionType.Click, TargetDescription = "Submit Different Instance" };
            var recording = new IntentLocatorRecordingResult
            {
                Step = registeredStep,
                LocatorKey = "Action.Click.Submit",
                Recorded = true,
            };

            var table = IntentRecordingLookupTable.Create(new[] { recording });

            var found = table.TryFindRecording(queryStep, out var matched);

            Assert.True(found);
            Assert.Same(recording, matched);
        }

        [Fact]
        public void TryFindRecording_MatchesByTargetDescriptionKey()
        {
            var queryStep = new IntentStep { Order = 99, ActionType = IntentActionType.Click, TargetDescription = "Field.CustomKey" };
            var recording = new IntentLocatorRecordingResult
            {
                LocatorKey = "Field.CustomKey",
                Recorded = true,
            };

            var table = IntentRecordingLookupTable.Create(new[] { recording });

            var found = table.TryFindRecording(queryStep, out var matched);

            Assert.True(found);
            Assert.Same(recording, matched);
        }

        [Fact]
        public void TryFindRecording_MatchesBySynthesizedKey()
        {
            var queryStep = new IntentStep { Order = 99, ActionType = IntentActionType.Fill, TargetDescription = "User Email Address" };
            var recording = new IntentLocatorRecordingResult
            {
                LocatorKey = "Field.UserEmailAddress",
                Recorded = true,
            };

            var table = IntentRecordingLookupTable.Create(new[] { recording });

            var found = table.TryFindRecording(queryStep, out var matched);

            Assert.True(found);
            Assert.Same(recording, matched);
        }

        [Fact]
        public void TryFindRecording_ReturnsFalse_WhenNoMatch()
        {
            var queryStep = new IntentStep { Order = 99, ActionType = IntentActionType.Fill, TargetDescription = "Nonexistent" };
            var table = IntentRecordingLookupTable.Create(new List<IntentLocatorRecordingResult>());

            var found = table.TryFindRecording(queryStep, out var matched);

            Assert.False(found);
            Assert.Null(matched);
        }

        [Fact]
        public void CreateDesktop_MatchesDesktopRecordings()
        {
            var step = new IntentStep { Order = 1, ActionType = IntentActionType.Click, TargetDescription = "Save" };
            var desktopRecording = new IntentDesktopLocatorRecordingResult
            {
                Step = step,
                LocatorKey = "Action.Click.Save",
                Recorded = true,
            };

            var table = IntentRecordingLookupTable.CreateDesktop(new[] { desktopRecording });

            var found = table.TryFindRecording(step, out var matched);

            Assert.True(found);
            Assert.Same(desktopRecording, matched);
        }

        [Fact]
        public void Constructor_HandlesNullAndEmptyInputsGracefully()
        {
            var table = IntentRecordingLookupTable.Create(null);

            Assert.False(table.TryFindRecording(null!, out var matchedNullStep));
            Assert.Null(matchedNullStep);

            var step = new IntentStep { Order = 1 };
            Assert.False(table.TryFindRecording(step, out var matchedValidStep));
            Assert.Null(matchedValidStep);
        }
    }
}
