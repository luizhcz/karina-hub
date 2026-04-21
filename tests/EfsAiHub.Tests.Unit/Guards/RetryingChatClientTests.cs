using System.Net;
using System.Runtime.CompilerServices;
using EfsAiHub.Platform.Runtime.Factories;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace EfsAiHub.Tests.Unit.Guards;

[Trait("Category", "Unit")]
public class RetryingChatClientTests
{
    private static readonly IEnumerable<AiChatMessage> AnyMessages =
    [
        new AiChatMessage(ChatRole.User, "teste"),
    ];

    private static RetryingChatClient BuildSut(IChatClient inner, int maxRetries = 2) =>
        new(inner, "agent-test", "model-test",
            NullLogger<RetryingChatClientTests>.Instance,
            new ResiliencePolicy { MaxRetries = maxRetries, InitialDelayMs = 1, BackoffMultiplier = 1.0 });

    // ── Retry bem-sucedido ────────────────────────────────────────────────────

    [Fact]
    public async Task GetResponseAsync_PrimeiraChavada_NaoRetenta()
    {
        var stub = new CountingChatClient("ok");
        var sut = BuildSut(stub);

        var response = await sut.GetResponseAsync(AnyMessages);

        response.Text.Should().Be("ok");
        stub.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetResponseAsync_Falha429_Retenta()
    {
        var stub = new FailThenSucceedChatClient(HttpStatusCode.TooManyRequests, failCount: 1, "retried");
        var sut = BuildSut(stub, maxRetries: 2);

        var response = await sut.GetResponseAsync(AnyMessages);

        response.Text.Should().Be("retried");
        stub.TotalCalls.Should().Be(2);
    }

    [Fact]
    public async Task GetResponseAsync_Falha500_Retenta()
    {
        var stub = new FailThenSucceedChatClient(HttpStatusCode.InternalServerError, failCount: 1, "retried");
        var sut = BuildSut(stub, maxRetries: 2);

        var response = await sut.GetResponseAsync(AnyMessages);

        response.Text.Should().Be("retried");
        stub.TotalCalls.Should().Be(2);
    }

    [Fact]
    public async Task GetResponseAsync_MaxRetriesEsgotados_Lanca()
    {
        var stub = new AlwaysFailChatClient(HttpStatusCode.ServiceUnavailable);
        var sut = BuildSut(stub, maxRetries: 2);

        var act = async () => await sut.GetResponseAsync(AnyMessages);

        await act.Should().ThrowAsync<Exception>();
        stub.CallCount.Should().BeGreaterThan(1); // tentou mais de uma vez
    }

    [Fact]
    public async Task GetResponseAsync_Falha400_NaoRetenta()
    {
        // Bad request não é transiente → não deve retentar
        var stub = new AlwaysFailChatClient(HttpStatusCode.BadRequest);
        var sut = BuildSut(stub, maxRetries: 3);

        var act = async () => await sut.GetResponseAsync(AnyMessages);

        await act.Should().ThrowAsync<Exception>();
        stub.CallCount.Should().Be(1); // apenas 1 chamada, sem retry
    }
}

// ── Stubs ─────────────────────────────────────────────────────────────────────

internal sealed class CountingChatClient : IChatClient
{
    private readonly string _text;
    public int CallCount { get; private set; }

    public CountingChatClient(string text) => _text = text;
    public ChatClientMetadata Metadata => new("stub", null, null);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<AiChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
    {
        CallCount++;
        return Task.FromResult(new ChatResponse([new AiChatMessage(ChatRole.Assistant, _text)]));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<AiChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, _text);
        await Task.CompletedTask;
    }

    public object? GetService(Type t, object? key = null) => null;
    public void Dispose() { }
}

internal sealed class FailThenSucceedChatClient : IChatClient
{
    private readonly HttpStatusCode _code;
    private readonly int _failCount;
    private readonly string _successText;
    private int _calls;

    public int TotalCalls => _calls;

    public FailThenSucceedChatClient(HttpStatusCode code, int failCount, string successText)
    {
        _code = code;
        _failCount = failCount;
        _successText = successText;
    }

    public ChatClientMetadata Metadata => new("stub", null, null);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<AiChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
    {
        _calls++;
        if (_calls <= _failCount)
            throw new HttpRequestException("transient", null, _code);
        return Task.FromResult(new ChatResponse([new AiChatMessage(ChatRole.Assistant, _successText)]));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<AiChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
        => throw new NotImplementedException();

    public object? GetService(Type t, object? key = null) => null;
    public void Dispose() { }
}

internal sealed class AlwaysFailChatClient : IChatClient
{
    private readonly HttpStatusCode _code;
    public int CallCount { get; private set; }

    public AlwaysFailChatClient(HttpStatusCode code) => _code = code;
    public ChatClientMetadata Metadata => new("stub", null, null);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<AiChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
    {
        CallCount++;
        throw new HttpRequestException("always fail", null, _code);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<AiChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
        => throw new NotImplementedException();

    public object? GetService(Type t, object? key = null) => null;
    public void Dispose() { }
}
