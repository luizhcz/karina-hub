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
[Route("api/aihub/agents")]
[Produces("application/json")]
public class AgentsController : ControllerBase
{
    private readonly IAgentService _agentService;
    private readonly IAgentVersionRepository _versionRepo;
    private readonly IAgentDraftService _draftService;
    private readonly IAgentRouterIntentLinkRepository _routerIntentLinks;
    private readonly EfsAiHub.Core.Abstractions.Identity.IProjectContextAccessor _projectAccessor;
    private readonly EfsAiHub.Core.Abstractions.Identity.ITenantContextAccessor _tenantAccessor;
    private readonly IAdminAuditLogger _audit;
    private readonly AdminAuditContext _auditContext;

    public AgentsController(
        IAgentService agentService,
        IAgentVersionRepository versionRepo,
        IAgentDraftService draftService,
        IAgentRouterIntentLinkRepository routerIntentLinks,
        EfsAiHub.Core.Abstractions.Identity.IProjectContextAccessor projectAccessor,
        EfsAiHub.Core.Abstractions.Identity.ITenantContextAccessor tenantAccessor,
        IAdminAuditLogger audit,
        AdminAuditContext auditContext)
    {
        _agentService = agentService;
        _versionRepo = versionRepo;
        _draftService = draftService;
        _routerIntentLinks = routerIntentLinks;
        _projectAccessor = projectAccessor;
        _tenantAccessor = tenantAccessor;
        _audit = audit;
        _auditContext = auditContext;
    }

    private async Task<IReadOnlyList<string>?> ReconcileAndLoadRouterIntentsAsync(
        AgentDefinition def,
        IReadOnlyList<string>? requestedIntentIds,
        CancellationToken ct)
    {
        if (def.Type != AgentType.Router) return null;

        // Reconcilia se o caller mandou um set explícito; preserva o existente
        // quando o request veio sem o campo (ex: PUT que só atualiza name).
        if (requestedIntentIds is not null)
        {
            await _routerIntentLinks.SetIntentsForAgentAsync(
                def.Id,
                _projectAccessor.Current.ProjectId,
                _tenantAccessor.Current.TenantId,
                requestedIntentIds,
                ct);
        }

        return await _routerIntentLinks.ListIntentIdsForAgentAsync(def.Id, ct);
    }

