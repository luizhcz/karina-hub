using System.ComponentModel;
using Microsoft.Extensions.Logging;

namespace EfsAiHub.Platform.Runtime.Tools.SwapSuggestion;

/// <summary>
/// Tool <c>suggest_swaps</c>: sugere trocas de ativos da carteira do cliente,
/// alinhando à visão da casa (vende venda/semcobertura, substitui por top picks
/// mantendo o peso do setor, redistribui a reserva por ranking global). Hoje roda
/// sobre os 3 endpoints mockados; trocar os mocks por clients HTTP na DI não muda
/// nem o tool nem o engine.
/// </summary>
public sealed class SwapSuggestionTool
{
    private readonly SwapSuggestionEngine _engine;
    private readonly ILogger<SwapSuggestionTool> _logger;

    public SwapSuggestionTool(SwapSuggestionEngine engine, ILogger<SwapSuggestionTool> logger)
    {
        _engine = engine;
        _logger = logger;
    }

    [Description(
        "Sugere trocas de ativos na carteira de um cliente, alinhando-a às recomendações da casa. " +
        "Vende ativos recomendados como venda ou sem cobertura, substitui por top picks do mesmo setor " +
        "(preservando o peso do setor) e redistribui o restante (setores sem cobertura) nos melhores top " +
        "picks globais por potential. Retorna um plano de trades (cada venda → 1..N compras) somando 100%. " +
        "Use quando o usuário pedir sugestão de troca, rebalanceamento ou alinhamento da carteira ao research.")]
    public Task<SwapSuggestionResult> SuggestSwapsAsync(
        [Description("Conta do cliente (obrigatório).")] string account,
        [Description("Setores proibidos, separados por vírgula (opcional). Posições neles são vendidas e nunca compradas.")] string? prohibitedSectors = null,
        [Description("Manter 'neutro' na carteira (opcional, default false — neutro também recebe sugestão de troca).")] bool keepNeutral = false,
        [Description("Máximo de nomes que recebem a reserva por ranking global (opcional, default 3).")] int maxReserveNames = 3,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(account))
            throw new ArgumentException("account é obrigatório.", nameof(account));

        var filters = new SwapFilters
        {
            ProhibitedSectors = string.IsNullOrWhiteSpace(prohibitedSectors)
                ? []
                : prohibitedSectors.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            KeepNeutral = keepNeutral,
            MaxReserveNames = maxReserveNames,
        };

        _logger.LogDebug("[suggest_swaps] account={Account} prohibited={Prohibited} keepNeutral={KeepNeutral}",
            account, prohibitedSectors ?? "<none>", keepNeutral);

        return _engine.SuggestAsync(account, filters, cancellationToken);
    }
}
