using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Agents.Memory;
using EfsAiHub.Core.Agents.Middlewares;
using EfsAiHub.Core.Orchestration.Executors;
using EfsAiHub.Infra.Observability;
using Microsoft.Extensions.AI;

namespace EfsAiHub.Platform.Runtime.Middlewares;

/// <summary>
/// Memória operacional persistida por (ProjectId, AgentId, escopo). Pré-call:
/// carrega o estado canônico e injeta como mensagem system com marcadores
/// <c>&lt;operational_memory&gt;...&lt;/operational_memory&gt;</c>. Pós-call:
/// extrai o campo <c>operationalMemory</c> do JSON de saída, persiste com
/// concorrência otimista (replace puro) e reescreve o output sem o campo —
/// caller só vê a resposta normal do agente.
///
/// Em streaming, bufferiza todos os updates pra fazer parse + persist + strip
/// no fim e emite um único <see cref="ChatResponseUpdate"/>. TTFT iguala o do
/// non-streaming — trade-off explícito da feature.
/// </summary>
public sealed class OperationalMemoryChatClient : AgentMiddlewareBase
{
    private const string MemoryFieldName = "operationalMemory";
    private const string OpenMarker = "<operational_memory>";
    private const string CloseMarker = "</operational_memory>";

    // Preamble de plataforma — invariante entre todos os agentes com memória
    // operacional. Captura três regras que o schema strict não consegue: replace
    // total (não delta), preservação de campos não-mencionados, e que o campo
    // não é exposto ao caller. Inglês formal pra coerência com prompts da
    // plataforma e independência de locale do agente.
    private const string Preamble =
        "You have persistent operational memory maintained by the platform across turns. " +
        "The current state is supplied below within <operational_memory>...</operational_memory>. " +
        "On every response, set the `operationalMemory` field to the COMPLETE updated state " +
        "(full replacement, not a delta). Preserve prior values unless the user explicitly changes them. " +
        "This field is internal and not exposed to the caller.";

    private readonly IOperationalMemoryRepository _repo;
    private readonly int _maxBytes;

    // State entre Pre e Post hook. Cada AgentFactory.CreateAgentAsync produz
    // uma nova cadeia, portanto cada turn tem instância nova — não há
    // concorrência inter-turn no mesmo objeto.
    private string? _projectId;
    private string? _scopeType;
    private string? _scopeId;
    private int? _expectedVersion;

    public OperationalMemoryChatClient(
        IChatClient inner,
        string agentId,
        IOperationalMemoryRepository repo,
        int maxBytes,
        ILogger logger)
        : base(inner, agentId, new Dictionary<string, string>(), logger)
    {
        _repo = repo;
        _maxBytes = maxBytes <= 0 ? AgentOperationalMemoryDefinition.DefaultMaxBytes : maxBytes;
    }

