namespace EfsAiHub.Tests.Integration.Evaluation;

[Collection("Integration")]
[Trait("Category", "Integration")]
public sealed class EvaluatorCatalogTests(IntegrationWebApplicationFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetCatalog_Retorna200_E_Lista_Nao_Vazia()
    {
        var response = await _client.GetAsync("/api/evaluator-config/catalog");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetCatalog_Inclui_Local_Meai_Foundry_Por_Kind()
    {
        var entries = await _client.GetFromJsonAsync<List<CatalogEntryDto>>("/api/evaluator-config/catalog");

        entries.Should().NotBeNull();
        entries!.Select(e => e.Kind).Distinct().Should().BeEquivalentTo("Local", "Meai", "Foundry");
    }

    [Fact]
    public async Task GetCatalog_Foundry_Inclui_Safety_Evaluators()
    {
        var entries = await _client.GetFromJsonAsync<List<CatalogEntryDto>>("/api/evaluator-config/catalog");

        var foundrySafety = entries!.Where(e => e.Kind == "Foundry" && e.Dimension == "Safety").ToList();
        foundrySafety.Select(e => e.Name).Should().BeEquivalentTo("Violence", "Sexual", "SelfHarm", "HateAndUnfairness");
    }

    [Fact]
    public async Task GetCatalog_Local_KeywordCheck_Tem_Params_Example()
    {
        var entries = await _client.GetFromJsonAsync<List<CatalogEntryDto>>("/api/evaluator-config/catalog");

        var keywordCheck = entries!.Single(e => e.Kind == "Local" && e.Name == "KeywordCheck");
        keywordCheck.RequiresParams.Should().BeTrue();
        keywordCheck.ParamsExampleJson.Should().NotBeNullOrEmpty();
        keywordCheck.ParamsExampleJson.Should().Contain("keywords");
    }

    private sealed record CatalogEntryDto(
        string Kind,
        string Name,
        string Dimension,
        string Description,
        bool RequiresParams,
        string? ParamsExampleJson);
}
