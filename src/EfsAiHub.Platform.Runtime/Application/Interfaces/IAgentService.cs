
namespace EfsAiHub.Platform.Runtime.Interfaces;

public interface IAgentService
{
    Task<AgentDefinition> CreateAsync(AgentDefinition definition, CancellationToken ct = default);
    Task<AgentDefinition?> GetAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<AgentDefinition>> ListAsync(CancellationToken ct = default);
    Task<AgentDefinition> UpdateAsync(AgentDefinition definition, CancellationToken ct = default);

    /// <summary>
    /// Muda Visibility ('project' | 'global') do agent. Apenas o projeto
    /// dono pode alterar; caller de outro projeto recebe UnauthorizedAccessException
    /// (mapeado pra 403). Lança KeyNotFoundException se agent não existir, ArgumentException
    /// se newVisibility for inválido.
    /// </summary>
    Task<AgentDefinition> UpdateVisibilityAsync(string id, string newVisibility, CancellationToken ct = default);

    /// <summary>
    /// Publica uma nova AgentVersion do agent corrente declarando explicitamente se a
    /// mudança é breaking (afeta workflows pinados — não propaga automático) ou patch
    /// (propaga pra workflows pinados em ancestor sem breaking entre).
    /// <paramref name="changeReason"/> é obrigatório quando <paramref name="breakingChange"/>
    /// é true (rastreabilidade pra caller decidir migrar).
    /// Idempotência por ContentHash: se o snapshot atual da AgentDefinition já é a última
    /// version persistida, retorna a existente sem criar nova revision.
    /// Owner gate: apenas o projeto dono pode publicar.
    /// </summary>
    Task<AgentVersion> PublishVersionAsync(
        string agentId,
        bool breakingChange,
        string? changeReason = null,
        string? createdBy = null,
        CancellationToken ct = default);

    Task DeleteAsync(string id, CancellationToken ct = default);
    Task<(bool IsValid, IReadOnlyList<string> Errors)> ValidateAsync(AgentDefinition definition, CancellationToken ct = default);
}
