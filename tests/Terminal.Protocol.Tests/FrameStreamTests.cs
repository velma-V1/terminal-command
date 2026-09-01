using System.Buffers.Binary;
using Terminal.Protocol;

namespace Terminal.Protocol.Tests;

public sealed class FrameStreamTests
{
    [Fact]
    public async Task Round_trip_reads_exactly_one_bounded_frame()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var stream = new MemoryStream();
        var requestId = Guid.NewGuid();
        var payload = "hello"u8.ToArray();
        var frame = new ProtocolFrame(
            new FrameHeader(ProtocolVersion.Current, ProtocolMessageType.Health, requestId, payload.Length),
            payload);

        await FrameStream.WriteAsync(stream, frame, cancellationToken);
        stream.Position = 0;
        var decoded = await FrameStream.ReadAsync(stream, cancellationToken);

        Assert.NotNull(decoded);
        Assert.Equal(requestId, decoded.Header.RequestId);
        Assert.Equal(payload, decoded.Payload.ToArray());
        Assert.Null(await FrameStream.ReadAsync(stream, cancellationToken));
    }

    [Fact]
    public async Task Truncated_header_is_rejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var stream = new MemoryStream(new byte[12]);

        await Assert.ThrowsAsync<EndOfStreamException>(
            () => FrameStream.ReadAsync(stream, cancellationToken).AsTask());
    }

    [Fact]
    public async Task Oversized_declared_payload_is_rejected_before_payload_read()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var header = new byte[32];
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), 0x334D5254);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4, 2), ProtocolVersion.Current.Major);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6, 2), ProtocolVersion.Current.Minor);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8, 2), (ushort)ProtocolMessageType.Health);
        Guid.NewGuid().TryWriteBytes(header.AsSpan(12, 16));
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(28, 4), FrameCodec.MaxPayloadBytes + 1);
        await using var stream = new MemoryStream(header);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => FrameStream.ReadAsync(stream, cancellationToken).AsTask());
    }
}
