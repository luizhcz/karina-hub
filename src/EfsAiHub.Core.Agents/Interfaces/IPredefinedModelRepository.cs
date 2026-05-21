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

/// <summary>
/// Lançada quando alguém tenta deletar um <see cref="PredefinedModel"/> que
/// ainda está referenciado por agentes. Lista os agentes ofendidos pra UI
/// orientar o user a trocar o preset antes.
/// </summary>
public sealed class PredefinedModelInUseException : Exception
{
    public IReadOnlyList<string> AgentIds { get; }

    public PredefinedModelInUseException(string modelId, IReadOnlyList<string> agentIds)
        : base($"PredefinedModel '{modelId}' está em uso por {agentIds.Count} agente(s); troque o preset desses agentes antes de deletar.")
    {
        AgentIds = agentIds;
    }
}
