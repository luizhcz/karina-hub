
namespace EfsAiHub.Core.Agents;

public interface IAgentSessionStore
{
    Task<AgentSessionRecord> CreateAsync(AgentSessionRecord record, CancellationToken ct = default);
    Task<AgentSessionRecord?> GetByIdAsync(string sessionId, CancellationToken ct = default);
    Task<IReadOnlyList<AgentSessionRecord>> GetByAgentIdAsync(string agentId, CancellationToken ct = default);
    Task<AgentSessionRecord> UpdateAsync(AgentSessionRecord record, CancellationToken ct = default);
    Task<bool> DeleteAsync(string sessionId, CancellationToken ct = default);
}
