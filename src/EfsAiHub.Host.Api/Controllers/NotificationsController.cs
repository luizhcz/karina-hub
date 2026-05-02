using EfsAiHub.Core.Agents;
using EfsAiHub.Host.Api.Models.Responses;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

[ApiController]
[Route("api/notifications")]
[Produces("application/json")]
public class NotificationsController : ControllerBase
{
    private readonly IAgentVersionRepository _versionRepo;
    private readonly IAgentDefinitionRepository _agentRepo;

    public NotificationsController(
        IAgentVersionRepository versionRepo,
        IAgentDefinitionRepository agentRepo)
    {
        _versionRepo = versionRepo;
        _agentRepo = agentRepo;
    }

    [HttpGet("agent-breaking-changes")]
    [SwaggerOperation(Summary = "Lista AgentVersions com BreakingChange=true publicadas " +
                                "nos últimos N dias (default 7) — alimenta notification bell.")]
    [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Client)]
    [ProducesResponseType(typeof(IReadOnlyList<AgentBreakingChangeNotification>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetBreakingChanges(
        [FromQuery] int days = 7,
        CancellationToken ct = default)
    {
        if (days <= 0 || days > 90)
            return BadRequest(new { error = "Parâmetro 'days' deve estar entre 1 e 90." });

        var versions = await _versionRepo.ListRecentBreakingAsync(days, ct);
        if (versions.Count == 0)
            return Ok(Array.Empty<AgentBreakingChangeNotification>());

        // Lookup names em batch (1 query por agent — aceitável dada limit de 50).
        // Otimização futura: bulk fetch via repository method dedicado.
        var agentIds = versions.Select(v => v.AgentDefinitionId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var agentMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in agentIds)
        {
            var agent = await _agentRepo.GetByIdAsync(id, ct);
            if (agent is not null) agentMap[id] = agent.Name;
        }

        var response = versions.Select(v => new AgentBreakingChangeNotification
        {
            AgentId = v.AgentDefinitionId,
            AgentName = agentMap.GetValueOrDefault(v.AgentDefinitionId),
            AgentVersionId = v.AgentVersionId,
            Revision = v.Revision,
            ChangeReason = v.ChangeReason,
            CreatedAt = v.CreatedAt,
            CreatedBy = v.CreatedBy,
        }).ToList();

        return Ok(response);
    }
}
