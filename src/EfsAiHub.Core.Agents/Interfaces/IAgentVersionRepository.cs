namespace EfsAiHub.Core.Agents;

/// <summary>
/// Repositório append-only de snapshots imutáveis de AgentVersion.
/// </summary>
public interface IAgentVersionRepository
{
    /// <summary>Retorna a versão por id (GUID). Null se não existir.</summary>
    Task<AgentVersion?> GetByIdAsync(string agentVersionId, CancellationToken ct = default);

    /// <summary>Retorna a versão atualmente apontada como Published mais recente do agente.</summary>
    Task<AgentVersion?> GetCurrentAsync(string agentDefinitionId, CancellationToken ct = default);

    /// <summary>Lista todas as versões de um agente ordenadas por Revision DESC.</summary>
    Task<IReadOnlyList<AgentVersion>> ListByDefinitionAsync(string agentDefinitionId, CancellationToken ct = default);

    /// <summary>
    /// Persiste um novo snapshot. Calcula a Revision automaticamente como MAX(Revision)+1
    /// dentro da mesma conexão/transação. Idempotente por ContentHash — se o hash já existe
    /// na última revision, retorna a existente (no-op).
    /// </summary>
    Task<AgentVersion> AppendAsync(AgentVersion version, CancellationToken ct = default);

    /// <summary>Retorna a próxima Revision disponível para um agente (MAX+1 ou 1).</summary>
    Task<int> GetNextRevisionAsync(string agentDefinitionId, CancellationToken ct = default);
}
