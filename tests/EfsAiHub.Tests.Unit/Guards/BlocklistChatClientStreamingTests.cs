using System.Runtime.CompilerServices;
using EfsAiHub.Core.Abstractions.Blocklist;
using EfsAiHub.Core.Orchestration.Executors;
using EfsAiHub.Platform.Runtime.Guards;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;
using ExecCtx = EfsAiHub.Core.Agents.Execution.ExecutionContext;
using ExecBudget = EfsAiHub.Core.Agents.Execution.ExecutionBudget;

namespace EfsAiHub.Tests.Unit.Guards;

/// <summary>
/// Cobre BlocklistChatClient streaming + UTF-8 não-ASCII (PR 10.g):
/// 1. Block: chunk com pattern proibido aborta stream com BlocklistViolationException
/// 2. Sem violação: tail flushed no fim
/// 3. Boundary crossing: pattern partido entre 2 chunks detectado no buffer combinado
/// 4. UTF-8 não-ASCII no input: pattern com 'ã'/'ç' detectado corretamente
/// 5. Streaming preserva texto não-ASCII fora do tail
/// </summary>
[Trait("Category", "Unit")]
public class BlocklistChatClientStreamingTests
{
    private const string ProjectId = "p-test";
    private const string AgentId = "a-test";

    [Fact]
    public async Task GetStreamingResponse_ChunkComBlock_AbortaComException()
    {
        var matcher = MakeMatcher(("secret_key", BlocklistAction.Block));
        var inner = new FakeStreamingClient([
            "begin ",
            "secret_key",                  // hit aqui
            " end"
        ]);
        var sut = BuildChatClient(inner, matcher);

        await using var ctxScope = SetExecutionContext();

        var act = async () =>
        {
            await foreach (var _ in sut.GetStreamingResponseAsync(MakeUserMessages("oi")))
            {
                // consume
            }
        };

        await act.Should().ThrowAsync<BlocklistViolationException>();
    }

    [Fact]
    public async Task GetStreamingResponse_SemViolacao_EmitsTodoOConteudoIncluindoTail()
    {
        var matcher = MakeMatcher(("forbidden", BlocklistAction.Block));
        var inner = new FakeStreamingClient([
            "Olá, ", "tudo bem? ", "Resposta normal sem viol."
        ]);
        var sut = BuildChatClient(inner, matcher);

        await using var ctxScope = SetExecutionContext();

        var collected = new System.Text.StringBuilder();
        await foreach (var update in sut.GetStreamingResponseAsync(MakeUserMessages("oi")))
        {
            foreach (var c in update.Contents)
                if (c is TextContent tc) collected.Append(tc.Text);
        }

        collected.ToString().Should().Be("Olá, tudo bem? Resposta normal sem viol.");
    }

    [Fact]
    public async Task GetStreamingResponse_PatternCruzandoBoundaryEntreChunks_DetectaEAborta()
    {
        // Pattern "secret_token" cruza chunks: "...sec" + "ret_token..." é detectado
        // pelo sliding-window no buffer combinado.
        var matcher = MakeMatcher(("secret_token", BlocklistAction.Block));
        var inner = new FakeStreamingClient([
            "Aqui vai o sec",
            "ret_token agora"
        ]);
        var sut = BuildChatClient(inner, matcher);

        await using var ctxScope = SetExecutionContext();

        var act = async () =>
        {
            await foreach (var _ in sut.GetStreamingResponseAsync(MakeUserMessages("oi"))) { }
        };

        await act.Should().ThrowAsync<BlocklistViolationException>();
    }

    [Fact]
    public async Task GetResponse_InputComCaracteresNaoAscii_DetectaPattern()
    {
        // Pattern literal com não-ASCII: "operação_secreta". Confirma que matcher
        // funciona com UTF-8 não escapado (caracteres 'ç', 'ã' preservados literais).
        var matcher = MakeMatcher(("operação_secreta", BlocklistAction.Block));
        var inner = new FakeStreamingClient(["resposta"]);
        var sut = BuildChatClient(inner, matcher);

        await using var ctxScope = SetExecutionContext();

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Por favor execute a operação_secreta agora.")
        };

        var act = async () => await sut.GetResponseAsync(messages);

