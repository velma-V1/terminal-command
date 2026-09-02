using System.Net;
using System.Text;
using System.Text.Json;
using Terminal.Core.Intelligence;
using Terminal.Intelligence;

namespace Terminal.Intelligence.Tests;

public sealed class QwenLocalProviderTests
{
    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("http://192.168.1.20:1234/")]
    public void Provider_rejects_non_loopback_model_endpoints(string endpoint)
    {
        using var client = new HttpClient(new StubHandler(_ => Response(HttpStatusCode.OK, ValidResponse())));

        Assert.Throws<ArgumentException>(() => new QwenOpenAiCompatibleProvider(
            client,
            new QwenLocalProviderOptions(new Uri(endpoint), "qwen3.5-9b")));
    }

    [Fact]
    public async Task Valid_response_maps_to_zero_authority_candidate_and_uses_configured_runtime_model()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        string? requestBody = null;
        using var client = new HttpClient(new StubHandler(async request =>
        {
            requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return Response(HttpStatusCode.OK, ValidResponse());
        }));
        var provider = Provider(client);

        var result = await provider.CompleteAsync(Request(), cancellationToken);

        Assert.False(result.Authoritative);
        Assert.Equal(ModelOutputClass.UntrustedCandidate, result.OutputClass);
        Assert.Equal("qwen3.5-9b", result.ProviderId);
        Assert.Equal("candidate-42", result.ResponseId);
        Assert.Equal("dependency mismatch", result.Summary);
        Assert.Equal(["pin package A"], result.ProposedSteps);
        Assert.NotNull(requestBody);
        using var json = JsonDocument.Parse(requestBody);
        Assert.Equal("Qwen3.5-9B-Q8_0.gguf", json.RootElement.GetProperty("model").GetString());
        Assert.Equal(512, json.RootElement.GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    public async Task Model_request_context_is_redacted_before_crossing_model_boundary()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        string? requestBody = null;
        using var client = new HttpClient(new StubHandler(async request =>
        {
            requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return Response(HttpStatusCode.OK, ValidResponse());
        }));
        var provider = Provider(client);
        var request = new ModelRequest(
            Guid.NewGuid(),
            "recovery",
            "failure password=hunter2",
            ["Authorization: Bearer abc.def.ghi", "api_key=supersecret", "safe context"],
            256);

        await provider.CompleteAsync(request, cancellationToken);

        Assert.NotNull(requestBody);
        Assert.DoesNotContain("hunter2", requestBody, StringComparison.Ordinal);
        Assert.DoesNotContain("abc.def.ghi", requestBody, StringComparison.Ordinal);
        Assert.DoesNotContain("supersecret", requestBody, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", requestBody, StringComparison.Ordinal);
        Assert.Contains("safe context", requestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Oversized_context_is_bounded_before_request_is_sent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        int bodyBytes = 0;
        using var client = new HttpClient(new StubHandler(async request =>
        {
            var body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            bodyBytes = body.Length;
            return Response(HttpStatusCode.OK, ValidResponse());
        }));
        var provider = new QwenOpenAiCompatibleProvider(
            client,
            new QwenLocalProviderOptions(
                new Uri("http://127.0.0.1:1234/"),
                "Qwen3.5-9B-Q8_0.gguf",
                MaxInputBytes: 16 * 1024,
                MaxResponseBytes: 64 * 1024,
                MaxOutputTokens: 1024));
        var request = new ModelRequest(
            Guid.NewGuid(),
            "recovery",
            "failure",
            [new string('x', 100_000)],
            512);

        await provider.CompleteAsync(request, cancellationToken);

        Assert.InRange(bodyBytes, 1, 24 * 1024);
    }

    [Fact]
    public async Task Markdown_or_malformed_candidate_json_fails_closed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = new HttpClient(new StubHandler(_ => Response(
            HttpStatusCode.OK,
            OpenAiResponse("```json\n{\"summary\":\"guess\"}\n```"))));
        var provider = Provider(client);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => provider.CompleteAsync(Request(), cancellationToken).AsTask());
    }

    [Fact]
    public async Task Missing_required_candidate_fields_fails_closed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = new HttpClient(new StubHandler(_ => Response(
            HttpStatusCode.OK,
            OpenAiResponse("{\"responseId\":\"x\",\"summary\":\"guess\",\"evidence\":[]}"))));
        var provider = Provider(client);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => provider.CompleteAsync(Request(), cancellationToken).AsTask());
    }

    [Fact]
    public async Task Non_success_http_status_is_not_converted_into_model_advice()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = new HttpClient(new StubHandler(_ => Response(HttpStatusCode.ServiceUnavailable, "offline")));
        var provider = Provider(client);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => provider.CompleteAsync(Request(), cancellationToken).AsTask());
    }

    [Fact]
    public async Task Oversized_model_response_is_rejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var huge = OpenAiResponse(new string('x', 70_000));
        using var client = new HttpClient(new StubHandler(_ => Response(HttpStatusCode.OK, huge)));
        var provider = new QwenOpenAiCompatibleProvider(
            client,
            new QwenLocalProviderOptions(
                new Uri("http://localhost:1234/"),
                "Qwen3.5-9B-Q8_0.gguf",
                MaxInputBytes: 16 * 1024,
                MaxResponseBytes: 8 * 1024,
                MaxOutputTokens: 1024));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => provider.CompleteAsync(Request(), cancellationToken).AsTask());
    }

    private static QwenOpenAiCompatibleProvider Provider(HttpClient client)
        => new(
            client,
            new QwenLocalProviderOptions(
                new Uri("http://127.0.0.1:1234/"),
                "Qwen3.5-9B-Q8_0.gguf",
                MaxInputBytes: 64 * 1024,
                MaxResponseBytes: 128 * 1024,
                MaxOutputTokens: 4096));

    private static ModelRequest Request()
        => new(
            Guid.NewGuid(),
            "recovery",
            "NU1605 dependency failure",
            ["package A requires version 2"],
            512);

    private static string ValidResponse()
        => OpenAiResponse("""
            {"responseId":"candidate-42","summary":"dependency mismatch","evidence":[{"source":"official-doc","sourceClass":"documentation"}],"proposedSteps":["pin package A"]}
            """);

    private static string OpenAiResponse(string content)
        => JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new { content }
                }
            }
        });

    private static HttpResponseMessage Response(HttpStatusCode status, string content)
        => new(status)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler = handler;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
            : this(request => Task.FromResult(handler(request)))
        {
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => _handler(request);
    }
}
