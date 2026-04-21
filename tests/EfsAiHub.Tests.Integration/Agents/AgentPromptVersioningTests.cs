namespace EfsAiHub.Tests.Integration.Agents;

[Collection("Integration")]
[Trait("Category", "Integration")]
public class AgentPromptVersioningTests(IntegrationWebApplicationFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> CreateAgentAsync()
    {
        var id = $"agent-prompt-{Guid.NewGuid():N}";
        var payload = new
        {
            id,
            name = "Agent Prompt Test",
            model = new { deploymentName = "gpt-4o" },
            instructions = "Instrução base."
        };
        var response = await _client.PostAsJsonAsync("/api/agents", payload);
        response.EnsureSuccessStatusCode();
        return id;
    }

    [Fact]
    public async Task Post_CriaVersaoPrompt_Retorna201()
    {
        var agentId = await CreateAgentAsync();
        var payload = new { versionId = "v1.0", content = "Prompt versão 1" };

        var response = await _client.PostAsJsonAsync($"/api/agents/{agentId}/prompts", payload);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Get_ListarVersionsPrompt_Retorna200()
    {
        var agentId = await CreateAgentAsync();
        await _client.PostAsJsonAsync($"/api/agents/{agentId}/prompts",
            new { versionId = "v1.0", content = "Prompt v1" });
        await _client.PostAsJsonAsync($"/api/agents/{agentId}/prompts",
            new { versionId = "v1.1", content = "Prompt v1.1" });

        var response = await _client.GetAsync($"/api/agents/{agentId}/prompts");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await response.Content.ReadFromJsonAsync<JsonElement>();
        list.GetArrayLength().Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Put_SetMaster_GetActive_RetornaMasterVersion()
    {
        var agentId = await CreateAgentAsync();
        await _client.PostAsJsonAsync($"/api/agents/{agentId}/prompts",
            new { versionId = "v1.0", content = "Prompt v1" });
        await _client.PostAsJsonAsync($"/api/agents/{agentId}/prompts",
            new { versionId = "v2.0", content = "Prompt v2 - master" });

        // Set master
        await _client.PutAsync($"/api/agents/{agentId}/prompts/master?versionId=v2.0", null);

        // Get active
        var response = await _client.GetAsync($"/api/agents/{agentId}/prompts/active");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var active = await response.Content.ReadFromJsonAsync<JsonElement>();
        active.GetProperty("content").GetString().Should().Be("Prompt v2 - master");
    }

    [Fact]
    public async Task Delete_VersaoNaoAtiva_Retorna204()
    {
        var agentId = await CreateAgentAsync();
        await _client.PostAsJsonAsync($"/api/agents/{agentId}/prompts",
            new { versionId = "v1.0", content = "Prompt v1" });
        await _client.PostAsJsonAsync($"/api/agents/{agentId}/prompts",
            new { versionId = "v2.0", content = "Prompt v2 - active" });

        await _client.PutAsync($"/api/agents/{agentId}/prompts/master?versionId=v2.0", null);

        var response = await _client.DeleteAsync($"/api/agents/{agentId}/prompts/v1.0");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
