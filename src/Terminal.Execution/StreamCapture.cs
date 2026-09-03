namespace Terminal.Execution;

public static class StreamCapture
{
    public static ValueTask<ProcessOutput> CaptureAsync(
        Stream stream,
        int maxCaptureBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (maxCaptureBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCaptureBytes));
        }

        // Windows anonymous pipes created with CreatePipe are synchronous handles.
        // FileStream.ReadAsync on a synchronous handle consumes ThreadPool workers and can
        // starve concurrent stdout/stderr drains under CI load, allowing a child process
        // to block on a full pipe until its execution timeout. Give blocking streams a
        // dedicated drain thread so bounded capture never becomes process backpressure.
        if (stream is FileStream { IsAsync: false })
        {
            return new ValueTask<ProcessOutput>(Task.Factory.StartNew(
                () => CaptureSynchronously(stream, maxCaptureBytes, cancellationToken),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default));
        }

        return CaptureAsynchronously(stream, maxCaptureBytes, cancellationToken);
    }

    private static ProcessOutput CaptureSynchronously(
        Stream stream,
        int maxCaptureBytes,
        CancellationToken cancellationToken)
    {
        using var captured = new MemoryStream(capacity: Math.Min(maxCaptureBytes, 64 * 1024));
        var buffer = new byte[16 * 1024];
        long total = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = stream.Read(buffer, 0, buffer.Length);
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

    private static async ValueTask<ProcessOutput> CaptureAsynchronously(
        Stream stream,
        int maxCaptureBytes,
        CancellationToken cancellationToken)
    {
        using var captured = new MemoryStream(capacity: Math.Min(maxCaptureBytes, 64 * 1024));
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
