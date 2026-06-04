using EfsAiHub.Core.Agents.Responses;
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
    private readonly IBackgroundResponseRepository _jobs;
    private readonly StandalonePoolsOptions _options;
    private readonly ILogger<StuckLeaseReaper> _logger;

    public StuckLeaseReaper(
        IBackgroundResponseRepository jobs,
        IOptions<StandalonePoolsOptions> options,
        ILogger<StuckLeaseReaper> logger)
    {
        _jobs = jobs;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.ReaperIntervalSeconds));
        _logger.LogInformation(
            "[StuckLeaseReaper] Ativo — varredura a cada {Interval}s, reclaim backoff {Backoff}s, maxAttempts {Max}.",
            (int)interval.TotalSeconds, _options.ReaperReclaimBackoffSeconds, _options.MaxAttempts);

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                var reclaimed = await _jobs.ReclaimExpiredLeasesAsync(
                    TimeSpan.FromSeconds(_options.ReaperReclaimBackoffSeconds),
                    _options.MaxAttempts,
                    stoppingToken).ConfigureAwait(false);

                if (reclaimed > 0)
                    _logger.LogWarning(
                        "[StuckLeaseReaper] {Count} job(s) com lease expirado devolvido(s) pra Queued (ou Failed se MaxAttempts atingido).",
                        reclaimed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[StuckLeaseReaper] Falha no ciclo periódico.");
            }
        }
    }
}
