using Google.Protobuf;
using Terminal.LinuxAgent;
using Terminal.Protocol;
using Terminal.Protocol.Messages;

namespace Terminal.LinuxAgent.Tests;

public sealed class LinuxAgentProtocolTests
{
    [Fact]
    public async Task Hello_and_health_are_supported_but_action_execution_is_disabled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var handler = new LinuxAgentProtocolHandler();

        var hello = await handler.HandleAsync(Frame(
            ProtocolMessageType.Hello,
            new HelloRequest
            {
                ProtocolMajor = ProtocolVersion.Current.Major,
                ProtocolMinor = ProtocolVersion.Current.Minor,
                Client = "terminal-windows"
            }), cancellationToken);
        var helloResponse = HelloResponse.Parser.ParseFrom(hello.Payload.Span);

        Assert.Equal(ProtocolMessageType.Hello, hello.Header.MessageType);
        Assert.True(helloResponse.Ready);
        Assert.Equal(ProtocolVersion.Current.Major, helloResponse.ProtocolMajor);

        var health = await handler.HandleAsync(
            Frame(ProtocolMessageType.Health, new HealthRequest()),
            cancellationToken);
        Assert.True(HealthResponse.Parser.ParseFrom(health.Payload.Span).Healthy);

        var denied = await handler.HandleAsync(
            new ProtocolFrame(
                new FrameHeader(ProtocolVersion.Current, ProtocolMessageType.ActionExecute, Guid.NewGuid(), 0),
                ReadOnlySpan<byte>.Empty),
            cancellationToken);
        var error = ErrorResponse.Parser.ParseFrom(denied.Payload.Span);

        Assert.Equal(ProtocolMessageType.Error, denied.Header.MessageType);
        Assert.Equal("execution.disabled", error.Code);
    }

    [Fact]
    public async Task Stdio_host_processes_multiple_frames_until_clean_eof()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var input = new MemoryStream();
        await FrameStream.WriteAsync(input, Frame(
            ProtocolMessageType.Hello,
            new HelloRequest
            {
                ProtocolMajor = ProtocolVersion.Current.Major,
                ProtocolMinor = ProtocolVersion.Current.Minor,
                Client = "test"
            }), cancellationToken);
        await FrameStream.WriteAsync(input, Frame(ProtocolMessageType.Health, new HealthRequest()), cancellationToken);
        input.Position = 0;
        await using var output = new MemoryStream();
        var host = new StdioAgentHost(input, output, new LinuxAgentProtocolHandler());

        await host.RunAsync(cancellationToken);
        output.Position = 0;

        Assert.Equal(ProtocolMessageType.Hello, (await FrameStream.ReadAsync(output, cancellationToken))!.Header.MessageType);
        Assert.Equal(ProtocolMessageType.Health, (await FrameStream.ReadAsync(output, cancellationToken))!.Header.MessageType);
        Assert.Null(await FrameStream.ReadAsync(output, cancellationToken));
    }

    private static ProtocolFrame Frame(ProtocolMessageType type, IMessage message)
    {
        var payload = message.ToByteArray();
        return new ProtocolFrame(
            new FrameHeader(ProtocolVersion.Current, type, Guid.NewGuid(), payload.Length),
            payload);
    }
}
