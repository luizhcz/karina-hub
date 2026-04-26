using System.Text.Json;
using EfsAiHub.Core.Abstractions.Projects;
using Npgsql;

namespace EfsAiHub.Infra.Persistence.Postgres;

public sealed class PgModelCatalogRepository : IModelCatalogRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public PgModelCatalogRepository(NpgsqlDataSource dataSource)
        => _dataSource = dataSource;

    public async Task<IReadOnlyList<ModelCatalog>> GetAllAsync(
        string? provider = null, bool activeOnly = true, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();

        var where = new List<string>();
        if (provider is not null) where.Add("provider = @provider");
        if (activeOnly) where.Add("is_active = true");
        var whereClause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

        cmd.CommandText = $"""
            SELECT id, provider, display_name, description, context_window, capabilities, is_active, created_at, updated_at
            FROM model_catalog {whereClause} ORDER BY provider, id
            """;
        if (provider is not null)
            cmd.Parameters.AddWithValue("provider", provider.ToUpperInvariant());

        var results = new List<ModelCatalog>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add(Map(reader));
        return results;
    }

    public async Task<IReadOnlyList<ModelCatalog>> GetAllAsync(
        string? provider, bool activeOnly, int page, int pageSize, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();

        var where = new List<string>();
        if (provider is not null) where.Add("provider = @provider");
        if (activeOnly) where.Add("is_active = true");
        var whereClause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

        cmd.CommandText = $"""
            SELECT id, provider, display_name, description, context_window, capabilities, is_active, created_at, updated_at
            FROM model_catalog {whereClause} ORDER BY provider, id
            LIMIT @pageSize OFFSET @offset
            """;
        if (provider is not null)
            cmd.Parameters.AddWithValue("provider", provider.ToUpperInvariant());
        cmd.Parameters.AddWithValue("pageSize", pageSize);
        cmd.Parameters.AddWithValue("offset", (page - 1) * pageSize);

        var results = new List<ModelCatalog>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add(Map(reader));
        return results;
    }

    public async Task<int> CountAsync(
        string? provider = null, bool activeOnly = true, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();

        var where = new List<string>();
        if (provider is not null) where.Add("provider = @provider");
        if (activeOnly) where.Add("is_active = true");
        var whereClause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

        cmd.CommandText = $"SELECT COUNT(*) FROM model_catalog {whereClause}";
        if (provider is not null)
            cmd.Parameters.AddWithValue("provider", provider.ToUpperInvariant());

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task<ModelCatalog?> GetByIdAsync(string id, string provider, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, provider, display_name, description, context_window, capabilities, is_active, created_at, updated_at
            FROM model_catalog WHERE id = @id AND provider = @provider
            """;
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("provider", provider.ToUpperInvariant());

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<ModelCatalog> UpsertAsync(ModelCatalog model, CancellationToken ct = default)
    {
        model.UpdatedAt = DateTime.UtcNow;
        var capJson = JsonSerializer.Serialize(model.Capabilities, JsonDefaults.Domain);

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO model_catalog (id, provider, display_name, description, context_window, capabilities, is_active, created_at, updated_at)
            VALUES (@id, @provider, @displayName, @description, @contextWindow, @capabilities::jsonb, @isActive, @createdAt, @updatedAt)
            ON CONFLICT (id, provider) DO UPDATE SET
                display_name   = EXCLUDED.display_name,
                description    = EXCLUDED.description,
                context_window = EXCLUDED.context_window,
                capabilities   = EXCLUDED.capabilities,
                is_active      = EXCLUDED.is_active,
                updated_at     = EXCLUDED.updated_at
            RETURNING id, provider, display_name, description, context_window, capabilities, is_active, created_at, updated_at
            """;
        cmd.Parameters.AddWithValue("id", model.Id);
        cmd.Parameters.AddWithValue("provider", model.Provider.ToUpperInvariant());
        cmd.Parameters.AddWithValue("displayName", model.DisplayName);
        cmd.Parameters.AddWithValue("description", (object?)model.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("contextWindow", (object?)model.ContextWindow ?? DBNull.Value);
        cmd.Parameters.AddWithValue("capabilities", capJson);
        cmd.Parameters.AddWithValue("isActive", model.IsActive);
        cmd.Parameters.AddWithValue("createdAt", model.CreatedAt);
        cmd.Parameters.AddWithValue("updatedAt", model.UpdatedAt);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return Map(reader);
    }

    public async Task<bool> SetActiveAsync(string id, string provider, bool isActive, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE model_catalog SET is_active = @isActive, updated_at = @now
            WHERE id = @id AND provider = @provider
            """;
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("provider", provider.ToUpperInvariant());
        cmd.Parameters.AddWithValue("isActive", isActive);
        cmd.Parameters.AddWithValue("now", DateTime.UtcNow);

        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    private static ModelCatalog Map(NpgsqlDataReader r) => new()
    {
        Id           = r.GetString(0),
        Provider     = r.GetString(1),
        DisplayName  = r.GetString(2),
        Description  = r.IsDBNull(3) ? null : r.GetString(3),
        ContextWindow = r.IsDBNull(4) ? null : r.GetInt32(4),
        Capabilities = JsonSerializer.Deserialize<List<string>>(r.GetString(5), JsonDefaults.Domain) ?? [],
        IsActive     = r.GetBoolean(6),
        CreatedAt    = r.GetDateTime(7),
        UpdatedAt    = r.GetDateTime(8)
    };
}
