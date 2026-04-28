using System.Text.Json;
using EfsAiHub.Core.Abstractions.Persistence;
using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Agents.Evaluation;
using EfsAiHub.Infra.Messaging;
using EfsAiHub.Infra.Observability;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace EfsAiHub.Platform.Runtime.Evaluation;

public sealed class EvaluationService : IEvaluationService
{
    private readonly IAgentDefinitionRepository _agentRepo;
    private readonly IAgentVersionRepository _agentVersionRepo;
    private readonly IEvaluationTestSetVersionRepository _testSetVersionRepo;
    private readonly IEvaluationTestCaseRepository _testCaseRepo;
    private readonly IEvaluatorConfigVersionRepository _evaluatorConfigVersionRepo;
    private readonly IEvaluationRunRepository _runRepo;
    private readonly EvaluatorFactory _evaluatorFactory;
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<EvaluationService> _logger;

    public EvaluationService(
        IAgentDefinitionRepository agentRepo,
        IAgentVersionRepository agentVersionRepo,
        IEvaluationTestSetVersionRepository testSetVersionRepo,
        IEvaluationTestCaseRepository testCaseRepo,
        IEvaluatorConfigVersionRepository evaluatorConfigVersionRepo,
        IEvaluationRunRepository runRepo,
        EvaluatorFactory evaluatorFactory,
        [FromKeyedServices("general")] NpgsqlDataSource dataSource,
        ILogger<EvaluationService> logger)
    {
        _agentRepo = agentRepo;
        _agentVersionRepo = agentVersionRepo;
        _testSetVersionRepo = testSetVersionRepo;
        _testCaseRepo = testCaseRepo;
        _evaluatorConfigVersionRepo = evaluatorConfigVersionRepo;
        _runRepo = runRepo;
        _evaluatorFactory = evaluatorFactory;
        _dataSource = dataSource;
        _logger = logger;
    }

    public async Task<EnqueueRunResult> EnqueueManualAsync(EnqueueRunRequest request, CancellationToken ct = default)
    {
        var def = await _agentRepo.GetByIdAsync(request.AgentDefinitionId, ct)
            ?? throw new EvaluationValidationException($"AgentDefinition '{request.AgentDefinitionId}' não encontrada.");

        var agentVersionId = request.AgentVersionId;
        if (string.IsNullOrEmpty(agentVersionId))
        {
            var current = await _agentVersionRepo.GetCurrentAsync(def.Id, ct)
                ?? throw new EvaluationValidationException($"AgentDefinition '{def.Id}' não tem AgentVersion published.");
            agentVersionId = current.AgentVersionId;
        }

        return await EnqueueCoreAsync(
            projectId: request.ProjectId,
            definition: def,
            agentVersionId: agentVersionId,
            testSetVersionId: request.TestSetVersionId,
            evaluatorConfigVersionId: request.EvaluatorConfigVersionId,
            triggeredBy: request.TriggeredBy,
            triggerSource: EvaluationTriggerSource.Manual,
            triggerContext: null,
            ct: ct);
    }

