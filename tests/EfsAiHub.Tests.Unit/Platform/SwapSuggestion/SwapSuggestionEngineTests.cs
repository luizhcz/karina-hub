using EfsAiHub.Platform.Runtime.Tools.SwapSuggestion;
using Microsoft.Extensions.Logging.Abstractions;

namespace EfsAiHub.Tests.Unit.Platform.SwapSuggestion;

[Trait("Category", "Unit")]
public class SwapSuggestionEngineTests
{
    private static SwapSuggestionEngine Build() => new(
        new MockPositionEndpoint(NullLogger<MockPositionEndpoint>.Instance),
        new MockAssetEndpoint(),
        new MockRecommendationEndpoint(),
        NullLogger<SwapSuggestionEngine>.Instance);

    [Fact]
    public async Task EmptyPortfolio_ReturnsNoSwaps()
    {
        var result = await Build().SuggestAsync("conta_inexistente");

        result.Swaps.Should().BeEmpty();
        result.Trades.Should().BeEmpty();
    }

    [Fact]
    public async Task RichScenario_SwapsAndReserve_SumsTo100()
    {
        var result = await Build().SuggestAsync("cli_swap_1");

        // venda em setor coberto → swap intra-setor 1×1
        result.Swaps.Should().Contain(s => s.SellSymbol == "BBDC4" && s.Targets.Single().Symbol == "BBAS3");
        result.Swaps.Should().Contain(s => s.SellSymbol == "PETR4" && s.Targets.Single().Symbol == "PRIO3");

        // setor sem cobertura → reserva POOLED (cada venda distribuída no cesto de top picks globais)
        var mglu = result.Swaps.Single(s => s.SellSymbol == "MGLU3");
        mglu.Targets.Select(t => t.Symbol).Should().BeEquivalentTo(new[] { "RADL3", "TOTS3" });
        result.Swaps.Single(s => s.SellSymbol == "WEGE3").Targets.Select(t => t.Symbol)
            .Should().BeEquivalentTo(new[] { "RADL3", "TOTS3" });
        // distribuída por potential: RADL3 (28%) recebe mais que TOTS3 (22%)
        mglu.Targets.Single(t => t.Symbol == "RADL3").WeightPct
            .Should().BeGreaterThan(mglu.Targets.Single(t => t.Symbol == "TOTS3").WeightPct);

        // só os alinhados (compra) são mantidos; neutro (WEGE3) agora recebe sugestão
        result.Trades.Where(t => t.Side == "SELL").Select(t => t.Symbol)
            .Should().NotContain(new[] { "ITUB4", "VALE3" });
        result.Trades.Where(t => t.Side == "SELL").Select(t => t.Symbol).Should().Contain("WEGE3");
        // setor do neutro (Industrial) sem cobertura → some; nada novo comprado lá
        result.Summary.SectorWeightsAfter.Should().NotContainKey("Industrial");

        // validação: soma 100%
        result.Summary.WeightsValid.Should().BeTrue();
        result.Summary.TotalWeightPct.Should().BeApproximately(100.0, 0.05);

        // peso do setor Bancos preservado (ITUB4 20% + BBDC4 15% = 35% antes; ITUB4 + BBAS3 = 35% depois)
        result.Summary.SectorWeightsAfter["Bancos"].Should().BeApproximately(35.0, 0.05);
        result.Summary.SectorWeightsAfter["Petroleo"].Should().BeApproximately(25.0, 0.05);

        // sugestões classificadas por importância (1 = mais importante)
        result.Swaps.Should().BeInDescendingOrder(s => s.Priority);
        result.Swaps.Single(s => s.PriorityRank == 1).SellSymbol.Should().Be("PETR4");
        result.Swaps.Single(s => s.SellSymbol == "WEGE3").PriorityRank
            .Should().BeGreaterThan(result.Swaps.Single(s => s.SellSymbol == "MGLU3").PriorityRank);

        // estratégia em múltiplos de 5: toda quantidade negociada é múltiplo de 5
        result.Trades.Should().OnlyContain(t => t.Quantity % 5 == 0 && t.Quantity > 0);
        result.Trades.Where(t => t.Side == "BUY").Should().OnlyContain(t => t.Volume == t.Quantity * t.Price);
    }

    [Fact]
    public async Task ProhibitedSector_GoesToReserve_NeverBought()
    {
        var result = await Build().SuggestAsync(
            "cli_swap_1", new SwapFilters { ProhibitedSectors = ["Petroleo"] });

        // Petroleo proibido: PETR4 não vira swap intra-setor → reserva; PRIO3 nunca comprado
        result.Trades.Where(t => t.Side == "BUY").Select(t => t.Symbol).Should().NotContain("PRIO3");
        result.Summary.SectorWeightsAfter.Should().NotContainKey("Petroleo");
        result.Summary.WeightsValid.Should().BeTrue();
    }
}