    [HttpPost]
    [SwaggerOperation(Summary = "Cria uma definição de agente")]
    [ProducesResponseType(typeof(AgentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateAgentRequest request, CancellationToken ct)
    {
        try
        {
            var domain = request.ToDomain();
            var definition = await _agentService.CreateAsync(domain, ct,
                breakingChange: request.BreakingChange,
                changeReason: request.ChangeReason,
                createdBy: _auditContext.GetActorUserId());

            // Reconcilia o set de intents (Router) na junction após o save.
            // No-op pra Custom. Re-valida depois pra coletar warnings com o
            // estado pós-save (link repo já tem o set vivo).
            var routerIntentIds = await ReconcileAndLoadRouterIntentsAsync(definition, domain.RouterIntentIds, ct);
            definition.RouterIntentIds = routerIntentIds;
            var (_, _, warnings) = await _agentService.ValidateAsync(definition, ct);

            await _audit.RecordAsync(_auditContext.Build(
                AdminAuditActions.Create,
                AdminAuditResources.Agent,
                definition.Id,
                payloadAfter: AdminAuditContext.Snapshot(AgentResponse.FromDomain(definition))), ct);
            return CreatedAtAction(
                nameof(GetById),
                new { id = definition.Id },
                AgentResponse.FromDomain(definition, warnings, routerIntentIds));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet]
    [SwaggerOperation(Summary = "Lista todas as definições de agentes. ?scope=project filtra estritamente pelo projeto atual (ignora Visibility=global de outros projetos do tenant); sem o param ou com qualquer outro valor mantém comportamento legado.")]
    [ProducesResponseType(typeof(IReadOnlyList<AgentResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] string? scope, CancellationToken ct)
    {
        var agents = string.Equals(scope, "project", StringComparison.OrdinalIgnoreCase)
            ? await _agentService.ListByProjectAsync(ct)
            : await _agentService.ListAsync(ct);
        return Ok(agents.Select(a => AgentResponse.FromDomain(a)));
    }

    [HttpGet("{id}")]
    [SwaggerOperation(Summary = "Busca um agente pelo ID")]
    [ProducesResponseType(typeof(AgentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        var agent = await _agentService.GetAsync(id, ct);
        if (agent is null) return NotFound();

        IReadOnlyList<string>? routerIntentIds = null;
        if (agent.Type == AgentType.Router)
            routerIntentIds = await _routerIntentLinks.ListIntentIdsForAgentAsync(agent.Id, ct);

        return Ok(AgentResponse.FromDomain(agent, warnings: null, routerIntentIds: routerIntentIds));
    }

    [HttpPut("{id}")]
    [SwaggerOperation(Summary = "Atualiza uma definição de agente. Admin-only " +
        "(non-admin é empurrado pro fluxo de edit-draft → approve). Toda chamada " +
        "exige ChangeReason e gera entry de AdminOverride em agent_approval_history " +
        "pra trilha de auditoria unificada.")]
    [ProducesResponseType(typeof(AgentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateAgentRequest request, CancellationToken ct)
    {
        try
        {
            var existing = await _agentService.GetAsync(id, ct);
            var before = existing is null ? null : AdminAuditContext.Snapshot(AgentResponse.FromDomain(existing));

            var definition = request.ToDomain();
            var actorUserId = _auditContext.GetActorUserId() ?? "anonymous";
            var updated = await _agentService.UpdateAsync(definition, ct,
                breakingChange: request.BreakingChange,
                changeReason: request.ChangeReason,
                createdBy: actorUserId);

            // Reconcilia o join apenas se o request mandou um set explícito.
            // Re-valida depois pra coletar warnings com o estado pós-save.
            var routerIntentIds = await ReconcileAndLoadRouterIntentsAsync(updated, definition.RouterIntentIds, ct);
            updated.RouterIntentIds = routerIntentIds;

            // Audit operacional (admin_audit_log) + trilha de governança
            // (agent_approval_history). Os dois servem auditores diferentes
            // (SecOps vs Compliance) e precisam coexistir.
            var (_, _, warnings) = await _agentService.ValidateAsync(updated, ct);
            await _audit.RecordAsync(_auditContext.Build(
                AdminAuditActions.Update,
                AdminAuditResources.Agent,
                updated.Id,
                payloadBefore: before,
                payloadAfter: AdminAuditContext.Snapshot(AgentResponse.FromDomain(updated))), ct);
            await _draftService.AppendAdminOverrideAsync(
                agentDefinitionId: updated.Id,
                actorUserId: actorUserId,
                changeReason: request.ChangeReason,
                ct: ct);

            return Ok(AgentResponse.FromDomain(updated, warnings, routerIntentIds));
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

    [HttpPatch("{id}/enabled")]
    [SwaggerOperation(Summary = "Liga/desliga o agent. Workflows que referenciam continuam saváveis; runtime pula o agent quando desligado.")]
    [ProducesResponseType(typeof(AgentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateEnabled(
        string id,
        [FromBody] UpdateAgentEnabledRequest request,
        CancellationToken ct)
    {
        try
        {
            var existing = await _agentService.GetAsync(id, ct);
            if (existing is null) return NotFound();

            var beforeEnabled = existing.Enabled;
            var updated = await _agentService.UpdateEnabledAsync(id, request.Enabled, ct);

            // No-op: já estava no estado pedido.
            if (beforeEnabled == updated.Enabled)
                return Ok(AgentResponse.FromDomain(updated));

            await _audit.RecordAsync(_auditContext.Build(
                AdminAuditActions.AgentEnabledChanged,
                AdminAuditResources.Agent,
                updated.Id,
                payloadBefore: AdminAuditContext.Snapshot(new { enabled = beforeEnabled }),
                payloadAfter: AdminAuditContext.Snapshot(new
                {
                    enabled = updated.Enabled,
                    reason = request.Reason
                })), ct);

            EfsAiHub.Infra.Observability.MetricsRegistry.AgentEnabledChanges.Add(1,
                new KeyValuePair<string, object?>("from", beforeEnabled),
                new KeyValuePair<string, object?>("to", updated.Enabled),
                new KeyValuePair<string, object?>("tenant", updated.TenantId));

            return Ok(AgentResponse.FromDomain(updated));
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

    [HttpPost("{id}/edit-draft")]
    [SwaggerOperation(Summary = "Forka o agent publicado pra um rascunho de edição. Original permanece ativo até a aprovação do rascunho.")]
    [ProducesResponseType(typeof(AgentDraftResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateEditDraft(string id, CancellationToken ct)
    {
        try
        {
            var draft = await _draftService.CreateEditDraftAsync(id, ct);

            await _audit.RecordAsync(_auditContext.Build(
                AdminAuditActions.AgentDraftCreated,
                AdminAuditResources.Agent,
                draft.Id,
                payloadAfter: AdminAuditContext.Snapshot(new
                {
                    draftId = draft.Id,
                    isEditDraft = true,
                    baseAgentId = draft.BaseAgentId,
                    baseRevision = draft.BaseRevision
                })), ct);

            EfsAiHub.Infra.Observability.MetricsRegistry.AgentDraftsCreated.Add(1,
                new KeyValuePair<string, object?>("tenant", draft.TenantId),
                new KeyValuePair<string, object?>("is_edit_draft", true));

            return CreatedAtAction(
                "GetById",
                "AgentDrafts",
                new { id = draft.Id },
                AgentDraftResponse.FromDomain(draft));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpGet("{id}/approval-history")]
    [SwaggerOperation(Summary = "Trilha unificada de approval do agent. " +
        "Soma drafts (Submitted/Approved/Rejected/AutoApproved) com qualquer " +
        "AdminOverride aplicado via PUT direto. Ordenado por OccurredAt asc.")]
    [ProducesResponseType(typeof(IReadOnlyList<AgentApprovalHistoryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetApprovalHistory(string id, CancellationToken ct)
    {
        // Confirma que o agent existe e é visível pro caller (HasQueryFilter
        // já restringe por project/tenant). Sem essa checagem, qualquer um do
        // tenant podia varrer ids alheios.
        var agent = await _agentService.GetAsync(id, ct);
        if (agent is null) return NotFound();

        var entries = await _draftService.GetApprovalHistoryByAgentAsync(id, ct);
        return Ok(entries.Select(AgentApprovalHistoryResponse.FromDomain));
    }

    [HttpDelete("{id}")]
    [SwaggerOperation(Summary = "Remove uma definição de agente")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        try
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
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = ex.Message });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpGet("{id}/versions")]
    [SwaggerOperation(Summary = "Lista todas as revisões (AgentVersion) de um agente, ordenadas por Revision DESC.")]
    [ProducesResponseType(typeof(IReadOnlyList<AgentVersionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListVersions(string id, CancellationToken ct)
    {
        var agent = await _agentService.GetAsync(id, ct);
        if (agent is null) return NotFound();

        var versions = await _versionRepo.ListByDefinitionAsync(id, ct);
        return Ok(versions.Select(AgentVersionResponse.FromDomain));
    }

    [HttpGet("{id}/versions/{versionId}")]
    [SwaggerOperation(Summary = "Busca um snapshot de AgentVersion pelo id (GUID).")]
    [ProducesResponseType(typeof(AgentVersionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetVersion(string id, string versionId, CancellationToken ct)
    {
        var agent = await _agentService.GetAsync(id, ct);
        if (agent is null) return NotFound();

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
        // Rollback não toca o set de intents (snapshot da definition é sobre
        // model/instructions/tools/output; intents vivem na junction). Apenas
        // carrega o estado vivo do join pra response.
        IReadOnlyList<string>? routerIntentIds = null;
        if (updated.Type == AgentType.Router)
            routerIntentIds = await _routerIntentLinks.ListIntentIdsForAgentAsync(updated.Id, ct);
        updated.RouterIntentIds = routerIntentIds;
        var (_, _, warnings) = await _agentService.ValidateAsync(updated, ct);
        return Ok(AgentResponse.FromDomain(updated, warnings, routerIntentIds));
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
        // Rollback: prefere o texto autoral do snapshot pra recompor a partir
        // da fonte editável. Snapshot pré-refactor não tem AuthorPromptContent
        // — recompor a partir de PromptContent ali leva ao composer renderizar
        // texto rendered como se fosse autoral. Fallback explícito ao current
        // pra preservar o estado vivo nesse caso (perde-se rollback de prompt,
        // mas o resto da config da version é aplicado).
        AuthorInstructions = snapshot.AuthorPromptContent ?? current.AuthorInstructions,
        // Instructions é regerado pelo composer no UpdateAsync. Deixar null
        // garante que o fallback (input.AuthorInstructions ?? input.Instructions)
        // não pegue lixo de uma snapshot legada.
        Instructions = null,
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

        var (isValid, errors, warnings) = await _agentService.ValidateAsync(agent, ct);
        return Ok(new { isValid, errors, warnings });
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
                      "Create a single-agent workflow referencing this agent and use POST /api/aihub/workflows/{id}/sandbox.",
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
                      "Use POST /api/aihub/workflows/{id}/sandbox with metadata specifying the target version.",
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
