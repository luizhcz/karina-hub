using EfsAiHub.Core.Abstractions.Observability;
using EfsAiHub.Core.Agents;
using EfsAiHub.Host.Api.Models.Requests.Evaluation;
using EfsAiHub.Host.Api.Models.Responses.Evaluation;
using EfsAiHub.Host.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

/// <summary>
/// CRUD da regression config por agente (2 colunas em <c>agent_definitions</c>):
/// <c>RegressionTestSetId</c> e <c>RegressionEvaluatorConfigVersionId</c>.
/// Sem qualquer campo populado, autotrigger é no-op.
/// </summary>
[ApiController]
[Route("api/agents/{agentId}/regression-config")]
[Produces("application/json")]
public sealed class AgentRegressionConfigController : ControllerBase
{
    private readonly IAgentDefinitionRepository _agentRepo;
    private readonly IAdminAuditLogger _audit;
    private readonly AdminAuditContext _auditContext;

    public AgentRegressionConfigController(
        IAgentDefinitionRepository agentRepo,
        IAdminAuditLogger audit,
        AdminAuditContext auditContext)
    {
        _agentRepo = agentRepo;
        _audit = audit;
        _auditContext = auditContext;
    }

    [HttpGet]
    [SwaggerOperation(Summary = "Regression config do agente (autotrigger setup)")]
    [ProducesResponseType(typeof(RegressionConfigResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string agentId, CancellationToken ct)
    {
        var def = await _agentRepo.GetByIdAsync(agentId, ct);
        if (def is null) return NotFound();

        return Ok(new RegressionConfigResponse(
            AgentDefinitionId: def.Id,
            RegressionTestSetId: def.RegressionTestSetId,
            RegressionEvaluatorConfigVersionId: def.RegressionEvaluatorConfigVersionId,
            AutotriggerEnabled: !string.IsNullOrEmpty(def.RegressionTestSetId)
                              && !string.IsNullOrEmpty(def.RegressionEvaluatorConfigVersionId)));
    }

    [HttpPut]
    [SwaggerOperation(Summary = "Atualiza regression config (TestSetId + EvaluatorConfigVersionId)")]
    [ProducesResponseType(typeof(RegressionConfigResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        string agentId, [FromBody] UpsertRegressionConfigRequest request, CancellationToken ct)
    {
        var def = await _agentRepo.GetByIdAsync(agentId, ct);
        if (def is null) return NotFound();

        var updated = new AgentDefinition
        {
            Id = def.Id,
            Name = def.Name,
            Description = def.Description,
            Model = def.Model,
            Provider = def.Provider,
            FallbackProvider = def.FallbackProvider,
            Instructions = def.Instructions,
            Tools = def.Tools,
            StructuredOutput = def.StructuredOutput,
            Middlewares = def.Middlewares,
            SkillRefs = def.SkillRefs,
            Resilience = def.Resilience,
            CostBudget = def.CostBudget,
            Metadata = def.Metadata,
            ProjectId = def.ProjectId,
            CreatedAt = def.CreatedAt,
            UpdatedAt = DateTime.UtcNow,
            RegressionTestSetId = request.RegressionTestSetId,
            RegressionEvaluatorConfigVersionId = request.RegressionEvaluatorConfigVersionId
        };
        var saved = await _agentRepo.UpsertAsync(updated, ct);

        await _audit.RecordAsync(_auditContext.Build(
            "Update",
            "AgentRegressionConfig",
            agentId,
            payloadBefore: AdminAuditContext.Snapshot(new
            {
                def.RegressionTestSetId,
                def.RegressionEvaluatorConfigVersionId
            }),
            payloadAfter: AdminAuditContext.Snapshot(new
            {
                saved.RegressionTestSetId,
                saved.RegressionEvaluatorConfigVersionId
            })), ct);

        return Ok(new RegressionConfigResponse(
            AgentDefinitionId: saved.Id,
            RegressionTestSetId: saved.RegressionTestSetId,
            RegressionEvaluatorConfigVersionId: saved.RegressionEvaluatorConfigVersionId,
            AutotriggerEnabled: !string.IsNullOrEmpty(saved.RegressionTestSetId)
                              && !string.IsNullOrEmpty(saved.RegressionEvaluatorConfigVersionId)));
    }
}
