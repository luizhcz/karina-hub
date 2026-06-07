namespace EfsAiHub.Core.Abstractions.Observability;

/// <summary>
/// Agregados de decisões de Router por projeto. Fonte: <c>workflow_event_audit</c>
/// filtrado em <c>EventType='node_completed'</c> + payload com
/// <c>agentType='Router'</c>. O output do Router já é JSON estruturado
/// (<c>{intent, confidence, reason, candidate_intents}</c>) — extraído
/// via cast jsonb dentro da query. Sem persistência nova; analytics opera
/// sobre o que o telemetria já loga.
///
/// Limitação conhecida: ~3% dos outputs vêm prefixados com "Assistant: ..."
/// (chain-of-thought de alguns provedores) e são filtrados fora da query.
/// Sub-amostragem em troca de robustez — analytics agregado tolera.
/// </summary>
public sealed class RouterDecisionOverview
{
    public required string ProjectId { get; init; }
    public DateTime PeriodFrom { get; init; }
    public DateTime PeriodTo { get; init; }

    /// <summary>Total de decisões parseáveis no período.</summary>
    public long TotalDecisions { get; init; }

    /// <summary>Quantidade de intents distintos vistos.</summary>
    public int DistinctIntents { get; init; }

    /// <summary>Confiança média (0..1) entre todas as decisões.</summary>
    public double AvgConfidence { get; init; }

    /// <summary>p50 da confiança (0..1).</summary>
    public double P50Confidence { get; init; }

    /// <summary>
    /// Decisões com confidence &lt; 0.5 — sinaliza ambiguidade ou modelo
    /// inseguro. Ratio = LowConfidence / TotalDecisions.
    /// </summary>
    public long LowConfidenceCount { get; init; }
    public double LowConfidenceRate { get; init; }

    /// <summary>
    /// Decisões com intent em <c>{out_of_scope, needs_clarification, loop_guard}</c>
    /// — fora de escopo, pediu esclarecimento, ou guardrail anti-loop bateu.
    /// Ratio = AmbiguityCount / TotalDecisions.
    /// </summary>
    public long AmbiguityCount { get; init; }
    public double AmbiguityRate { get; init; }

    public required IReadOnlyList<RouterIntentStats> Intents { get; init; }
}

/// <summary>Linha por intent decidido. Ordenada por count DESC.</summary>
public sealed class RouterIntentStats
{
    public required string Intent { get; init; }
    public long Count { get; init; }
    public double AvgConfidence { get; init; }
    public double P50Confidence { get; init; }
    public double P95Confidence { get; init; }
    public double MinConfidence { get; init; }
}

public sealed class RouterDecisionTimeseriesBucket
{
    public DateTime Bucket { get; init; }
    public long Total { get; init; }
    public double AvgConfidence { get; init; }
    public long LowConfidenceCount { get; init; }
    public long AmbiguityCount { get; init; }
}
