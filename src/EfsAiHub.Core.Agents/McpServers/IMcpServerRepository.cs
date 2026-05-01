namespace EfsAiHub.Core.Agents.McpServers;

/// <summary>
/// Repositório de MCP servers. Project-scoped: queries aplicam HasQueryFilter via
/// IProjectContextAccessor. Não mantém histórico de versões (append-only) —
/// auditoria de mudanças vive em <c>aihub.admin_audit_log</c>.
/// </summary>
public interface IMcpServerRepository
{
    /// <summary>Busca por Id no projeto atual. Retorna null se não encontrado.</summary>
    Task<McpServer?> GetByIdAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Phase 3 — Busca MCP server bypassing project query filter, restrita ao
    /// project dono. Uso exclusivo de cross-project resolution (agent global
    /// referenciando MCP local do owner). Retorna null se MCP não existe ou
    /// não pertence ao owner.
    /// </summary>
    Task<McpServer?> GetByIdForOwnerAsync(string id, string ownerProjectId, CancellationToken ct = default);

    /// <summary>Lista paginada de MCP servers do projeto atual, ordenados por Nome.</summary>
    Task<IReadOnlyList<McpServer>> GetAllAsync(int page, int pageSize, CancellationToken ct = default);

    /// <summary>Conta total de MCP servers no projeto atual.</summary>
    Task<int> CountAsync(CancellationToken ct = default);

    /// <summary>
    /// Upsert do MCP server — cria se Id não existe, atualiza se existe. Atualiza
    /// <c>UpdatedAt</c> automaticamente. O controller emite <c>admin_audit_log</c>.
    /// </summary>
    Task<McpServer> UpsertAsync(McpServer server, CancellationToken ct = default);

    /// <summary>
    /// Remove o MCP server. Não cascateia — agents que referenciam via McpServerId
    /// ficam com tool "dangling" (provider loga warning e pula a tool em runtime).
    /// </summary>
    Task<bool> DeleteAsync(string id, CancellationToken ct = default);
}
