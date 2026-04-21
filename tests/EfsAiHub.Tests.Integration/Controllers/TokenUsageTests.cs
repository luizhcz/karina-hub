namespace EfsAiHub.Tests.Integration.Controllers;

[Collection("Integration")]
[Trait("Category", "Integration")]
public class TokenUsageTests(IntegrationWebApplicationFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetSummary_Retorna200()
    {
        var response = await _client.GetAsync("/api/token-usage/summary");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetSummary_ComFiltroDeData_Retorna200()
    {
        var from = DateTime.UtcNow.AddDays(-7).ToString("o");
        var to = DateTime.UtcNow.ToString("o");

        var response = await _client.GetAsync($"/api/token-usage/summary?from={from}&to={to}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetAgentSummary_AgenteSemDados_Retorna200()
    {
        var response = await _client.GetAsync("/api/token-usage/agents/agent-sem-dados/summary");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetAgentHistory_LimitePadrao_Retorna200()
    {
        var response = await _client.GetAsync("/api/token-usage/agents/agent-sem-dados/history");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetThroughput_Retorna200()
    {
        var response = await _client.GetAsync("/api/token-usage/throughput");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
