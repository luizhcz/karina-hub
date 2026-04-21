using System.Text.Json;
using EfsAiHub.Core.Orchestration.Executors;
using EfsAiHub.Platform.Runtime.Middlewares;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;
using ExecutionContext = EfsAiHub.Core.Agents.Execution.ExecutionContext;

namespace EfsAiHub.Tests.Unit.Middleware;

[Trait("Category", "Unit")]
public class StructuredOutputStateChatClientTests : IDisposable
{
    private readonly List<(string Path, JsonElement Value)> _stateUpdates = new();
    private readonly ExecutionContext _execCtx;

    public StructuredOutputStateChatClientTests()
    {
        _execCtx = new ExecutionContext(
            ExecutionId: "exec-1",
            WorkflowId: "wf-1",
            Input: null,
            PromptVersions: new(),
            NodeCallback: null,
            Budget: new ExecutionBudget(100_000),
            UpdateSharedState: (path, value) =>
            {
                _stateUpdates.Add((path, value));
                return Task.CompletedTask;
            },
            ConversationId: "conv-1");

        DelegateExecutor.Current.Value = _execCtx;
    }

    public void Dispose()
    {
        DelegateExecutor.Current.Value = null;
    }

    private static StructuredOutputStateChatClient Build(
        IChatClient inner,
        string agentId = "test-agent",
        Dictionary<string, string>? settings = null)
    {
        return new StructuredOutputStateChatClient(
            inner, agentId, settings, NullLogger.Instance);
    }

    // ── GetResponseAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task JsonObject_UpdatesState()
    {
        var json = """{"ticker": "PETR4", "qty": 100}""";
        var inner = new FakeChatClient(json);
        var sut = Build(inner);

        await sut.GetResponseAsync([new ChatMessage(ChatRole.User, "test")]);

        _stateUpdates.Should().HaveCount(1);
        _stateUpdates[0].Path.Should().Be("agents/test-agent");
        _stateUpdates[0].Value.GetProperty("ticker").GetString().Should().Be("PETR4");
        _stateUpdates[0].Value.GetProperty("qty").GetInt32().Should().Be(100);
    }

    [Fact]
    public async Task JsonArray_UpdatesState()
    {
        var json = """[{"ticker": "PETR4"}, {"ticker": "VALE3"}]""";
        var inner = new FakeChatClient(json);
        var sut = Build(inner);

        await sut.GetResponseAsync([new ChatMessage(ChatRole.User, "test")]);

        _stateUpdates.Should().HaveCount(1);
        _stateUpdates[0].Value.GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task PlainText_DoesNotUpdateState()
    {
        var inner = new FakeChatClient("A posição do cliente é 500 ações.");
        var sut = Build(inner);

        await sut.GetResponseAsync([new ChatMessage(ChatRole.User, "test")]);

        _stateUpdates.Should().BeEmpty();
    }

    [Fact]
    public async Task JsonPrimitive_DoesNotUpdateState()
    {
        var inner = new FakeChatClient("42");
        var sut = Build(inner);

        await sut.GetResponseAsync([new ChatMessage(ChatRole.User, "test")]);

        _stateUpdates.Should().BeEmpty();
    }

    [Fact]
    public async Task CustomStateKey_UsesSettingsKey()
    {
        var json = """{"status": "done"}""";
        var inner = new FakeChatClient(json);
        var sut = Build(inner, settings: new Dictionary<string, string> { ["stateKey"] = "coletor-boleta" });

        await sut.GetResponseAsync([new ChatMessage(ChatRole.User, "test")]);

        _stateUpdates.Should().HaveCount(1);
        _stateUpdates[0].Path.Should().Be("agents/coletor-boleta");
    }

    [Fact]
    public async Task NoExecutionContext_DoesNotThrow()
    {
        DelegateExecutor.Current.Value = null;

        var json = """{"ticker": "PETR4"}""";
        var inner = new FakeChatClient(json);
        var sut = Build(inner);

        await sut.GetResponseAsync([new ChatMessage(ChatRole.User, "test")]);

        _stateUpdates.Should().BeEmpty();
    }

    [Fact]
    public async Task EmptyResponse_DoesNotUpdateState()
    {
        var inner = new FakeChatClient("");
        var sut = Build(inner);

        await sut.GetResponseAsync([new ChatMessage(ChatRole.User, "test")]);

        _stateUpdates.Should().BeEmpty();
    }

    // ── GetStreamingResponseAsync ────────────────────────────────────────

    [Fact]
    public async Task Streaming_JsonObject_UpdatesStateAfterCompletion()
    {
        var chunks = new[] { "{\"ticker\":", " \"PETR4\", ", "\"qty\": 100}" };
        var inner = new FakeStreamingChatClient(chunks);
        var sut = Build(inner);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var u in sut.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "test")]))
            updates.Add(u);

        updates.Should().HaveCount(3);
        _stateUpdates.Should().HaveCount(1);
        _stateUpdates[0].Value.GetProperty("ticker").GetString().Should().Be("PETR4");
    }

    [Fact]
    public async Task Streaming_PlainText_DoesNotUpdateState()
    {
        var chunks = new[] { "Hello ", "world!" };
        var inner = new FakeStreamingChatClient(chunks);
        var sut = Build(inner);

        await foreach (var _ in sut.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "test")])) { }

        _stateUpdates.Should().BeEmpty();
    }

    // ── Fakes ────────────────────────────────────────────────────────────

    private sealed class FakeChatClient : IChatClient
    {
        private readonly string _responseText;
        public FakeChatClient(string responseText) => _responseText = responseText;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var msg = new ChatMessage(ChatRole.Assistant, _responseText);
            return Task.FromResult(new ChatResponse([msg]));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class FakeStreamingChatClient : IChatClient
    {
        private readonly string[] _chunks;
        public FakeStreamingChatClient(string[] chunks) => _chunks = chunks;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var chunk in _chunks)
            {
                yield return new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    Contents = [new TextContent(chunk)]
                };
            }
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
