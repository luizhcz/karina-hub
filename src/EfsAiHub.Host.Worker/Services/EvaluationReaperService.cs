using EfsAiHub.Core.Agents.Evaluation;
using EfsAiHub.Infra.Observability;
using EfsAiHub.Platform.Runtime.Evaluation;

namespace EfsAiHub.Host.Worker.Services;

/// <summary>
/// Varre periodicamente <see cref="EvaluationRun"/> em <c>Running</c> sem
/// heartbeat há mais que <see cref="EvaluationOptions.HeartbeatTimeoutSeconds"/>
/// e marca como <c>Failed</c> com <c>LastError="HeartbeatTimeout"</c>. Sem
/// isso, runs stuck (pod crashou mid-run) seguram budget reservado, bloqueiam
/// o rate limit (1 concorrente por projeto) e poluem dashboards.
/// Bootstrap: também varre na startup. Kill-switch SRE:
/// <c>EvaluationOptions:Enabled=false</c> → no-op.
/// </summary>
public sealed class EvaluationReaperService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<EvaluationOptions> _options;
    private readonly ILogger<EvaluationReaperService> _logger;

    public EvaluationReaperService(
        IServiceProvider serviceProvider,
        IOptions<EvaluationOptions> options,
        ILogger<EvaluationReaperService> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = _options.Value;
        if (!opts.Enabled)
        {
            _logger.LogInformation("[EvaluationReaper] Desabilitado via EvaluationOptions:Enabled=false.");
            return;
        }

        var staleAfter = TimeSpan.FromSeconds(Math.Max(60, opts.HeartbeatTimeoutSeconds));
        var interval = TimeSpan.FromSeconds(Math.Max(15, opts.ReaperIntervalSeconds));

        _logger.LogInformation(
            "[EvaluationReaper] Ativo — staleAfter={StaleAfter}s, interval={Interval}s.",
            (int)staleAfter.TotalSeconds, (int)interval.TotalSeconds);

        // Bootstrap: varre imediatamente para cobrir crash do pod entre Running e Completed.
        try { await ReapStaleAsync(staleAfter, stoppingToken); }
        catch (Exception ex) { _logger.LogError(ex, "[EvaluationReaper] Falha no bootstrap reap."); }

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ReapStaleAsync(staleAfter, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[EvaluationReaper] Falha no ciclo periódico.");
            }
        }
    }

    private async Task ReapStaleAsync(TimeSpan staleAfter, CancellationToken ct)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var runRepo = scope.ServiceProvider.GetRequiredService<IEvaluationRunRepository>();

        var stale = await runRepo.ListStaleRunningAsync(staleAfter, ct);
        if (stale.Count == 0)
        {
            MetricsRegistry.SetEvaluationsHeartbeatAgeSeconds(0);
            return;
        }

        // Idade do mais antigo (gauge).
        var now = DateTime.UtcNow;
        var oldestAge = stale
            .Select(r => r.LastHeartbeatAt.HasValue ? (long)(now - r.LastHeartbeatAt.Value).TotalSeconds : (long)staleAfter.TotalSeconds)
            .DefaultIfEmpty(0)
            .Max();
        MetricsRegistry.SetEvaluationsHeartbeatAgeSeconds(oldestAge);

        foreach (var run in stale)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var transitioned = await runRepo.TryTransitionStatusAsync(
                    run.RunId,
                    EvaluationRunStatus.Running,
                    EvaluationRunStatus.Failed,
                    lastError: "HeartbeatTimeout",
                    ct: ct);

                if (transitioned)
                {
                    MetricsRegistry.EvaluationsRunsReaped.Add(1);
                    _logger.LogWarning(
                        "[EvaluationReaper] Run '{RunId}' marcada como Failed (heartbeat timeout). " +
                        "LastHeartbeatAt={LastHeartbeat}.",
                        run.RunId, run.LastHeartbeatAt);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[EvaluationReaper] Falha marcando run '{RunId}' como Failed.", run.RunId);
            }
        }
    }
}
