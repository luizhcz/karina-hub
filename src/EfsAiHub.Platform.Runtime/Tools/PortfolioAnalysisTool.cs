using System.ComponentModel;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using EfsAiHub.Core.Abstractions.Users;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Platform.Runtime.Tools;

/// <summary>
/// Tool <c>analyze_portfolio</c>: cruza posições do cliente (positions API)
/// com recomendações do research (assets API) e devolve uma estrutura pronta
/// para o agente sintetizar a análise — sem ter de cruzar listas nem fazer
/// aritmética por conta própria.
///
/// As duas APIs externas exigem os headers <c>app_origin</c> e
/// <c>access_token</c> (mesmo contrato da GenericTool), lidos do
/// <see cref="IRequestAuthContextAccessor"/>. URLs/timeouts vêm de
/// <see cref="PortfolioApiOptions"/> (seção <c>PortfolioApi</c> do
/// appsettings). Enquanto <c>BaseUrl</c> não estiver preenchido, qualquer
/// invocação falha com mensagem orientando a ligação.
/// </summary>
public sealed class PortfolioAnalysisTool
{
    public const string HttpClientName = "portfolio-api";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IRequestAuthContextAccessor _authContext;
    private readonly IOptions<PortfolioApiOptions> _options;
    private readonly ILogger<PortfolioAnalysisTool> _logger;

    public PortfolioAnalysisTool(
        IHttpClientFactory httpClientFactory,
        IRequestAuthContextAccessor authContext,
        IOptions<PortfolioApiOptions> options,
        ILogger<PortfolioAnalysisTool> logger)
    {
        _httpClientFactory = httpClientFactory;
        _authContext = authContext;
        _options = options;
        _logger = logger;
    }

    [Description(
        "Analisa a carteira de um cliente cruzando posições com recomendações do research. " +
        "Para cada ativo retorna recommendation (compra/venda/neutro/null), isTopPick e o " +
        "alignment calculado (aligned/misaligned/neutral/uncovered), além do weight (peso na carteira) " +
        "e agregados por alignment. Use sempre que o usuário pedir análise da carteira de uma conta específica.")]
    public async Task<PortfolioAnalysisResult> AnalyzePortfolioAsync(
        [Description("Identificador da conta do cliente (account).")] string account,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(account))
            throw new ArgumentException("account é obrigatório.", nameof(account));

        var options = _options.Value;
        EnsureConfigured(options);

        var appOrigin = _authContext.AppOrigin;
        var accessToken = _authContext.AccessToken;

        var positions = await FetchPositionsAsync(account, appOrigin, accessToken, options, cancellationToken)
            .ConfigureAwait(false);

        if (positions.Count == 0)
            return BuildEmpty(account);

        var symbols = positions
            .Select(p => p.Symbol)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var recommendations = await FetchAssetRecommendationsAsync(
            symbols, appOrigin, accessToken, options, cancellationToken).ConfigureAwait(false);

