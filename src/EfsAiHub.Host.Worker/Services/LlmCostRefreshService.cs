using EfsAiHub.Infra.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace EfsAiHub.Host.Worker.Services;

/// <summary>
/// Atualiza a MATERIALIZED VIEW v_llm_cost periodicamente.
/// Intervalo configurável via WorkflowEngine:LlmCostRefreshIntervalMinutes (default 30).
/// Usa CONCURRENTLY para não travar leituras durante o refresh.
/// Advisory lock garante que apenas um pod executa o refresh de cada vez.
/// </summary>
public sealed class LlmCostRefreshService : BackgroundService
{
    // Chave estável para advisory lock — evita refresh concorrente entre pods
    private const long RefreshLockKey = 0x4566_7341_6948_7562;

    private readonly IDbContextFactory<AgentFwDbContext> _dbFactory;
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<LlmCostRefreshService> _logger;
    private readonly TimeSpan _interval;

    public LlmCostRefreshService(
        IDbContextFactory<AgentFwDbContext> dbFactory,
        [FromKeyedServices("general")] NpgsqlDataSource dataSource,
        ILogger<LlmCostRefreshService> logger,
        IOptions<WorkflowEngineOptions> options)
    {
        _dbFactory = dbFactory;
        _dataSource = dataSource;
        _logger = logger;
        var minutes = options.Value.LlmCostRefreshIntervalMinutes;
        _interval = TimeSpan.FromMinutes(minutes > 0 ? minutes : 30);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RefreshIfLeaderAsync(stoppingToken);

        using var timer = new PeriodicTimer(_interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await RefreshIfLeaderAsync(stoppingToken);
    }

    private async Task RefreshIfLeaderAsync(CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        await using var lockCmd = conn.CreateCommand();
        lockCmd.CommandText = "SELECT pg_try_advisory_lock(@key)";
        lockCmd.Parameters.AddWithValue("key", RefreshLockKey);
        var acquired = (bool)(await lockCmd.ExecuteScalarAsync(ct))!;
        if (!acquired)
        {
            _logger.LogDebug("[LlmCostRefresh] Outro pod já detém o lock de refresh — pulando.");
            return;
        }

        try
        {
            await RefreshViewAsync("v_llm_cost", ct);
            await RefreshViewAsync("mv_execution_stats_hourly", ct);
            await RefreshViewAsync("mv_token_usage_hourly", ct);
        }
        finally
        {
            await using var unlockCmd = conn.CreateCommand();
            unlockCmd.CommandText = "SELECT pg_advisory_unlock(@key)";
            unlockCmd.Parameters.AddWithValue("key", RefreshLockKey);
            await unlockCmd.ExecuteScalarAsync(ct);
        }
    }

    // Lista explícita de matviews permitidas — evita injeção mesmo vindo de constante.
    private static readonly HashSet<string> AllowedViews = new()
    {
        "v_llm_cost",
        "mv_execution_stats_hourly",
        "mv_token_usage_hourly",
    };

    private async Task RefreshViewAsync(string viewName, CancellationToken ct)
    {
        if (!AllowedViews.Contains(viewName)) return;
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
#pragma warning disable EF1002
            await db.Database.ExecuteSqlRawAsync(
                $"REFRESH MATERIALIZED VIEW CONCURRENTLY {viewName}", ct);
#pragma warning restore EF1002
            _logger.LogDebug("{View} atualizada.", viewName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao atualizar MATERIALIZED VIEW {View}.", viewName);
        }
    }
}
