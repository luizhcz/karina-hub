namespace EfsAiHub.Tests.Integration.Controllers;

[Collection("Integration")]
[Trait("Category", "Integration")]
public class ModelCatalogTests(IntegrationWebApplicationFactory factory) : IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync()
        => DatabaseCleanup.TruncateAsync(factory.ConnectionString, "aihub.model_catalog");

    public Task DisposeAsync() => Task.CompletedTask;

    private static object BuildModel(string id, string provider = "OPENAI") => new
    {
        id,
        provider,
        displayName = $"Test Model {id}",
        description = "Integration test model",
        contextWindow = 128000,
        capabilities = new[] { "chat", "function_calling" },
        isActive = true
    };

    [Fact]
    public async Task GetAll_Retorna200()
    {
        var response = await _client.GetAsync("/api/model-catalog");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task Post_CriaModelo_Retorna200()
    {
        var id = $"model-{Guid.NewGuid():N}";
        var response = await _client.PostAsJsonAsync("/api/model-catalog", BuildModel(id));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetString().Should().Be(id);
    }

    [Fact]
    public async Task Post_SemIdOuProvider_Retorna400()
    {
        var response = await _client.PostAsJsonAsync("/api/model-catalog", new { displayName = "No Id" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetById_ModeloExistente_Retorna200()
    {
        var id = $"model-{Guid.NewGuid():N}";
        await _client.PostAsJsonAsync("/api/model-catalog", BuildModel(id, "OPENAI"));

        var response = await _client.GetAsync($"/api/model-catalog/OPENAI/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetString().Should().Be(id);
    }

    [Fact]
    public async Task GetById_Inexistente_Retorna404()
    {
        var response = await _client.GetAsync($"/api/model-catalog/OPENAI/nao-existe-xyz-999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetAll_FiltradoPorProvider_RetornaSomenteProvider()
    {
        var id1 = $"model-{Guid.NewGuid():N}";
        var id2 = $"model-{Guid.NewGuid():N}";
        await _client.PostAsJsonAsync("/api/model-catalog", BuildModel(id1, "OPENAI"));
        await _client.PostAsJsonAsync("/api/model-catalog", BuildModel(id2, "AZUREOPENAI"));

        var response = await _client.GetAsync("/api/model-catalog?provider=OPENAI");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        body.ValueKind.Should().Be(JsonValueKind.Array);
        var providers = body.EnumerateArray()
            .Select(e => e.GetProperty("provider").GetString())
            .ToList();
        providers.Should().AllSatisfy(p => p.Should().Be("OPENAI"));
    }

    [Fact]
    public async Task Delete_SoftDelete_Retorna204()
    {
        var id = $"model-{Guid.NewGuid():N}";
        await _client.PostAsJsonAsync("/api/model-catalog", BuildModel(id));

        var response = await _client.DeleteAsync($"/api/model-catalog/OPENAI/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Delete_Inexistente_Retorna404()
    {
        var response = await _client.DeleteAsync("/api/model-catalog/OPENAI/nao-existe-xyz-999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
