using EfsAiHub.Core.Agents;

namespace EfsAiHub.Host.Worker.Services;

/// <summary>
/// Fonte única da regra "este output de agente é um TURNO de transcript da
/// conversa?". Um turno é persistido em <c>aihub.chat_messages</c> e replayado
/// como histórico pro LLM; o que não é turno fica fora do transcript.
///
/// O Router classifica a intenção (decisão de controle interna) — não fala com
/// o usuário, então seu output não é turno e não entra no transcript. Ele
/// permanece em <c>node_executions</c>, <c>llm_invocation_log</c> e nos eventos
/// AG-UI <c>STEP_*</c>, que são os lares corretos da decisão de roteamento.
///
/// Denylist (não allowlist): papéis desconhecidos contam como turno pra não
/// derrubar mensagens legítimas; papéis internos futuros entram aqui.
/// </summary>
public static class ChatTranscriptPolicy
{
    // nome do enum (não string mágica) — quebra em compile-time se o tipo mudar.
    private static readonly string RouterRole = nameof(AgentType.Router);

    /// <param name="agentRole">
    /// <see cref="AgentNodeInfo.Type"/> do agente produtor (string do
    /// <see cref="AgentType"/>); null quando o papel não pôde ser resolvido.
    /// </param>
    public static bool ProducesTranscriptTurn(string? agentRole) =>
        !string.Equals(agentRole, RouterRole, StringComparison.OrdinalIgnoreCase);
}
