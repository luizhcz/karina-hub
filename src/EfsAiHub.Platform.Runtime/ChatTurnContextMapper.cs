using System.Text;
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
    ///
    /// <paramref name="userReinforcement"/>: quando não-null, é anexado à última
    /// mensagem do usuário (trailing). Usado para reforço curto de persona
    /// (combate lost-in-the-middle). Só opera em chamadas com ChatTurnContext
    /// expandido — inputs crus recebem o mesmo tratamento no caller.
    ///
    /// <paramref name="historyWindow"/>: quando setado e &gt; 0, fatia o
    /// histórico nas últimas N mensagens antes do append. Slice acontece aqui
    /// (não no ConversationService) pra que cada agente do mesmo workflow
    /// receba a janela apropriada — Router=5 enquanto Conversational mantém
    /// a janela global do workflow.
    /// </summary>
    public static List<AiChatMessage>? TryExpand(
        string? rawInput,
        string? userReinforcement = null,
        JsonSerializerOptions? opts = null,
        int? historyWindow = null)
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

        return BuildMessages(ctx, userReinforcement, historyWindow);
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

    private const string NullSentinel = "<null>";

    private static List<AiChatMessage> BuildMessages(
        ChatTurnContext ctx,
        string? userReinforcement = null,
        int? historyWindow = null)
    {
        var messages = new List<AiChatMessage>();

        // XML-delimited blocks isolam contexto do user input e ancoram o LLM
        // (Anthropic prompt-eng pattern) — também bloqueia injection via
        // valores que contenham marcadores como "##" ou "</shared_state>".
        if (ctx.Metadata.Count > 0)
        {
            var parts = ctx.Metadata.Select(kv => $"{kv.Key}: {SanitizeScalarString(kv.Value)}");
            messages.Add(new(ChatRole.System,
                $"<session_context>\n{string.Join("\n", parts)}\n</session_context>"));
        }

        if (ctx.SharedState is { } state && state.ValueKind == JsonValueKind.Object)
        {
            var rendered = RenderSharedStateForPrompt(state);
            if (rendered is not null)
                messages.Add(new(ChatRole.System, rendered));
        }

        IEnumerable<ChatTurnMessage> historySource = ctx.History;
        if (historyWindow is { } window && window > 0 && ctx.History.Count > window)
            historySource = ctx.History.Skip(ctx.History.Count - window);

        foreach (var msg in historySource)
        {
            var role = ResolveRole(msg.Role);

            var content = msg.Content;
            if (role == ChatRole.Assistant && msg.Output is { } output)
                content = ExtractAssistantContent(output, msg.Content);

            messages.Add(new(role, content));
        }

        // Reforço de persona ancorado no fim — last-token bias compensa
        // lost-in-the-middle quando o system prompt é longo.
        var userContent = string.IsNullOrWhiteSpace(userReinforcement)
            ? ctx.Message.Content
            : $"{ctx.Message.Content}\n\n{userReinforcement}";
        messages.Add(new(ChatRole.User, userContent));

        return messages;
    }

    private static ChatRole ResolveRole(string role) =>
        string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase) ? ChatRole.Assistant
        : string.Equals(role, "system", StringComparison.OrdinalIgnoreCase) ? ChatRole.System
        : ChatRole.User;

    /// <summary>
    /// Extrai conteúdo legível do output canônico Conversational
    /// <c>{output_type, output_status, message, output?}</c>. Detecção de
    /// canônico via presença de <c>output_type</c> ou <c>output_status</c>;
    /// nesse caso NUNCA cai pro raw JSON — ler "Qual conta?" em vez de
    /// "{output_type:'form',message:'Qual conta?',...}" é o que permite o
    /// Router reaproveitar contexto no próximo turn ("1234" continua a
    /// boleta em vez de virar out_of_scope).
    ///
    /// <para>
    /// <b>Marker semântico prefixado</b>: <c>output_status</c> não-terminal
    /// vira um marker em colchetes no INÍCIO do conteúdo (não sufixo). O
    /// Router tem regra dura sobre marker em <c>prompts/router.md</c>
    /// (bloco <c>multi_turn_classification</c>) — sinal machine-grade que
    /// reduz a chance de classificar resposta curta a pergunta como
    /// <c>needs_clarification</c>. Status terminal (<c>default/done/completed</c>)
    /// não emite marker — flui como resposta normal.
    /// </para>
    /// </summary>
    private static string ExtractAssistantContent(JsonElement output, string fallback)
    {
        if (output.ValueKind != JsonValueKind.Object)
            return TryGetRawText(output) ?? fallback;

        var isCanonical = output.TryGetProperty("output_type", out _)
            || output.TryGetProperty("output_status", out _);

        if (!isCanonical)
            return TryGetRawText(output) ?? fallback;

        string? text = null;
        if (output.TryGetProperty("message", out var msgField)
            && msgField.ValueKind == JsonValueKind.String)
        {
            var s = msgField.GetString();
            if (!string.IsNullOrWhiteSpace(s)) text = s;
        }

        string? marker = null;
        if (output.TryGetProperty("output_status", out var statusField)
            && statusField.ValueKind == JsonValueKind.String)
        {
            marker = MarkerForStatus(statusField.GetString());
        }

        if (text is null) return marker ?? fallback;
        return marker is null ? text : $"{marker} {text}";
    }

    /// <summary>
    /// De-para semântico: <c>output_status</c> → marker prefixado pro Router.
    /// Taxonomia minimalista em 3 buckets com viés decrescente pra continuação:
    ///
    /// <list type="bullet">
    ///   <item><b>INCOMPLETE</b> (<c>incomplete</c>): intenção em progresso —
    ///   Router DEVE priorizar continuação, mesmo confirmando com o user.</item>
    ///   <item><b>AMBIGUOUS</b> (<c>error/none/default/text</c> + status ausente):
    ///   agente esperando input válido ou desambiguação dentro da intenção atual.
    ///   Continuação ainda é a leitura padrão; viés médio-alto.</item>
    ///   <item><b>DONE</b> (qualquer outro status): intenção fechou — Router
    ///   classifica pelo conteúdo da mensagem, continuação só se claramente
    ///   indicado pelo user.</item>
    /// </list>
    ///
    /// Sempre emite marker (não há "sem marker") — toda resposta de
    /// Conversational viaja com sinal explícito pro Router, mesmo as terminais.
    /// </summary>
    private static string? MarkerForStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return "[ASSISTANT-AMBIGUOUS]";

        var s = status.Trim().ToLowerInvariant();

        if (s == "incomplete") return "[ASSISTANT-INCOMPLETE]";
        if (s is "error" or "none" or "default" or "text") return "[ASSISTANT-AMBIGUOUS]";
        return "[ASSISTANT-DONE]";
    }

    private static string? TryGetRawText(JsonElement element)
    {
        try { return element.GetRawText(); }
        catch { return null; }
    }

    /// <summary>
    /// Renderiza <c>SharedState</c> como bloco <c>&lt;shared_state&gt;</c>.
    /// Shape esperado <c>{ "agents": { "&lt;stateKey&gt;": &lt;draft&gt; } }</c>
    /// (produzido pelo <c>StructuredOutputStateChatClient</c>). Cada agente
    /// vira section <c>## &lt;agentId&gt; — status: &lt;status&gt;</c> com
    /// corpo do <c>output</c> em key:value. Drafts canônicos sem
    /// <c>output</c> não iteram os meta-keys do wrapper (output_type etc) —
    /// só status no header. Nulls viram <c>&lt;null&gt;</c> em vez de
    /// <c>(vazio)</c> pra evitar colisão com strings literais em PT-BR.
    /// </summary>
    private static string? RenderSharedStateForPrompt(JsonElement state)
    {
        if (state.ValueKind != JsonValueKind.Object) return null;
        if (!state.TryGetProperty("agents", out var agents)
            || agents.ValueKind != JsonValueKind.Object) return null;

        var inner = new StringBuilder();
        var first = true;
        foreach (var entry in agents.EnumerateObject())
        {
            var draft = entry.Value;
            if (draft.ValueKind != JsonValueKind.Object) continue;

            var body = new StringBuilder();
            var isCanonical = draft.TryGetProperty("output_type", out _)
                || draft.TryGetProperty("output_status", out _);

            string? status = null;
            if (isCanonical
                && draft.TryGetProperty("output_status", out var statusField)
                && statusField.ValueKind == JsonValueKind.String)
            {
                status = statusField.GetString();
            }

            if (isCanonical)
            {
                if (draft.TryGetProperty("output", out var outputField))
                    RenderBody(body, outputField, indent: 0);
            }
            else
            {
                RenderBody(body, draft, indent: 0);
            }

            if (body.Length == 0 && string.IsNullOrEmpty(status)) continue;

            if (!first) inner.Append("\n\n");
            first = false;
            inner.Append("## ").Append(SanitizeScalarString(entry.Name));
            if (!string.IsNullOrEmpty(status))
                inner.Append(" — status: ").Append(SanitizeScalarString(status));
            inner.Append('\n');
            inner.Append(body);
        }

        if (inner.Length == 0) return null;
        return $"<shared_state>\n{inner}</shared_state>";
    }

    private static void RenderBody(StringBuilder sb, JsonElement value, int indent)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            AppendIndent(sb, indent);
            AppendScalar(sb, value);
            sb.Append('\n');
            return;
        }

        foreach (var prop in value.EnumerateObject())
        {
            AppendIndent(sb, indent);
            sb.Append(prop.Name).Append(':');
            var v = prop.Value;

            switch (v.ValueKind)
            {
                case JsonValueKind.Object when indent < 1:
                    sb.Append('\n');
                    RenderBody(sb, v, indent + 1);
                    break;
                case JsonValueKind.Object:
                    // Limite de recursão: ≥2 níveis colapsa pra inline key:value.
                    // Mais profundo que isso é UX smell (boleta com endereço
                    // dentro de cliente dentro de operação) — JSON-ish line é
                    // aceitável e evita indent runaway.
                    sb.Append(' ');
                    AppendObjectInline(sb, v);
                    sb.Append('\n');
                    break;
                case JsonValueKind.Array:
                    AppendArray(sb, v, indent + 1);
                    break;
                default:
                    sb.Append(' ');
                    AppendScalar(sb, v);
                    sb.Append('\n');
                    break;
            }
        }
    }

    private static void AppendArray(StringBuilder sb, JsonElement arr, int indent)
    {
        var any = false;
        foreach (var item in arr.EnumerateArray())
        {
            if (!any) { sb.Append('\n'); any = true; }
            AppendIndent(sb, indent);
            sb.Append("- ");
            if (item.ValueKind == JsonValueKind.Object)
                AppendObjectInline(sb, item);
            else
                AppendScalar(sb, item);
            sb.Append('\n');
        }
        if (!any) sb.Append(" []\n");
    }

    private static void AppendObjectInline(StringBuilder sb, JsonElement obj)
    {
        var first = true;
        foreach (var prop in obj.EnumerateObject())
        {
            if (!first) sb.Append(", ");
            first = false;
            sb.Append(prop.Name).Append(": ");
            AppendScalar(sb, prop.Value);
        }
    }

    private static void AppendScalar(StringBuilder sb, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                sb.Append(SanitizeScalarString(value.GetString() ?? string.Empty));
                break;
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                sb.Append(value.GetRawText());
                break;
            case JsonValueKind.Null:
                sb.Append(NullSentinel);
                break;
            case JsonValueKind.Object:
            case JsonValueKind.Array:
                // Fallback inline pra nesting que ultrapassa o limite — vira
                // raw JSON saneado em vez de quebrar a render.
                sb.Append(SanitizeScalarString(value.GetRawText()));
                break;
        }
    }

    /// <summary>
    /// Bloqueia injection de marcador via valor de campo: colapsa newlines
    /// (sem isso um campo poderia injetar "\n## fake-agent" forjando bloco)
    /// e escapa "##" no início pela mesma razão. Sem cap de tamanho — em
    /// fluxos financeiros é melhor estourar contexto e falhar visível do que
    /// truncar dado de negócio sem rastro.
    /// </summary>
    private static string SanitizeScalarString(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        if (s.IndexOfAny(['\n', '\r']) >= 0)
            s = s.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
        if (s.StartsWith("##", StringComparison.Ordinal))
            s = "\\" + s;
        return s;
    }

    private static void AppendIndent(StringBuilder sb, int indent)
    {
        for (var i = 0; i < indent; i++) sb.Append("  ");
    }
}
