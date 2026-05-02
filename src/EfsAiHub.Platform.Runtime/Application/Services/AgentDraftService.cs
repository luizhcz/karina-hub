using EfsAiHub.Core.Abstractions.Identity;

namespace EfsAiHub.Platform.Runtime.Services;

public sealed class AgentDraftService : IAgentDraftService
{
    private readonly IAgentDraftRepository _draftRepo;
    private readonly IAgentDefinitionRepository _agentRepo;
    private readonly IAgentVersionRepository _versionRepo;
    private readonly IProjectContextAccessor _projectAccessor;
    private readonly ITenantContextAccessor _tenantAccessor;
    private readonly ILogger<AgentDraftService> _logger;

    public AgentDraftService(
        IAgentDraftRepository draftRepo,
        IAgentDefinitionRepository agentRepo,
        IAgentVersionRepository versionRepo,
        IProjectContextAccessor projectAccessor,
        ITenantContextAccessor tenantAccessor,
        ILogger<AgentDraftService> logger)
    {
        _draftRepo = draftRepo;
        _agentRepo = agentRepo;
        _versionRepo = versionRepo;
        _projectAccessor = projectAccessor;
        _tenantAccessor = tenantAccessor;
        _logger = logger;
    }

    public async Task<AgentDraft> CreateAsync(
        string? id,
        AgentDraftPayload payload,
        CancellationToken ct = default)
    {
        var projectId = _projectAccessor.Current.ProjectId;
        var tenantId = _tenantAccessor.Current.TenantId;
        var draftId = !string.IsNullOrWhiteSpace(id)
            ? id!.Trim()
            : Guid.NewGuid().ToString("N");

        if (await _draftRepo.ExistsAsync(draftId, ct))
            throw new InvalidOperationException(
                $"Já existe draft com id '{draftId}'. Edite o existente ou use outro id.");

        if (await _agentRepo.ExistsAsync(draftId, ct))
            throw new InvalidOperationException(
                $"Id '{draftId}' já é usado por agent publicado. Use POST /api/agents/{{id}}/edit-draft pra forkar.");

        var draft = new AgentDraft
        {
            Id = draftId,
            Name = payload.Name ?? string.Empty,
            Payload = payload,
            ProjectId = projectId,
            TenantId = tenantId,
            BaseAgentId = null,
            BaseRevision = null,
            CreatedBy = null,
        };

        var saved = await _draftRepo.UpsertAsync(draft, expectedUpdatedAt: null, ct);

        _logger.LogInformation(
            "[AgentDraftService] Draft '{DraftId}' criado em projeto '{ProjectId}'.",
            saved.Id, projectId);

        return saved;
    }

    public async Task<AgentDraft> CreateEditDraftAsync(string baseAgentId, CancellationToken ct = default)
    {
        var existingAgent = await _agentRepo.GetByIdAsync(baseAgentId, ct)
            ?? throw new KeyNotFoundException($"Agent '{baseAgentId}' não encontrado.");

        var projectId = _projectAccessor.Current.ProjectId;
        if (!string.Equals(existingAgent.ProjectId, projectId, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException(
                $"Agent '{baseAgentId}' não pertence ao projeto atual; apenas o projeto dono pode forkar pra edit-draft.");

        if (await _draftRepo.ExistsAsync(baseAgentId, ct))
            throw new InvalidOperationException(
                $"Já existe edit-draft para '{baseAgentId}'. Continue editando o draft existente ou descarte-o antes.");

        // Captura Revision corrente do agent base. Se outro caller publicar
        // diretamente em agent_versions entre o fork e o approve do draft,
        // PgAgentDraftRepository.ApproveAsync detecta divergência via comparação
        // com MAX(agent_versions.Revision) e lança DraftRePublishRaceException.
        // Quando o agent não tem AgentVersion ainda (raro — ocorre em agents pré
        // dual-write ou se o append falhou silenciosamente), BaseRevision = null
        // e a detecção de race fica passiva. ContentHash idempotente em UpsertAsync
        // colapsa re-publish de payload idêntico, então a janela é estreita; backfill
        // de agent_versions resolve definitivamente.
        var current = await _versionRepo.GetCurrentAsync(baseAgentId, ct);
        var baseRevision = current?.Revision;

        var draft = new AgentDraft
        {
            Id = baseAgentId,
            Name = existingAgent.Name,
            Payload = AgentDraftPayload.FromAgentDefinition(existingAgent),
            ProjectId = existingAgent.ProjectId,
            TenantId = existingAgent.TenantId,
            BaseAgentId = baseAgentId,
            BaseRevision = baseRevision,
            CreatedBy = null,
        };

        var saved = await _draftRepo.UpsertAsync(draft, expectedUpdatedAt: null, ct);

        _logger.LogInformation(
            "[AgentDraftService] Edit-draft criado para agent '{AgentId}' em projeto '{ProjectId}'.",
            baseAgentId, projectId);

        return saved;
    }

    public Task<AgentDraft?> GetAsync(string id, CancellationToken ct = default)
        => _draftRepo.GetByIdAsync(id, ct);

    public Task<IReadOnlyList<AgentDraft>> ListAsync(CancellationToken ct = default)
        => _draftRepo.ListAsync(ct);

    public async Task<AgentDraft> UpdateAsync(
        string id,
        AgentDraftPayload payload,
        DateTime expectedUpdatedAt,
        CancellationToken ct = default)
    {
        var existing = await _draftRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Draft '{id}' não encontrado.");

        var currentProjectId = _projectAccessor.Current.ProjectId;
        if (!string.Equals(existing.ProjectId, currentProjectId, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException(
                $"Draft '{id}' não pertence ao projeto atual.");

        existing.Name = payload.Name ?? string.Empty;
        existing.Payload = payload;

        return await _draftRepo.UpsertAsync(existing, expectedUpdatedAt, ct);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        var existing = await _draftRepo.GetByIdAsync(id, ct);
        if (existing is null)
            throw new KeyNotFoundException($"Draft '{id}' não encontrado.");

        var currentProjectId = _projectAccessor.Current.ProjectId;
        if (!string.Equals(existing.ProjectId, currentProjectId, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException(
                $"Draft '{id}' não pertence ao projeto atual.");

        await _draftRepo.DeleteAsync(id, ct);

        _logger.LogInformation("[AgentDraftService] Draft '{DraftId}' descartado.", id);
    }

    public async Task<AgentDraft> SubmitForApprovalAsync(
        string id,
        string actorUserId,
        CancellationToken ct = default)
    {
        var existing = await _draftRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Draft '{id}' não encontrado.");

        var currentProjectId = _projectAccessor.Current.ProjectId;
        if (!string.Equals(existing.ProjectId, currentProjectId, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException(
                $"Draft '{id}' não pertence ao projeto atual.");

        var wasResubmit = existing.Status == AgentDraftStatus.Rejected;
        var submitted = await _draftRepo.SubmitForApprovalAsync(id, actorUserId, ct);

        _logger.LogInformation(
            "[AgentDraftService] Draft '{DraftId}' submetido ao painel (wasResubmit={WasResubmit}, isEditDraft={IsEdit}).",
            id, wasResubmit, submitted.IsEditDraft);

        return submitted;
    }
}
