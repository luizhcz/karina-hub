using EfsAiHub.Host.Api.Models.Requests;
using EfsAiHub.Host.Api.Models.Responses;
using EfsAiHub.Core.Abstractions.Execution;
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

    public WorkflowsController(
        IWorkflowService workflowService,
        IWorkflowFactory workflowFactory,
        DiagramRenderingService diagramService)
    {
        _workflowService = workflowService;
        _workflowFactory = workflowFactory;
        _diagramService = diagramService;
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
            return CreatedAtAction(nameof(GetById), new { id = definition.Id }, WorkflowResponse.FromDomain(definition));
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
            var definition = request.ToDomain();
            var updated = await _workflowService.UpdateAsync(definition, ct);
            return Ok(WorkflowResponse.FromDomain(updated));
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
        await _workflowService.DeleteAsync(id, ct);
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

    // ── Catalog ───────────────────────────────────────────────────────────

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

    // ── Versioning ──────────────────────────────────────────────────────────

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
}
