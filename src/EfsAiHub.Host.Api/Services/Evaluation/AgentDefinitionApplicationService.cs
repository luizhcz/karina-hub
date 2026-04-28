using EfsAiHub.Core.Agents;
using EfsAiHub.Infra.Observability;
using EfsAiHub.Platform.Runtime.Evaluation;

namespace EfsAiHub.Host.Api.Services.Evaluation;

public sealed class AgentDefinitionApplicationService : IAgentDefinitionApplicationService
{
    private readonly IAgentDefinitionRepository _agentRepo;
    private readonly IAgentVersionRepository _agentVersionRepo;
    private readonly IEvaluationService _evaluationService;
    private readonly ILogger<AgentDefinitionApplicationService> _logger;

    public AgentDefinitionApplicationService(
        IAgentDefinitionRepository agentRepo,
        IAgentVersionRepository agentVersionRepo,
        IEvaluationService evaluationService,
        ILogger<AgentDefinitionApplicationService> logger)
    {
        _agentRepo = agentRepo;
        _agentVersionRepo = agentVersionRepo;
        _evaluationService = evaluationService;
        _logger = logger;
    }

    public async Task<UpsertWithRegressionResult> UpsertWithRegressionAsync(
        AgentDefinition definition,
        string? publishedBy,
        CancellationToken ct = default)
    {
        // Upsert cria AgentVersion como side-effect (append-only por ContentHash).
        var saved = await _agentRepo.UpsertAsync(definition, ct);

        var current = await _agentVersionRepo.GetCurrentAsync(saved.Id, ct);
        if (current is null)
        {
            // Snapshot ainda não materializado — não dispara autotrigger.
            return new UpsertWithRegressionResult(
                Definition: saved,
                AgentVersionId: null,
                AutotriggerRunId: null,
                AutotriggerSkipped: true,
                AutotriggerSkipReason: "AgentVersionNotResolved",
                AutotriggerFailed: false);
        }

        // Autotrigger best-effort: não bloqueia publish — falha vira métrica + log.
        if (string.IsNullOrEmpty(saved.RegressionTestSetId)
            || string.IsNullOrEmpty(saved.RegressionEvaluatorConfigVersionId))
        {
            return new UpsertWithRegressionResult(
                Definition: saved,
                AgentVersionId: current.AgentVersionId,
                AutotriggerRunId: null,
                AutotriggerSkipped: true,
                AutotriggerSkipReason: "RegressionConfigNotSet",
                AutotriggerFailed: false);
        }

        try
        {
            var result = await _evaluationService.EnqueueAutotriggerAsync(new AgentVersionPublishedRequest(
                ProjectId: saved.ProjectId,
                AgentDefinitionId: saved.Id,
                AgentVersionId: current.AgentVersionId,
                PublishedBy: publishedBy), ct);

            return new UpsertWithRegressionResult(
                Definition: saved,
                AgentVersionId: current.AgentVersionId,
                AutotriggerRunId: result.RunId,
                AutotriggerSkipped: result.Skipped,
                AutotriggerSkipReason: result.SkipReason,
                AutotriggerFailed: false);
        }
        catch (Exception ex)
        {
            // Autotrigger é safety net — falha não quebra o publish do agente.
            MetricsRegistry.EvaluationsAutotriggerFailed.Add(1);
            _logger.LogError(ex,
                "[AgentDefinitionApplicationService] Autotrigger de eval falhou para AgentVersion '{VersionId}'. " +
                "Publish do agent foi commitado normalmente.",
                current.AgentVersionId);
            return new UpsertWithRegressionResult(
                Definition: saved,
                AgentVersionId: current.AgentVersionId,
                AutotriggerRunId: null,
                AutotriggerSkipped: false,
                AutotriggerSkipReason: null,
                AutotriggerFailed: true);
        }
    }
}
