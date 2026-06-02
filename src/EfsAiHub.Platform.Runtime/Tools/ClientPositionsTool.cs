using System.ComponentModel;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace EfsAiHub.Platform.Runtime.Tools;

/// <summary>
/// Tools de consulta de posições — versão mock. Expõe duas funções:
/// <list type="bullet">
///   <item><c>get_portfolio</c>: carteira completa do cliente.</item>
///   <item><c>get_position</c>: posição de um único ticker (ou null).</item>
/// </list>
///
/// Quando ligar nas APIs reais, mesmo padrão de <see cref="PortfolioAnalysisTool"/>:
/// injetar <c>IHttpClientFactory</c> + <c>IOptions&lt;PortfolioApiOptions&gt;</c> +
/// <c>IRequestAuthContextAccessor</c> e substituir os corpos dos métodos privados
/// <see cref="LookupAsync"/>.
/// </summary>
public sealed class ClientPositionsTool
{
    private readonly ILogger<ClientPositionsTool> _logger;

    public ClientPositionsTool(ILogger<ClientPositionsTool> logger)
    {
        _logger = logger;
    }

    [Description(
        "Retorna a carteira completa de posições do cliente. " +
        "Input: account (identificador do cliente). " +
        "Output: lista de objetos {account, symbol, totalQuantity, volume}. " +
        "Lista vazia significa que o cliente não tem posições em carteira. " +
        "Use quando o usuário da mesa pedir 'a carteira', 'as posições', 'a alocação' do cliente — qualquer pergunta sem ticker específico.")]
    public Task<List<ClientPosition>> GetPortfolioAsync(
        [Description("Identificador da conta do cliente (account).")] string account,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(account))
            throw new ArgumentException("account é obrigatório.", nameof(account));

        _logger.LogDebug("[get_portfolio] MOCK account={Account}", account);

        var positions = LookupAccount(account);
        return Task.FromResult(positions);
    }

    [Description(
        "Retorna a posição de um único ativo na carteira do cliente. " +
        "Input: account (identificador do cliente) e ticker (símbolo do ativo, ex.: PETR4). " +
        "Output: objeto {account, symbol, totalQuantity, volume} quando o cliente possui o ativo, ou null quando não possui. " +
        "Use quando a mensagem mencionar um ticker específico. Se a mensagem citar 2+ tickers, chame esta tool uma vez por ticker.")]
    public Task<ClientPosition?> GetPositionAsync(
        [Description("Identificador da conta do cliente (account).")] string account,
        [Description("Símbolo do ativo. Case-insensitive (ex.: PETR4, vale3).")] string ticker,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(account))
            throw new ArgumentException("account é obrigatório.", nameof(account));
        if (string.IsNullOrWhiteSpace(ticker))
            throw new ArgumentException("ticker é obrigatório.", nameof(ticker));

        var normalized = ticker.Trim().ToUpperInvariant();
        _logger.LogDebug("[get_position] MOCK account={Account} ticker={Ticker}", account, normalized);

        var match = LookupAccount(account)
            .FirstOrDefault(p => string.Equals(p.Symbol, normalized, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult<ClientPosition?>(match);
    }

    // Mock dataset. Hoje 3 accounts conhecidos cobrem cenários distintos (multi-ativos,
    // poucos ativos, conta vazia). Qualquer outro account devolve lista vazia — útil
    // pra exercitar o branch "carteira vazia" da persona sem precisar de fixtures.
    private static List<ClientPosition> LookupAccount(string account) =>
        account.Trim().ToLowerInvariant() switch
        {
            "cli_8421" =>
            [
                new ClientPosition(account, "PETR4", 1000m, 38500.00m),
                new ClientPosition(account, "VALE3",  500m, 32750.00m),
                new ClientPosition(account, "ITUB4",  800m, 26400.00m),
                new ClientPosition(account, "XPTO3", 2000m, 18000.00m),
            ],
            "cli_4242" =>
            [
                new ClientPosition(account, "WEGE3",  300m, 15500.00m),
                new ClientPosition(account, "ABEV3", 1200m, 18240.00m),
            ],
            _ => [],
        };
}

public sealed record ClientPosition(
    [property: JsonPropertyName("account")]       string Account,
    [property: JsonPropertyName("symbol")]        string Symbol,
    [property: JsonPropertyName("totalQuantity")] decimal TotalQuantity,
    [property: JsonPropertyName("volume")]        decimal Volume);
