using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Agents.RouterIntents;
using EfsAiHub.Host.Api.Models.Requests;
using EfsAiHub.Host.Api.Models.Responses;
using EfsAiHub.Platform.Runtime.Interfaces;

namespace EfsAiHub.Host.Api.Controllers;

[ApiController]
[Route("api/aihub/router-intents")]
[Produces("application/json")]
public class RouterIntentsController : ControllerBase
{
    private readonly IRouterIntentService _service;
    private readonly IAdminAuditLogger _audit;
    private readonly AdminAuditContext _auditContext;

    public RouterIntentsController(
        IRouterIntentService service,
        IAdminAuditLogger audit,
        AdminAuditContext auditContext)
    {
        _service = service;
        _audit = audit;
        _auditContext = auditContext;
    }

    [HttpPost]
    [SwaggerOperation(Summary = "Cria uma intent no pool global do tenant. Categoria = ProjectId (FK pra projects). Name único por tenant.")]
    [ProducesResponseType(typeof(RouterIntentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateRouterIntentRequest request, CancellationToken ct)
    {
        try
        {
            var draft = request.ToDomainDraft();
            var saved = await _service.CreateAsync(request.Id, draft, ct);

            await _audit.RecordAsync(_auditContext.Build(
                AdminAuditActions.Create,
                AdminAuditResources.RouterIntent,
                saved.Id,
                payloadAfter: AdminAuditContext.Snapshot(new
                {
                    intentId = saved.Id,
                    name = saved.Name,
                    projectId = saved.ProjectId,
                })), ct);

            return CreatedAtAction(nameof(GetById), new { id = saved.Id }, RouterIntentResponse.FromDomain(saved));
        }
        catch (RouterIntentNameConflictException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet]
    [SwaggerOperation(Summary = "Lista o pool de intents do tenant atual (cross-project).")]
    [ProducesResponseType(typeof(IReadOnlyList<RouterIntentResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var intents = await _service.ListAsync(ct);
        return Ok(intents.Select(RouterIntentResponse.FromDomain));
    }

    [HttpGet("{id}")]
    [SwaggerOperation(Summary = "Busca intent pelo Id.")]
    [ProducesResponseType(typeof(RouterIntentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        var intent = await _service.GetAsync(id, ct);
        return intent is null ? NotFound() : Ok(RouterIntentResponse.FromDomain(intent));
    }

    [HttpPut("{id}")]
    [SwaggerOperation(Summary = "Atualiza intent. Edits propagam pros Routers que referenciam (lookup runtime).")]
    [ProducesResponseType(typeof(RouterIntentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateRouterIntentRequest request, CancellationToken ct)
    {
        try
        {
            var before = await _service.GetAsync(id, ct);
            if (before is null) return NotFound();

            var saved = await _service.UpdateAsync(id, request.ToDomainPatch(), ct);

            await _audit.RecordAsync(_auditContext.Build(
                AdminAuditActions.Update,
                AdminAuditResources.RouterIntent,
                saved.Id,
                payloadBefore: AdminAuditContext.Snapshot(new
                {
                    name = before.Name,
                    projectId = before.ProjectId,
                }),
                payloadAfter: AdminAuditContext.Snapshot(new
                {
                    name = saved.Name,
                    projectId = saved.ProjectId,
                })), ct);

            return Ok(RouterIntentResponse.FromDomain(saved));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (RouterIntentNameConflictException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("{id}")]
    [SwaggerOperation(Summary = "Remove intent. 409 se algum Router referencia (FK RESTRICT) — body lista os agentsReferencing.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        try
        {
            var deleted = await _service.DeleteAsync(id, ct);
            if (!deleted) return NotFound();

            await _audit.RecordAsync(_auditContext.Build(
                AdminAuditActions.Delete,
                AdminAuditResources.RouterIntent,
                id), ct);

            return NoContent();
        }
        catch (RouterIntentInUseException ex)
        {
            return Conflict(new
            {
                error = ex.Message,
                agentsReferencing = ex.AgentIds,
            });
        }
    }

    [HttpGet("{id}/usage")]
    [SwaggerOperation(Summary = "Lista os Router agents que referenciam essa intent. Preview pré-delete.")]
    [ProducesResponseType(typeof(IReadOnlyList<RouterIntentUsageResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUsage(string id, CancellationToken ct)
    {
        var intent = await _service.GetAsync(id, ct);
        if (intent is null) return NotFound();

        var usage = await _service.GetUsageAsync(id, ct);
        return Ok(usage.Select(RouterIntentUsageResponse.FromDomain));
    }

    [HttpPost("analyze")]
    [SwaggerOperation(Summary = "Dispara o agente analyzer (workflow wf-router-intent-analyzer no projeto geral). Retorna executionId; caller polla /executions/{id} pra resultado final.")]
    [ProducesResponseType(typeof(AnalyzeRouterIntentResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Analyze([FromBody] AnalyzeRouterIntentRequest request, CancellationToken ct)
    {
        var input = new AnalyzeRouterIntentInput(
            Description: request.Description,
            Examples: request.Examples ?? Array.Empty<string>(),
            NameHint: request.NameHint,
            DisplayNameHint: request.DisplayNameHint,
            ProjectIdHint: request.ProjectIdHint,
            ExcludeId: request.ExcludeId);

        var executionId = await _service.AnalyzeAsync(input, ct);
        return Accepted(new AnalyzeRouterIntentResponse { ExecutionId = executionId });
    }
}
