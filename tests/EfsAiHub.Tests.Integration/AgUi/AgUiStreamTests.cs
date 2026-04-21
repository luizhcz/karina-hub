namespace EfsAiHub.Tests.Integration.AgUi;

[Collection("Integration")]
[Trait("Category", "Integration")]
public class AgUiStreamTests(IntegrationWebApplicationFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Post_SemMensagens_Retorna400()
    {
        var payload = new { messages = (object[]?)null };

        var response = await _client.PostAsJsonAsync("/api/chat/ag-ui/stream", payload);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_MensagemVazia_Retorna400()
    {
        var payload = new
        {
            messages = new[] { new { role = "user", content = "   " } }
        };

        var response = await _client.PostAsJsonAsync("/api/chat/ag-ui/stream", payload);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_WorkflowIdViaHeader_Aceita()
    {
        // This test just verifies the endpoint accepts x-efs-workflow-id header
        // without returning 400 (full execution would require a real LLM)
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/ag-ui/stream");
        request.Headers.Add("x-efs-workflow-id", "wf-test");
        request.Content = JsonContent.Create(new
        {
            messages = new[] { new { role = "user", content = "Olá" } }
        });

        var response = await _client.SendAsync(request);

        // Should not be 400 (bad request from missing message) — may be 200 or error from LLM
        response.StatusCode.Should().NotBe(HttpStatusCode.BadRequest);
    }
}
