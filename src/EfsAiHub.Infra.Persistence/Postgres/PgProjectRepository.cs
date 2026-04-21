using System.Text.Json;
using System.Text.Json.Serialization;
using EfsAiHub.Core.Abstractions.Projects;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;

namespace EfsAiHub.Infra.Persistence.Postgres;

public sealed class PgProjectRepository : IProjectRepository
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IDataProtector _protector;

    // DTO interno para o JSONB — armazena ciphertext + metadados de rotação.
    private sealed record CredentialJson(
        [property: JsonPropertyName("apiKeyCipher")] string? ApiKeyCipher,
        [property: JsonPropertyName("keyVersion")]   string? KeyVersion,
        [property: JsonPropertyName("endpoint")]     string? Endpoint);

    private sealed record LlmConfigJson(
        [property: JsonPropertyName("credentials")]    Dictionary<string, CredentialJson>? Credentials,
        [property: JsonPropertyName("defaultModel")]   string? DefaultModel,
        [property: JsonPropertyName("defaultProvider")] string? DefaultProvider);

    public PgProjectRepository(NpgsqlDataSource dataSource, IDataProtectionProvider dpProvider)
    {
        _dataSource = dataSource;
        _protector  = dpProvider.CreateProtector("ProjectLlmCredentials");
    }

    public async Task<Project?> GetByIdAsync(string projectId, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, tenant_id, description, settings, llm_config, budget, created_at, updated_at
            FROM projects WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("id", projectId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapProject(reader) : null;
    }

    public async Task<IReadOnlyList<Project>> GetByTenantAsync(string tenantId, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, tenant_id, description, settings, llm_config, budget, created_at, updated_at
            FROM projects WHERE tenant_id = @tenantId ORDER BY name
            """;
        cmd.Parameters.AddWithValue("tenantId", tenantId);

        var results = new List<Project>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add(MapProject(reader));
        return results;
    }

    public async Task<IReadOnlyList<Project>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, tenant_id, description, settings, llm_config, budget, created_at, updated_at
            FROM projects ORDER BY name
            """;

        var results = new List<Project>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add(MapProject(reader));
        return results;
    }

    public async Task CreateAsync(Project project, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO projects (id, name, tenant_id, description, settings, llm_config, budget, created_at, updated_at)
            VALUES (@id, @name, @tenantId, @description, @settings::jsonb, @llmConfig::jsonb, @budget::jsonb, @createdAt, @updatedAt)
            """;
        cmd.Parameters.AddWithValue("id", project.Id);
        cmd.Parameters.AddWithValue("name", project.Name);
        cmd.Parameters.AddWithValue("tenantId", project.TenantId);
        cmd.Parameters.AddWithValue("description", (object?)project.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("settings", JsonSerializer.Serialize(project.Settings));
        cmd.Parameters.AddWithValue("llmConfig", (object?)SerializeLlmConfig(project.LlmConfig) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("budget",
            (object?)project.Budget?.RootElement.GetRawText() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("createdAt", project.CreatedAt);
        cmd.Parameters.AddWithValue("updatedAt", project.UpdatedAt);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpdateAsync(Project project, CancellationToken ct = default)
    {
        project.UpdatedAt = DateTime.UtcNow;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE projects SET name = @name, description = @description,
                settings = @settings::jsonb, llm_config = @llmConfig::jsonb,
                budget = @budget::jsonb, updated_at = @updatedAt
            WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("id", project.Id);
        cmd.Parameters.AddWithValue("name", project.Name);
        cmd.Parameters.AddWithValue("description", (object?)project.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("settings", JsonSerializer.Serialize(project.Settings));
        cmd.Parameters.AddWithValue("llmConfig", (object?)SerializeLlmConfig(project.LlmConfig) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("budget",
            (object?)project.Budget?.RootElement.GetRawText() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("updatedAt", project.UpdatedAt);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(string projectId, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM projects WHERE id = @id";
        cmd.Parameters.AddWithValue("id", projectId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Mapeamento ────────────────────────────────────────────────────────────

    private Project MapProject(NpgsqlDataReader reader)
    {
        var settingsJson = reader.GetString(4);
        var settings = JsonSerializer.Deserialize<ProjectSettings>(settingsJson) ?? new ProjectSettings();

        return new Project
        {
            Id          = reader.GetString(0),
            Name        = reader.GetString(1),
            TenantId    = reader.GetString(2),
            Description = reader.IsDBNull(3) ? null : reader.GetString(3),
            Settings    = settings.Migrate(),
            LlmConfig   = reader.IsDBNull(5) ? null : DeserializeLlmConfig(reader.GetString(5)),
            Budget      = reader.IsDBNull(6) ? null : JsonDocument.Parse(reader.GetString(6)),
            CreatedAt   = reader.GetDateTime(7),
            UpdatedAt   = reader.GetDateTime(8)
        };
    }

    // ── Cifragem / Decifragem ─────────────────────────────────────────────────

    /// <summary>
    /// Serializa ProjectLlmConfig para JSON, cifrando cada ApiKey com IDataProtector.
    /// Grava apiKeyCipher + keyVersion (timestamp ISO-8601) no JSONB.
    /// </summary>
    private string? SerializeLlmConfig(ProjectLlmConfig? config)
    {
        if (config is null) return null;

        var jsonCreds = new Dictionary<string, CredentialJson>();
        foreach (var (provider, cred) in config.Credentials)
        {
            string? cipher = null;
            string? keyVersion = null;
            if (!string.IsNullOrEmpty(cred.ApiKey))
            {
                cipher     = _protector.Protect(cred.ApiKey);
                keyVersion = DateTime.UtcNow.ToString("O");
            }
            jsonCreds[provider] = new CredentialJson(cipher, keyVersion, cred.Endpoint);
        }

        var jsonConfig = new LlmConfigJson(jsonCreds, config.DefaultModel, config.DefaultProvider);
        return JsonSerializer.Serialize(jsonConfig);
    }

    /// <summary>
    /// Desserializa JSONB do banco e decifra cada ApiKey antes de retornar ao domínio.
    /// Se a decifragem falhar (key ring rotacionada), ApiKey fica null — não quebra o startup.
    /// </summary>
    private ProjectLlmConfig? DeserializeLlmConfig(string json)
    {
        var jsonConfig = JsonSerializer.Deserialize<LlmConfigJson>(json);
        if (jsonConfig is null) return null;

        var creds = new Dictionary<string, ProviderCredentials>();
        foreach (var (provider, jsonCred) in jsonConfig.Credentials ?? [])
        {
            string? plainKey = null;
            if (!string.IsNullOrEmpty(jsonCred.ApiKeyCipher))
            {
                try { plainKey = _protector.Unprotect(jsonCred.ApiKeyCipher); }
                catch { /* key ring rotacionada — ApiKey irrecuperável, retorna null */ }
            }

            creds[provider] = new ProviderCredentials
            {
                ApiKey     = plainKey,
                Endpoint   = jsonCred.Endpoint,
                KeyVersion = jsonCred.KeyVersion
            };
        }

        return new ProjectLlmConfig
        {
            Credentials     = creds,
            DefaultModel    = jsonConfig.DefaultModel,
            DefaultProvider = jsonConfig.DefaultProvider
        };
    }
}
