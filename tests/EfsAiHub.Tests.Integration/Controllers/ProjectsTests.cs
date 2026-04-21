namespace EfsAiHub.Tests.Integration.Controllers;

[Collection("Integration")]
[Trait("Category", "Integration")]
public class ProjectsTests(IntegrationWebApplicationFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Post_SemName_Retorna400()
    {
        var response = await _client.PostAsJsonAsync("/api/projects", new { name = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_Valido_Retorna201ComId()
    {
        var response = await _client.PostAsJsonAsync("/api/projects", new { name = $"Projeto {Guid.NewGuid():N}" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("id", out _).Should().BeTrue();
        body.TryGetProperty("tenantId", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GetAll_Retorna200()
    {
        var response = await _client.GetAsync("/api/projects");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task Get_ProjetoExistente_Retorna200()
    {
        var postResp = await _client.PostAsJsonAsync("/api/projects", new { name = $"Projeto {Guid.NewGuid():N}" });
        var created = await postResp.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString()!;

        var response = await _client.GetAsync($"/api/projects/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetString().Should().Be(id);
    }

    [Fact]
    public async Task Get_ProjetoInexistente_Retorna404()
    {
        var response = await _client.GetAsync($"/api/projects/{Guid.NewGuid():N}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Put_AtualizaNome_Retorna200()
    {
        var postResp = await _client.PostAsJsonAsync("/api/projects", new { name = $"Projeto {Guid.NewGuid():N}" });
        var created = await postResp.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString()!;

        var response = await _client.PutAsJsonAsync($"/api/projects/{id}", new { name = "Projeto Atualizado" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("name").GetString().Should().Be("Projeto Atualizado");
    }

    [Fact]
    public async Task Delete_ProjetoDefault_Retorna400()
    {
        var response = await _client.DeleteAsync("/api/projects/default");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Delete_ProjetoExistente_Retorna204()
    {
        var postResp = await _client.PostAsJsonAsync("/api/projects", new { name = $"Projeto {Guid.NewGuid():N}" });
        var created = await postResp.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString()!;

        var response = await _client.DeleteAsync($"/api/projects/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Delete_ProjetoInexistente_Retorna404()
    {
        var response = await _client.DeleteAsync($"/api/projects/{Guid.NewGuid():N}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── DefaultProjectGuard: proteção do projeto "default" ────────────────────

    [Fact]
    public async Task ProjetoDefault_SemProjectHeader_ComGateAtivo_Retorna403()
    {
        // Gate ativo com admin configurado; cliente sem x-efs-project-id → ProjectId = "default"
        var client = factory.CreateClientWithAdminGate("admin-proj-test");

        var response = await client.GetAsync("/api/agents");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ProjetoDefault_AdminComGateAtivo_Passa()
    {
        var client = factory.CreateClientWithAdminGate("admin-proj-test")
            .WithAdminAccount("admin-proj-test");

        var response = await client.GetAsync("/api/agents");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ProjetoNaoDefault_SemAccountHeader_ComGateAtivo_Passa()
    {
        // Mesmo com gate ativo, qualquer projeto diferente de "default" passa sem admin
        var client = factory.CreateClientWithAdminGate("admin-proj-test")
            .WithProject($"projeto-livre-{Guid.NewGuid():N}");

        var response = await client.GetAsync("/api/agents");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
