using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EfsAiHub.Core.Agents.Middlewares;
using EfsAiHub.Core.Agents.RouterIntents;
using EfsAiHub.Core.Orchestration.Executors;
using EfsAiHub.Infra.Observability;
using Microsoft.Extensions.AI;

namespace EfsAiHub.Platform.Runtime.Middlewares;

/// <summary>
/// Telemetria por decisão do Router + validação dura + loop guard server-side.
///
/// <para>
/// Roda como middleware POST do pipeline, mas posicionado MAIS INTERNO que o
/// <see cref="OperationalMemoryChatClient"/> pra que o rewrite aconteça ANTES
/// do estado ser persistido em <c>aihub.operational_memory</c>. Isso garante
/// que o estado persistido reflete o output FINAL emitido ao usuário, não o
/// output bruto do LLM — invariante essencial pro loop guard funcionar entre
/// turnos. <c>AgentTemplateService.ApplyRouter</c> auto-injeta este middleware
/// na frente do array <c>Middlewares</c>; <c>AgentFactory</c> reconhece esse
/// tipo como pre-memory phase.
/// </para>
///
/// <para>Para cada turno:</para>
/// <list type="number">
///   <item>OnBefore: parseia o <c>&lt;operational_memory&gt;</c> system message
///         injetado pelo OpMem (mais externo) pra extrair o estado anterior —
///         essencial pro loop guard server-side decidir se o LLM está
///         repetindo <c>needs_clarification</c> de forma indevida.</item>
///   <item>OnAfter: parseia o output como JSON e extrai <c>intent</c>,
///         <c>confidence</c>, <c>reason</c>, <c>candidate_intents</c>.</item>
///   <item>Emite métricas OTel (<c>router.decisions_total</c>,
///         <c>router.confidence</c> histograma,
///         <c>router.ambiguity_signals_total</c>) + 1 log estruturado.</item>
///   <item>Validação dura — REESCREVE o output como <c>out_of_scope</c> em 2
///         casos: (a) <c>intent == needs_clarification</c> com menos de 2
///         candidatos (signal <c>invalid_clarification</c>); (b) loop guard
///         server-side disparado (signal <c>loop_guard_triggered</c>).</item>
/// </list>
///
/// <para>
/// Falha de parse (output não-JSON) é no-op silencioso — outros middlewares
/// já log warnings; aqui só conta <c>signal=parse_failure</c>.
/// </para>
/// </summary>
public sealed class RouterDecisionTelemetryChatClient : AgentMiddlewareBase
{
    // Loop guard threshold: a partir desse depth o LLM já tentou desambiguar e
    // continua emitindo needs_clarification. Server-side força out_of_scope
    // mesmo que o prompt instrua o LLM corretamente — modelos médios erram
    // aritmética sob pressão de contexto.
    private const int LoopGuardDepthThreshold = 1;

    // State entre Pre e Post hook. Cada AgentFactory.CreateAgentAsync produz
    // uma nova cadeia — não há concorrência inter-turn no mesmo objeto.
    private string? _priorLastIntent;
    private int _priorClarificationDepth;

    public RouterDecisionTelemetryChatClient(
        IChatClient inner,
        string agentId,
        Dictionary<string, string>? settings,
        ILogger logger)
        : base(inner, agentId, settings ?? new Dictionary<string, string>(), logger)
    {
    }

