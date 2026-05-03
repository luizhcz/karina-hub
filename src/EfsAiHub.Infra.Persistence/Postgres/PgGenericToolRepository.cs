using System.Text.Json;
using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Agents.GenericTools;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EfsAiHub.Infra.Persistence.Postgres;

public sealed class PgGenericToolRepository : IGenericToolRepository
{
    private readonly IDbContextFactory<AgentFwDbContext> _factory;
    private readonly ILogger<PgGenericToolRepository> _logger;

    public PgGenericToolRepository(
        IDbContextFactory<AgentFwDbContext> factory,
        ILogger<PgGenericToolRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<GenericTool?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.GenericTools.FirstOrDefaultAsync(r => r.Id == id, ct);
        return row is null ? null : Hydrate(row);
    }

    public async Task<GenericTool?> GetByIdAsync(string id, string projectId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        // IgnoreQueryFilters bypassa o HasQueryFilter por CurrentProjectId — o caller
        // já passou o projectId explícito (vem do agent definition no binder).
        var row = await ctx.GenericTools
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == id && r.ProjectId == projectId, ct);
        return row is null ? null : Hydrate(row);
    }

    public async Task<IReadOnlyList<GenericTool>> ListAsync(CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var rows = await ctx.GenericTools
            .OrderByDescending(r => r.UpdatedAt)
            .ToListAsync(ct);
        return rows.Select(Hydrate).ToList();
    }

    public async Task<bool> NameExistsAsync(string name, string? excludeId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var query = ctx.GenericTools.Where(r => r.Name == name);
        if (!string.IsNullOrEmpty(excludeId))
            query = query.Where(r => r.Id != excludeId);
        return await query.AnyAsync(ct);
    }

    public async Task<GenericTool> CreateAsync(GenericTool tool, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        var row = new GenericToolRow
        {
            Id = tool.Id,
            ProjectId = tool.ProjectId,
            TenantId = tool.TenantId,
            Name = tool.Name,
            Description = tool.Description ?? string.Empty,
            HttpMethod = tool.HttpMethod.ToString(),
            UrlTemplate = tool.UrlTemplate,
            PathParams = JsonSerializer.Serialize(tool.PathParams, JsonDefaults.Domain),
            QueryParams = JsonSerializer.Serialize(tool.QueryParams, JsonDefaults.Domain),
            CustomHeaders = JsonSerializer.Serialize(tool.CustomHeaders, JsonDefaults.Domain),
            InputContentType = tool.InputContentType.ToString(),
            InputSchema = tool.InputSchema,
            OutputContentType = tool.OutputContentType.ToString(),
            OutputSchema = tool.OutputSchema,
            TimeoutSecondsOverride = tool.TimeoutSecondsOverride,
            WhenToUse = tool.WhenToUse,
            CreatedAt = now,
            UpdatedAt = now,
        };
        ctx.GenericTools.Add(row);

        try
        {
            await ctx.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsNameConflict(ex))
        {
            throw new GenericToolNameConflictException(tool.Name);
        }

        tool.UpdatedAt = now;
        return tool;
    }

    public async Task<GenericTool> UpdateAsync(
        GenericTool tool,
        DateTime expectedUpdatedAt,
        CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;

        var current = await ctx.GenericTools
            .FirstOrDefaultAsync(r => r.Id == tool.Id && r.UpdatedAt == expectedUpdatedAt, ct);

        if (current is null)
        {
            var stillExists = await ctx.GenericTools.AnyAsync(r => r.Id == tool.Id, ct);
            if (stillExists)
                throw new GenericToolConcurrencyException(tool.Id);
            throw new KeyNotFoundException($"GenericTool '{tool.Id}' não encontrado.");
        }

        current.Name = tool.Name;
        current.Description = tool.Description ?? string.Empty;
        current.HttpMethod = tool.HttpMethod.ToString();
        current.UrlTemplate = tool.UrlTemplate;
        current.PathParams = JsonSerializer.Serialize(tool.PathParams, JsonDefaults.Domain);
        current.QueryParams = JsonSerializer.Serialize(tool.QueryParams, JsonDefaults.Domain);
        current.CustomHeaders = JsonSerializer.Serialize(tool.CustomHeaders, JsonDefaults.Domain);
        current.InputContentType = tool.InputContentType.ToString();
        current.InputSchema = tool.InputSchema;
        current.OutputContentType = tool.OutputContentType.ToString();
        current.OutputSchema = tool.OutputSchema;
        current.TimeoutSecondsOverride = tool.TimeoutSecondsOverride;
        current.WhenToUse = tool.WhenToUse;
        current.UpdatedAt = now;

        try
        {
            await ctx.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsNameConflict(ex))
        {
            throw new GenericToolNameConflictException(tool.Name);
        }

        tool.UpdatedAt = now;
        return tool;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var rows = await ctx.GenericTools
            .Where(r => r.Id == id)
            .ExecuteDeleteAsync(ct);
        return rows > 0;
    }

    private GenericTool Hydrate(GenericToolRow row)
    {
        return new GenericTool
        {
            Id = row.Id,
            ProjectId = row.ProjectId,
            TenantId = row.TenantId,
            Name = row.Name,
            Description = row.Description,
            HttpMethod = ParseEnum(row.HttpMethod, HttpMethodType.GET, nameof(row.HttpMethod), row.Id),
            UrlTemplate = row.UrlTemplate,
            PathParams = DeserializeParams(row.PathParams),
            QueryParams = DeserializeParams(row.QueryParams),
            CustomHeaders = DeserializeHeaders(row.CustomHeaders),
            InputContentType = ParseEnum(row.InputContentType, InputContentType.None, nameof(row.InputContentType), row.Id),
            InputSchema = row.InputSchema,
            OutputContentType = ParseEnum(row.OutputContentType, OutputContentType.Json, nameof(row.OutputContentType), row.Id),
            OutputSchema = row.OutputSchema,
            TimeoutSecondsOverride = row.TimeoutSecondsOverride,
            WhenToUse = row.WhenToUse,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt,
        };
    }

    private static IReadOnlyDictionary<string, ParamDefinition> DeserializeParams(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new Dictionary<string, ParamDefinition>();
        return JsonSerializer.Deserialize<Dictionary<string, ParamDefinition>>(raw, JsonDefaults.Domain)
            ?? new Dictionary<string, ParamDefinition>();
    }

    private static IReadOnlyDictionary<string, string> DeserializeHeaders(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new Dictionary<string, string>();
        return JsonSerializer.Deserialize<Dictionary<string, string>>(raw, JsonDefaults.Domain)
            ?? new Dictionary<string, string>();
    }

    private T ParseEnum<T>(string raw, T fallback, string column, string toolId) where T : struct, Enum
    {
        if (Enum.TryParse<T>(raw, out var v)) return v;
        _logger.LogWarning(
            "[PgGenericToolRepository] Valor desconhecido '{Raw}' em '{Column}' do tool '{ToolId}'. Usando fallback '{Fallback}'.",
            raw, column, toolId, fallback);
        return fallback;
    }

    private static bool IsNameConflict(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: "23505" } pg
        && string.Equals(pg.ConstraintName, "UX_generic_tools_ProjectId_Name", StringComparison.Ordinal);
}
