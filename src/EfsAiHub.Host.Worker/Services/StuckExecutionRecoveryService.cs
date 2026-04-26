using Npgsql;

namespace EfsAiHub.Host.Worker.Services;

public sealed class StuckExecutionRecoveryService : BackgroundService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<StuckExecutionRecoveryService> _logger;
    private readonly WorkflowEngineOptions _options;

    public StuckExecutionRecoveryService(
        [FromKeyedServices("general")] NpgsqlDataSource dataSource,
        ILogger<StuckExecutionRecoveryService> logger,
        IOptions<WorkflowEngineOptions> options)
    {
        _dataSource = dataSource;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalSeconds = _options.StuckExecutionRecoveryIntervalSeconds;
        if (intervalSeconds <= 0)
        {
            _logger.LogInformation(
                "[StuckExecutionRecovery] Desabilitado (StuckExecutionRecoveryIntervalSeconds=0).");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));
        _logger.LogInformation(
            "[StuckExecutionRecovery] Ativo — varredura a cada {Interval}s, timeout={Timeout}min.",
            intervalSeconds, _options.StuckExecutionTimeoutMinutes);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RecoverStuckAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[StuckExecutionRecovery] Falha no ciclo periódico.");
            }
        }
    }

    internal async Task RecoverStuckAsync(CancellationToken ct)
    {
        const string sql = """
            UPDATE aihub.workflow_executions
            SET "Status" = 'Failed',
                "ErrorCategory" = 'Timeout',
                "ErrorMessage" = 'Execução interrompida sem progresso há mais que o limite configurado.',
                "CompletedAt" = NOW()
            WHERE "Status" = 'Running'
              AND "StartedAt" < NOW() - make_interval(mins => @minutes);
            """;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("minutes", _options.StuckExecutionTimeoutMinutes);

        var affected = await cmd.ExecuteNonQueryAsync(ct);
        if (affected > 0)
        {
            MetricsRegistry.StuckExecutionsRecovered.Add(affected);
            _logger.LogWarning(
                "[StuckExecutionRecovery] {Count} execução(ões) Running >{Minutes}min marcadas como Failed.",
                affected, _options.StuckExecutionTimeoutMinutes);
        }
    }
}
