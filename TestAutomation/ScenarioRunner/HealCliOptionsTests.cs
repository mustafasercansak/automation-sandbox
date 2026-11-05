using System;
using AutomationSandbox.SelfHealing;
using AutomationSandbox.SelfHealingCli;

namespace ScenarioRunner
{
    // automation-sandbox-heal answers exit 0 (confident), 1 (not confident) or 2 (could not run). A mistake on the command line used to be
    // exit 1, so a CI gate that sends 1 to review read a typo like --profil as an ordinary "not confident" and nobody learned the tool had
    // not run (#646). The parser decides nothing about exit codes; these tests pin what counts as a mistake (Program maps every Error to 2).
    public class HealCliOptionsTests
    {
        public static readonly object[][] Mistakes =
        {
            new object[] { new string[0], "No arguments" },
            new object[] { new[] { "--bogus" }, "Unrecognized or incomplete argument: --bogus" },
            new object[] { new[] { "--expected" }, "Unrecognized or incomplete argument: --expected" },
            new object[] { new[] { "--expected", "e.json", "--tree" }, "Unrecognized or incomplete argument: --tree" },
            new object[] { new[] { "--expected", "e.json" }, "--tree and exactly one of" },
            new object[] { new[] { "--tree", "t.json" }, "--tree and exactly one of" },
            new object[] { new[] { "--expected", "e.json", "--selector", "getByTestId('x')", "--tree", "t.json" }, "--tree and exactly one of" },
            new object[] { new[] { "--expected", "e.json", "--tree", "t.json", "--profile", "nope" }, "Unknown profile 'nope'" },
            new object[] { new[] { "--expected", "e.json", "--tree", "t.json", "--profile", "99" }, "Unknown profile '99'" },
            new object[] { new[] { "--expected", "e.json", "--tree", "t.json", "--profil", "balanced" }, "Unrecognized or incomplete argument: --profil" },
            new object[] { new[] { "--expected", "e.json", "--tree", "t.json", "extra" }, "Unrecognized or incomplete argument: extra" },
        };

        [Theory]
        [MemberData(nameof(Mistakes))]
        public void ACommandLineMistake_IsAnErrorWithAReason_NeverSomethingThatCouldRun(string[] args, string expectedStart)
        {
            var options = HealCliOptions.Parse(args);

            Assert.NotNull(options.Error);
            Assert.StartsWith(expectedStart, options.Error, StringComparison.Ordinal);
            Assert.False(options.ShowHelp);
        }

        [Theory]
        [InlineData("-h")]
        [InlineData("--help")]
        public void Help_IsNotAMistake(string flag)
        {
            var options = HealCliOptions.Parse(new[] { flag });

            Assert.True(options.ShowHelp);
            Assert.Null(options.Error);
        }

        [Fact]
        public void AValidCommandLineWithAnExpectedSnapshot_IsReadFully()
        {
            var options = HealCliOptions.Parse(new[] { "--expected", "e.json", "--tree", "t.json", "--profile", "Conservative", "--compact" });

            Assert.Null(options.Error);
            Assert.Equal("e.json", options.ExpectedPath);
            Assert.Null(options.Selector);
            Assert.Equal("t.json", options.TreePath);
            Assert.Equal(ThresholdProfile.Conservative, options.Profile);
            Assert.True(options.Compact);
        }

        [Fact]
        public void AValidCommandLineWithASelector_IsReadFully_AndTheProfileDefaultsToBalanced()
        {
            var options = HealCliOptions.Parse(new[] { "--selector", "getByRole('button', { name: 'Save' })", "--tree", "t.json" });

            Assert.Null(options.Error);
            Assert.Equal("getByRole('button', { name: 'Save' })", options.Selector);
            Assert.Null(options.ExpectedPath);
            Assert.Equal(ThresholdProfile.Balanced, options.Profile);
            Assert.False(options.Compact);
        }

        [Fact]
        public void AnArgumentInAMessage_IsShortenedAndOnOneLine()
        {
            var hostile = "--x" + new string('a', 500) + "\nSECOND LINE";

            var options = HealCliOptions.Parse(new[] { hostile });

            Assert.NotNull(options.Error);
            Assert.DoesNotContain("\n", options.Error, StringComparison.Ordinal);
            Assert.True(options.Error!.Length < 200, "The message must stay short.");
        }

        [Fact]
        public void AFlagThatIsTheValueOfAnotherFlag_IsTakenAsTheValue()
        {
            // "--tree --compact": the second token is the tree path, as it was before; the parser does not second-guess a value.
            var options = HealCliOptions.Parse(new[] { "--expected", "e.json", "--tree", "--compact" });

            Assert.Null(options.Error);
            Assert.Equal("--compact", options.TreePath);
            Assert.False(options.Compact);
        }
    }
}
