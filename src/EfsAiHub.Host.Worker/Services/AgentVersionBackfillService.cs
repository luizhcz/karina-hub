using System.Text.Json;
using EfsAiHub.Core.Abstractions.Persistence;
using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Agents.Services;
using EfsAiHub.Infra.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EfsAiHub.Host.Worker.Services;

/// <summary>
/// Backfill obrigatório no startup: garante que todo <c>agent_definitions</c>
/// tenha sido processado pelo <see cref="IAgentDefinitionComposer"/> e tenha
/// um <c>agent_versions</c> row vigente. Necessário pra agents seedados via
/// <c>db/seeds.sql</c> (inserção direta sem passar pelo flow de approval) e
/// pra migrar agents legados pré-composer.
///
/// <para>
/// Self-healing: roda 1x no startup, idempotente. <c>AppendAsync</c> usa
/// ContentHash pra deduplicar — agents já compostos não geram nova revision.
/// Falha em <c>StartAsync</c> propaga e bloqueia inicialização — refs quebradas
/// detectadas cedo, antes de servir tráfego.
/// </para>
/// </summary>
public sealed class AgentVersionBackfillService(
    IServiceScopeFactory scopeFactory,
    ILogger<AgentVersionBackfillService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        await BackfillAsync(ct);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private async Task BackfillAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var dbFactory = sp.GetRequiredService<IDbContextFactory<AgentFwDbContext>>();
        var agentRepo = sp.GetRequiredService<IAgentDefinitionRepository>();
        var composer = sp.GetRequiredService<IAgentDefinitionComposer>();
        var decomposer = sp.GetRequiredService<IAgentDefinitionDecomposer>();

        var rows = await LoadAllAgentsAsync(dbFactory, ct);
        if (rows.Count == 0)
        {
            logger.LogDebug("[VersionBackfill] Nenhum agente cadastrado — skip.");
            return;
        }

        // TemplateService re-aplica os fragmentos auto-injetados por tipo
        // (StructuredOutputState middleware do Conversational, wrap canônico do
        // StructuredOutput, etc.). Sem essa etapa, o decompose tira esses
        // fragmentos e o compose não os reinjeta — backfill grava o agente
        // mutilado de volta. AgentService.CreateAsync já faz template→compose
        // nessa ordem; aqui replicamos a simetria.
        var templateService = sp.GetRequiredService<IAgentTemplateService>();

        logger.LogInformation(
            "[VersionBackfill] {Count} agente(s) — recompondo pra garantir snapshot autocontido.",
            rows.Count);

        int processed = 0;
        var failures = new List<(string AgentId, Exception Error)>();
        foreach (var (id, dataJson) in rows)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var stored = JsonSerializer.Deserialize<AgentDefinition>(dataJson, JsonDefaults.Domain)
                    ?? throw new InvalidOperationException($"Agente '{id}' tem Data jsonb inválido.");

                // Recompose round-trip: decompose → template.Apply → compose →
                // upsert. UpsertAsync grava nova revision apenas se ContentHash
                // mudou — idempotente pra agents já compostos. Composer falha
                // hard se dep faltando (intent/tool/model removido); falha
                // propaga + lista no final.
                var authored = decomposer.Decompose(stored);
                var templated = templateService.Apply(authored);
                var composed = await composer.ComposeAsync(templated, ct);

                await agentRepo.UpsertAsync(
                    composed,
                    ct,
                    breakingChange: false,
                    changeReason: "Startup backfill: ensure self-contained snapshot.",
                    createdBy: "system:agent-version-backfill",
                    isCosmeticOnly: true);

                processed++;
            }
            catch (Exception ex)
            {
                failures.Add((id, ex));
                logger.LogError(ex,
                    "[VersionBackfill] Falha ao recompor agente '{AgentId}'.", id);
            }
        }

        if (failures.Count > 0)
        {
            var summary = string.Join(", ", failures.Select(f => f.AgentId));
            throw new InvalidOperationException(
                $"Agent version backfill falhou em {failures.Count} agente(s): {summary}. " +
                "Resolva as dependências quebradas (intent/tool/model/skill ausente) e reinicie.");
        }

        logger.LogInformation(
            "[VersionBackfill] Concluído: {Processed} agente(s) processado(s) sem falhas.", processed);
    }

    /// <summary>
    /// Carrega <c>(Id, Data)</c> de todos os agentes via raw SQL — bypass
    /// query filter por project/tenant (backfill roda system-wide, sem caller
    /// HTTP). Ordem por Id estabiliza retries (mesmo conjunto a cada run).
    /// </summary>
    private static async Task<List<(string Id, string Data)>> LoadAllAgentsAsync(
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
            SELECT "Id", "Data"
            FROM aihub.agent_definitions
            ORDER BY "Id"
            """;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add((reader.GetString(0), reader.GetString(1)));
        return results;
    }
}
