using EfsAiHub.Core.Agents;

namespace EfsAiHub.Host.Api.Services.Evaluation;

/// <summary>
/// Camada de aplicação que orquestra upsert de <see cref="AgentDefinition"/>
/// + autotrigger de avaliação em publish de <c>AgentVersion</c>. Autotrigger
/// é best-effort (try/catch sem bloquear publish — falha incrementa
/// <c>evaluations.autotrigger.failed</c>).
/// </summary>
public interface IAgentDefinitionApplicationService
{
    /// <summary>
    /// Upsert + autotrigger de eval quando a definition tiver
    /// <c>RegressionTestSetId</c> e <c>RegressionEvaluatorConfigVersionId</c>.
    /// Idempotente: re-publish da mesma version é no-op via unique index parcial
    /// (<c>UX_evaluation_runs_Autotrigger</c>).
    /// </summary>
    Task<UpsertWithRegressionResult> UpsertWithRegressionAsync(
        AgentDefinition definition,
        string? publishedBy,
        CancellationToken ct = default);
}

public sealed record UpsertWithRegressionResult(
    AgentDefinition Definition,
    string? AgentVersionId,
    string? AutotriggerRunId,
    bool AutotriggerSkipped,
    string? AutotriggerSkipReason,
    bool AutotriggerFailed);
