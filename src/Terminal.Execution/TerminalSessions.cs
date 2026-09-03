using System.Collections.ObjectModel;
using Terminal.Core.Actions;

namespace Terminal.Execution;

public enum TerminalSessionMode
{
    Foreground,
    Background
}

public enum TerminalSessionState
{
    Starting,
    Running,
    Exited,
    Cancelled,
    Failed,
    Closed
}

public enum TerminalSignal
{
    CtrlC,
    Terminate
}

public readonly record struct TerminalDimensions
{
    public TerminalDimensions(short columns, short rows)
    {
        if (columns <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(columns));
        }

        if (rows <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        Columns = columns;
        Rows = rows;
    }

    public short Columns { get; }
    public short Rows { get; }
}

public readonly record struct TerminalSessionFeatures(
    bool Interactive,
    bool Resize,
    bool CtrlC,
    bool StreamingOutput);

public sealed record TerminalSessionRequest
{
    public TerminalSessionRequest(
        Guid sessionId,
        TerminalAction action,
        TerminalSessionMode mode,
        TerminalDimensions initialDimensions)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("Session ID must not be empty.", nameof(sessionId));
        }

        SessionId = sessionId;
        Action = action ?? throw new ArgumentNullException(nameof(action));
        Mode = mode;
        InitialDimensions = initialDimensions;
    }

    public Guid SessionId { get; }
    public TerminalAction Action { get; }
    public TerminalSessionMode Mode { get; }
    public TerminalDimensions InitialDimensions { get; }
}

public interface ITerminalSession : IAsyncDisposable
{
    Guid SessionId { get; }
    ActionBackend Backend { get; }
    TerminalSessionFeatures Features { get; }
    TerminalSessionState State { get; }

    ValueTask WriteAsync(
        ReadOnlyMemory<byte> input,
        CancellationToken cancellationToken = default);

    ValueTask ResizeAsync(
        TerminalDimensions dimensions,
        CancellationToken cancellationToken = default);

    ValueTask SignalAsync(
        TerminalSignal signal,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<ReadOnlyMemory<byte>> ReadOutputAsync(
        CancellationToken cancellationToken = default);
}

public interface ITerminalSessionHost
{
    IReadOnlySet<ActionBackend> SupportedBackends { get; }

    ValueTask<ITerminalSession> StartAsync(
        TerminalSessionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class TerminalSessionManager : IAsyncDisposable
{
    private readonly IReadOnlyList<ITerminalSessionHost> _hosts;
    private readonly Dictionary<Guid, SessionEntry> _sessions = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public TerminalSessionManager(IReadOnlyList<ITerminalSessionHost> hosts)
    {
        ArgumentNullException.ThrowIfNull(hosts);
        if (hosts.Count == 0)
        {
            throw new ArgumentException("At least one session host is required.", nameof(hosts));
        }

        if (hosts.Any(static host => host is null))
        {
            throw new ArgumentException("Session hosts cannot contain null entries.", nameof(hosts));
        }

        _hosts = Array.AsReadOnly(hosts.ToArray());
    }

    public int Count
    {
        get
        {
            _gate.Wait();
            try
            {
                return _sessions.Count;
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    public Guid? ForegroundSessionId
    {
        get
        {
            _gate.Wait();
            try
            {
                return _sessions.Values
                    .Where(static entry => entry.Mode == TerminalSessionMode.Foreground)
                    .Select(static entry => (Guid?)entry.Session.SessionId)
                    .SingleOrDefault();
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    public bool TryGet(Guid sessionId, out ITerminalSession? session)
    {
        _gate.Wait();
        try
        {
            if (_sessions.TryGetValue(sessionId, out var entry))
            {
                session = entry.Session;
                return true;
            }

            session = null;
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<ITerminalSession> StartAsync(
        TerminalSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_sessions.ContainsKey(request.SessionId))
            {
                throw new InvalidOperationException($"Session {request.SessionId} is already owned by Terminal.");
            }

            if (request.Mode == TerminalSessionMode.Foreground &&
                _sessions.Values.Any(static entry => entry.Mode == TerminalSessionMode.Foreground))
            {
                throw new InvalidOperationException("A foreground terminal session is already active.");
            }

            var matchingHosts = _hosts
                .Where(host => host.SupportedBackends.Contains(request.Action.Backend))
                .ToArray();
            if (matchingHosts.Length == 0)
            {
                throw new NotSupportedException(
                    $"No terminal session host supports backend {request.Action.Backend}.");
            }

            if (matchingHosts.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Multiple terminal session hosts claim backend {request.Action.Backend}; routing must be unambiguous.");
            }

            var session = await matchingHosts[0]
                .StartAsync(request, cancellationToken)
                .ConfigureAwait(false);
            if (session.SessionId != request.SessionId || session.Backend != request.Action.Backend)
            {
                await session.DisposeAsync().ConfigureAwait(false);
                throw new InvalidDataException("Session host returned an identity/backend mismatch.");
            }

            _sessions.Add(request.SessionId, new SessionEntry(session, request.Mode));
            return session;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask CloseAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        ITerminalSession session;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (!_sessions.Remove(sessionId, out var entry))
            {
                throw new KeyNotFoundException($"Session {sessionId} is not owned by Terminal.");
            }

            session = entry.Session;
        }
        finally
        {
            _gate.Release();
        }

        await TerminateAndDisposeAsync(session, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        ITerminalSession[] sessions;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            sessions = _sessions.Values.Select(static entry => entry.Session).ToArray();
            _sessions.Clear();
        }
        finally
        {
            _gate.Release();
        }

        List<Exception>? failures = null;
        foreach (var session in sessions)
        {
            try
            {
                await TerminateAndDisposeAsync(session, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        _gate.Dispose();
        if (failures is not null)
        {
            throw new AggregateException("One or more terminal sessions failed to close cleanly.", failures);
        }
    }

    private static async ValueTask TerminateAndDisposeAsync(
        ITerminalSession session,
        CancellationToken cancellationToken)
    {
        try
        {
            if (session.State is TerminalSessionState.Starting or TerminalSessionState.Running)
            {
                await session.SignalAsync(TerminalSignal.Terminate, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TerminalSessionManager));
        }
    }

    private sealed record SessionEntry(ITerminalSession Session, TerminalSessionMode Mode);
}
