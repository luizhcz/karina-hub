namespace EfsAiHub.Tests.Integration.Workflows;

[Collection("Integration")]
[Trait("Category", "Integration")]
public class WorkflowCrudTests(IntegrationWebApplicationFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private static object BuildPayload(string id = "wf-crud-test") => new
    {
        id,
        name = "Workflow CRUD Test",
        orchestrationMode = "Sequential",
        agents = new[] { new { agentId = "agent-placeholder" } }
    };

    [Fact]
    public async Task Post_CriaWorkflow_Retorna201()
    {
        var payload = BuildPayload($"wf-create-{Guid.NewGuid():N}");

        var response = await _client.PostAsJsonAsync("/api/workflows", payload);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("id", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Get_WorkflowExistente_Retorna200()
    {
        var id = $"wf-get-{Guid.NewGuid():N}";
        await _client.PostAsJsonAsync("/api/workflows", BuildPayload(id));

        var response = await _client.GetAsync($"/api/workflows/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetString().Should().Be(id);
    }

    [Fact]
    public async Task Get_WorkflowInexistente_Retorna404()
    {
        var response = await _client.GetAsync("/api/workflows/wf-nao-existe-xyz-999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Put_AtualizaWorkflow_PreservaId()
    {
        var id = $"wf-put-{Guid.NewGuid():N}";
        await _client.PostAsJsonAsync("/api/workflows", BuildPayload(id));

        var updated = new
        {
            id,
            name = "Workflow Atualizado",
            orchestrationMode = "Sequential",
            agents = new[] { new { agentId = "agent-placeholder" } }
        };

        var response = await _client.PutAsJsonAsync($"/api/workflows/{id}", updated);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetString().Should().Be(id);
        body.GetProperty("name").GetString().Should().Be("Workflow Atualizado");
    }

    [Fact]
    public async Task Delete_WorkflowExistente_Retorna204()
    {
        var id = $"wf-del-{Guid.NewGuid():N}";
        await _client.PostAsJsonAsync("/api/workflows", BuildPayload(id));

        var response = await _client.DeleteAsync($"/api/workflows/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
