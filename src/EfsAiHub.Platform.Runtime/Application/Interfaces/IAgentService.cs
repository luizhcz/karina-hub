
namespace EfsAiHub.Platform.Runtime.Interfaces;

public interface IAgentService
{
    Task<AgentDefinition> CreateAsync(AgentDefinition definition, CancellationToken ct = default);
    Task<AgentDefinition?> GetAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<AgentDefinition>> ListAsync(CancellationToken ct = default);
    Task<AgentDefinition> UpdateAsync(AgentDefinition definition, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
    Task<(bool IsValid, IReadOnlyList<string> Errors)> ValidateAsync(AgentDefinition definition, CancellationToken ct = default);
}
