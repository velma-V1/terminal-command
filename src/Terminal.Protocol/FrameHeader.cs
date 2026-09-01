namespace Terminal.Protocol;

public readonly record struct FrameHeader(
    ProtocolVersion Version,
    ProtocolMessageType MessageType,
    Guid RequestId,
    int PayloadLength);

public sealed class ProtocolFrame
{
    private readonly byte[] _payload;

    public ProtocolFrame(FrameHeader header, ReadOnlySpan<byte> payload)
    {
        Header = header;
        _payload = payload.ToArray();
    }

    public FrameHeader Header { get; }
    public ReadOnlyMemory<byte> Payload => _payload;
}
