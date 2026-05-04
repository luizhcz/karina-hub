using EfsAiHub.Core.Abstractions.Exceptions;
using EfsAiHub.Host.Api.Models.Requests;
using EfsAiHub.Host.Api.Models.Responses;

namespace EfsAiHub.Host.Api.Controllers;

[ApiController]
[Route("api/agent-drafts")]
[Produces("application/json")]
public class AgentDraftsController : ControllerBase
{
    private readonly IAgentDraftService _draftService;
    private readonly IAdminAuditLogger _audit;
    private readonly AdminAuditContext _auditContext;

    public AgentDraftsController(
        IAgentDraftService draftService,
        IAdminAuditLogger audit,
        AdminAuditContext auditContext)
    {
        _draftService = draftService;
        _audit = audit;
        _auditContext = auditContext;
    }

    [HttpPost]
    [SwaggerOperation(Summary = "Cria um rascunho de agent. Aceita payload parcial — invariantes só rodam na aprovação.")]
    [ProducesResponseType(typeof(AgentDraftResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateAgentDraftRequest request, CancellationToken ct)
    {
        try
        {
            var draft = await _draftService.CreateAsync(request.Id, request.Payload ?? new AgentDraftPayload(), ct);

            await _audit.RecordAsync(_auditContext.Build(
                AdminAuditActions.AgentDraftCreated,
                AdminAuditResources.Agent,
                draft.Id,
                payloadAfter: AdminAuditContext.Snapshot(new
                {
                    draftId = draft.Id,
                    isEditDraft = draft.IsEditDraft,
                    baseAgentId = draft.BaseAgentId
                })), ct);

            MetricsRegistry.AgentDraftsCreated.Add(1,
                new KeyValuePair<string, object?>("tenant", draft.TenantId),
                new KeyValuePair<string, object?>("is_edit_draft", draft.IsEditDraft));

            return CreatedAtAction(nameof(GetById), new { id = draft.Id }, AgentDraftResponse.FromDomain(draft));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    [HttpGet]
    [SwaggerOperation(Summary = "Lista rascunhos do projeto atual (owner-only).")]
    [ProducesResponseType(typeof(IReadOnlyList<AgentDraftResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var drafts = await _draftService.ListAsync(ct);
        return Ok(drafts.Select(AgentDraftResponse.FromDomain));
    }

    [HttpGet("{id}")]
    [SwaggerOperation(Summary = "Busca rascunho pelo Id.")]
    [ProducesResponseType(typeof(AgentDraftResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        var draft = await _draftService.GetAsync(id, ct);
        return draft is null ? NotFound() : Ok(AgentDraftResponse.FromDomain(draft));
    }

    [HttpPut("{id}")]
    [SwaggerOperation(Summary = "Atualiza rascunho com optimistic concurrency via expectedUpdatedAt.")]
    [ProducesResponseType(typeof(AgentDraftResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    public async Task<IActionResult> Update(
        string id,
        [FromBody] UpdateAgentDraftRequest request,
        CancellationToken ct)
    {
        try
        {
            var updated = await _draftService.UpdateAsync(id, request.Payload, request.ExpectedUpdatedAt, ct);

            await _audit.RecordAsync(_auditContext.Build(
                AdminAuditActions.AgentDraftUpdated,
                AdminAuditResources.Agent,
                updated.Id,
                payloadAfter: AdminAuditContext.Snapshot(new { draftId = updated.Id, updatedAt = updated.UpdatedAt })), ct);

            return Ok(AgentDraftResponse.FromDomain(updated));
        }
        catch (DraftConcurrencyException ex)
        {
            return StatusCode(StatusCodes.Status412PreconditionFailed, new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = ex.Message });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpDelete("{id}")]
    [SwaggerOperation(Summary = "Descarta rascunho.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        try
        {
            var existing = await _draftService.GetAsync(id, ct);
            if (existing is null) return NotFound();

            await _draftService.DeleteAsync(id, ct);

            await _audit.RecordAsync(_auditContext.Build(
                AdminAuditActions.AgentDraftDeleted,
                AdminAuditResources.Agent,
                id), ct);

            MetricsRegistry.AgentDraftsAbandoned.Add(1,
                new KeyValuePair<string, object?>("tenant", existing.TenantId));

            return NoContent();
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = ex.Message });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpPost("{id}/submit")]
    [SwaggerOperation(Summary = "Submete o rascunho ao painel de aprovação. Aceita Draft|Rejected → PendingApproval. Aprovação humana promove a agent canônico via /api/agent-approvals/{id}/approve.")]
    [ProducesResponseType(typeof(AgentDraftResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Submit(string id, CancellationToken ct)
    {
        try
        {
            var before = await _draftService.GetAsync(id, ct);
            if (before is null) return NotFound();

            var actorUserId = _auditContext.GetActorUserId() ?? "anonymous";
            var wasResubmit = before.Status == AgentDraftStatus.Rejected;

            var result = await _draftService.SubmitForApprovalAsync(id, actorUserId, ct);
            var submitted = result.Draft;

            await _audit.RecordAsync(_auditContext.Build(
                AdminAuditActions.AgentDraftSubmitted,
                AdminAuditResources.Agent,
                submitted.Id,
                payloadAfter: AdminAuditContext.Snapshot(new
                {
                    draftId = submitted.Id,
                    isEditDraft = submitted.IsEditDraft,
                    baseAgentId = submitted.BaseAgentId,
                    wasResubmit,
                    autoApproved = result.AutoApproved,
                    tier = result.Tier?.ToString(),
                })), ct);

            MetricsRegistry.AgentDraftsSubmitted.Add(1,
                new KeyValuePair<string, object?>("tenant", submitted.TenantId),
                new KeyValuePair<string, object?>("was_resubmit", wasResubmit),
                new KeyValuePair<string, object?>("auto_approved", result.AutoApproved));

            // Quando auto-aprovado, o draft já foi consumido e o agent
            // publicado/atualizado. Caller (UI) usa autoApproved=true pra
            // pular a tela de "aguardando aprovação" e voltar pra lista de
            // publicados direto.
            return Ok(new
            {
                draft = AgentDraftResponse.FromDomain(submitted),
                autoApproved = result.AutoApproved,
                tier = result.Tier?.ToString(),
            });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (DraftStatusTransitionException ex)
        {
            return Conflict(new { error = ex.Message, currentStatus = ex.CurrentStatus.ToString() });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = ex.Message });
        }
    }
}
