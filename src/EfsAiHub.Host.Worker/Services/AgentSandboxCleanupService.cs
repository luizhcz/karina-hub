using EfsAiHub.Core.Abstractions.AgentSandbox;
using EfsAiHub.Infra.Persistence.Postgres;
using EfsAiHub.Platform.Runtime.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Host.Worker.Services;

/// <summary>
/// Limpa periodicamente Agent Sandbox sessions expiradas: deleta o workflow
/// efêmero (cascade derruba workflow_executions, chat_messages, etc.) e marca
/// a session como <c>Status=Expired</c>. Sessions com <c>Status=Validated</c>
/// NUNCA expiram — preservam audit trail do que foi aprovado pra produção.
///
/// Critério de seleção em <see cref="IAgentSandboxSessionRepository.ListExpiredAsync"/>:
/// <c>ExpiresAt &lt; now AND Status IN ('Active','Closed')</c>.
/// </summary>
public sealed class AgentSandboxCleanupService(
    IServiceScopeFactory scopeFactory,
    IOptions<AgentSandboxOptions> options,
    ILogger<AgentSandboxCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalSeconds = options.Value.CleanupIntervalSeconds;
        if (intervalSeconds <= 0)
        {
            logger.LogInformation(
                "[AgentSandboxCleanup] Polling desabilitado (CleanupIntervalSeconds={Interval}).",
                intervalSeconds);
            return;
        }

        // Primeiro ciclo: imediato (pega lixo acumulado em deploys anteriores)
        await CleanupAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));
        logger.LogInformation(
            "[AgentSandboxCleanup] Polling ativo a cada {Interval}s, batch={BatchSize}.",
            intervalSeconds, options.Value.CleanupBatchSize);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await CleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[AgentSandboxCleanup] Erro no ciclo periódico de cleanup.");
            }
        }
    }

    private async Task CleanupAsync(CancellationToken ct)
    {
        var batchSize = options.Value.CleanupBatchSize > 0 ? options.Value.CleanupBatchSize : 100;

        await using var scope = scopeFactory.CreateAsyncScope();
        var sandboxRepo = scope.ServiceProvider.GetRequiredService<IAgentSandboxSessionRepository>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AgentFwDbContext>>();

        var expired = await sandboxRepo.ListExpiredAsync(batchSize, ct);
        if (expired.Count == 0) return;

        logger.LogInformation(
            "[AgentSandboxCleanup] {Count} sessions expiradas — iniciando limpeza.",
            expired.Count);

        int workflowsDeleted = 0;
        int sessionsMarked = 0;
        foreach (var session in expired)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                // Background service não tem ProjectContext setado — o
                // IWorkflowDefinitionRepository.DeleteAsync respeita HasQueryFilter
                // por project/tenant e bloqueia o delete. Acesso direto via
                // DbContext com IgnoreQueryFilters é o caminho correto pra
                // jobs system-wide (mesma estratégia do AgentSessionCleanupService).
                // FKs cascateiam workflow_executions/chat_messages.
                await using (var ctx = await dbFactory.CreateDbContextAsync(ct))
                {
                    var rowsAffected = await ctx.Database.ExecuteSqlRawAsync(
                        "DELETE FROM aihub.workflow_definitions WHERE \"Id\" = {0}",
                        parameters: new object[] { session.WorkflowId },
                        cancellationToken: ct);
                    if (rowsAffected > 0) workflowsDeleted++;
                }

                await sandboxRepo.MarkExpiredAsync(session.SandboxSessionId, ct);
                sessionsMarked++;
            }
            catch (Exception ex)
            {
                // Cleanup é best-effort por session — falha de uma não derruba o batch.
                logger.LogWarning(ex,
                    "[AgentSandboxCleanup] Falha ao limpar session '{SessionId}' (workflow='{WorkflowId}'). Próximo ciclo tenta de novo.",
                    session.SandboxSessionId, session.WorkflowId);
            }
        }

        logger.LogInformation(
            "[AgentSandboxCleanup] Ciclo concluído: {Workflows} workflows deletados, {Sessions} sessions marcadas como Expired.",
            workflowsDeleted, sessionsMarked);
    }
}
