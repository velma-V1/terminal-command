using Google.Protobuf;
using Terminal.Protocol;
using Terminal.Protocol.Messages;

namespace Terminal.LinuxAgent;

public sealed class LinuxAgentProtocolHandler
{
    public ValueTask<ProtocolFrame> HandleAsync(
        ProtocolFrame request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return ValueTask.FromResult(request.Header.MessageType switch
            {
                ProtocolMessageType.Hello => Hello(request),
                ProtocolMessageType.Health => Health(request),
                ProtocolMessageType.Cancel => Cancel(request),
                ProtocolMessageType.ActionPrepare or ProtocolMessageType.ActionExecute => Error(
                    request.Header.RequestId,
                    "execution.disabled",
                    "The bootstrap Linux agent does not accept Action execution yet."),
                _ => Error(
                    request.Header.RequestId,
                    "message.unsupported",
                    $"Message type {request.Header.MessageType} is not supported by the bootstrap agent.")
            });
        }
        catch (InvalidProtocolBufferException exception)
        {
            return ValueTask.FromResult(Error(
                request.Header.RequestId,
                "payload.invalid",
                exception.Message));
        }
    }

    private static ProtocolFrame Hello(ProtocolFrame request)
    {
        var hello = HelloRequest.Parser.ParseFrom(request.Payload.Span);
        if (hello.ProtocolMajor != ProtocolVersion.Current.Major)
        {
            return Error(
                request.Header.RequestId,
                "protocol.major_mismatch",
                $"Unsupported protocol major version {hello.ProtocolMajor}.");
        }

        return Encode(
            ProtocolMessageType.Hello,
            request.Header.RequestId,
            new HelloResponse
            {
                ProtocolMajor = ProtocolVersion.Current.Major,
                ProtocolMinor = ProtocolVersion.Current.Minor,
                Agent = "terminal-linux-agent",
                Ready = true
            });
    }

    private static ProtocolFrame Health(ProtocolFrame request)
    {
        _ = HealthRequest.Parser.ParseFrom(request.Payload.Span);
        return Encode(
            ProtocolMessageType.Health,
            request.Header.RequestId,
            new HealthResponse { Healthy = true, Status = "ready" });
    }

    private static ProtocolFrame Cancel(ProtocolFrame request)
    {
        _ = CancelRequest.Parser.ParseFrom(request.Payload.Span);
        return Encode(
            ProtocolMessageType.Cancel,
            request.Header.RequestId,
            new CancelResponse
            {
                Accepted = false,
                Reason = "No remotely executable Action is active in the bootstrap agent."
            });
    }

    private static ProtocolFrame Error(Guid requestId, string code, string message)
        => Encode(
            ProtocolMessageType.Error,
            requestId,
            new ErrorResponse { Code = code, Message = message });

    private static ProtocolFrame Encode(
        ProtocolMessageType type,
        Guid requestId,
        IMessage message)
    {
        var payload = message.ToByteArray();
        return new ProtocolFrame(
            new FrameHeader(ProtocolVersion.Current, type, requestId, payload.Length),
            payload);
    }
}

public sealed class StdioAgentHost(
    Stream input,
    Stream output,
    LinuxAgentProtocolHandler handler)
{
    private readonly Stream _input = input ?? throw new ArgumentNullException(nameof(input));
    private readonly Stream _output = output ?? throw new ArgumentNullException(nameof(output));
    private readonly LinuxAgentProtocolHandler _handler = handler ?? throw new ArgumentNullException(nameof(handler));

    public async ValueTask RunAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var request = await FrameStream.ReadAsync(_input, cancellationToken).ConfigureAwait(false);
            if (request is null)
            {
                return;
            }

            var response = await _handler.HandleAsync(request, cancellationToken).ConfigureAwait(false);
            await FrameStream.WriteAsync(_output, response, cancellationToken).ConfigureAwait(false);
        }
    }
}
