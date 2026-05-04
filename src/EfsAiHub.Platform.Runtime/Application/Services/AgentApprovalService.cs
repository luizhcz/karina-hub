namespace EfsAiHub.Platform.Runtime.Services;

public sealed class AgentApprovalService : IAgentApprovalService
{
    private readonly IAgentDraftRepository _draftRepo;
    private readonly ILogger<AgentApprovalService> _logger;

    public AgentApprovalService(
        IAgentDraftRepository draftRepo,
        ILogger<AgentApprovalService> logger)
    {
        _draftRepo = draftRepo;
        _logger = logger;
    }

    public Task<IReadOnlyList<AgentDraft>> ListPendingAsync(CancellationToken ct = default)
        => _draftRepo.ListByStatusForTenantAsync(AgentDraftStatus.PendingApproval, ct);

    public Task<IReadOnlyList<AgentDraft>> ListByStatusAsync(AgentDraftStatus status, CancellationToken ct = default)
        => _draftRepo.ListByStatusForTenantAsync(status, ct);

    public Task<AgentDraft?> GetAsync(string id, CancellationToken ct = default)
        => _draftRepo.GetByIdForTenantAsync(id, ct);

    public async Task<AgentDefinition> ApproveAsync(
        string id,
        string actorUserId,
        string? changeReason,
        CancellationToken ct = default)
    {
        var published = await _draftRepo.ApproveAsync(id, actorUserId, changeReason, AgentApprovalAction.Approved, null, ct);

        _logger.LogInformation(
            "[AgentApprovalService] Draft '{DraftId}' aprovado por '{Actor}' como agent '{AgentId}'.",
            id, actorUserId, published.Id);

        return published;
    }

    public async Task<AgentDraft> RejectAsync(
        string id,
        string actorUserId,
        string feedback,
        CancellationToken ct = default)
    {
        var rejected = await _draftRepo.RejectAsync(id, actorUserId, feedback, ct);

        _logger.LogInformation(
            "[AgentApprovalService] Draft '{DraftId}' rejeitado por '{Actor}'.",
            id, actorUserId);

        return rejected;
    }

    public Task<IReadOnlyList<AgentApprovalHistoryEntry>> GetHistoryAsync(string draftId, CancellationToken ct = default)
        => _draftRepo.GetHistoryAsync(draftId, ct);
}
