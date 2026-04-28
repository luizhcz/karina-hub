using System.Text.Json;
using EfsAiHub.Core.Agents.Evaluation;

namespace EfsAiHub.Platform.Runtime.Evaluation;

/// <summary>Orquestrador de eval runs: valida, enfileira e cancela. Idempotente em autotrigger via unique index.</summary>
public interface IEvaluationService
{
    /// <summary>Enfileira run manual; AgentVersionId opcional (null → current Published).</summary>
    Task<EnqueueRunResult> EnqueueManualAsync(EnqueueRunRequest request, CancellationToken ct = default);

    /// <summary>Enfileira run a partir de AgentVersion publicada; sem regression config retorna Skipped=true.</summary>
    Task<EnqueueRunResult> EnqueueAutotriggerAsync(AgentVersionPublishedRequest request, CancellationToken ct = default);

    /// <summary>CAS Pending|Running → Cancelled e emite NOTIFY eval_run_cancelled. Idempotente em estados terminais.</summary>
    Task<bool> CancelRunAsync(string runId, string? cancelledBy, CancellationToken ct = default);
}

public sealed record EnqueueRunRequest(
    string ProjectId,
    string AgentDefinitionId,
    string TestSetVersionId,
    string EvaluatorConfigVersionId,
    string? AgentVersionId,
    string? TriggeredBy);

public sealed record AgentVersionPublishedRequest(
    string ProjectId,
    string AgentDefinitionId,
    string AgentVersionId,
    string? PublishedBy);

public sealed record EnqueueRunResult(
    string? RunId,
    EvaluationRunStatus? Status,
    bool Skipped,
    string? SkipReason,
    bool DeduplicatedFromExisting);
