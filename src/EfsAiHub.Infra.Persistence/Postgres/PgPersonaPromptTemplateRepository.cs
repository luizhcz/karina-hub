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

    public async Task<PersonaPromptTemplate> UpsertAsync(
        PersonaPromptTemplate template,
        string? createdBy = null,
        string? changeReason = null,
        CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;

        // F5: upsert + append version em transação.
        //
        // Atenção: IDbContextFactory abre conexão nova do pool em cada
        // CreateDbContextAsync. Esta transação está isolada dentro deste
        // ctx. Se um colaborador no mesmo request abrir ctx paralelo via
        // factory, ele NÃO participa desta transação. Fluxo atual não
        // faz isso — preservar o padrão em qualquer operação nova.
        await using var tx = await ctx.Database.BeginTransactionAsync(ct);

        var existing = await ctx.PersonaPromptTemplates
            .FirstOrDefaultAsync(r => r.Scope == template.Scope, ct);

        PersonaPromptTemplateRow row;
        bool contentChanged;
        if (existing is not null)
        {
            contentChanged = existing.Template != template.Template;
            existing.Name = template.Name;
            existing.Template = template.Template;
            existing.UpdatedAt = now;
            existing.UpdatedBy = template.UpdatedBy;
            row = existing;
        }
        else
        {
            contentChanged = true; // create sempre gera version inicial
            row = new PersonaPromptTemplateRow
            {
                Scope = template.Scope,
                Name = template.Name,
                Template = template.Template,
                CreatedAt = template.CreatedAt == default ? now : template.CreatedAt,
                UpdatedAt = now,
                UpdatedBy = template.UpdatedBy,
            };
            ctx.PersonaPromptTemplates.Add(row);
            await ctx.SaveChangesAsync(ct); // gera Id
        }

        if (contentChanged)
        {
            var version = new PersonaPromptTemplateVersionRow
            {
                TemplateId = row.Id,
                VersionId = Guid.NewGuid(),
                Template = template.Template,
                CreatedAt = now,
                CreatedBy = createdBy,
                ChangeReason = changeReason,
            };
            ctx.PersonaPromptTemplateVersions.Add(version);
            row.ActiveVersionId = version.VersionId;
        }

        await ctx.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Map(row);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.PersonaPromptTemplates.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return false;
        // ON DELETE CASCADE remove versions automaticamente (FK na migration).
        ctx.PersonaPromptTemplates.Remove(row);
        await ctx.SaveChangesAsync(ct);
        return true;
    }

    // ── F5: versionamento ───────────────────────────────────────────────────

    public async Task<IReadOnlyList<PersonaPromptTemplateVersion>> GetVersionsAsync(
        int templateId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var rows = await ctx.PersonaPromptTemplateVersions.AsNoTracking()
            .Where(v => v.TemplateId == templateId)
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync(ct);
        return rows.Select(MapVersion).ToList();
    }

    public async Task<PersonaPromptTemplate?> RollbackAsync(
        int templateId,
        Guid targetVersionId,
        string? createdBy = null,
        CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        await using var tx = await ctx.Database.BeginTransactionAsync(ct);

        var template = await ctx.PersonaPromptTemplates
            .FirstOrDefaultAsync(r => r.Id == templateId, ct);
        if (template is null) return null;

        // Target version precisa pertencer ao mesmo template — defesa contra
        // rollback cross-template.
        var target = await ctx.PersonaPromptTemplateVersions.AsNoTracking()
            .FirstOrDefaultAsync(
                v => v.TemplateId == templateId && v.VersionId == targetVersionId, ct);
        if (target is null) return null;

        var now = DateTime.UtcNow;
        var newVersion = new PersonaPromptTemplateVersionRow
        {
            TemplateId = templateId,
            VersionId = Guid.NewGuid(),
            Template = target.Template,
            CreatedAt = now,
            CreatedBy = createdBy,
            ChangeReason = $"rollback to {targetVersionId}",
        };
        ctx.PersonaPromptTemplateVersions.Add(newVersion);

        template.Template = target.Template;
        template.UpdatedAt = now;
        template.UpdatedBy = createdBy;
        template.ActiveVersionId = newVersion.VersionId;

        await ctx.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Map(template);
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
        ActiveVersionId = row.ActiveVersionId,
    };

    private static PersonaPromptTemplateVersion MapVersion(PersonaPromptTemplateVersionRow row) => new()
    {
        Id = row.Id,
        TemplateId = row.TemplateId,
        VersionId = row.VersionId,
        Template = row.Template,
        CreatedAt = row.CreatedAt,
        CreatedBy = row.CreatedBy,
        ChangeReason = row.ChangeReason,
    };
}
