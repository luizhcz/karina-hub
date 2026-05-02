using EfsAiHub.Platform.Runtime.Interfaces;

namespace EfsAiHub.Platform.Runtime.Services;

public sealed class WorkflowAgentVersionStatusService : IWorkflowAgentVersionStatusService
{
    private readonly IWorkflowDefinitionRepository _workflowRepo;
    private readonly IAgentDefinitionRepository _agentRepo;
    private readonly IAgentVersionRepository _versionRepo;

    public WorkflowAgentVersionStatusService(
        IWorkflowDefinitionRepository workflowRepo,
        IAgentDefinitionRepository agentRepo,
        IAgentVersionRepository versionRepo)
    {
        _workflowRepo = workflowRepo;
        _agentRepo = agentRepo;
        _versionRepo = versionRepo;
    }

    public async Task<IReadOnlyList<WorkflowAgentVersionStatus>> GetStatusAsync(
        string workflowId,
        CancellationToken ct = default)
    {
        var workflow = await _workflowRepo.GetByIdAsync(workflowId, ct)
            ?? throw new KeyNotFoundException($"Workflow '{workflowId}' não encontrado.");

        if (workflow.Agents.Count == 0)
            return Array.Empty<WorkflowAgentVersionStatus>();

        var result = new List<WorkflowAgentVersionStatus>(workflow.Agents.Count);

        foreach (var agentRef in workflow.Agents)
        {
            var agent = await _agentRepo.GetByIdAsync(agentRef.AgentId, ct);
            var current = await _versionRepo.GetCurrentAsync(agentRef.AgentId, ct);

            AgentVersion? pinned = null;
            if (!string.IsNullOrEmpty(agentRef.AgentVersionId))
            {
                pinned = await _versionRepo.GetByIdAsync(agentRef.AgentVersionId, ct);
            }

            var pinnedRev = pinned?.Revision;
            var currentRev = current?.Revision;
            var hasUpdate = pinnedRev is not null
                && currentRev is not null
                && currentRev > pinnedRev;

            // Detecção de breaking entre pinned e current: se há, caller fica preso.
            var blocked = false;
            IReadOnlyList<WorkflowAgentVersionChangeEntry> changes = Array.Empty<WorkflowAgentVersionChangeEntry>();

            if (pinnedRev is not null && currentRev is not null && currentRev > pinnedRev)
            {
                var breaking = await _versionRepo.GetAncestorBreakingAsync(
                    agentRef.AgentId, pinnedRev.Value, currentRev.Value, ct);
                blocked = breaking is not null;

                var between = await _versionRepo.ListBetweenRevisionsAsync(
                    agentRef.AgentId, pinnedRev.Value, currentRev.Value, ct);
                changes = between
                    .Select(v => new WorkflowAgentVersionChangeEntry(
                        AgentVersionId: v.AgentVersionId,
                        Revision: v.Revision,
                        BreakingChange: v.BreakingChange,
                        ChangeReason: v.ChangeReason,
                        CreatedAt: v.CreatedAt,
                        CreatedBy: v.CreatedBy))
                    .ToList();
            }

            result.Add(new WorkflowAgentVersionStatus(
                AgentId: agentRef.AgentId,
                AgentName: agent?.Name,
                PinnedVersionId: pinned?.AgentVersionId,
                PinnedRevision: pinnedRev,
                CurrentVersionId: current?.AgentVersionId,
                CurrentRevision: currentRev,
                IsPinnedBlockedByBreaking: blocked,
                HasUpdate: hasUpdate,
                Changes: changes,
                IsAgentDisabled: agent is not null && !agent.Enabled));
        }

        return result;
    }
}
