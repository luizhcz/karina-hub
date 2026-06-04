using EfsAiHub.Core.Abstractions.Execution;
using EfsAiHub.Core.Agents.Responses;
using EfsAiHub.Core.Orchestration.Workflows;
using EfsAiHub.Platform.Runtime.Configuration;
using EfsAiHub.Platform.Runtime.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Host.Worker.Services;

/// <summary>
/// Consumer da fila standalone (<c>aihub.background_response_jobs</c>). Cada
/// tick:
/// <list type="number">
///   <item>Calcula folga de slots cross-pod via <see cref="IDistributedSlotCounter"/>.</item>
///   <item>Lease atômico (FOR UPDATE SKIP LOCKED) de até N jobs Queued, com cota
///         por workflow aplicada na própria query.</item>
///   <item>Pra cada job: dispara workflow via <see cref="IWorkflowDispatcher"/>,
///         polla <c>workflow_executions</c> até terminal e atualiza o job com
///         ownership-aware UPDATE.</item>
/// </list>
///
/// Gateado por <see cref="StandalonePoolsOptions.Enabled"/> — quando false,
/// loop fica parado e endpoints respondem 503.
///
/// Cross-pod safety:
/// <list type="bullet">
///   <item>Slot counter Redis (<c>scope=standalone</c>) é o teto global. TTL =
///         <see cref="StandalonePoolsOptions.JobMaxLifetimeMinutes"/> + 5 min
///         pra cobrir workflows longos sem expirar a chave Redis no meio.</item>
///   <item>Cota por workflow vive no SQL do <c>TryLeaseAsync</c> — não há
///         retry-loop interno por excesso de cota.</item>
///   <item>Lease com heartbeat (10s) + reaper (30s) cobre crash do pod.
///         Heartbeat detecta "lease perdido" e cancela o processamento via
///         <c>CancellationTokenSource</c> linkado pra evitar dupla execução.</item>
/// </list>
/// </summary>
public sealed class StandaloneJobDispatcherService : BackgroundService
{
    private const string SlotScope = "standalone";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IBackgroundResponseRepository _jobs;
    private readonly IDistributedSlotCounter _slots;
    private readonly StandalonePoolsOptions _options;
    private readonly ILogger<StandaloneJobDispatcherService> _logger;
    private readonly string _podId;
    private readonly TimeSpan _slotTtl;

    private int _localActive;

    public StandaloneJobDispatcherService(
        IServiceScopeFactory scopeFactory,
        IBackgroundResponseRepository jobs,
        IDistributedSlotCounter slots,
        IOptions<StandalonePoolsOptions> options,
        ILogger<StandaloneJobDispatcherService> logger)
    {
        _scopeFactory = scopeFactory;
        _jobs = jobs;
        _slots = slots;
        _options = options.Value;
        _logger = logger;
        // PodId estável por processo — diferencia leases entre réplicas e fica
        // visível na coluna LeasedBy pra debug/reaper.
        _podId = $"{Environment.MachineName}:{Environment.ProcessId}";
        // Slot TTL acomoda o workflow inteiro mais 5min de buffer pra que a
        // chave Redis não expire no meio de um workflow longo (sem
        // PEXPIRE periódico). Trade-off: pod crash vaza slot por até esse
        // tempo; reaper devolve o job ao pool de Queued em paralelo.
        _slotTtl = TimeSpan.FromMinutes(Math.Max(1, _options.JobMaxLifetimeMinutes) + 5);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation(
                "[StandaloneDispatcher] Desligado (StandalonePools:Enabled=false). HostedService idle.");
            return;
        }

        _logger.LogInformation(
            "[StandaloneDispatcher] Ativo pod={Pod} globalCap={Global} perWorkflow={PerWf} leaseTtl={Lease}s heartbeat={Hb}s slotTtl={SlotTtl}m",
            _podId, _options.GlobalConcurrency, _options.DefaultMaxConcurrentPerWorkflow,
            _options.LeaseTtlSeconds, _options.HeartbeatSeconds, (int)_slotTtl.TotalMinutes);

