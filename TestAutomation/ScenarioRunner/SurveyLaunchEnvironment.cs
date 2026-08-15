using System;
using System.Collections.Generic;

namespace ScenarioRunner
{
    // Environment a surveyed application is launched with. Kept separate from the FlaUI-bound runner so
    // the policy decisions here are unit-testable on every target framework.
    public static class SurveyLaunchEnvironment
    {
        // "Major" rolls forward to the lowest higher major *only when the requested runtime is missing*.
        // "LatestMajor" always jumps to the newest installed major, even when the exact runtime is present,
        // and that broke HandBrake 1.9.2: it ran fine on its own runtime in run 31883560197 and threw
        // "An Unknown Error has occurred." in runs 31885249314 and 31887707261 once it was forced onto the
        // newest major. Roll forward as a rescue for releases whose runtime is gone, never as a default.
        public const string RollForwardPolicy = "Major";

        public static IReadOnlyDictionary<string, string> Build(string roamingProfileDirectory, string localProfileDirectory)
        {
            if (string.IsNullOrWhiteSpace(roamingProfileDirectory))
            {
                throw new ArgumentException("Roaming profile directory is required.", nameof(roamingProfileDirectory));
            }

            if (string.IsNullOrWhiteSpace(localProfileDirectory))
            {
                throw new ArgumentException("Local profile directory is required.", nameof(localProfileDirectory));
            }

            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "DOTNET_ROLL_FORWARD", RollForwardPolicy },

                // Releases otherwise share one user profile, so settings written by an earlier release
                // change how a later one starts and captures become order-dependent.
                { "APPDATA", roamingProfileDirectory },
                { "LOCALAPPDATA", localProfileDirectory },
            };
        }

        public static IReadOnlyList<string> Describe() => new[]
        {
            $"DOTNET_ROLL_FORWARD={RollForwardPolicy} applied (roll forward only when the target runtime is absent)",
            "Isolated APPDATA/LOCALAPPDATA for this version",
        };
    }
}
