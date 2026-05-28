using EfsAiHub.Core.Agents.GenericTools;

namespace EfsAiHub.Platform.Runtime.Interfaces;

/// <summary>
/// Aplicação acima de <see cref="EfsAiHub.Core.Agents.IGenericToolRepository"/>:
/// gera Id quando ausente, força ProjectId/TenantId do contexto, valida invariantes
/// e timeout ceiling, normaliza GET → InputContentType=None, canonicaliza schemas
/// de input/output via <c>ISchemaNormalizer</c> antes de persistir.
/// </summary>
public interface IGenericToolService
{
    Task<GenericToolSaveResult> CreateAsync(string? id, GenericTool draft, CancellationToken ct = default);

    Task<GenericTool?> GetAsync(string id, CancellationToken ct = default);

    Task<IReadOnlyList<GenericTool>> ListAsync(CancellationToken ct = default);

    /// <summary>
    /// Atualiza tool existente com optimistic concurrency via <paramref name="expectedUpdatedAt"/>.
    /// Owner gate por ProjectId. Re-roda invariantes + ceiling + normalização.
    /// </summary>
    Task<GenericToolSaveResult> UpdateAsync(
        string id,
        GenericTool patch,
        DateTime expectedUpdatedAt,
        CancellationToken ct = default);

    Task DeleteAsync(string id, CancellationToken ct = default);
}
