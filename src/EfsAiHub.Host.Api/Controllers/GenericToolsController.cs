using EfsAiHub.Core.Abstractions.Exceptions;
using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Agents.GenericTools;
using EfsAiHub.Host.Api.Models.Requests;
using EfsAiHub.Host.Api.Models.Responses;
using EfsAiHub.Platform.Runtime.Tools.Generic;

namespace EfsAiHub.Host.Api.Controllers;

[ApiController]
[Route("api/aihub/generic-tools")]
[Produces("application/json")]
public class GenericToolsController : ControllerBase
{
    private readonly IGenericToolService _service;
    private readonly IGenericToolTester _tester;
    private readonly IAdminAuditLogger _audit;
    private readonly AdminAuditContext _auditContext;
    private readonly IProjectContextAccessor _projectAccessor;
    private readonly ITenantContextAccessor _tenantAccessor;

    public GenericToolsController(
        IGenericToolService service,
        IGenericToolTester tester,
        IAdminAuditLogger audit,
        AdminAuditContext auditContext,
        IProjectContextAccessor projectAccessor,
        ITenantContextAccessor tenantAccessor)
    {
        _service = service;
        _tester = tester;
        _audit = audit;
        _auditContext = auditContext;
        _projectAccessor = projectAccessor;
        _tenantAccessor = tenantAccessor;
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

    [HttpPost("test-draft")]
    [SwaggerOperation(Summary = "Testa uma config de tool ANTES dela ser persistida. UI força o PM a validar a ferramenta funcionando contra o endpoint real antes de habilitar 'Criar'. Sem write, sem audit, sem métricas. Valida invariantes do domain antes de executar — 400 se config inválida.")]
    [ProducesResponseType(typeof(GenericToolTestResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> TestDraft(
        [FromBody] TestDraftGenericToolRequest request,
        CancellationToken ct)
    {
        try
        {
            var draft = request.Tool.ToDomainTemplate();
            // Materializa identidade efêmera pra que EnsureInvariants passe
            // (Id/ProjectId/TenantId são obrigatórios) e pra que o tester
            // tenha o projeto correto pra resolução de credenciais.
            var sandbox = new GenericTool
            {
                Id = $"draft-test:{Guid.NewGuid():N}",
                ProjectId = _projectAccessor.Current.ProjectId,
                TenantId = _tenantAccessor.Current.TenantId,
                Name = draft.Name,
                HttpMethod = draft.HttpMethod,
                UrlTemplate = draft.UrlTemplate,
                PathParams = draft.PathParams,
                QueryParams = draft.QueryParams,
                CustomHeaders = draft.CustomHeaders,
                InputContentType = draft.InputContentType,
                InputSchema = draft.InputSchema,
                OutputContentType = draft.OutputContentType,
                OutputSchema = draft.OutputSchema,
                OutputProjectionMode = draft.OutputContentType == OutputContentType.Text
                    ? OutputProjectionMode.Off
                    : OutputProjectionMode.Project,
                TimeoutSecondsOverride = draft.TimeoutSecondsOverride,
                IsExclusive = draft.IsExclusive,
            };
            sandbox.EnsureInvariants();

            var rawArgs = request.Args ?? new();
            var args = rawArgs.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
            var result = await _tester.TestAsync(sandbox, args, ct);
            return Ok(GenericToolTestResponse.FromResult(result));
        }
        catch (DomainException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{id}/execute")]
    [SwaggerOperation(Summary = "Executa o Generic Tool isoladamente pra teste. Sem audit, sem métricas, timeout fixo de 10s. Retorna envelope verboso (status, headers, body cru, parsed) — não usar em runtime de agente.")]
    [ProducesResponseType(typeof(GenericToolTestResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Execute(
        string id,
        [FromBody] ExecuteGenericToolRequest request,
        CancellationToken ct)
    {
        var tool = await _service.GetAsync(id, ct);
        if (tool is null) return NotFound();

        var rawArgs = request.Args ?? new();
        var args = rawArgs.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);

        var result = await _tester.TestAsync(tool, args, ct);
        return Ok(GenericToolTestResponse.FromResult(result));
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
