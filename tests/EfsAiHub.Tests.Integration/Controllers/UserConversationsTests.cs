namespace EfsAiHub.Tests.Integration.Controllers;

[Collection("Integration")]
[Trait("Category", "Integration")]
public class UserConversationsTests(IntegrationWebApplicationFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetConversations_UsuarioSemDados_Retorna200ComArray()
    {
        var userId = $"user-{Guid.NewGuid():N}";

        var response = await _client.GetAsync($"/api/users/{userId}/conversations");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetConversations_ComLimit_Retorna200()
    {
        var userId = $"user-{Guid.NewGuid():N}";

        var response = await _client.GetAsync($"/api/users/{userId}/conversations?limit=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
