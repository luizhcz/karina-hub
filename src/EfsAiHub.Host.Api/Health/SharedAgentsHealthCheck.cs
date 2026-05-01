using EfsAiHub.Core.Agents;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EfsAiHub.Host.Api.Health;

/// <summary>
/// Phase 3 — Reporta agents globais cujo project owner foi deletado (orphans).
/// Esses agents continuam visíveis em outros projetos do tenant via query filter, mas
/// quebram em runtime ao tentar resolver credenciais (owner project sumiu). Estado:
/// <list type="bullet">
///   <item><c>Healthy</c> — sem orphans.</item>
///   <item><c>Degraded</c> — há orphans (lista até 5 IDs no payload).</item>
/// </list>
/// Não derruba o pod (Degraded) porque o problema afeta apenas execuções que tentam
/// resolver esses agents — workflows que não os referenciam continuam OK.
/// </summary>
public sealed class SharedAgentsHealthCheck : IHealthCheck
{
    private readonly IAgentDefinitionRepository _agentRepo;

    public SharedAgentsHealthCheck(IAgentDefinitionRepository agentRepo)
    {
        _agentRepo = agentRepo;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var orphans = await _agentRepo.ListOrphanGlobalAgentsAsync(20, cancellationToken);

            if (orphans.Count == 0)
                return HealthCheckResult.Healthy("Sem agents globais orphan.");

            var payload = new Dictionary<string, object>
            {
                ["shared_agents_orphans_count"] = orphans.Count,
                ["sample"] = orphans
                    .Take(5)
                    .Select(o => new { agentId = o.AgentId, missingProjectId = o.MissingProjectId })
                    .ToList(),
            };

            return HealthCheckResult.Degraded(
                $"{orphans.Count} agent(s) globais com project owner deletado. " +
                "Workflows que os referenciam falharão em runtime.",
                data: payload);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "SharedAgentsHealthCheck falhou ao consultar Postgres.", ex);
        }
    }
}
