using System.Text.Json;
using EfsAiHub.Core.Abstractions.Persistence;
using EfsAiHub.Core.Agents;
using Microsoft.EntityFrameworkCore;

namespace EfsAiHub.Infra.Persistence.Postgres;

public sealed class PgAgentDraftRepository : IAgentDraftRepository
{
    private readonly IDbContextFactory<AgentFwDbContext> _factory;
    private readonly IAgentDefinitionRepository _definitionRepo;
    private readonly IAgentVersionRepository _versionRepo;
    private readonly EfsAiHub.Core.Abstractions.Identity.ITenantContextAccessor _tenantAccessor;
    private readonly ILogger<PgAgentDraftRepository> _logger;

    public PgAgentDraftRepository(
        IDbContextFactory<AgentFwDbContext> factory,
        IAgentDefinitionRepository definitionRepo,
        IAgentVersionRepository versionRepo,
        EfsAiHub.Core.Abstractions.Identity.ITenantContextAccessor tenantAccessor,
        ILogger<PgAgentDraftRepository> logger)
    {
        _factory = factory;
        _definitionRepo = definitionRepo;
        _versionRepo = versionRepo;
        _tenantAccessor = tenantAccessor;
        _logger = logger;
    }

    public async Task<AgentDraft?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.AgentDrafts.FirstOrDefaultAsync(r => r.Id == id, ct);
        return row is null ? null : Hydrate(row);
    }

    public async Task<AgentDraft?> GetByIdForTenantAsync(string id, CancellationToken ct = default)
    {
        var tenantId = _tenantAccessor.Current.TenantId;
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.AgentDrafts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct);
        return row is null ? null : Hydrate(row);
    }

    public async Task<IReadOnlyList<AgentDraft>> ListAsync(CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var rows = await ctx.AgentDrafts
            .OrderByDescending(r => r.UpdatedAt)
            .ToListAsync(ct);
        return rows.Select(Hydrate).ToList();
    }

    public async Task<IReadOnlyList<AgentDraft>> ListByStatusForTenantAsync(
        AgentDraftStatus status,
        CancellationToken ct = default)
    {
        var tenantId = _tenantAccessor.Current.TenantId;
        var statusStr = status.ToString();
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var rows = await ctx.AgentDrafts
            .IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId && r.Status == statusStr)
            .OrderByDescending(r => r.SubmittedAt ?? r.UpdatedAt)
            .ToListAsync(ct);
        return rows.Select(Hydrate).ToList();
    }

    public async Task<bool> ExistsAsync(string id, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        return await ctx.AgentDrafts.AnyAsync(r => r.Id == id, ct);
    }

    public async Task<AgentDraft> UpsertAsync(
        AgentDraft draft,
        DateTime? expectedUpdatedAt,
        CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var data = JsonSerializer.Serialize(draft.Payload, JsonDefaults.Domain);
        var now = DateTime.UtcNow;

        if (expectedUpdatedAt is null)
        {
            var existing = await ctx.AgentDrafts.FirstOrDefaultAsync(r => r.Id == draft.Id, ct);
            if (existing is null)
            {
                ctx.AgentDrafts.Add(new AgentDraftRow
                {
                    Id = draft.Id,
                    Name = draft.Name,
                    Data = data,
                    ProjectId = draft.ProjectId,
                    TenantId = draft.TenantId,
                    BaseAgentId = draft.BaseAgentId,
                    BaseRevision = draft.BaseRevision,
                    Status = AgentDraftStatus.Draft.ToString(),
                    CreatedAt = now,
                    UpdatedAt = now,
                    CreatedBy = draft.CreatedBy,
                    RegressionTestSetId = draft.Payload.RegressionTestSetId,
                    RegressionEvaluatorConfigVersionId = draft.Payload.RegressionEvaluatorConfigVersionId,
                });
                await ctx.SaveChangesAsync(ct);
                draft.Status = AgentDraftStatus.Draft;
                draft.UpdatedAt = now;
                return draft;
            }

            // PUT em draft pendente é proibido — owner cancela a submissão antes.
            if (string.Equals(existing.Status, AgentDraftStatus.PendingApproval.ToString(), StringComparison.Ordinal))
                throw new DraftStatusTransitionException(draft.Id, AgentDraftStatus.PendingApproval, "edit");

            existing.Name = draft.Name;
            existing.Data = data;
            existing.UpdatedAt = now;
            existing.RegressionTestSetId = draft.Payload.RegressionTestSetId;
            existing.RegressionEvaluatorConfigVersionId = draft.Payload.RegressionEvaluatorConfigVersionId;

            // Re-edição após rejeição: limpa feedback e volta status pra Draft.
            // Owner faz novo Submit quando estiver pronto.
            if (string.Equals(existing.Status, AgentDraftStatus.Rejected.ToString(), StringComparison.Ordinal))
            {
                existing.Status = AgentDraftStatus.Draft.ToString();
                existing.RejectionFeedback = null;
            }

            await ctx.SaveChangesAsync(ct);
            draft.Status = ParseStatus(existing.Status);
            draft.RejectionFeedback = existing.RejectionFeedback;
            draft.UpdatedAt = now;
            return draft;
        }

        // Optimistic concurrency atomico.
        // Status check via ExecuteUpdate é impraticável com lógica condicional;
        // re-fetch + check + update no mesmo ctx cobre o caminho concorrente.
        var current = await ctx.AgentDrafts
            .FirstOrDefaultAsync(r => r.Id == draft.Id && r.UpdatedAt == expectedUpdatedAt.Value, ct);

        if (current is null)
        {
            var stillExists = await ctx.AgentDrafts.AnyAsync(r => r.Id == draft.Id, ct);
            if (stillExists)
                throw new DraftConcurrencyException(draft.Id);
            throw new KeyNotFoundException($"Draft '{draft.Id}' não encontrado.");
        }

        if (string.Equals(current.Status, AgentDraftStatus.PendingApproval.ToString(), StringComparison.Ordinal))
            throw new DraftStatusTransitionException(draft.Id, AgentDraftStatus.PendingApproval, "edit");

        current.Name = draft.Name;
        current.Data = data;
        current.UpdatedAt = now;
        current.RegressionTestSetId = draft.Payload.RegressionTestSetId;
        current.RegressionEvaluatorConfigVersionId = draft.Payload.RegressionEvaluatorConfigVersionId;
        if (string.Equals(current.Status, AgentDraftStatus.Rejected.ToString(), StringComparison.Ordinal))
        {
            current.Status = AgentDraftStatus.Draft.ToString();
            current.RejectionFeedback = null;
        }
        await ctx.SaveChangesAsync(ct);

        draft.Status = ParseStatus(current.Status);
        draft.RejectionFeedback = current.RejectionFeedback;
        draft.UpdatedAt = now;
        return draft;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.AgentDrafts.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return false;
        ctx.AgentDrafts.Remove(row);
        await ctx.SaveChangesAsync(ct);
        return true;
    }

    public async Task<AgentDraft> SubmitForApprovalAsync(
        string id,
        string actorUserId,
        CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;

        var draftStatus = AgentDraftStatus.Draft.ToString();
        var rejectedStatus = AgentDraftStatus.Rejected.ToString();
        var pendingStatus = AgentDraftStatus.PendingApproval.ToString();

        // Owner-scoped (query filter) — caller só submete drafts próprios.
        var row = await ctx.AgentDrafts.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new KeyNotFoundException($"Draft '{id}' não encontrado.");

        if (string.Equals(row.Status, pendingStatus, StringComparison.Ordinal))
            throw new DraftStatusTransitionException(id, AgentDraftStatus.PendingApproval, "submit");

        var wasRejected = string.Equals(row.Status, rejectedStatus, StringComparison.Ordinal);
        var isValidSource = string.Equals(row.Status, draftStatus, StringComparison.Ordinal) || wasRejected;
        if (!isValidSource)
            throw new DraftStatusTransitionException(id, ParseStatus(row.Status), "submit");

        row.Status = pendingStatus;
        row.SubmittedAt = now;
        row.UpdatedAt = now;
        row.RejectionFeedback = null;

        ctx.AgentApprovalHistory.Add(new AgentApprovalHistoryRow
        {
            Id = Guid.NewGuid().ToString("N"),
            DraftId = id,
            AgentDefinitionId = ResolveAgentDefinitionId(row),
            TenantId = row.TenantId,
            Action = (wasRejected ? AgentApprovalAction.Resubmitted : AgentApprovalAction.Submitted).ToString(),
            ActorUserId = actorUserId,
            Feedback = null,
            OccurredAt = now,
        });

        await ctx.SaveChangesAsync(ct);

        return Hydrate(row);
    }

    public async Task<AgentDefinition> ApproveAsync(
        string id,
        string actorUserId,
        string? changeReason,
        AgentApprovalAction action = AgentApprovalAction.Approved,
        AgentChangeTier? tier = null,
        CancellationToken ct = default)
    {
        var draft = await GetByIdForTenantAsync(id, ct)
            ?? throw new KeyNotFoundException($"Draft '{id}' não encontrado.");

        if (draft.Status != AgentDraftStatus.PendingApproval)
            throw new DraftStatusTransitionException(id, draft.Status, "approve");

        // Edit-draft race detection: agent base re-publicado entre fork e approve.
        if (draft.IsEditDraft && draft.BaseRevision is int baseRev)
        {
            var current = await _versionRepo.GetCurrentAsync(draft.BaseAgentId!, ct);
            if (current is not null && current.Revision > baseRev)
                throw new DraftRePublishRaceException(id, baseRev, current.Revision);
        }

        var definition = draft.Payload.ToAgentDefinition(
            id: draft.Id,
            projectId: draft.ProjectId,
            tenantId: draft.TenantId,
            createdAt: draft.CreatedAt);

        // UpsertAsync no IAgentDefinitionRepository roda fora do escopo de project
        // do caller — agent recém-aprovado fica visível pro owner project sem
        // depender do query filter atual. Cobre stamp de FingerprintHash, tenant
        // lookup canônico via owner project, dual-write de AgentVersion.
        var published = await _definitionRepo.UpsertAsync(
            definition,
            ct,
            breakingChange: false,
            changeReason: changeReason,
            createdBy: actorUserId);

        // Cleanup do draft + history em transação atômica via mesmo ctx.
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var rows = await ctx.AgentDrafts
            .IgnoreQueryFilters()
            .Where(r => r.Id == id && r.Status == AgentDraftStatus.PendingApproval.ToString())
            .ExecuteDeleteAsync(ct);

        if (rows == 0)
        {
            // Concorrência: outro aprovador já agiu. Re-publish do agent_definitions
            // foi idempotente (ContentHash); ok continuar.
            _logger.LogWarning(
                "[PgAgentDraftRepository] Draft '{DraftId}' já não estava em PendingApproval no momento do delete pós-approve.",
                id);
        }

        ctx.AgentApprovalHistory.Add(new AgentApprovalHistoryRow
        {
            Id = Guid.NewGuid().ToString("N"),
            DraftId = id,
            AgentDefinitionId = published.Id,
            TenantId = draft.TenantId,
            Action = action.ToString(),
            ActorUserId = actorUserId,
            Feedback = changeReason,
            Tier = tier?.ToString(),
            OccurredAt = DateTime.UtcNow,
        });
        await ctx.SaveChangesAsync(ct);

        return published;
    }

    public async Task<AgentDraft> RejectAsync(
        string id,
        string actorUserId,
        string feedback,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(feedback))
            throw new ArgumentException("Feedback é obrigatório na rejeição.", nameof(feedback));

        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        var pendingStatus = AgentDraftStatus.PendingApproval.ToString();
        var tenantId = _tenantAccessor.Current.TenantId;

        // TX explícita: ExecuteUpdate (statement individual auto-committed) +
        // history insert via SaveChanges precisam ficar atômicos pra evitar
        // janela onde o draft fica Rejected sem entry de history correspondente.
        await using var tx = await ctx.Database.BeginTransactionAsync(ct);

        var rows = await ctx.AgentDrafts
            .IgnoreQueryFilters()
            .Where(r => r.Id == id && r.TenantId == tenantId && r.Status == pendingStatus)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, AgentDraftStatus.Rejected.ToString())
                .SetProperty(r => r.RejectionFeedback, feedback)
                .SetProperty(r => r.UpdatedAt, now),
                ct);

        if (rows == 0)
        {
            await tx.RollbackAsync(ct);
            var stillExists = await ctx.AgentDrafts
                .IgnoreQueryFilters()
                .AnyAsync(r => r.Id == id && r.TenantId == tenantId, ct);
            if (!stillExists)
                throw new KeyNotFoundException($"Draft '{id}' não encontrado.");
            throw new DraftStatusTransitionException(id, AgentDraftStatus.Draft, "reject");
        }

        // Pra preencher AgentDefinitionId no history precisamos do draft pós-update.
        // Reload barato — já estamos no mesmo ctx + tx.
        var draftRow = await ctx.AgentDrafts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct);

        ctx.AgentApprovalHistory.Add(new AgentApprovalHistoryRow
        {
            Id = Guid.NewGuid().ToString("N"),
            DraftId = id,
            AgentDefinitionId = draftRow is null ? id : ResolveAgentDefinitionId(draftRow),
            TenantId = tenantId,
            Action = AgentApprovalAction.Rejected.ToString(),
            ActorUserId = actorUserId,
            Feedback = feedback,
            OccurredAt = now,
        });
        await ctx.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var refreshed = await GetByIdForTenantAsync(id, ct)
            ?? throw new KeyNotFoundException($"Draft '{id}' não encontrado pós-reject.");
        return refreshed;
    }

    /// <summary>
    /// Liga uma entry de history ao agent publicado: pra new draft (isEditDraft=
    /// false) o draft.Id vira o AgentDefinition.Id após approve, então o próprio
    /// id já serve. Pra edit-draft o vínculo é via BaseAgentId (agent existente
    /// que será atualizado ou recém atualizado).
    /// </summary>
    private static string ResolveAgentDefinitionId(AgentDraftRow row)
    {
        return string.IsNullOrWhiteSpace(row.BaseAgentId) ? row.Id : row.BaseAgentId!;
    }

    public async Task<IReadOnlyList<AgentApprovalHistoryEntry>> GetHistoryAsync(
        string draftId,
        CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var rows = await ctx.AgentApprovalHistory
            .Where(r => r.DraftId == draftId)
            .OrderBy(r => r.OccurredAt)
            .ToListAsync(ct);

        return rows.Select(MapEntry).ToList();
    }

    public async Task<IReadOnlyList<AgentApprovalHistoryEntry>> GetHistoryByAgentAsync(
        string agentDefinitionId,
        CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var rows = await ctx.AgentApprovalHistory
            .Where(r => r.AgentDefinitionId == agentDefinitionId)
            .OrderBy(r => r.OccurredAt)
            .ToListAsync(ct);

        return rows.Select(MapEntry).ToList();
    }

    public async Task AppendAdminOverrideAsync(
        string agentDefinitionId,
        string tenantId,
        string actorUserId,
        string changeReason,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(agentDefinitionId))
            throw new ArgumentException("agentDefinitionId obrigatório", nameof(agentDefinitionId));
        if (string.IsNullOrWhiteSpace(changeReason))
            throw new ArgumentException("changeReason obrigatório", nameof(changeReason));

        await using var ctx = await _factory.CreateDbContextAsync(ct);
        ctx.AgentApprovalHistory.Add(new AgentApprovalHistoryRow
        {
            Id = Guid.NewGuid().ToString("N"),
            // DraftId sintético — não há draft real envolvido. Mantém a coluna NOT
            // NULL satisfeita e identifica o caminho na trilha de auditoria.
            DraftId = $"admin-override:{agentDefinitionId}",
            AgentDefinitionId = agentDefinitionId,
            TenantId = tenantId,
            Action = AgentApprovalAction.AdminOverride.ToString(),
            ActorUserId = actorUserId,
            Feedback = changeReason,
            OccurredAt = DateTime.UtcNow,
        });
        await ctx.SaveChangesAsync(ct);
    }

    private static AgentApprovalHistoryEntry MapEntry(AgentApprovalHistoryRow r) =>
        new(
            Id: r.Id,
            DraftId: r.DraftId,
            AgentDefinitionId: r.AgentDefinitionId,
            TenantId: r.TenantId,
            Action: ParseAction(r.Action),
            ActorUserId: r.ActorUserId,
            Feedback: r.Feedback,
            Tier: r.Tier,
            OccurredAt: r.OccurredAt);

    private static AgentDraft Hydrate(AgentDraftRow row)
    {
        var payload = JsonSerializer.Deserialize<AgentDraftPayload>(row.Data, JsonDefaults.Domain)
            ?? new AgentDraftPayload();

        return new AgentDraft
        {
            Id = row.Id,
            Name = row.Name,
            Payload = payload,
            ProjectId = row.ProjectId,
            TenantId = row.TenantId,
            BaseAgentId = row.BaseAgentId,
            BaseRevision = row.BaseRevision,
            Status = ParseStatus(row.Status),
            RejectionFeedback = row.RejectionFeedback,
            SubmittedAt = row.SubmittedAt,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt,
            CreatedBy = row.CreatedBy,
        };
    }

    private static AgentDraftStatus ParseStatus(string raw) =>
        Enum.TryParse<AgentDraftStatus>(raw, out var s) ? s : AgentDraftStatus.Draft;

    private static AgentApprovalAction ParseAction(string raw) =>
        Enum.TryParse<AgentApprovalAction>(raw, out var a) ? a : AgentApprovalAction.Submitted;
}
