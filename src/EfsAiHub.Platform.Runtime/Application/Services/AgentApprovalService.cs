namespace EfsAiHub.Platform.Runtime.Services;

public sealed class AgentApprovalService : IAgentApprovalService
{
    private readonly IAgentDraftRepository _draftRepo;
    private readonly IAgentRouterIntentLinkRepository? _intentLinks;
    private readonly ILogger<AgentApprovalService> _logger;

    public AgentApprovalService(
        IAgentDraftRepository draftRepo,
        ILogger<AgentApprovalService> logger,
        IAgentRouterIntentLinkRepository? intentLinks = null)
    {
        _draftRepo = draftRepo;
        _intentLinks = intentLinks;
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
        // Captura o set declarado no draft antes do approve — ApproveAsync apaga
        // o draft após publicar, então o snapshot do payload precisa ser feito
        // aqui pra reconciliar a junction em seguida.
        var draftBefore = await _draftRepo.GetByIdForTenantAsync(id, ct);
        var declaredIntentIds = draftBefore?.Payload.RouterIntentIds;

        var published = await _draftRepo.ApproveAsync(id, actorUserId, changeReason, AgentApprovalAction.Approved, null, ct);

        // RouterIntentIds tem [JsonIgnore] na AgentDefinition: o set não viaja
        // no jsonb do upsert. A fonte da verdade é a junction agent_router_intents.
        // Reconcilia aqui pra manter o mesmo invariante que AgentsController.Update
        // aplica via ReconcileAndLoadRouterIntentsAsync — sem isso o approve
        // publicaria um Router sem nenhuma intent.
        if (published.Type == AgentType.Router && _intentLinks is not null && declaredIntentIds is not null)
        {
            await _intentLinks.SetIntentsForAgentAsync(
                published.Id,
                published.ProjectId,
                published.TenantId,
                declaredIntentIds,
                ct);
            published.RouterIntentIds = await _intentLinks.ListIntentIdsForAgentAsync(published.Id, ct);
        }

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
