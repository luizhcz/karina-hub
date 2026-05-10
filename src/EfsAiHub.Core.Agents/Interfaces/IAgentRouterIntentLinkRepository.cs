using EfsAiHub.Core.Agents.RouterIntents;

namespace EfsAiHub.Core.Agents;

/// <summary>
/// Junction <c>(AgentId, IntentId)</c> — set de intents que cada Router
/// agent atende. Filter por ProjectId do agent (junction é per-Router/
/// per-projeto). Lookup do nome/descrição da intent referida vai à tabela
/// <c>router_intents</c> filtrada por TenantId.
/// </summary>
public interface IAgentRouterIntentLinkRepository
{
    /// <summary>Lista as intents (resolvidas com nome/descrição/projeto) que o agent atende.</summary>
    Task<IReadOnlyList<RouterIntent>> ListIntentsForAgentAsync(string agentId, CancellationToken ct = default);

    /// <summary>Lista só os IntentIds — útil pra hidratar DTOs sem JOIN extra.</summary>
    Task<IReadOnlyList<string>> ListIntentIdsForAgentAsync(string agentId, CancellationToken ct = default);

    /// <summary>Conta intents referenciadas pelo agent. Usado em <c>ValidateRouter</c> (>=2).</summary>
    Task<int> CountForAgentAsync(string agentId, CancellationToken ct = default);

    /// <summary>
    /// Lista os agentes que referenciam essa intent. Usado pra preview de
    /// delete-blocking (UI mostra qual Router precisa ser editado primeiro).
    /// </summary>
    Task<IReadOnlyList<RouterIntentUsage>> ListAgentsForIntentAsync(string intentId, CancellationToken ct = default);

    /// <summary>
    /// Reconcilia o set do agent: DELETE de todas as refs atuais + INSERT do
    /// novo set, atomicamente. Idempotente — chamada repetida com mesmo set
    /// não muda estado. Caller deve garantir que os IntentIds existem no
    /// tenant antes (FK só cobre existência, não pertencimento ao tenant).
    /// </summary>
    Task SetIntentsForAgentAsync(
        string agentId,
        string projectId,
        string tenantId,
        IReadOnlyList<string> intentIds,
        CancellationToken ct = default);
}

public sealed record RouterIntentUsage(string AgentId, string AgentName, string ProjectId);
