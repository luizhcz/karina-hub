namespace EfsAiHub.Tests.Integration.Agents;

[Collection("Integration")]
[Trait("Category", "Integration")]
public class AgentVersioningTests(IntegrationWebApplicationFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> CreateAgentAsync(string? instructions = null)
    {
        var id = $"agent-ver-{Guid.NewGuid():N}";
        var payload = new
        {
            id,
            name = "Agent Versionamento",
            model = new { deploymentName = "gpt-4o" },
            instructions = instructions ?? "Instrução inicial."
        };
        var response = await _client.PostAsJsonAsync("/api/agents", payload);
        response.EnsureSuccessStatusCode();
        return id;
    }

    private async Task UpdateAgentAsync(string id, string newInstructions)
    {
        var payload = new
        {
            id,
            name = "Agent Versionamento",
            model = new { deploymentName = "gpt-4o" },
            instructions = newInstructions
        };
        var response = await _client.PutAsJsonAsync($"/api/agents/{id}", payload);
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Put_GeraNovaRevisao()
    {
        var id = await CreateAgentAsync("instrução v1");

        await UpdateAgentAsync(id, "instrução v2");

        var response = await _client.GetAsync($"/api/agents/{id}/versions");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var versions = await response.Content.ReadFromJsonAsync<JsonElement>();
        versions.GetArrayLength().Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Get_Versions_ListaEmOrdemDescendente()
    {
        var id = await CreateAgentAsync("instrução original");
        await UpdateAgentAsync(id, "instrução update-1");
        await UpdateAgentAsync(id, "instrução update-2");

        var response = await _client.GetAsync($"/api/agents/{id}/versions");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var versions = await response.Content.ReadFromJsonAsync<JsonElement>();
        var revisions = versions.EnumerateArray()
            .Select(v => v.GetProperty("revision").GetInt32())
            .ToList();

        revisions.Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task Get_VersionById_RetornaSnapshotCompleto()
    {
        var id = await CreateAgentAsync("instrução snapshot");
        await UpdateAgentAsync(id, "instrução snapshot v2");

        var listResp = await _client.GetAsync($"/api/agents/{id}/versions");
        var versions = await listResp.Content.ReadFromJsonAsync<JsonElement>();
        var firstVersion = versions.EnumerateArray().First();
        var versionId = firstVersion.GetProperty("agentVersionId").GetString()!;

        var response = await _client.GetAsync($"/api/agents/{id}/versions/{versionId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var snapshot = await response.Content.ReadFromJsonAsync<JsonElement>();
        snapshot.GetProperty("agentVersionId").GetString().Should().Be(versionId);
        snapshot.TryGetProperty("contentHash", out _).Should().BeTrue();
    }

    [Fact]
    public async Task ContentHash_MesmoConteudo_MesmoHash()
    {
        var id = await CreateAgentAsync("idempotente");
        await UpdateAgentAsync(id, "mesma instrução");
        await UpdateAgentAsync(id, "mesma instrução");

        var response = await _client.GetAsync($"/api/agents/{id}/versions");
        var versions = await response.Content.ReadFromJsonAsync<JsonElement>();

        var hashes = versions.EnumerateArray()
            .Select(v => v.GetProperty("contentHash").GetString())
            .Distinct()
            .ToList();

        hashes.Should().HaveCount(1);
    }

    [Fact]
    public async Task Post_Rollback_Retorna200()
    {
        var id = await CreateAgentAsync("original");
        await UpdateAgentAsync(id, "alterado");

        var versionsResp = await _client.GetAsync($"/api/agents/{id}/versions");
        var versions = await versionsResp.Content.ReadFromJsonAsync<JsonElement>();
        var oldest = versions.EnumerateArray().Last();
        var versionId = oldest.GetProperty("agentVersionId").GetString()!;

        var rollbackResp = await _client.PostAsync(
            $"/api/agents/{id}/rollback?versionId={versionId}", null);

        rollbackResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
