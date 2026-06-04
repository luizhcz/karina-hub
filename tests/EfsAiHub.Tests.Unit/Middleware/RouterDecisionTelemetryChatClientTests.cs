using System.Diagnostics.Metrics;
using System.Text.Json;
using EfsAiHub.Core.Agents.RouterIntents;
using EfsAiHub.Core.Orchestration.Executors;
using EfsAiHub.Infra.Observability;
using EfsAiHub.Platform.Runtime.Middlewares;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;
using ExecutionContext = EfsAiHub.Core.Agents.Execution.ExecutionContext;

namespace EfsAiHub.Tests.Unit.Middleware;

/// <summary>
/// Cobre PR 3 do plano de ambiguidade: telemetria por decisão do Router +
/// validação dura (rewrite de needs_clarification inválido → out_of_scope).
/// Cada teste captura emissões OTel via <see cref="MeterListener"/> num escopo
/// isolado pra não interferir entre casos.
/// </summary>
[Trait("Category", "Unit")]
public class RouterDecisionTelemetryChatClientTests : IDisposable
{
    private readonly ExecutionContext _execCtx;
    private readonly List<(string Name, double Value, KeyValuePair<string, object?>[] Tags)> _emissions = new();
    private readonly MeterListener _listener;

    public RouterDecisionTelemetryChatClientTests()
    {
        _execCtx = new ExecutionContext(
            ExecutionId: "exec-1",
            WorkflowId: "wf-1",
            Input: null,
            PromptVersions: new(),
            NodeCallback: null,
            Budget: new ExecutionBudget(100_000),
            UpdateSharedState: null,
            ConversationId: "conv-1") with { ProjectId = "proj-1" };
        DelegateExecutor.Current.Value = _execCtx;

        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == MetricsRegistry.MeterName
                    && (instrument.Name.StartsWith("router.")))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };
        _listener.SetMeasurementEventCallback<long>(OnLong);
        _listener.SetMeasurementEventCallback<double>(OnDouble);
        _listener.Start();
    }

    public void Dispose()
    {
        DelegateExecutor.Current.Value = null;
        _listener.Dispose();
    }

    private void OnLong(Instrument instrument, long measurement, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state) =>
        _emissions.Add((instrument.Name, measurement, tags.ToArray()));

    private void OnDouble(Instrument instrument, double measurement, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state) =>
        _emissions.Add((instrument.Name, measurement, tags.ToArray()));

    private static RouterDecisionTelemetryChatClient Build(IChatClient inner, string agentId = "router-x") =>
        new(inner, agentId, settings: null, logger: NullLogger.Instance);

    private static string JsonOutput(
        string intent,
        double confidence = 0.9,
        string reason = "ok",
        int candidateCount = 0)
    {
        var candidates = new System.Text.StringBuilder("[");
        for (var i = 0; i < candidateCount; i++)
        {
            if (i > 0) candidates.Append(',');
            candidates.Append($"{{\"intent\":\"biz_{i}\",\"confidence\":0.4}}");
        }
        candidates.Append(']');

        return $$"""
        {
          "intent": "{{intent}}",
          "confidence": {{confidence.ToString(System.Globalization.CultureInfo.InvariantCulture)}},
          "reason": "{{reason}}",
          "candidate_intents": {{candidates}},
          "operationalMemory": {
            "last_intent": "{{intent}}",
            "last_reason": "{{reason}}",
            "clarification_depth": 0,
            "last_candidate_intents": []
          }
        }
        """;
    }

    /// <summary>
    /// Constrói o conjunto de mensagens system que o OperationalMemoryChatClient
    /// injetaria com o payload do turno anterior. Usado pra exercitar o loop
    /// guard server-side: passamos como input ao GetResponseAsync e o middleware
    /// extrai o estado anterior no OnBefore.
    /// </summary>
    private static ChatMessage[] MessagesWithPriorMemory(string priorIntent, int priorDepth) =>
        new[]
        {
            new ChatMessage(ChatRole.System,
                "<operational_memory>\n" +
                "{" +
                $"\"last_intent\":\"{priorIntent}\"," +
                "\"last_reason\":\"prev\"," +
                $"\"clarification_depth\":{priorDepth}," +
                "\"last_candidate_intents\":[]" +
                "}" +
                "\n</operational_memory>"),
            new ChatMessage(ChatRole.User, "test"),
        };

    // ── Caminho feliz: outputs válidos não são reescritos ─────────────────

    [Fact]
    public async Task DominantIntent_DoesNotRewrite_EmitsDominantSignal()
    {
        var output = JsonOutput("investir_renda_fixa", confidence: 0.85, reason: "match claro");
        var inner = new FakeChatClient(output);
        var sut = Build(inner);

        var response = await sut.GetResponseAsync([new ChatMessage(ChatRole.User, "test")]);
        var finalText = ExtractText(response);

        finalText.Should().Be(output, "intent dominante não é reescrita");

        _emissions.Should().Contain(e => e.Name == "router.decisions_total"
                                         && e.Tags.Any(t => t.Key == "intent"
                                                            && (string?)t.Value == "investir_renda_fixa"));
        _emissions.Should().Contain(e => e.Name == "router.ambiguity_signals_total"
                                         && e.Tags.Any(t => t.Key == "signal"
                                                            && (string?)t.Value == "dominant"));
        _emissions.Should().Contain(e => e.Name == "router.confidence" && Math.Abs(e.Value - 0.85) < 1e-9);
    }

    [Fact]
    public async Task OutOfScopeIntent_DoesNotRewrite_EmitsOutOfScopeSignal()
    {
        var output = JsonOutput(SystemIntents.OutOfScopeName, confidence: 0.95, reason: "fora do domínio");
        var inner = new FakeChatClient(output);
        var sut = Build(inner);

        var response = await sut.GetResponseAsync([new ChatMessage(ChatRole.User, "test")]);
        ExtractText(response).Should().Be(output);

        _emissions.Should().Contain(e => e.Name == "router.ambiguity_signals_total"
                                         && e.Tags.Any(t => t.Key == "signal"
                                                            && (string?)t.Value == "out_of_scope"));
    }

    [Fact]
    public async Task NeedsClarification_ComCandidatosSuficientes_NaoReescreve()
    {
        var output = JsonOutput(
            SystemIntents.NeedsClarificationName,
            confidence: 0.5,
            reason: "ambíguo entre 2 intents",
            candidateCount: 2);
        var inner = new FakeChatClient(output);
        var sut = Build(inner);

        var response = await sut.GetResponseAsync([new ChatMessage(ChatRole.User, "test")]);
        ExtractText(response).Should().Be(output, "candidate_intents.Count >= 2 → válido");

        _emissions.Should().Contain(e => e.Name == "router.ambiguity_signals_total"
                                         && e.Tags.Any(t => t.Key == "signal"
                                                            && (string?)t.Value == "needs_clarification"));
        _emissions.Should().NotContain(e => e.Name == "router.ambiguity_signals_total"
                                            && e.Tags.Any(t => t.Key == "signal"
                                                               && (string?)t.Value == "invalid_clarification"));
    }

    // ── Validação dura: rewrite quando needs_clarification inválido ───────

    [Fact]
    public async Task NeedsClarification_SemCandidatos_ReescreveParaOutOfScope()
    {
        var output = JsonOutput(
            SystemIntents.NeedsClarificationName,
            confidence: 0.4,
            reason: "ambíguo mas LLM esqueceu candidate_intents",
            candidateCount: 0);
        var inner = new FakeChatClient(output);
        var sut = Build(inner);

        var response = await sut.GetResponseAsync([new ChatMessage(ChatRole.User, "test")]);
        var finalText = ExtractText(response);

        finalText.Should().NotBeNull();
        using var doc = JsonDocument.Parse(finalText!);
        doc.RootElement.GetProperty("intent").GetString()
            .Should().Be(SystemIntents.OutOfScopeName);
        doc.RootElement.GetProperty("confidence").GetDouble().Should().Be(0);
        doc.RootElement.GetProperty("candidate_intents").GetArrayLength().Should().Be(0);

        var memory = doc.RootElement.GetProperty("operationalMemory");
        memory.GetProperty("last_intent").GetString().Should().Be(SystemIntents.OutOfScopeName);
        memory.GetProperty("clarification_depth").GetInt32().Should().Be(0);

        _emissions.Should().Contain(e => e.Name == "router.ambiguity_signals_total"
                                         && e.Tags.Any(t => t.Key == "signal"
                                                            && (string?)t.Value == "invalid_clarification"));
        _emissions.Should().Contain(e => e.Name == "router.decisions_total"
                                         && e.Tags.Any(t => t.Key == "intent"
                                                            && (string?)t.Value == SystemIntents.OutOfScopeName),
            "métrica reflete a decisão FINAL (após rewrite), não o output bruto do LLM");
    }

    [Fact]
    public async Task NeedsClarification_ApenasUmCandidato_ReescreveParaOutOfScope()
    {
        var output = JsonOutput(
            SystemIntents.NeedsClarificationName,
            confidence: 0.5,
            reason: "1 candidato só — não justifica desambiguar",
            candidateCount: 1);
        var inner = new FakeChatClient(output);
        var sut = Build(inner);

        var response = await sut.GetResponseAsync([new ChatMessage(ChatRole.User, "test")]);
        using var doc = JsonDocument.Parse(ExtractText(response)!);
        doc.RootElement.GetProperty("intent").GetString().Should().Be(SystemIntents.OutOfScopeName);
    }

    // ── Edge cases ─────────────────────────────────────────────────────────

    [Fact]
    public async Task OutputNaoJson_NoOp_EmiteParseFailure()
    {
        var inner = new FakeChatClient("não é JSON, é texto livre que algum middleware bug deixou passar");
        var sut = Build(inner);

        var response = await sut.GetResponseAsync([new ChatMessage(ChatRole.User, "test")]);
        ExtractText(response).Should().StartWith("não é JSON");

        _emissions.Should().Contain(e => e.Name == "router.ambiguity_signals_total"
                                         && e.Tags.Any(t => t.Key == "signal"
                                                            && (string?)t.Value == "parse_failure"));
        _emissions.Should().NotContain(e => e.Name == "router.decisions_total");
    }

    [Fact]
    public async Task JsonSemCampoIntent_NoOp_EmiteParseFailure()
    {
        // Cenário hipotético: middleware aplicado a agente não-Router (admin bug).
        var inner = new FakeChatClient("""{"foo": "bar", "baz": 42}""");
        var sut = Build(inner);

        await sut.GetResponseAsync([new ChatMessage(ChatRole.User, "test")]);

        _emissions.Should().Contain(e => e.Name == "router.ambiguity_signals_total"
                                         && e.Tags.Any(t => t.Key == "signal"
                                                            && (string?)t.Value == "parse_failure"));
    }

    [Fact]
    public async Task SemTextoNaResposta_NoOp()
    {
        // Tool call sem texto — pode acontecer quando LLM emite só function call.
        // Middleware deve devolver response intacto sem emitir nada.
        var inner = new FakeChatClient(string.Empty);
        var sut = Build(inner);

        var response = await sut.GetResponseAsync([new ChatMessage(ChatRole.User, "test")]);
        _emissions.Should().BeEmpty();
    }

    // ── Streaming ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Streaming_DominantIntent_NaoReescreve()
    {
        var json = JsonOutput("investir_renda_fixa", confidence: 0.85);
        var inner = new FakeStreamingChatClient(SplitInChunks(json, 3));
        var sut = Build(inner);

        var collected = new System.Text.StringBuilder();
        await foreach (var u in sut.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "test")]))
        {
            foreach (var content in u.Contents)
                if (content is TextContent tc) collected.Append(tc.Text);
        }

        collected.ToString().Should().Be(json);

        _emissions.Should().Contain(e => e.Name == "router.ambiguity_signals_total"
                                         && e.Tags.Any(t => t.Key == "signal"
                                                            && (string?)t.Value == "dominant"));
    }

    // ── Loop guard server-side (P0-#3) ───────────────────────────────────

    [Fact]
    public async Task LoopGuard_DispararQuandoEstadoAnteriorEraNeedsClarification()
    {
        // Estado anterior: last_intent=needs_clarification, depth=1.
        // LLM emite needs_clarification de novo (com candidatos válidos, ou
        // seja, NÃO é caso de invalid_clarification). Middleware deve
        // server-side rewrite pra out_of_scope e emitir signal
        // loop_guard_triggered.
        var output = JsonOutput(
            SystemIntents.NeedsClarificationName,
            confidence: 0.5,
            reason: "ainda ambíguo",
            candidateCount: 2);
        var inner = new FakeChatClient(output);
        var sut = Build(inner);

        var response = await sut.GetResponseAsync(
            MessagesWithPriorMemory(SystemIntents.NeedsClarificationName, priorDepth: 1));

        using var doc = JsonDocument.Parse(ExtractText(response)!);
        doc.RootElement.GetProperty("intent").GetString().Should().Be(SystemIntents.OutOfScopeName);
        doc.RootElement.GetProperty("operationalMemory")
            .GetProperty("last_intent").GetString().Should().Be(SystemIntents.OutOfScopeName);
        doc.RootElement.GetProperty("operationalMemory")
            .GetProperty("clarification_depth").GetInt32().Should().Be(0);

        _emissions.Should().Contain(e => e.Name == "router.ambiguity_signals_total"
                                         && e.Tags.Any(t => t.Key == "signal"
                                                            && (string?)t.Value == "loop_guard_triggered"));
        _emissions.Should().NotContain(e => e.Name == "router.ambiguity_signals_total"
                                            && e.Tags.Any(t => t.Key == "signal"
                                                               && (string?)t.Value == "invalid_clarification"));
    }

    [Fact]
    public async Task LoopGuard_NaoDispararQuandoEstadoAnteriorNaoEraNeedsClarification()
    {
        // Estado anterior: business intent (não needs_clarification). LLM
        // emite needs_clarification válido. NÃO é loop — primeira tentativa de
        // desambiguar nesse turno.
        var output = JsonOutput(
            SystemIntents.NeedsClarificationName,
            candidateCount: 2);
        var inner = new FakeChatClient(output);
        var sut = Build(inner);

        var response = await sut.GetResponseAsync(
            MessagesWithPriorMemory("investir_renda_fixa", priorDepth: 0));

        using var doc = JsonDocument.Parse(ExtractText(response)!);
        doc.RootElement.GetProperty("intent").GetString()
            .Should().Be(SystemIntents.NeedsClarificationName, "primeira tentativa não dispara loop guard");

        _emissions.Should().Contain(e => e.Name == "router.ambiguity_signals_total"
                                         && e.Tags.Any(t => t.Key == "signal"
                                                            && (string?)t.Value == "needs_clarification"));
        _emissions.Should().NotContain(e => e.Name == "router.ambiguity_signals_total"
                                            && e.Tags.Any(t => t.Key == "signal"
                                                               && (string?)t.Value == "loop_guard_triggered"));
    }

    [Fact]
    public async Task LoopGuard_NaoAcionaQuandoOutputNaoEhNeedsClarification()
    {
        // Estado anterior: needs_clarification (depth=1). LLM emite a INTENT
        // específica resolvendo a desambiguação. Caminho feliz; nenhum guard.
        var output = JsonOutput("investir_renda_fixa", confidence: 0.85);
        var inner = new FakeChatClient(output);
        var sut = Build(inner);

        var response = await sut.GetResponseAsync(
            MessagesWithPriorMemory(SystemIntents.NeedsClarificationName, priorDepth: 1));

        ExtractText(response).Should().Be(output);
        _emissions.Should().Contain(e => e.Name == "router.ambiguity_signals_total"
                                         && e.Tags.Any(t => t.Key == "signal"
                                                            && (string?)t.Value == "dominant"));
        _emissions.Should().NotContain(e => e.Name == "router.ambiguity_signals_total"
                                            && e.Tags.Any(t => t.Key == "signal"
                                                               && (string?)t.Value == "loop_guard_triggered"));
    }

    // ── P1-E: preserve clarification_depth no rewrite de invalid_clarification

    [Fact]
    public async Task InvalidClarificationRewrite_PreservaClarificationDepthAnterior()
    {
        // LLM emite needs_clarification sem candidatos válidos, mas com
        // clarification_depth=1 no operationalMemory (válido — contou turno
        // anterior). Rewrite zera intent mas NÃO reseta depth — próximo turno
        // segue contagem.
        var output = $$"""
        {
          "intent": "{{SystemIntents.NeedsClarificationName}}",
          "confidence": 0.4,
          "reason": "esqueci candidatos",
          "candidate_intents": [],
          "operationalMemory": {
            "last_intent": "{{SystemIntents.NeedsClarificationName}}",
            "last_reason": "ambíguo",
            "clarification_depth": 1,
            "last_candidate_intents": ["a","b"]
          }
        }
        """;
        var inner = new FakeChatClient(output);
        var sut = Build(inner);

        var response = await sut.GetResponseAsync([new ChatMessage(ChatRole.User, "test")]);
        using var doc = JsonDocument.Parse(ExtractText(response)!);

        // intent reescrito (schema violation), mas depth PRESERVADO.
        doc.RootElement.GetProperty("intent").GetString().Should().Be(SystemIntents.OutOfScopeName);
        doc.RootElement.GetProperty("operationalMemory")
            .GetProperty("clarification_depth").GetInt32()
            .Should().Be(1, "invalid_clarification não é loop guard — depth do contexto válido fica");
        doc.RootElement.GetProperty("operationalMemory")
            .GetProperty("last_candidate_intents").GetArrayLength()
            .Should().Be(0, "candidatos sempre zeram no rewrite — ciclo acabou");
    }

    [Fact]
    public async Task LoopGuardRewrite_ZeraClarificationDepth()
    {
        // Loop guard: ciclo terminou explicitamente. Reseta depth a 0 pra que
        // próximo turno seja fresh start.
        var output = JsonOutput(
            SystemIntents.NeedsClarificationName,
            candidateCount: 2);
        var inner = new FakeChatClient(output);
        var sut = Build(inner);

        var response = await sut.GetResponseAsync(
            MessagesWithPriorMemory(SystemIntents.NeedsClarificationName, priorDepth: 1));

        using var doc = JsonDocument.Parse(ExtractText(response)!);
        doc.RootElement.GetProperty("operationalMemory")
            .GetProperty("clarification_depth").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Streaming_NeedsClarificationInvalido_Reescreve()
    {
        var json = JsonOutput(SystemIntents.NeedsClarificationName, candidateCount: 0);
        var inner = new FakeStreamingChatClient(SplitInChunks(json, 4));
        var sut = Build(inner);

        var collected = new System.Text.StringBuilder();
        await foreach (var u in sut.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "test")]))
        {
            foreach (var content in u.Contents)
                if (content is TextContent tc) collected.Append(tc.Text);
        }

        using var doc = JsonDocument.Parse(collected.ToString());
        doc.RootElement.GetProperty("intent").GetString().Should().Be(SystemIntents.OutOfScopeName);

        _emissions.Should().Contain(e => e.Name == "router.ambiguity_signals_total"
                                         && e.Tags.Any(t => t.Key == "signal"
                                                            && (string?)t.Value == "invalid_clarification"));
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static string? ExtractText(ChatResponse response)
    {
        foreach (var msg in response.Messages)
        {
            if (msg.Role != ChatRole.Assistant) continue;
            foreach (var content in msg.Contents)
                if (content is TextContent tc) return tc.Text;
        }
        return null;
    }

    private static string[] SplitInChunks(string s, int parts)
    {
        var chunkSize = (int)Math.Ceiling((double)s.Length / parts);
        var chunks = new List<string>(parts);
        for (var i = 0; i < s.Length; i += chunkSize)
            chunks.Add(s.Substring(i, Math.Min(chunkSize, s.Length - i)));
        return chunks.ToArray();
    }

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
                    Contents = [new TextContent(chunk)],
                };
            }
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
