using System.Text.Json;
using EfsAiHub.Host.Api.Models.Requests;
using EfsAiHub.Host.Api.Models.Responses;
using EfsAiHub.Host.Api.Services;
using EfsAiHub.Core.Abstractions.Execution;
using EfsAiHub.Core.Abstractions.Observability;
using EfsAiHub.Core.Agents;
using EfsAiHub.Platform.Runtime.Interfaces;
using Microsoft.Agents.AI.Workflows;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

[ApiController]
[Route("api/workflows")]
[Produces("application/json")]
public class WorkflowsController : ControllerBase
{
    private readonly IWorkflowService _workflowService;
    private readonly IWorkflowFactory _workflowFactory;
    private readonly DiagramRenderingService _diagramService;
    private readonly IAdminAuditLogger _audit;
    private readonly AdminAuditContext _auditContext;
    private readonly IWorkflowDefinitionRepository _workflowRepo;
    private readonly IAgentVersionRepository _agentVersionRepo;
    private readonly IWorkflowAgentVersionStatusService _statusService;

    public WorkflowsController(
        IWorkflowService workflowService,
        IWorkflowFactory workflowFactory,
        DiagramRenderingService diagramService,
        IAdminAuditLogger audit,
        AdminAuditContext auditContext,
        IWorkflowDefinitionRepository workflowRepo,
        IAgentVersionRepository agentVersionRepo,
        IWorkflowAgentVersionStatusService statusService)
    {
        _workflowService = workflowService;
        _workflowFactory = workflowFactory;
        _diagramService = diagramService;
        _audit = audit;
        _auditContext = auditContext;
        _workflowRepo = workflowRepo;
        _agentVersionRepo = agentVersionRepo;
        _statusService = statusService;
    }

