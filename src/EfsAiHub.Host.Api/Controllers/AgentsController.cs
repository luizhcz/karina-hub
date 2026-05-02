using System.Text.Json;
using EfsAiHub.Core.Agents;
using EfsAiHub.Host.Api.Models.Requests;
using EfsAiHub.Host.Api.Models.Responses;
using EfsAiHub.Host.Api.Services;
using EfsAiHub.Core.Abstractions.Execution;
using EfsAiHub.Core.Abstractions.Observability;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

[ApiController]
[Route("api/agents")]
[Produces("application/json")]
public class AgentsController : ControllerBase
{
    private readonly IAgentService _agentService;
    private readonly IAgentVersionRepository _versionRepo;
    private readonly IAdminAuditLogger _audit;
    private readonly AdminAuditContext _auditContext;

    public AgentsController(
        IAgentService agentService,
        IAgentVersionRepository versionRepo,
        IAdminAuditLogger audit,
        AdminAuditContext auditContext)
    {
        _agentService = agentService;
        _versionRepo = versionRepo;
        _audit = audit;
        _auditContext = auditContext;
    }

    [HttpPost]
    [SwaggerOperation(Summary = "Cria uma definição de agente")]
    [ProducesResponseType(typeof(AgentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateAgentRequest request, CancellationToken ct)
    {
        try
        {
            var definition = await _agentService.CreateAsync(request.ToDomain(), ct,
                breakingChange: request.BreakingChange,
                changeReason: request.ChangeReason,
                createdBy: _auditContext.GetActorUserId());
            await _audit.RecordAsync(_auditContext.Build(
                AdminAuditActions.Create,
                AdminAuditResources.Agent,
                definition.Id,
                payloadAfter: AdminAuditContext.Snapshot(AgentResponse.FromDomain(definition))), ct);
            return CreatedAtAction(nameof(GetById), new { id = definition.Id }, AgentResponse.FromDomain(definition));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet]
    [SwaggerOperation(Summary = "Lista todas as definições de agentes")]
    [ProducesResponseType(typeof(IReadOnlyList<AgentResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var agents = await _agentService.ListAsync(ct);
        return Ok(agents.Select(AgentResponse.FromDomain));
    }

    [HttpGet("{id}")]
    [SwaggerOperation(Summary = "Busca um agente pelo ID")]
    [ProducesResponseType(typeof(AgentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        var agent = await _agentService.GetAsync(id, ct);
        if (agent is null) return NotFound();
        return Ok(AgentResponse.FromDomain(agent));
    }

    [HttpPut("{id}")]
    [SwaggerOperation(Summary = "Atualiza uma definição de agente")]
    [ProducesResponseType(typeof(AgentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(string id, [FromBody] CreateAgentRequest request, CancellationToken ct)
    {
        try
        {
            var existing = await _agentService.GetAsync(id, ct);
            var before = existing is null ? null : AdminAuditContext.Snapshot(AgentResponse.FromDomain(existing));

            var definition = request.ToDomain();
            var updated = await _agentService.UpdateAsync(definition, ct,
                breakingChange: request.BreakingChange,
                changeReason: request.ChangeReason,
                createdBy: _auditContext.GetActorUserId());
            await _audit.RecordAsync(_auditContext.Build(
                AdminAuditActions.Update,
                AdminAuditResources.Agent,
                updated.Id,
                payloadBefore: before,
                payloadAfter: AdminAuditContext.Snapshot(AgentResponse.FromDomain(updated))), ct);
            return Ok(AgentResponse.FromDomain(updated));
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
    [SwaggerOperation(Summary = "Altera Visibility de um agent (project | global)")]
    [ProducesResponseType(typeof(AgentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateVisibility(
        string id,
        [FromBody] UpdateAgentVisibilityRequest request,
        CancellationToken ct)
    {
        try
        {
            var existing = await _agentService.GetAsync(id, ct);
            if (existing is null) return NotFound();

            var beforeVisibility = existing.Visibility;
            var updated = await _agentService.UpdateVisibilityAsync(id, request.Visibility, ct);

            // No-op: visibility já era a desejada — sem audit/metric.
            if (string.Equals(beforeVisibility, updated.Visibility, StringComparison.OrdinalIgnoreCase))
                return Ok(AgentResponse.FromDomain(updated));

            await _audit.RecordAsync(_auditContext.Build(
                AdminAuditActions.AgentVisibilityChanged,
                AdminAuditResources.Agent,
                updated.Id,
                payloadBefore: AdminAuditContext.Snapshot(new { visibility = beforeVisibility }),
                payloadAfter: AdminAuditContext.Snapshot(new
                {
                    visibility = updated.Visibility,
                    reason = request.Reason
                })), ct);

            EfsAiHub.Infra.Observability.MetricsRegistry.AgentVisibilityChanges.Add(1,
                new KeyValuePair<string, object?>("from", beforeVisibility),
                new KeyValuePair<string, object?>("to", updated.Visibility),
                new KeyValuePair<string, object?>("tenant", updated.TenantId));

            return Ok(AgentResponse.FromDomain(updated));
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
    [SwaggerOperation(Summary = "Remove uma definição de agente")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var existing = await _agentService.GetAsync(id, ct);
        var before = existing is null ? null : AdminAuditContext.Snapshot(AgentResponse.FromDomain(existing));

        await _agentService.DeleteAsync(id, ct);
        await _audit.RecordAsync(_auditContext.Build(
            AdminAuditActions.Delete,
            AdminAuditResources.Agent,
            id,
            payloadBefore: before), ct);
        return NoContent();
    }

    [HttpGet("{id}/versions")]
    [SwaggerOperation(Summary = "Lista todas as revisões (AgentVersion) de um agente, ordenadas por Revision DESC.")]
    [ProducesResponseType(typeof(IReadOnlyList<AgentVersionResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListVersions(string id, CancellationToken ct)
    {
        var versions = await _versionRepo.ListByDefinitionAsync(id, ct);
        return Ok(versions.Select(AgentVersionResponse.FromDomain));
    }

    [HttpGet("{id}/versions/{versionId}")]
    [SwaggerOperation(Summary = "Busca um snapshot de AgentVersion pelo id (GUID).")]
    [ProducesResponseType(typeof(AgentVersionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetVersion(string id, string versionId, CancellationToken ct)
    {
        var v = await _versionRepo.GetByIdAsync(versionId, ct);
        if (v is null || !string.Equals(v.AgentDefinitionId, id, StringComparison.OrdinalIgnoreCase))
            return NotFound();
        return Ok(AgentVersionResponse.FromDomain(v));
    }

    [HttpPost("{id}/versions")]
    [SwaggerOperation(Summary = "Publica nova AgentVersion com intent declarado (breaking ou patch). " +
                                "BreakingChange=true exige ChangeReason. Idempotente por ContentHash.")]
    [ProducesResponseType(typeof(AgentVersionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PublishVersion(
        string id,
        [FromBody] PublishAgentVersionRequest request,
        CancellationToken ct)
    {
        try
        {
            var version = await _agentService.PublishVersionAsync(
                id,
                request.BreakingChange,
                request.ChangeReason,
                createdBy: _auditContext.GetActorUserId(),
                ct);
            return CreatedAtAction(
                nameof(GetVersion),
                new { id, versionId = version.AgentVersionId },
                AgentVersionResponse.FromDomain(version));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = ex.Message });
        }
        catch (EfsAiHub.Core.Abstractions.Exceptions.DomainException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{id}/rollback")]
    [SwaggerOperation(Summary = "Rollback determinístico: reconstrói o AgentDefinition a partir de uma AgentVersion e gera uma nova revision idêntica à alvo.")]
    [ProducesResponseType(typeof(AgentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Rollback(string id, [FromBody] RollbackAgentRequest request, CancellationToken ct)
    {
        var current = await _agentService.GetAsync(id, ct);
        if (current is null) return NotFound();

        var target = await _versionRepo.GetByIdAsync(request.TargetVersionId, ct);
        if (target is null || !string.Equals(target.AgentDefinitionId, id, StringComparison.OrdinalIgnoreCase))
            return NotFound(new { error = $"AgentVersion '{request.TargetVersionId}' not found for agent '{id}'." });

        var rebuilt = RebuildFromSnapshot(current, target);
        var updated = await _agentService.UpdateAsync(rebuilt, ct);
        return Ok(AgentResponse.FromDomain(updated));
    }

    private static AgentDefinition RebuildFromSnapshot(AgentDefinition current, AgentVersion snapshot) => new()
    {
        Id = current.Id,
        Name = current.Name,
        Description = current.Description,
        Metadata = current.Metadata,
        CreatedAt = current.CreatedAt,

        // Campos versionados — vêm do snapshot.
        Model = new AgentModelConfig
        {
            DeploymentName = snapshot.Model.DeploymentName,
            Temperature = snapshot.Model.Temperature,
            MaxTokens = snapshot.Model.MaxTokens
        },
        Provider = new AgentProviderConfig
        {
            Type = snapshot.Provider.Type,
            ClientType = snapshot.Provider.ClientType,
            Endpoint = snapshot.Provider.Endpoint
        },
        Instructions = snapshot.PromptContent ?? current.Instructions,
        Tools = snapshot.Tools is null
            ? current.Tools
            : snapshot.Tools.Select(t => t.ToDefinition()).ToList(),
        Middlewares = snapshot.MiddlewarePipeline
            .Select(m => new AgentMiddlewareConfig
            {
                Type = m.Type,
                Enabled = m.Enabled,
                Settings = new Dictionary<string, string>(m.Settings)
            })
            .ToList(),
        StructuredOutput = snapshot.OutputSchema is null ? null : new AgentStructuredOutputDefinition
        {
            ResponseFormat = snapshot.OutputSchema.ResponseFormat,
            SchemaName = snapshot.OutputSchema.SchemaName,
            SchemaDescription = snapshot.OutputSchema.SchemaDescription,
            Schema = snapshot.OutputSchema.SchemaJson is null ? null : JsonDocument.Parse(snapshot.OutputSchema.SchemaJson)
        },
        Resilience = snapshot.Resilience,
        CostBudget = snapshot.CostBudget,
        SkillRefs = snapshot.SkillRefs.ToList()
    };

    [HttpPost("{id}/validate")]
    [SwaggerOperation(Summary = "Valida uma definição de agente")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Validate(string id, CancellationToken ct)
    {
        var agent = await _agentService.GetAsync(id, ct);
        if (agent is null) return NotFound();

        var (isValid, errors) = await _agentService.ValidateAsync(agent, ct);
        return Ok(new { isValid, errors });
    }

    [HttpPost("{id}/sandbox")]
    [SwaggerOperation(Summary = "Testa um agente em modo sandbox (tools mockadas, LLM real, sem persistência de chat).")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Sandbox(string id, [FromBody] SandboxAgentRequest request, CancellationToken ct)
    {
        var agent = await _agentService.GetAsync(id, ct);
        if (agent is null) return NotFound();

        return Ok(new
        {
            agentId = id,
            mode = ExecutionMode.Sandbox.ToString(),
            message = "Sandbox execution is available via workflow trigger with mode=sandbox. " +
                      "Create a single-agent workflow referencing this agent and use POST /api/workflows/{id}/sandbox.",
            input = request.Input
        });
    }

    [HttpPost("{id}/compare")]
    [SwaggerOperation(Summary = "Compara duas versões de um agente com o mesmo input (sandbox).")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Compare(string id, [FromBody] CompareAgentRequest request, CancellationToken ct)
    {
        var agent = await _agentService.GetAsync(id, ct);
        if (agent is null) return NotFound();

        var versionA = await _versionRepo.GetByIdAsync(request.VersionIdA, ct);
        var versionB = await _versionRepo.GetByIdAsync(request.VersionIdB, ct);

        if (versionA is null || !string.Equals(versionA.AgentDefinitionId, id, StringComparison.OrdinalIgnoreCase))
            return NotFound(new { error = $"AgentVersion '{request.VersionIdA}' not found for agent '{id}'." });
        if (versionB is null || !string.Equals(versionB.AgentDefinitionId, id, StringComparison.OrdinalIgnoreCase))
            return NotFound(new { error = $"AgentVersion '{request.VersionIdB}' not found for agent '{id}'." });

        return Ok(new
        {
            agentId = id,
            mode = ExecutionMode.Sandbox.ToString(),
            versionA = new { versionA.AgentVersionId, versionA.Revision, versionA.ContentHash },
            versionB = new { versionB.AgentVersionId, versionB.Revision, versionB.ContentHash },
            message = "Version comparison requires sandbox execution of both versions. " +
                      "Use POST /api/workflows/{id}/sandbox with metadata specifying the target version.",
            input = request.Input
        });
    }
}

public class SandboxAgentRequest
{
    public string? Input { get; init; }
    public IReadOnlyList<string>? MockTools { get; init; }
}

public class CompareAgentRequest
{
    public required string VersionIdA { get; init; }
    public required string VersionIdB { get; init; }
    public string? Input { get; init; }
}
