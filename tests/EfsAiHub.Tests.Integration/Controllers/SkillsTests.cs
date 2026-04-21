namespace EfsAiHub.Tests.Integration.Controllers;

[Collection("Integration")]
[Trait("Category", "Integration")]
public class SkillsTests(IntegrationWebApplicationFactory factory) : IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync()
        => DatabaseCleanup.TruncateAsync(factory.ConnectionString, "aihub.skill_versions", "aihub.skills");

    public Task DisposeAsync() => Task.CompletedTask;

    private static object BuildSkill(string id) => new
    {
        id,
        name = "Test Skill",
        description = "Integration test skill"
    };

    [Fact]
    public async Task Put_NovaSkill_Retorna200()
    {
        var id = $"skill-create-{Guid.NewGuid():N}";
        var response = await _client.PutAsJsonAsync($"/api/skills/{id}", BuildSkill(id));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetString().Should().Be(id);
    }

    [Fact]
    public async Task Put_IdNoBodyDivergeDePath_Retorna400()
    {
        var pathId = $"skill-path-{Guid.NewGuid():N}";
        var bodyId = $"skill-body-{Guid.NewGuid():N}";

        var response = await _client.PutAsJsonAsync($"/api/skills/{pathId}", BuildSkill(bodyId));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Get_SkillExistente_Retorna200()
    {
        var id = $"skill-get-{Guid.NewGuid():N}";
        await _client.PutAsJsonAsync($"/api/skills/{id}", BuildSkill(id));

        var response = await _client.GetAsync($"/api/skills/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetString().Should().Be(id);
    }

    [Fact]
    public async Task Get_SkillInexistente_Retorna404()
    {
        var response = await _client.GetAsync("/api/skills/skill-nao-existe-xyz-999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetAll_Retorna200()
    {
        var response = await _client.GetAsync("/api/skills");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Delete_SkillExistente_Retorna204()
    {
        var id = $"skill-del-{Guid.NewGuid():N}";
        await _client.PutAsJsonAsync($"/api/skills/{id}", BuildSkill(id));

        var response = await _client.DeleteAsync($"/api/skills/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Delete_SkillInexistente_Retorna404()
    {
        var response = await _client.DeleteAsync("/api/skills/skill-nao-existe-xyz-999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetVersions_SkillUpsertada_RetornaLista()
    {
        var id = $"skill-ver-{Guid.NewGuid():N}";
        await _client.PutAsJsonAsync($"/api/skills/{id}", BuildSkill(id));

        var response = await _client.GetAsync($"/api/skills/{id}/versions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.ValueKind.Should().Be(JsonValueKind.Array);
    }
}
