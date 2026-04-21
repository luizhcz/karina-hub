namespace EfsAiHub.Tests.Integration.Controllers;

[Collection("Integration")]
[Trait("Category", "Integration")]
public class AgentsExtrasTests(IntegrationWebApplicationFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> CreateAgentAsync()
    {
        var id = $"agent-extra-{Guid.NewGuid():N}";
        await _client.PostAsJsonAsync("/api/agents", new { id, name = "Extra Test Agent", model = new { deploymentName = "gpt-4o" } });
        return id;
    }

    [Fact]
    public async Task Validate_AgenteExistente_Retorna200()
    {
        var id = await CreateAgentAsync();

        var response = await _client.PostAsync($"/api/agents/{id}/validate", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Validate_AgenteInexistente_Retorna404()
    {
        var response = await _client.PostAsync("/api/agents/agent-nao-existe-xyz/validate", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Sandbox_AgenteExistente_Retorna200()
    {
        var id = await CreateAgentAsync();
        var body = new { input = "test" };

        var response = await _client.PostAsJsonAsync($"/api/agents/{id}/sandbox", body);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Sandbox_AgenteInexistente_Retorna404()
    {
        var response = await _client.PostAsJsonAsync("/api/agents/agent-nao-existe-xyz/sandbox", new { input = "test" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Compare_AgenteInexistente_Retorna404()
    {
        var body = new { versionIdA = Guid.NewGuid().ToString(), versionIdB = Guid.NewGuid().ToString() };
        var response = await _client.PostAsJsonAsync("/api/agents/agent-nao-existe-xyz/compare", body);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetVersions_AgenteExistente_RetornaLista()
    {
        var id = await CreateAgentAsync();

        var response = await _client.GetAsync($"/api/agents/{id}/versions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.ValueKind.Should().Be(JsonValueKind.Array);
    }
}
