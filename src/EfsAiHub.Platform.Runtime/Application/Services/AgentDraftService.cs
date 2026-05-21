using EfsAiHub.Core.Abstractions.Identity;

namespace EfsAiHub.Platform.Runtime.Services;

public sealed class AgentDraftService : IAgentDraftService
{
    private readonly IAgentDraftRepository _draftRepo;
    private readonly IAgentDefinitionRepository _agentRepo;
    private readonly IAgentVersionRepository _versionRepo;
    private readonly IAgentRouterIntentLinkRepository? _intentLinkRepo;
    private readonly IProjectContextAccessor _projectAccessor;
    private readonly ITenantContextAccessor _tenantAccessor;
    private readonly ILogger<AgentDraftService> _logger;

    public AgentDraftService(
        IAgentDraftRepository draftRepo,
        IAgentDefinitionRepository agentRepo,
        IAgentVersionRepository versionRepo,
        IProjectContextAccessor projectAccessor,
        ITenantContextAccessor tenantAccessor,
        ILogger<AgentDraftService> logger,
        IAgentRouterIntentLinkRepository? intentLinkRepo = null)
    {
        _draftRepo = draftRepo;
        _agentRepo = agentRepo;
        _versionRepo = versionRepo;
        _intentLinkRepo = intentLinkRepo;
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
                $"Id '{draftId}' já é usado por agent publicado. Use POST /api/aihub/agents/{{id}}/edit-draft pra forkar.");

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

        // RouterIntentIds não persiste no jsonb da AgentDefinition (campo
        // [JsonIgnore] cuja fonte da verdade é a junction agent_router_intents).
        // Pra que o edit-draft mostre o set selecionado no wizard, hidrata
        // explicitamente via lookup no link repo antes de derivar o payload.
        if (existingAgent.Type == AgentType.Router && _intentLinkRepo is not null)
        {
            existingAgent.RouterIntentIds = await _intentLinkRepo
                .ListIntentIdsForAgentAsync(baseAgentId, ct);
        }

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

    public async Task<SubmitForApprovalResult> SubmitForApprovalAsync(
        string id,
        string actorUserId,
        string? changeReason = null,
        CancellationToken ct = default)
    {
        var existing = await _draftRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Draft '{id}' não encontrado.");

        var currentProjectId = _projectAccessor.Current.ProjectId;
        if (!string.Equals(existing.ProjectId, currentProjectId, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException(
                $"Draft '{id}' não pertence ao projeto atual.");

        var wasResubmit = existing.Status == AgentDraftStatus.Rejected;

        // Edit-draft cosmético é auto-aprovado: o sistema empurra direto pra
        // PendingApproval e na sequência aprova com action=AutoApproved. Isso
        // evita fila pra mudanças triviais (Description/Metadata) sem perder
        // trilha de auditoria. Edit comportamental e new draft seguem fluxo
        // normal: PendingApproval, espera admin.
        var tier = await ClassifyEditDraftAsync(existing, ct);

        var submitted = await _draftRepo.SubmitForApprovalAsync(id, actorUserId, ct);
        _logger.LogInformation(
            "[AgentDraftService] Draft '{DraftId}' submetido ao painel (wasResubmit={WasResubmit}, isEditDraft={IsEdit}, tier={Tier}).",
            id, wasResubmit, submitted.IsEditDraft, tier);

        if (tier == AgentChangeTier.Cosmetic)
        {
            // Aprova já no fluxo de submit. Race detection do BaseRevision
            // continua válido — se outro publish bumpa entre o submit e o
            // approve aqui, levanta DraftRePublishRaceException e o caller
            // recebe 409 com a mensagem padrão do AgentApprovalService.
            var publishedCosmetic = await _draftRepo.ApproveAsync(
                id,
                actorUserId: "system:auto",
                changeReason: $"Auto-approved (cosmetic change submitted by {actorUserId}).",
                action: AgentApprovalAction.AutoApproved,
                tier: AgentChangeTier.Cosmetic,
                ct: ct);

            // Mesma reconciliação do caminho explícito de approve — RouterIntentIds
            // não vive no jsonb da AgentDefinition (junction é fonte da verdade).
            // Cosmetic edits normalmente não tocam intents, mas reconciliar mantém
            // o invariante e protege contra payloads ainda em trânsito.
            await ReconcileRouterIntentsAsync(publishedCosmetic, existing.Payload.RouterIntentIds, ct);

            _logger.LogInformation(
                "[AgentDraftService] Draft '{DraftId}' auto-aprovado (cosmetic).",
                id);

            // Draft foi deletado pelo ApproveAsync. Devolve o estado pré-delete
            // só pra o caller ter feedback claro do que aconteceu.
            return new SubmitForApprovalResult(submitted, AutoApproved: true, Tier: tier);
        }

        return new SubmitForApprovalResult(submitted, AutoApproved: false, Tier: tier);
    }

    public Task<IReadOnlyList<AgentApprovalHistoryEntry>> GetApprovalHistoryByAgentAsync(
        string agentDefinitionId,
        CancellationToken ct = default)
        => _draftRepo.GetHistoryByAgentAsync(agentDefinitionId, ct);

    public Task AppendAdminOverrideAsync(
        string agentDefinitionId,
        string actorUserId,
        string changeReason,
        CancellationToken ct = default)
        => _draftRepo.AppendAdminOverrideAsync(
            agentDefinitionId,
            tenantId: _tenantAccessor.Current.TenantId,
            actorUserId: actorUserId,
            changeReason: changeReason,
            ct: ct);

    /// <summary>
    /// Persiste o set de intents do Router publicado na junction
    /// <c>agent_router_intents</c>. RouterIntentIds tem <c>[JsonIgnore]</c> na
    /// <see cref="AgentDefinition"/>, então o upsert do jsonb não cobre — o
    /// caller é responsável por chamar isso após o approve quando o tipo é Router.
    /// </summary>
    private async Task ReconcileRouterIntentsAsync(
        AgentDefinition published,
        IReadOnlyList<string>? declaredIntentIds,
        CancellationToken ct)
    {
        if (published.Type != AgentType.Router) return;
        if (_intentLinkRepo is null || declaredIntentIds is null) return;

        await _intentLinkRepo.SetIntentsForAgentAsync(
            published.Id,
            published.ProjectId,
            published.TenantId,
            declaredIntentIds,
            ct);
    }

    /// <summary>
    /// Compara payload do edit-draft contra o estado atual do agent base via
    /// <see cref="AgentChangeClassifier"/>. New drafts (sem BaseAgentId) são
    /// sempre Behavioral por definição (não há "antes" pra comparar). Falha
    /// silenciosa retorna Behavioral pra fail-safe.
    /// </summary>
    private async Task<AgentChangeTier> ClassifyEditDraftAsync(AgentDraft draft, CancellationToken ct)
    {
        if (!draft.IsEditDraft || string.IsNullOrWhiteSpace(draft.BaseAgentId))
            return AgentChangeTier.Behavioral;

        try
        {
            var current = await _agentRepo.GetByIdAsync(draft.BaseAgentId!, ct);
            if (current is null) return AgentChangeTier.Behavioral;
            return AgentChangeClassifier.Classify(current, draft.Payload);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[AgentDraftService] Falha ao classificar diff do edit-draft '{DraftId}'. Tratando como Behavioral.",
                draft.Id);
            return AgentChangeTier.Behavioral;
        }
    }
}
