using System;
using System.IO;
using Xunit;

namespace ScenarioRunner
{
    // Live test that runs exclusively on Windows net48 to survey open-source application pairs.
    // Opt-in via RUN_OPEN_SOURCE_APP_SURVEY=1 environment variable.
    public class OpenSourceAppSurveyLiveTests
    {
        [Fact]
        public void RunLiveOpenSourceAppSurvey()
        {
            var optIn = Environment.GetEnvironmentVariable("RUN_OPEN_SOURCE_APP_SURVEY");
            if (optIn != "1" && !string.Equals(optIn, "true", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("[OpenSourceSurvey] RUN_OPEN_SOURCE_APP_SURVEY=1 is not set - skipping live open-source app survey.");
                return;
            }

            var outputDir = Environment.GetEnvironmentVariable("SURVEY_OUTPUT_DIR")
                ?? Path.Combine(AppContext.BaseDirectory, "TestResults", "open-source-survey");

            var report = OpenSourceAppSurveyRunner.RunSurvey(outputDir);

            Assert.NotNull(report);
            Assert.NotEmpty(report.Chains);
        }
    }
}
