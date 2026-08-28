using SelfHealing;
using UiModel;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        var treePath = args[0];
        string? applicationName = null;
        var maxProbedElements = 50;
        string? outPath = null;

        for (var i = 1; i < args.Length; i++)
        {
            string value;
            switch (args[i])
            {
                case "--app":
                    if (!TryReadValue(args, ref i, out value))
                    {
                        Console.Error.WriteLine("Missing value for --app.");
                        return 1;
                    }

                    applicationName = value;
                    break;

                case "--max-probed":
                    if (!TryReadValue(args, ref i, out value) || !int.TryParse(value, out maxProbedElements))
                    {
                        Console.Error.WriteLine("Missing or invalid integer value for --max-probed.");
                        return 1;
                    }

                    break;

                case "--out":
                    if (!TryReadValue(args, ref i, out value))
                    {
                        Console.Error.WriteLine("Missing value for --out.");
                        return 1;
                    }

                    outPath = value;
                    break;

                default:
                    Console.Error.WriteLine($"Unknown argument: {args[i]}");
                    PrintUsage();
                    return 1;
            }
        }

        if (!File.Exists(treePath))
        {
            Console.Error.WriteLine($"Tree file not found: {treePath}");
            return 1;
        }

        applicationName ??= Path.GetFileNameWithoutExtension(treePath);

        UiElementInfo tree;
        try
        {
            tree = UiTreeSerializer.FromJson(File.ReadAllText(treePath));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to parse '{treePath}' as a UiElementInfo tree: {ex.Message}");
            return 1;
        }

        var report = TreeCalibrator.Calibrate(tree, applicationName, maxProbedElements);
        var markdown = report.ToMarkdownReport();

        Console.WriteLine(markdown);

        outPath ??= $"{applicationName}-calibration-report.md";
        try
        {
            File.WriteAllText(outPath, markdown);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Failed to write report to '{outPath}': {ex.Message}");
            return 1;
        }

        Console.WriteLine($"Report written to {Path.GetFullPath(outPath)}");

        return 0;
    }

    private static bool TryReadValue(string[] args, ref int i, out string value)
    {
        if (i + 1 >= args.Length)
        {
            value = "";
            return false;
        }

        i++;
        value = args[i];
        return true;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: dotnet run --project samples/CalibrationCli -- <tree.json> [--app <name>] [--max-probed <n>] [--out <path>]");
        Console.WriteLine();
        Console.WriteLine("  <tree.json>       Path to a captured UiElementInfo tree (UiTreeSerializer JSON format).");
        Console.WriteLine("  --app <name>      Application name shown in the report (default: the file name).");
        Console.WriteLine("  --max-probed <n>  Max number of elements to probe (default: 50).");
        Console.WriteLine("  --out <path>      Where to write the markdown report (default: <app>-calibration-report.md).");
    }
}
