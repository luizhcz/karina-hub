using System.Text.Json;

namespace EfsAiHub.Core.Agents.Evaluation;

/// <summary>
/// Header de uma run de avaliação. AgentVersionId + TestSetVersionId +
/// EvaluatorConfigVersionId são snapshots pinados — run é reproducível.
/// Contadores rolling ficam em <see cref="EvaluationRunProgress"/> para evitar
/// hot row contention.
/// Idempotência do autotrigger: unique index parcial em (AgentVersionId)
/// WHERE TriggerSource='AgentVersionPublished' AND Status IN ('Pending','Running','Completed')
/// torna re-publish da mesma AgentVersion no-op no nível do banco.
/// </summary>
public sealed record EvaluationRun(
    string RunId,
    string ProjectId,
    string AgentDefinitionId,
    string AgentVersionId,
    string TestSetVersionId,
    string EvaluatorConfigVersionId,
    string? BaselineRunId,
    EvaluationRunStatus Status,
    int Priority,
    string? TriggeredBy,
    EvaluationTriggerSource TriggerSource,
    JsonDocument? TriggerContext,
    string ExecutionId,
    int CasesTotal,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    DateTime? LastHeartbeatAt,
    string? LastError,
    DateTime CreatedAt)
{
    /// <summary>
    /// Prioridade default por trigger source: Manual=0 (high) supera
    /// AgentVersionPublished=10 (low). Runner ordena por (Priority ASC, CreatedAt ASC).
    /// </summary>
    public static int DefaultPriority(EvaluationTriggerSource source) => source switch
    {
        EvaluationTriggerSource.Manual => 0,
        EvaluationTriggerSource.ApiClient => 5,
        EvaluationTriggerSource.AgentVersionPublished => 10,
        _ => 10
    };

    /// <summary>
    /// Constrói uma run em <c>Pending</c>. ExecutionId = "eval:{RunId}" para
    /// correlação com llm_token_usage.
    /// </summary>
    public static EvaluationRun NewPending(
        string projectId,
        string agentDefinitionId,
        string agentVersionId,
        string testSetVersionId,
        string evaluatorConfigVersionId,
        EvaluationTriggerSource triggerSource,
        string? triggeredBy,
        JsonDocument? triggerContext,
        string? baselineRunId,
        int casesTotal)
    {
        var runId = Guid.NewGuid().ToString("N");
        return new EvaluationRun(
            RunId: runId,
            ProjectId: projectId,
            AgentDefinitionId: agentDefinitionId,
            AgentVersionId: agentVersionId,
            TestSetVersionId: testSetVersionId,
            EvaluatorConfigVersionId: evaluatorConfigVersionId,
            BaselineRunId: baselineRunId,
            Status: EvaluationRunStatus.Pending,
            Priority: DefaultPriority(triggerSource),
            TriggeredBy: triggeredBy,
            TriggerSource: triggerSource,
            TriggerContext: triggerContext,
            ExecutionId: $"eval:{runId}",
            CasesTotal: casesTotal,
            StartedAt: null,
            CompletedAt: null,
            LastHeartbeatAt: null,
            LastError: null,
            CreatedAt: DateTime.UtcNow);
    }
}

/// <summary>
/// Contadores rolling e custo acumulado.
/// INSERT ON CONFLICT DO UPDATE col = col + EXCLUDED.col reduz lock contention
/// vs UPDATE no hot row do header.
/// </summary>
public sealed record EvaluationRunProgress(
    string RunId,
    int CasesCompleted,
    int CasesPassed,
    int CasesFailed,
    decimal? AvgScore,
    decimal TotalCostUsd,
    long TotalTokens,
    DateTime LastUpdated);
