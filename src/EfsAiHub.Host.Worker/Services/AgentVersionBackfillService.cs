using System.Text.Json;
using EfsAiHub.Core.Abstractions.Persistence;
using EfsAiHub.Core.Agents;
using EfsAiHub.Infra.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EfsAiHub.Host.Worker.Services;

/// <summary>
/// Garante invariante "todo agent_definitions tem ≥1 agent_versions" no
/// startup. Necessário porque <c>db/seeds.sql</c> insere agents direto na
/// tabela base (jsonb opaco) sem passar pelo fluxo de aprovação que normalmente
/// gera a revision inicial — qualquer caller que pina versão (sandbox,
/// predict-intent, exactAgentPin no AgentFactory) falha sem isso.
///
/// <para>
/// Self-healing por design: roda 1x no startup, idempotente (AppendAsync
/// usa ContentHash pra deduplicar). Edita pelo path direto Db + repo,
/// bypassando o owner gate de <c>AgentService.PublishVersionAsync</c>
/// (backfill é system-wide, sem ProjectContext HTTP).
/// </para>
/// </summary>
public sealed class AgentVersionBackfillService(
    IServiceScopeFactory scopeFactory,
    ILogger<AgentVersionBackfillService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            await BackfillAsync(ct);
        }
        catch (Exception ex)
        {
            // Backfill é best-effort no startup — falha não trava o boot.
            // Próximo restart tenta de novo (idempotente).
            logger.LogWarning(ex, "[VersionBackfill] Falha no backfill — continuando inicialização.");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private async Task BackfillAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AgentFwDbContext>>();
        var versionRepo = scope.ServiceProvider.GetRequiredService<IAgentVersionRepository>();

        var orphans = await LoadOrphansAsync(dbFactory, ct);
        if (orphans.Count == 0)
        {
            logger.LogDebug("[VersionBackfill] Nenhum agente órfão — skip.");
            return;
        }

        logger.LogInformation(
            "[VersionBackfill] {Count} agentes sem agent_versions — publicando revision inicial.",
            orphans.Count);

        int published = 0;
        int failed = 0;
        foreach (var (id, dataJson) in orphans)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var definition = JsonSerializer.Deserialize<AgentDefinition>(dataJson, JsonDefaults.Domain);
                if (definition is null)
                {
                    logger.LogWarning(
                        "[VersionBackfill] Agente '{AgentId}' tem Data jsonb inválido — pulando.", id);
                    failed++;
                    continue;
                }

                var snapshot = AgentVersion.FromDefinition(
                    definition,
                    revision: 1,
                    promptContent: definition.Instructions,
                    promptVersionId: null,
                    createdBy: "system:agent-version-backfill",
                    changeReason: "Initial publish via backfill (seed import).",
                    breakingChange: false);

                await versionRepo.AppendAsync(snapshot, ct);
                published++;
            }
            catch (Exception ex)
            {
                failed++;
                logger.LogWarning(ex,
                    "[VersionBackfill] Falha ao publicar version inicial pra '{AgentId}' — próximo restart tenta de novo.",
                    id);
            }
        }

        logger.LogInformation(
            "[VersionBackfill] Concluído: {Published} versions publicadas, {Failed} falhas.",
            published, failed);
    }

    /// <summary>
    /// Busca pares (Id, Data) de agents sem nenhuma row em agent_versions.
    /// Raw SQL bypassa o HasQueryFilter por project/tenant — backfill é
    /// system-wide, não respeita escopo do caller (não há caller HTTP).
    /// </summary>
    private static async Task<List<(string Id, string Data)>> LoadOrphansAsync(
        IDbContextFactory<AgentFwDbContext> dbFactory,
        CancellationToken ct)
    {
        await using var ctx = await dbFactory.CreateDbContextAsync(ct);
        var conn = ctx.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);

        var results = new List<(string Id, string Data)>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT d."Id", d."Data"
            FROM aihub.agent_definitions d
            WHERE NOT EXISTS (
                SELECT 1 FROM aihub.agent_versions v
                WHERE v."AgentDefinitionId" = d."Id"
            )
            """;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add((reader.GetString(0), reader.GetString(1)));
        }
        return results;
    }
}
