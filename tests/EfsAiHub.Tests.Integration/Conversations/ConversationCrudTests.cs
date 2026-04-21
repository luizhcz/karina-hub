namespace EfsAiHub.Tests.Integration.Conversations;

[Collection("Integration")]
[Trait("Category", "Integration")]
public class ConversationCrudTests(IntegrationWebApplicationFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly string AccountHeader = "x-efs-account";
    private static readonly string UserId = "test-user-conv";

    [Fact]
    public async Task Post_CriaConversa_Retorna201()
    {
        var request = new { userId = UserId, workflowId = (string?)null };

        var response = await _client.PostAsJsonAsync("/api/conversations", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("conversationId").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Get_ConversaExistente_Retorna200()
    {
        // Arrange: criar conversa
        var created = await _client.PostAsJsonAsync("/api/conversations", new { userId = UserId });
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("conversationId").GetString()!;

        // Act
        var response = await _client.GetAsync($"/api/conversations/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("conversationId").GetString().Should().Be(id);
    }

    [Fact]
    public async Task Get_ConversaInexistente_Retorna404()
    {
        var response = await _client.GetAsync("/api/conversations/conv-nao-existe-xyz");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_ConversaExistente_Retorna204()
    {
        var created = await _client.PostAsJsonAsync("/api/conversations", new { userId = UserId });
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("conversationId").GetString()!;

        var response = await _client.DeleteAsync($"/api/conversations/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Post_ListarPorUserId_RetornaConversasDoUsuario()
    {
        var uid = $"user-list-{Guid.NewGuid():N}";
        await _client.PostAsJsonAsync("/api/conversations", new { userId = uid });
        await _client.PostAsJsonAsync("/api/conversations", new { userId = uid });

        var response = await _client.GetAsync($"/api/conversations?userId={uid}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await response.Content.ReadFromJsonAsync<JsonElement>();
        list.GetArrayLength().Should().BeGreaterThanOrEqualTo(2);
    }
}
