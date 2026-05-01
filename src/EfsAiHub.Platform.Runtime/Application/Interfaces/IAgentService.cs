
namespace EfsAiHub.Platform.Runtime.Interfaces;

public interface IAgentService
{
    Task<AgentDefinition> CreateAsync(AgentDefinition definition, CancellationToken ct = default);
    Task<AgentDefinition?> GetAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<AgentDefinition>> ListAsync(CancellationToken ct = default);
    Task<AgentDefinition> UpdateAsync(AgentDefinition definition, CancellationToken ct = default);

    /// <summary>
    /// Phase 2 — Muda Visibility ('project' | 'global') do agent. Apenas o projeto
    /// dono pode alterar; caller de outro projeto recebe UnauthorizedAccessException
    /// (mapeado pra 403). Lança KeyNotFoundException se agent não existir, ArgumentException
    /// se newVisibility for inválido.
    /// </summary>
    Task<AgentDefinition> UpdateVisibilityAsync(string id, string newVisibility, CancellationToken ct = default);

    Task DeleteAsync(string id, CancellationToken ct = default);
    Task<(bool IsValid, IReadOnlyList<string> Errors)> ValidateAsync(AgentDefinition definition, CancellationToken ct = default);
}
