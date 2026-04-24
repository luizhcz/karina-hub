using EfsAiHub.Infra.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;

namespace EfsAiHub.Host.Worker.Services;

/// <summary>
/// Limpa periodicamente as linhas expiradas das tabelas agent_sessions e workflow_event_audit.
/// Implementa TTL para entidades que antes usavam expiração nativa do Redis.
/// </summary>
public class AgentSessionCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<AgentSessionCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private static readonly TimeSpan EventAuditRetention = TimeSpan.FromDays(7);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("[SessionCleanup] Serviço iniciado. Intervalo: {Interval}.", Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AgentFwDbContext>>();
                await using var ctx = await factory.CreateDbContextAsync(stoppingToken);

                var now = DateTime.UtcNow;
                var cutoff = now.Subtract(EventAuditRetention);

                // Batched DELETE em agent_sessions: lotes de 500 com pausa entre batches
                // para ceder I/O ao Chat Path.
                int totalSessions = 0, batch;
                do
                {
                    batch = await ctx.Database.ExecuteSqlRawAsync(
                        """
                        DELETE FROM agent_sessions
                        WHERE ctid IN (
                            SELECT ctid FROM agent_sessions
                            WHERE "ExpiresAt" <= {0}
                            LIMIT 500
                        )
                        """,
                        parameters: new object[] { now },
                        cancellationToken: stoppingToken);
                    totalSessions += batch;
                    if (batch > 0) await Task.Delay(200, stoppingToken);
                }
                while (batch == 500 && !stoppingToken.IsCancellationRequested);

                // Batched DELETE em workflow_event_audit.
                int totalEvents = 0;
                do
                {
                    batch = await ctx.Database.ExecuteSqlRawAsync(
                        """
                        DELETE FROM workflow_event_audit
                        WHERE ctid IN (
                            SELECT ctid FROM workflow_event_audit
                            WHERE "Timestamp" <= {0}
                            LIMIT 500
                        )
                        """,
                        parameters: new object[] { cutoff },
                        cancellationToken: stoppingToken);
                    totalEvents += batch;
                    if (batch > 0) await Task.Delay(200, stoppingToken);
                }
                while (batch == 500 && !stoppingToken.IsCancellationRequested);

                // Batched DELETE em document_extraction_cache.
                int totalCache = 0;
                do
                {
                    batch = await ctx.Database.ExecuteSqlRawAsync(
                        """
                        DELETE FROM aihub.document_extraction_cache
                        WHERE ctid IN (
                            SELECT ctid FROM aihub.document_extraction_cache
                            WHERE expires_at <= {0}
                            LIMIT 500
                        )
                        """,
                        parameters: new object[] { now },
                        cancellationToken: stoppingToken);
                    totalCache += batch;
                    if (batch > 0) await Task.Delay(200, stoppingToken);
                }
                while (batch == 500 && !stoppingToken.IsCancellationRequested);

                logger.LogInformation(
                    "[SessionCleanup] Limpeza concluída: {Sessions} sessões expiradas, {Events} eventos antigos, {Cache} cache docs expirados removidos.",
                    totalSessions, totalEvents, totalCache);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[SessionCleanup] Erro durante limpeza. Próxima tentativa em {Interval}.", Interval);
            }

            await Task.Delay(Interval, stoppingToken);
        }

        logger.LogInformation("[SessionCleanup] Serviço encerrado.");
    }
}
