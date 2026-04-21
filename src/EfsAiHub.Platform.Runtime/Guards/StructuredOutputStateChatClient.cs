using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using EfsAiHub.Core.Agents.Middlewares;
using EfsAiHub.Core.Orchestration.Executors;

namespace EfsAiHub.Platform.Runtime.Middlewares;

/// <summary>
/// Middleware que intercepta a resposta do agente e, se for JSON válido (structured output),
/// atualiza automaticamente o shared state da conversa via AG-UI STATE_DELTA.
///
/// Substitui a necessidade de o LLM chamar a tool <c>update_state</c> manualmente:
/// - Zero tokens extras (não precisa de tool call)
/// - Execução garantida (determinístico, não depende do LLM)
/// - Real-time: o SSE STATE_DELTA é emitido assim que a resposta chega
///
/// Ativação: middleware type "StructuredOutputState" na definição do agente.
/// Settings opcionais:
///   - "stateKey": chave no state (default: agentId). Ex: "coletor-boleta"
/// </summary>
public class StructuredOutputStateChatClient : AgentMiddlewareBase
{
    private readonly string _stateKey;

    public StructuredOutputStateChatClient(
        IChatClient innerClient,
        string agentId,
        Dictionary<string, string>? settings,
        ILogger logger)
        : base(innerClient, agentId, settings ?? new Dictionary<string, string>(), logger)
    {
        _stateKey = settings?.GetValueOrDefault("stateKey") ?? agentId;
    }

    protected override async Task<ChatResponse> OnAfterResponseAsync(
        ChatResponse response,
        CancellationToken ct)
    {
        await TryUpdateState(ExtractText(response));
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var buffer = new StringBuilder();

        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            if (update.Text is not null)
                buffer.Append(update.Text);

            yield return update;
        }

        await TryUpdateState(buffer.ToString());
    }

    private async Task TryUpdateState(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var ctx = DelegateExecutor.Current.Value;
        if (ctx?.UpdateSharedState is null || ctx.ConversationId is null)
            return;

        JsonElement value;
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
                return;

            value = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return;
        }

        var path = $"agents/{_stateKey}";

        try
        {
            await ctx.UpdateSharedState(path, value);
            Logger.LogDebug(
                "[StructuredOutputState] Agent '{AgentId}': state updated at '{Path}'.",
                AgentId, path);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex,
                "[StructuredOutputState] Agent '{AgentId}': failed to update state at '{Path}'.",
                AgentId, path);
        }
    }

    private static string? ExtractText(ChatResponse response)
    {
        foreach (var msg in response.Messages)
        {
            if (msg.Role != ChatRole.Assistant) continue;
            var text = msg.Text;
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }
        return null;
    }
}
