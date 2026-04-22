using System.Text.Json;
using EfsAiHub.Core.Agents.McpServers;
using Microsoft.EntityFrameworkCore;

namespace EfsAiHub.Infra.Persistence.Postgres;

/// <summary>
/// Repositório de MCP servers. Persiste o domain serializado como JSONB em <c>mcp_servers.Data</c>.
/// Project-scoped via HasQueryFilter (<c>IProjectContextAccessor</c>) no DbContext — queries
/// são automaticamente filtradas pelo projeto atual, sem precisar passar projectId no chamador.
/// </summary>
public sealed class PgMcpServerRepository : IMcpServerRepository
{
    private readonly IDbContextFactory<AgentFwDbContext> _factory;

    public PgMcpServerRepository(IDbContextFactory<AgentFwDbContext> factory)
        => _factory = factory;

    public async Task<McpServer?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        // FirstOrDefaultAsync respeita HasQueryFilter (FindAsync não aplica o filter de projeto).
        var row = await ctx.McpServers.FirstOrDefaultAsync(r => r.Id == id, ct);
        return row is null ? null : Deserialize(row.Data);
    }

    public async Task<IReadOnlyList<McpServer>> GetAllAsync(int page, int pageSize, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var rows = await ctx.McpServers
            .OrderBy(r => r.Name)
            .Skip((Math.Max(1, page) - 1) * Math.Max(1, pageSize))
            .Take(Math.Max(1, pageSize))
            .ToListAsync(ct);
        return rows.Select(r => Deserialize(r.Data)!).ToList();
    }

    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        return await ctx.McpServers.CountAsync(ct);
    }

    public async Task<McpServer> UpsertAsync(McpServer server, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        server.UpdatedAt = now;

        var existing = await ctx.McpServers.FirstOrDefaultAsync(r => r.Id == server.Id, ct);
        var data = JsonSerializer.Serialize(server, JsonDefaults.Domain);

        if (existing is null)
        {
            ctx.McpServers.Add(new McpServerRow
            {
                Id = server.Id,
                Name = server.Name,
                Data = data,
                ProjectId = server.ProjectId,
                CreatedAt = server.CreatedAt == default ? now : server.CreatedAt,
                UpdatedAt = now,
            });
        }
        else
        {
            existing.Name = server.Name;
            existing.Data = data;
            existing.UpdatedAt = now;
            // ProjectId não muda em update — mcp server é imutável em relação ao projeto
            // (mudança de projeto requer delete + recreate no novo scope).
        }

        await ctx.SaveChangesAsync(ct);
        return server;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.McpServers.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return false;
        ctx.McpServers.Remove(row);
        await ctx.SaveChangesAsync(ct);
        return true;
    }

    private static McpServer? Deserialize(string json)
        => JsonSerializer.Deserialize<McpServer>(json, JsonDefaults.Domain);
}
