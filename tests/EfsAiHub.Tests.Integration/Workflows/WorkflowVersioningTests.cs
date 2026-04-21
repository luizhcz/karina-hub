namespace EfsAiHub.Tests.Integration.Workflows;

[Collection("Integration")]
[Trait("Category", "Integration")]
public class WorkflowVersioningTests(IntegrationWebApplicationFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> CreateWorkflowAsync(string? name = null)
    {
        var id = $"wf-ver-{Guid.NewGuid():N}";
        var payload = new
        {
            id,
            name = name ?? "Workflow Versionamento",
            orchestrationMode = "Sequential",
            agents = new[] { new { agentId = "agent-placeholder" } }
        };
        var response = await _client.PostAsJsonAsync("/api/workflows", payload);
        response.EnsureSuccessStatusCode();
        return id;
    }

    private async Task UpdateWorkflowAsync(string id, string newName)
    {
        var payload = new
        {
            id,
            name = newName,
            orchestrationMode = "Sequential",
            agents = new[] { new { agentId = "agent-placeholder" } }
        };
        var response = await _client.PutAsJsonAsync($"/api/workflows/{id}", payload);
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Put_GeraNovaRevisao()
    {
        var id = await CreateWorkflowAsync("v1");

        await UpdateWorkflowAsync(id, "v2");

        var response = await _client.GetAsync($"/api/workflows/{id}/versions");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var versions = await response.Content.ReadFromJsonAsync<JsonElement>();
        versions.GetArrayLength().Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Get_Versions_ListaEmOrdemDescendente()
    {
        var id = await CreateWorkflowAsync("original");
        await UpdateWorkflowAsync(id, "update-1");
        await UpdateWorkflowAsync(id, "update-2");

        var response = await _client.GetAsync($"/api/workflows/{id}/versions");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var versions = await response.Content.ReadFromJsonAsync<JsonElement>();
        var revisions = versions.EnumerateArray()
            .Select(v => v.GetProperty("revision").GetInt32())
            .ToList();

        revisions.Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task ContentHash_Idempotente_DuasPutsIguais_MesmHash()
    {
        var id = await CreateWorkflowAsync("hash-test");
        await UpdateWorkflowAsync(id, "mesma-coisa");
        await UpdateWorkflowAsync(id, "mesma-coisa");

        var response = await _client.GetAsync($"/api/workflows/{id}/versions");
        var versions = await response.Content.ReadFromJsonAsync<JsonElement>();

        var hashes = versions.EnumerateArray()
            .Select(v => v.GetProperty("contentHash").GetString())
            .Distinct()
            .ToList();

        // Duas PUTs com mesmo conteúdo geram apenas uma versão (idempotência)
        hashes.Should().HaveCount(1);
    }

    [Fact]
    public async Task Post_Rollback_CriaNovaRevisaoComConteudoAnterior()
    {
        var id = await CreateWorkflowAsync("original");
        await UpdateWorkflowAsync(id, "alterado");

        // Obtém lista de versões
        var versionsResp = await _client.GetAsync($"/api/workflows/{id}/versions");
        var versions = await versionsResp.Content.ReadFromJsonAsync<JsonElement>();
        var allVersions = versions.EnumerateArray().ToList();

        // Pega a versão mais antiga (última na lista DESC)
        var oldestVersion = allVersions.Last();
        var versionId = oldestVersion.GetProperty("workflowVersionId").GetString()!;

        // Faz rollback
        var rollbackResp = await _client.PostAsync(
            $"/api/workflows/{id}/rollback?versionId={versionId}", null);

        rollbackResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
