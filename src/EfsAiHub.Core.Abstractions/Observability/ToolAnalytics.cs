namespace EfsAiHub.Core.Abstractions.Observability;

/// <summary>
/// Resumo de uso de ferramentas (GenericTool) no período. Alimenta a tela
/// "Ferramentas" do dashboard analytics (chamada de ferramenta). Sempre
/// project-scoped — joins com <c>v_production_executions</c> (filtra sandbox)
/// e amarra <c>tool_invocations</c> via ExecutionId.
/// </summary>
public sealed class ToolUsageOverview
{
    public required string ProjectId { get; init; }
    public DateTime PeriodFrom { get; init; }
    public DateTime PeriodTo { get; init; }

    /// <summary>Total de chamadas no período (todas as tools, success + fail).</summary>
    public long TotalCalls { get; init; }

    public long TotalSucceeded { get; init; }
    public long TotalFailed { get; init; }

    /// <summary>Quantidade de ToolNames distintos invocados no período.</summary>
    public int DistinctTools { get; init; }

    /// <summary>Falhas / (sucessos+falhas). 0 quando ainda não houve chamada.</summary>
    public double ErrorRate { get; init; }

    /// <summary>P95 de duração agregado entre todas as chamadas (ms).</summary>
    public double P95DurationMs { get; init; }

    /// <summary>Linhas detalhadas por tool, ordenadas por chamadas DESC.</summary>
    public required IReadOnlyList<ToolUsageRow> Tools { get; init; }
}

/// <summary>
/// Métricas por tool. p50/p95 calculados via <c>percentile_cont</c> em SQL —
/// não amostram, são exatos sobre o conjunto de chamadas do período.
/// </summary>
public sealed class ToolUsageRow
{
    public required string ToolName { get; init; }
    public long Calls { get; init; }
    public long Succeeded { get; init; }
    public long Failed { get; init; }
    /// <summary>Falhas / (sucessos+falhas). 0 quando ainda não houve chamada.</summary>
    public double ErrorRate { get; init; }
    public double AvgDurationMs { get; init; }
    public double P50DurationMs { get; init; }
    public double P95DurationMs { get; init; }
    public double MaxDurationMs { get; init; }
    /// <summary>Quantidade de AgentIds distintos que invocaram essa tool no período.</summary>
    public int DistinctAgents { get; init; }
}

/// <summary>
/// Bucket temporal de uso de tools. Granularidade <c>day</c> ou <c>hour</c> —
/// escolha controlada pelo caller via query param e propagada ao
/// <c>date_trunc</c> da query. Mantém o mesmo shape de
/// <see cref="ProjectTimeseriesBucket"/> pra que o componente de chart reuse
/// o formatter de bucket sem branch específico de tool.
/// </summary>
public sealed class ToolTimeseriesBucket
{
    /// <summary>ISO-8601 da borda inicial do bucket (UTC).</summary>
    public DateTime Bucket { get; init; }
    public long Calls { get; init; }
    public long Succeeded { get; init; }
    public long Failed { get; init; }
    public double AvgDurationMs { get; init; }
    public double P95DurationMs { get; init; }
}