        return BuildResult(account, positions, recommendations);
    }

    // Positions API — input: account; output: [{account, symbol, totalQuantity, volume}, ...].
    // TODO(integração): confirmar method/path/shape exatos com o provedor.
    // Suposição atual: GET {BaseUrl}{PositionsPath}?account={account}.
    private async Task<List<PositionDto>> FetchPositionsAsync(
        string account,
        string? appOrigin,
        string? accessToken,
        PortfolioApiOptions options,
        CancellationToken ct)
    {
        var path = $"{options.PositionsPath}?account={Uri.EscapeDataString(account)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        ApplyAuthHeaders(request, appOrigin, accessToken);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

        var client = _httpClientFactory.CreateClient(HttpClientName);

        _logger.LogDebug(
            "[analyze_portfolio] positions request account={Account} appOrigin={AppOrigin} hasToken={HasToken}",
            account, appOrigin ?? "<null>", !string.IsNullOrEmpty(accessToken));

        using var response = await client.SendAsync(request, cts.Token).ConfigureAwait(false);
        EnsureAuthorized(response, "posições");
        response.EnsureSuccessStatusCode();

        var positions = await response.Content
            .ReadFromJsonAsync<List<PositionDto>>(cancellationToken: cts.Token)
            .ConfigureAwait(false);

        return positions ?? [];
    }

    // Assets API — input: lista de symbols; output: [{symbol, recommendation, inTopPicks}, ...].
    // TODO(integração): confirmar method/path/shape exatos com o provedor.
    // Suposição atual: POST {BaseUrl}{RecommendationsPath} com body {"symbols":[...]}.
    private async Task<Dictionary<string, AssetRecommendationDto>> FetchAssetRecommendationsAsync(
        IReadOnlyList<string> symbols,
        string? appOrigin,
        string? accessToken,
        PortfolioApiOptions options,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, options.RecommendationsPath)
        {
            Content = JsonContent.Create(new { symbols }),
        };
        ApplyAuthHeaders(request, appOrigin, accessToken);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

        var client = _httpClientFactory.CreateClient(HttpClientName);

        _logger.LogDebug(
            "[analyze_portfolio] recommendations request symbols={Symbols} appOrigin={AppOrigin} hasToken={HasToken}",
            string.Join(",", symbols), appOrigin ?? "<null>", !string.IsNullOrEmpty(accessToken));

        using var response = await client.SendAsync(request, cts.Token).ConfigureAwait(false);
        EnsureAuthorized(response, "recomendações");
        response.EnsureSuccessStatusCode();

        var items = await response.Content
            .ReadFromJsonAsync<List<AssetRecommendationDto>>(cancellationToken: cts.Token)
            .ConfigureAwait(false);

        var result = new Dictionary<string, AssetRecommendationDto>(StringComparer.OrdinalIgnoreCase);
        if (items is null) return result;

        foreach (var item in items)
        {
            if (!string.IsNullOrWhiteSpace(item.Symbol))
                result[item.Symbol] = item;
        }
        return result;
    }

    // Forward dos headers de auth do caller, mesmo contrato da GenericTool:
    // só propaga quando presente — request sem header deixa o downstream decidir.
    private static void ApplyAuthHeaders(HttpRequestMessage request, string? appOrigin, string? accessToken)
    {
        if (!string.IsNullOrWhiteSpace(appOrigin))
            request.Headers.TryAddWithoutValidation("app_origin", appOrigin);
        if (!string.IsNullOrWhiteSpace(accessToken))
            request.Headers.TryAddWithoutValidation("access_token", accessToken);
    }

    // 401/403 do downstream = caller sem permissão na API real. Surface como
    // mensagem amigável pro agente repassar ao usuário sem vazar detalhes do
    // backend (mesmo critério do GenericToolExecutor).
    private void EnsureAuthorized(HttpResponseMessage response, string recursoLabel)
    {
        if (response.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden))
            return;

        _logger.LogWarning(
            "[analyze_portfolio] downstream barrou caller (status {Status}) ao buscar {Recurso}.",
            (int)response.StatusCode, recursoLabel);

        throw new InvalidOperationException(
            $"Você não tem permissão para consultar {recursoLabel} deste cliente.");
    }

    private static void EnsureConfigured(PortfolioApiOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
            throw new InvalidOperationException(
                "PortfolioApi:BaseUrl não configurada. Defina a seção 'PortfolioApi' no appsettings " +
                "antes de invocar 'analyze_portfolio'.");
    }

    private static PortfolioAnalysisResult BuildResult(
        string account,
        IReadOnlyList<PositionDto> positions,
        IReadOnlyDictionary<string, AssetRecommendationDto> recommendations)
    {
        var totalVolume = positions.Sum(p => p.Volume);

        var rows = new List<PortfolioPositionRow>(positions.Count);
        foreach (var p in positions)
        {
            recommendations.TryGetValue(p.Symbol, out var rec);
            var alignment = ClassifyAlignment(rec?.Recommendation);
            var weight = totalVolume > 0 ? (double)(p.Volume / totalVolume) : 0d;

            rows.Add(new PortfolioPositionRow
            {
                Ticker = p.Symbol,
                Quantidade = p.TotalQuantity,
                Volume = p.Volume,
                Weight = Math.Round(weight, 4),
                Recommendation = rec?.Recommendation,
                IsTopPick = rec?.InTopPicks ?? false,
                Alignment = alignment,
            });
        }

        return new PortfolioAnalysisResult
        {
            AsOf = DateTime.UtcNow,
            Client = new ClientInfo
            {
                Id = account,
                TotalVolume = totalVolume,
                Currency = "BRL",
            },
            Positions = rows,
            Summary = BuildSummary(rows, totalVolume),
        };
    }

    private static PortfolioAnalysisResult BuildEmpty(string account) => new()
    {
        AsOf = DateTime.UtcNow,
        Client = new ClientInfo { Id = account, TotalVolume = 0m, Currency = "BRL" },
        Positions = [],
        Summary = new SummaryInfo
        {
            PositionsCount = 0,
            ByAlignment = new Dictionary<string, AlignmentBucket>(),
            TopPicksHeld = [],
        },
    };

    private static SummaryInfo BuildSummary(IReadOnlyList<PortfolioPositionRow> rows, decimal totalVolume)
    {
        var byAlignment = rows
            .GroupBy(r => r.Alignment, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var volume = g.Sum(r => r.Volume);
                    return new AlignmentBucket
                    {
                        Count = g.Count(),
                        Volume = volume,
                        WeightPct = totalVolume > 0 ? Math.Round((double)(volume / totalVolume), 4) : 0d,
                    };
                });

        var topPicksHeld = rows
            .Where(r => r.IsTopPick)
            .Select(r => r.Ticker)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new SummaryInfo
        {
            PositionsCount = rows.Count,
            ByAlignment = byAlignment,
            TopPicksHeld = topPicksHeld,
        };
    }

    private static string ClassifyAlignment(string? recommendation) =>
        recommendation?.Trim().ToLowerInvariant() switch
        {
            "compra" => "aligned",
            "venda"  => "misaligned",
            "neutro" => "neutral",
            _        => "uncovered",
        };
}

