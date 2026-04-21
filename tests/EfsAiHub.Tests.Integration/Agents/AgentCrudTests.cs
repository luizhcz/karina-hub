namespace EfsAiHub.Tests.Integration.Agents;

[Collection("Integration")]
[Trait("Category", "Integration")]
public class AgentCrudTests(IntegrationWebApplicationFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private static object BuildPayload(string id) => new
    {
        id,
        name = "Agente CRUD Test",
        model = new { deploymentName = "gpt-4o" }
    };

    [Fact]
    public async Task Post_CriaAgente_Retorna201()
    {
        var id = $"agent-create-{Guid.NewGuid():N}";

        var response = await _client.PostAsJsonAsync("/api/agents", BuildPayload(id));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetString().Should().Be(id);
    }

    [Fact]
    public async Task Get_AgenteExistente_Retorna200()
    {
        var id = $"agent-get-{Guid.NewGuid():N}";
        await _client.PostAsJsonAsync("/api/agents", BuildPayload(id));

        var response = await _client.GetAsync($"/api/agents/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetString().Should().Be(id);
    }

    [Fact]
    public async Task Get_AgenteInexistente_Retorna404()
    {
        var response = await _client.GetAsync("/api/agents/agent-nao-existe-xyz-999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Put_AtualizaAgente_PreservaId()
    {
        var id = $"agent-put-{Guid.NewGuid():N}";
        await _client.PostAsJsonAsync("/api/agents", BuildPayload(id));

        var updated = new
        {
            id,
            name = "Agente Atualizado",
            model = new { deploymentName = "gpt-4o" }
        };

        var response = await _client.PutAsJsonAsync($"/api/agents/{id}", updated);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetString().Should().Be(id);
        body.GetProperty("name").GetString().Should().Be("Agente Atualizado");
    }

    [Fact]
    public async Task Delete_AgenteExistente_Retorna204()
    {
        var id = $"agent-del-{Guid.NewGuid():N}";
        await _client.PostAsJsonAsync("/api/agents", BuildPayload(id));

        var response = await _client.DeleteAsync($"/api/agents/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
