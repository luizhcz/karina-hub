using EfsAiHub.Core.Agents;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EfsAiHub.Host.Api.Health;

/// <summary>
/// Reporta AgentVersions cujo <c>agent_definitions</c> parent foi deletado (orphan).
/// Workflows pinados nessas versions falham em runtime porque a governance source
/// (Visibility/ProjectId/TenantId) está ausente. Estado:
/// <list type="bullet">
///   <item><c>Healthy</c> — sem orphans.</item>
///   <item><c>Degraded</c> — há orphans (lista até 5 IDs no payload).</item>
/// </list>
/// Não derruba o pod (Degraded) porque a falha afeta apenas execuções que tentam
/// resolver esses pins — workflows que não os referenciam continuam OK.
/// </summary>
public sealed class WorkflowAgentVersionHealthCheck : IHealthCheck
{
    private readonly IAgentVersionRepository _versionRepo;

    public WorkflowAgentVersionHealthCheck(IAgentVersionRepository versionRepo)
    {
        _versionRepo = versionRepo;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var orphans = await _versionRepo.ListOrphanVersionsAsync(50, cancellationToken);

            if (orphans.Count == 0)
                return HealthCheckResult.Healthy("Sem AgentVersions orphan.");

            var payload = new Dictionary<string, object>
            {
                ["agent_version_orphans_count"] = orphans.Count,
                ["sample"] = orphans
                    .Take(5)
                    .Select(o => new { agentVersionId = o.AgentVersionId, missingAgentId = o.AgentDefinitionId })
                    .ToList(),
            };

            return HealthCheckResult.Degraded(
                $"{orphans.Count} AgentVersion(s) com agent owner deletado. " +
                "Workflows pinados nessas versions falharão em runtime.",
                data: payload);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "WorkflowAgentVersionHealthCheck falhou ao consultar Postgres.", ex);
        }
    }
}
