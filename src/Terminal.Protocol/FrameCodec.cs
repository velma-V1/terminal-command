using System.Buffers.Binary;

namespace Terminal.Protocol;

public static class FrameCodec
{
    private const uint Magic = 0x334D5254; // ASCII "TRM3" in little-endian order.
    private const int HeaderBytes = 32;
    public const int MaxPayloadBytes = 8 * 1024 * 1024;

    public static byte[] Encode(FrameHeader header, ReadOnlySpan<byte> payload)
    {
        ValidatePayloadLength(header.PayloadLength, payload.Length);

        if (!Enum.IsDefined(header.MessageType))
        {
            throw new InvalidDataException("Unknown protocol message type.");
        }

        var buffer = new byte[HeaderBytes + payload.Length];
        var span = buffer.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(span[0..4], Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..6], header.Version.Major);
        BinaryPrimitives.WriteUInt16LittleEndian(span[6..8], header.Version.Minor);
        BinaryPrimitives.WriteUInt16LittleEndian(span[8..10], (ushort)header.MessageType);
        BinaryPrimitives.WriteUInt16LittleEndian(span[10..12], 0);

        if (!header.RequestId.TryWriteBytes(span[12..28]))
        {
            throw new InvalidOperationException("Could not encode request identifier.");
        }

        BinaryPrimitives.WriteInt32LittleEndian(span[28..32], header.PayloadLength);
        payload.CopyTo(span[HeaderBytes..]);
        return buffer;
    }

    public static ProtocolFrame Decode(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < HeaderBytes)
        {
            throw new InvalidDataException("Frame is shorter than the fixed header.");
        }

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(frame[0..4]);
        if (magic != Magic)
        {
            throw new InvalidDataException("Invalid frame magic.");
        }

        var version = new ProtocolVersion(
            BinaryPrimitives.ReadUInt16LittleEndian(frame[4..6]),
            BinaryPrimitives.ReadUInt16LittleEndian(frame[6..8]));

        if (version.Major != ProtocolVersion.Current.Major)
        {
            throw new InvalidDataException($"Unsupported protocol major version {version.Major}.");
        }

        var messageType = (ProtocolMessageType)BinaryPrimitives.ReadUInt16LittleEndian(frame[8..10]);
        if (!Enum.IsDefined(messageType))
        {
            throw new InvalidDataException("Unknown protocol message type.");
        }

        var reservedFlags = BinaryPrimitives.ReadUInt16LittleEndian(frame[10..12]);
        if (reservedFlags != 0)
        {
            throw new InvalidDataException("Reserved protocol flags must be zero.");
        }

        var requestId = new Guid(frame[12..28]);
        var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(frame[28..32]);

        if (payloadLength < 0 || payloadLength > MaxPayloadBytes)
        {
            throw new InvalidDataException("Payload length is outside the permitted bounds.");
        }

        if (frame.Length != HeaderBytes + payloadLength)
        {
            throw new InvalidDataException("Frame length does not match its declared payload length.");
        }

        var header = new FrameHeader(version, messageType, requestId, payloadLength);
        return new ProtocolFrame(header, frame[HeaderBytes..]);
    }

    private static void ValidatePayloadLength(int declaredLength, int actualLength)
    {
        if (declaredLength < 0 || declaredLength > MaxPayloadBytes)
        {
            throw new InvalidDataException("Payload length is outside the permitted bounds.");
        }

        if (declaredLength != actualLength)
        {
            throw new InvalidDataException("Declared payload length does not match actual payload length.");
        }
    }
}
