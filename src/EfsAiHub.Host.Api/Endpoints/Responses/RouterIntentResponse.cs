using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Agents.RouterIntents;

namespace EfsAiHub.Host.Api.Models.Responses;

public sealed class RouterIntentResponse
{
    public required string Id { get; init; }
    public required string TenantId { get; init; }

    /// <summary>Projeto-categoria (FK pra <c>aihub.projects</c>). UI exibe o nome via lookup.</summary>
    public required string ProjectId { get; init; }

    public required string Name { get; init; }

    /// <summary>Texto humano editável; null cai pro Name na UI.</summary>
    public string? DisplayName { get; init; }

    public required string Description { get; init; }
    public required IReadOnlyList<string> Examples { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }

    public static RouterIntentResponse FromDomain(RouterIntent intent) => new()
    {
        Id = intent.Id,
        TenantId = intent.TenantId,
        ProjectId = intent.ProjectId,
        Name = intent.Name,
        DisplayName = intent.DisplayName,
        Description = intent.Description,
        Examples = intent.Examples,
        CreatedAt = intent.CreatedAt,
        UpdatedAt = intent.UpdatedAt,
    };
}

public sealed class RouterIntentUsageResponse
{
    public required string AgentId { get; init; }
    public required string AgentName { get; init; }
    public required string ProjectId { get; init; }

    public static RouterIntentUsageResponse FromDomain(RouterIntentUsage usage) => new()
    {
        AgentId = usage.AgentId,
        AgentName = usage.AgentName,
        ProjectId = usage.ProjectId,
    };
}

public sealed class AnalyzeRouterIntentResponse
{
    /// <summary>Id da execução do workflow analyzer. Caller polla <c>/executions/{id}</c>.</summary>
    public required string ExecutionId { get; init; }
}
