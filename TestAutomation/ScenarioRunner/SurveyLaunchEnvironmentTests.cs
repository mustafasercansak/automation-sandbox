using System;
using Xunit;

namespace ScenarioRunner
{
    public class SurveyLaunchEnvironmentTests
    {
        [Fact]
        public void RollForwardPolicy_IsMajor_NotLatestMajor()
        {
            // LatestMajor forces every release onto the newest installed major even when its own runtime
            // is present. That is what turned HandBrake 1.9.2 into an error window in runs 31885249314
            // and 31887707261, while it started normally on its own runtime in run 31883560197.
            Assert.Equal("Major", SurveyLaunchEnvironment.RollForwardPolicy);
            Assert.NotEqual("LatestMajor", SurveyLaunchEnvironment.RollForwardPolicy);
        }

        [Fact]
        public void Build_SetsRollForwardAndIsolatedProfileDirectories()
        {
            var env = SurveyLaunchEnvironment.Build(@"C:\survey\HandBrake_1.9.2_profile\Roaming", @"C:\survey\HandBrake_1.9.2_profile\Local");

            Assert.Equal("Major", env["DOTNET_ROLL_FORWARD"]);
            Assert.Equal(@"C:\survey\HandBrake_1.9.2_profile\Roaming", env["APPDATA"]);
            Assert.Equal(@"C:\survey\HandBrake_1.9.2_profile\Local", env["LOCALAPPDATA"]);
        }

        [Fact]
        public void Build_GivesEachVersionADistinctProfile()
        {
            var first = SurveyLaunchEnvironment.Build(@"C:\survey\a\Roaming", @"C:\survey\a\Local");
            var second = SurveyLaunchEnvironment.Build(@"C:\survey\b\Roaming", @"C:\survey\b\Local");

            Assert.NotEqual(first["APPDATA"], second["APPDATA"]);
            Assert.NotEqual(first["LOCALAPPDATA"], second["LOCALAPPDATA"]);
        }

        [Theory]
        [InlineData("", @"C:\survey\a\Local")]
        [InlineData("   ", @"C:\survey\a\Local")]
        [InlineData(@"C:\survey\a\Roaming", "")]
        public void Build_RejectsMissingProfileDirectory(string roaming, string local)
        {
            // A blank directory would silently fall back to the shared machine profile, which is the
            // order-dependence this isolation exists to remove.
            Assert.Throws<ArgumentException>(() => SurveyLaunchEnvironment.Build(roaming, local));
        }

        [Fact]
        public void Describe_ReportsBothPolicyAndIsolation()
        {
            var described = string.Join(" | ", SurveyLaunchEnvironment.Describe());

            Assert.Contains("DOTNET_ROLL_FORWARD=Major", described);
            Assert.Contains("only when the target runtime is absent", described);
            Assert.Contains("Isolated APPDATA/LOCALAPPDATA", described);
        }
    }
}
