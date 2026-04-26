namespace EfsAiHub.Tests.Integration.Controllers;

/// <summary>
/// Valida o round-trip de enums (BlocklistAction, BlocklistPatternType) entre:
///   1. Cliente HTTP (JSON com casing variado: lowercase, PascalCase, UPPERCASE)
///   2. JsonStringEnumConverter (default no JsonDefaults.Domain) — parser case-insensitive
///   3. ProjectSettings.Blocklist (JSONB no banco — escrito via PgProjectRepository)
///   4. Repository read + JsonSerializer.Deserialize com JsonDefaults.Domain
///   5. Catálogo curado: Npgsql.Enum.Parse(ignoreCase: true) lê valores lowercase do banco
///
/// PR 10.c — TL pediu validação após refutação na review da PR 3.
/// </summary>
[Collection("Integration")]
[Trait("Category", "Integration")]
public class BlocklistEnumRoundTripTests(IntegrationWebApplicationFactory factory)
{
    // appsettings.json popula Admin:AccountIds com '123456789' — test factory herda
    // a config porque appsettings.Test.json não sobrescreve. WithAdminAccount injeta
    // o header pra passar pelo AdminGate.
    private readonly HttpClient _client = factory.CreateClient().WithAdminAccount("123456789");

    /// <summary>
    /// Cria projeto via SQL direto (bypass do AdminGate de POST /api/projects).
    /// Isola o teste de issues pre-existentes no setup de auth dos testes integration.
    /// </summary>
    private async Task<string> CreateProjectAsync()
    {
        var id = $"p-{Guid.NewGuid():N}";
        await using var conn = new Npgsql.NpgsqlConnection(factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand("""
            INSERT INTO aihub.projects (id, name, tenant_id, settings, created_at, updated_at)
            VALUES (@id, @name, 'default', '{}', NOW(), NOW());
            """, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("name", $"Project {id}");
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    [Theory]
    [InlineData("Block")]
    [InlineData("block")]
    [InlineData("BLOCK")]
    public async Task PutBlocklist_ActionOverrideCasingVariado_Aceita(string casing)
    {
        // Cliente envia JSON com casing arbitrário. JsonStringEnumConverter (default no
        // JsonDefaults.Domain via .NET 5+) é case-insensitive na desserialização.
        var id = await CreateProjectAsync();
        var body = new
        {
            enabled = true,
            scanInput = true,
            scanOutput = true,
            replacement = "[REDACTED]",
            auditBlocks = true,
            groups = new Dictionary<string, object>
            {
                ["PII"] = new { enabled = true, actionOverride = casing }
            }
        };

        var resp = await _client.PutAsJsonAsync($"/api/projects/{id}/blocklist", body);

        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            $"casing '{casing}' deve ser aceito pelo JsonStringEnumConverter case-insensitive");
    }

    [Fact]
    public async Task PutBlocklist_LowercaseAction_GetRetornaPascalCaseCanonical()
    {
        // Round-trip: write lowercase → ler de volta → resposta normaliza pra PascalCase
        // (output do JsonStringEnumConverter sem naming policy preserva o nome do enum).
        var id = await CreateProjectAsync();
        await _client.PutAsJsonAsync($"/api/projects/{id}/blocklist", new
        {
            enabled = true,
            scanInput = true,
            scanOutput = true,
            replacement = "[REDACTED]",
            auditBlocks = true,
            groups = new Dictionary<string, object>
            {
                ["PII"] = new { enabled = true, actionOverride = "redact" } // lowercase
            }
        });

        var getResp = await _client.GetAsync($"/api/projects/{id}/blocklist");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await getResp.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("settings")
            .GetProperty("groups")
            .GetProperty("PII")
            .GetProperty("actionOverride")
            .GetString()
            .Should().Be("Redact", "round-trip normaliza pra PascalCase canonical");
    }

    [Theory]
    [InlineData("literal")]
    [InlineData("Literal")]
    [InlineData("regex")]
    [InlineData("Regex")]
    [InlineData("builtin")]
    [InlineData("BuiltIn")]
    public async Task PutBlocklist_CustomPatternTypeCasingVariado_Aceita(string typeCasing)
    {
        var id = await CreateProjectAsync();
        var body = new
        {
            enabled = true,
            scanInput = true,
            scanOutput = true,
            replacement = "[REDACTED]",
            auditBlocks = true,
            customPatterns = new[]
            {
                new
                {
                    id = $"custom.{Guid.NewGuid():N}",
                    type = typeCasing,
                    pattern = "test",
                    action = "Block",
                    wholeWord = true,
                    caseSensitive = false
                }
            }
        };

        var resp = await _client.PutAsJsonAsync($"/api/projects/{id}/blocklist", body);

        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            $"BlocklistPatternType deve aceitar casing '{typeCasing}'");
    }

    [Fact]
    public async Task PutBlocklist_CustomPattern_RoundTripPreservaSemantica()
    {
        // Write em casing variado → ler de volta → cada campo enum normalizado.
        var id = await CreateProjectAsync();
        var pattId = $"custom.{Guid.NewGuid():N}";
        await _client.PutAsJsonAsync($"/api/projects/{id}/blocklist", new
        {
            enabled = true,
            scanInput = true,
            scanOutput = true,
            replacement = "[REDACTED]",
            auditBlocks = true,
            customPatterns = new[]
            {
                new
                {
                    id = pattId,
                    type = "regex",        // lowercase
                    pattern = @"\d{4}",
                    action = "WARN",       // uppercase
                    wholeWord = true,
                    caseSensitive = false
                }
            }
        });

        var getResp = await _client.GetAsync($"/api/projects/{id}/blocklist");
        var body = await getResp.Content.ReadFromJsonAsync<JsonElement>();

        var customs = body.GetProperty("settings").GetProperty("customPatterns");
        customs.GetArrayLength().Should().Be(1);
        var first = customs[0];
        first.GetProperty("type").GetString().Should().Be("Regex");
        first.GetProperty("action").GetString().Should().Be("Warn");
        first.GetProperty("id").GetString().Should().Be(pattId);
    }

    [Fact]
    public async Task GetCatalog_ValoresLowercaseDoBanco_SaiemPascalCase()
    {
        // Catálogo curado é seedado em lowercase no banco (literal, regex, builtin, mod11, luhn,
        // block). PgBlocklistCatalogRepository usa Enum.Parse(ignoreCase: true) → API serializa
        // como PascalCase via JsonStringEnumConverter.
        var resp = await _client.GetAsync("/api/admin/blocklist/catalog");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var patterns = body.GetProperty("patterns");

        if (patterns.GetArrayLength() == 0)
        {
            // Catálogo vazio (depende do seeds.sql ter rodado). Não falha — apenas valida
            // que o repo não explode ao carregar.
            return;
        }

        // Cada pattern deve ter Type, DefaultAction, Validator em PascalCase.
        foreach (var p in patterns.EnumerateArray())
        {
            var type = p.GetProperty("type").GetString();
            new[] { "Literal", "Regex", "BuiltIn" }.Should().Contain(type!);

            var action = p.GetProperty("defaultAction").GetString();
            new[] { "Block", "Redact", "Warn" }.Should().Contain(action!);

            var validator = p.GetProperty("validator").GetString();
            new[] { "None", "Mod11", "Luhn" }.Should().Contain(validator!);
        }
    }
}
