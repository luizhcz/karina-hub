using EfsAiHub.Core.Abstractions.Secrets;
using EfsAiHub.Infra.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;

namespace EfsAiHub.Host.Api.Identity.Secrets;

/// <summary>
/// Coleta refs <c>secret://aws/...</c> a partir de <c>agent_definitions.Data->Provider->ApiKey</c>
/// via raw SQL com filtro por prefixo no índice GIN/jsonb — evita deserializar
/// cada AgentDefinition em memória.
/// </summary>
public sealed class AgentSecretReferenceSource : ISecretReferenceSource
{
    private readonly IDbContextFactory<AgentFwDbContext> _ctxFactory;
    private readonly ILogger<AgentSecretReferenceSource> _logger;

    public AgentSecretReferenceSource(
        IDbContextFactory<AgentFwDbContext> ctxFactory,
        ILogger<AgentSecretReferenceSource> logger)
    {
        _ctxFactory = ctxFactory;
        _logger = logger;
    }

    public string Name => "agent_definitions";

    public async Task<IReadOnlyCollection<string>> CollectAsync(CancellationToken ct)
    {
        var refs = new List<string>();
        try
        {
            await using var ctx = await _ctxFactory.CreateDbContextAsync(ct);
            var conn = ctx.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync(ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT DISTINCT "Data"::jsonb #>> '{Provider,ApiKey}' AS api_key
                  FROM aihub.agent_definitions
                 WHERE "Data"::jsonb #>> '{Provider,ApiKey}' LIKE 'secret://aws/%';
                """;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (!reader.IsDBNull(0))
                    refs.Add(reader.GetString(0));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[AgentSecretReferenceSource] Falha consultando agent_definitions — refs per-agent não pré-carregadas.");
            return Array.Empty<string>();
        }
        return refs;
    }
}
