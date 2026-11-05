using AutomationSandbox.SelfHealing;

namespace AutomationSandbox.SelfHealingCli;

/// <summary>What <c>automation-sandbox-heal</c> was asked to do, read from its command line. Pure: it writes nothing and decides no exit code,
/// so the mapping from a mistake to exit code 2 (never 1, which means "not confident") is tested without starting the tool (#646).</summary>
internal sealed class HealCliOptions
{
    private HealCliOptions()
    {
    }

    /// <summary>True when <c>-h</c> or <c>--help</c> was the first argument; the usage goes to stdout and the exit code is 0.</summary>
    public bool ShowHelp { get; private set; }

    /// <summary>What is wrong with the command line, or null when it can be run. Anything here is exit code 2.</summary>
    public string? Error { get; private set; }

    /// <summary>Path of the stale element snapshot; null when <c>--selector</c> is used instead.</summary>
    public string? ExpectedPath { get; private set; }

    /// <summary>A Playwright locator standing in for a recorded snapshot; null when <c>--expected</c> is used instead.</summary>
    public string? Selector { get; private set; }

    /// <summary>Path of the live tree.</summary>
    public string? TreePath { get; private set; }

    /// <summary>Whether to print the smaller result.</summary>
    public bool Compact { get; private set; }

    /// <summary>The threshold profile; <see cref="ThresholdProfile.Balanced" /> unless <c>--profile</c> says otherwise.</summary>
    public ThresholdProfile Profile { get; private set; } = ThresholdProfile.Balanced;

    /// <summary>Reads <paramref name="args" />. Never throws; a mistake is reported through <see cref="Error" />.</summary>
    public static HealCliOptions Parse(string[] args)
    {
        var options = new HealCliOptions();
        if (args.Length == 0)
        {
            options.Error = "No arguments were given.";
            return options;
        }

        if (args[0] is "-h" or "--help")
        {
            options.ShowHelp = true;
            return options;
        }

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--expected" when i + 1 < args.Length:
                    options.ExpectedPath = args[++i];
                    break;
                case "--selector" when i + 1 < args.Length:
                    options.Selector = args[++i];
                    break;
                case "--compact":
                    options.Compact = true;
                    break;
                case "--tree" when i + 1 < args.Length:
                    options.TreePath = args[++i];
                    break;
                case "--profile" when i + 1 < args.Length:
                    i++;
                    if (!Enum.TryParse(args[i], ignoreCase: true, out ThresholdProfile profile) || !Enum.IsDefined(typeof(ThresholdProfile), profile))
                    {
                        options.Error = $"Unknown profile '{Shorten(args[i])}'. Use balanced, conservative, or aggressive.";
                        return options;
                    }

                    options.Profile = profile;
                    break;
                default:
                    options.Error = $"Unrecognized or incomplete argument: {Shorten(args[i])}";
                    return options;
            }
        }

        if (options.TreePath is null || (options.ExpectedPath is null) == (options.Selector is null))
        {
            options.Error = "--tree and exactly one of --expected or --selector are required.";
        }

        return options;
    }

    // An argument can be anything a caller typed; keep a message on one short line.
    private static string Shorten(string value)
    {
        var clean = new string(value.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
        return clean.Length <= 60 ? clean : clean.Substring(0, 60) + "...";
    }
}
