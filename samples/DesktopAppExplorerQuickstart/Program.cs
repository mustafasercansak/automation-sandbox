using AutomationSandbox.Discovery;
using AutomationSandbox.UiModel;

// Windows/FlaUI-only - this project targets net8.0-windows and requires the real UIA COM APIs
// to actually run. It compiles (EnableWindowsTargeting) but cannot execute on Linux/macOS.
internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        ApplicationConnector connector;
        try
        {
            connector = args[0] == "--attach"
                ? AttachToRunningProcess(args)
                : ApplicationConnector.Launch(args[0]);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Could not launch/attach: {ex.Message}");
            Console.ResetColor();
            return 1;
        }

        using (connector)
        {
            try
            {
                Console.WriteLine("1. Waiting for the main window...");
                var window = connector.GetMainWindow();
                Console.WriteLine($"   - Main window found: \"{window.Title}\"");

                Console.WriteLine();
                Console.WriteLine("2. Walking the UI tree (bounded: MaxDepth=25, MaxElements=5000, Timeout=15s)...");
                var options = new DiscoveryOptions
                {
                    Timeout = TimeSpan.FromSeconds(15),
                };
                var result = UiTreeWalker.Discover(window, options);

                Console.WriteLine($"   - Captured {result.CapturedCount} element(s) in {result.Elapsed.TotalSeconds:0.0}s");
                if (result.HitMaxDepth)
                {
                    Console.WriteLine("   - Hit MaxDepth - the tree is deeper than this walk covered.");
                }

                if (result.HitMaxElements)
                {
                    Console.WriteLine("   - Hit MaxElements - the tree is larger than this walk covered.");
                }

                if (result.Warnings.Count > 0)
                {
                    Console.WriteLine($"   - {result.Warnings.Count} warning(s) recorded (non-fatal element read failures).");
                }

                var elements = result.Root.Flatten().ToList();

                Console.WriteLine();
                Console.WriteLine("3. Control type breakdown:");
                foreach (var group in elements.GroupBy(e => string.IsNullOrWhiteSpace(e.ControlType) ? "(unknown)" : e.ControlType)
                                               .OrderByDescending(g => g.Count()))
                {
                    Console.WriteLine($"   - {group.Key,-20} {group.Count()}");
                }

                // This is the exact case study this project's own demo apps (WinFormsApp/WpfApp)
                // are built around: an element with no AutomationId, or one that collides with a
                // sibling's, is a locator that will break (or already has broken) the moment
                // anything nearby changes.
                var emptyIds = elements.Count(e => string.IsNullOrWhiteSpace(e.AutomationId));
                var duplicateIds = elements
                    .Where(e => !string.IsNullOrWhiteSpace(e.AutomationId))
                    .GroupBy(e => e.AutomationId)
                    .Where(g => g.Count() > 1)
                    .ToList();

                Console.WriteLine();
                Console.WriteLine("4. Locator health (why this project exists):");
                Console.WriteLine($"   - {emptyIds} element(s) with no AutomationId - name/position-based matching is the only option for these.");
                Console.WriteLine($"   - {duplicateIds.Count} AutomationId(s) shared by more than one element:");
                foreach (var group in duplicateIds.Take(10))
                {
                    Console.WriteLine($"       \"{group.Key}\" x{group.Count()} ({string.Join(", ", group.Select(e => e.ControlType))})");
                }

                var snapshotPath = Path.Combine(Directory.GetCurrentDirectory(), "desktop-app-snapshot.json");
                File.WriteAllText(snapshotPath, UiTreeSerializer.ToJson(result.Root));
                Console.WriteLine();
                Console.WriteLine($"5. Full tree snapshot written to: {snapshotPath}");
                Console.WriteLine("   Use this as a baseline `expected` UiElementInfo for SelfHealingEngine/SelfHealingResolver -");
                Console.WriteLine("   see docs/getting-started.md and docs/desktop-automation.md.");

                return 0;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error exploring the application: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                Console.ResetColor();
                return 1;
            }
        }
    }

    private static ApplicationConnector AttachToRunningProcess(string[] args)
    {
        if (args.Length < 2)
        {
            throw new ArgumentException("--attach requires a process name, e.g. --attach MyApp");
        }

        return ApplicationConnector.Attach(args[1]);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("================================================================================");
        Console.WriteLine("  Automation Sandbox - Desktop App Explorer Quickstart");
        Console.WriteLine("  (Discovery + UiTreeWalker against any real .NET Framework or .NET desktop app)");
        Console.WriteLine("================================================================================");
        Console.WriteLine();
        Console.WriteLine("Launch a fresh instance and explore it:");
        Console.WriteLine("  dotnet run --project samples/DesktopAppExplorerQuickstart -- \"C:\\path\\to\\YourApp.exe\"");
        Console.WriteLine();
        Console.WriteLine("Attach to an already-running instance by process name (no .exe extension):");
        Console.WriteLine("  dotnet run --project samples/DesktopAppExplorerQuickstart -- --attach YourApp");
        Console.WriteLine();
        Console.WriteLine("Requires Windows (FlaUI/UIA3 uses Windows UI Automation COM APIs) and works");
        Console.WriteLine("against both .NET Framework (WinForms) and modern .NET (WinForms/WPF) apps -");
        Console.WriteLine("the target application's runtime doesn't matter, only that it exposes UIA.");
    }
}
