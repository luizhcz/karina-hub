using System.Text.Json.Serialization;

namespace EfsAiHub.Platform.Runtime.Tools.SwapSuggestion;

// ─────────────────────────────────────────────────────────────────────────────
// DTOs dos 3 endpoints (hoje mockados; viram clients HTTP depois, mesmo padrão
// de PortfolioAnalysisTool). Os nomes de campo espelham os contratos descritos.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Endpoint 1 — posição: a carteira do cliente.</summary>
public sealed record SwapPosition(
    [property: JsonPropertyName("account")]       string Account,
    [property: JsonPropertyName("symbol")]        string Symbol,
    [property: JsonPropertyName("totalQuantity")] decimal TotalQuantity,
    [property: JsonPropertyName("volume")]        decimal Volume);

/// <summary>
/// Endpoint 2 — ativo: catálogo. <c>priceAtual</c> é necessário pra converter
/// volume (R$) em quantidade (lotes) — a estratégia trabalha em múltiplos de 5.
/// </summary>
public sealed record SwapAsset(
    [property: JsonPropertyName("symbol")]       string Symbol,
    [property: JsonPropertyName("nome")]         string Nome,
    [property: JsonPropertyName("securityType")] string SecurityType,
    [property: JsonPropertyName("sector")]       string Sector,
    [property: JsonPropertyName("priceAtual")]   decimal PriceAtual);

/// <summary>
/// Endpoint 3 — recomendação da casa. <c>recomendacao</c> ∈
/// {compra, venda, neutro, semcobertura}. <c>potential</c> = upside esperado (ranking).
/// </summary>
public sealed record SwapRecommendation(
    [property: JsonPropertyName("symbol")]      string Symbol,
    [property: JsonPropertyName("sector")]      string Sector,
    [property: JsonPropertyName("recomendacao")] string Recomendacao,
    [property: JsonPropertyName("potential")]   decimal Potential,
    [property: JsonPropertyName("inTopPicks")]  bool InTopPicks);

/// <summary>Valores canônicos de <c>recomendacao</c>.</summary>
public static class Rec
{
    public const string Compra = "compra";
    public const string Venda = "venda";
    public const string Neutro = "neutro";
    public const string SemCobertura = "semcobertura";

    public static string Normalize(string? r) =>
        (r ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "compra" => Compra,
            "venda" => Venda,
            "neutro" => Neutro,
            "semcobertura" or "sem cobertura" or "" or null => SemCobertura,
            var other => other,
        };
}

