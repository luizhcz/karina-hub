
namespace EfsAiHub.Core.Agents;

public interface IAgentDefinitionRepository
{
    Task<AgentDefinition?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<AgentDefinition>> GetAllAsync(CancellationToken ct = default);
    /// <summary>
    /// Upsert da definição + auto-snapshot atomico em <c>agent_versions</c>.
    /// <paramref name="breakingChange"/>/<paramref name="changeReason"/>/<paramref name="createdBy"/>
    /// são propagados pra <c>AgentVersion.FromDefinition</c> quando o auto-snapshot é criado.
    /// Idempotência por ContentHash: re-upsert sem mudança no conteúdo retorna a version
    /// existente, ignorando metadata declarada pelo caller (versions são imutáveis).
    /// </summary>
    Task<AgentDefinition> UpsertAsync(
        AgentDefinition definition,
        CancellationToken ct = default,
        bool breakingChange = false,
        string? changeReason = null,
        string? createdBy = null,
        bool isCosmeticOnly = false);
    Task<bool> DeleteAsync(string id, CancellationToken ct = default);
    Task<bool> ExistsAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Retorna o subconjunto de IDs que existem no banco — query única para evitar N+1.
    /// </summary>
    Task<IReadOnlySet<string>> GetExistingIdsAsync(IEnumerable<string> ids, CancellationToken ct = default);

    /// <summary>
    /// Lista até <paramref name="limit"/> agents globais cujo project owner foi
    /// deletado (orphans). Bypass do query filter (cross-project + cross-tenant). Read-only,
    /// usado por health checks e operações admin.
    /// </summary>
    Task<IReadOnlyList<(string AgentId, string MissingProjectId)>> ListOrphanGlobalAgentsAsync(
        int limit = 20, CancellationToken ct = default);

    /// <summary>
    /// Popula as colunas <c>LastChatSandboxValidated*</c> em <c>agent_definitions</c>
    /// e invalida o cache do agent. Usado por <c>ChatSandboxService.ValidateAsync</c>.
    /// Retorna false se o agent não existe.
    /// </summary>
    Task<bool> SetChatSandboxValidationAsync(
        string agentId,
        DateTime validatedAt,
        string validatedByUserId,
        string validatedAgentVersionId,
        CancellationToken ct = default);

    /// <summary>
    /// Zera <c>LastChatSandboxValidated*</c> em <c>agent_definitions</c> e invalida
    /// o cache. Idempotente (no-op se já null). Retorna a versão pinada anterior
    /// (pra emissão de audit) ou null se já estava limpa.
    /// </summary>
    Task<string?> ClearChatSandboxValidationAsync(string agentId, CancellationToken ct = default);
}
