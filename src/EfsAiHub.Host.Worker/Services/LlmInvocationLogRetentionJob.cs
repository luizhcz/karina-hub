using EfsAiHub.Core.Abstractions.BackgroundServices;
using EfsAiHub.Core.Agents.Capture;

namespace EfsAiHub.Host.Worker.Services;

/// <summary>
/// Job de retenção da tabela particionada <c>aihub.llm_invocation_log</c>.
/// Roda diário às 03:00 UTC e:
///   (a) dropa partições mensais cujo limite superior está abaixo do cutoff
///       (default 30d) via DETACH + DROP TABLE — muito mais barato que
///       DELETE em massa numa tabela com payloads grandes;
///   (b) garante que a partição "próximo mês" + "mês após" existam, pra
///       inserts não falharem na virada do mês.
///
/// Idempotente: se nenhuma partição está fora da janela, faz no-op.
/// </summary>
public sealed class LlmInvocationLogRetentionJob : BackgroundService
{
    private const string HeartbeatName = "LlmInvocationLogRetention";
    private const int RetentionDays = 30;

    /// <summary>Hora UTC pra rodar o sweep (3h da manhã — baixo tráfego).</summary>
    private static readonly TimeSpan RunAt = TimeSpan.FromHours(3);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IBackgroundServiceHeartbeatSink _heartbeat;
    private readonly ILogger<LlmInvocationLogRetentionJob> _logger;

    public LlmInvocationLogRetentionJob(
        IServiceScopeFactory scopeFactory,
        IBackgroundServiceHeartbeatSink heartbeat,
        ILogger<LlmInvocationLogRetentionJob> logger)
    {
        _scopeFactory = scopeFactory;
        _heartbeat = heartbeat;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _heartbeat.Started(HeartbeatName, DateTimeOffset.UtcNow);
        _logger.LogInformation(
            "[LlmInvocationLogRetention] Started — retention={Days}d, runs daily at {Time:hh\\:mm} UTC.",
            RetentionDays, RunAt);

        // Roda uma vez no startup pra cobrir o caso de a app ter ficado
        // semanas offline; depois entra no loop diário.
        await RunSweepSafe(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var nextRun = ComputeNextRun(DateTime.UtcNow);
            var delay = nextRun - DateTime.UtcNow;
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (TaskCanceledException) { break; }

            await RunSweepSafe(stoppingToken);
        }

        _logger.LogInformation("[LlmInvocationLogRetention] Stopped.");
    }

    private async Task RunSweepSafe(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<ILlmInvocationLogRepository>();

            var dropped = await repo.DropOldPartitionsAsync(RetentionDays, ct);
            if (dropped.Count > 0)
                _logger.LogInformation(
                    "[LlmInvocationLogRetention] Dropadas {Count} partições antigas: {Names}",
                    dropped.Count, string.Join(", ", dropped));
            else
                _logger.LogDebug("[LlmInvocationLogRetention] Nenhuma partição fora da janela.");

            // Pré-cria partições futuras pra cobrir a virada do mês mesmo quando
            // o sweep roda no dia 31 (jobs noturnos passam virando dia).
            await repo.EnsureFuturePartitionAsync(1, ct);
            await repo.EnsureFuturePartitionAsync(2, ct);

            _heartbeat.RecordSuccess(HeartbeatName, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _heartbeat.RecordError(HeartbeatName, DateTimeOffset.UtcNow, ex);
            _logger.LogWarning(ex, "[LlmInvocationLogRetention] Falha no sweep — tentará novamente amanhã.");
        }
    }

    private static DateTime ComputeNextRun(DateTime nowUtc)
    {
        var today = new DateTime(nowUtc.Year, nowUtc.Month, nowUtc.Day, RunAt.Hours, RunAt.Minutes, 0, DateTimeKind.Utc);
        return nowUtc < today ? today : today.AddDays(1);
    }
}
