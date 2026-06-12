using Microsoft.Extensions.Logging;

namespace EfsAiHub.Platform.Runtime.Tools.SwapSuggestion;

// ─────────────────────────────────────────────────────────────────────────────
// Os 3 endpoints como interfaces. Hoje: mocks in-memory. Depois: clients HTTP
// (mesmo padrão de PortfolioAnalysisTool — IHttpClientFactory + auth headers),
// trocando só o registro na DI. O engine não muda.
// ─────────────────────────────────────────────────────────────────────────────

public interface IPositionEndpoint
{
    Task<List<SwapPosition>> GetPositionsAsync(string account, CancellationToken ct = default);
}

public interface IAssetEndpoint
{
    Task<List<SwapAsset>> GetAssetsAsync(CancellationToken ct = default);
}

public interface IRecommendationEndpoint
{
    Task<List<SwapRecommendation>> GetRecommendationsAsync(CancellationToken ct = default);
}

// ─────────────────────────────────────────────────────────────────────────────
// Mock — endpoint de posição. Cenário 'cli_swap_1' (carteira de 100k) cobre
// todos os ramos da lógica; 'cli_swap_2' é simples; qualquer outro = vazio.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class MockPositionEndpoint : IPositionEndpoint
{
    private readonly ILogger<MockPositionEndpoint> _logger;
    public MockPositionEndpoint(ILogger<MockPositionEndpoint> logger) => _logger = logger;

    public Task<List<SwapPosition>> GetPositionsAsync(string account, CancellationToken ct = default)
    {
        _logger.LogDebug("[mock:posicao] account={Account}", account);
        List<SwapPosition> positions = account.Trim().ToLowerInvariant() switch
        {
            "cli_swap_1" =>
            [
                new(account, "ITUB4",  800m, 20_000m), // banks   · compra        → mantém
                new(account, "VALE3",  300m, 15_000m), // mining  · compra+toppick → mantém
                new(account, "WEGE3",  200m, 10_000m), // indust. · neutro         → mantém (default)
                new(account, "BBDC4", 1000m, 15_000m), // banks   · venda          → swap intra-setor (BBAS3)
                new(account, "PETR4",  700m, 25_000m), // oil     · venda          → swap intra-setor (PRIO3)
                new(account, "MGLU3", 5000m, 15_000m), // retail  · semcobertura   → reserva (setor sem top pick)
            ],
            "cli_swap_2" =>
            [
                new(account, "BBDC4", 1000m, 30_000m), // banks   · venda → swap (BBAS3)
                new(account, "PETR4",  700m, 70_000m), // oil     · venda → swap (PRIO3)
            ],
            _ => [],
        };
        return Task.FromResult(positions);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Mock — endpoint de ativo (catálogo: symbol, nome, securityType, sector).
// ─────────────────────────────────────────────────────────────────────────────

public sealed class MockAssetEndpoint : IAssetEndpoint
{
    public Task<List<SwapAsset>> GetAssetsAsync(CancellationToken ct = default) =>
        Task.FromResult(new List<SwapAsset>
        {
            new("ITUB4", "Itaú Unibanco PN",   "Acao", "Bancos",     25.00m),
            new("BBDC4", "Bradesco PN",        "Acao", "Bancos",     15.00m),
            new("BBAS3", "Banco do Brasil ON", "Acao", "Bancos",     28.00m),
            new("VALE3", "Vale ON",            "Acao", "Mineracao",  50.00m),
            new("PETR4", "Petrobras PN",       "Acao", "Petroleo",   36.00m),
            new("PRIO3", "PRIO ON",            "Acao", "Petroleo",   45.00m),
            new("WEGE3", "WEG ON",             "Acao", "Industrial", 50.00m),
            new("MGLU3", "Magazine Luiza ON",  "Acao", "Varejo",     12.00m),
            new("RADL3", "Raia Drogasil ON",   "Acao", "Saude",      30.00m),
            new("TOTS3", "Totvs ON",           "Acao", "Tecnologia", 33.00m),
        });
}

// ─────────────────────────────────────────────────────────────────────────────
// Mock — endpoint de recomendação (lista da casa). potential = upside %.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class MockRecommendationEndpoint : IRecommendationEndpoint
{
    public Task<List<SwapRecommendation>> GetRecommendationsAsync(CancellationToken ct = default) =>
        Task.FromResult(new List<SwapRecommendation>
        {
            // possuídos
            new("ITUB4", "Bancos",     Rec.Compra,       12m, false),
            new("VALE3", "Mineracao",  Rec.Compra,       18m, true),
            new("WEGE3", "Industrial", Rec.Neutro,        8m, false),
            new("BBDC4", "Bancos",     Rec.Venda,        -5m, false),
            new("PETR4", "Petroleo",   Rec.Venda,         2m, false),
            new("MGLU3", "Varejo",     Rec.SemCobertura,  0m, false),
            // top picks NÃO possuídos (candidatos)
            new("BBAS3", "Bancos",     Rec.Compra,       25m, true),  // top pick do setor Bancos
            new("PRIO3", "Petroleo",   Rec.Compra,       30m, true),  // top pick do setor Petroleo
            new("RADL3", "Saude",      Rec.Compra,       28m, true),  // top pick global (setor não possuído)
            new("TOTS3", "Tecnologia", Rec.Compra,       22m, true),  // top pick global (setor não possuído)
        });
}
