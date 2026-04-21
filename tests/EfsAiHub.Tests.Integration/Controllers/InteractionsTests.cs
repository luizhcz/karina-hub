namespace EfsAiHub.Tests.Integration.Controllers;

[Collection("Integration")]
[Trait("Category", "Integration")]
public class InteractionsTests(IntegrationWebApplicationFactory factory) : IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync()
        => DatabaseCleanup.TruncateAsync(factory.ConnectionString, "aihub.human_interactions");

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetPending_Retorna200ComArray()
    {
        var response = await _client.GetAsync("/api/interactions/pending");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetById_Inexistente_Retorna404()
    {
        var response = await _client.GetAsync($"/api/interactions/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Resolve_Inexistente_Retorna404()
    {
        var body = new { resolution = "approved", approved = true };
        var response = await _client.PostAsJsonAsync($"/api/interactions/{Guid.NewGuid()}/resolve", body);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
