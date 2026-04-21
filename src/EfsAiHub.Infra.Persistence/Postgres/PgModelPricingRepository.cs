using Microsoft.EntityFrameworkCore;

namespace EfsAiHub.Infra.Persistence.Postgres;

public class PgModelPricingRepository : IModelPricingRepository
{
    private readonly IDbContextFactory<AgentFwDbContext> _factory;

    public PgModelPricingRepository(IDbContextFactory<AgentFwDbContext> factory)
        => _factory = factory;

    public async Task<ModelPricing?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.ModelPricings.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, ct);
        return row is null ? null : MapToDomain(row);
    }

    public async Task<IReadOnlyList<ModelPricing>> GetAllAsync(CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var rows = await ctx.ModelPricings.AsNoTracking()
            .OrderBy(r => r.ModelId).ThenByDescending(r => r.EffectiveFrom)
            .ToListAsync(ct);
        return rows.Select(MapToDomain).ToList();
    }

    public async Task<IReadOnlyList<ModelPricing>> GetAllAsync(int page, int pageSize, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var rows = await ctx.ModelPricings.AsNoTracking()
            .OrderBy(r => r.ModelId).ThenByDescending(r => r.EffectiveFrom)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return rows.Select(MapToDomain).ToList();
    }

    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        return await ctx.ModelPricings.CountAsync(ct);
    }

    public async Task<ModelPricing> UpsertAsync(ModelPricing pricing, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);

        if (pricing.Id > 0)
        {
            var existing = await ctx.ModelPricings.FirstOrDefaultAsync(r => r.Id == pricing.Id, ct);
            if (existing is not null)
            {
                existing.ModelId = pricing.ModelId;
                existing.Provider = pricing.Provider;
                existing.PricePerInputToken = pricing.PricePerInputToken;
                existing.PricePerOutputToken = pricing.PricePerOutputToken;
                existing.Currency = pricing.Currency;
                existing.EffectiveFrom = pricing.EffectiveFrom;
                existing.EffectiveTo = pricing.EffectiveTo;
                await ctx.SaveChangesAsync(ct);
                return MapToDomain(existing);
            }
        }

        var row = new ModelPricingRow
        {
            ModelId = pricing.ModelId,
            Provider = pricing.Provider,
            PricePerInputToken = pricing.PricePerInputToken,
            PricePerOutputToken = pricing.PricePerOutputToken,
            Currency = pricing.Currency,
            EffectiveFrom = pricing.EffectiveFrom,
            EffectiveTo = pricing.EffectiveTo,
            CreatedAt = pricing.CreatedAt
        };
        ctx.ModelPricings.Add(row);
        await ctx.SaveChangesAsync(ct);
        return MapToDomain(row);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.ModelPricings.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return false;
        ctx.ModelPricings.Remove(row);
        await ctx.SaveChangesAsync(ct);
        return true;
    }

    public async Task RefreshMaterializedViewAsync(CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        await ctx.Database.ExecuteSqlRawAsync("REFRESH MATERIALIZED VIEW CONCURRENTLY v_llm_cost", ct);
    }

    private static ModelPricing MapToDomain(ModelPricingRow row) => new()
    {
        Id = row.Id,
        ModelId = row.ModelId,
        Provider = row.Provider,
        PricePerInputToken = row.PricePerInputToken,
        PricePerOutputToken = row.PricePerOutputToken,
        Currency = row.Currency,
        EffectiveFrom = row.EffectiveFrom,
        EffectiveTo = row.EffectiveTo,
        CreatedAt = row.CreatedAt
    };
}
