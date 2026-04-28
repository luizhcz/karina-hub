namespace EfsAiHub.Core.Agents.Evaluation;

// Header mutável; versions append-only via CurrentVersionId.
// Tabela dedicada (não em AgentDefinition.Metadata) para audit trail inspecionável.
public sealed record EvaluatorConfig(
    string Id,
    string AgentDefinitionId,
    string Name,
    string? CurrentVersionId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string? CreatedBy);
