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
    /// na última revision, retorna a existente (no-op). Valida invariantes via
    /// <see cref="AgentVersion.EnsureInvariants"/> antes de persistir.
    /// </summary>
    Task<AgentVersion> AppendAsync(AgentVersion version, CancellationToken ct = default);

    /// <summary>Retorna a próxima Revision disponível para um agente (MAX+1 ou 1).</summary>
    Task<int> GetNextRevisionAsync(string agentDefinitionId, CancellationToken ct = default);

    /// <summary>
    /// Retorna a primeira version com <c>BreakingChange=TRUE</c> no intervalo
    /// <c>(fromRevisionExclusive, toRevisionInclusive]</c>. Versions com
    /// <c>BreakingChange=NULL</c> (legacy) NÃO são consideradas — caller decide
    /// como tratar (default conservador: assume breaking ao ver null em outras camadas).
    /// Retorna null se não há breaking no intervalo.
    /// </summary>
    Task<AgentVersion?> GetAncestorBreakingAsync(
        string agentDefinitionId,
        int fromRevisionExclusive,
        int toRevisionInclusive,
        CancellationToken ct = default);

    /// <summary>
    /// Resolve a version efetiva pra execução considerando patch propagation:
    /// <list type="bullet">
    ///   <item>Pin == current → retorna current.</item>
    ///   <item>Pin é ancestor de current SEM breaking entre eles → retorna current (patch propaga).</item>
    ///   <item>Pin é ancestor de current COM breaking entre eles → retorna o snapshot pinado (caller fica preso).</item>
    ///   <item>Pin > current (raro/odd) → retorna pin (snapshot pinado é mais novo).</item>
    /// </list>
    /// Lança <see cref="System.InvalidOperationException"/> quando o pin não existe ou não pertence ao agent.
    /// </summary>
    Task<AgentVersion> ResolveEffectiveAsync(
        string agentDefinitionId,
        string pinnedVersionId,
        CancellationToken ct = default);

    /// <summary>
    /// Lista AgentVersions cujo <c>AgentDefinitionId</c> não tem mais row correspondente
    /// em <c>aihub.agent_definitions</c> (orphan). Limitado a <paramref name="limit"/>
    /// pra não inflar payload de health check. Retorna pares (versionId, agentDefinitionId).
    /// </summary>
    Task<IReadOnlyList<(string AgentVersionId, string AgentDefinitionId)>> ListOrphanVersionsAsync(
        int limit = 50,
        CancellationToken ct = default);
}
