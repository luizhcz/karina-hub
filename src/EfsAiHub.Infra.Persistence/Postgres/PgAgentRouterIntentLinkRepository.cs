using System.Text.Json;
using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Agents.RouterIntents;
using Microsoft.EntityFrameworkCore;

namespace EfsAiHub.Infra.Persistence.Postgres;

public sealed class PgAgentRouterIntentLinkRepository : IAgentRouterIntentLinkRepository
{
    private readonly IDbContextFactory<AgentFwDbContext> _factory;
    private readonly ILogger<PgAgentRouterIntentLinkRepository> _logger;

    public PgAgentRouterIntentLinkRepository(
        IDbContextFactory<AgentFwDbContext> factory,
        ILogger<PgAgentRouterIntentLinkRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RouterIntent>> ListIntentsForAgentAsync(string agentId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);

        // JOIN entre junction (filtra por ProjectId do agent) e router_intents
        // (filtra por TenantId). Os dois HasQueryFilter aplicam automaticamente.
        var rows = await (
            from link in ctx.AgentRouterIntents
            where link.AgentId == agentId
            join intent in ctx.RouterIntents on link.IntentId equals intent.Id
            orderby intent.Name
            select intent
        ).ToListAsync(ct);

        return rows.Select(Hydrate).ToList();
    }

    public async Task<IReadOnlyList<string>> ListIntentIdsForAgentAsync(string agentId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        return await ctx.AgentRouterIntents
            .Where(l => l.AgentId == agentId)
            .Select(l => l.IntentId)
            .ToListAsync(ct);
    }

    public async Task<int> CountForAgentAsync(string agentId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        return await ctx.AgentRouterIntents.CountAsync(l => l.AgentId == agentId, ct);
    }

    public async Task<IReadOnlyList<RouterIntentUsage>> ListAgentsForIntentAsync(string intentId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);

        // IgnoreQueryFilters na junction porque a UI de uma intent (do tenant
        // inteiro) precisa enxergar Routers de qualquer projeto do tenant que
        // referenciam ela. Filtramos manualmente por TenantId via JOIN com
        // AgentDefinitions (que tem QueryFilter por tenant via Visibility=global,
        // mas aqui queremos ALL agents do tenant — usamos IgnoreQueryFilters
        // também e filtramos por TenantId explícito).
        var rows = await (
            from link in ctx.AgentRouterIntents.IgnoreQueryFilters()
            where link.IntentId == intentId
            join agent in ctx.AgentDefinitions.IgnoreQueryFilters()
                on link.AgentId equals agent.Id
            where agent.TenantId == link.TenantId
            select new { agent.Id, agent.Name, agent.ProjectId }
        ).ToListAsync(ct);

        return rows.Select(r => new RouterIntentUsage(r.Id, r.Name, r.ProjectId)).ToList();
    }

    public async Task SetIntentsForAgentAsync(
        string agentId,
        string projectId,
        string tenantId,
        IReadOnlyList<string> intentIds,
        CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);

        var distinctIds = intentIds.Distinct(StringComparer.Ordinal).ToList();

        // Strategy: DELETE+INSERT atomicamente. Idempotente — se o set não
        // mudou, o DELETE remove tudo e o INSERT recria os mesmos rows
        // (CreatedAt vai pra agora, mas isso é dado audit, não comportamento).
        await using var tx = await ctx.Database.BeginTransactionAsync(ct);

        var existing = await ctx.AgentRouterIntents
            .Where(l => l.AgentId == agentId)
            .ToListAsync(ct);
        if (existing.Count > 0)
        {
            ctx.AgentRouterIntents.RemoveRange(existing);
            await ctx.SaveChangesAsync(ct);
        }

        if (distinctIds.Count > 0)
        {
            var now = DateTime.UtcNow;
            foreach (var intentId in distinctIds)
            {
                ctx.AgentRouterIntents.Add(new AgentRouterIntentRow
                {
                    AgentId = agentId,
                    IntentId = intentId,
                    ProjectId = projectId,
                    TenantId = tenantId,
                    CreatedAt = now,
                });
            }
            await ctx.SaveChangesAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    private static RouterIntent Hydrate(RouterIntentRow r)
    {
        var examples = string.IsNullOrWhiteSpace(r.Examples)
            ? Array.Empty<string>()
            : JsonSerializer.Deserialize<string[]>(r.Examples, JsonDefaults.Domain) ?? Array.Empty<string>();
        return new RouterIntent
        {
            Id = r.Id,
            TenantId = r.TenantId,
            ProjectId = r.ProjectId,
            Name = r.Name,
            DisplayName = r.DisplayName,
            Description = r.Description,
            Examples = examples,
            CreatedAt = r.CreatedAt,
            UpdatedAt = r.UpdatedAt,
        };
    }
}
