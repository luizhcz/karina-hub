namespace EfsAiHub.Tests.Integration.Controllers;

[Collection("Integration")]
[Trait("Category", "Integration")]
public class WorkflowsExtrasTests(IntegrationWebApplicationFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> CreateAgentAsync()
    {
        var id = $"agent-wfx-{Guid.NewGuid():N}";
        await _client.PostAsJsonAsync("/api/agents", new { id, name = "WF Extra Agent", model = new { deploymentName = "gpt-4o" } });
        return id;
    }

    private object BuildWorkflow(string id, string agentId) => new
    {
        id,
        name = "Extra Test Workflow",
        orchestrationMode = "Sequential",
        agents = new[] { new { agentId } }
    };

    private async Task<string> CreateWorkflowAsync()
    {
        var agentId = await CreateAgentAsync();
        var id = $"wf-extra-{Guid.NewGuid():N}";
        await _client.PostAsJsonAsync("/api/workflows", BuildWorkflow(id, agentId));
        return id;
    }

    [Fact]
    public async Task Clone_WorkflowExistente_Retorna201()
    {
        var id = await CreateWorkflowAsync();

        var response = await _client.PostAsJsonAsync($"/api/workflows/{id}/clone", new { });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("id", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Clone_WorkflowInexistente_Retorna404()
    {
        var response = await _client.PostAsJsonAsync("/api/workflows/wf-nao-existe-xyz/clone", new { });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Validate_WorkflowExistente_Retorna200()
    {
        var id = await CreateWorkflowAsync();

        var response = await _client.PostAsync($"/api/workflows/{id}/validate", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Validate_WorkflowInexistente_Retorna404()
    {
        var response = await _client.PostAsync("/api/workflows/wf-nao-existe-xyz/validate", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetVisible_Retorna200ComArray()
    {
        var response = await _client.GetAsync("/api/workflows/visible");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetExecutions_WorkflowExistente_Retorna200()
    {
        var id = await CreateWorkflowAsync();

        var response = await _client.GetAsync($"/api/workflows/{id}/executions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetExecutions_WorkflowInexistente_Retorna200VazioOu404()
    {
        var response = await _client.GetAsync("/api/workflows/wf-nao-existe-xyz/executions");

        new[] { HttpStatusCode.OK, HttpStatusCode.NotFound }
            .Should().Contain(response.StatusCode);
    }

    [Fact]
    public async Task GetVersions_WorkflowExistente_RetornaLista()
    {
        var id = await CreateWorkflowAsync();

        var response = await _client.GetAsync($"/api/workflows/{id}/versions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.ValueKind.Should().Be(JsonValueKind.Array);
    }
}
