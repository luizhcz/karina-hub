using System.Text.Json;
using EfsAiHub.Core.Abstractions.Observability;
using EfsAiHub.Platform.Runtime.Interfaces;

namespace EfsAiHub.Platform.Runtime.Services;

public sealed class WorkflowAutoPinService : IWorkflowAutoPinService
{
    private readonly IWorkflowDefinitionRepository _workflowRepo;
    private readonly IAgentVersionRepository _versionRepo;
    private readonly IAdminAuditLogger? _auditLogger;
    private readonly ILogger<WorkflowAutoPinService> _logger;

    public WorkflowAutoPinService(
        IWorkflowDefinitionRepository workflowRepo,
        IAgentVersionRepository versionRepo,
        ILogger<WorkflowAutoPinService> logger,
        IAdminAuditLogger? auditLogger = null)
    {
        _workflowRepo = workflowRepo;
        _versionRepo = versionRepo;
        _auditLogger = auditLogger;
        _logger = logger;
    }

    public async Task AutoPinLegacyReferencesAsync(
        WorkflowDefinition workflow,
        CancellationToken ct = default)
    {
        // Quick return — idempotência local quando o objeto in-memory já tem todos os pins.
        var legacyOnInput = workflow.Agents.Count(a => string.IsNullOrEmpty(a.AgentVersionId));
        if (legacyOnInput == 0) return;

        // Re-fetch defensivo: outra instância pode ter pinado entre a leitura do caller
        // e este método. O upsert posterior grava a row inteira; partir de uma cópia
        // potencialmente stale sobrescreveria pins concorrentes.
        var fresh = await _workflowRepo.GetByIdAsync(workflow.Id, ct);
        if (fresh is null)
        {
            _logger.LogWarning(
                "[AutoPin] Workflow '{WorkflowId}' não encontrado durante auto-pin.",
                workflow.Id);
            return;
        }

        var pinned = new List<(string AgentId, string PinnedVersionId)>();
        foreach (var agentRef in fresh.Agents)
        {
            if (!string.IsNullOrEmpty(agentRef.AgentVersionId)) continue;

            var current = await _versionRepo.GetCurrentAsync(agentRef.AgentId, ct);
            if (current is null)
            {
                _logger.LogWarning(
                    "[AutoPin] Agent '{AgentId}' (workflow '{WorkflowId}') sem version Published; auto-pin pulado pra esse ref.",
                    agentRef.AgentId, workflow.Id);
                continue;
            }

            agentRef.AgentVersionId = current.AgentVersionId;
            pinned.Add((agentRef.AgentId, current.AgentVersionId));
        }

        if (pinned.Count == 0) return;

        await _workflowRepo.UpsertAsync(fresh, ct);

        // Sincroniza no objeto in-memory passado pelo caller pra que ele "veja" o pin
        // sem precisar re-buscar do DB. AgentFactory consome workflow.Agents direto.
        foreach (var (agentId, pinnedVersionId) in pinned)
        {
            var inputRef = workflow.Agents.FirstOrDefault(
                a => string.Equals(a.AgentId, agentId, StringComparison.OrdinalIgnoreCase));
            if (inputRef is not null && string.IsNullOrEmpty(inputRef.AgentVersionId))
                inputRef.AgentVersionId = pinnedVersionId;
        }

        EfsAiHub.Infra.Observability.MetricsRegistry.WorkflowAgentVersionAutoPins.Add(pinned.Count,
            new KeyValuePair<string, object?>("workflow_id", workflow.Id));

        _logger.LogInformation(
            "[AutoPin] Workflow '{WorkflowId}': {Count} agents auto-pinados.",
            workflow.Id, pinned.Count);

        if (_auditLogger is not null)
        {
            try
            {
                var payload = JsonDocument.Parse(JsonSerializer.Serialize(new
                {
                    workflowId = workflow.Id,
                    pinned = pinned.Select(p => new
                    {
                        agentId = p.AgentId,
                        agentVersionId = p.PinnedVersionId,
                    }).ToList(),
                }));
                await _auditLogger.RecordAsync(new AdminAuditEntry
                {
                    ActorUserId = "system:auto-pin",
                    ActorUserType = "system",
                    Action = AdminAuditActions.WorkflowAgentVersionAutoPinned,
                    ResourceType = AdminAuditResources.Workflow,
                    ResourceId = workflow.Id,
                    ProjectId = fresh.ProjectId,
                    TenantId = fresh.TenantId,
                    PayloadAfter = payload,
                    Timestamp = DateTime.UtcNow,
                }, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[AutoPin] Falha ao registrar audit workflow.agent_version_auto_pinned (não-bloqueante).");
            }
        }
    }
}
