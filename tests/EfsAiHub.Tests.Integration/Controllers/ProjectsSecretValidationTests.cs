namespace EfsAiHub.Tests.Integration.Controllers;

[Collection("Integration")]
[Trait("Category", "Integration")]
public class ProjectsSecretValidationTests(IntegrationWebApplicationFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Post_LiteralLlmApiKey_Retorna400()
    {
        var response = await _client.PostAsJsonAsync("/api/projects", new
        {
            name = $"proj-{Guid.NewGuid():N}",
            llmConfig = new
            {
                credentials = new Dictionary<string, object>
                {
                    ["openai"] = new { apiKey = "sk-literal-key-not-allowed" }
                }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("literal");
        body.Should().Contain("secret://aws/");
    }

    [Fact]
    public async Task Post_AwsRefLlmApiKey_Retorna201()
    {
        var response = await _client.PostAsJsonAsync("/api/projects", new
        {
            name = $"proj-{Guid.NewGuid():N}",
            llmConfig = new
            {
                credentials = new Dictionary<string, object>
                {
                    ["openai"] = new { apiKey = "secret://aws/efs-ai-hub-test-openai" }
                }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Post_LiteralFoundryApiKeyRef_Retorna400()
    {
        var response = await _client.PostAsJsonAsync("/api/projects", new
        {
            name = $"proj-{Guid.NewGuid():N}",
            settings = new
            {
                evaluation = new
                {
                    foundry = new { enabled = true, endpoint = "https://x", modelDeployment = "y", apiKeyRef = "raw-foundry-key" }
                }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Foundry");
        body.Should().Contain("secret://aws/");
    }

    [Fact]
    public async Task Put_LiteralLlmApiKey_Retorna400()
    {
        var post = await _client.PostAsJsonAsync("/api/projects", new { name = $"proj-{Guid.NewGuid():N}" });
        var created = await post.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString()!;

        var response = await _client.PutAsJsonAsync($"/api/projects/{id}", new
        {
            llmConfig = new
            {
                credentials = new Dictionary<string, object>
                {
                    ["openai"] = new { apiKey = "sk-direct-literal" }
                }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Put_MaskedApiKey_Retorna400()
    {
        var post = await _client.PostAsJsonAsync("/api/projects", new
        {
            name = $"proj-{Guid.NewGuid():N}",
            llmConfig = new
            {
                credentials = new Dictionary<string, object>
                {
                    ["openai"] = new { apiKey = "secret://aws/efs-ai-hub-mask-test" }
                }
            }
        });
        var created = await post.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString()!;

        // UI nunca deve fazer round-trip do valor mascarado — esperamos rejeição
        // explícita pra forçar reenvio da referência completa.
        var response = await _client.PutAsJsonAsync($"/api/projects/{id}", new
        {
            name = "renamed",
            llmConfig = new
            {
                credentials = new Dictionary<string, object>
                {
                    ["openai"] = new { apiKey = "***" }
                }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
