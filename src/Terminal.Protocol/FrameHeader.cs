namespace Terminal.Protocol;

public readonly record struct FrameHeader(
    ProtocolVersion Version,
    ProtocolMessageType MessageType,
    Guid RequestId,
    int PayloadLength);

public sealed record ProtocolFrame(FrameHeader Header, byte[] Payload);
