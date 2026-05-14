namespace EfsAiHub.Core.Abstractions.ChatSandbox;

public interface IChatSandboxSessionRepository
{
    Task<ChatSandboxSession> CreateAsync(ChatSandboxSession session, CancellationToken ct = default);

    Task<ChatSandboxSession?> GetByIdAsync(string sessionId, CancellationToken ct = default);

    /// <summary>Lista sessions de um agente ordenadas por mais recente primeiro.</summary>
    Task<IReadOnlyList<ChatSandboxSession>> ListByAgentAsync(
        string agentId,
        ChatSandboxSessionStatus? statusFilter = null,
        int limit = 50,
        CancellationToken ct = default);

    Task<ChatSandboxSession> UpdateAsync(ChatSandboxSession session, CancellationToken ct = default);

    /// <summary>Cleanup background: sessions <c>Active</c>/<c>Closed</c> com <c>ExpiresAt &lt; now</c>.</summary>
    Task<IReadOnlyList<ChatSandboxSession>> ListExpiredAsync(int batchSize, CancellationToken ct = default);

    /// <summary>Marca como <c>Expired</c> e remove FK refs (workflow/conversation são limpos pelo caller).</summary>
    Task MarkExpiredAsync(string sessionId, CancellationToken ct = default);
}
