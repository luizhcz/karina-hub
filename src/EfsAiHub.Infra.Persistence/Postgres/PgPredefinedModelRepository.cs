using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Agents.PredefinedModels;
using Microsoft.EntityFrameworkCore;

namespace EfsAiHub.Infra.Persistence.Postgres;

public sealed class PgPredefinedModelRepository : IPredefinedModelRepository
{
    private readonly IDbContextFactory<AgentFwDbContext> _factory;
    private readonly ILogger<PgPredefinedModelRepository> _logger;

    public PgPredefinedModelRepository(
        IDbContextFactory<AgentFwDbContext> factory,
        ILogger<PgPredefinedModelRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<PredefinedModel?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.PredefinedModels.FirstOrDefaultAsync(r => r.Id == id, ct);
        return row is null ? null : Hydrate(row);
    }

    public async Task<IReadOnlyList<PredefinedModel>> ListAsync(
        bool includeDisabled = false,
        CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var query = ctx.PredefinedModels.AsQueryable();
        if (!includeDisabled) query = query.Where(r => r.Enabled);
        var rows = await query
            .OrderBy(r => r.DisplayName)
            .ToListAsync(ct);
        return rows.Select(Hydrate).ToList();
    }

    public async Task<PredefinedModel> CreateAsync(PredefinedModel model, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        var row = new PredefinedModelRow
        {
            Id = model.Id,
            DisplayName = model.DisplayName,
            Description = model.Description ?? string.Empty,
            Provider = model.Provider,
            ClientType = model.ClientType,
            Endpoint = model.Endpoint,
            DeploymentName = model.DeploymentName,
            DefaultTemperature = model.DefaultTemperature,
            DefaultMaxTokens = model.DefaultMaxTokens,
            Enabled = model.Enabled,
            CreatedAt = now,
            UpdatedAt = now,
        };
        ctx.PredefinedModels.Add(row);
        await ctx.SaveChangesAsync(ct);
        model.UpdatedAt = now;
        return model;
    }

    public async Task<PredefinedModel> UpdateAsync(
        PredefinedModel model,
        DateTime expectedUpdatedAt,
        CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;

        var current = await ctx.PredefinedModels
            .FirstOrDefaultAsync(r => r.Id == model.Id && r.UpdatedAt == expectedUpdatedAt, ct);

        if (current is null)
        {
            var stillExists = await ctx.PredefinedModels.AnyAsync(r => r.Id == model.Id, ct);
            if (stillExists)
                throw new PredefinedModelConcurrencyException(model.Id);
            throw new KeyNotFoundException($"PredefinedModel '{model.Id}' não encontrado.");
        }

        current.DisplayName = model.DisplayName;
        current.Description = model.Description ?? string.Empty;
        current.Provider = model.Provider;
        current.ClientType = model.ClientType;
        current.Endpoint = model.Endpoint;
        current.DeploymentName = model.DeploymentName;
        current.DefaultTemperature = model.DefaultTemperature;
        current.DefaultMaxTokens = model.DefaultMaxTokens;
        current.Enabled = model.Enabled;
        current.UpdatedAt = now;

        await ctx.SaveChangesAsync(ct);
        model.UpdatedAt = now;
        return model;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var rows = await ctx.PredefinedModels
            .Where(r => r.Id == id)
            .ExecuteDeleteAsync(ct);
        return rows > 0;
    }

    private static PredefinedModel Hydrate(PredefinedModelRow row) => new()
    {
        Id = row.Id,
        DisplayName = row.DisplayName,
        Description = row.Description,
        Provider = row.Provider,
        ClientType = row.ClientType,
        Endpoint = row.Endpoint,
        DeploymentName = row.DeploymentName,
        DefaultTemperature = row.DefaultTemperature,
        DefaultMaxTokens = row.DefaultMaxTokens,
        Enabled = row.Enabled,
        CreatedAt = row.CreatedAt,
        UpdatedAt = row.UpdatedAt,
    };
}
