using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Terminal.Core.Actions;
using Terminal.Execution;

namespace Terminal.LinuxAgent;

public sealed class LinuxPtySessionHost : ITerminalSessionHost
{
    private static readonly IReadOnlySet<ActionBackend> Backends =
        new HashSet<ActionBackend> { ActionBackend.Wsl };

    public IReadOnlySet<ActionBackend> SupportedBackends => Backends;

    public async ValueTask<ITerminalSession> StartAsync(
        TerminalSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("Linux PTY sessions require Linux.");
        }

        if (request.Action.Backend != ActionBackend.Wsl)
        {
            throw new NotSupportedException(
                $"Linux PTY sessions do not support backend {request.Action.Backend}.");
        }

        if (request.Action.MemoryLimitBytes is not null)
        {
            throw new NotSupportedException(
                "Linux PTY sessions cannot accept a memory limit until the interactive cgroup boundary is wired.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var script = FindExecutable("/usr/bin/script", "/bin/script");
        var stty = FindExecutable("/usr/bin/stty", "/bin/stty");
        var startInfo = BuildStartInfo(script, stty, request);
        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("util-linux script did not start.");
        }

        var stderrDrain = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            var slave = await OpenChildPtySlaveAsync(process, cancellationToken).ConfigureAwait(false);
            await ReleaseStartupBarrierAsync(process, cancellationToken).ConfigureAwait(false);
            return new LinuxPtySession(request.SessionId, process, slave, stderrDrain);
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }

            _ = await stderrDrain.ConfigureAwait(false);
            process.Dispose();
            throw;
        }
    }

    private static ProcessStartInfo BuildStartInfo(
        string script,
        string stty,
        TerminalSessionRequest request)
    {
        var action = request.Action;
        var dimensions = request.InitialDimensions;
        var command = new StringBuilder();
        command.Append("IFS= read -r _terminal_gate || exit 125; ")
            .Append(ShellQuote(stty))
            .Append(" rows ")
            .Append(dimensions.Rows)
            .Append(" cols ")
            .Append(dimensions.Columns)
            .Append(" || exit 126; exec ")
            .Append(ShellQuote(action.Operation));
        foreach (var argument in action.Arguments)
        {
            command.Append(' ').Append(ShellQuote(argument));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = script,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = action.WorkingDirectory.CanonicalIdentity
        };
        startInfo.ArgumentList.Add("--quiet");
        startInfo.ArgumentList.Add("--return");
        startInfo.ArgumentList.Add("--flush");
        startInfo.ArgumentList.Add("--echo");
        startInfo.ArgumentList.Add("never");
        startInfo.ArgumentList.Add("--command");
        startInfo.ArgumentList.Add(command.ToString());
        startInfo.ArgumentList.Add("/dev/null");

        foreach (var (key, value) in action.EnvironmentDelta)
        {
            if (value is null)
            {
                startInfo.Environment.Remove(key);
            }
            else
            {
                startInfo.Environment[key] = value;
            }
        }

        return startInfo;
    }

    private static async ValueTask ReleaseStartupBarrierAsync(
        Process process,
        CancellationToken cancellationToken)
    {
        await process.StandardInput.BaseStream
            .WriteAsync(new byte[] { (byte)'\n' }, cancellationToken)
            .ConfigureAwait(false);
        await process.StandardInput.BaseStream
            .FlushAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static async ValueTask<SafeLinuxFd> OpenChildPtySlaveAsync(
        Process process,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var childrenPath = $"/proc/{process.Id}/task/{process.Id}/children";

        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    $"util-linux script exited before exposing its PTY child (exit {process.ExitCode}).");
            }

            try
            {
                var children = (await File.ReadAllTextAsync(childrenPath, timeout.Token).ConfigureAwait(false))
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var child in children)
                {
                    if (!int.TryParse(child, out var childPid) || childPid <= 0)
                    {
                        continue;
                    }

                    var fdPath = $"/proc/{childPid}/fd/0";
                    var target = new FileInfo(fdPath).LinkTarget;
                    if (target is null ||
                        !target.StartsWith("/dev/pts/", StringComparison.Ordinal) ||
                        string.Equals(target, "/dev/pts/ptmx", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    const int ORdWr = 0x0002;
                    const int ONoCtty = 0x0100;
                    const int OCloExec = 0x80000;
                    var fd = open(target, ORdWr | ONoCtty | OCloExec);
                    if (fd < 0)
                    {
                        throw new IOException(
                            $"Failed to open Linux PTY slave {target}; errno {Marshal.GetLastPInvokeError()}.");
                    }

                    return new SafeLinuxFd(fd);
                }
            }
            catch (IOException) when (!process.HasExited)
            {
            }
            catch (UnauthorizedAccessException) when (!process.HasExited)
            {
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token).ConfigureAwait(false);
        }
    }

    private static string FindExecutable(params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            $"Required Linux PTY primitive was not found. Checked: {string.Join(", ", candidates)}.");
    }

    private static string ShellQuote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }

    private sealed class LinuxPtySession : ITerminalSession
    {
        private const ulong Tiocswinsz = 0x5414;
        private readonly Process _process;
        private readonly Stream _input;
        private readonly Stream _output;
        private readonly SafeLinuxFd _slave;
        private readonly Task<string> _stderrDrain;
        private readonly Task _exitMonitor;
        private int _state = (int)TerminalSessionState.Running;
        private int _disposed;

        public LinuxPtySession(
            Guid sessionId,
            Process process,
            SafeLinuxFd slave,
            Task<string> stderrDrain)
        {
            SessionId = sessionId;
            _process = process;
            _input = process.StandardInput.BaseStream;
            _output = process.StandardOutput.BaseStream;
            _slave = slave;
            _stderrDrain = stderrDrain;
            _exitMonitor = MonitorExitAsync();
        }

        public Guid SessionId { get; }
        public ActionBackend Backend => ActionBackend.Wsl;
        public TerminalSessionFeatures Features { get; } = new(
            Interactive: true,
            Resize: true,
            CtrlC: true,
            StreamingOutput: true);
        public TerminalSessionState State => (TerminalSessionState)Volatile.Read(ref _state);

        public async ValueTask WriteAsync(
            ReadOnlyMemory<byte> input,
            CancellationToken cancellationToken = default)
        {
            ThrowIfRunning();
            await _input.WriteAsync(input, cancellationToken).ConfigureAwait(false);
            await _input.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        public ValueTask ResizeAsync(
            TerminalDimensions dimensions,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfRunning();
            var size = new WinSize
            {
                Rows = checked((ushort)dimensions.Rows),
                Columns = checked((ushort)dimensions.Columns)
            };
            if (ioctl(_slave.FileDescriptor, Tiocswinsz, ref size) != 0)
            {
                throw new IOException(
                    $"TIOCSWINSZ failed with errno {Marshal.GetLastPInvokeError()}.");
            }

            return ValueTask.CompletedTask;
        }

        public async ValueTask SignalAsync(
            TerminalSignal signal,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();

            if (signal == TerminalSignal.CtrlC)
            {
                ThrowIfRunning();
                await _input.WriteAsync(new byte[] { 0x03 }, cancellationToken).ConfigureAwait(false);
                await _input.FlushAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            if (State == TerminalSessionState.Running)
            {
                _process.Kill(entireProcessTree: true);
                _ = Interlocked.CompareExchange(
                    ref _state,
                    (int)TerminalSessionState.Cancelled,
                    (int)TerminalSessionState.Running);
            }

            await _exitMonitor.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        public async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadOutputAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            var buffer = new byte[16 * 1024];
            while (true)
            {
                var read = await _output
                    .ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                yield return buffer.AsMemory(0, read).ToArray();
            }

            await _exitMonitor.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            if (State == TerminalSessionState.Running)
            {
                try
                {
                    _process.Kill(entireProcessTree: true);
                    _ = Interlocked.CompareExchange(
                        ref _state,
                        (int)TerminalSessionState.Cancelled,
                        (int)TerminalSessionState.Running);
                }
                catch (InvalidOperationException)
                {
                }
            }

            try
            {
                await _exitMonitor.ConfigureAwait(false);
            }
            finally
            {
                _input.Dispose();
                _output.Dispose();
                _slave.Dispose();
                _ = await _stderrDrain.ConfigureAwait(false);
                _process.Dispose();
                Interlocked.Exchange(ref _state, (int)TerminalSessionState.Closed);
            }
        }

        private async Task MonitorExitAsync()
        {
            try
            {
                await _process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                _ = Interlocked.CompareExchange(
                    ref _state,
                    (int)TerminalSessionState.Exited,
                    (int)TerminalSessionState.Running);
            }
            catch (InvalidOperationException)
            {
                _ = Interlocked.CompareExchange(
                    ref _state,
                    (int)TerminalSessionState.Failed,
                    (int)TerminalSessionState.Running);
            }
        }

        private void ThrowIfRunning()
        {
            ThrowIfDisposed();
            if (State != TerminalSessionState.Running)
            {
                throw new InvalidOperationException($"Terminal session is not running; state is {State}.");
            }
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(LinuxPtySession));
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WinSize
        {
            public ushort Rows;
            public ushort Columns;
            public ushort XPixel;
            public ushort YPixel;
        }
    }

    private sealed class SafeLinuxFd : SafeHandle
    {
        public SafeLinuxFd(int fd)
            : base(new IntPtr(-1), ownsHandle: true)
        {
            SetHandle(new IntPtr(fd));
        }

        public override bool IsInvalid => handle.ToInt64() < 0;
        public int FileDescriptor => checked((int)handle.ToInt64());

        protected override bool ReleaseHandle() => close(FileDescriptor) == 0;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int open(string pathname, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(int fd, ulong request, ref LinuxPtySession.WinSize size);
}
