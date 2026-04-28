using System.Text.Json;
using EfsAiHub.Core.Agents.Evaluation;

namespace EfsAiHub.Host.Api.Models.Responses.Evaluation;

public sealed record EvaluationRunResponse(
    string RunId,
    string ProjectId,
    string AgentDefinitionId,
    string AgentVersionId,
    string TestSetVersionId,
    string EvaluatorConfigVersionId,
    string? BaselineRunId,
    string Status,
    int Priority,
    string? TriggeredBy,
    string TriggerSource,
    JsonElement? TriggerContext,
    int CasesTotal,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string? LastError,
    DateTime CreatedAt,
    int CasesCompleted,
    int CasesPassed,
    int CasesFailed,
    decimal? AvgScore,
    decimal TotalCostUsd,
    long TotalTokens)
{
    public static EvaluationRunResponse FromDomain(
        EvaluationRun run,
        EvaluationRunProgress? progress,
        EvaluationRunUsage? usage = null) => new(
        RunId: run.RunId,
        ProjectId: run.ProjectId,
        AgentDefinitionId: run.AgentDefinitionId,
        AgentVersionId: run.AgentVersionId,
        TestSetVersionId: run.TestSetVersionId,
        EvaluatorConfigVersionId: run.EvaluatorConfigVersionId,
        BaselineRunId: run.BaselineRunId,
        Status: run.Status.ToString(),
        Priority: run.Priority,
        TriggeredBy: run.TriggeredBy,
        TriggerSource: run.TriggerSource.ToString(),
        TriggerContext: run.TriggerContext is null ? null : JsonSerializer.Deserialize<JsonElement>(run.TriggerContext.RootElement.GetRawText()),
        CasesTotal: run.CasesTotal,
        StartedAt: run.StartedAt,
        CompletedAt: run.CompletedAt,
        LastError: run.LastError,
        CreatedAt: run.CreatedAt,
        CasesCompleted: progress?.CasesCompleted ?? 0,
        CasesPassed: progress?.CasesPassed ?? 0,
        CasesFailed: progress?.CasesFailed ?? 0,
        AvgScore: progress?.AvgScore,
        // Cost/tokens vêm de llm_token_usage agregado (single source of truth
        // via TokenTrackingChatClient). Fallback pro progress quando usage ausente.
        TotalCostUsd: usage?.TotalCostUsd ?? progress?.TotalCostUsd ?? 0m,
        TotalTokens: usage?.TotalTokens ?? progress?.TotalTokens ?? 0L);
}

public sealed record EvaluationResultResponse(
    string ResultId,
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
    DateTime CreatedAt)
{
    public static EvaluationResultResponse FromDomain(EvaluationResult r) => new(
        ResultId: r.ResultId,
        CaseId: r.CaseId,
        EvaluatorName: r.EvaluatorName,
        BindingIndex: r.BindingIndex,
        RepetitionIndex: r.RepetitionIndex,
        Score: r.Score,
        Passed: r.Passed,
        Reason: r.Reason,
        OutputContent: r.OutputContent,
        JudgeModel: r.JudgeModel,
        LatencyMs: r.LatencyMs,
        CostUsd: r.CostUsd,
        InputTokens: r.InputTokens,
        OutputTokens: r.OutputTokens,
        CreatedAt: r.CreatedAt);
}

public sealed record EvaluationTestSetResponse(
    string Id,
    string ProjectId,
    string Name,
    string? Description,
    string Visibility,
    string? CurrentVersionId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string? CreatedBy)
{
    public static EvaluationTestSetResponse FromDomain(EvaluationTestSet ts) => new(
        Id: ts.Id,
        ProjectId: ts.ProjectId,
        Name: ts.Name,
        Description: ts.Description,
        Visibility: ts.Visibility.ToString().ToLowerInvariant(),
        CurrentVersionId: ts.CurrentVersionId,
        CreatedAt: ts.CreatedAt,
        UpdatedAt: ts.UpdatedAt,
        CreatedBy: ts.CreatedBy);
}

public sealed record EvaluationTestSetVersionResponse(
    string TestSetVersionId,
    string TestSetId,
    int Revision,
    string Status,
    string ContentHash,
    DateTime CreatedAt,
    string? CreatedBy,
    string? ChangeReason)
{
    public static EvaluationTestSetVersionResponse FromDomain(EvaluationTestSetVersion v) => new(
        TestSetVersionId: v.TestSetVersionId,
        TestSetId: v.TestSetId,
        Revision: v.Revision,
        Status: v.Status.ToString(),
        ContentHash: v.ContentHash,
        CreatedAt: v.CreatedAt,
        CreatedBy: v.CreatedBy,
        ChangeReason: v.ChangeReason);
}

