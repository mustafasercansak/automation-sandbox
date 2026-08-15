using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace ScenarioRunner
{
    // Pure decision logic for telling a blocking startup dialog apart from an application window.
    //
    // Discovered on run 31883560197: four of five HandBrake releases were captured while a modal
    // #32770 dialog owned the foreground (a first-run update prompt on 1.9.2/1.11.2, a missing
    // .NET runtime error on 1.6.1/1.7.3). Both produced 7-node "captures" that then contributed
    // fake removed-AutomationIds to the chain totals.
    //
    // Kept free of FlaUI types so it compiles and is testable on every target framework.
    public static class WindowReadinessHeuristics
    {
        // Standard Win32 dialog window class. MessageBox, TaskDialog and WinForms dialogs all use it.
        public const string Win32DialogClassName = "#32770";

        // Dialogs are small. Anything above this is a real window that merely happens to be a dialog class.
        public const int MaxDialogNodeCount = 15;

        // Ordered by preference: the least destructive way to decline a startup prompt.
        private static readonly string[] DismissButtonPreference =
        {
            "No",
            "No thanks",
            "Not now",
            "Later",
            "Remind me later",
            "Skip",
            "Cancel",
            "Close",
            "OK",
        };

        private static readonly Regex MissingRuntimePattern = new Regex(
            @"'(?<framework>Microsoft\.[A-Za-z.]+)',\s*version\s*'(?<version>[0-9][0-9A-Za-z.\-]*)'",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static bool IsWin32DialogClass(string? className) =>
            string.Equals(className, Win32DialogClassName, StringComparison.Ordinal);

        // A window is treated as a blocking startup dialog only when it is both a dialog class and small.
        public static bool IsBlockingDialog(string? className, int nodeCount) =>
            IsWin32DialogClass(className) && nodeCount > 0 && nodeCount <= MaxDialogNodeCount;

        // Detects the "You must install or update .NET to run this application" host error.
        // Returns the missing framework identity so the failure is reported instead of captured.
        public static bool TryDetectMissingRuntime(IEnumerable<string?> texts, out string missingRuntime)
        {
            missingRuntime = "";
            if (texts == null)
            {
                return false;
            }

            foreach (var text in texts)
            {
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                if (text!.IndexOf("must install or update .NET", StringComparison.OrdinalIgnoreCase) < 0 &&
                    text.IndexOf("app-launch-failed", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var match = MissingRuntimePattern.Match(text);
                missingRuntime = match.Success
                    ? string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} {1}",
                        match.Groups["framework"].Value,
                        match.Groups["version"].Value)
                    : "unspecified .NET runtime";
                return true;
            }

            return false;
        }

        // Picks the button that declines the prompt. Returns null when no known safe button exists,
        // so an unrecognised dialog is left alone rather than clicked blindly.
        public static string? SelectDismissButtonName(IEnumerable<string?> buttonNames)
        {
            if (buttonNames == null)
            {
                return null;
            }

            var available = buttonNames
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!.Trim())
                .ToList();

            if (available.Count == 0)
            {
                return null;
            }

            foreach (var preferred in DismissButtonPreference)
            {
                var match = available.FirstOrDefault(n =>
                    string.Equals(StripAccelerator(n), preferred, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        // Win32 buttons carry keyboard accelerators as ampersands ("&No").
        private static string StripAccelerator(string name) => name.Replace("&", "");
    }
}
