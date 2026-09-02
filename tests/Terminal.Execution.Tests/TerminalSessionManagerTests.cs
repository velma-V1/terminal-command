using System.Runtime.CompilerServices;
using Terminal.Core.Actions;
using Terminal.Core.Evidence;
using Terminal.Execution;

namespace Terminal.Execution.Tests;

public sealed class TerminalSessionManagerTests
{
    [Fact]
    public async Task Manager_owns_foreground_and_background_sessions_until_explicit_close()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var host = new FakeSessionHost();
        await using var manager = new TerminalSessionManager([host]);
        var foreground = Request(TerminalSessionMode.Foreground, ActionBackend.Windows);
        var background = Request(TerminalSessionMode.Background, ActionBackend.Windows);

        var first = await manager.StartAsync(foreground, cancellationToken);
        var second = await manager.StartAsync(background, cancellationToken);

        Assert.Equal(first.SessionId, manager.ForegroundSessionId);
        Assert.Equal(2, manager.Count);
        Assert.True(manager.TryGet(second.SessionId, out _));

        await manager.CloseAsync(first.SessionId, cancellationToken);
        Assert.Null(manager.ForegroundSessionId);
        Assert.Equal(1, manager.Count);
        Assert.True(manager.TryGet(second.SessionId, out _));
    }

    [Fact]
    public async Task Second_foreground_session_is_rejected_instead_of_implicitly_killing_the_first()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var host = new FakeSessionHost();
        await using var manager = new TerminalSessionManager([host]);
        await manager.StartAsync(Request(TerminalSessionMode.Foreground, ActionBackend.Windows), cancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.StartAsync(
                Request(TerminalSessionMode.Foreground, ActionBackend.Windows),
                cancellationToken).AsTask());

        Assert.Equal(1, host.Starts);
        Assert.Equal(1, manager.Count);
    }

    [Fact]
    public async Task Unsupported_backend_fails_before_any_session_is_started()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var host = new FakeSessionHost(ActionBackend.Windows);
        await using var manager = new TerminalSessionManager([host]);

        await Assert.ThrowsAsync<NotSupportedException>(
            () => manager.StartAsync(
                Request(TerminalSessionMode.Background, ActionBackend.Wsl),
                cancellationToken).AsTask());

        Assert.Equal(0, host.Starts);
    }

    [Fact]
    public async Task Manager_disposal_terminates_every_owned_session()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var host = new FakeSessionHost();
        var manager = new TerminalSessionManager([host]);
        var first = await manager.StartAsync(Request(TerminalSessionMode.Background, ActionBackend.Windows), cancellationToken);
        var second = await manager.StartAsync(Request(TerminalSessionMode.Background, ActionBackend.Windows), cancellationToken);

        await manager.DisposeAsync();

        Assert.Equal(TerminalSessionState.Closed, first.State);
        Assert.Equal(TerminalSessionState.Closed, second.State);
        Assert.Equal(2, host.Sessions.Sum(static session => session.TerminateSignals));
    }

    [Fact]
    public void Session_request_rejects_invalid_terminal_dimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TerminalDimensions(0, 24));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TerminalDimensions(80, 0));
    }

    private static TerminalSessionRequest Request(TerminalSessionMode mode, ActionBackend backend)
        => new(
            Guid.NewGuid(),
            CreateAction(backend),
            mode,
            new TerminalDimensions(100, 40));

    private static TerminalAction CreateAction(ActionBackend backend)
    {
        var cwd = backend == ActionBackend.Wsl ? "/tmp" : "C:\\repo";
        return new TerminalAction(
            Guid.NewGuid(),
            "test",
            "terminal.session",
            backend == ActionBackend.Wsl ? "/bin/sh" : "cmd.exe",
            backend == ActionBackend.Wsl ? ["-c", "echo ready"] : ["/c", "echo ready"],
            backend,
            new ResourceRef(
                backend == ActionBackend.Wsl ? ResourceEnvironment.Wsl : ResourceEnvironment.Windows,
                ResourceKind.Directory,
                cwd,
                cwd,
                $"cwd:{backend}",
                backend == ActionBackend.Wsl ? "wsl:test" : "windows:test",
                null,
                DateTimeOffset.UtcNow,
                RevalidationMethod.DirectoryIdentity),
            new Dictionary<string, string?>(),
            [],
            new ScopeContract([]),
            TimeSpan.FromMinutes(5),
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
    }

    private sealed class FakeSessionHost(params ActionBackend[] backends) : ITerminalSessionHost
    {
        private readonly HashSet<ActionBackend> _backends = new(
            backends.Length == 0 ? [ActionBackend.Windows] : backends);

        public int Starts { get; private set; }
        public List<FakeSession> Sessions { get; } = [];
        public IReadOnlySet<ActionBackend> SupportedBackends => _backends;

        public ValueTask<ITerminalSession> StartAsync(
            TerminalSessionRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_backends.Contains(request.Action.Backend))
            {
                throw new NotSupportedException();
            }

            Starts++;
            var session = new FakeSession(request.SessionId, request.Action.Backend);
            Sessions.Add(session);
            return ValueTask.FromResult<ITerminalSession>(session);
        }
    }

    private sealed class FakeSession(Guid sessionId, ActionBackend backend) : ITerminalSession
    {
        public Guid SessionId { get; } = sessionId;
        public ActionBackend Backend { get; } = backend;
        public TerminalSessionFeatures Features { get; } = new(
            Interactive: true,
            Resize: true,
            CtrlC: true,
            StreamingOutput: true);
        public TerminalSessionState State { get; private set; } = TerminalSessionState.Running;
        public int TerminateSignals { get; private set; }

        public ValueTask WriteAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (State != TerminalSessionState.Running)
            {
                throw new InvalidOperationException();
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask ResizeAsync(TerminalDimensions dimensions, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public ValueTask SignalAsync(TerminalSignal signal, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (signal == TerminalSignal.Terminate)
            {
                TerminateSignals++;
                State = TerminalSessionState.Closed;
            }
            return ValueTask.CompletedTask;
        }

        public async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadOutputAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            yield break;
        }

        public ValueTask DisposeAsync()
        {
            State = TerminalSessionState.Closed;
            return ValueTask.CompletedTask;
        }
    }
}
