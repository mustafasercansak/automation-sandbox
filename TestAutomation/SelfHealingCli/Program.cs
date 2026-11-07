using System.Text.Json;
using AutomationSandbox.SelfHealing;
using AutomationSandbox.SelfHealingCli;
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
        // The resolver log lines on stderr quote element names verbatim. A Windows console
        // writes stderr in the OEM code page, so "Bağlan" would print as "Ba?lan" (#621);
        // write UTF-8 without a BOM instead. stdout is JSON, which escapes non-ASCII itself.
        Console.SetError(new StreamWriter(Console.OpenStandardError(), new System.Text.UTF8Encoding(false)) { AutoFlush = true });

        // A command-line mistake is exit code 2, "could not run". Exit code 1 means "not confident", which a CI gate sends to review, so
        // a typo in a flag must never look like that (#646). The usage goes to stderr then: stdout is for JSON only.
        var options = HealCliOptions.Parse(args);
        if (options.ShowHelp)
        {
            PrintUsage(Console.Out);
            return 0;
        }

        if (options.Error is not null)
        {
            Console.Error.WriteLine(options.Error);
            PrintUsage(Console.Error);
            return 2;
        }

        var expectedPath = options.ExpectedPath;
        var selector = options.Selector;
        var compact = options.Compact;
        var treePath = options.TreePath!;
        var profile = options.Profile;

        try
        {
            UiElementInfo expected;
            if (selector is not null)
            {
                if (!PlaywrightSelectorParser.TryParse(selector, out expected, out var selectorError))
                {
                    Console.Error.WriteLine($"Error: {selectorError}");
                    return 2;
                }
            }
            else
            {
                expected = UiTreeSerializer.FromJson(File.ReadAllText(expectedPath!));
            }

            var currentTreeRoot = UiTreeSerializer.FromJson(File.ReadAllText(treePath));
            var weights = SimilarityWeights.FromProfile(profile);

            // Resolve's default log callback writes to stdout, which would corrupt the JSON
            // this tool prints there for a script to parse. Route it to stderr instead so
            // stdout stays pure JSON - the one promise this tool makes to any caller.
            var result = SelfHealingResolver.Resolve(expected, currentTreeRoot, weights, log: Console.Error.WriteLine);

            // Decide on the full result first: the exit code must not depend on how much of it is printed.
            var isConfident = result.IsConfident;
            if (compact)
            {
                HealResultCompactor.Compact(result);
            }

            // The matched element can carry its subtree, so the result is written with the same depth bound the trees are read with.
            var jsonOptions = new JsonSerializerOptions { WriteIndented = true, MaxDepth = UiTreeSerializer.MaxJsonDepth };
            if (selector is null)
            {
                Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
            }
            else
            {
                // A selector states only part of what a recorded snapshot would, so say so in
                // the output and show exactly what the engine was given to match against.
                Console.WriteLine(JsonSerializer.Serialize(
                    new { PartialEvidence = true, DerivedExpected = expected, Result = result },
                    jsonOptions));
            }

            // IsConfident is the same acceptance gate SelfHealingResolver/HealResult use
            // everywhere else in this repo - a caller piping this into a CI gate gets the
            // identical evidence/margin/per-component rules the .NET API enforces, not a
            // separate, looser CLI-only notion of "good enough".
            return isConfident ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 2;
        }
    }

    private static void PrintUsage(TextWriter output)
    {
        output.WriteLine("================================================================================");
        output.WriteLine("  Automation Sandbox - Self-Healing CLI");
        output.WriteLine("  (language/platform-agnostic locator healing: JSON in, JSON out)");
        output.WriteLine("================================================================================");
        output.WriteLine();
        output.WriteLine("Usage:");
        output.WriteLine("  automation-sandbox-heal --expected <stale-element.json> --tree <live-tree.json> [--profile balanced|conservative|aggressive] [--compact]");
        output.WriteLine("  automation-sandbox-heal --selector \"<playwright locator>\" --tree <live-tree.json> [--profile ...]");
        output.WriteLine();
        output.WriteLine("--expected   A single UiElementInfo JSON object: the stale/broken locator's last known snapshot.");
        output.WriteLine("--selector   Instead of --expected, for a suite with no recorded snapshot: a Playwright locator such as");
        output.WriteLine("             getByTestId('x') or getByRole('button', { name: 'Save' }). Only the signals it states are used,");
        output.WriteLine("             the output is wrapped as { PartialEvidence, DerivedExpected, Result }, and the same gate applies.");
        output.WriteLine("--tree       A UiElementInfo JSON tree: a freshly captured snapshot of the live application.");
        output.WriteLine("--compact    Print a smaller result: elements without their child subtree and only the best 5 candidates");
        output.WriteLine("             (CandidateCount still reports the full number). The decision and exit code are unchanged.");
        output.WriteLine("--profile    Threshold profile (default: balanced). See SimilarityWeights.FromProfile.");
        output.WriteLine();
        output.WriteLine("Both JSON inputs must be shaped like UiTreeSerializer.ToJson's output - produce this");
        output.WriteLine("from any language or tool; no C# or FlaUI/Playwright .NET dependency is needed to call this.");
        output.WriteLine();
        output.WriteLine("Prints a HealResult as JSON to stdout. Exit code 0 means IsConfident (safe to accept");
        output.WriteLine("Matched); non-zero (1) means not confident - do not auto-accept Matched; 2 means an error");
        output.WriteLine("(bad input, missing file) occurred before a decision could be made.");
    }
}
