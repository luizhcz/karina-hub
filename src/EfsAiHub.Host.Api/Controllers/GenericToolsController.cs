using EfsAiHub.Core.Abstractions.Exceptions;
using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Agents.GenericTools;
using EfsAiHub.Host.Api.Models.Requests;
using EfsAiHub.Host.Api.Models.Responses;

namespace EfsAiHub.Host.Api.Controllers;

[ApiController]
[Route("api/aihub/generic-tools")]
[Produces("application/json")]
public class GenericToolsController : ControllerBase
{
    private readonly IGenericToolService _service;
    private readonly IAdminAuditLogger _audit;
    private readonly AdminAuditContext _auditContext;

    public GenericToolsController(
        IGenericToolService service,
        IAdminAuditLogger audit,
        AdminAuditContext auditContext)
    {
        _service = service;
        _audit = audit;
        _auditContext = auditContext;
    }

    [HttpPost]
    [SwaggerOperation(Summary = "Cria um Generic Tool (HTTP genérico) no projeto atual. Validações: placeholders ↔ path params, headers reservados, FormUrlEncoded plano, timeout ≤ máximo.")]
    [ProducesResponseType(typeof(GenericToolResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateGenericToolRequest request, CancellationToken ct)
    {
        try
        {
            var draft = request.ToDomainTemplate();
            var saved = await _service.CreateAsync(request.Id, draft, ct);

            await _audit.RecordAsync(_auditContext.Build(
                AdminAuditActions.GenericToolCreated,
                AdminAuditResources.GenericTool,
                saved.Id,
                payloadAfter: AdminAuditContext.Snapshot(new
                {
                    toolId = saved.Id,
                    name = saved.Name,
                    httpMethod = saved.HttpMethod.ToString(),
                    projectId = saved.ProjectId,
                })), ct);

            return CreatedAtAction(nameof(GetById), new { id = saved.Id }, GenericToolResponse.FromDomain(saved));
        }
        catch (DomainException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (GenericToolNameConflictException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    [HttpGet]
    [SwaggerOperation(Summary = "Lista os Generic Tools do projeto atual (owner-only).")]
    [ProducesResponseType(typeof(IReadOnlyList<GenericToolResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var tools = await _service.ListAsync(ct);
        return Ok(tools.Select(GenericToolResponse.FromDomain));
    }

    [HttpGet("{id}")]
    [SwaggerOperation(Summary = "Busca Generic Tool pelo Id (owner-only).")]
    [ProducesResponseType(typeof(GenericToolResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        var tool = await _service.GetAsync(id, ct);
        return tool is null ? NotFound() : Ok(GenericToolResponse.FromDomain(tool));
    }

    [HttpPut("{id}")]
    [SwaggerOperation(Summary = "Atualiza Generic Tool com optimistic concurrency via expectedUpdatedAt.")]
    [ProducesResponseType(typeof(GenericToolResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    public async Task<IActionResult> Update(
        string id,
        [FromBody] UpdateGenericToolRequest request,
        CancellationToken ct)
    {
        try
        {
            var patch = request.ToDomainPatch();
            var updated = await _service.UpdateAsync(id, patch, request.ExpectedUpdatedAt, ct);

            await _audit.RecordAsync(_auditContext.Build(
                AdminAuditActions.GenericToolUpdated,
                AdminAuditResources.GenericTool,
                updated.Id,
                payloadAfter: AdminAuditContext.Snapshot(new { toolId = updated.Id, updatedAt = updated.UpdatedAt })), ct);

            return Ok(GenericToolResponse.FromDomain(updated));
        }
        catch (DomainException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (GenericToolNameConflictException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (GenericToolConcurrencyException ex)
        {
            return StatusCode(StatusCodes.Status412PreconditionFailed, new { error = ex.Message });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpDelete("{id}")]
    [SwaggerOperation(Summary = "Remove Generic Tool. Agents que referenciam o tool perdem-no graciosamente em runtime.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        try
        {
            await _service.DeleteAsync(id, ct);

            await _audit.RecordAsync(_auditContext.Build(
                AdminAuditActions.GenericToolDeleted,
                AdminAuditResources.GenericTool,
                id), ct);

            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }
}
