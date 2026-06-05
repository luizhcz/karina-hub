using System.Text.Json;
using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Agents.RouterIntents;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EfsAiHub.Infra.Persistence.Postgres;

public sealed class PgRouterIntentRepository : IRouterIntentRepository
{
    private readonly IDbContextFactory<AgentFwDbContext> _factory;
    private readonly ILogger<PgRouterIntentRepository> _logger;

    public PgRouterIntentRepository(
        IDbContextFactory<AgentFwDbContext> factory,
        ILogger<PgRouterIntentRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<RouterIntent?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.RouterIntents.FirstOrDefaultAsync(r => r.Id == id, ct);
        return row is null ? null : Hydrate(row);
    }

    public async Task<IReadOnlyList<RouterIntent>> GetByIdsAsync(IReadOnlyList<string> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return Array.Empty<RouterIntent>();

        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var rows = await ctx.RouterIntents
            .Where(r => ids.Contains(r.Id))
            .ToListAsync(ct);

        // Preserva a ordem declarada pelo caller — o renderer respeita essa ordem
        // ao listar intents no system prompt.
        var byId = rows.ToDictionary(r => r.Id, StringComparer.Ordinal);
        var ordered = new List<RouterIntent>(ids.Count);
        foreach (var id in ids)
        {
            if (byId.TryGetValue(id, out var row))
                ordered.Add(Hydrate(row));
        }
        return ordered;
    }

    public async Task<IReadOnlyList<RouterIntent>> ListAsync(CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var rows = await ctx.RouterIntents
            .OrderByDescending(r => r.UpdatedAt)
            .ToListAsync(ct);
        return rows.Select(Hydrate).ToList();
    }

    public async Task<bool> NameExistsAsync(string name, string? excludeId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var query = ctx.RouterIntents.Where(r => r.Name == name);
        if (!string.IsNullOrEmpty(excludeId))
            query = query.Where(r => r.Id != excludeId);
        return await query.AnyAsync(ct);
    }

    public async Task<RouterIntent> CreateAsync(RouterIntent intent, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        var row = new RouterIntentRow
        {
            Id = intent.Id,
            TenantId = intent.TenantId,
            ProjectId = intent.ProjectId,
            Name = intent.Name,
            DisplayName = intent.DisplayName,
            Description = intent.Description,
            Examples = JsonSerializer.Serialize(intent.Examples, JsonDefaults.Domain),
            CreatedAt = now,
            UpdatedAt = now,
        };
        ctx.RouterIntents.Add(row);

        try
        {
            await ctx.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsNameConflict(ex))
        {
            throw new RouterIntentNameConflictException(intent.Name);
        }

        return Hydrate(row);
    }

    public async Task<RouterIntent> UpdateAsync(RouterIntent intent, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.RouterIntents.FirstOrDefaultAsync(r => r.Id == intent.Id, ct)
            ?? throw new KeyNotFoundException($"RouterIntent '{intent.Id}' não encontrada.");

        row.ProjectId = intent.ProjectId;
        row.Name = intent.Name;
        row.DisplayName = intent.DisplayName;
        row.Description = intent.Description;
        row.Examples = JsonSerializer.Serialize(intent.Examples, JsonDefaults.Domain);
        row.UpdatedAt = DateTime.UtcNow;

        try
        {
            await ctx.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsNameConflict(ex))
        {
            throw new RouterIntentNameConflictException(intent.Name);
        }

        return Hydrate(row);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.RouterIntents.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return false;
        ctx.RouterIntents.Remove(row);
        try
        {
            await ctx.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsForeignKeyConflict(ex))
        {
            // FK RESTRICT em agent_router_intents.IntentId — algum Router referencia.
            // Service expõe via GetUsageAsync pra mensagem amigável.
            throw new RouterIntentInUseException(id, Array.Empty<string>());
        }
        return true;
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
            IsSystem = r.IsSystem,
            CreatedAt = r.CreatedAt,
            UpdatedAt = r.UpdatedAt,
        };
    }

    private static bool IsNameConflict(DbUpdateException ex)
        => ex.InnerException is PostgresException { SqlState: "23505" } pe
           && pe.ConstraintName == "UX_router_intents_TenantId_Name";

    private static bool IsForeignKeyConflict(DbUpdateException ex)
        => ex.InnerException is PostgresException { SqlState: "23503" };
}
