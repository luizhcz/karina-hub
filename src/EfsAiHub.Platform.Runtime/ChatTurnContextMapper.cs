using System.Text.Json;
using EfsAiHub.Core.Abstractions.Conversations;
using EfsAiHub.Core.Orchestration.Enums;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;
using Microsoft.Extensions.AI;

namespace EfsAiHub.Platform.Runtime;

/// <summary>
/// Utilitário centralizado para converter ChatTurnContext em List&lt;ChatMessage&gt;.
/// Elimina a duplicação entre AgentFactory.TryExpandChatTurnContext e
/// WorkflowRunnerService.BuildInputMessages.
/// </summary>
public static class ChatTurnContextMapper
{
    private static readonly JsonSerializerOptions DefaultOpts =
        new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Tenta expandir um ChatTurnContext serializado em mensagens separadas para o LLM.
    /// Usado no Graph+Chat mode (single-agent) — sem o JSON blob como system message.
    /// Retorna null se o input não for um ChatTurnContext válido com metadata.
    /// </summary>
    public static List<AiChatMessage>? TryExpand(string? rawInput, JsonSerializerOptions? opts = null)
    {
        if (string.IsNullOrWhiteSpace(rawInput) || rawInput[0] != '{')
            return null;

        ChatTurnContext? ctx;
        try
        {
            ctx = JsonSerializer.Deserialize<ChatTurnContext>(rawInput, opts ?? DefaultOpts);
        }
        catch
        {
            return null;
        }

        // Só expande se tiver metadata (sinal de que é ChatTurnContext real)
        if (ctx is null || ctx.Metadata.Count == 0)
            return null;

        return BuildMessages(ctx);
    }

    /// <summary>
    /// Monta a lista de ChatMessages de acordo com o OrchestrationMode.
    /// Para Handoff/GroupChat: expande ChatTurnContext em mensagens reais (sem JSON blob duplicado).
    /// Para outros modos: retorna o JSON blob como mensagem User única.
    /// </summary>
    public static List<AiChatMessage> Build(string? rawInput, OrchestrationMode mode, JsonSerializerOptions? opts = null)
    {
        if (string.IsNullOrEmpty(rawInput))
            return [new(ChatRole.User, string.Empty)];

        // Graph/Sequential/Concurrent: manter JSON blob (ChatTriggerExecutor e DelegateExecutors)
        if (mode is OrchestrationMode.Graph or OrchestrationMode.Sequential or OrchestrationMode.Concurrent)
            return [new(ChatRole.User, rawInput)];

        // Handoff/GroupChat: expandir ChatTurnContext em mensagens reais
        try
        {
            var ctx = JsonSerializer.Deserialize<ChatTurnContext>(rawInput, opts ?? DefaultOpts);
            if (ctx is null)
                return [new(ChatRole.User, rawInput)];

            return BuildMessages(ctx);
        }
        catch
        {
            return [new(ChatRole.User, rawInput)];
        }
    }

    private static List<AiChatMessage> BuildMessages(ChatTurnContext ctx)
    {
        var messages = new List<AiChatMessage>();

        // 1. System message com metadata da sessão
        if (ctx.Metadata.Count > 0)
        {
            var parts = ctx.Metadata.Select(kv => $"{kv.Key}: {kv.Value}");
            messages.Add(new(ChatRole.System, $"Contexto da sessão:\n{string.Join("\n", parts)}"));
        }

        // 2. Shared state (agent drafts) — injetado como system message (formato compacto para economia de tokens)
        if (ctx.SharedState is { } state && state.ValueKind == JsonValueKind.Object)
        {
            try
            {
                var stateJson = state.GetRawText();
                messages.Add(new(ChatRole.System,
                    $"Estado compartilhado da conversa (agent drafts — dados coletados em turnos anteriores):\n{stateJson}"));
            }
            catch { /* state inválido — ignora silenciosamente */ }
        }

        // 3. Histórico como mensagens User/Assistant reais
        foreach (var msg in ctx.History)
        {
            var role = msg.Role.ToLowerInvariant() switch
            {
                "user" => ChatRole.User,
                "assistant" => ChatRole.Assistant,
                "system" => ChatRole.System,
                _ => ChatRole.User
            };

            var content = msg.Content;
            if (role == ChatRole.Assistant && msg.Output is { } output)
            {
                try { content = output.GetRawText(); }
                catch { /* manter content original */ }
            }

            messages.Add(new(role, content));
        }

        // 4. Mensagem atual do usuário
        messages.Add(new(ChatRole.User, ctx.Message.Content));

        return messages;
    }
}
