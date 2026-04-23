using EfsAiHub.Core.Abstractions.Identity.Persona;
using Microsoft.EntityFrameworkCore;

namespace EfsAiHub.Infra.Persistence.Postgres;

public class PgPersonaPromptTemplateRepository : IPersonaPromptTemplateRepository
{
    private readonly IDbContextFactory<AgentFwDbContext> _factory;

    public PgPersonaPromptTemplateRepository(IDbContextFactory<AgentFwDbContext> factory)
        => _factory = factory;

    public async Task<PersonaPromptTemplate?> GetByScopeAsync(string scope, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.PersonaPromptTemplates.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Scope == scope, ct);
        return row is null ? null : Map(row);
    }

    public async Task<PersonaPromptTemplate?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.PersonaPromptTemplates.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, ct);
        return row is null ? null : Map(row);
    }

    public async Task<IReadOnlyList<PersonaPromptTemplate>> GetAllAsync(CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var rows = await ctx.PersonaPromptTemplates.AsNoTracking()
            .OrderBy(r => r.Scope == "global" ? 0 : 1) // global primeiro
            .ThenBy(r => r.Scope)
            .ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    public async Task<PersonaPromptTemplate> UpsertAsync(PersonaPromptTemplate template, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;

        var existing = await ctx.PersonaPromptTemplates
            .FirstOrDefaultAsync(r => r.Scope == template.Scope, ct);

        if (existing is not null)
        {
            existing.Name = template.Name;
            existing.Template = template.Template;
            existing.UpdatedAt = now;
            existing.UpdatedBy = template.UpdatedBy;
            await ctx.SaveChangesAsync(ct);
            return Map(existing);
        }

        var row = new PersonaPromptTemplateRow
        {
            Scope = template.Scope,
            Name = template.Name,
            Template = template.Template,
            CreatedAt = template.CreatedAt == default ? now : template.CreatedAt,
            UpdatedAt = now,
            UpdatedBy = template.UpdatedBy,
        };
        ctx.PersonaPromptTemplates.Add(row);
        await ctx.SaveChangesAsync(ct);
        return Map(row);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.PersonaPromptTemplates.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return false;
        ctx.PersonaPromptTemplates.Remove(row);
        await ctx.SaveChangesAsync(ct);
        return true;
    }

    private static PersonaPromptTemplate Map(PersonaPromptTemplateRow row) => new()
    {
        Id = row.Id,
        Scope = row.Scope,
        Name = row.Name,
        Template = row.Template,
        CreatedAt = row.CreatedAt,
        UpdatedAt = row.UpdatedAt,
        UpdatedBy = row.UpdatedBy,
    };
}
