using System.Runtime.CompilerServices;
using System.Threading.Channels;
using EfsAiHub.Core.Abstractions.Observability;
using EfsAiHub.Platform.Runtime.Factories;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace EfsAiHub.Tests.Unit.Platform;

[Trait("Category", "Unit")]
public class TokenTrackingChatClientTests
{
    private static TokenTrackingChatClient Build(
        IChatClient inner,
        Channel<LlmTokenUsage>? channel = null,
        string agentId = "agent-1",
        string modelId = "gpt-4o") =>
        new(
            inner,
            agentId,
            modelId,
            (channel ?? Channel.CreateUnbounded<LlmTokenUsage>()).Writer,
            NullLogger.Instance);

    // ── GetResponseAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetResponseAsync_ResponsePassthrough_RetornaTextoDoInner()
    {
        var inner = new StubResponseClient("olá mundo");
        var client = Build(inner);

        var response = await client.GetResponseAsync([new AiChatMessage(ChatRole.User, "oi")]);

        response.Text.Should().Be("olá mundo");
    }

    [Fact]
    public async Task GetResponseAsync_ComUsage_EnfileiraNaChannel()
    {
        var channel = Channel.CreateUnbounded<LlmTokenUsage>();
        var inner = new StubResponseClient("ok", inputTokens: 20, outputTokens: 10);
        var client = Build(inner, channel, agentId: "agent-boleta");

        await client.GetResponseAsync([new AiChatMessage(ChatRole.User, "hi")]);

        channel.Reader.TryRead(out var usage).Should().BeTrue();
        usage!.AgentId.Should().Be("agent-boleta");
        usage.InputTokens.Should().Be(20);
        usage.OutputTokens.Should().Be(10);
        usage.TotalTokens.Should().Be(30);
    }

    [Fact]
    public async Task GetResponseAsync_SemUsage_NaoEnfileira()
    {
        var channel = Channel.CreateUnbounded<LlmTokenUsage>();
        var inner = new StubResponseClient("ok", inputTokens: 0, outputTokens: 0);
        var client = Build(inner, channel);

        await client.GetResponseAsync([new AiChatMessage(ChatRole.User, "hi")]);

        channel.Reader.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public async Task GetResponseAsync_ModelId_PreservadoNaUsage()
    {
        var channel = Channel.CreateUnbounded<LlmTokenUsage>();
        var inner = new StubResponseClient("ok", inputTokens: 5, outputTokens: 5);
        var client = Build(inner, channel, modelId: "gpt-4o-mini");

        await client.GetResponseAsync([new AiChatMessage(ChatRole.User, "test")]);

        channel.Reader.TryRead(out var usage).Should().BeTrue();
        usage!.ModelId.Should().Be("gpt-4o-mini");
    }

    [Fact]
    public async Task GetResponseAsync_OutputLongo_TruncadoA4000Chars()
    {
        var channel = Channel.CreateUnbounded<LlmTokenUsage>();
        var longText = new string('x', 5000);
        var inner = new StubResponseClient(longText, inputTokens: 100, outputTokens: 1000);
        var client = Build(inner, channel);

        await client.GetResponseAsync([new AiChatMessage(ChatRole.User, "hi")]);

        channel.Reader.TryRead(out var usage).Should().BeTrue();
        usage!.OutputContent.Should().HaveLength(4000);
    }

    // ── GetStreamingResponseAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetStreamingResponseAsync_EntreguaTodosUpdates()
    {
        var inner = new StubStreamingClient(["Hello", " world"], inputTokens: 5, outputTokens: 5);
        var client = Build(inner);

        var chunks = new List<string>();
        await foreach (var update in client.GetStreamingResponseAsync([new AiChatMessage(ChatRole.User, "hi")]))
        {
            if (update.Text is not null)
                chunks.Add(update.Text);
        }

        chunks.Should().ContainInOrder("Hello", " world");
    }

    [Fact]
    public async Task GetStreamingResponseAsync_ComUsage_EnfileiraNaChannel()
    {
        var channel = Channel.CreateUnbounded<LlmTokenUsage>();
        var inner = new StubStreamingClient(["ok"], inputTokens: 10, outputTokens: 5);
        var client = Build(inner, channel, agentId: "stream-agent");

        await foreach (var _ in client.GetStreamingResponseAsync([new AiChatMessage(ChatRole.User, "go")])) { }

        channel.Reader.TryRead(out var usage).Should().BeTrue();
        usage!.AgentId.Should().Be("stream-agent");
        usage.TotalTokens.Should().Be(15);
    }
}

// ── Stubs ─────────────────────────────────────────────────────────────────────

file sealed class StubResponseClient : IChatClient
{
    private readonly string _text;
    private readonly int _inputTokens;
    private readonly int _outputTokens;

    public StubResponseClient(string text, int inputTokens = 0, int outputTokens = 0)
    {
        _text = text;
        _inputTokens = inputTokens;
        _outputTokens = outputTokens;
    }

    public ChatClientMetadata Metadata => new("stub", null, null);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<AiChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
    {
        var response = new ChatResponse([new AiChatMessage(ChatRole.Assistant, _text)]);
        if (_inputTokens > 0 || _outputTokens > 0)
        {
            response.Usage = new UsageDetails
            {
                InputTokenCount = _inputTokens,
                OutputTokenCount = _outputTokens,
                TotalTokenCount = _inputTokens + _outputTokens
            };
        }
        return Task.FromResult(response);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<AiChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
        => throw new NotImplementedException();

    public object? GetService(Type t, object? key = null) => null;
    public void Dispose() { }
}

file sealed class StubStreamingClient : IChatClient
{
    private readonly string[] _chunks;
    private readonly int _inputTokens;
    private readonly int _outputTokens;

    public StubStreamingClient(string[] chunks, int inputTokens = 0, int outputTokens = 0)
    {
        _chunks = chunks;
        _inputTokens = inputTokens;
        _outputTokens = outputTokens;
    }

    public ChatClientMetadata Metadata => new("stub-streaming", null, null);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<AiChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
        => throw new NotImplementedException();

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<AiChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var chunk in _chunks)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, chunk);
            await Task.Yield();
        }

        if (_inputTokens > 0 || _outputTokens > 0)
        {
            var update = new ChatResponseUpdate(ChatRole.Assistant, (string?)null);
            update.Contents.Add(new UsageContent(new UsageDetails
            {
                InputTokenCount = _inputTokens,
                OutputTokenCount = _outputTokens,
                TotalTokenCount = _inputTokens + _outputTokens
            }));
            yield return update;
        }
    }

    public object? GetService(Type t, object? key = null) => null;
    public void Dispose() { }
}
