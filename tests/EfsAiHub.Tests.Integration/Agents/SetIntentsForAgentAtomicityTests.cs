namespace EfsAiHub.Tests.Integration.Agents;

[Collection("Integration")]
[Trait("Category", "Integration")]
public class SetIntentsForAgentAtomicityTests(IntegrationWebApplicationFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private static object BuildIntentPayload(string id, string name, string projectId = "default") => new
    {
        id,
        name,
        description = $"Intent {name} para teste de atomicidade.",
        projectId,
        examples = Array.Empty<string>(),
    };

    private static object BuildRouterAgentPayload(string id, IReadOnlyList<string> intentIds) => new
    {
        id,
        name = $"Router atomicidade {id}",
        type = "Router",
        model = new { deploymentName = "gpt-4o-mini" },
        structuredOutput = new
        {
            responseFormat = "json_schema",
            schemaName = "router_intent",
            schema = new { type = "object", properties = new { intent = new { type = "string" } } },
        },
        routerIntentIds = intentIds,
    };

    private async Task EnsureIntentExists(string id, string name)
    {
        // Pode existir de teste anterior — POST falha com 409, ignoramos.
        var resp = await _client.PostAsJsonAsync("/api/aihub/router-intents", BuildIntentPayload(id, name));
        if (resp.StatusCode != HttpStatusCode.Created
            && resp.StatusCode != HttpStatusCode.Conflict
            && resp.StatusCode != HttpStatusCode.OK)
        {
            var body = await resp.Content.ReadAsStringAsync();
            throw new Xunit.Sdk.XunitException($"Falha ao criar intent {id}: {(int)resp.StatusCode} — {body}");
        }
    }

    [Fact]
    public async Task SetIntentsForAgent_ReplacesAllIntents_WhenUpdated()
    {
        var run = Guid.NewGuid().ToString("N").Substring(0, 8);
        var iA = $"intent-{run}-a";
        var iB = $"intent-{run}-b";
        var iC = $"intent-{run}-c";
        var agentId = $"router-{run}";

        await EnsureIntentExists(iA, $"action_a_{run}");
        await EnsureIntentExists(iB, $"action_b_{run}");
        await EnsureIntentExists(iC, $"action_c_{run}");

        // Cria Router referenciando [iA, iB].
        var createResp = await _client.PostAsJsonAsync(
            "/api/aihub/agents", BuildRouterAgentPayload(agentId, new[] { iA, iB }));
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var afterCreate = await _client.GetAsync($"/api/aihub/agents/{agentId}");
        var afterCreateBody = await afterCreate.Content.ReadFromJsonAsync<JsonElement>();
        afterCreateBody.GetProperty("routerIntentIds")
            .EnumerateArray()
            .Select(e => e.GetString())
            .Should().BeEquivalentTo(new[] { iA, iB });

        // Substitui set por [iB, iC] — iA deve sair, iC entrar, iB persistir.
        var updateResp = await _client.PutAsJsonAsync(
            $"/api/aihub/agents/{agentId}",
            BuildRouterAgentPayload(agentId, new[] { iB, iC }));
        updateResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterUpdate = await _client.GetAsync($"/api/aihub/agents/{agentId}");
        var afterUpdateBody = await afterUpdate.Content.ReadFromJsonAsync<JsonElement>();
        var finalIds = afterUpdateBody.GetProperty("routerIntentIds")
            .EnumerateArray()
            .Select(e => e.GetString()!)
            .ToList();

        finalIds.Should().BeEquivalentTo(new[] { iB, iC });
        finalIds.Should().NotContain(iA, "iA deve ter sido removido pelo replace atômico");
    }
}
