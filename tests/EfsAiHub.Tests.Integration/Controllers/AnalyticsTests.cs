namespace EfsAiHub.Tests.Integration.Controllers;

[Collection("Integration")]
[Trait("Category", "Integration")]
public class AnalyticsTests(IntegrationWebApplicationFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetSummary_SemParams_Retorna200()
    {
        var response = await _client.GetAsync("/api/analytics/executions/summary");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetSummary_ComFiltroDeData_Retorna200()
    {
        var from = DateTime.UtcNow.AddDays(-7).ToString("o");
        var to = DateTime.UtcNow.ToString("o");

        var response = await _client.GetAsync($"/api/analytics/executions/summary?from={from}&to={to}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetTimeseries_SemParams_Retorna200()
    {
        var response = await _client.GetAsync("/api/analytics/executions/timeseries");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetTimeseries_GroupByDay_Retorna200()
    {
        var from = DateTime.UtcNow.AddDays(-7).ToString("o");
        var to = DateTime.UtcNow.ToString("o");

        var response = await _client.GetAsync($"/api/analytics/executions/timeseries?from={from}&to={to}&groupBy=day");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