        await act.Should().ThrowAsync<BlocklistViolationException>();
    }

    [Fact]
    public async Task GetStreamingResponse_TextoNaoAsciiNoOutput_PreservaCharsForaDoTail()
    {
        // Stream sem violação contendo 'ã', 'ç', 'ã' — sliding-window não pode
        // mutilar caracteres multi-byte se eles caírem no tail boundary.
        var matcher = MakeMatcher(("xxxx_block", BlocklistAction.Block));
        var inner = new FakeStreamingClient([
            "Atenção:",
            " coordenação ",
            "concluída em São Paulo. ",
            "Próximo passo?"
        ]);
        var sut = BuildChatClient(inner, matcher);

        await using var ctxScope = SetExecutionContext();

        var collected = new System.Text.StringBuilder();
        await foreach (var update in sut.GetStreamingResponseAsync(MakeUserMessages("oi")))
        {
            foreach (var c in update.Contents)
                if (c is TextContent tc) collected.Append(tc.Text);
        }

        collected.ToString().Should().Be(
            "Atenção: coordenação concluída em São Paulo. Próximo passo?",
            "sliding-window não deve mutilar caracteres não-ASCII");
    }

    [Fact]
    public async Task GetStreamingResponse_ChunkGrandeAcumulaPara_DispararScanIntermediario()
    {
        // PR 11 — B2 Throttled Scan: scan intermediário só dispara quando charsSinceLastScan
        // >= ScanIntervalChars (512). Este test exercita o caminho de produção onde chunks
        // acumulam o suficiente pra disparar scan ANTES do post-stream — diferente dos outros
        // tests que usam chunks pequenos e caem no scan final.
        var matcher = MakeMatcher(("forbidden_token_xyz", BlocklistAction.Block));

        // Chunk 1 de 600 chars + chunk 2 contendo o pattern. O scan intermediário dispara
        // após o primeiro chunk (charsSinceLastScan=600 >= 512), encontra nada, reseta.
        // Próximo chunk vem com pattern + acumula novos chars; throttle vai bater de novo
        // ou no scan final.
        var chunk1 = new string('a', 600);
        var chunk2 = " forbidden_token_xyz aqui";

        var inner = new FakeStreamingClient([chunk1, chunk2]);
        var sut = BuildChatClient(inner, matcher);

        await using var ctxScope = SetExecutionContext();

        var act = async () =>
        {
            await foreach (var _ in sut.GetStreamingResponseAsync(MakeUserMessages("oi"))) { }
        };

        await act.Should().ThrowAsync<BlocklistViolationException>(
            "throttle não pode mascarar block mesmo quando hit acumula entre scans");
    }

    [Fact]
    public async Task GetStreamingResponse_SemPatterns_NaoTocaNoStream()
    {
        // Matcher sem patterns (Empty) — fast path no início de GetStreamingResponseAsync,
        // updates passam direto sem processar buffer.
        var inner = new FakeStreamingClient(["chunk1", "chunk2", "chunk3"]);
        var sut = BuildChatClient(inner, BlocklistMatcher.Empty);

        await using var ctxScope = SetExecutionContext();

        var count = 0;
        await foreach (var _ in sut.GetStreamingResponseAsync(MakeUserMessages("oi")))
            count++;

        count.Should().Be(3, "fast path não deve introduzir/perder updates");
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private static IList<ChatMessage> MakeUserMessages(string text)
        => [new ChatMessage(ChatRole.User, text)];

    private static BlocklistMatcher MakeMatcher(params (string Pattern, BlocklistAction Action)[] patterns)
    {
        var effective = patterns.Select((p, i) =>
        {
            var src = new BlocklistPattern(
                Id: $"test.{i}",
                GroupId: "TEST",
                Type: BlocklistPatternType.Literal,
                Pattern: p.Pattern,
                Validator: BlocklistValidator.None,
                WholeWord: false,
                CaseSensitive: false,
                DefaultAction: p.Action,
                Enabled: true,
                Version: 1);
            return new EffectivePattern(src, "TEST", p.Action);
        }).ToList();

        return BlocklistMatcher.Build(
            effective,
            new Dictionary<string, EfsAiHub.Platform.Runtime.Guards.BuiltIns.IBuiltInPatternHandler>(),
            replacement: "[REDACTED]",
            logger: null);
    }

    private static BlocklistChatClient BuildChatClient(IChatClient inner, BlocklistMatcher matcher)
    {
        var engine = new TestEngine(matcher);
        return new BlocklistChatClient(
            inner: inner,
            engine: engine,
            eventBus: null,
            auditLogger: null,
            agentId: AgentId,
            logger: NullLogger.Instance);
    }

    private static AsyncLocalScope SetExecutionContext()
    {
        var prev = DelegateExecutor.Current.Value;
        DelegateExecutor.Current.Value = new ExecCtx(
            ExecutionId: "exec-test",
            WorkflowId: "wf-test",
            Input: null,
            PromptVersions: new System.Collections.Concurrent.ConcurrentDictionary<string, string>(),
            NodeCallback: null,
            Budget: new ExecBudget(0),
            ProjectId: ProjectId);
        return new AsyncLocalScope(prev);
    }

    private sealed class AsyncLocalScope : IAsyncDisposable
    {
        private readonly ExecCtx? _prev;
        public AsyncLocalScope(ExecCtx? prev) => _prev = prev;
        public ValueTask DisposeAsync()
        {
            DelegateExecutor.Current.Value = _prev;
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Subclasse de BlocklistEngine pra teste — passa null nas deps de runtime
    /// e retorna um matcher pré-construído via override de GetMatcherAsync.
    /// Funciona porque base ctor só atribui campos sem chamar nada.
    /// </summary>
    private sealed class TestEngine : BlocklistEngine
    {
        private readonly BlocklistMatcher _matcher;

        public TestEngine(BlocklistMatcher matcher) : base(
            catalogRepo: null!,
            projectRepo: null!,
            l2Cache: null!,
            dispatcher: null!,
            builtIns: Array.Empty<EfsAiHub.Platform.Runtime.Guards.BuiltIns.IBuiltInPatternHandler>(),
            logger: NullLogger<BlocklistEngine>.Instance,
            services: null!)
        {
            _matcher = matcher;
        }

        public override Task<BlocklistMatcher> GetMatcherAsync(string projectId, CancellationToken ct = default)
            => Task.FromResult(_matcher);
    }

    private sealed class FakeStreamingClient : IChatClient
    {
        private readonly string[] _chunks;
        public FakeStreamingClient(string[] chunks) => _chunks = chunks;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var combined = string.Concat(_chunks);
            var msg = new ChatMessage(ChatRole.Assistant, combined);
            return Task.FromResult(new ChatResponse([msg]));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
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
