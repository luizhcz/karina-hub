namespace EfsAiHub.Tests.Integration.Controllers;

[Collection("Integration")]
[Trait("Category", "Integration")]
public class FunctionsTests(IntegrationWebApplicationFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Get_Functions_Retorna200()
    {
        var response = await _client.GetAsync("/api/functions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Get_Functions_ContemCamposObrigatorios()
    {
        var response = await _client.GetAsync("/api/functions");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        body.TryGetProperty("functionTools", out _).Should().BeTrue();
        body.TryGetProperty("codeExecutors", out _).Should().BeTrue();
        body.TryGetProperty("middlewareTypes", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Get_Functions_FunctionToolsEhArray()
    {
        var response = await _client.GetAsync("/api/functions");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("functionTools").ValueKind.Should().Be(JsonValueKind.Array);
    }
}
