using System.Text.Json;
using AutomationSandbox.SelfHealing;
using AutomationSandbox.UiModel;

// The point of this tool: SelfHealingResolver.Resolve operates purely on the
// framework-agnostic UiElementInfo tree (no FlaUI/Playwright .NET dependency at all), so
// the only thing standing between "any language, any CI" and this engine was packaging.
// Any caller - Python, JS, a shell script, whatever - just needs to produce JSON shaped
// like UiTreeSerializer.ToJson's output and can shell out to this command.
internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        string? expectedPath = null;
        string? treePath = null;
        var profile = ThresholdProfile.Balanced;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--expected" when i + 1 < args.Length:
                    expectedPath = args[++i];
                    break;
                case "--tree" when i + 1 < args.Length:
                    treePath = args[++i];
                    break;
                case "--profile" when i + 1 < args.Length:
                    i++;
                    if (!Enum.TryParse(args[i], ignoreCase: true, out profile))
                    {
                        Console.Error.WriteLine($"Unknown profile '{args[i]}'. Use balanced, conservative, or aggressive.");
                        return 1;
                    }

                    break;
                default:
                    Console.Error.WriteLine($"Unrecognized or incomplete argument: {args[i]}");
                    PrintUsage();
                    return 1;
            }
        }

        if (expectedPath is null || treePath is null)
        {
            Console.Error.WriteLine("Both --expected and --tree are required.");
            PrintUsage();
            return 1;
        }

        try
        {
            var expected = UiTreeSerializer.FromJson(File.ReadAllText(expectedPath));
            var currentTreeRoot = UiTreeSerializer.FromJson(File.ReadAllText(treePath));
            var weights = SimilarityWeights.FromProfile(profile);

            // Resolve's default log callback writes to stdout, which would corrupt the JSON
            // this tool prints there for a script to parse. Route it to stderr instead so
            // stdout stays pure JSON - the one promise this tool makes to any caller.
            var result = SelfHealingResolver.Resolve(expected, currentTreeRoot, weights, log: Console.Error.WriteLine);

            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));

            // IsConfident is the same acceptance gate SelfHealingResolver/HealResult use
            // everywhere else in this repo - a caller piping this into a CI gate gets the
            // identical evidence/margin/per-component rules the .NET API enforces, not a
            // separate, looser CLI-only notion of "good enough".
            return result.IsConfident ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 2;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("================================================================================");
        Console.WriteLine("  Automation Sandbox - Self-Healing CLI");
        Console.WriteLine("  (language/platform-agnostic locator healing: JSON in, JSON out)");
        Console.WriteLine("================================================================================");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  automation-sandbox-heal --expected <stale-element.json> --tree <live-tree.json> [--profile balanced|conservative|aggressive]");
        Console.WriteLine();
        Console.WriteLine("--expected   A single UiElementInfo JSON object: the stale/broken locator's last known snapshot.");
        Console.WriteLine("--tree       A UiElementInfo JSON tree: a freshly captured snapshot of the live application.");
        Console.WriteLine("--profile    Threshold profile (default: balanced). See SimilarityWeights.FromProfile.");
        Console.WriteLine();
        Console.WriteLine("Both JSON inputs must be shaped like UiTreeSerializer.ToJson's output - produce this");
        Console.WriteLine("from any language or tool; no C# or FlaUI/Playwright .NET dependency is needed to call this.");
        Console.WriteLine();
        Console.WriteLine("Prints a HealResult as JSON to stdout. Exit code 0 means IsConfident (safe to accept");
        Console.WriteLine("Matched); non-zero (1) means not confident - do not auto-accept Matched; 2 means an error");
        Console.WriteLine("(bad input, missing file) occurred before a decision could be made.");
    }
}