    public async Task<EnqueueRunResult> EnqueueAutotriggerAsync(AgentVersionPublishedRequest request, CancellationToken ct = default)
    {
        var def = await _agentRepo.GetByIdAsync(request.AgentDefinitionId, ct);
        if (def is null)
        {
            _logger.LogWarning("[EvaluationService] Autotrigger sem AgentDefinition '{AgentDefinitionId}'.", request.AgentDefinitionId);
            return new EnqueueRunResult(null, null, Skipped: true, SkipReason: "AgentDefinitionNotFound", DeduplicatedFromExisting: false);
        }

        if (string.IsNullOrEmpty(def.RegressionTestSetId)
            || string.IsNullOrEmpty(def.RegressionEvaluatorConfigVersionId))
        {
            MetricsRegistry.EvaluationsAutotriggerSkippedNoConfig.Add(1);
            _logger.LogInformation(
                "[EvaluationService] Autotrigger no-op: AgentDefinition '{AgentId}' sem regression_test_set_id ou regression_evaluator_config_version_id.",
                def.Id);
            return new EnqueueRunResult(null, null, Skipped: true, SkipReason: "RegressionConfigNotSet", DeduplicatedFromExisting: false);
        }

        var publishedVersions = await _testSetVersionRepo.ListByTestSetAsync(def.RegressionTestSetId!, ct);
        var currentTestSetVersion = publishedVersions.FirstOrDefault(v => v.Status == TestSetVersionStatus.Published);
        if (currentTestSetVersion is null)
        {
            _logger.LogWarning(
                "[EvaluationService] Autotrigger: regression_test_set_id '{TestSetId}' não tem version Published.",
                def.RegressionTestSetId);
            return new EnqueueRunResult(null, null, Skipped: true, SkipReason: "NoPublishedTestSetVersion", DeduplicatedFromExisting: false);
        }

        var triggerContext = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            agent_version_id = request.AgentVersionId,
            published_by = request.PublishedBy
        }, JsonDefaults.Domain));

        return await EnqueueCoreAsync(
            projectId: request.ProjectId,
            definition: def,
            agentVersionId: request.AgentVersionId,
            testSetVersionId: currentTestSetVersion.TestSetVersionId,
            evaluatorConfigVersionId: def.RegressionEvaluatorConfigVersionId!,
            triggeredBy: null,
            triggerSource: EvaluationTriggerSource.AgentVersionPublished,
            triggerContext: triggerContext,
            ct: ct);
    }

    private async Task<EnqueueRunResult> EnqueueCoreAsync(
        string projectId,
        AgentDefinition definition,
        string agentVersionId,
        string testSetVersionId,
        string evaluatorConfigVersionId,
        string? triggeredBy,
        EvaluationTriggerSource triggerSource,
        JsonDocument? triggerContext,
        CancellationToken ct)
    {
        var testSetVersion = await _testSetVersionRepo.GetByIdAsync(testSetVersionId, ct)
            ?? throw new EvaluationValidationException($"TestSetVersion '{testSetVersionId}' não encontrada.");
        if (testSetVersion.Status == TestSetVersionStatus.Deprecated)
            throw new EvaluationValidationException($"TestSetVersion '{testSetVersionId}' está Deprecated.");

        var configVersion = await _evaluatorConfigVersionRepo.GetByIdAsync(evaluatorConfigVersionId, ct)
            ?? throw new EvaluationValidationException($"EvaluatorConfigVersion '{evaluatorConfigVersionId}' não encontrada.");
        if (configVersion.Status == EvaluatorConfigVersionStatus.Deprecated)
            throw new EvaluationValidationException($"EvaluatorConfigVersion '{evaluatorConfigVersionId}' está Deprecated.");

        var cases = await _testCaseRepo.ListByVersionAsync(testSetVersion.TestSetVersionId, ct: ct);
        if (cases.Count == 0)
            throw new EvaluationValidationException($"TestSetVersion '{testSetVersionId}' sem cases.");

        // ExpectedToolCalls órfãos bloqueiam a run com 400 antes de enfileirar.
        var agentToolNames = definition.Tools
            .Where(t => !string.IsNullOrEmpty(t.Name))
            .Select(t => t.Name!)
            .ToHashSet(StringComparer.Ordinal);
        EvaluatorFactory.ValidateCasesAgainstAgent(cases, agentToolNames);

        var baseline = await _runRepo.FindBaselineAsync(definition.Id, testSetVersionId, evaluatorConfigVersionId, ct);

        var run = EvaluationRun.NewPending(
            projectId: projectId,
            agentDefinitionId: definition.Id,
            agentVersionId: agentVersionId,
            testSetVersionId: testSetVersionId,
            evaluatorConfigVersionId: evaluatorConfigVersionId,
            triggerSource: triggerSource,
            triggeredBy: triggeredBy,
            triggerContext: triggerContext,
            baselineRunId: baseline?.RunId,
            casesTotal: cases.Count);

        var inserted = await _runRepo.EnqueueAsync(run, ct);
        if (inserted is null)
        {
            throw new InvalidOperationException("Falha ao enfileirar run — conflito não esperado.");
        }

        var deduplicated = !ReferenceEquals(inserted, run) || inserted.RunId != run.RunId;
        if (!deduplicated)
        {
            MetricsRegistry.EvaluationsRunsTriggered.Add(1,
                new KeyValuePair<string, object?>("trigger_source", triggerSource.ToString()));
        }

        return new EnqueueRunResult(
            RunId: inserted.RunId,
            Status: inserted.Status,
            Skipped: false,
            SkipReason: null,
            DeduplicatedFromExisting: deduplicated);
    }

    public async Task<bool> CancelRunAsync(string runId, string? cancelledBy, CancellationToken ct = default)
    {
        var fromPending = await _runRepo.TryTransitionStatusAsync(
            runId, EvaluationRunStatus.Pending, EvaluationRunStatus.Cancelled,
            lastError: cancelledBy is null ? "Cancelled" : $"Cancelled by {cancelledBy}", ct: ct);

        var fromRunning = false;
        if (!fromPending)
        {
            fromRunning = await _runRepo.TryTransitionStatusAsync(
                runId, EvaluationRunStatus.Running, EvaluationRunStatus.Cancelled,
                lastError: cancelledBy is null ? "Cancelled" : $"Cancelled by {cancelledBy}", ct: ct);
        }

        if (!fromPending && !fromRunning)
            return false;

        // pg_notify(text, text) parametrizado: payload literal = runId, NpgsqlDbType.Text explícito
        // pra evitar inferência ambígua do driver.
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT pg_notify($1, $2)";
            cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text, Value = PgNotifyDispatcher.EvalRunCancelledChannel });
            cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text, Value = runId });
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[EvaluationService] Falha ao emitir NOTIFY {Channel} para run '{RunId}'. " +
                "Polling fallback do runner cobre em até CancelPollFallbackSeconds.",
                PgNotifyDispatcher.EvalRunCancelledChannel, runId);
        }

        MetricsRegistry.EvaluationsRunsCancelled.Add(1);
        return true;
    }
}
