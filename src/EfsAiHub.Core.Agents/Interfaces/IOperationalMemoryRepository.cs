using EfsAiHub.Core.Agents.Memory;

namespace EfsAiHub.Core.Agents;

/// <summary>
/// Persistência de memória operacional dos agentes — strictamente owner-only via
/// query filter por ProjectId. Memória de project A é invisível pra project B
/// mesmo via combinação direta de (AgentId, ScopeType, ScopeId).
/// </summary>
public interface IOperationalMemoryRepository
{
    /// <summary>Busca a memória do escopo no project atual; null se inexistente.</summary>
    Task<OperationalMemoryRecord?> GetAsync(
        string agentId,
        string scopeType,
        string scopeId,
        CancellationToken ct = default);

    /// <summary>
    /// Lista memórias do agente no project atual ordenadas por UpdatedAt desc.
    /// Usado pelo endpoint admin de inspeção.
    /// </summary>
    Task<IReadOnlyList<OperationalMemoryRecord>> ListAsync(
        string agentId,
        CancellationToken ct = default);

    /// <summary>
    /// Insert-or-update com optimistic concurrency. <paramref name="expectedVersion"/>
    /// é null no primeiro write (insert), ou a Version lida pelo caller. Postgres
    /// aplica WHERE Version=@expected; 0 rows afetadas → <see cref="OperationalMemoryConcurrencyException"/>.
    /// Retorna a row persistida com Version bumped e timestamps atualizados.
    /// </summary>
    Task<OperationalMemoryRecord> UpsertAsync(
        OperationalMemoryRecord record,
        int? expectedVersion,
        CancellationToken ct = default);

    /// <summary>True se a memória existia e foi removida; false se não existia.</summary>
    Task<bool> DeleteAsync(
        string agentId,
        string scopeType,
        string scopeId,
        CancellationToken ct = default);
}

public sealed class OperationalMemoryConcurrencyException : Exception
{
    public OperationalMemoryConcurrencyException(string agentId, string scopeType, string scopeId)
        : base($"Memória operacional de '{agentId}' ({scopeType}/{scopeId}) foi modificada por outra requisição (Version divergente).")
    { }
}
