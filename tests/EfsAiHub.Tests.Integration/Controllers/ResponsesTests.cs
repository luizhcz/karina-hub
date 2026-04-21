namespace EfsAiHub.Tests.Integration.Controllers;

[Collection("Integration")]
[Trait("Category", "Integration")]
public class ResponsesTests(IntegrationWebApplicationFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Post_SemAgentId_Retorna400()
    {
        var response = await _client.PostAsJsonAsync("/api/responses", new { input = "hello" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_Valido_Retorna202ComJobId()
    {
        var response = await _client.PostAsJsonAsync("/api/responses", new
        {
            agentId = $"agent-resp-{Guid.NewGuid():N}",
            input = "Hello from integration test"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        response.Headers.Location.Should().NotBeNull();
    }

    [Fact]
    public async Task GetJob_Criado_Retorna200()
    {
        var postResp = await _client.PostAsJsonAsync("/api/responses", new
        {
            agentId = $"agent-resp-{Guid.NewGuid():N}",
            input = "Hello"
        });
        var location = postResp.Headers.Location!;

        var response = await _client.GetAsync(location);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetJob_Inexistente_Retorna404()
    {
        var response = await _client.GetAsync($"/api/responses/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Cancel_JobCriado_Retorna204()
    {
        var postResp = await _client.PostAsJsonAsync("/api/responses", new
        {
            agentId = $"agent-resp-{Guid.NewGuid():N}",
            input = "Hello"
        });
        var location = postResp.Headers.Location!;
        var jobId = location.ToString().Split('/').Last();

        var response = await _client.PostAsync($"/api/responses/{jobId}:cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Cancel_JobInexistente_Retorna404()
    {
        var response = await _client.PostAsync($"/api/responses/{Guid.NewGuid()}:cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
