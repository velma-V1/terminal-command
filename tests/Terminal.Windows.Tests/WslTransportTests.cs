using System.Buffers.Binary;
using Google.Protobuf;
using Terminal.Protocol;
using Terminal.Protocol.Messages;
using Terminal.Windows;

namespace Terminal.Windows.Tests;

public sealed class WslTransportTests
{
    [Fact]
    public async Task Connect_health_and_heartbeat_use_one_correlated_persistent_stdio_process()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var process = new ReactiveWslProcess(request => request.Header.MessageType switch
        {
            ProtocolMessageType.Hello => Encode(
                ProtocolMessageType.Hello,
                request.Header.RequestId,
                new HelloResponse
                {
                    ProtocolMajor = ProtocolVersion.Current.Major,
                    ProtocolMinor = ProtocolVersion.Current.Minor,
                    Agent = "terminal-linux-agent",
                    Ready = true
                }),
            ProtocolMessageType.Health => Encode(
                ProtocolMessageType.Health,
                request.Header.RequestId,
                new HealthResponse { Healthy = true, Status = "ready" }),
            ProtocolMessageType.Heartbeat => Encode(
                ProtocolMessageType.Heartbeat,
                request.Header.RequestId,
                new HealthResponse { Healthy = true, Status = "alive" }),
            _ => throw new InvalidOperationException()
        });
        var factory = new FakeFactory(process);

        await using var transport = await WslTransport.ConnectAsync(
            new WslTransportOptions("Ubuntu", "terminal-linux-agent"),
            factory,
            cancellationToken);
        var health = await transport.HealthAsync(cancellationToken);
        var heartbeat = await transport.HeartbeatAsync(cancellationToken);

        Assert.True(health.Healthy);
        Assert.True(heartbeat.Healthy);
        Assert.True(transport.IsAvailable);
        Assert.Equal(1, factory.Starts);
        Assert.False(process.Terminated);
        Assert.Equal(
            [ProtocolMessageType.Hello, ProtocolMessageType.Health, ProtocolMessageType.Heartbeat],
            process.Requests.Select(static request => request.Header.MessageType));
    }

    [Fact]
    public async Task Response_with_wrong_request_id_fails_closed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var process = new ReactiveWslProcess(request => Encode(
            request.Header.MessageType,
            Guid.NewGuid(),
            new HelloResponse
            {
                ProtocolMajor = ProtocolVersion.Current.Major,
                ProtocolMinor = ProtocolVersion.Current.Minor,
                Agent = "stale",
                Ready = true
            }));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => WslTransport.ConnectAsync(
                new WslTransportOptions("Ubuntu", "terminal-linux-agent"),
                new FakeFactory(process),
                cancellationToken).AsTask());

        Assert.True(process.Terminated);
    }

    [Fact]
    public async Task Failed_heartbeat_marks_transport_unavailable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var process = new ReactiveWslProcess(request => request.Header.MessageType switch
        {
            ProtocolMessageType.Hello => Encode(
                ProtocolMessageType.Hello,
                request.Header.RequestId,
                new HelloResponse
                {
                    ProtocolMajor = ProtocolVersion.Current.Major,
                    ProtocolMinor = ProtocolVersion.Current.Minor,
                    Agent = "terminal-linux-agent",
                    Ready = true
                }),
            ProtocolMessageType.Heartbeat => Encode(
                ProtocolMessageType.Heartbeat,
                request.Header.RequestId,
                new HealthResponse { Healthy = false, Status = "degraded" }),
            _ => throw new InvalidOperationException()
        });

        await using var transport = await WslTransport.ConnectAsync(
            new WslTransportOptions("Ubuntu", "terminal-linux-agent"),
            new FakeFactory(process),
            cancellationToken);
        var heartbeat = await transport.HeartbeatAsync(cancellationToken);

        Assert.False(heartbeat.Healthy);
        Assert.False(transport.IsAvailable);
    }

    [Fact]
    public async Task Protocol_major_mismatch_fails_closed_and_terminates_child()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var process = new ReactiveWslProcess(request => Encode(
            ProtocolMessageType.Hello,
            request.Header.RequestId,
            new HelloResponse
            {
                ProtocolMajor = (uint)ProtocolVersion.Current.Major + 1,
                ProtocolMinor = 0,
                Agent = "wrong",
                Ready = true
            }));

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
        var process = new ReactiveWslProcess(_ => new byte[32]);

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

    private sealed class FakeFactory(ReactiveWslProcess process) : IWslProcessFactory
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

    private sealed class ReactiveWslProcess : IWslAgentProcess
    {
        private readonly Func<ProtocolFrame, byte[]> _respond;
        private readonly MemoryStream _input = new();
        private readonly ReactiveResponseStream _output;

        public ReactiveWslProcess(Func<ProtocolFrame, byte[]> respond)
        {
            _respond = respond;
            _output = new ReactiveResponseStream(this);
            StandardError = new MemoryStream("diagnostic only"u8.ToArray());
        }

        public List<ProtocolFrame> Requests { get; } = [];
        public Stream StandardInput => _input;
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
            _input.Dispose();
            _output.Dispose();
            StandardError.Dispose();
            return ValueTask.CompletedTask;
        }

        private byte[] NextResponse(ref int consumedInputBytes)
        {
            var bytes = _input.ToArray();
            if (bytes.Length - consumedInputBytes < 32)
            {
                throw new EndOfStreamException("No complete request frame is available.");
            }

            var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(consumedInputBytes + 28, 4));
            var length = 32 + payloadLength;
            if (payloadLength < 0 || bytes.Length - consumedInputBytes < length)
            {
                throw new EndOfStreamException("No complete request frame is available.");
            }

            var request = FrameCodec.Decode(bytes.AsSpan(consumedInputBytes, length));
            consumedInputBytes += length;
            Requests.Add(request);
            return _respond(request);
        }

        private sealed class ReactiveResponseStream(ReactiveWslProcess owner) : Stream
        {
            private int _consumedInputBytes;
            private byte[]? _response;
            private int _responseOffset;

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count)
                => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

            public override ValueTask<int> ReadAsync(
                Memory<byte> buffer,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_response is null || _responseOffset >= _response.Length)
                {
                    _response = owner.NextResponse(ref _consumedInputBytes);
                    _responseOffset = 0;
                }

                var count = Math.Min(buffer.Length, _response.Length - _responseOffset);
                _response.AsMemory(_responseOffset, count).CopyTo(buffer);
                _responseOffset += count;
                return ValueTask.FromResult(count);
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
