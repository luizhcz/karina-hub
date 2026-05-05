using EfsAiHub.Core.Abstractions.Exceptions;
using EfsAiHub.Host.Api.Models.Requests;
using EfsAiHub.Host.Api.Models.Responses;

namespace EfsAiHub.Host.Api.Controllers;

[ApiController]
[Route("api/aihub/agent-approvals")]
[Produces("application/json")]
public class AgentApprovalsController : ControllerBase
{
    private readonly IAgentApprovalService _approvalService;
    private readonly IAdminAuditLogger _audit;
    private readonly AdminAuditContext _auditContext;

    public AgentApprovalsController(
        IAgentApprovalService approvalService,
        IAdminAuditLogger audit,
        AdminAuditContext auditContext)
    {
        _approvalService = approvalService;
        _audit = audit;
        _auditContext = auditContext;
    }

    [HttpGet]
    [SwaggerOperation(Summary = "Lista rascunhos no painel de aprovação. Default status=pending; aceita pending|rejected.")]
    [ProducesResponseType(typeof(IReadOnlyList<AgentDraftResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List([FromQuery] string? status, CancellationToken ct)
    {
        AgentDraftStatus? resolved;
        try
        {
            resolved = ParseStatus(status);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        var items = resolved is null
            ? await _approvalService.ListPendingAsync(ct)
            : await _approvalService.ListByStatusAsync(resolved.Value, ct);
        return Ok(items.Select(AgentDraftResponse.FromDomain));
    }

    [HttpGet("{id}")]
    [SwaggerOperation(Summary = "Detalhe do rascunho pendente (tenant-scope).")]
    [ProducesResponseType(typeof(AgentDraftResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        var draft = await _approvalService.GetAsync(id, ct);
        return draft is null ? NotFound() : Ok(AgentDraftResponse.FromDomain(draft));
    }

    [HttpPost("{id}/approve")]
    [SwaggerOperation(Summary = "Aprova o rascunho. Promove a agent canônico (cria AgentVersion + remove draft).")]
    [ProducesResponseType(typeof(AgentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Approve(
        string id,
        [FromBody] ApproveAgentDraftRequest? request,
        CancellationToken ct)
    {
        try
        {
            var draftBefore = await _approvalService.GetAsync(id, ct);
            if (draftBefore is null) return NotFound();

            var actorUserId = _auditContext.GetActorUserId() ?? "anonymous";
            var published = await _approvalService.ApproveAsync(id, actorUserId, request?.ChangeReason, ct);

            var ageHours = (DateTime.UtcNow - draftBefore.CreatedAt).TotalHours;

            await _audit.RecordAsync(_auditContext.Build(
                AdminAuditActions.AgentDraftApproved,
                AdminAuditResources.Agent,
                published.Id,
                payloadAfter: AdminAuditContext.Snapshot(new
                {
                    agentId = published.Id,
                    fromDraftId = id,
                    wasEditDraft = draftBefore.IsEditDraft,
                    approverUserId = actorUserId,
                    ageHours,
                })), ct);

            MetricsRegistry.AgentDraftsPublished.Add(1,
                new KeyValuePair<string, object?>("tenant", published.TenantId),
                new KeyValuePair<string, object?>("was_edit_draft", draftBefore.IsEditDraft));
            MetricsRegistry.AgentDraftAgeHours.Record(ageHours,
                new KeyValuePair<string, object?>("tenant", published.TenantId));

            // Latência só faz sentido com SubmittedAt. Em fluxo normal, draft
            // em PendingApproval sempre tem SubmittedAt; defensive skip evita
            // pollutar histograma com bucket 0 em casos anormais (backfill manual).
            if (draftBefore.SubmittedAt is DateTime submittedAt)
            {
                var latencyHours = (DateTime.UtcNow - submittedAt).TotalHours;
                MetricsRegistry.AgentApprovalLatencyHours.Record(latencyHours,
                    new KeyValuePair<string, object?>("tenant", published.TenantId),
                    new KeyValuePair<string, object?>("outcome", "approved"));
            }

            return CreatedAtAction(
                "GetById",
                "Agents",
                new { id = published.Id },
                AgentResponse.FromDomain(published));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (DraftRePublishRaceException ex)
        {
            return Conflict(new { error = ex.Message, baseRevision = ex.BaseRevision, currentRevision = ex.CurrentRevision });
        }
        catch (DraftStatusTransitionException ex)
        {
            return Conflict(new { error = ex.Message, currentStatus = ex.CurrentStatus.ToString() });
        }
        catch (DomainException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{id}/reject")]
    [SwaggerOperation(Summary = "Rejeita o rascunho com feedback obrigatório. Owner re-edita ou re-submete.")]
    [ProducesResponseType(typeof(AgentDraftResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reject(
        string id,
        [FromBody] RejectAgentDraftRequest request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var draftBefore = await _approvalService.GetAsync(id, ct);
            if (draftBefore is null) return NotFound();

            var actorUserId = _auditContext.GetActorUserId() ?? "anonymous";
            var rejected = await _approvalService.RejectAsync(id, actorUserId, request.Feedback, ct);

            await _audit.RecordAsync(_auditContext.Build(
                AdminAuditActions.AgentDraftRejected,
                AdminAuditResources.Agent,
                rejected.Id,
                payloadAfter: AdminAuditContext.Snapshot(new
                {
                    draftId = rejected.Id,
                    approverUserId = actorUserId,
                    feedback = request.Feedback,
                })), ct);

            MetricsRegistry.AgentDraftsRejected.Add(1,
                new KeyValuePair<string, object?>("tenant", rejected.TenantId));

            if (draftBefore.SubmittedAt is DateTime submittedAt)
            {
                var latencyHours = (DateTime.UtcNow - submittedAt).TotalHours;
                MetricsRegistry.AgentApprovalLatencyHours.Record(latencyHours,
                    new KeyValuePair<string, object?>("tenant", rejected.TenantId),
                    new KeyValuePair<string, object?>("outcome", "rejected"));
            }

            return Ok(AgentDraftResponse.FromDomain(rejected));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (DraftStatusTransitionException ex)
        {
            return Conflict(new { error = ex.Message, currentStatus = ex.CurrentStatus.ToString() });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("{id}/history")]
    [SwaggerOperation(Summary = "Histórico de transições do rascunho (Submitted / Resubmitted / Approved / Rejected).")]
    [ProducesResponseType(typeof(IReadOnlyList<AgentApprovalHistoryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> History(string id, CancellationToken ct)
    {
        var entries = await _approvalService.GetHistoryAsync(id, ct);
        return Ok(entries.Select(AgentApprovalHistoryResponse.FromDomain));
    }

    // Whitelist: o painel só faz sentido pra status que envolvem o ciclo de
    // aprovação. Rejeita Draft (estado pré-submissão, owner-scoped) e valores
    // desconhecidos com 400 explícito ao invés de fallback silencioso.
    private static AgentDraftStatus? ParseStatus(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        if (Enum.TryParse<AgentDraftStatus>(raw, ignoreCase: true, out var parsed))
        {
            if (parsed == AgentDraftStatus.PendingApproval || parsed == AgentDraftStatus.Rejected)
                return parsed;
        }

        // Aceita aliases comuns ("pending" → PendingApproval).
        if (string.Equals(raw, "pending", StringComparison.OrdinalIgnoreCase))
            return AgentDraftStatus.PendingApproval;
        if (string.Equals(raw, "rejected", StringComparison.OrdinalIgnoreCase))
            return AgentDraftStatus.Rejected;

        throw new ArgumentException(
            $"Status '{raw}' inválido pra painel de aprovação. Valores aceitos: pending, rejected.");
    }
}
