namespace EfsAiHub.Core.Abstractions.Observability;

/// <summary>
/// Agregações de uso de ferramentas (GenericTool) por projeto. Fonte primária:
/// <c>aihub.tool_invocations</c> + INNER JOIN com <c>v_production_executions</c>
/// (que já filtra sandbox). Sem matview/cache no V1 — tabela é pequena (~80
/// linhas em dev), p95 calculado inline via <c>percentile_cont</c> sem custo
/// perceptível. Quando o volume escalar, cachear no Redis com mesma estratégia
/// de <see cref="IProjectAnalyticsRepository"/>.
///
/// Authorization fica no controller — repo só recebe ProjectId já validado e
/// parametriza tudo em SQL (defesa em profundidade).
/// </summary>
public interface IToolAnalyticsRepository
{
    /// <summary>
    /// Resumo do período + linhas por tool. Tools com 0 chamadas no período
    /// não aparecem (catálogo de tools cadastradas vs. usadas é tela
    /// diferente — <c>/ferramentas</c> do MVP).
    /// </summary>
    Task<ToolUsageOverview> GetToolUsageOverviewAsync(
        string projectId,
        DateTime from,
        DateTime to,
        CancellationToken ct = default);

    /// <summary>
    /// Série temporal agregada de chamadas + sucessos + falhas + latência.
    /// <paramref name="groupBy"/> aceita <c>"day"</c> ou <c>"hour"</c>; outros
    /// valores caem em <c>"day"</c> via fallback no repo (defensivo contra
    /// query param malicioso).
    /// </summary>
    Task<IReadOnlyList<ToolTimeseriesBucket>> GetToolTimeseriesAsync(
        string projectId,
        DateTime from,
        DateTime to,
        string groupBy,
        CancellationToken ct = default);
}
