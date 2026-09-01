using Google.Protobuf;
using Terminal.Protocol;
using Terminal.Protocol.Messages;
using Terminal.Windows;

namespace Terminal.Windows.Tests;

public sealed class WslTransportTests
{
    [Fact]
    public async Task Connect_and_health_use_one_persistent_stdio_process()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var hello = Encode(
            ProtocolMessageType.Hello,
            Guid.NewGuid(),
            new HelloResponse
            {
                ProtocolMajor = ProtocolVersion.Current.Major,
                ProtocolMinor = ProtocolVersion.Current.Minor,
                Agent = "terminal-linux-agent",
                Ready = true
            });
        var health = Encode(
            ProtocolMessageType.Health,
            Guid.NewGuid(),
            new HealthResponse { Healthy = true, Status = "ready" });
        var process = new ScriptedWslProcess([hello, health]);
        var factory = new FakeFactory(process);

        await using var transport = await WslTransport.ConnectAsync(
            new WslTransportOptions("Ubuntu", "terminal-linux-agent"),
            factory,
            cancellationToken);
        var response = await transport.HealthAsync(cancellationToken);

        Assert.True(response.Healthy);
        Assert.True(transport.IsAvailable);
        Assert.Equal(1, factory.Starts);
        Assert.False(process.Terminated);

        process.Input.Position = 0;
        var first = await FrameStream.ReadAsync(process.Input, cancellationToken);
        var second = await FrameStream.ReadAsync(process.Input, cancellationToken);
        Assert.Equal(ProtocolMessageType.Hello, first!.Header.MessageType);
        Assert.Equal(ProtocolMessageType.Health, second!.Header.MessageType);
    }

    [Fact]
    public async Task Protocol_major_mismatch_fails_closed_and_terminates_child()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var response = Encode(
            ProtocolMessageType.Hello,
            Guid.NewGuid(),
            new HelloResponse
            {
                ProtocolMajor = (uint)ProtocolVersion.Current.Major + 1,
                ProtocolMinor = 0,
                Agent = "wrong",
                Ready = true
            });
        var process = new ScriptedWslProcess([response]);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => WslTransport.ConnectAsync(
                new WslTransportOptions("Ubuntu", "terminal-linux-agent"),
                new FakeFactory(process),
                cancellationToken).AsTask());

        Assert.True(process.Terminated);
    }

    [Fact]
    public async Task Malformed_agent_frame_fails_closed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var process = new ScriptedWslProcess([new byte[32]]);

        await Assert.ThrowsAnyAsync<Exception>(
            () => WslTransport.ConnectAsync(
                new WslTransportOptions("Ubuntu", "terminal-linux-agent"),
                new FakeFactory(process),
                cancellationToken).AsTask());

        Assert.True(process.Terminated);
    }

    private static byte[] Encode(ProtocolMessageType type, Guid requestId, IMessage message)
    {
        var payload = message.ToByteArray();
        return FrameCodec.Encode(
            new FrameHeader(ProtocolVersion.Current, type, requestId, payload.Length),
            payload);
    }

    private sealed class FakeFactory(ScriptedWslProcess process) : IWslProcessFactory
    {
        public int Starts { get; private set; }

        public ValueTask<IWslAgentProcess> StartAsync(
            WslTransportOptions options,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Starts++;
            return ValueTask.FromResult<IWslAgentProcess>(process);
        }
    }

    private sealed class ScriptedWslProcess : IWslAgentProcess
    {
        private readonly MemoryStream _output;

        public ScriptedWslProcess(IEnumerable<byte[]> frames)
        {
            Input = new MemoryStream();
            _output = new MemoryStream(frames.SelectMany(static frame => frame).ToArray());
            StandardError = new MemoryStream("diagnostic only"u8.ToArray());
        }

        public MemoryStream Input { get; }
        public Stream StandardInput => Input;
        public Stream StandardOutput => _output;
        public Stream StandardError { get; }
        public bool HasExited => Terminated;
        public bool Terminated { get; private set; }

        public ValueTask TerminateAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Terminated = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Input.Dispose();
            _output.Dispose();
            StandardError.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
