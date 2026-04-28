using System.Collections.Concurrent;
using System.Diagnostics;
using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Agents.Evaluation;
using EfsAiHub.Infra.Messaging;
using EfsAiHub.Infra.Observability;
using EfsAiHub.Platform.Runtime.Evaluation;
using EfsAiHub.Platform.Runtime.Factories;
using Microsoft.Extensions.AI;
// Alias evita conflito com EfsAiHub.Core.Abstractions.Conversations.ChatMessage
// (importado via GlobalUsings).
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace EfsAiHub.Host.Worker.Services;

/// <summary>
/// Processa <see cref="EvaluationRun"/> em <c>Pending</c>.
///
/// Pipeline por run:
///  1. <see cref="IEvaluationRunRepository.DequeuePendingAsync"/> faz UPDATE
///     atômico Pending→Running com FOR UPDATE SKIP LOCKED — pickup serializado
///     entre pods, sem CAS adicional no caller.
///  2. Resolve config snapshot + cases. Bail se TestSetVersion=Deprecated.
///  3. Cria <see cref="IChatClient"/> bare via <see cref="AgentFactory.CreateBareAgentAsync"/>.
///  4. Constrói evaluators via <see cref="EvaluatorFactory.BuildAsync"/>.
///  5. Subscribe <c>eval_run_cancelled</c> (filtra por RunId atual).
///  6. Heartbeat a cada <see cref="EvaluationOptions.HeartbeatIntervalSeconds"/>s.
///  7. <c>Parallel.ForEachAsync(MaxDegreeOfParallelism=MaxParallelCases)</c>.
///  8. CAS Running → Completed.
///
/// Cancel ≤1s: NOTIFY <c>eval_run_cancelled</c> sinaliza CTS local. Polling
/// fallback de 5s cobre canal NOTIFY indisponível.
/// Kill-switch SRE: <c>EvaluationOptions.Enabled=false</c> → no-op.
/// </summary>
public sealed class EvaluationRunnerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly PgNotifyDispatcher _notifyDispatcher;
    private readonly IOptions<EvaluationOptions> _options;
    private readonly ILogger<EvaluationRunnerService> _logger;

    // Map RunId → CTS local. Cancel chega via NOTIFY sinaliza o CTS específico.
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _runCts = new();

    public EvaluationRunnerService(
        IServiceProvider serviceProvider,
        PgNotifyDispatcher notifyDispatcher,
        IOptions<EvaluationOptions> options,
        ILogger<EvaluationRunnerService> logger)
    {
        _serviceProvider = serviceProvider;
        _notifyDispatcher = notifyDispatcher;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = _options.Value;
        if (!opts.Enabled)
        {
            _logger.LogInformation("[EvaluationRunner] Desabilitado via EvaluationOptions:Enabled=false.");
            return;
        }

        // LISTEN eval_run_cancelled — sinaliza CTS local quando vem o runId.
        using var cancelSubscription = _notifyDispatcher.SubscribeEvalRunCancelled(runId =>
        {
            if (_runCts.TryGetValue(runId, out var cts))
            {
                _logger.LogInformation("[EvaluationRunner] Cancel recebido via NOTIFY para run '{RunId}'.", runId);
                try { cts.Cancel(); } catch { /* race com Dispose: ignorar */ }
            }
            return Task.CompletedTask;
        });

        var pollInterval = TimeSpan.FromSeconds(Math.Max(1, opts.SchedulerPollSeconds));
        using var timer = new PeriodicTimer(pollInterval);

        _logger.LogInformation(
            "[EvaluationRunner] Ativo — polling a cada {Sec}s, MaxParallelCases={Par}, HeartbeatTimeout={Hb}s.",
            opts.SchedulerPollSeconds, opts.MaxParallelCases, opts.HeartbeatTimeoutSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                EvaluationRun? run = null;
                using (var pickScope = _serviceProvider.CreateScope())
                {
                    var runRepo = pickScope.ServiceProvider.GetRequiredService<IEvaluationRunRepository>();
                    run = await runRepo.DequeuePendingAsync(stoppingToken);

                    // Métrica de queue depth (gauge atualizado a cada poll).
                    var pendingCount = await CountPendingAsync(runRepo, stoppingToken);
                    MetricsRegistry.SetEvaluationsQueueDepth(pendingCount);
                }

                if (run is not null)
                {
                    // Fire-and-forget para não bloquear o poll loop; processa em scope próprio.
                    _ = Task.Run(() => ProcessRunSafelyAsync(run, stoppingToken), stoppingToken);
                }

                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[EvaluationRunner] Falha no loop de polling.");
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task<long> CountPendingAsync(IEvaluationRunRepository repo, CancellationToken ct)
    {
        // Placeholder: COUNT(*) WHERE Status='Pending' ainda não exposto no repo;
        // EvaluationReaperService atualiza o gauge real.
        return 0;
    }

    private async Task ProcessRunSafelyAsync(EvaluationRun run, CancellationToken stoppingToken)
    {
        var runId = run.RunId;
        var runCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        if (!_runCts.TryAdd(runId, runCts))
        {
            // Defesa em profundidade: DequeuePending já serializa por CAS.
            _logger.LogWarning("[EvaluationRunner] RunId '{RunId}' já está sendo processado neste pod.", runId);
            runCts.Dispose();
            return;
        }

        var sw = Stopwatch.StartNew();
        var triggerSourceTag = run.TriggerSource.ToString();

        try
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var runRepo = sp.GetRequiredService<IEvaluationRunRepository>();
            var resultRepo = sp.GetRequiredService<IEvaluationResultRepository>();
            var caseRepo = sp.GetRequiredService<IEvaluationTestCaseRepository>();
            var versionRepo = sp.GetRequiredService<IEvaluationTestSetVersionRepository>();
            var configVersionRepo = sp.GetRequiredService<IEvaluatorConfigVersionRepository>();
            var agentRepo = sp.GetRequiredService<IAgentDefinitionRepository>();
            var agentFactory = sp.GetRequiredService<AgentFactory>();
            var evaluatorFactory = sp.GetRequiredService<EvaluatorFactory>();

            // DequeuePendingAsync já transitou Pending→Running atomicamente
            // (FOR UPDATE SKIP LOCKED) — não há race aqui.
            MetricsRegistry.EvaluationsRunsStarted.Add(1, new KeyValuePair<string, object?>("trigger_source", triggerSourceTag));

            // Carrega snapshots pinados.
            var configVersion = await configVersionRepo.GetByIdAsync(run.EvaluatorConfigVersionId, runCts.Token);
            if (configVersion is null)
            {
                await FailRunAsync(runRepo, runId, "EvaluatorConfigVersion não encontrada (FK inconsistente).", runCts.Token);
                return;
            }

            var testSetVersion = await versionRepo.GetByIdAsync(run.TestSetVersionId, runCts.Token);
            if (testSetVersion is null)
            {
                await FailRunAsync(runRepo, runId, "TestSetVersion não encontrada (FK inconsistente).", runCts.Token);
                return;
            }
            if (testSetVersion.Status == TestSetVersionStatus.Deprecated)
            {
                await FailRunAsync(runRepo, runId, "TestSetVersionDeprecated", runCts.Token);
                return;
            }

            var cases = await caseRepo.ListByVersionAsync(testSetVersion.TestSetVersionId, ct: runCts.Token);
            if (cases.Count == 0)
            {
                await FailRunAsync(runRepo, runId, "TestSetVersion sem cases.", runCts.Token);
                return;
            }

            var definition = await agentRepo.GetByIdAsync(run.AgentDefinitionId, runCts.Token);
            if (definition is null)
            {
                await FailRunAsync(runRepo, runId, "AgentDefinition não encontrada.", runCts.Token);
                return;
            }

            var bareClient = await agentFactory.CreateBareAgentAsync(definition, runCts.Token);
            var agentToolNames = definition.Tools
                .Where(t => !string.IsNullOrEmpty(t.Name))
                .Select(t => t.Name!)
                .ToHashSet(StringComparer.Ordinal);

            var evaluators = await evaluatorFactory.BuildAsync(
                configVersion,
                agentJudgeClient: bareClient,
                agentJudgeModelId: definition.Model.DeploymentName,
                projectId: run.ProjectId,
                agentToolNames: agentToolNames,
                ct: runCts.Token);

            using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(runCts.Token);
            var heartbeatTask = HeartbeatLoopAsync(runRepo, runId, heartbeatCts.Token);

            // Hidrata DelegateExecutor.Current: BlocklistChatClient exige ProjectId
            // em ExecutionContext; TokenTrackingChatClient persiste em llm_token_usage
            // com este ExecutionId. Mode=Evaluation distingue de Production/Sandbox
            // downstream. Limpa no finally.
            EfsAiHub.Core.Orchestration.Executors.DelegateExecutor.Current.Value =
                new EfsAiHub.Core.Agents.Execution.ExecutionContext(
                    ExecutionId: run.ExecutionId,
                    WorkflowId: string.Empty,
                    Input: null,
                    PromptVersions: new System.Collections.Concurrent.ConcurrentDictionary<string, string>(),
                    NodeCallback: null,
                    Budget: new EfsAiHub.Core.Agents.Execution.ExecutionBudget(
                        maxTokensPerExecution: 0,
                        maxCostUsd: null),
                    AgentVersions: new System.Collections.Concurrent.ConcurrentDictionary<string, string>(),
                    Mode: EfsAiHub.Core.Abstractions.Execution.ExecutionMode.Evaluation,
                    ProjectId: run.ProjectId);

            try
            {
                await ProcessCasesAsync(
                    sp,
                    runId,
                    run,
                    definition,
                    bareClient,
                    cases,
                    configVersion,
                    evaluators,
                    runCts);

                // Sucesso — transita para Completed. CAS protege contra cancel concorrente.
                var completed = await runRepo.TryTransitionStatusAsync(
                    runId, EvaluationRunStatus.Running, EvaluationRunStatus.Completed, ct: runCts.Token);
                if (completed)
                {
                    MetricsRegistry.EvaluationsRunsCompleted.Add(1,
                        new KeyValuePair<string, object?>("trigger_source", triggerSourceTag));

                    // Regression detection rodando apenas em autotrigger.
                    if (run.TriggerSource == EvaluationTriggerSource.AgentVersionPublished
                        && !string.IsNullOrEmpty(run.BaselineRunId))
                    {
                        await DetectRegressionAsync(sp, run, definition, runCts.Token);
                    }
                }
            }
            finally
            {
                heartbeatCts.Cancel();
                try { await heartbeatTask; } catch { /* heartbeat cancel — ok */ }
            }
        }
        catch (OperationCanceledException) when (runCts.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
        {
            // Cancel via API (NOTIFY ou polling). Transita para Cancelled.
            await using var scope = _serviceProvider.CreateAsyncScope();
            var runRepo = scope.ServiceProvider.GetRequiredService<IEvaluationRunRepository>();
            await runRepo.TryTransitionStatusAsync(
                runId, EvaluationRunStatus.Running, EvaluationRunStatus.Cancelled,
                lastError: "Cancelled by operator.", ct: CancellationToken.None);
            MetricsRegistry.EvaluationsRunsCancelled.Add(1,
                new KeyValuePair<string, object?>("trigger_source", triggerSourceTag));
            _logger.LogInformation("[EvaluationRunner] Run '{RunId}' cancelada.", runId);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown do pod — não marca como Failed (reaper recupera no próximo start).
            _logger.LogInformation("[EvaluationRunner] Run '{RunId}' interrompida por shutdown do pod.", runId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EvaluationRunner] Falha processando run '{RunId}'.", runId);
            try
            {
                await using var scope = _serviceProvider.CreateAsyncScope();
                var runRepo = scope.ServiceProvider.GetRequiredService<IEvaluationRunRepository>();
                await runRepo.TryTransitionStatusAsync(
                    runId, EvaluationRunStatus.Running, EvaluationRunStatus.Failed,
                    lastError: $"{ex.GetType().Name}: {ex.Message}", ct: CancellationToken.None);
                MetricsRegistry.EvaluationsRunsFailed.Add(1,
                    new KeyValuePair<string, object?>("trigger_source", triggerSourceTag),
                    new KeyValuePair<string, object?>("error_category", ex.GetType().Name));
            }
            catch (Exception persistEx)
            {
                _logger.LogError(persistEx, "[EvaluationRunner] Falha ao persistir status Failed para run '{RunId}'.", runId);
            }
        }
        finally
        {
            // Limpa AsyncLocal para evitar leak de ProjectId/ExecutionId entre runs
            // (AsyncLocal viaja com Task.Run no mesmo pool de threads).
            EfsAiHub.Core.Orchestration.Executors.DelegateExecutor.Current.Value = null;
            sw.Stop();
            MetricsRegistry.EvaluationsRunDurationMs.Record(sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("trigger_source", triggerSourceTag));
            _runCts.TryRemove(runId, out _);
            runCts.Dispose();
        }
    }

    private async Task ProcessCasesAsync(
        IServiceProvider sp,
        string runId,
        EvaluationRun run,
        AgentDefinition definition,
        IChatClient bareClient,
        IReadOnlyList<EvaluationTestCase> cases,
        EvaluatorConfigVersion configVersion,
        IReadOnlyList<IAgentEvaluator> evaluators,
        CancellationTokenSource runCts)
    {
        var resultRepo = sp.GetRequiredService<IEvaluationResultRepository>();
        var runRepo = sp.GetRequiredService<IEvaluationRunRepository>();
        var opts = _options.Value;
        var modelId = definition.Model.DeploymentName ?? "unknown";
        var ct = runCts.Token;

        var parallelOpts = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, opts.MaxParallelCases),
            CancellationToken = ct
        };

        await Parallel.ForEachAsync(cases, parallelOpts, async (testCase, innerCt) =>
        {
            var batchResults = new List<EvaluationResult>();

            for (int repetitionIndex = 0; repetitionIndex < configVersion.NumRepetitions; repetitionIndex++)
            {
                innerCt.ThrowIfCancellationRequested();

                var messages = new List<AiChatMessage>();
                if (!string.IsNullOrWhiteSpace(definition.Instructions))
                    messages.Add(new AiChatMessage(ChatRole.System, definition.Instructions));
                messages.Add(new AiChatMessage(ChatRole.User, testCase.Input));

                ChatResponse? agentResponse = null;
                try
                {
                    agentResponse = await bareClient.GetResponseAsync(messages, options: null, innerCt);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    // Crash do agente neste case: registra resultado falho por evaluator
                    // sem interromper os demais cases.
                    _logger.LogWarning(ex,
                        "[EvaluationRunner] Agente falhou no case '{CaseId}' (rep {Rep}) — registrando como falha.",
                        testCase.CaseId, repetitionIndex);

                    foreach (var ev in evaluators)
                    {
                        var failed = new EvaluationResult(
                            ResultId: Guid.NewGuid().ToString("N"),
                            RunId: runId,
                            CaseId: testCase.CaseId,
                            EvaluatorName: $"{ev.Id}.agent_error",
                            BindingIndex: 0,
                            RepetitionIndex: repetitionIndex,
                            Score: 0m,
                            Passed: false,
                            Reason: $"Agent invocation failed: {ex.GetType().Name}: {ex.Message}",
                            OutputContent: null,
                            JudgeModel: modelId,
                            LatencyMs: 0,
                            CostUsd: 0m,
                            InputTokens: 0,
                            OutputTokens: 0,
                            EvaluatorMetadata: null,
                            CreatedAt: DateTime.UtcNow);
                        batchResults.Add(failed);
                    }
                    continue;
                }

                // Sequencial dentro do case: chamadas em flight no run são
                // MaxParallelCases × N evaluators — checar contra
                // HttpClient.MaxConnectionsPerServer.
                foreach (var ev in evaluators)
                {
                    innerCt.ThrowIfCancellationRequested();

                    var binding = configVersion.Bindings.First(b => b.Enabled && BindingIdMatches(b, ev.Id));
                    var invocation = new EvaluationInvocation(
                        RunId: runId,
                        TestCase: testCase,
                        Messages: messages,
                        ModelResponse: agentResponse,
                        BindingIndex: binding.BindingIndex,
                        RepetitionIndex: repetitionIndex,
                        AgentModelId: modelId);

                    var results = await ev.EvaluateAsync(invocation, innerCt);
                    batchResults.AddRange(results);

                    // Métrica por case+evaluator.
                    foreach (var r in results)
                    {
                        if (r.Score.HasValue)
                        {
                            MetricsRegistry.EvaluationsCaseScore.Record((double)r.Score.Value,
                                new KeyValuePair<string, object?>("agent_definition_name", definition.Name),
                                new KeyValuePair<string, object?>("evaluator_name", r.EvaluatorName));
                        }
                    }
                }
            }

            // Persist batch desse case (results + progress).
            if (batchResults.Count > 0)
            {
                await resultRepo.AppendBatchAsync(runId, batchResults, innerCt);

            }
        });
    }

    private static bool BindingIdMatches(EvaluatorBinding binding, string evaluatorId)
    {
        // Adapter Id format: "{kind}.{name}.{bindingIndex}".
        var parts = evaluatorId.Split('.');
        if (parts.Length < 3) return false;
        return string.Equals(parts[1], binding.Name, StringComparison.Ordinal)
            && int.TryParse(parts[^1], out var idx)
            && idx == binding.BindingIndex;
    }

    private async Task HeartbeatLoopAsync(IEvaluationRunRepository repo, string runId, CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, _options.Value.HeartbeatIntervalSeconds));
        try
        {
            using var timer = new PeriodicTimer(interval);
            // Heartbeat imediato logo após Running.
            await repo.TouchHeartbeatAsync(runId, ct);
            while (await timer.WaitForNextTickAsync(ct))
            {
                await repo.TouchHeartbeatAsync(runId, ct);
            }
        }
        catch (OperationCanceledException) { /* esperado */ }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[EvaluationRunner] Heartbeat para run '{RunId}' encerrou com falha.", runId);
        }
    }

    private async Task FailRunAsync(IEvaluationRunRepository repo, string runId, string lastError, CancellationToken ct)
    {
        await repo.TryTransitionStatusAsync(runId, EvaluationRunStatus.Running, EvaluationRunStatus.Failed,
            lastError: lastError, ct: ct);
        MetricsRegistry.EvaluationsRunsFailed.Add(1,
            new KeyValuePair<string, object?>("error_category", "Validation"));
    }

    private async Task DetectRegressionAsync(
        IServiceProvider sp,
        EvaluationRun run,
        AgentDefinition definition,
        CancellationToken ct)
    {
        try
        {
            var resultRepo = sp.GetRequiredService<IEvaluationResultRepository>();
            var runRepo = sp.GetRequiredService<IEvaluationRunRepository>();

            var current = await resultRepo.GetProgressAsync(run.RunId, ct);
            if (current is null || current.CasesCompleted == 0) return;

            var baseline = await runRepo.GetByIdAsync(run.BaselineRunId!, ct);
            if (baseline is null) return;
            var baselineProgress = await resultRepo.GetProgressAsync(baseline.RunId, ct);
            if (baselineProgress is null || baselineProgress.CasesCompleted == 0) return;

            // Regra ADR 0015: passRateDelta < -0.05 AND CasesFailedDelta >= 2.
            var currentPassRate = (double)current.CasesPassed / Math.Max(1, current.CasesCompleted);
            var baselinePassRate = (double)baselineProgress.CasesPassed / Math.Max(1, baselineProgress.CasesCompleted);
            var passRateDelta = currentPassRate - baselinePassRate;
            var casesFailedDelta = current.CasesFailed - baselineProgress.CasesFailed;

            if (passRateDelta < -0.05 && casesFailedDelta >= 2)
            {
                MetricsRegistry.EvaluationsRegressionDetected.Add(1,
                    new KeyValuePair<string, object?>("agent_definition_name", definition.Name));
                _logger.LogWarning(
                    "[EvaluationRunner] Regression detected: run '{RunId}' vs baseline '{BaselineId}'. " +
                    "passRateDelta={PassRateDelta:F3}, casesFailedDelta={CasesFailedDelta}.",
                    run.RunId, run.BaselineRunId, passRateDelta, casesFailedDelta);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[EvaluationRunner] Falha em DetectRegression para run '{RunId}'.", run.RunId);
        }
    }
}
