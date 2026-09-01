using System.Buffers.Binary;

namespace Terminal.Protocol;

public static class FrameStream
{
    private const int HeaderBytes = 32;

    public static async ValueTask WriteAsync(
        Stream stream,
        ProtocolFrame frame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(frame);
        var encoded = FrameCodec.Encode(frame.Header, frame.Payload.Span);
        await stream.WriteAsync(encoded, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask<ProtocolFrame?> ReadAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var header = new byte[HeaderBytes];
        var first = await stream.ReadAsync(header.AsMemory(0, HeaderBytes), cancellationToken).ConfigureAwait(false);
        if (first == 0)
        {
            return null;
        }

        var offset = first;
        while (offset < HeaderBytes)
        {
            var read = await stream.ReadAsync(header.AsMemory(offset, HeaderBytes - offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("Protocol frame ended inside its fixed header.");
            }

            offset += read;
        }

        var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(28, 4));
        if (payloadLength < 0 || payloadLength > FrameCodec.MaxPayloadBytes)
        {
            throw new InvalidDataException("Payload length is outside the permitted bounds.");
        }

        var frameBytes = new byte[HeaderBytes + payloadLength];
        header.CopyTo(frameBytes, 0);
        offset = 0;
        while (offset < payloadLength)
        {
            var read = await stream.ReadAsync(
                frameBytes.AsMemory(HeaderBytes + offset, payloadLength - offset),
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("Protocol frame ended inside its payload.");
            }

            offset += read;
        }

        return FrameCodec.Decode(frameBytes);
    }
}
