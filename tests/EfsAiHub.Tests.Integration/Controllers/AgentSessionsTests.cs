namespace EfsAiHub.Tests.Integration.Controllers;

/// <summary>
/// AgentSessionsController tests.
/// Note: POST /sessions requires a real Azure AI Agents backend (PersistentAgentsAdministrationClient)
/// to create sessions. Only route-level tests that don't require the backend are included here.
/// </summary>
[Collection("Integration")]
[Trait("Category", "Integration")]
public class AgentSessionsTests(IntegrationWebApplicationFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetAll_AgenteInexistente_Retorna200ComArrayVazio()
    {
        var agentId = $"agent-sess-{Guid.NewGuid():N}";

        var response = await _client.GetAsync($"/api/agents/{agentId}/sessions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetById_SessaoInexistente_Retorna404()
    {
        var agentId = $"agent-sess-{Guid.NewGuid():N}";
        var sessionId = Guid.NewGuid().ToString();

        var response = await _client.GetAsync($"/api/agents/{agentId}/sessions/{sessionId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_SessaoInexistente_Retorna404()
    {
        var agentId = $"agent-sess-{Guid.NewGuid():N}";
        var sessionId = Guid.NewGuid().ToString();

        var response = await _client.DeleteAsync($"/api/agents/{agentId}/sessions/{sessionId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
