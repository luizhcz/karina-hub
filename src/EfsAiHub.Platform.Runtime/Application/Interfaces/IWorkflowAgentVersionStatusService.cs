namespace EfsAiHub.Platform.Runtime.Interfaces;

/// <summary>
/// Resolve estado consolidado dos pins de agent refs em um workflow pra UI
/// renderizar tab "Versões dos agentes" e diff modals. Batch-friendly:
/// 1 chamada agrega todos os refs do workflow.
/// </summary>
public interface IWorkflowAgentVersionStatusService
{
    /// <summary>
    /// Pra cada agent ref do workflow, resolve current Published, lookup name,
    /// detecta breaking entre pinned e current, agrega change reasons.
    /// Retorna lista no mesmo order dos refs do workflow.
    /// Lança <see cref="KeyNotFoundException"/> quando workflow não existe.
    /// </summary>
    Task<IReadOnlyList<WorkflowAgentVersionStatus>> GetStatusAsync(
        string workflowId,
        CancellationToken ct = default);
}

/// <summary>
/// Snapshot de status de um agent ref dentro de um workflow. Construído pelo
/// <see cref="IWorkflowAgentVersionStatusService"/> a partir de joins entre
/// <c>workflow_definitions</c>, <c>agent_definitions</c> e <c>agent_versions</c>.
/// </summary>
public sealed record WorkflowAgentVersionStatus(
    string AgentId,
    string? AgentName,
    string? PinnedVersionId,
    int? PinnedRevision,
    string? CurrentVersionId,
    int? CurrentRevision,
    bool IsPinnedBlockedByBreaking,
    bool HasUpdate,
    IReadOnlyList<WorkflowAgentVersionChangeEntry> Changes);

/// <summary>Mudança individual entre pinned e current — usada pelo diff modal.</summary>
public sealed record WorkflowAgentVersionChangeEntry(
    string AgentVersionId,
    int Revision,
    bool? BreakingChange,
    string? ChangeReason,
    DateTime CreatedAt,
    string? CreatedBy);
