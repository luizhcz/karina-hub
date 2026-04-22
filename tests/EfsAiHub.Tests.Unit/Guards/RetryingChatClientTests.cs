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

    // ── Timeout per-call (C4) ─────────────────────────────────────────────────

    [Fact]
    public async Task GetResponseAsync_CallTimeoutExpira_RetentaAteMax()
    {
        // Provider sempre trava até cancelar — timeout de 50ms força retry
        var stub = new HangingChatClient();
        var sut = new RetryingChatClient(
            stub, "agent-test", "model-test",
            NullLogger<RetryingChatClientTests>.Instance,
            new ResiliencePolicy(
                MaxRetries: 1, InitialDelayMs: 1, BackoffMultiplier: 1.0,
                CallTimeoutMs: 50));

        var act = async () => await sut.GetResponseAsync(AnyMessages);

        // MaxRetries=1 → 2 tentativas totais, ambas timed-out, última propaga OCE
        await act.Should().ThrowAsync<OperationCanceledException>();
        stub.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task GetResponseAsync_UserCancela_NaoRetenta()
    {
        // CancellationToken externo cancelado deve propagar sem retry
        var stub = new HangingChatClient();
        var sut = new RetryingChatClient(
            stub, "agent-test", "model-test",
            NullLogger<RetryingChatClientTests>.Instance,
            new ResiliencePolicy(
                MaxRetries: 3, InitialDelayMs: 1, BackoffMultiplier: 1.0,
                CallTimeoutMs: 10_000));  // timeout alto — só o user cancela

        using var cts = new CancellationTokenSource();
        var task = sut.GetResponseAsync(AnyMessages, cancellationToken: cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        var act = async () => await task;
        await act.Should().ThrowAsync<OperationCanceledException>();
        stub.CallCount.Should().Be(1);  // nenhum retry — foi user quem cancelou
    }

    // ── Jitter (C4) ───────────────────────────────────────────────────────────

    [Fact]
    public void ApplyJitter_RatioZero_RetornaDelayOriginal()
    {
        var sut = new RetryingChatClient(
            new CountingChatClient("ok"), "a", "m",
            NullLogger<RetryingChatClientTests>.Instance,
            new ResiliencePolicy(JitterRatio: 0.0));

        var original = TimeSpan.FromMilliseconds(1000);
        sut.ApplyJitter(original).Should().Be(original);
    }

    [Fact]
    public void ApplyJitter_RatioPositivo_AdicionaDentroDoRatio()
    {
        var sut = new RetryingChatClient(
            new CountingChatClient("ok"), "a", "m",
            NullLogger<RetryingChatClientTests>.Instance,
            new ResiliencePolicy(JitterRatio: 0.1));

        var original = TimeSpan.FromMilliseconds(1000);
        // Com ratio=0.1, jitter máximo = 100ms; resultado ∈ [1000, 1100]
        for (var i = 0; i < 100; i++)
        {
            var actual = sut.ApplyJitter(original);
            actual.TotalMilliseconds.Should()
                .BeGreaterThanOrEqualTo(1000)
                .And.BeLessThanOrEqualTo(1100);
        }
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

/// <summary>
/// Stub que trava indefinidamente até o CancellationToken ser cancelado.
/// Usado para simular provider pendurado e verificar timeout per-call / cancel por user.
/// </summary>
internal sealed class HangingChatClient : IChatClient
{
    public int CallCount { get; private set; }
    public ChatClientMetadata Metadata => new("stub", null, null);

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<AiChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
    {
        CallCount++;
        await Task.Delay(Timeout.Infinite, ct);
        throw new InvalidOperationException("unreachable");
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<AiChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
        => throw new NotImplementedException();

    public object? GetService(Type t, object? key = null) => null;
    public void Dispose() { }
}
