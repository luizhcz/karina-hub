namespace EfsAiHub.Tests.Integration.Agents;

/// <summary>
/// Cobre PATCH /api/agents/{id}/visibility +
/// critérios de aceitação: owner gate (403, sem vazar ProjectId), preservação no
/// PUT, hidratação no GET, audit/metric.
/// </summary>
[Collection("Integration")]
[Trait("Category", "Integration")]
public class AgentVisibilityTests(IntegrationWebApplicationFactory factory)
{
    private readonly IntegrationWebApplicationFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private static object BuildPayload(string id, string? visibility = null) => visibility is null
        ? new
        {
            id,
            name = "Agent Visibility Test",
            model = new { deploymentName = "gpt-5.4-mini" }
        }
        : new
        {
            id,
            name = "Agent Visibility Test",
            model = new { deploymentName = "gpt-5.4-mini" },
            visibility,
        };

    [Fact]
    public async Task Patch_VisibilityValida_Retorna200_AtualizaCampo()
    {
        var id = $"agent-vis-{Guid.NewGuid():N}";
        await _client.PostAsJsonAsync("/api/agents", BuildPayload(id));

        var resp = await _client.PatchAsJsonAsync(
            $"/api/agents/{id}/visibility",
            new { visibility = "global", reason = "test promote" });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("visibility").GetString().Should().Be("global");
        body.GetProperty("originProjectId").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("originTenantId").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Patch_VisibilityInvalida_Retorna400()
    {
        var id = $"agent-vis-bad-{Guid.NewGuid():N}";
        await _client.PostAsJsonAsync("/api/agents", BuildPayload(id));

        var resp = await _client.PatchAsJsonAsync(
            $"/api/agents/{id}/visibility",
            new { visibility = "shared" });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Patch_AgentInexistente_Retorna404()
    {
        var resp = await _client.PatchAsJsonAsync(
            "/api/agents/agent-inexistente-xyz/visibility",
            new { visibility = "global" });

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Patch_NonOwnerProject_Retorna403_SemVazarProjectId()
    {
        // Setup: cria agent global no projeto default. Outro projeto tenta mudar visibility.
        var id = $"agent-owner-gate-{Guid.NewGuid():N}";
        await _client.PostAsJsonAsync("/api/agents", BuildPayload(id, "global"));

        var otherClient = _factory.CreateClient().WithProject("other-project-fake");
        var resp = await otherClient.PatchAsJsonAsync(
            $"/api/agents/{id}/visibility",
            new { visibility = "project" });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString()
            .Should().NotContain("default", because: "mensagem 403 não pode expor o ProjectId real do owner");
    }

    [Fact]
    public async Task Patch_VisibilityIgualAoExistente_RetornaOK_NoOp()
    {
        var id = $"agent-vis-noop-{Guid.NewGuid():N}";
        await _client.PostAsJsonAsync("/api/agents", BuildPayload(id));

        var resp = await _client.PatchAsJsonAsync(
            $"/api/agents/{id}/visibility",
            new { visibility = "project" });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("visibility").GetString().Should().Be("project");
    }

    [Fact]
    public async Task Get_AgentResponse_ExpoeVisibility_E_Origin()
    {
        var id = $"agent-vis-resp-{Guid.NewGuid():N}";
        await _client.PostAsJsonAsync("/api/agents", BuildPayload(id, "global"));

        var resp = await _client.GetAsync($"/api/agents/{id}");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("visibility").GetString().Should().Be("global");
        body.TryGetProperty("originProjectId", out _).Should().BeTrue();
        body.TryGetProperty("originTenantId", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Put_Agent_PreservaVisibility_GlobalNaoVoltaParaProject()
    {
        var id = $"agent-put-preserve-{Guid.NewGuid():N}";
        await _client.PostAsJsonAsync("/api/agents", BuildPayload(id));

        // Marca como global via PATCH.
        var promote = await _client.PatchAsJsonAsync(
            $"/api/agents/{id}/visibility",
            new { visibility = "global" });
        promote.StatusCode.Should().Be(HttpStatusCode.OK);

        // PUT regular sem campo "visibility" — não pode reverter pra "project".
        var updateBody = new
        {
            id,
            name = "Agent Atualizado",
            model = new { deploymentName = "gpt-5.4-mini" }
        };
        var put = await _client.PutAsJsonAsync($"/api/agents/{id}", updateBody);
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        var get = await _client.GetAsync($"/api/agents/{id}");
        var body = await get.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("visibility").GetString()
            .Should().Be("global", because: "PUT não pode resetar visibility quando request omite o campo");
        body.GetProperty("name").GetString().Should().Be("Agent Atualizado");
    }
}
