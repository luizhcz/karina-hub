using EfsAiHub.Core.Agents.PredefinedModels;

namespace EfsAiHub.Core.Agents;

/// <summary>
/// Persistência do catálogo global de Modelos Pré-definidos. Sem ProjectId —
/// presets são cross-tenant e admin-managed. Reads não filtram por
/// <see cref="PredefinedModel.Enabled"/> por default; controllers públicos
/// fazem o filter via flag <paramref name="includeDisabled"/>.
/// </summary>
public interface IPredefinedModelRepository
{
    Task<PredefinedModel?> GetByIdAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Lista presets do catálogo. <paramref name="includeDisabled"/>=false (default)
    /// retorna apenas Enabled=true — usado pelo endpoint público que popula o
    /// dropdown do AgentForm. Admin lista usa true pra ver tudo.
    /// </summary>
    Task<IReadOnlyList<PredefinedModel>> ListAsync(bool includeDisabled = false, CancellationToken ct = default);

    Task<PredefinedModel> CreateAsync(PredefinedModel model, CancellationToken ct = default);

    /// <summary>
    /// Atualiza com optimistic concurrency: WHERE Id=@id AND UpdatedAt=@expected.
    /// 0 rows afetadas lança <see cref="PredefinedModelConcurrencyException"/>.
    /// </summary>
    Task<PredefinedModel> UpdateAsync(
        PredefinedModel model,
        DateTime expectedUpdatedAt,
        CancellationToken ct = default);

    Task<bool> DeleteAsync(string id, CancellationToken ct = default);
}

public sealed class PredefinedModelConcurrencyException : Exception
{
    public PredefinedModelConcurrencyException(string id)
        : base($"PredefinedModel '{id}' foi modificado por outra requisição (UpdatedAt divergente).")
    { }
}

public sealed class PredefinedModelIdConflictException : Exception
{
    public PredefinedModelIdConflictException(string id)
        : base($"Já existe PredefinedModel com Id '{id}'.")
    { }
}
