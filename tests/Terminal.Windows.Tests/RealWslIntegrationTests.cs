using Terminal.Windows;

namespace Terminal.Windows.Tests;

public sealed class RealWslIntegrationTests
{
    [Fact]
    public async Task Real_wsl_agent_handshake_succeeds_when_explicitly_enabled()
    {
        if (!OperatingSystem.IsWindows()) return;
        if (!string.Equals(Environment.GetEnvironmentVariable("TERMINAL_RUN_WSL_E2E"), "1", StringComparison.Ordinal)) return;

        var cancellationToken = TestContext.Current.CancellationToken;
        var distro = Environment.GetEnvironmentVariable("TERMINAL_WSL_DISTRO") ?? "Ubuntu";
        var agent = Environment.GetEnvironmentVariable("TERMINAL_WSL_AGENT") ?? "terminal-linux-agent";

        await using var transport = await WslTransport.ConnectAsync(
            new WslTransportOptions(distro, agent),
            new WindowsWslProcessFactory(),
            cancellationToken);
        var health = await transport.HealthAsync(cancellationToken);

        Assert.True(transport.IsAvailable);
        Assert.True(health.Healthy);
    }
}
