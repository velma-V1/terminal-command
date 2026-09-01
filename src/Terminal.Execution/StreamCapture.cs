namespace Terminal.Execution;

public static class StreamCapture
{
    public static async ValueTask<ProcessOutput> CaptureAsync(
        Stream stream,
        int maxCaptureBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (maxCaptureBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCaptureBytes));
        }

        var captured = new MemoryStream(capacity: Math.Min(maxCaptureBytes, 64 * 1024));
        var buffer = new byte[16 * 1024];
        long total = 0;

        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
            var remaining = maxCaptureBytes - (int)captured.Length;
            if (remaining > 0)
            {
                captured.Write(buffer, 0, Math.Min(remaining, read));
            }
        }

        return new ProcessOutput(captured.ToArray(), total);
    }
}
