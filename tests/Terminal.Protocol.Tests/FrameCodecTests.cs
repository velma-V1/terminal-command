using Terminal.Protocol;

namespace Terminal.Protocol.Tests;

public sealed class FrameCodecTests
{
    [Fact]
    public void Frame_round_trips_without_changing_identity_or_payload()
    {
        var header = new FrameHeader(ProtocolVersion.Current, ProtocolMessageType.Health, Guid.NewGuid(), 3);
        byte[] payload = [1, 2, 3];

        var encoded = FrameCodec.Encode(header, payload);
        var decoded = FrameCodec.Decode(encoded);

        Assert.Equal(header.Version, decoded.Header.Version);
        Assert.Equal(header.MessageType, decoded.Header.MessageType);
        Assert.Equal(header.RequestId, decoded.Header.RequestId);
        Assert.Equal(payload, decoded.Payload.ToArray());
    }

    [Fact]
    public void Truncated_frame_is_rejected()
    {
        var header = new FrameHeader(ProtocolVersion.Current, ProtocolMessageType.Hello, Guid.NewGuid(), 4);
        var encoded = FrameCodec.Encode(header, [1, 2, 3, 4]);

        Assert.Throws<InvalidDataException>(() => FrameCodec.Decode(encoded.AsSpan(0, encoded.Length - 1)));
    }

    [Fact]
    public void Payload_larger_than_limit_is_rejected_before_allocation()
    {
        var header = new FrameHeader(ProtocolVersion.Current, ProtocolMessageType.Health, Guid.NewGuid(), FrameCodec.MaxPayloadBytes + 1);

        Assert.Throws<InvalidDataException>(() => FrameCodec.Encode(header, new byte[1]));
    }

    [Fact]
    public void Incompatible_protocol_major_is_rejected()
    {
        var version = new ProtocolVersion((ushort)(ProtocolVersion.Current.Major + 1), 0);
        var header = new FrameHeader(version, ProtocolMessageType.Health, Guid.NewGuid(), 0);
        var encoded = FrameCodec.Encode(header, []);

        Assert.Throws<InvalidDataException>(() => FrameCodec.Decode(encoded));
    }

    [Fact]
    public void Header_payload_length_must_match_actual_payload()
    {
        var header = new FrameHeader(ProtocolVersion.Current, ProtocolMessageType.Health, Guid.NewGuid(), 5);

        Assert.Throws<InvalidDataException>(() => FrameCodec.Encode(header, [1, 2]));
    }

    [Fact]
    public void Nonzero_reserved_flags_are_rejected()
    {
        var header = new FrameHeader(ProtocolVersion.Current, ProtocolMessageType.Health, Guid.NewGuid(), 0);
        var encoded = FrameCodec.Encode(header, []);
        encoded[10] = 1;

        Assert.Throws<InvalidDataException>(() => FrameCodec.Decode(encoded));
    }

    [Fact]
    public void Unknown_message_type_is_rejected()
    {
        var header = new FrameHeader(ProtocolVersion.Current, ProtocolMessageType.Health, Guid.NewGuid(), 0);
        var encoded = FrameCodec.Encode(header, []);
        encoded[8] = 0xFF;
        encoded[9] = 0x7F;

        Assert.Throws<InvalidDataException>(() => FrameCodec.Decode(encoded));
    }
}