// ─────────────────────────────────────────────────────────────────────────────
// Filtros opcionais do tool.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class SwapFilters
{
    /// <summary>Setores proibidos: posições neles são vendidas e nunca compradas.</summary>
    public IReadOnlyList<string> ProhibitedSectors { get; init; } = [];

    /// <summary>
    /// Mantém <c>neutro</c> na carteira. Default false: neutro também recebe
    /// sugestão de troca (só <c>compra</c> é mantido).
    /// </summary>
    public bool KeepNeutral { get; init; }

    /// <summary>Máximo de nomes que recebem a reserva (ranking global). 0 = sem teto.</summary>
    public int MaxReserveNames { get; init; } = 3;

    /// <summary>Distribui a reserva proporcional ao potential (default) ou em peso igual.</summary>
    public bool ReserveEqualWeight { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Resultado: lista de trades (1×n) + plano + sumário.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class SwapSuggestionResult
{
    [JsonPropertyName("account")] public string Account { get; init; } = string.Empty;
    [JsonPropertyName("asOf")] public DateTime AsOf { get; init; }
    [JsonPropertyName("totalVolume")] public decimal TotalVolume { get; init; }

    /// <summary>Plano de troca: cada item é 1 venda → N compras (a "lista 1×n").</summary>
    [JsonPropertyName("swaps")] public List<SwapPlanItem> Swaps { get; init; } = [];

    /// <summary>Trades achatados (todas as vendas e compras), prontos pra boletar.</summary>
    [JsonPropertyName("trades")] public List<TradeAction> Trades { get; init; } = [];

    [JsonPropertyName("summary")] public SwapSummary Summary { get; init; } = new();
}

/// <summary>Uma venda e os destinos (1..N) que recebem o peso liberado.</summary>
public sealed class SwapPlanItem
{
    /// <summary>Ordem de importância (1 = mais importante). Ver Priority.</summary>
    [JsonPropertyName("priorityRank")] public int PriorityRank { get; set; }
    /// <summary>Score de importância = peso% × urgência × (1 + potential/100).</summary>
    [JsonPropertyName("priority")] public double Priority { get; init; }
    /// <summary>Urgência da venda: proibido | venda | semcobertura | neutro.</summary>
    [JsonPropertyName("severity")] public string Severity { get; init; } = string.Empty;
    [JsonPropertyName("sellSymbol")] public string SellSymbol { get; init; } = string.Empty;
    [JsonPropertyName("sellSector")] public string SellSector { get; init; } = string.Empty;
    [JsonPropertyName("weightPct")] public double WeightPct { get; init; }
    [JsonPropertyName("volume")] public decimal Volume { get; init; }
    [JsonPropertyName("reason")] public string Reason { get; init; } = string.Empty;
    [JsonPropertyName("targets")] public List<SwapTarget> Targets { get; init; } = [];
}

public sealed class SwapTarget
{
    [JsonPropertyName("symbol")] public string Symbol { get; init; } = string.Empty;
    [JsonPropertyName("nome")] public string Nome { get; init; } = string.Empty;
    [JsonPropertyName("sector")] public string Sector { get; init; } = string.Empty;
    [JsonPropertyName("weightPct")] public double WeightPct { get; init; }
    [JsonPropertyName("volume")] public decimal Volume { get; init; }
    [JsonPropertyName("potential")] public decimal Potential { get; init; }
    /// <summary>intra_sector (mantém peso do setor) ou reserve (ranking global).</summary>
    [JsonPropertyName("kind")] public string Kind { get; init; } = string.Empty;
}

public sealed class TradeAction
{
    [JsonPropertyName("side")] public string Side { get; init; } = string.Empty; // SELL | BUY
    [JsonPropertyName("symbol")] public string Symbol { get; init; } = string.Empty;
    [JsonPropertyName("nome")] public string Nome { get; init; } = string.Empty;
    [JsonPropertyName("sector")] public string Sector { get; init; } = string.Empty;
    [JsonPropertyName("weightPct")] public double WeightPct { get; init; }
    /// <summary>Quantidade de ativos (lotes), sempre múltiplo de 5.</summary>
    [JsonPropertyName("quantity")] public decimal Quantity { get; init; }
    [JsonPropertyName("price")] public decimal Price { get; init; }
    /// <summary>Volume executado = quantity × price (R$).</summary>
    [JsonPropertyName("volume")] public decimal Volume { get; init; }
    [JsonPropertyName("reason")] public string Reason { get; init; } = string.Empty;
}

public sealed class SwapSummary
{
    [JsonPropertyName("keptWeightPct")] public double KeptWeightPct { get; init; }
    [JsonPropertyName("intraSectorWeightPct")] public double IntraSectorWeightPct { get; init; }
    [JsonPropertyName("reserveWeightPct")] public double ReserveWeightPct { get; init; }
    [JsonPropertyName("sectorWeightsBefore")] public Dictionary<string, double> SectorWeightsBefore { get; init; } = new();
    [JsonPropertyName("sectorWeightsAfter")] public Dictionary<string, double> SectorWeightsAfter { get; init; } = new();
    /// <summary>true se a soma dos pesos finais ≈ 100% (tolerância de arredondamento).</summary>
    [JsonPropertyName("weightsValid")] public bool WeightsValid { get; init; }
    [JsonPropertyName("totalWeightPct")] public double TotalWeightPct { get; init; }
    /// <summary>Caixa residual (R$) do arredondamento de quantidade ao múltiplo de 5.</summary>
    [JsonPropertyName("cashResidual")] public decimal CashResidual { get; init; }
    [JsonPropertyName("notes")] public List<string> Notes { get; init; } = [];
}
