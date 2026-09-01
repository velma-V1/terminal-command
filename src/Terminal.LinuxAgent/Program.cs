using Terminal.LinuxAgent;

if (args.Length != 1 || !string.Equals(args[0], "--stdio", StringComparison.Ordinal))
{
    Console.Error.WriteLine("usage: terminal-linux-agent --stdio");
    return 2;
}

try
{
    var host = new StdioAgentHost(
        Console.OpenStandardInput(),
        Console.OpenStandardOutput(),
        new LinuxAgentProtocolHandler());
    await host.RunAsync().ConfigureAwait(false);
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"terminal-linux-agent: {exception.GetType().Name}: {exception.Message}");
    return 1;
}
