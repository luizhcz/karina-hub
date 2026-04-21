namespace EfsAiHub.Tests.Integration.Controllers;

[Collection("Integration")]
[Trait("Category", "Integration")]
public class ModelPricingTests(IntegrationWebApplicationFactory factory) : IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync()
        => DatabaseCleanup.TruncateAsync(factory.ConnectionString, "aihub.model_pricing");

    public Task DisposeAsync() => Task.CompletedTask;

    private static object BuildPricing(string modelId) => new
    {
        modelId,
        provider = "OPENAI",
        pricePerInputToken = 0.000001m,
        pricePerOutputToken = 0.000002m,
        currency = "USD",
        effectiveFrom = DateTime.UtcNow.AddDays(-1)
    };

    [Fact]
    public async Task GetAll_Retorna200()
    {
        var response = await _client.GetAsync("/api/admin/model-pricing");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task Post_CriaPricing_Retorna200()
    {
        var modelId = $"gpt-test-{Guid.NewGuid():N}";
        var response = await _client.PostAsJsonAsync("/api/admin/model-pricing", BuildPricing(modelId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("modelId").GetString().Should().Be(modelId);
    }

    [Fact]
    public async Task GetById_PricingExistente_Retorna200()
    {
        var modelId = $"gpt-test-{Guid.NewGuid():N}";
        var postResp = await _client.PostAsJsonAsync("/api/admin/model-pricing", BuildPricing(modelId));
        var created = await postResp.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetInt32();

        var response = await _client.GetAsync($"/api/admin/model-pricing/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("modelId").GetString().Should().Be(modelId);
    }

    [Fact]
    public async Task GetById_Inexistente_Retorna404()
    {
        var response = await _client.GetAsync("/api/admin/model-pricing/999999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_PricingExistente_Retorna204()
    {
        var modelId = $"gpt-test-{Guid.NewGuid():N}";
        var postResp = await _client.PostAsJsonAsync("/api/admin/model-pricing", BuildPricing(modelId));
        var created = await postResp.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetInt32();

        var response = await _client.DeleteAsync($"/api/admin/model-pricing/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Delete_PricingInexistente_Retorna404()
    {
        var response = await _client.DeleteAsync("/api/admin/model-pricing/999999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