        var idle = TimeSpan.FromSeconds(Math.Max(1, _options.PollIdleSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var leased = await PollOnceAsync(stoppingToken).ConfigureAwait(false);
                if (leased == 0)
                    await Task.Delay(idle, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[StandaloneDispatcher] Falha no loop. Aplicando backoff.");
                try { await Task.Delay(idle, stoppingToken).ConfigureAwait(false); } catch { /* shutdown */ }
            }
        }

        _logger.LogInformation("[StandaloneDispatcher] Encerrando — aguardando jobs em andamento drenarem.");
        // Aguarda o ProcessJobAsync de cada job ativo limpar via finally — não
        // bloqueia indefinidamente. Slots restantes liberam por TTL Redis.
        for (var i = 0; i < 30 && Volatile.Read(ref _localActive) > 0; i++)
            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
    }

    /// <summary>Uma iteração: calcula folga, lease, dispara processamento fire-and-forget.</summary>
    private async Task<int> PollOnceAsync(CancellationToken ct)
    {
        var localFree = Math.Max(0, _options.BatchSize - Volatile.Read(ref _localActive));
        if (localFree == 0) return 0;

        var globalUsed = await _slots.GetActiveCountAsync(SlotScope).ConfigureAwait(false);
        var globalFree = Math.Max(0, _options.GlobalConcurrency - globalUsed);
        var leaseSize = Math.Min(localFree, globalFree);
        if (leaseSize == 0) return 0;

        var leased = await _jobs.TryLeaseAsync(
            leaseSize,
            _podId,
            TimeSpan.FromSeconds(_options.LeaseTtlSeconds),
            _options.DefaultMaxConcurrentPerWorkflow,
            ct).ConfigureAwait(false);
        if (leased.Count == 0) return 0;

        foreach (var job in leased)
        {
            var acquiredSlot = await _slots.TryAcquireAsync(SlotScope, _options.GlobalConcurrency, _slotTtl)
                .ConfigureAwait(false);
            // Race aceitável: outro pod adquiriu slot entre o GetActiveCount e o TryAcquire.
            // Devolve o job pra Queued com NextAttemptAt curto e segue.
            if (!acquiredSlot)
            {
                _logger.LogDebug(
                    "[StandaloneDispatcher] Global cap atingido cross-pod ao alocar slot pro job {JobId} — devolvendo pra Queued.",
                    job.JobId);
                await _jobs.FailAsync(
                    job.JobId,
                    _podId,
                    "Global concurrency cap reached, queued for retry",
                    DateTime.UtcNow.AddSeconds(_options.PollIdleSeconds * 2),
                    permanent: false, ct).ConfigureAwait(false);
                continue;
            }

            Interlocked.Increment(ref _localActive);
            _ = Task.Run(() => ProcessJobWithCleanupAsync(job, ct), ct);
        }

        return leased.Count;
    }

