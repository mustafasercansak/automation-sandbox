using System;
using System.IO;
using Xunit;

namespace ScenarioRunner
{
    // Live test that runs exclusively on Windows net48 to probe real system applications.
    // Opt-in via RUN_WINDOWS_APP_SURVEY=1 environment variable.
    public class WindowsAppSurveyLiveTests
    {
        [SkippableFact]
        public void RunLiveWindowsAppSurvey()
        {
            var optIn = Environment.GetEnvironmentVariable("RUN_WINDOWS_APP_SURVEY");
            if (optIn != "1" && !string.Equals(optIn, "true", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("[WindowsAppSurvey] RUN_WINDOWS_APP_SURVEY=1 is not set - skipping live Windows app survey.");
                Skip.If(true, "RUN_WINDOWS_APP_SURVEY=1 is not set.");
            }

            var imageName = Environment.GetEnvironmentVariable("IMAGE_NAME");
            if (string.IsNullOrEmpty(imageName))
            {
                imageName = "windows-local";
            }

            var outputDir = Environment.GetEnvironmentVariable("SURVEY_OUTPUT_DIR")
                ?? Path.Combine(AppContext.BaseDirectory, "TestResults", "survey-trees", imageName);

            var report = WindowsAppSurveyRunner.RunSurvey(imageName, outputDir);

            Assert.NotNull(report);
            Assert.NotEmpty(report.Applications);
        }
    }
}