    protected override Task<IEnumerable<ChatMessage>> OnBeforeRequestAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        CancellationToken ct)
    {
        // Lista é enumerable; só iteramos uma vez. Não materializa fora desse
        // scope (caller passou IEnumerable explícito; preservamos).
        foreach (var msg in messages)
        {
            if (msg.Role != ChatRole.System) continue;
            var text = msg.Text;
            if (string.IsNullOrEmpty(text)) continue;
            if (!TryExtractMemoryPayload(text, out var payload)) continue;
            ParsePriorState(payload);
            break;
        }
        return Task.FromResult(messages);
    }

    protected override Task<ChatResponse> OnAfterResponseAsync(
        ChatResponse response,
        CancellationToken ct)
    {
        var (assistantMsg, contentIndex, text) = FindAssistantText(response);
        if (assistantMsg is null || string.IsNullOrWhiteSpace(text))
            return Task.FromResult(response);

        ProcessOutput(text!, rewritten =>
        {
            if (rewritten is not null)
                assistantMsg.Contents[contentIndex] = new TextContent(rewritten);
        });

        return Task.FromResult(response);
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // OnBefore manual no streaming porque AgentMiddlewareBase só plugou
        // em GetResponseAsync. Padrão idêntico ao OperationalMemoryChatClient.
        var processedMessages = await OnBeforeRequestAsync(messages, options, cancellationToken)
            .ConfigureAwait(false);

        // Buffer streaming pra processar no fim — telemetria precisa do output
        // completo (LLM emite intent num só chunk JSON terminal em prática, mas
        // não dá pra confiar). TTFT já é dominado pelo OperationalMemoryChatClient
        // que também buferiza; este middleware empilha sem custo adicional.
        var buffer = new StringBuilder();
        var nonText = new List<AIContent>();
        ChatRole? lastRole = null;

        await foreach (var update in base.GetStreamingResponseAsync(processedMessages, options, cancellationToken))
        {
            lastRole ??= update.Role;
            foreach (var content in update.Contents)
            {
                if (content is TextContent tc) buffer.Append(tc.Text);
                else nonText.Add(content);
            }
        }

        var fullText = buffer.ToString();
        string? finalText = fullText;

        if (!string.IsNullOrWhiteSpace(fullText))
        {
            ProcessOutput(fullText, rewritten =>
            {
                if (rewritten is not null)
                    finalText = rewritten;
            });
        }

        var contents = new List<AIContent>(nonText.Count + (string.IsNullOrEmpty(finalText) ? 0 : 1));
        if (!string.IsNullOrEmpty(finalText))
            contents.Add(new TextContent(finalText));
        contents.AddRange(nonText);

        yield return new ChatResponseUpdate
        {
            Contents = contents,
            Role = lastRole ?? ChatRole.Assistant,
        };
    }

    /// <summary>
    /// Pipeline pós-LLM: parse → loop guard → validação → emit metrics →
    /// log → (rewrite se aplicável). <paramref name="applyRewrite"/> recebe o
    /// novo texto OU null quando não há rewrite.
    /// </summary>
    private void ProcessOutput(string rawText, Action<string?> applyRewrite)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(rawText);
        }
        catch (JsonException)
        {
            EmitSignal("parse_failure");
            applyRewrite(null);
            return;
        }

        if (root is not JsonObject obj)
        {
            EmitSignal("parse_failure");
            applyRewrite(null);
            return;
        }

        var intent = ReadStrictString(obj, "intent");
        var confidence = ReadDouble(obj, "confidence");
        var reason = ReadStrictString(obj, "reason") ?? string.Empty;
        var candidateIntents = obj["candidate_intents"] as JsonArray;
        var candidateCount = candidateIntents?.Count ?? 0;

        if (string.IsNullOrEmpty(intent))
        {
            // Output JSON mas sem campo intent — não é Router. Pode acontecer
            // quando admin atribui esse middleware a um agente não-Router.
            EmitSignal("parse_failure");
            applyRewrite(null);
            return;
        }

        var projectId = DelegateExecutor.Current.Value?.ProjectId ?? string.Empty;
        var conversationId = DelegateExecutor.Current.Value?.ConversationId ?? string.Empty;
        var executionId = DelegateExecutor.Current.Value?.ExecutionId ?? string.Empty;

        var isNeedsClarification = string.Equals(
            intent, SystemIntents.NeedsClarificationName, StringComparison.OrdinalIgnoreCase);

        // Loop guard server-side: LLM emitiu needs_clarification num turno
        // onde o estado anterior já era needs_clarification (depth >= 1).
        // Sinal claramente diferente de invalid_clarification (que é violação
        // de schema), pra que dashboards separem "LLM bugou" vs "ambiguidade
        // não resolvida em N tentativas".
        var isLoopGuardTriggered = isNeedsClarification
            && (string.Equals(_priorLastIntent, SystemIntents.NeedsClarificationName, StringComparison.OrdinalIgnoreCase)
                || _priorClarificationDepth >= LoopGuardDepthThreshold);

        // Hard validation: needs_clarification exige >= 2 candidatos.
        var isInvalidClarification = isNeedsClarification && candidateCount < 2;

        if (isLoopGuardTriggered)
        {
            EmitSignal("loop_guard_triggered");
            Logger.LogWarning(
                "[RouterTelemetry] {AgentId}: loop guard server-side acionado " +
                "(prior_intent={PriorIntent} prior_depth={PriorDepth}). " +
                "Rewrite => out_of_scope. conversation_id={ConvId}",
                AgentId, _priorLastIntent ?? "<null>", _priorClarificationDepth, conversationId);

            var rewritten = RewriteAsOutOfScope(
                obj,
                rewriteReason: "loop guard: ambiguidade não resolvida após tentativas",
                resetDepth: true,
                resetCandidates: true);
            intent = SystemIntents.OutOfScopeName;
            confidence = 0;
            reason = "loop guard triggered";
            EmitDecisionMetrics(intent, confidence, projectId);
            LogDecision(intent, confidence, reason, candidateCount: 0, conversationId, executionId, projectId);
            applyRewrite(rewritten);
            return;
        }

        if (isInvalidClarification)
        {
            EmitSignal("invalid_clarification");
            Logger.LogWarning(
                "[RouterTelemetry] {AgentId}: needs_clarification COM candidate_intents inválido " +
                "(count={Count}). Rewrite => out_of_scope. reason={Reason}",
                AgentId, candidateCount, Trim(reason, 200));

            // P1-E: preserva clarification_depth — não estamos no loop guard,
            // é só uma falha de schema isolada. Próximo turno reutiliza o
            // contador se LLM tentar needs_clarification de novo.
            var rewritten = RewriteAsOutOfScope(
                obj,
                rewriteReason: "schema violation: needs_clarification sem candidate_intents válido",
                resetDepth: false,
                resetCandidates: true);
            intent = SystemIntents.OutOfScopeName;
            confidence = 0;
            reason = "schema violation: needs_clarification sem candidate_intents válido";
            EmitDecisionMetrics(intent, confidence, projectId);
            LogDecision(intent, confidence, reason, candidateCount: 0, conversationId, executionId, projectId);
            applyRewrite(rewritten);
            return;
        }

        EmitDecisionMetrics(intent, confidence, projectId);
        EmitSignal(SignalFor(intent));
        LogDecision(intent, confidence, reason, candidateCount, conversationId, executionId, projectId);
        applyRewrite(null);
    }

    private void EmitDecisionMetrics(string intent, double confidence, string projectId)
    {
        MetricsRegistry.RouterDecisions.Add(1,
            new KeyValuePair<string, object?>("agent_id", AgentId),
            new KeyValuePair<string, object?>("intent", intent),
            new KeyValuePair<string, object?>("project_id", projectId));

        // Histograma só faz sentido com valor não-negativo. Quando rewrite zera
        // confidence (caso invalid_clarification/loop_guard), ainda registra 0
        // — útil pra ver baseline de "Router degradado" no histograma.
        if (confidence >= 0 && confidence <= 1)
        {
            MetricsRegistry.RouterConfidence.Record(confidence,
                new KeyValuePair<string, object?>("agent_id", AgentId),
                new KeyValuePair<string, object?>("intent", intent));
        }
    }

    private void EmitSignal(string signal)
    {
        MetricsRegistry.RouterAmbiguitySignals.Add(1,
            new KeyValuePair<string, object?>("signal", signal),
            new KeyValuePair<string, object?>("agent_id", AgentId));
    }

    private static string SignalFor(string intent)
    {
        if (string.Equals(intent, SystemIntents.NeedsClarificationName, StringComparison.OrdinalIgnoreCase))
            return "needs_clarification";
        if (string.Equals(intent, SystemIntents.OutOfScopeName, StringComparison.OrdinalIgnoreCase))
            return "out_of_scope";
        return "dominant";
    }

    private void LogDecision(
        string intent, double confidence, string reason,
        int candidateCount, string conversationId, string executionId, string projectId)
    {
        Logger.LogInformation(
            "[RouterTelemetry] decision agent_id={AgentId} intent={Intent} " +
            "confidence={Confidence:F3} candidate_intents_count={CandidateCount} " +
            "conversation_id={ConversationId} execution_id={ExecutionId} " +
            "project_id={ProjectId} reason={Reason}",
            AgentId, intent, confidence, candidateCount,
            conversationId, executionId, projectId, Trim(reason, 200));
    }

    /// <summary>
    /// Mutate o JsonObject pra forma <c>out_of_scope</c> canônica. Devolve o
    /// JSON serializado. Operação dual-purpose: usada por loop_guard e
    /// invalid_clarification (semânticas levemente diferentes — ver
    /// <paramref name="resetDepth"/>).
    /// </summary>
    private static string RewriteAsOutOfScope(
        JsonObject root,
        string rewriteReason,
        bool resetDepth,
        bool resetCandidates)
    {
        root["intent"] = SystemIntents.OutOfScopeName;
        root["confidence"] = 0;
        root["reason"] = rewriteReason;
        if (resetCandidates)
            root["candidate_intents"] = new JsonArray();

        if (root["operationalMemory"] is not JsonObject memory)
        {
            memory = new JsonObject();
            root["operationalMemory"] = memory;
        }

        memory["last_intent"] = SystemIntents.OutOfScopeName;
        memory["last_reason"] = Trim(rewriteReason, 200);
        if (resetDepth || !memory.ContainsKey("clarification_depth"))
            memory["clarification_depth"] = 0;
        // Se NÃO reset (invalid_clarification): preserva o depth que LLM
        // emitiu. Próximo turno o Router vê o depth e segue contagem.

        // last_candidate_intents sempre zera no rewrite — ciclo terminou
        // (out_of_scope) ou o LLM bugou; não há candidatos válidos a preservar.
        memory["last_candidate_intents"] = new JsonArray();

        return root.ToJsonString();
    }

    /// <summary>
    /// Extrai o JSON entre <c>&lt;operational_memory&gt;</c> e
    /// <c>&lt;/operational_memory&gt;</c>. Tags são injetadas pelo
    /// <c>OperationalMemoryChatClient</c> (mais externo no pipeline).
    /// </summary>
    private static bool TryExtractMemoryPayload(string systemMessage, out string payload)
    {
        payload = string.Empty;
        const string Open = "<operational_memory>";
        const string Close = "</operational_memory>";
        var openIdx = systemMessage.IndexOf(Open, StringComparison.Ordinal);
        if (openIdx < 0) return false;
        var closeIdx = systemMessage.IndexOf(Close, openIdx + Open.Length, StringComparison.Ordinal);
        if (closeIdx < 0) return false;
        payload = systemMessage.Substring(openIdx + Open.Length, closeIdx - openIdx - Open.Length).Trim();
        return payload.Length > 0;
    }

    /// <summary>
    /// Lê <c>last_intent</c> e <c>clarification_depth</c> do payload anterior
    /// pra alimentar o loop guard server-side. Falha silenciosa: payload
    /// vazio/malformado deixa o guard passivo (depth=0, last_intent=null).
    /// </summary>
    private void ParsePriorState(string payload)
    {
        if (payload == "{}") return;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return;
            if (doc.RootElement.TryGetProperty("last_intent", out var li)
                && li.ValueKind == JsonValueKind.String)
            {
                _priorLastIntent = li.GetString();
            }
            if (doc.RootElement.TryGetProperty("clarification_depth", out var cd)
                && cd.ValueKind == JsonValueKind.Number
                && cd.TryGetInt32(out var depth))
            {
                _priorClarificationDepth = depth;
            }
        }
        catch (JsonException)
        {
            // payload corrompido — segue com defaults
        }
    }

    private static (ChatMessage? Msg, int Index, string? Text) FindAssistantText(ChatResponse response)
    {
        foreach (var msg in response.Messages)
        {
            if (msg.Role != ChatRole.Assistant) continue;
            for (var i = 0; i < msg.Contents.Count; i++)
            {
                if (msg.Contents[i] is TextContent text && !string.IsNullOrWhiteSpace(text.Text))
                    return (msg, i, text.Text);
            }
        }
        return (null, -1, null);
    }

    /// <summary>
    /// Lê string do JsonObject. Pra defesa contra LLM bugado emitindo objeto
    /// onde deveria ser string (ex: <c>intent: {...}</c>), retorna null —
    /// caller trata como parse_failure. Evita high-cardinality blowup em tag
    /// de métrica (P2 do tech lead review).
    /// </summary>
    private static string? ReadStrictString(JsonObject obj, string key)
    {
        if (!obj.TryGetPropertyValue(key, out var node) || node is null) return null;
        if (node is not JsonValue val) return null;
        return val.TryGetValue<string>(out var s) ? s : null;
    }

    private static double ReadDouble(JsonObject obj, string key)
    {
        if (!obj.TryGetPropertyValue(key, out var node) || node is null) return 0;
        if (node is JsonValue val && val.TryGetValue<double>(out var d)) return d;
        return 0;
    }

    private static string Trim(string s, int maxLen) =>
        s.Length <= maxLen ? s : s.Substring(0, maxLen);
}
