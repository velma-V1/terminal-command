using System.Text;
using Terminal.Core.Actions;
using Terminal.Core.Evidence;
using Terminal.Execution;

namespace Terminal.LinuxAgent.Tests;

public sealed class LinuxPtySessionHostTests
{
    [Fact]
    public void Host_claims_only_the_wsl_backend()
    {
        var host = new LinuxPtySessionHost();

        Assert.Equal([ActionBackend.Wsl], host.SupportedBackends);
    }

    [Fact]
    public async Task Start_on_non_linux_fails_before_launch()
    {
        if (OperatingSystem.IsLinux())
        {
            return;
        }

        var host = new LinuxPtySessionHost();
        var request = Request("/bin/sh", ["-c", "printf should-not-run"]);

        await Assert.ThrowsAsync<PlatformNotSupportedException>(
            () => host.StartAsync(request, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task Linux_host_provides_real_tty_and_streams_output()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var cancellationToken = TestContext.Current.CancellationToken;
        var host = new LinuxPtySessionHost();
        var request = Request(
            "/bin/sh",
            ["-c", "test -t 0 && test -t 1 && printf 'linux-pty-ready\\n'"]);
        await using var session = await host.StartAsync(request, cancellationToken);

        Assert.Equal(request.SessionId, session.SessionId);
        Assert.Equal(ActionBackend.Wsl, session.Backend);
        Assert.True(session.Features.Interactive);
        Assert.True(session.Features.Resize);
        Assert.True(session.Features.CtrlC);
        Assert.True(session.Features.StreamingOutput);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var output = await ReadAllAsync(session, timeout.Token);

        Assert.Contains("linux-pty-ready", output, StringComparison.Ordinal);
        Assert.Equal(TerminalSessionState.Exited, session.State);
    }

    [Fact]
    public async Task Linux_host_streams_input_to_the_pty_child()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var cancellationToken = TestContext.Current.CancellationToken;
        var host = new LinuxPtySessionHost();
        var request = Request(
            "/bin/sh",
            ["-c", "IFS= read -r line; printf 'received:%s\\n' \"$line\""]);
        await using var session = await host.StartAsync(request, cancellationToken);

        await session.WriteAsync(Encoding.UTF8.GetBytes("terminal-input\n"), cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var output = await ReadAllAsync(session, timeout.Token);

        Assert.Contains("received:terminal-input", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Linux_host_runtime_resize_updates_the_child_terminal_size()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var cancellationToken = TestContext.Current.CancellationToken;
        var host = new LinuxPtySessionHost();
        var request = Request(
            "/bin/sh",
            ["-c", "printf 'before:'; stty size; IFS= read -r line; printf 'after:'; stty size"],
            new TerminalDimensions(80, 24));
        await using var session = await host.StartAsync(request, cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var firstOutput = await ReadUntilAsync(session, "before:24 80", timeout.Token);
        Assert.Contains("before:24 80", firstOutput, StringComparison.Ordinal);

        await session.ResizeAsync(new TerminalDimensions(120, 50), timeout.Token);
        await session.WriteAsync(Encoding.UTF8.GetBytes("continue\n"), timeout.Token);
        var remaining = await ReadAllAsync(session, timeout.Token);

        Assert.Contains("after:50 120", remaining, StringComparison.Ordinal);
    }

    private static async Task<string> ReadUntilAsync(
        ITerminalSession session,
        string marker,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        await foreach (var chunk in session.ReadOutputAsync(cancellationToken))
        {
            builder.Append(Encoding.UTF8.GetString(chunk.Span));
            if (builder.ToString().Contains(marker, StringComparison.Ordinal))
            {
                break;
            }
        }

        return builder.ToString();
    }

    private static async Task<string> ReadAllAsync(
        ITerminalSession session,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        await foreach (var chunk in session.ReadOutputAsync(cancellationToken))
        {
            builder.Append(Encoding.UTF8.GetString(chunk.Span));
        }

        return builder.ToString();
    }

    private static TerminalSessionRequest Request(
        string executable,
        IReadOnlyList<string> arguments,
        TerminalDimensions? dimensions = null)
    {
        var sessionId = Guid.NewGuid();
        var cwd = Environment.CurrentDirectory;
        var action = new TerminalAction(
            Guid.NewGuid(),
            "test",
            "terminal.session.linux",
            executable,
            arguments,
            ActionBackend.Wsl,
            new ResourceRef(
                ResourceEnvironment.Wsl,
                ResourceKind.Directory,
                cwd,
                cwd,
                null,
                Environment.UserName,
                null,
                DateTimeOffset.UtcNow,
                RevalidationMethod.DirectoryIdentity),
            new Dictionary<string, string?>(),
            [],
            new ScopeContract([]),
            TimeSpan.FromSeconds(30),
            null,
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
            dimensions ?? new TerminalDimensions(100, 40));
    }
}