internal sealed record PositionDto(
    [property: JsonPropertyName("account")]       string Account,
    [property: JsonPropertyName("symbol")]        string Symbol,
    [property: JsonPropertyName("totalQuantity")] decimal TotalQuantity,
    [property: JsonPropertyName("volume")]        decimal Volume);

internal sealed record AssetRecommendationDto(
    [property: JsonPropertyName("symbol")]         string Symbol,
    [property: JsonPropertyName("recommendation")] string Recommendation,
    [property: JsonPropertyName("inTopPicks")]     bool InTopPicks);

public sealed class PortfolioAnalysisResult
{
    [JsonPropertyName("asOf")]
    public DateTime AsOf { get; init; }

    [JsonPropertyName("client")]
    public ClientInfo Client { get; init; } = new();

    [JsonPropertyName("positions")]
    public List<PortfolioPositionRow> Positions { get; init; } = [];

    [JsonPropertyName("summary")]
    public SummaryInfo Summary { get; init; } = new();
}

public sealed class ClientInfo
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("totalVolume")]
    public decimal TotalVolume { get; init; }

    [JsonPropertyName("currency")]
    public string Currency { get; init; } = "BRL";
}

public sealed class PortfolioPositionRow
{
    [JsonPropertyName("ticker")]
    public string Ticker { get; init; } = string.Empty;

    [JsonPropertyName("quantidade")]
    public decimal Quantidade { get; init; }

    [JsonPropertyName("volume")]
    public decimal Volume { get; init; }

    [JsonPropertyName("weight")]
    public double Weight { get; init; }

    [JsonPropertyName("recommendation")]
    public string? Recommendation { get; init; }

    [JsonPropertyName("isTopPick")]
    public bool IsTopPick { get; init; }

    [JsonPropertyName("alignment")]
    public string Alignment { get; init; } = string.Empty;
}

public sealed class SummaryInfo
{
    [JsonPropertyName("positionsCount")]
    public int PositionsCount { get; init; }

    [JsonPropertyName("byAlignment")]
    public Dictionary<string, AlignmentBucket> ByAlignment { get; init; } = new();

    [JsonPropertyName("topPicksHeld")]
    public List<string> TopPicksHeld { get; init; } = [];
}

public sealed class AlignmentBucket
{
    [JsonPropertyName("count")]
    public int Count { get; init; }

    [JsonPropertyName("volume")]
    public decimal Volume { get; init; }

    [JsonPropertyName("weightPct")]
    public double WeightPct { get; init; }
}
