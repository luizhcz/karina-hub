using EfsAiHub.Core.Orchestration.Enums;
using EfsAiHub.Core.Orchestration.Validation;

namespace EfsAiHub.Host.Api.Models.Responses;

public class WorkflowResponse
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string Version { get; init; }
    public required OrchestrationMode OrchestrationMode { get; init; }
    public required IReadOnlyList<WorkflowAgentReference> Agents { get; init; }
    public IReadOnlyList<WorkflowExecutorStep> Executors { get; init; } = [];
    public IReadOnlyList<WorkflowEdge> Edges { get; init; } = [];
    public WorkflowConfiguration Configuration { get; init; } = new();
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
    /// <summary>"project" (default) | "global"</summary>
    public required string Visibility { get; init; }
    /// <summary>Project owner do workflow. Distingue de quem está consumindo (caller pode ser outro).</summary>
    public required string OriginProjectId { get; init; }
    /// <summary>Tenant do owner. Reforça boundary cross-tenant na UI.</summary>
    public required string OriginTenantId { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }

    /// <summary>
    /// Snapshot ativo em runtime — corresponde à row em workflow_versions cujo
    /// ContentHash bate com o estado mutável atual do workflow. Útil pra UI
    /// destacar "esta é a versão em produção" na timeline. null quando não há
    /// versionamento ativo ou quando a row correspondente sumiu (caso patológico).
    /// </summary>
    public string? CurrentVersionId { get; init; }

    /// <summary>Revision (int) da CurrentVersionId — atalho pra UI exibir "rN".</summary>
    public int? CurrentRevision { get; init; }

    /// <summary>
    /// Warnings não-bloqueantes do save — emitidos quando Chat deploy tem
    /// branch agent Conversational sem validation válida em Chat Sandbox.
    /// Frontend renderiza inline; save NÃO é bloqueado (decisão de produto:
    /// gate é warning, não erro). Null/omitido quando não há warnings.
    /// </summary>
    public IReadOnlyList<ChatValidationWarning>? ValidationWarnings { get; init; }

    public static WorkflowResponse FromDomain(WorkflowDefinition def) => FromDomain(def, null);

    public static WorkflowResponse FromDomain(
        WorkflowDefinition def,
        WorkflowVersion? currentVersion,
        IReadOnlyList<ChatValidationWarning>? validationWarnings = null) => new()
    {
        Id = def.Id,
        Name = def.Name,
        Description = def.Description,
        Version = def.Version,
        OrchestrationMode = def.OrchestrationMode,
        Agents = def.Agents,
        Executors = def.Executors,
        Edges = def.Edges,
        Configuration = def.Configuration,
        Metadata = def.Metadata,
        Visibility = def.Visibility,
        OriginProjectId = def.ProjectId,
        OriginTenantId = def.TenantId,
        CreatedAt = def.CreatedAt,
        UpdatedAt = def.UpdatedAt,
        CurrentVersionId = currentVersion?.WorkflowVersionId,
        CurrentRevision = currentVersion?.Revision,
        ValidationWarnings = validationWarnings is { Count: > 0 } ? validationWarnings : null,
    };
}
