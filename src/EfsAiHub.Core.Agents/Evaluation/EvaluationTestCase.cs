using System.Text.Json;

namespace EfsAiHub.Core.Agents.Evaluation;

// ExpectedToolCalls: array JSONB de {name, argsSchema?} validado em runtime
// contra tools do AgentVersion (tool órfã → 400 explícito no enqueue).
public sealed record EvaluationTestCase(
    string CaseId,
    string TestSetVersionId,
    int Index,
    string Input,
    string? ExpectedOutput,
    JsonDocument? ExpectedToolCalls,
    IReadOnlyList<string> Tags,
    double Weight,
    DateTime CreatedAt);
