using EfsAiHub.Core.Abstractions.Exceptions;
using EfsAiHub.Core.Agents;
using EfsAiHub.Host.Api.Models.Requests;
using EfsAiHub.Host.Api.Models.Responses;

namespace EfsAiHub.Host.Api.Controllers;

[ApiController]
[Route("api/admin/predefined-models")]
[Produces("application/json")]
public class PredefinedModelsAdminController : ControllerBase
{
    private readonly IPredefinedModelService _service;
    private readonly IAdminAuditLogger _audit;
    private readonly AdminAuditContext _auditContext;

    public PredefinedModelsAdminController(
        IPredefinedModelService service,
        IAdminAuditLogger audit,
        AdminAuditContext auditContext)
    {
        _service = service;
        _audit = audit;
        _auditContext = auditContext;
    }

    [HttpPost]
    [SwaggerOperation(Summary = "Cria um Predefined Model (preset global do catálogo). Admin-gated.")]
    [ProducesResponseType(typeof(PredefinedModelResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreatePredefinedModelRequest request, CancellationToken ct)
    {
        try
        {
            var saved = await _service.CreateAsync(request.ToDomain(), ct);

            await _audit.RecordAsync(_auditContext.Build(
                AdminAuditActions.PredefinedModelCreated,
                AdminAuditResources.PredefinedModel,
                saved.Id,
                payloadAfter: AdminAuditContext.Snapshot(new
                {
                    id = saved.Id,
                    displayName = saved.DisplayName,
                    provider = saved.Provider,
                    deploymentName = saved.DeploymentName,
                })), ct);

            return CreatedAtAction(nameof(GetById), new { id = saved.Id }, PredefinedModelResponse.FromDomain(saved));
        }
        catch (DomainException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (PredefinedModelIdConflictException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    [HttpGet]
    [SwaggerOperation(Summary = "Lista todos os presets do catálogo. ?includeDisabled=true mostra também os inativos.")]
    [ProducesResponseType(typeof(IReadOnlyList<PredefinedModelResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] bool includeDisabled = true,
        CancellationToken ct = default)
    {
        var presets = await _service.ListAsync(includeDisabled, ct);
        return Ok(presets.Select(PredefinedModelResponse.FromDomain));
    }

    [HttpGet("{id}")]
    [SwaggerOperation(Summary = "Busca preset pelo Id (admin enxerga inclusive disabled).")]
    [ProducesResponseType(typeof(PredefinedModelResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        var preset = await _service.GetAsync(id, ct);
        return preset is null ? NotFound() : Ok(PredefinedModelResponse.FromDomain(preset));
    }

    [HttpPut("{id}")]
    [SwaggerOperation(Summary = "Atualiza preset com optimistic concurrency via expectedUpdatedAt.")]
    [ProducesResponseType(typeof(PredefinedModelResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    public async Task<IActionResult> Update(
        string id,
        [FromBody] UpdatePredefinedModelRequest request,
        CancellationToken ct)
    {
        try
        {
            var updated = await _service.UpdateAsync(id, request.ToDomainPatch(), request.ExpectedUpdatedAt, ct);

            await _audit.RecordAsync(_auditContext.Build(
                AdminAuditActions.PredefinedModelUpdated,
                AdminAuditResources.PredefinedModel,
                updated.Id,
                payloadAfter: AdminAuditContext.Snapshot(new { id = updated.Id, updatedAt = updated.UpdatedAt })), ct);

            return Ok(PredefinedModelResponse.FromDomain(updated));
        }
        catch (DomainException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (PredefinedModelConcurrencyException ex)
        {
            return StatusCode(StatusCodes.Status412PreconditionFailed, new { error = ex.Message });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpDelete("{id}")]
    [SwaggerOperation(Summary = "Remove preset. Agents que referenciam falham ao invocar até serem reapontados.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        try
        {
            await _service.DeleteAsync(id, ct);

            await _audit.RecordAsync(_auditContext.Build(
                AdminAuditActions.PredefinedModelDeleted,
                AdminAuditResources.PredefinedModel,
                id), ct);

            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }
}