    private async Task ProcessJobWithCleanupAsync(BackgroundResponseJob job, CancellationToken ct)
    {
        try { await ProcessJobAsync(job, ct).ConfigureAwait(false); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[StandaloneDispatcher] Erro não-tratado no job {JobId}.", job.JobId);
            // Fallback: marca Failed permanente (ownership-aware — só atualiza se
            // ainda somos donos do lease) pra não ciclar infinito.
            try
            {
                await _jobs.FailAsync(job.JobId, _podId, ex.Message, null, permanent: true, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch { /* engole */ }
        }
        finally
        {
            try { await _slots.ReleaseAsync(SlotScope).ConfigureAwait(false); } catch { /* ignore */ }
            Interlocked.Decrement(ref _localActive);
        }
    }

    /// <summary>
    /// Dispara o workflow alvo e bloqueia até terminal (polling). Heartbeat
    /// roda em paralelo renovando o lease — se for "roubado" pelo reaper
    /// (cenário de pod stale), cancela o próprio processamento via
    /// <see cref="CancellationTokenSource"/> linkado, abortando antes de
    /// reescrever estado do job (que pertence a outro pod agora).
    /// </summary>
    private async Task ProcessJobAsync(BackgroundResponseJob job, CancellationToken outerCt)
    {
        if (string.IsNullOrWhiteSpace(job.WorkflowId))
        {
            await _jobs.FailAsync(job.JobId, _podId, "WorkflowId obrigatório no job standalone.", null, permanent: true, outerCt)
                .ConfigureAwait(false);
            return;
        }

        using var jobCts = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
        var heartbeatTask = HeartbeatLoopAsync(job.JobId, jobCts);

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<IWorkflowDispatcher>();
            var executionRepo = scope.ServiceProvider.GetRequiredService<IWorkflowExecutionRepository>();

            var metadata = new Dictionary<string, string>
            {
                ["standaloneJobId"] = job.JobId
            };

            var executionId = await dispatcher.TriggerAsync(
                job.WorkflowId,
                job.Input,
                metadata,
                source: ExecutionSource.Api,
                mode: ExecutionMode.Production,
                workflowVersionId: job.AgentVersionId,
                ct: jobCts.Token).ConfigureAwait(false);

            await _jobs.SetExecutionIdAsync(job.JobId, executionId, jobCts.Token).ConfigureAwait(false);
            await PollUntilTerminalAsync(job, executionId, executionRepo, jobCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (jobCts.IsCancellationRequested && !outerCt.IsCancellationRequested)
        {
            // Heartbeat detectou lease perdido e cancelou. NÃO escrever estado —
            // outro pod já assumiu o job.
            _logger.LogWarning(
                "[StandaloneDispatcher] Job {JobId} abandonado por lease perdido (outro pod assumiu).",
                job.JobId);
        }
        finally
        {
            jobCts.Cancel();
            try { await heartbeatTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { /* expected */ }
            catch { /* heartbeat falha já loggada internamente */ }
        }
    }

    /// <summary>Loop de polling do workflow_executions até terminal ou deadline.</summary>
    private async Task PollUntilTerminalAsync(
        BackgroundResponseJob job,
        string executionId,
        IWorkflowExecutionRepository executionRepo,
        CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddMinutes(_options.JobMaxLifetimeMinutes);
        var pollDelay = TimeSpan.FromSeconds(2);

        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            await Task.Delay(pollDelay, ct).ConfigureAwait(false);
            var execution = await executionRepo.GetByIdAsync(executionId, ct).ConfigureAwait(false);
            if (execution is null) continue;

            switch (execution.Status)
            {
                case WorkflowStatus.Completed:
                    await _jobs.CompleteAsync(job.JobId, _podId, execution.Output, ct).ConfigureAwait(false);
                    return;

                case WorkflowStatus.Failed:
                case WorkflowStatus.Cancelled:
                    var cancelled = execution.Status == WorkflowStatus.Cancelled;
                    var permanent = cancelled || job.Attempt >= _options.MaxAttempts;
                    DateTime? next = permanent ? null : DateTime.UtcNow.AddSeconds(CalcBackoffSeconds(job.Attempt));
                    var errMsg = execution.ErrorMessage ?? (cancelled ? "Execution cancelled" : "Execution failed");
                    await _jobs.FailAsync(job.JobId, _podId, errMsg, next, permanent, ct).ConfigureAwait(false);
                    return;

                // Paused (HITL), Pending, Running — segue pollando.
            }
        }

        // Deadline atingido — encerra como Failed permanente.
        if (!ct.IsCancellationRequested)
        {
            await _jobs.FailAsync(
                job.JobId,
                _podId,
                $"Job excedeu JobMaxLifetimeMinutes={_options.JobMaxLifetimeMinutes}m",
                null,
                permanent: true,
                ct).ConfigureAwait(false);
        }
    }

    private async Task HeartbeatLoopAsync(string jobId, CancellationTokenSource jobCts)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.HeartbeatSeconds));
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(jobCts.Token).ConfigureAwait(false))
            {
                var renewed = await _jobs.RenewLeaseAsync(
                    jobId, _podId, TimeSpan.FromSeconds(_options.LeaseTtlSeconds), jobCts.Token)
                    .ConfigureAwait(false);
                if (!renewed)
                {
                    _logger.LogWarning(
                        "[StandaloneDispatcher] Lease perdido pro job {JobId} (roubado pelo reaper?). Sinalizando cancelamento.",
                        jobId);
                    // Cancela TODO o processamento — PollUntilTerminal aborta, ProcessJobAsync
                    // captura OperationCanceledException sem reescrever estado do job.
                    jobCts.Cancel();
                    return;
                }
            }
        }
        catch (OperationCanceledException) { /* normal no shutdown */ }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[StandaloneDispatcher] Heartbeat falhou pro job {JobId}.", jobId);
        }
    }

    private int CalcBackoffSeconds(int attempt)
    {
        var baseSecs = Math.Max(1, _options.RetryBackoffBaseSeconds);
        // Cap exponencial em 2^10 ≈ 1024 multiplier — evita overflow e mantém retry razoável.
        var multiplier = Math.Pow(2, Math.Min(attempt, 10));
        return (int)(baseSecs * multiplier);
    }
}