    protected override async Task<IEnumerable<ChatMessage>> OnBeforeRequestAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        CancellationToken ct)
    {
        var ctx = DelegateExecutor.Current.Value;
        if (ctx is null || string.IsNullOrEmpty(ctx.ProjectId))
        {
            EmitEvent("no_context");
            Logger.LogDebug("[OperationalMemory] {AgentId}: sem ProjectId no ExecutionContext — no-op.", AgentId);
            return messages;
        }

        var (scopeType, scopeId) = ResolveScope(ctx);
        if (string.IsNullOrEmpty(scopeId))
        {
            EmitEvent("no_context");
            Logger.LogDebug("[OperationalMemory] {AgentId}: sem ConversationId/ExecutionId — no-op.", AgentId);
            return messages;
        }

        OperationalMemoryRecord? record = null;
        try
        {
            record = await _repo.GetAsync(AgentId, scopeType, scopeId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex,
                "[OperationalMemory] {AgentId}: falha ao carregar memória ({Scope}/{ScopeId}) — segue com {{}}.",
                AgentId, scopeType, scopeId);
        }

        _projectId = ctx.ProjectId;
        _scopeType = scopeType;
        _scopeId = scopeId;
        _expectedVersion = record?.Version;

        var payloadJson = record is null ? "{}" : record.Payload.GetRawText();
        EmitEvent(record is null ? "miss" : "hit");

        // Preamble vem antes do bloco de memória pra que o LLM leia o contrato
        // ANTES de enxergar o payload — reduz risco de tratar o bloco como
        // descartável ou duplicar conteúdo no campo `response`.
        var preambleMsg = new ChatMessage(ChatRole.System, Preamble);
        var memoryMsg = new ChatMessage(
            ChatRole.System,
            $"{OpenMarker}\n{payloadJson}\n{CloseMarker}");

        var enriched = messages.ToList();
        enriched.Add(preambleMsg);
        enriched.Add(memoryMsg);
        return enriched;
    }

    protected override async Task<ChatResponse> OnAfterResponseAsync(
        ChatResponse response,
        CancellationToken ct)
    {
        if (!IsScopeReady()) return response;

        var (assistantMsg, contentIndex, originalText) = FindAssistantText(response);
        if (assistantMsg is null || originalText is null)
        {
            EmitEvent("no_op_tool_call");
            return response;
        }

        var (strippedText, persistJson) = TryExtractAndStrip(originalText);
        if (persistJson is null) return response;

        await TryPersistAsync(persistJson, ct).ConfigureAwait(false);

        // Substitui o conteúdo da mensagem original — preserva ordem e demais
        // contents (function calls, usage data) intactos.
        assistantMsg.Contents[contentIndex] = new TextContent(strippedText);
        EmitEvent("strip");

        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var processedMessages = await OnBeforeRequestAsync(messages, options, cancellationToken)
            .ConfigureAwait(false);

        var nonTextContents = new List<AIContent>();
        var textBuffer = new StringBuilder();
        ChatRole? lastRole = null;

        await foreach (var update in base.GetStreamingResponseAsync(processedMessages, options, cancellationToken))
        {
            lastRole ??= update.Role;
            foreach (var content in update.Contents)
            {
                if (content is TextContent tc) textBuffer.Append(tc.Text);
                else nonTextContents.Add(content);
            }
        }

        EmitEvent("streaming_buffered");

        var fullText = textBuffer.ToString();
        var finalText = fullText;

        if (IsScopeReady() && !string.IsNullOrWhiteSpace(fullText))
        {
            var (stripped, persistJson) = TryExtractAndStrip(fullText);
            if (persistJson is not null)
            {
                await TryPersistAsync(persistJson, cancellationToken).ConfigureAwait(false);
                finalText = stripped;
                EmitEvent("strip");
            }
        }

        var contents = new List<AIContent>(nonTextContents.Count + (string.IsNullOrEmpty(finalText) ? 0 : 1));
        if (!string.IsNullOrEmpty(finalText))
            contents.Add(new TextContent(finalText));
        contents.AddRange(nonTextContents);

        yield return new ChatResponseUpdate
        {
            Contents = contents,
            Role = lastRole ?? ChatRole.Assistant,
        };
    }

    /// <summary>
    /// Resolve escopo da memória pelo ExecutionContext: chat usa
    /// <c>ConversationId</c>; sandbox/standalone cai em <c>ExecutionId</c>.
    /// </summary>
    private static (string ScopeType, string ScopeId) ResolveScope(EfsAiHub.Core.Agents.Execution.ExecutionContext ctx)
    {
        if (!string.IsNullOrEmpty(ctx.ConversationId))
            return ("conversation", ctx.ConversationId);
        return ("session", ctx.ExecutionId ?? string.Empty);
    }

    private bool IsScopeReady() =>
        _projectId is not null && _scopeType is not null && _scopeId is not null;

    /// <summary>
    /// Localiza primeira <see cref="TextContent"/> de mensagem assistant. Tool
    /// calls (sem texto JSON) retornam null — caller faz no-op.
    /// </summary>
    private static (ChatMessage? Msg, int ContentIndex, string? Text) FindAssistantText(ChatResponse response)
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
    /// Parseia <paramref name="originalText"/> como JSON object, extrai
    /// <c>operationalMemory</c> e retorna (textoSemCampo, jsonDoCampo). Quando
    /// o output não é JSON object ou não tem o campo, devolve (originalText,
    /// null) — caller no-op.
    /// </summary>
    private (string Stripped, string? PersistJson) TryExtractAndStrip(string originalText)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(originalText);
        }
        catch (JsonException)
        {
            EmitEvent("parse_failure");
            return (originalText, null);
        }

        if (root is not JsonObject obj || !obj.ContainsKey(MemoryFieldName))
            return (originalText, null);

        var memNode = obj[MemoryFieldName];
        var memJson = memNode?.ToJsonString() ?? "{}";

        // Cap de tamanho aplicado em UTF-8 bytes — o que vai pro banco/prompt.
        if (Encoding.UTF8.GetByteCount(memJson) > _maxBytes)
        {
            EmitEvent("size_exceeded");
            Logger.LogWarning(
                "[OperationalMemory] {AgentId}: payload acima de {MaxBytes} bytes — write rejeitada.",
                AgentId, _maxBytes);
            return (originalText, null);
        }

        obj.Remove(MemoryFieldName);
        return (obj.ToJsonString(), memJson);
    }

    private async Task TryPersistAsync(string memJson, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(memJson);
            var record = new OperationalMemoryRecord
            {
                ProjectId = _projectId!,
                AgentId = AgentId,
                ScopeType = _scopeType!,
                ScopeId = _scopeId!,
                Payload = doc.RootElement.Clone(),
                Version = _expectedVersion ?? 0,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };

            await _repo.UpsertAsync(record, _expectedVersion, ct).ConfigureAwait(false);
            EmitEvent("write");
        }
        catch (OperationalMemoryConcurrencyException ex)
        {
            EmitEvent("concurrency_conflict");
            Logger.LogWarning(ex,
                "[OperationalMemory] {AgentId}: conflito de concorrência ({Scope}/{ScopeId}) — last-write-wins.",
                AgentId, _scopeType, _scopeId);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex,
                "[OperationalMemory] {AgentId}: falha ao persistir ({Scope}/{ScopeId}).",
                AgentId, _scopeType, _scopeId);
        }
    }

    private void EmitEvent(string eventType) =>
        MetricsRegistry.OperationalMemoryEvents.Add(1,
            new KeyValuePair<string, object?>("event", eventType),
            new KeyValuePair<string, object?>("agent_id", AgentId));
}
