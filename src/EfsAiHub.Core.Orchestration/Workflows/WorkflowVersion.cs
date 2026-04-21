using System.Text.Json;
using EfsAiHub.Core.Abstractions.Persistence;
using EfsAiHub.Core.Abstractions.Hashing;

namespace EfsAiHub.Core.Orchestration.Workflows;

/// <summary>
/// Snapshot imutável de uma WorkflowDefinition num determinado ponto do tempo.
/// Append-only: UpsertAsync de WorkflowDefinition cria um novo WorkflowVersion
/// com Revision = MAX+1. Idempotente por ContentHash — mesmo conteúdo não gera nova revision.
///
/// O campo DefinitionSnapshot carrega a WorkflowDefinition serializada para rollback.
/// Padrão idêntico ao AgentVersion (Core.Agents).
/// </summary>
public sealed record WorkflowVersion(
    string WorkflowVersionId,
    string WorkflowDefinitionId,
    int Revision,
    DateTime CreatedAt,
    string? CreatedBy,
    string? ChangeReason,
    WorkflowVersionStatus Status,
    string ContentHash)
{
    /// <summary>
    /// JSON da WorkflowDefinition completa. Não faz parte da identidade do record (init-only).
    /// Armazenado no Snapshot JSONB para rollback determinístico.
    /// </summary>
    public string? DefinitionSnapshot { get; init; }

    /// <summary>
    /// Constrói um snapshot a partir de uma WorkflowDefinition viva.
    /// Calcula ContentHash canônico (sha256) para idempotência.
    /// </summary>
    public static WorkflowVersion FromDefinition(
        WorkflowDefinition definition,
        int revision,
        string? createdBy = null,
        string? changeReason = null)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            workflowId = definition.Id,
            name = definition.Name,
            description = definition.Description,
            version = definition.Version,
            orchestrationMode = definition.OrchestrationMode,
            agents = definition.Agents,
            executors = definition.Executors,
            edges = definition.Edges,
            routingRules = definition.RoutingRules,
            configuration = definition.Configuration,
            metadata = definition.Metadata,
            visibility = definition.Visibility
        }, JsonDefaults.Domain);

        var hash = ContentHashCalculator.ComputeFromString(canonical);
        var definitionJson = JsonSerializer.Serialize(definition, JsonDefaults.Domain);

        return new WorkflowVersion(
            WorkflowVersionId: Guid.NewGuid().ToString("N"),
            WorkflowDefinitionId: definition.Id,
            Revision: revision,
            CreatedAt: DateTime.UtcNow,
            CreatedBy: createdBy,
            ChangeReason: changeReason,
            Status: WorkflowVersionStatus.Published,
            ContentHash: hash)
        {
            DefinitionSnapshot = definitionJson
        };
    }
}

public enum WorkflowVersionStatus
{
    Draft,
    Published,
    Retired
}
