using System.Text.Json;

namespace EfsAiHub.Core.Agents.Evaluation;

// PK composta com BindingIndex permite o mesmo evaluator (ex.: KeywordCheck)
// declarado 2x na config com keyword sets diferentes — sem colisão.
// JudgeModel registrado para detecção de self-enhancement bias.
// OutputContent truncado a 8KB; integral em llm_token_usage correlacionado por ExecutionId.
public sealed record EvaluationResult(
    string ResultId,
    string RunId,
    string CaseId,
    string EvaluatorName,
    int BindingIndex,
    int RepetitionIndex,
    decimal? Score,
    bool Passed,
    string? Reason,
    string? OutputContent,
    string? JudgeModel,
    double? LatencyMs,
    decimal? CostUsd,
    int? InputTokens,
    int? OutputTokens,
    JsonDocument? EvaluatorMetadata,
    DateTime CreatedAt);
