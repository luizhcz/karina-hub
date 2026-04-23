using Microsoft.EntityFrameworkCore;

namespace EfsAiHub.Infra.Persistence.Postgres;

public class PgDocumentIntelligencePricingRepository : IDocumentIntelligencePricingRepository
{
    private readonly IDbContextFactory<AgentFwDbContext> _factory;

    public PgDocumentIntelligencePricingRepository(IDbContextFactory<AgentFwDbContext> factory)
        => _factory = factory;

    public async Task<DocumentIntelligencePricing?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.DocumentIntelligencePricings.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, ct);
        return row is null ? null : MapToDomain(row);
    }

    public async Task<DocumentIntelligencePricing?> GetCurrentAsync(
        string modelId, string provider, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        var row = await ctx.DocumentIntelligencePricings.AsNoTracking()
            .Where(r => r.ModelId == modelId && r.Provider == provider)
            .Where(r => r.EffectiveFrom <= now && (r.EffectiveTo == null || r.EffectiveTo > now))
            .OrderByDescending(r => r.EffectiveFrom)
            .FirstOrDefaultAsync(ct);
        return row is null ? null : MapToDomain(row);
    }

    public async Task<IReadOnlyList<DocumentIntelligencePricing>> GetAllAsync(CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var rows = await ctx.DocumentIntelligencePricings.AsNoTracking()
            .OrderBy(r => r.Provider).ThenBy(r => r.ModelId).ThenByDescending(r => r.EffectiveFrom)
            .ToListAsync(ct);
        return rows.Select(MapToDomain).ToList();
    }

    public async Task<IReadOnlyList<DocumentIntelligencePricing>> GetAllAsync(
        int page, int pageSize, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var rows = await ctx.DocumentIntelligencePricings.AsNoTracking()
            .OrderBy(r => r.Provider).ThenBy(r => r.ModelId).ThenByDescending(r => r.EffectiveFrom)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return rows.Select(MapToDomain).ToList();
    }

    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        return await ctx.DocumentIntelligencePricings.CountAsync(ct);
    }

    public async Task<DocumentIntelligencePricing> UpsertAsync(
        DocumentIntelligencePricing pricing, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);

        if (pricing.Id > 0)
        {
            var existing = await ctx.DocumentIntelligencePricings
                .FirstOrDefaultAsync(r => r.Id == pricing.Id, ct);
            if (existing is not null)
            {
                existing.ModelId = pricing.ModelId;
                existing.Provider = pricing.Provider;
                existing.PricePerPage = pricing.PricePerPage;
                existing.Currency = pricing.Currency;
                existing.EffectiveFrom = pricing.EffectiveFrom;
                existing.EffectiveTo = pricing.EffectiveTo;
                await ctx.SaveChangesAsync(ct);
                return MapToDomain(existing);
            }
        }

        var row = new DocumentIntelligencePricingRow
        {
            ModelId = pricing.ModelId,
            Provider = pricing.Provider,
            PricePerPage = pricing.PricePerPage,
            Currency = pricing.Currency,
            EffectiveFrom = pricing.EffectiveFrom,
            EffectiveTo = pricing.EffectiveTo,
            CreatedAt = pricing.CreatedAt == default ? DateTime.UtcNow : pricing.CreatedAt,
        };
        ctx.DocumentIntelligencePricings.Add(row);
        await ctx.SaveChangesAsync(ct);
        return MapToDomain(row);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.DocumentIntelligencePricings.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return false;
        ctx.DocumentIntelligencePricings.Remove(row);
        await ctx.SaveChangesAsync(ct);
        return true;
    }

    private static DocumentIntelligencePricing MapToDomain(DocumentIntelligencePricingRow row) => new()
    {
        Id = row.Id,
        ModelId = row.ModelId,
        Provider = row.Provider,
        PricePerPage = row.PricePerPage,
        Currency = row.Currency,
        EffectiveFrom = row.EffectiveFrom,
        EffectiveTo = row.EffectiveTo,
        CreatedAt = row.CreatedAt,
    };
}
