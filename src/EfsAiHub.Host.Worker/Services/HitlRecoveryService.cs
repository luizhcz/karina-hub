using System.Security.Cryptography;
using System.Text;
using EfsAiHub.Platform.Runtime.Interfaces;
using EfsAiHub.Core.Orchestration.Enums;
using EfsAiHub.Infra.Observability;
using EfsAiHub.Core.Abstractions.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace EfsAiHub.Host.Worker.Services;

/// <summary>
/// Varre periodicamente execuções com Status=Paused e tenta retomá-las a partir do
/// checkpoint persistido.
///
/// P1-B: roda em loop periódico (default 30s) além do startup, complementando o
/// PostgreSQL LISTEN/NOTIFY que é fire-and-forget e pode perder eventos.
///
/// Fix #A2: pagina o SELECT, limita concorrência com SemaphoreSlim, respeita back-pressure do
/// Chat via IExecutionSlotRegistry.TryAcquireSlot, usa scope por item e token de shutdown real.
/// </summary>
public sealed class HitlRecoveryService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<HitlRecoveryService> _logger;
    private readonly WorkflowEngineOptions _options;

    public HitlRecoveryService(
        IServiceScopeFactory scopeFactory,
        [FromKeyedServices("general")] NpgsqlDataSource dataSource,
        ILogger<HitlRecoveryService> logger,
        IOptions<WorkflowEngineOptions> options)
    {
        _scopeFactory = scopeFactory;
        _dataSource = dataSource;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Primeiro ciclo: imediato (startup recovery)
        await RecoverAllAsync(stoppingToken);

        var intervalSeconds = _options.HitlRecoveryIntervalSeconds;
        if (intervalSeconds <= 0)
        {
            _logger.LogInformation("[HitlRecovery] Polling periódico desabilitado (HitlRecoveryIntervalSeconds=0).");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));
        _logger.LogInformation(
            "[HitlRecovery] Polling periódico ativo a cada {Interval}s.", intervalSeconds);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RecoverAllAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[HitlRecovery] Erro no ciclo periódico de recovery.");
            }
        }
    }

    private async Task RecoverAllAsync(CancellationToken ct)
    {
        var concurrency = _options.HitlRecoveryConcurrency > 0 ? _options.HitlRecoveryConcurrency : 4;
        var batchSize = _options.HitlRecoveryBatchSize > 0 ? _options.HitlRecoveryBatchSize : 100;

        // Backlog inicial para métrica
        long total0;
        using (var scope0 = _scopeFactory.CreateScope())
        {
            var repo0 = scope0.ServiceProvider.GetRequiredService<IWorkflowExecutionRepository>();
            total0 = await repo0.CountPausedAsync(ct);
            MetricsRegistry.SetHitlRecoveryBacklog(total0);
            if (total0 == 0) return;
        }

        _logger.LogInformation(
            "[HitlRecovery] {Count} execução(ões) em Paused — iniciando recovery (concurrency={Concurrency}, batch={Batch}).",
            total0, concurrency, batchSize);

        using var gate = new SemaphoreSlim(concurrency, concurrency);
        var inflight = new List<Task>();
        int offset = 0;

        while (!ct.IsCancellationRequested)
        {
            IReadOnlyList<WorkflowExecution> page;
            using (var pageScope = _scopeFactory.CreateScope())
            {
                var repo = pageScope.ServiceProvider.GetRequiredService<IWorkflowExecutionRepository>();
                page = await repo.GetPausedExecutionsPagedAsync(offset, batchSize, ct);
            }

            if (page.Count == 0) break;

            foreach (var exec in page)
            {
                if (ct.IsCancellationRequested) break;

                await gate.WaitAsync(ct);
                var execCopy = exec;
                inflight.Add(Task.Run(async () =>
                {
                    try
                    {
                        await RecoverOneAsync(execCopy, ct);
                    }
                    finally
                    {
                        gate.Release();
                        try
                        {
                            using var s = _scopeFactory.CreateScope();
                            var repo = s.ServiceProvider.GetRequiredService<IWorkflowExecutionRepository>();
                            MetricsRegistry.SetHitlRecoveryBacklog(await repo.CountPausedAsync(CancellationToken.None));
                        }
                        catch (Exception backlogEx) { _logger.LogDebug(backlogEx, "[HitlRecovery] Falha ao atualizar backlog metric."); }
                    }
                }, ct));
            }

            offset += page.Count;
            if (page.Count < batchSize) break;
        }

        try { await Task.WhenAll(inflight); }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[HitlRecovery] Uma ou mais tarefas de recovery falharam (já logadas individualmente).");
        }
    }

    // ── Advisory lock para coordenação multi-pod ──────────────────────────────

    private readonly struct AdvisoryLockHandle : IAsyncDisposable
    {
        private readonly NpgsqlConnection _conn;
        private readonly long _key;

        public AdvisoryLockHandle(NpgsqlConnection conn, long key)
        {
            _conn = conn;
            _key = key;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var cmd = _conn.CreateCommand();
                cmd.CommandText = "SELECT pg_advisory_unlock(@key)";
                cmd.Parameters.AddWithValue("key", _key);
                await cmd.ExecuteScalarAsync();
            }
            catch { /* unlock best-effort — lock expira com a conexão de qualquer forma */ }
            finally
            {
                await _conn.DisposeAsync();
            }
        }
    }

    private static long HashExecutionId(string executionId)
        => BitConverter.ToInt64(SHA256.HashData(Encoding.UTF8.GetBytes(executionId)), 0);

    private async Task<AdvisoryLockHandle?> TryAcquireRecoveryLockAsync(string executionId, CancellationToken ct)
    {
        var key = HashExecutionId(executionId);
        var conn = await _dataSource.OpenConnectionAsync(ct);
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT pg_try_advisory_lock(@key)";
            cmd.Parameters.AddWithValue("key", key);
            var acquired = (bool)(await cmd.ExecuteScalarAsync(ct))!;
            if (!acquired)
            {
                await conn.DisposeAsync();
                return null;
            }
            return new AdvisoryLockHandle(conn, key);
        }
        catch
        {
            await conn.DisposeAsync();
            throw;
        }
    }

    // ── Recovery de uma execução ─────────────────────────────────────────────

    private async Task RecoverOneAsync(WorkflowExecution exec, CancellationToken ct)
    {
        // Advisory lock por execução: evita que dois pods processem a mesma execução simultaneamente
        var lockHandle = await TryAcquireRecoveryLockAsync(exec.ExecutionId, ct);
        if (lockHandle is null)
        {
            _logger.LogDebug("[HitlRecovery] Execução '{ExecutionId}' já sendo recuperada por outro pod.", exec.ExecutionId);
            return;
        }
        await using var _ = lockHandle.Value;

        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var executionRepo = sp.GetRequiredService<IWorkflowExecutionRepository>();
        var workflowDefRepo = sp.GetRequiredService<IWorkflowDefinitionRepository>();
        var agentRepo = sp.GetRequiredService<IAgentDefinitionRepository>();
        var workflowFactory = sp.GetRequiredService<IWorkflowFactory>();
        var runner = sp.GetRequiredService<WorkflowRunnerService>();
        var hitlService = sp.GetRequiredService<IHumanInteractionService>();
        var observers = sp.GetServices<IExecutionLifecycleObserver>();
        var chatRegistry = sp.GetRequiredService<IExecutionSlotRegistry>();
        var engineOpts = sp.GetRequiredService<IOptions<WorkflowEngineOptions>>().Value;

        try
        {
            // Busca o HITL mais recente (qualquer status) para esta execução.
            // Cobre o gap onde o HITL já foi Approved/Rejected (ex: pod morreu após persistir a
            // aprovação mas antes de consumir o NOTIFY) — antes esse caso era ignorado.
            var hitlRepo = sp.GetRequiredService<IHumanInteractionRepository>();
            var latestHitl = await hitlRepo.GetLatestByExecutionIdAsync(exec.ExecutionId, ct);
            if (latestHitl is null)
            {
                _logger.LogWarning(
                    "[HitlRecovery] Execução '{ExecutionId}' em Paused sem nenhum HITL — marcando como Failed.",
                    exec.ExecutionId);
                await FailRecoveryAsync(exec, "Execução em Paused sem HITL associado.", observers, executionRepo);
                return;
            }

            // Caso 3: HITL já expirado — marca execução como Failed
            if (latestHitl.Status == HumanInteractionStatus.Expired)
            {
                _logger.LogWarning(
                    "[HitlRecovery] Execução '{ExecutionId}' em Paused com HITL Expired '{InteractionId}' — marcando como Failed.",
                    exec.ExecutionId, latestHitl.InteractionId);
                await FailRecoveryAsync(exec, "HITL expirado — execução não pode ser retomada.", observers, executionRepo);
                return;
            }

            // Caso 2: HITL já resolvido (Approved/Rejected) — resolve imediatamente o TCS para retomar
            if (latestHitl.Status is HumanInteractionStatus.Approved or HumanInteractionStatus.Rejected)
            {
                _logger.LogInformation(
                    "[HitlRecovery] Execução '{ExecutionId}' em Paused com HITL já resolvido ({Status}) — retomando imediatamente.",
                    exec.ExecutionId, latestHitl.Status);
                MetricsRegistry.HitlOrphanedRecoveries.Add(1);

                // Injeta o HITL no cache em memória e cria TCS que será resolvido logo abaixo
                hitlService.InjectForRecovery(latestHitl);
                var tcsResolved = hitlService.ReRegisterPending(latestHitl.InteractionId);
                if (tcsResolved is not null)
                {
                    // Resolve imediatamente — sem persistir (já está no banco)
                    tcsResolved.TrySetResult(latestHitl.Resolution ?? string.Empty);
                }
            }
            else
            {
                // Caso 1: HITL Pending — comportamento original (recria TCS, espera humano)
                var pending = hitlService.GetPendingForExecution(exec.ExecutionId);
                if (pending is null)
                {
                    await FailRecoveryAsync(exec, "HITL Pending não encontrado no cache em memória.", observers, executionRepo);
                    return;
                }

                var tcs = hitlService.ReRegisterPending(pending.InteractionId);
                if (tcs is null)
                {
                    await FailRecoveryAsync(exec, "Não foi possível re-registrar TCS do HITL.", observers, executionRepo);
                    return;
                }
            }

            var definition = await workflowDefRepo.GetByIdAsync(exec.WorkflowId, ct);
            if (definition is null)
            {
                await FailRecoveryAsync(exec, "WorkflowDefinition não encontrada.", observers, executionRepo);
                return;
            }

            // Back-pressure: se o Chat estiver no teto, não força recovery agora.
            if (!await chatRegistry.TryAcquireSlotAsync())
            {
                _logger.LogWarning(
                    "[HitlRecovery] Back-pressure do Chat no topo — adiando recovery de '{ExecutionId}'.",
                    exec.ExecutionId);
                return;
            }

            bool slotReleased = false;
            try
            {
                exec.Metadata.TryGetValue("startAgentId", out var startAgentId);
                var executable = await workflowFactory.BuildWorkflowAsync(definition, startAgentId, ct);

                int timeout = definition.Configuration.TimeoutSeconds > 0
                    ? definition.Configuration.TimeoutSeconds
                    : engineOpts.DefaultTimeoutSeconds;

                var agentNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var guardMode = EfsAiHub.Core.Agents.Execution.AccountGuardMode.None;
                foreach (var agentRef in definition.Agents)
                {
                    var agentDef = await agentRepo.GetByIdAsync(agentRef.AgentId, ct);
                    if (agentDef is null) continue;
                    agentNames[agentDef.Id] = agentDef.Name;
                    if (guardMode == EfsAiHub.Core.Agents.Execution.AccountGuardMode.None &&
                        agentDef.Middlewares.Any(m => m.Enabled &&
                            string.Equals(m.Type, "AccountGuard", StringComparison.OrdinalIgnoreCase)))
                    {
                        guardMode = EfsAiHub.Core.Agents.Execution.AccountGuardMode.ClientLocked;
                    }
                }

                await runner.ResumeAsync(
                    exec,
                    executable.Value,
                    timeout,
                    definition.Configuration.MaxAgentInvocations,
                    definition.Configuration.MaxTokensPerExecution,
                    definition.Configuration.MaxCostUsdPerExecution,
                    guardMode,
                    agentNames,
                    definition.OrchestrationMode,
                    ct);

                slotReleased = true;
                await chatRegistry.ReleaseSlotAsync();

                MetricsRegistry.HitlRecoveries.Add(1,
                    new KeyValuePair<string, object?>("workflow.id", exec.WorkflowId));
                _logger.LogInformation(
                    "[HitlRecovery] Execução '{ExecutionId}' retomada com sucesso.", exec.ExecutionId);
            }
            finally
            {
                if (!slotReleased) await chatRegistry.ReleaseSlotAsync();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation(
                "[HitlRecovery] Recovery de '{ExecutionId}' cancelado por shutdown.", exec.ExecutionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[HitlRecovery] Falha ao processar execução '{ExecutionId}'.", exec.ExecutionId);
            try { await FailRecoveryAsync(exec, ex.Message, observers, executionRepo); }
            catch (Exception inner)
            {
                _logger.LogError(inner,
                    "[HitlRecovery] Falha adicional ao marcar execução '{ExecutionId}' como Failed.", exec.ExecutionId);
            }
        }
    }

    private async Task FailRecoveryAsync(
        WorkflowExecution exec,
        string reason,
        IEnumerable<IExecutionLifecycleObserver> observers,
        IWorkflowExecutionRepository executionRepo)
    {
        exec.Status = WorkflowStatus.Failed;
        exec.ErrorCategory = ErrorCategory.CheckpointRecoveryFailed;
        exec.ErrorMessage = $"HITL recovery falhou: {reason}";
        exec.CompletedAt = DateTime.UtcNow;
        await executionRepo.UpdateAsync(exec, CancellationToken.None);

        if (exec.Metadata.TryGetValue("conversationId", out var convId) && !string.IsNullOrEmpty(convId))
        {
            foreach (var observer in observers)
            {
                try
                {
                    await observer.OnRecoveryFailedAsync(convId, exec.ExecutionId, reason, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "[HitlRecovery] Observer '{Observer}' falhou em OnRecoveryFailedAsync para execução '{ExecutionId}'.",
                        observer.GetType().Name, exec.ExecutionId);
                }
            }
        }
    }
}
