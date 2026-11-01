using System.Reflection;
using AutomationSandbox.Mcp;

// An MCP server over stdio: JSON-RPC messages, one per line, on stdin and stdout. Nothing but protocol messages may go to
// stdout, so diagnostics (there are almost none) go to stderr. It is read-only and takes no credential from a caller; the
// tokens and model keys it needs are read from this process's own environment (see docs/cli-and-ci-integration.md).
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

var version = typeof(McpProtocol).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0";
var plus = version.IndexOf('+');
if (plus >= 0)
{
    version = version.Substring(0, plus);
}

var tools = McpTools.Create(Environment.GetEnvironmentVariable, null);
try
{
    await McpProtocol.RunAsync(Console.In, Console.Out, tools, version, cancellation.Token);
}
catch (OperationCanceledException)
{
    // Interrupted: stop quietly.
}

return 0;
