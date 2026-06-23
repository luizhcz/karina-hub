using EfsAiHub.Core.Abstractions.BackgroundServices;
using EfsAiHub.Core.Abstractions.Execution;
using EfsAiHub.Core.Agents.Responses;
using EfsAiHub.Host.Worker.Services.Handlers;
using EfsAiHub.Infra.Observability;
using EfsAiHub.Platform.Runtime.Configuration;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Host.Worker.Services;

/// <summary>
/// Consumer da fila standalone (<c>aihub.background_response_jobs</c>). Cada
/// tick:
/// <list type="number">
///   <item>Calcula folga de slots cross-pod via <see cref="IDistributedSlotCounter"/>.</item>
///   <item>Lease atômico (FOR UPDATE SKIP LOCKED) de até N jobs Queued, com cota
///         por workflow aplicada na própria query.</item>
///   <item>Pra cada job: encontra o <see cref="IStandaloneJobHandler"/> que
///         aceita e delega o processamento. Heartbeat + cancellation
///         cooperativo continuam aqui no dispatcher pra cobrir todos os
///         handlers uniformemente.</item>
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
    private const string HeartbeatName = "StandaloneJobDispatcher";
    private const string SlotScope = "standalone";

    private readonly IBackgroundResponseRepository _jobs;
    private readonly IDistributedSlotCounter _slots;
    private readonly IEnumerable<IStandaloneJobHandler> _handlers;
    private readonly StandalonePoolsOptions _options;
    private readonly IBackgroundServiceHeartbeatSink _heartbeat;
    private readonly ILogger<StandaloneJobDispatcherService> _logger;
    private readonly string _podId;
    private readonly TimeSpan _slotTtl;

    private int _localActive;

    public StandaloneJobDispatcherService(
        IBackgroundResponseRepository jobs,
        IDistributedSlotCounter slots,
        IEnumerable<IStandaloneJobHandler> handlers,
        IOptions<StandalonePoolsOptions> options,
        IBackgroundServiceHeartbeatSink heartbeat,
        ILogger<StandaloneJobDispatcherService> logger)
    {
        _jobs = jobs;
        _slots = slots;
        _handlers = handlers;
        _options = options.Value;
        _heartbeat = heartbeat;
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
        _heartbeat.Started(HeartbeatName, DateTimeOffset.UtcNow);

        // Barreira de último recurso: ExecuteAsync NUNCA deve deixar uma exceção
        // escapar. O default do host é StopHost — um escape aqui derrubaria a
        // aplicação inteira. Mesmo com HostOptions.BackgroundServiceExceptionBehavior=
        // Ignore no composition root, mantemos o guard (defense-in-depth) pra que um
        // escape inesperado encerre só o dispatcher, de forma logada/observável.
        try
        {
            if (!_options.Enabled)
            {
                _logger.LogInformation(
                    "[StandaloneDispatcher] Desligado (StandalonePools:Enabled=false). HostedService idle.");
                return;
            }

            _logger.LogInformation(
                "[StandaloneDispatcher] Ativo pod={Pod} globalCap={Global} perWorkflow={PerWf} leaseTtl={Lease}s heartbeat={Hb}s slotTtl={SlotTtl}m handlers={Handlers}",
                _podId, _options.GlobalConcurrency, _options.DefaultMaxConcurrentPerWorkflow,
                _options.LeaseTtlSeconds, _options.HeartbeatSeconds, (int)_slotTtl.TotalMinutes,
                string.Join(",", _handlers.Select(h => h.GetType().Name)));

            var idle = TimeSpan.FromSeconds(Math.Max(1, _options.PollIdleSeconds));
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var leased = await PollOnceAsync(stoppingToken).ConfigureAwait(false);
                    _heartbeat.RecordSuccess(HeartbeatName, DateTimeOffset.UtcNow);
                    if (leased == 0)
                        await Task.Delay(idle, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _heartbeat.RecordError(HeartbeatName, DateTimeOffset.UtcNow, ex);
                    // O log NÃO pode derrubar o loop: um provider/exporter que lance
                    // aqui (dentro do catch) escaparia do ExecuteAsync. Protegido.
                    try { _logger.LogError(ex, "[StandaloneDispatcher] Falha no loop. Aplicando backoff."); }
                    catch { /* logging não pode matar o dispatcher */ }
                    try { await Task.Delay(idle, stoppingToken).ConfigureAwait(false); } catch { /* shutdown */ }
                }
            }

            _logger.LogInformation("[StandaloneDispatcher] Encerrando — aguardando jobs em andamento drenarem.");
            // Aguarda o ProcessJobAsync de cada job ativo limpar via finally — não
            // bloqueia indefinidamente. Slots restantes liberam por TTL Redis.
            for (var i = 0; i < 30 && Volatile.Read(ref _localActive) > 0; i++)
                await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown cooperativo — encerramento normal.
        }
        catch (Exception fatal)
        {
            // Última linha de defesa: encerra o dispatcher de forma controlada em vez
            // de propagar (o que, no default StopHost, derrubaria a aplicação inteira).
            _heartbeat.RecordError(HeartbeatName, DateTimeOffset.UtcNow, fatal);
            try
            {
                _logger.LogCritical(fatal,
                    "[StandaloneDispatcher] Falha fatal não-tratada no loop principal — dispatcher encerrando SEM derrubar o host.");
            }
            catch { /* nem o log da falha fatal pode propagar */ }
        }
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
            // Backpressure puro (o job não chegou a rodar) — devolve pra Queued via
            // DeferAsync, que NÃO consome tentativa. FailAsync aqui penalizaria o job
            // por uma corrida de capacidade alheia ao seu processamento.
            if (!acquiredSlot)
            {
                _logger.LogDebug(
                    "[StandaloneDispatcher] Global cap atingido cross-pod ao alocar slot pro job {JobId} — devolvendo pra Queued.",
                    job.JobId);
                MetricsRegistry.StandaloneAdmissionRejected.Add(1,
                    new KeyValuePair<string, object?>("reason", "global_safety"));
                await _jobs.DeferAsync(
                    job.JobId,
                    _podId,
                    "Global concurrency cap reached, queued for retry",
                    DateTime.UtcNow.AddSeconds(_options.PollIdleSeconds * 2),
                    ct).ConfigureAwait(false);
                continue;
            }

            // Lease bem-sucedido: emite queue_seconds (CreatedAt → StartedAt).
            if (job.StartedAt is not null)
            {
                var queueSeconds = (job.StartedAt.Value - job.CreatedAt).TotalSeconds;
                MetricsRegistry.StandaloneJobQueueSeconds.Record(queueSeconds,
                    new KeyValuePair<string, object?>("workflow_id", job.WorkflowId ?? "<none>"));
            }

            Interlocked.Increment(ref _localActive);
            _ = Task.Run(() => ProcessJobWithCleanupAsync(job, ct), ct);
        }

        return leased.Count;
    }

    private async Task ProcessJobWithCleanupAsync(BackgroundResponseJob job, CancellationToken ct)
    {
        StandaloneJobContext? ctx = null;
        try
        {
            ctx = await ProcessJobAsync(job, ct).ConfigureAwait(false);
        }
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

            // Métricas de latência ponta-a-ponta lidas do contexto (sem
            // re-leitura do DB). LastTerminalStatus é setado pelo handler
            // quando chama Complete/Fail ownership-aware bem-sucedido.
            if (ctx?.LastTerminalStatus is { } terminal && ctx.LastTerminalAt is { } terminalAt)
            {
                var totalSeconds = (terminalAt - job.CreatedAt).TotalSeconds;
                var workflowTag = new KeyValuePair<string, object?>("workflow_id", job.WorkflowId ?? "<none>");
                var statusTag = new KeyValuePair<string, object?>("status", terminal.ToString());
                MetricsRegistry.StandaloneJobTotalSeconds.Record(totalSeconds, workflowTag, statusTag);
                MetricsRegistry.StandaloneJobsCompleted.Add(1, workflowTag, statusTag);
            }
        }
    }

    /// <summary>
    /// Setup do lifecycle por job (heartbeat + cancellation cooperativo) e
    /// delegação ao handler que aceita o job. Heartbeat detecta lease perdido
    /// → <c>jobCts.Cancel()</c> → handler aborta antes de reescrever estado
    /// que outro pod já assumiu. Retorna o <see cref="StandaloneJobContext"/>
    /// criado pra que o caller leia <c>LastTerminalStatus</c> pras métricas.
    /// </summary>
    private async Task<StandaloneJobContext?> ProcessJobAsync(BackgroundResponseJob job, CancellationToken outerCt)
    {
        var handler = ResolveHandler(job);
        if (handler is null)
        {
            await _jobs.FailAsync(
                job.JobId, _podId,
                "Nenhum IStandaloneJobHandler aceitou o job.",
                null, permanent: true, outerCt).ConfigureAwait(false);
            return null;
        }

        using var jobCts = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
        var heartbeatTask = HeartbeatLoopAsync(job.JobId, jobCts);

        var ctx = new StandaloneJobContext(_jobs, job.JobId, _podId);

        try
        {
            await handler.ProcessAsync(job, ctx, jobCts.Token).ConfigureAwait(false);
            return ctx;
        }
        catch (OperationCanceledException) when (jobCts.IsCancellationRequested && !outerCt.IsCancellationRequested)
        {
            // Heartbeat detectou lease perdido e cancelou. NÃO escrever estado —
            // outro pod já assumiu o job.
            _logger.LogWarning(
                "[StandaloneDispatcher] Job {JobId} abandonado por lease perdido (outro pod assumiu).",
                job.JobId);
            return ctx;
        }
        finally
        {
            jobCts.Cancel();
            try { await heartbeatTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { /* expected */ }
            catch { /* heartbeat falha já loggada internamente */ }
        }
    }

    private IStandaloneJobHandler? ResolveHandler(BackgroundResponseJob job)
    {
        foreach (var handler in _handlers)
        {
            if (handler.CanHandle(job)) return handler;
        }
        return null;
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
                    // Cancela TODO o processamento — handler aborta sem reescrever estado.
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
}
