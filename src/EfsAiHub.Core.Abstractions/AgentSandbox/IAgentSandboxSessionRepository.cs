namespace EfsAiHub.Core.Abstractions.AgentSandbox;

public interface IAgentSandboxSessionRepository
{
    Task<AgentSandboxSession> CreateAsync(AgentSandboxSession session, CancellationToken ct = default);

    Task<AgentSandboxSession?> GetByIdAsync(string sessionId, CancellationToken ct = default);

    /// <summary>Lista sessions de um agente ordenadas por mais recente primeiro.</summary>
    Task<IReadOnlyList<AgentSandboxSession>> ListByAgentAsync(
        string agentId,
        AgentSandboxSessionStatus? statusFilter = null,
        int limit = 50,
        CancellationToken ct = default);

    Task<AgentSandboxSession> UpdateAsync(AgentSandboxSession session, CancellationToken ct = default);

    /// <summary>Cleanup background: sessions <c>Active</c>/<c>Closed</c> com <c>ExpiresAt &lt; now</c>.</summary>
    Task<IReadOnlyList<AgentSandboxSession>> ListExpiredAsync(int batchSize, CancellationToken ct = default);

    /// <summary>Marca como <c>Expired</c> (workflow/conversation são limpos pelo caller).</summary>
    Task MarkExpiredAsync(string sessionId, CancellationToken ct = default);
}
