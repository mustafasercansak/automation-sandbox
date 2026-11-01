using System.Diagnostics;
using System.Threading;
using AutomationSandbox.AgentCli;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

return await AgentCommand.RunAsync(
    args,
    Environment.GetEnvironmentVariable,
    path => File.ReadAllText(path),
    Console.Out,
    Console.Error,
    null,
    cancellation.Token,
    ListTrackedFiles,
    File.WriteAllText);

// The files git tracks under a checkout, as repository-relative paths. Run without a shell, with the directory as the
// working directory; the docs task accepts a path from an issue only if it is exactly one of these.
static IReadOnlyCollection<string> ListTrackedFiles(string root)
{
    var start = new ProcessStartInfo("git")
    {
        WorkingDirectory = root,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };
    foreach (var argument in new[] { "ls-files", "-z" })
    {
        start.ArgumentList.Add(argument);
    }

    using var process = Process.Start(start) ?? throw new InvalidOperationException("git could not be started.");
    var output = process.StandardOutput.ReadToEnd();
    process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException("git ls-files failed.");
    }

    return output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
}
