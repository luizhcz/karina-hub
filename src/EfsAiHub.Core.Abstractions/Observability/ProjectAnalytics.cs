namespace EfsAiHub.Core.Abstractions.Observability;

/// <summary>
/// Resumo agregado de uso/custo do projeto num intervalo. Alimenta o card
/// principal do dashboard (overview MTD).
/// </summary>
public sealed class ProjectOverview
{
    public required string ProjectId { get; init; }
    public required DateTime PeriodFrom { get; init; }
    public required DateTime PeriodTo { get; init; }
    public required decimal TotalCostUsd { get; init; }
    public required long TotalTokens { get; init; }
    public required int TotalCalls { get; init; }
    public required int TotalExecutions { get; init; }
    public required int Completed { get; init; }
    public required int Failed { get; init; }
    /// <summary>Sucesso/(Sucesso+Falha). Quando TotalExecutions==0 retorna 0.</summary>
    public required double SuccessRate { get; init; }
    public required IReadOnlyList<AgentMiniRow> TopAgents { get; init; }
}

/// <summary>Linha compacta usada em listas top-N (overview.topAgents).</summary>
public sealed class AgentMiniRow
{
    public required string AgentId { get; init; }
    public required long TotalTokens { get; init; }
    public required decimal CostUsd { get; init; }
    public required int Calls { get; init; }
}

/// <summary>Bucket temporal do gráfico de série (custo + execuções).</summary>
public sealed class ProjectTimeseriesBucket
{
    public required DateTime Bucket { get; init; }
    public required decimal CostUsd { get; init; }
    public required long Tokens { get; init; }
    public required int Calls { get; init; }
    public required int Executions { get; init; }
    public required int Completed { get; init; }
    public required int Failed { get; init; }
}

/// <summary>Linha da tabela de breakdown por agente do projeto.</summary>
public sealed class ProjectAgentBreakdown
{
    public required string AgentId { get; init; }
    public string? AgentName { get; init; }
    public string? ModelId { get; init; }
    public required int Calls { get; init; }
    public required long TotalTokens { get; init; }
    public required decimal CostUsd { get; init; }
    public required double AvgDurationMs { get; init; }
    public required double P95DurationMs { get; init; }
    /// <summary>Falhas/(Sucessos+Falhas). 0 quando sem execuções.</summary>
    public required double ErrorRate { get; init; }
}

/// <summary>
/// Estado do orçamento diário do projeto. Lê limites de ProjectSettings e
/// consumo do dia em Redis (já populado pelo ProjectBudgetGuard).
/// Hoje é warning-only no runtime — a UI deve sinalizar `exceeded:true` mas
/// avisar que a plataforma não bloqueia execução.
/// </summary>
public sealed class ProjectBudgetStatus
{
    public required string ProjectId { get; init; }
    public int? MaxTokensPerDay { get; init; }
    public decimal? MaxCostUsdPerDay { get; init; }
    public required long TodayTokens { get; init; }
    public required decimal TodayCostUsd { get; init; }
    /// <summary>0..1 (ou >1 quando estourou). Null quando o limite não está configurado.</summary>
    public double? TokensUsagePct { get; init; }
    public double? CostUsagePct { get; init; }
    /// <summary>true quando qualquer um dos limites configurados foi cruzado.</summary>
    public required bool Exceeded { get; init; }
}