    [HttpPost]
    [SwaggerOperation(Summary = "Cria uma definição de workflow")]
    [ProducesResponseType(typeof(WorkflowResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateWorkflowRequest request, CancellationToken ct)
    {
        try
        {
            var definition = await _workflowService.CreateAsync(request.ToDomain(), ct);
            await _audit.RecordAsync(_auditContext.Build(
                AdminAuditActions.Create,
                AdminAuditResources.Workflow,
                definition.Id,
                payloadAfter: AdminAuditContext.Snapshot(WorkflowResponse.FromDomain(definition))), ct);
            return CreatedAtAction(nameof(GetById), new { id = definition.Id }, WorkflowResponse.FromDomain(definition));
        }
        catch (EfsAiHub.Core.Orchestration.Validation.WorkflowInvariantViolationException ex)
        {
            return BadRequest(new { errors = ex.Errors });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet]
    [SwaggerOperation(Summary = "Lista todos os workflows")]
    [ProducesResponseType(typeof(IReadOnlyList<WorkflowResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var workflows = await _workflowService.ListAsync(ct);
        return Ok(workflows.Select(WorkflowResponse.FromDomain));
    }

    [HttpGet("{id}")]
    [SwaggerOperation(Summary = "Busca um workflow pelo ID")]
    [ProducesResponseType(typeof(WorkflowResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        var workflow = await _workflowService.GetAsync(id, ct);
        if (workflow is null) return NotFound();
        return Ok(WorkflowResponse.FromDomain(workflow));
    }

    [HttpPut("{id}")]
    [SwaggerOperation(Summary = "Atualiza uma definição de workflow")]
    [ProducesResponseType(typeof(WorkflowResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(string id, [FromBody] CreateWorkflowRequest request, CancellationToken ct)
    {
        try
        {
            var existing = await _workflowService.GetAsync(id, ct);
            var before = existing is null ? null : AdminAuditContext.Snapshot(WorkflowResponse.FromDomain(existing));

            var definition = request.ToDomain();
            var updated = await _workflowService.UpdateAsync(definition, ct);
            await _audit.RecordAsync(_auditContext.Build(
                AdminAuditActions.Update,
                AdminAuditResources.Workflow,
                updated.Id,
                payloadBefore: before,
                payloadAfter: AdminAuditContext.Snapshot(WorkflowResponse.FromDomain(updated))), ct);
            return Ok(WorkflowResponse.FromDomain(updated));
        }
        catch (EfsAiHub.Core.Orchestration.Validation.WorkflowInvariantViolationException ex)
        {
            return BadRequest(new { errors = ex.Errors });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpPatch("{id}/visibility")]
    [SwaggerOperation(Summary = "Altera Visibility de um workflow (project | global)")]
    [ProducesResponseType(typeof(WorkflowResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateVisibility(
        string id,
        [FromBody] UpdateWorkflowVisibilityRequest request,
        CancellationToken ct)
    {
        try
        {
            var existing = await _workflowService.GetAsync(id, ct);
            if (existing is null) return NotFound();

            var beforeVisibility = existing.Visibility;
            var updated = await _workflowService.UpdateVisibilityAsync(id, request.Visibility, ct);

            // No-op (visibility já era a desejada): não emite audit/metric.
            if (string.Equals(beforeVisibility, updated.Visibility, StringComparison.OrdinalIgnoreCase))
                return Ok(WorkflowResponse.FromDomain(updated));

            // Audit dedicado (não polui Update payload).
            await _audit.RecordAsync(_auditContext.Build(
                AdminAuditActions.WorkflowVisibilityChanged,
                AdminAuditResources.Workflow,
                updated.Id,
                payloadBefore: AdminAuditContext.Snapshot(new { visibility = beforeVisibility }),
                payloadAfter: AdminAuditContext.Snapshot(new
                {
                    visibility = updated.Visibility,
                    reason = request.Reason
                })), ct);

            // Telemetry.
            EfsAiHub.Infra.Observability.MetricsRegistry.WorkflowVisibilityChanges.Add(1,
                new KeyValuePair<string, object?>("from", beforeVisibility),
                new KeyValuePair<string, object?>("to", updated.Visibility),
                new KeyValuePair<string, object?>("tenant", updated.TenantId));

            return Ok(WorkflowResponse.FromDomain(updated));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpDelete("{id}")]
    [SwaggerOperation(Summary = "Remove uma definição de workflow")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var existing = await _workflowService.GetAsync(id, ct);
        var before = existing is null ? null : AdminAuditContext.Snapshot(WorkflowResponse.FromDomain(existing));

        await _workflowService.DeleteAsync(id, ct);
        await _audit.RecordAsync(_auditContext.Build(
            AdminAuditActions.Delete,
            AdminAuditResources.Workflow,
            id,
            payloadBefore: before), ct);
        return NoContent();
    }

    [HttpPost("{id}/trigger")]
    [SwaggerOperation(Summary = "Dispara a execução de um workflow. Retorna 202 com executionId imediatamente.")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Trigger(string id, [FromBody] TriggerWorkflowRequest request, CancellationToken ct)
    {
        var executionId = await _workflowService.TriggerAsync(id, request.Input, request.Metadata, ct: ct);
        return Accepted(new
        {
            executionId,
            statusUrl = Url.Action(nameof(ExecutionsController.GetById), "Executions", new { executionId }, Request.Scheme)
        });
    }

    [HttpPost("{id}/sandbox")]
    [SwaggerOperation(Summary = "Executa um workflow em modo sandbox (tools mockadas, sem persistência de chat, métricas tagueadas).")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Sandbox(string id, [FromBody] TriggerWorkflowRequest request, CancellationToken ct)
    {
        var executionId = await _workflowService.TriggerAsync(
            id, request.Input, request.Metadata, mode: ExecutionMode.Sandbox, ct: ct);
        return Accepted(new
        {
            executionId,
            mode = "sandbox",
            statusUrl = Url.Action(nameof(ExecutionsController.GetById), "Executions", new { executionId }, Request.Scheme)
        });
    }

    [HttpGet("{id}/diagram")]
    [Produces("image/png")]
    [SwaggerOperation(Summary = "Gera PNG do workflow. Usa Graphviz local ('dot') se disponível; caso contrário usa mermaid.ink como fallback.")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Diagram(string id, CancellationToken ct)
    {
        var definition = await _workflowService.GetAsync(id, ct);
        if (definition is null) return NotFound();

        var built = await _workflowFactory.BuildWorkflowAsync(definition, ct: ct);
        if (built.IsExposedAsAgent || built.Value is not Workflow workflow)
            return UnprocessableEntity("Workflow está configurado como AIAgent (ExposeAsAgent=true) e não expõe grafo diretamente.");

        var png = await _diagramService.RenderToPngAsync(
            workflow.ToDotString(), workflow.ToMermaidString(), ct);
        return File(png, "image/png", $"{id}.png");
    }

    [HttpPost("{id}/validate")]
    [SwaggerOperation(Summary = "Valida uma definição de workflow sem persistir")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Validate(string id, CancellationToken ct)
    {
        var workflow = await _workflowService.GetAsync(id, ct);
        if (workflow is null) return NotFound();
        var (isValid, errors) = await _workflowService.ValidateAsync(workflow, ct);
        return Ok(new { isValid, errors });
    }

    [HttpGet("{id}/executions")]
    [SwaggerOperation(Summary = "Lista execuções de um workflow (paginado)")]
    [ProducesResponseType(typeof(IReadOnlyList<ExecutionResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetExecutions(
        string id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        CancellationToken ct = default)
    {
        var executions = await _workflowService.GetExecutionsAsync(id, page, pageSize, status, ct);
        return Ok(executions.Select(ExecutionResponse.FromDomain));
    }

    [HttpGet("visible")]
    [SwaggerOperation(Summary = "Lista workflows visíveis para o projeto atual (project + global)")]
    [ProducesResponseType(typeof(IReadOnlyList<WorkflowResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetVisible(CancellationToken ct)
    {
        var workflows = await _workflowService.ListVisibleAsync(ct);
        return Ok(workflows.Select(WorkflowResponse.FromDomain));
    }

    [HttpPost("{id}/clone")]
    [SwaggerOperation(Summary = "Clona um workflow para o projeto atual")]
    [ProducesResponseType(typeof(WorkflowResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Clone(string id, [FromBody] CloneWorkflowRequest? request, CancellationToken ct)
    {
        try
        {
            var cloned = await _workflowService.CloneAsync(id, request?.NewId, ct);
            return CreatedAtAction(nameof(GetById), new { id = cloned.Id }, WorkflowResponse.FromDomain(cloned));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("{id}/versions")]
    [SwaggerOperation(Summary = "Lista todas as versões de um workflow (mais recente primeiro)")]
    [ProducesResponseType(typeof(IReadOnlyList<WorkflowVersionResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetVersions(string id, CancellationToken ct)
    {
        var versions = await _workflowService.ListVersionsAsync(id, ct);
        return Ok(versions.Select(WorkflowVersionResponse.FromDomain));
    }

    [HttpGet("{id}/versions/{versionId}")]
    [SwaggerOperation(Summary = "Busca uma versão específica de um workflow")]
    [ProducesResponseType(typeof(WorkflowVersionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetVersion(string id, string versionId, CancellationToken ct)
    {
        var version = await _workflowService.GetVersionAsync(versionId, ct);
        if (version is null || version.WorkflowDefinitionId != id) return NotFound();
        return Ok(WorkflowVersionResponse.FromDomain(version));
    }

    [HttpPost("{id}/rollback")]
    [SwaggerOperation(Summary = "Restaura um workflow para uma versão anterior")]
    [ProducesResponseType(typeof(WorkflowResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Rollback(
        string id, [FromBody] RollbackWorkflowRequest request, CancellationToken ct)
    {
        try
        {
            var definition = await _workflowService.RollbackAsync(id, request.VersionId, ct);
            return Ok(WorkflowResponse.FromDomain(definition));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("{id}/agent-version-status")]
    [SwaggerOperation(Summary = "Estado de pin de cada agent ref do workflow — usado pela UI de migration")]
    [ProducesResponseType(typeof(IReadOnlyList<WorkflowAgentVersionStatusResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAgentVersionStatus(string id, CancellationToken ct)
    {
        try
        {
            var status = await _statusService.GetStatusAsync(id, ct);
            var response = status.Select(s => new WorkflowAgentVersionStatusResponse
            {
                AgentId = s.AgentId,
                AgentName = s.AgentName,
                PinnedVersionId = s.PinnedVersionId,
                PinnedRevision = s.PinnedRevision,
                CurrentVersionId = s.CurrentVersionId,
                CurrentRevision = s.CurrentRevision,
                IsPinnedBlockedByBreaking = s.IsPinnedBlockedByBreaking,
                HasUpdate = s.HasUpdate,
                Changes = s.Changes
                    .Select(c => new EfsAiHub.Host.Api.Models.Responses.WorkflowAgentVersionChangeEntry
                    {
                        AgentVersionId = c.AgentVersionId,
                        Revision = c.Revision,
                        BreakingChange = c.BreakingChange,
                        ChangeReason = c.ChangeReason,
                        CreatedAt = c.CreatedAt,
                        CreatedBy = c.CreatedBy,
                    })
                    .ToList(),
            }).ToList();
            return Ok(response);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpPatch("{id}/agents/{agentId}/pin")]
    [SwaggerOperation(Summary = "Atualiza pin de version de um agent ref específico do workflow")]
    [ProducesResponseType(typeof(WorkflowResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAgentPin(
        string id,
        string agentId,
        [FromBody] UpdateWorkflowAgentPinRequest request,
        CancellationToken ct)
    {
        var workflow = await _workflowRepo.GetByIdAsync(id, ct);
        if (workflow is null) return NotFound();

        var agentRef = workflow.Agents.FirstOrDefault(
            a => string.Equals(a.AgentId, agentId, StringComparison.OrdinalIgnoreCase));
        if (agentRef is null)
            return NotFound(new { error = $"Agent '{agentId}' não está referenciado no workflow." });

        var newVersion = await _agentVersionRepo.GetByIdAsync(request.NewVersionId, ct);
        if (newVersion is null
            || !string.Equals(newVersion.AgentDefinitionId, agentId, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                error = $"AgentVersion '{request.NewVersionId}' não encontrada ou não pertence ao agent '{agentId}'.",
            });
        }

        var previousVersionId = agentRef.AgentVersionId;
        agentRef.AgentVersionId = newVersion.AgentVersionId;
        await _workflowRepo.UpsertAsync(workflow, ct);

        var payload = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            agentId,
            previousVersionId,
            newVersionId = newVersion.AgentVersionId,
            wasBreaking = newVersion.BreakingChange,
            reason = request.Reason,
        }));
        await _audit.RecordAsync(_auditContext.Build(
            AdminAuditActions.WorkflowAgentVersionPinned,
            AdminAuditResources.Workflow,
            id,
            payloadAfter: payload), ct);

        return Ok(WorkflowResponse.FromDomain(workflow));
    }
}
