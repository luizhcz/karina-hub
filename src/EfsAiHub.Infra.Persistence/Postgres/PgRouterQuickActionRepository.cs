using EfsAiHub.Core.Abstractions.RouterQuickActions;
using Microsoft.EntityFrameworkCore;

namespace EfsAiHub.Infra.Persistence.Postgres;

public sealed class PgRouterQuickActionRepository : IRouterQuickActionRepository
{
    private readonly IDbContextFactory<AgentFwDbContext> _factory;

    public PgRouterQuickActionRepository(IDbContextFactory<AgentFwDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<RouterQuickAction> CreateAsync(RouterQuickAction action, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.RouterQuickActions.Add(action);
        await db.SaveChangesAsync(ct);
        return action;
    }

    public async Task<RouterQuickAction?> GetByIdAsync(string id, string tenantId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // IgnoreQueryFilters: GET por Id pra UI admin não pode falhar por filtro de ProjectId
        // (admin pode estar olhando atalho de outro projeto do mesmo tenant). Tenant ainda filtra.
        return await db.RouterQuickActions
            .IgnoreQueryFilters()
            .Where(e => e.TenantId == tenantId)
            .FirstOrDefaultAsync(e => e.Id == id, ct);
    }

    public async Task<IReadOnlyList<RouterQuickAction>> ListByRouterAsync(
        string routerId, string tenantId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // IgnoreQueryFilters porque o matcher precisa listar atalhos do projeto corrente
        // E o caller já passou o tenantId — filtro explícito aqui.
        // A consulta por projeto continua via HasQueryFilter quando chamado pelo Controller
        // (com ProjectId scope ativo).
        return await db.RouterQuickActions
            .AsNoTracking()
            .Where(e => e.RouterId == routerId && e.TenantId == tenantId)
            .ToListAsync(ct);
    }

    public async Task<RouterQuickAction> UpdateAsync(RouterQuickAction action, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        action.UpdatedAt = DateTime.UtcNow;
        db.RouterQuickActions.Update(action);
        await db.SaveChangesAsync(ct);
        return action;
    }

    public async Task<bool> DeleteAsync(string id, string tenantId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.RouterQuickActions
            .IgnoreQueryFilters()
            .Where(e => e.Id == id && e.TenantId == tenantId)
            .ExecuteDeleteAsync(ct);
        return rows > 0;
    }
}