public sealed record EvaluationTestCaseResponse(
    string CaseId,
    int Index,
    string Input,
    string? ExpectedOutput,
    JsonElement? ExpectedToolCalls,
    IReadOnlyList<string> Tags,
    double Weight,
    DateTime CreatedAt)
{
    public static EvaluationTestCaseResponse FromDomain(EvaluationTestCase c) => new(
        CaseId: c.CaseId,
        Index: c.Index,
        Input: c.Input,
        ExpectedOutput: c.ExpectedOutput,
        ExpectedToolCalls: c.ExpectedToolCalls is null ? null : JsonSerializer.Deserialize<JsonElement>(c.ExpectedToolCalls.RootElement.GetRawText()),
        Tags: c.Tags,
        Weight: c.Weight,
        CreatedAt: c.CreatedAt);
}

public sealed record EvaluatorConfigResponse(
    string Id,
    string AgentDefinitionId,
    string Name,
    string? CurrentVersionId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string? CreatedBy)
{
    public static EvaluatorConfigResponse FromDomain(EvaluatorConfig c) => new(
        Id: c.Id,
        AgentDefinitionId: c.AgentDefinitionId,
        Name: c.Name,
        CurrentVersionId: c.CurrentVersionId,
        CreatedAt: c.CreatedAt,
        UpdatedAt: c.UpdatedAt,
        CreatedBy: c.CreatedBy);
}

public sealed record EvaluatorConfigVersionResponse(
    string EvaluatorConfigVersionId,
    string EvaluatorConfigId,
    int Revision,
    string Status,
    string ContentHash,
    IReadOnlyList<EvaluatorBindingResponse> Bindings,
    string Splitter,
    int NumRepetitions,
    DateTime CreatedAt,
    string? CreatedBy,
    string? ChangeReason)
{
    public static EvaluatorConfigVersionResponse FromDomain(EvaluatorConfigVersion v) => new(
        EvaluatorConfigVersionId: v.EvaluatorConfigVersionId,
        EvaluatorConfigId: v.EvaluatorConfigId,
        Revision: v.Revision,
        Status: v.Status.ToString(),
        ContentHash: v.ContentHash,
        Bindings: v.Bindings.Select(EvaluatorBindingResponse.FromDomain).ToList(),
        Splitter: v.Splitter.ToString(),
        NumRepetitions: v.NumRepetitions,
        CreatedAt: v.CreatedAt,
        CreatedBy: v.CreatedBy,
        ChangeReason: v.ChangeReason);
}

public sealed record EvaluatorBindingResponse(
    string Kind,
    string Name,
    JsonElement? Params,
    bool Enabled,
    double Weight,
    int BindingIndex)
{
    public static EvaluatorBindingResponse FromDomain(EvaluatorBinding b) => new(
        Kind: b.Kind.ToString(),
        Name: b.Name,
        Params: b.Params is null ? null : JsonSerializer.Deserialize<JsonElement>(b.Params.RootElement.GetRawText()),
        Enabled: b.Enabled,
        Weight: b.Weight,
        BindingIndex: b.BindingIndex);
}

public sealed record EvaluatorCatalogEntry(
    string Kind,
    string Name,
    string Dimension,
    string Description,
    bool RequiresParams,
    string? ParamsExampleJson);

public sealed record RegressionConfigResponse(
    string AgentDefinitionId,
    string? RegressionTestSetId,
    string? RegressionEvaluatorConfigVersionId,
    bool AutotriggerEnabled);

public sealed record EvaluationRunCompareResponse(
    string RunIdA,
    string RunIdB,
    decimal? PassRateA,
    decimal? PassRateB,
    decimal? PassRateDelta,
    int CasesFailedA,
    int CasesFailedB,
    int CasesFailedDelta,
    bool RegressionDetected,
    IReadOnlyList<CaseDiff> CaseDiffs);

public sealed record CaseDiff(
    string CaseId,
    bool? PassedA,
    bool? PassedB,
    decimal? ScoreA,
    decimal? ScoreB,
    string? ReasonA,
    string? ReasonB);

public sealed record EnqueueEvaluationRunResponse(
    string? RunId,
    string? Status,
    bool Skipped,
    string? SkipReason,
    bool DeduplicatedFromExisting);
