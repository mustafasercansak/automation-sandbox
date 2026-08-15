using System;
using System.Collections.Generic;
using Xunit;

namespace ScenarioRunner
{
    // Regression cover for the blocking-dialog captures found in run 31883560197, where four of five
    // HandBrake releases were surveyed against a modal #32770 dialog instead of the application window.
    public class WindowReadinessHeuristicsTests
    {
        [Fact]
        public void IsBlockingDialog_TrueForSmallWin32Dialog()
        {
            Assert.True(WindowReadinessHeuristics.IsBlockingDialog("#32770", 7));
        }

        [Fact]
        public void IsBlockingDialog_FalseForApplicationWindowClass()
        {
            Assert.False(WindowReadinessHeuristics.IsBlockingDialog("HwndWrapper[HandBrake.exe;;]", 7));
            Assert.False(WindowReadinessHeuristics.IsBlockingDialog("WindowsForms10.Window.8.app.0.141b42a_r6_ad1", 34));
        }

        [Fact]
        public void IsBlockingDialog_FalseForLargeDialogClassWindow()
        {
            // A dialog-class window with a real tree is an application window that happens to be a dialog.
            Assert.False(WindowReadinessHeuristics.IsBlockingDialog("#32770", 149));
        }

        [Fact]
        public void IsBlockingDialog_FalseForEmptyOrMissingClassName()
        {
            Assert.False(WindowReadinessHeuristics.IsBlockingDialog(null, 7));
            Assert.False(WindowReadinessHeuristics.IsBlockingDialog("", 7));
            Assert.False(WindowReadinessHeuristics.IsBlockingDialog("#32770", 0));
        }

        [Fact]
        public void TryDetectMissingRuntime_ExtractsFrameworkAndVersion_FromRealHostErrorText()
        {
            // Verbatim text captured from HandBrake 1.6.1 on the CI runner.
            var texts = new List<string?>
            {
                "",
                "You must install or update .NET to run this application.\n\n" +
                "Framework: 'Microsoft.NETCore.App', version '6.0.0' (x64)\n\n" +
                "Would you like to download it now?\n\n" +
                "Learn about framework resolution:\nhttps://aka.ms/dotnet/app-launch-failed",
            };

            Assert.True(WindowReadinessHeuristics.TryDetectMissingRuntime(texts, out var missingRuntime));
            Assert.Equal("Microsoft.NETCore.App 6.0.0", missingRuntime);
        }

        [Fact]
        public void TryDetectMissingRuntime_FalseForOrdinaryPrompt()
        {
            var texts = new List<string?>
            {
                "Would you like to allow HandBrake to automatically check for updates?",
                "Yes",
                "No",
            };

            Assert.False(WindowReadinessHeuristics.TryDetectMissingRuntime(texts, out var missingRuntime));
            Assert.Equal("", missingRuntime);
        }

        [Fact]
        public void TryDetectMissingRuntime_FalseForNullOrEmptyInput()
        {
            Assert.False(WindowReadinessHeuristics.TryDetectMissingRuntime(null!, out _));
            Assert.False(WindowReadinessHeuristics.TryDetectMissingRuntime(new List<string?> { null, "" }, out _));
        }

        [Fact]
        public void SelectDismissButtonName_PrefersNoOverYes()
        {
            // The HandBrake first-run prompt: answering "No" declines auto-update checks.
            var chosen = WindowReadinessHeuristics.SelectDismissButtonName(new[] { "Yes", "No" });
            Assert.Equal("No", chosen);
        }

        [Fact]
        public void SelectDismissButtonName_FallsBackThroughPreferenceOrder()
        {
            Assert.Equal("Cancel", WindowReadinessHeuristics.SelectDismissButtonName(new[] { "Continue", "Cancel" }));
            Assert.Equal("Later", WindowReadinessHeuristics.SelectDismissButtonName(new[] { "Install", "Later" }));
            Assert.Equal("OK", WindowReadinessHeuristics.SelectDismissButtonName(new[] { "OK" }));
        }

        [Fact]
        public void SelectDismissButtonName_MatchesAcceleratedLabel_AndReturnsItVerbatim()
        {
            // The returned name is used as a UIA ByName condition, so it must keep the ampersand.
            Assert.Equal("&No", WindowReadinessHeuristics.SelectDismissButtonName(new[] { "&Yes", "&No" }));
        }

        [Fact]
        public void SelectDismissButtonName_NullWhenNoSafeButtonExists()
        {
            // An unrecognised dialog is left alone rather than clicked blindly.
            Assert.Null(WindowReadinessHeuristics.SelectDismissButtonName(new[] { "Delete everything", "Reformat" }));
            Assert.Null(WindowReadinessHeuristics.SelectDismissButtonName(new string?[] { null, "  " }));
            Assert.Null(WindowReadinessHeuristics.SelectDismissButtonName(null!));
        }
    }
}
