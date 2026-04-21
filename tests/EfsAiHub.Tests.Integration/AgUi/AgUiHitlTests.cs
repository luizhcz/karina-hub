namespace EfsAiHub.Tests.Integration.AgUi;

[Collection("Integration")]
[Trait("Category", "Integration")]
public class AgUiHitlTests(IntegrationWebApplicationFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Post_ResolveHitl_SemInteracoes_Retorna200()
    {
        // Resolve-hitl with tool messages but no pending interaction
        // Should return gracefully (200 or 404)
        var payload = new
        {
            messages = new[]
            {
                new { role = "tool", content = "approved", toolCallId = "int-nao-existe" }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/chat/ag-ui/stream", payload);

        // HITL puro sem mensagem user → retorna hitlResolved: true (200)
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("hitlResolved").GetBoolean().Should().BeTrue();
    }
}
