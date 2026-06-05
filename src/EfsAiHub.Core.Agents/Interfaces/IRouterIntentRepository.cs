using EfsAiHub.Core.Agents.RouterIntents;

namespace EfsAiHub.Core.Agents;

/// <summary>
/// Persistência do pool global de Router intents — escopo tenant
/// (cross-project) via query filter por TenantId. Qualquer projeto do tenant
/// vê e referencia as mesmas intents.
/// </summary>
public interface IRouterIntentRepository
{
    /// <summary>Busca intent pelo Id no scope do tenant atual; null se não existir.</summary>
    Task<RouterIntent?> GetByIdAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Hidrata um set de intents pelos seus Ids no scope do tenant atual.
    /// Resultado preserva a ordem do <paramref name="ids"/>; Ids não encontrados
    /// são silenciosamente omitidos. Usado pelo composer pra resolver
    /// <c>AgentDefinition.RouterIntentIds</c> no save sem depender da tabela
    /// de link (que é populada depois do save).
    /// </summary>
    Task<IReadOnlyList<RouterIntent>> GetByIdsAsync(IReadOnlyList<string> ids, CancellationToken ct = default);

    /// <summary>Lista intents do tenant atual ordenadas por UpdatedAt desc.</summary>
    Task<IReadOnlyList<RouterIntent>> ListAsync(CancellationToken ct = default);

    /// <summary>True se já existe intent com mesmo Name no tenant. Usado pra validar unique constraint.</summary>
    Task<bool> NameExistsAsync(string name, string? excludeId, CancellationToken ct = default);

    /// <summary>Insere uma nova intent. Lança quando o Id já existir.</summary>
    Task<RouterIntent> CreateAsync(RouterIntent intent, CancellationToken ct = default);

    /// <summary>Atualiza intent. Lança quando não encontrada no tenant atual.</summary>
    Task<RouterIntent> UpdateAsync(RouterIntent intent, CancellationToken ct = default);

    /// <summary>True se intent existia e foi removida; false se não existia.</summary>
    Task<bool> DeleteAsync(string id, CancellationToken ct = default);
}
