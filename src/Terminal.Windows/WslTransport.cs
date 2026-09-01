using System.Diagnostics;
using Google.Protobuf;
using Terminal.Execution;
using Terminal.Protocol;
using Terminal.Protocol.Messages;

namespace Terminal.Windows;

public sealed record WslTransportOptions
{
    public WslTransportOptions(
        string distro,
        string agentCommand,
        TimeSpan? requestTimeout = null)
    {
        Distro = string.IsNullOrWhiteSpace(distro)
            ? throw new ArgumentException("WSL distro must not be empty.", nameof(distro))
            : distro;
        AgentCommand = string.IsNullOrWhiteSpace(agentCommand)
            ? throw new ArgumentException("Linux agent command must not be empty.", nameof(agentCommand))
            : agentCommand;
        RequestTimeout = requestTimeout ?? TimeSpan.FromSeconds(10);
        if (RequestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        }
    }

    public string Distro { get; }
    public string AgentCommand { get; }
    public TimeSpan RequestTimeout { get; }
}

public interface IWslAgentProcess : IAsyncDisposable
{
    Stream StandardInput { get; }
    Stream StandardOutput { get; }
    Stream StandardError { get; }
    bool HasExited { get; }
    ValueTask TerminateAsync(CancellationToken cancellationToken = default);
}

public interface IWslProcessFactory
{
    ValueTask<IWslAgentProcess> StartAsync(
        WslTransportOptions options,
        CancellationToken cancellationToken = default);
}

public sealed class WindowsWslProcessFactory : IWslProcessFactory
{
    public ValueTask<IWslAgentProcess> StartAsync(
        WslTransportOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("wsl.exe transport requires Windows.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "wsl.exe",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-d");
        startInfo.ArgumentList.Add(options.Distro);
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add(options.AgentCommand);
        startInfo.ArgumentList.Add("--stdio");

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("wsl.exe did not start.");
        return ValueTask.FromResult<IWslAgentProcess>(new WindowsWslAgentProcess(process));
    }

    private sealed class WindowsWslAgentProcess(Process process) : IWslAgentProcess
    {
        private readonly Process _process = process;

        public Stream StandardInput => _process.StandardInput.BaseStream;
        public Stream StandardOutput => _process.StandardOutput.BaseStream;
        public Stream StandardError => _process.StandardError.BaseStream;
        public bool HasExited => _process.HasExited;

        public async ValueTask TerminateAsync(CancellationToken cancellationToken = default)
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }

            await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }

        public ValueTask DisposeAsync()
        {
            _process.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

public sealed class WslTransport : IAsyncDisposable
{
    private readonly IWslAgentProcess _process;
    private readonly WslTransportOptions _options;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly Task<ProcessOutput> _stderrDrain;
    private bool _disposed;

    private WslTransport(IWslAgentProcess process, WslTransportOptions options)
    {
        _process = process;
        _options = options;
        _stderrDrain = StreamCapture.CaptureAsync(
            process.StandardError,
            64 * 1024,
            CancellationToken.None).AsTask();
    }

    public bool IsAvailable { get; private set; }
    public ProcessOutput? Diagnostics { get; private set; }

    public static async ValueTask<WslTransport> ConnectAsync(
        WslTransportOptions options,
        IWslProcessFactory factory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(factory);
        var process = await factory.StartAsync(options, cancellationToken).ConfigureAwait(false);
        var transport = new WslTransport(process, options);
        try
        {
            var hello = await transport.RequestAsync(
                ProtocolMessageType.Hello,
                new HelloRequest
                {
                    ProtocolMajor = ProtocolVersion.Current.Major,
                    ProtocolMinor = ProtocolVersion.Current.Minor,
                    Client = "terminal-windows"
                },
                HelloResponse.Parser,
                cancellationToken).ConfigureAwait(false);

            if (hello.ProtocolMajor != ProtocolVersion.Current.Major)
            {
                throw new InvalidDataException($"Linux agent protocol major version {hello.ProtocolMajor} is incompatible.");
            }

            if (!hello.Ready)
            {
                throw new InvalidDataException("Linux agent completed the handshake but is not ready.");
            }

            transport.IsAvailable = true;
            return transport;
        }
        catch
        {
            await transport.FailClosedAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask<HealthResponse> HealthAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!IsAvailable)
        {
            throw new InvalidOperationException("WSL transport is unavailable.");
        }

        try
        {
            var response = await RequestAsync(
                ProtocolMessageType.Health,
                new HealthRequest(),
                HealthResponse.Parser,
                cancellationToken).ConfigureAwait(false);
            if (!response.Healthy)
            {
                IsAvailable = false;
            }

            return response;
        }
        catch
        {
            IsAvailable = false;
            throw;
        }
    }

    private async ValueTask<TResponse> RequestAsync<TResponse>(
        ProtocolMessageType type,
        IMessage request,
        MessageParser<TResponse> parser,
        CancellationToken cancellationToken)
        where TResponse : class, IMessage<TResponse>
    {
        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.RequestTimeout);
            var payload = request.ToByteArray();
            var requestId = Guid.NewGuid();
            await FrameStream.WriteAsync(
                _process.StandardInput,
                new ProtocolFrame(
                    new FrameHeader(ProtocolVersion.Current, type, requestId, payload.Length),
                    payload),
                timeout.Token).ConfigureAwait(false);

            var response = await FrameStream.ReadAsync(_process.StandardOutput, timeout.Token).ConfigureAwait(false)
                ?? throw new EndOfStreamException("Linux agent closed the protocol stream without a response.");
            if (response.Header.MessageType == ProtocolMessageType.Error)
            {
                var error = ErrorResponse.Parser.ParseFrom(response.Payload.Span);
                throw new InvalidDataException($"Linux agent error {error.Code}: {error.Message}");
            }

            if (response.Header.MessageType != type)
            {
                throw new InvalidDataException(
                    $"Expected {type} response but received {response.Header.MessageType}.");
            }

            return parser.ParseFrom(response.Payload.Span);
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private async ValueTask FailClosedAsync()
    {
        IsAvailable = false;
        try
        {
            await _process.TerminateAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            Diagnostics = await DrainDiagnosticsAsync().ConfigureAwait(false);
            await _process.DisposeAsync().ConfigureAwait(false);
            _disposed = true;
        }
    }

    private async ValueTask<ProcessOutput?> DrainDiagnosticsAsync()
    {
        try
        {
            return await _stderrDrain.ConfigureAwait(false);
        }
        catch (IOException)
        {
            return null;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        IsAvailable = false;
        try
        {
            if (!_process.HasExited)
            {
                await _process.TerminateAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            Diagnostics = await DrainDiagnosticsAsync().ConfigureAwait(false);
            await _process.DisposeAsync().ConfigureAwait(false);
            _requestGate.Dispose();
            _disposed = true;
        }
    }
}
