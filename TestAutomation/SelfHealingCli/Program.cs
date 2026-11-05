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

        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        string? expectedPath = null;
        string? selector = null;
        var compact = false;
        string? treePath = null;
        var profile = ThresholdProfile.Balanced;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--expected" when i + 1 < args.Length:
                    expectedPath = args[++i];
                    break;
                case "--selector" when i + 1 < args.Length:
                    selector = args[++i];
                    break;
                case "--compact":
                    compact = true;
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

        if (treePath is null || (expectedPath is null) == (selector is null))
        {
            Console.Error.WriteLine("--tree and exactly one of --expected or --selector are required.");
            PrintUsage();
            return 1;
        }

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
                CompactResult(result);
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

    // Keeps the decision and its evidence, drops what makes the output many times larger than its
    // input (#620): every element carries its whole subtree, and every scored node is a candidate.
    // Elements are shallow-copied so the live tree handed to the resolver is not modified.
    private const int CompactCandidateLimit = 5;

    private static void CompactResult(HealResult result)
    {
        result.Matched = WithoutChildren(result.Matched);
        result.HeuristicMatched = WithoutChildren(result.HeuristicMatched);
        if (result.Candidates is not null)
        {
            result.Candidates = result.Candidates
                .OrderByDescending(candidate => candidate.TotalScore)
                .Take(CompactCandidateLimit)
                .Select(candidate => new CandidateScore
                {
                    Candidate = WithoutChildren(candidate.Candidate)!,
                    TotalScore = candidate.TotalScore,
                    Components = candidate.Components,
                    EvidenceCoverage = candidate.EvidenceCoverage,
                    CandidateId = candidate.CandidateId,
                })
                .ToList();
        }
    }

    private static UiElementInfo? WithoutChildren(UiElementInfo? element) =>
        element is null
            ? null
            : new UiElementInfo
            {
                ControlType = element.ControlType,
                Name = element.Name,
                AutomationId = element.AutomationId,
                ClassName = element.ClassName,
                BoundingRectangle = element.BoundingRectangle,
                ParentControlType = element.ParentControlType,
                ParentAutomationId = element.ParentAutomationId,
                SiblingIndex = element.SiblingIndex,
                SiblingCount = element.SiblingCount,
                TestIntent = element.TestIntent,
                ChildControlTypeSignature = element.ChildControlTypeSignature,
            };

    private static void PrintUsage()
    {
        Console.WriteLine("================================================================================");
        Console.WriteLine("  Automation Sandbox - Self-Healing CLI");
        Console.WriteLine("  (language/platform-agnostic locator healing: JSON in, JSON out)");
        Console.WriteLine("================================================================================");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  automation-sandbox-heal --expected <stale-element.json> --tree <live-tree.json> [--profile balanced|conservative|aggressive] [--compact]");
        Console.WriteLine("  automation-sandbox-heal --selector \"<playwright locator>\" --tree <live-tree.json> [--profile ...]");
        Console.WriteLine();
        Console.WriteLine("--expected   A single UiElementInfo JSON object: the stale/broken locator's last known snapshot.");
        Console.WriteLine("--selector   Instead of --expected, for a suite with no recorded snapshot: a Playwright locator such as");
        Console.WriteLine("             getByTestId('x') or getByRole('button', { name: 'Save' }). Only the signals it states are used,");
        Console.WriteLine("             the output is wrapped as { PartialEvidence, DerivedExpected, Result }, and the same gate applies.");
        Console.WriteLine("--tree       A UiElementInfo JSON tree: a freshly captured snapshot of the live application.");
        Console.WriteLine("--compact    Print a smaller result: elements without their child subtree and only the best 5 candidates");
        Console.WriteLine("             (CandidateCount still reports the full number). The decision and exit code are unchanged.");
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
