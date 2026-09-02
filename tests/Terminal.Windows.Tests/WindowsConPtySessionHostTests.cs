using System.Text;
using Terminal.Core.Actions;
using Terminal.Core.Evidence;
using Terminal.Execution;

namespace Terminal.Windows.Tests;

public sealed class WindowsConPtySessionHostTests
{
    [Fact]
    public void Host_claims_only_the_windows_backend()
    {
        var host = new WindowsConPtySessionHost();

        Assert.Equal([ActionBackend.Windows], host.SupportedBackends);
    }

    [Fact]
    public async Task Start_on_non_windows_fails_before_launch()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var host = new WindowsConPtySessionHost();
        var request = Request("cmd.exe", ["/d", "/s", "/c", "echo should-not-run"]);

        await Assert.ThrowsAsync<PlatformNotSupportedException>(
            () => host.StartAsync(request, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task Windows_host_runs_command_through_real_pseudoconsole_and_streams_output()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var cancellationToken = TestContext.Current.CancellationToken;
        var host = new WindowsConPtySessionHost();
        var request = Request("cmd.exe", ["/d", "/s", "/c", "echo conpty-ready"]);
        await using var session = await host.StartAsync(request, cancellationToken);

        Assert.Equal(request.SessionId, session.SessionId);
        Assert.Equal(ActionBackend.Windows, session.Backend);
        Assert.True(session.Features.Interactive);
        Assert.True(session.Features.Resize);
        Assert.True(session.Features.CtrlC);
        Assert.True(session.Features.StreamingOutput);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var output = await ReadAllAsync(session, timeout.Token);

        Assert.Contains("conpty-ready", output, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TerminalSessionState.Exited, session.State);
    }

    [Fact]
    public async Task Windows_host_accepts_resize_while_session_is_running()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var cancellationToken = TestContext.Current.CancellationToken;
        var host = new WindowsConPtySessionHost();
        var request = Request(
            "cmd.exe",
            ["/d", "/s", "/c", "ping -n 3 127.0.0.1 >nul"]);
        await using var session = await host.StartAsync(request, cancellationToken);

        await session.ResizeAsync(new TerminalDimensions(120, 50), cancellationToken);
        await session.SignalAsync(TerminalSignal.Terminate, cancellationToken);

        Assert.Contains(
            session.State,
            new[] { TerminalSessionState.Cancelled, TerminalSessionState.Exited, TerminalSessionState.Closed });
    }

    private static async Task<string> ReadAllAsync(
        ITerminalSession session,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await foreach (var chunk in session.ReadOutputAsync(cancellationToken))
        {
            await buffer.WriteAsync(chunk, cancellationToken);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static TerminalSessionRequest Request(string executable, IReadOnlyList<string> arguments)
    {
        var sessionId = Guid.NewGuid();
        var action = new TerminalAction(
            Guid.NewGuid(),
            "test",
            "terminal.session.windows",
            executable,
            arguments,
            ActionBackend.Windows,
            new ResourceRef(
                ResourceEnvironment.Windows,
                ResourceKind.Directory,
                Environment.CurrentDirectory,
                Environment.CurrentDirectory,
                null,
                Environment.UserName,
                null,
                DateTimeOffset.UtcNow,
                RevalidationMethod.DirectoryIdentity),
            new Dictionary<string, string?>(),
            [],
            new ScopeContract([]),
            TimeSpan.FromSeconds(30),
            256 * 1024 * 1024,
            MutationClass.Ephemeral,
            RecoveryClass.None,
            new Provenance(
                ProvenanceSourceType.System,
                "test",
                TrustClass.TrustedLocal,
                DateTimeOffset.UtcNow,
                null,
                []),
            DateTimeOffset.UtcNow);

        return new TerminalSessionRequest(
            sessionId,
            action,
            TerminalSessionMode.Foreground,
            new TerminalDimensions(100, 40));
    }
}
