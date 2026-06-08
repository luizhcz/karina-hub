using EfsAiHub.Core.Abstractions.BackgroundServices;
using EfsAiHub.Core.Agents.Responses;
using EfsAiHub.Infra.Observability;
using EfsAiHub.Platform.Runtime.Configuration;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Host.Worker.Services;

/// <summary>
/// Resgata jobs cujo <c>LeaseUntil</c> já passou (pod morreu, processo
/// pendurou, heartbeat falhou) devolvendo-os para <c>Status='Queued'</c> com
/// <c>NextAttemptAt</c> calculado pelo backoff base.
///
/// Roda em paralelo com <see cref="StandaloneJobDispatcherService"/> — não é
/// gateado pelo mesmo <see cref="StandalonePoolsOptions.Enabled"/> porque
/// jobs órfãos podem existir mesmo após a feature ser desligada. Mantemos
/// o reaper sempre ativo (idempotente, custo desprezível) pra evitar
/// jobs travados em Running indefinidamente.
/// </summary>
public sealed class StuckLeaseReaper : BackgroundService
{
    private const string HeartbeatName = "StuckLeaseReaper";

    private readonly IBackgroundResponseRepository _jobs;
    private readonly StandalonePoolsOptions _options;
    private readonly IBackgroundServiceHeartbeatSink _heartbeat;
    private readonly ILogger<StuckLeaseReaper> _logger;

    public StuckLeaseReaper(
        IBackgroundResponseRepository jobs,
        IOptions<StandalonePoolsOptions> options,
        IBackgroundServiceHeartbeatSink heartbeat,
        ILogger<StuckLeaseReaper> logger)
    {
        _jobs = jobs;
        _options = options.Value;
        _heartbeat = heartbeat;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _heartbeat.Started(HeartbeatName, DateTimeOffset.UtcNow);
        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.ReaperIntervalSeconds));
        _logger.LogInformation(
            "[StuckLeaseReaper] Ativo — varredura a cada {Interval}s, reclaim backoff {Backoff}s, maxAttempts {Max}.",
            (int)interval.TotalSeconds, _options.ReaperReclaimBackoffSeconds, _options.MaxAttempts);

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                var result = await _jobs.ReclaimExpiredLeasesAsync(
                    TimeSpan.FromSeconds(_options.ReaperReclaimBackoffSeconds),
                    _options.MaxAttempts,
                    stoppingToken).ConfigureAwait(false);

                if (result.Requeued > 0)
                {
                    _logger.LogWarning(
                        "[StuckLeaseReaper] {Count} job(s) com lease expirado devolvido(s) pra Queued.",
                        result.Requeued);
                    MetricsRegistry.StandaloneStuckLeasesRecovered.Add(result.Requeued,
                        new KeyValuePair<string, object?>("outcome", "requeued"));
                }
                if (result.FailedMaxAttempts > 0)
                {
                    _logger.LogError(
                        "[StuckLeaseReaper] {Count} job(s) promovidos a Failed por atingir MaxAttempts={Max}.",
                        result.FailedMaxAttempts, _options.MaxAttempts);
                    MetricsRegistry.StandaloneStuckLeasesRecovered.Add(result.FailedMaxAttempts,
                        new KeyValuePair<string, object?>("outcome", "failed_max_attempts"));
                }
                _heartbeat.RecordSuccess(HeartbeatName, DateTimeOffset.UtcNow);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _heartbeat.RecordError(HeartbeatName, DateTimeOffset.UtcNow, ex);
                _logger.LogError(ex, "[StuckLeaseReaper] Falha no ciclo periódico.");
            }
        }
    }
}
