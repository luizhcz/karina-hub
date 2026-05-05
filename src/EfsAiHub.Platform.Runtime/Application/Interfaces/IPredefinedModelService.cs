using EfsAiHub.Core.Agents.PredefinedModels;

namespace EfsAiHub.Platform.Runtime.Interfaces;

/// <summary>
/// Aplicação acima de <see cref="EfsAiHub.Core.Agents.IPredefinedModelRepository"/>:
/// valida invariantes, normaliza Description, gerencia timestamps. Sem owner gate
/// — catálogo é global e gerenciado por admin via /api/aihub/admin/predefined-models.
/// </summary>
public interface IPredefinedModelService
{
    Task<PredefinedModel> CreateAsync(PredefinedModel model, CancellationToken ct = default);

    Task<PredefinedModel?> GetAsync(string id, CancellationToken ct = default);

    Task<IReadOnlyList<PredefinedModel>> ListAsync(bool includeDisabled = false, CancellationToken ct = default);

    Task<PredefinedModel> UpdateAsync(
        string id,
        PredefinedModel patch,
        DateTime expectedUpdatedAt,
        CancellationToken ct = default);

    Task DeleteAsync(string id, CancellationToken ct = default);
}
