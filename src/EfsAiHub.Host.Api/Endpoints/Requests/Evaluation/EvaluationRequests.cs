using System.Text.Json;

namespace EfsAiHub.Host.Api.Models.Requests.Evaluation;

/// <summary>POST /api/agents/{agentId}/evaluations/runs</summary>
public sealed class CreateEvaluationRunRequest
{
    public required string TestSetVersionId { get; init; }
    public required string EvaluatorConfigVersionId { get; init; }

    /// <summary>Snapshot AgentVersion específico. Null = current Published do agente.</summary>
    public string? AgentVersionId { get; init; }
}

/// <summary>POST /api/projects/{projectId}/evaluation-test-sets</summary>
public sealed class CreateTestSetRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }

    /// <summary>"project" (default) ou "global". Global exige permissão admin.</summary>
    public string Visibility { get; init; } = "project";
}

/// <summary>POST /api/evaluation-test-sets/{id}/versions</summary>
public sealed class PublishTestSetVersionRequest
{
    public required IReadOnlyList<TestCaseRequest> Cases { get; init; }
    public string? ChangeReason { get; init; }
}

public sealed class TestCaseRequest
{
    public required string Input { get; init; }
    public string? ExpectedOutput { get; init; }

    /// <summary>Array opcional de <c>{name, argsSchema?}</c>. Validador rejeita 400 se referenciar tool órfã.</summary>
    public JsonElement? ExpectedToolCalls { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
    public double? Weight { get; init; }
}

/// <summary>
/// POST /api/evaluation-test-sets/{id}/versions/import (multipart/form-data)
/// CSV format: input,expectedOutput,tags,weight (header obrigatório).
/// </summary>
public sealed class CsvImportRequest
{
    public required IFormFile File { get; init; }
    public string? ChangeReason { get; init; }
}

/// <summary>PUT /api/evaluation-test-sets/{id}/versions/{vid}/status</summary>
public sealed class UpdateTestSetVersionStatusRequest
{
    /// <summary>"Draft" | "Published" | "Deprecated"</summary>
    public required string Status { get; init; }
}

/// <summary>PUT /api/agents/{agentId}/evaluator-config</summary>
public sealed class UpsertEvaluatorConfigRequest
{
    public required string Name { get; init; }
    public required IReadOnlyList<EvaluatorBindingRequest> Bindings { get; init; }

    /// <summary>"LastTurn" (default) | "Full" | "PerTurn"</summary>
    public string Splitter { get; init; } = "LastTurn";
    public int NumRepetitions { get; init; } = 3;
    public string? ChangeReason { get; init; }
}

public sealed class EvaluatorBindingRequest
{
    /// <summary>"Foundry" | "Local" | "Meai"</summary>
    public required string Kind { get; init; }
    public required string Name { get; init; }
    public JsonElement? Params { get; init; }
    public bool Enabled { get; init; } = true;
    public double Weight { get; init; } = 1.0;
    public int BindingIndex { get; init; } = 0;
}

/// <summary>PUT /api/agents/{agentId}/regression-config</summary>
public sealed class UpsertRegressionConfigRequest
{
    public string? RegressionTestSetId { get; init; }
    public string? RegressionEvaluatorConfigVersionId { get; init; }
}
