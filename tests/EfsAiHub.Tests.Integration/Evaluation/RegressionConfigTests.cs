namespace EfsAiHub.Tests.Integration.Evaluation;

[Collection("Integration")]
[Trait("Category", "Integration")]
public sealed class RegressionConfigTests(IntegrationWebApplicationFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Get_Sem_Config_Retorna_AutotriggerEnabled_False()
    {
        var agentId = await CreateAgentAsync();

        var response = await _client.GetAsync($"/api/agents/{agentId}/regression-config");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<RegressionConfigDto>();
        dto!.AutotriggerEnabled.Should().BeFalse();
        dto.RegressionTestSetId.Should().BeNull();
        dto.RegressionEvaluatorConfigVersionId.Should().BeNull();
    }

    [Fact]
    public async Task Put_TestSetId_E_ConfigVersionId_Habilita_Autotrigger()
    {
        var agentId = await CreateAgentAsync();

        var body = new
        {
            regressionTestSetId = "ts-fake",
            regressionEvaluatorConfigVersionId = "ec-fake"
        };

        var response = await _client.PutAsJsonAsync($"/api/agents/{agentId}/regression-config", body);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<RegressionConfigDto>();
        dto!.AutotriggerEnabled.Should().BeTrue();
        dto.RegressionTestSetId.Should().Be("ts-fake");
    }

    [Fact]
    public async Task Put_Agent_Inexistente_Retorna_404()
    {
        var response = await _client.PutAsJsonAsync(
            "/api/agents/agent-nao-existe-xyz/regression-config",
            new { regressionTestSetId = "ts-1" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<string> CreateAgentAsync()
    {
        var id = $"agent-reg-{Guid.NewGuid():N}";
        var response = await _client.PostAsJsonAsync("/api/agents", new
        {
            id,
            name = "Reg Test Agent",
            model = new { deploymentName = "gpt-4o" }
        });
        response.EnsureSuccessStatusCode();
        return id;
    }

    private sealed record RegressionConfigDto(
        string AgentDefinitionId,
        string? RegressionTestSetId,
        string? RegressionEvaluatorConfigVersionId,
        bool AutotriggerEnabled);
}
