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
    cancellation.Token);
