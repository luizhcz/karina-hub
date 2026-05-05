namespace EfsAiHub.Core.Abstractions.Observability;

/// <summary>
/// Agregações de uso/custo escopadas por projeto. Fonte primária:
/// <c>aihub.v_llm_cost</c> (matview com EstimatedCostUsd row-level via
/// LATERAL JOIN com model_pricing) + <c>workflow_executions</c> pra
/// status/contagem. Refresh da matview a cada 30min via LlmCostRefreshService
/// — dashboard mostra dado defasado em até esse intervalo.
///
/// Authorization é responsabilidade do controller — o repo recebe ProjectId
/// validado e parametriza em SQL (defesa em profundidade).
/// </summary>
public interface IProjectAnalyticsRepository
{
    Task<ProjectOverview> GetProjectOverviewAsync(
        string projectId, DateTime from, DateTime to, CancellationToken ct = default);

    /// <summary>
    /// <paramref name="excludeAgentIds"/>: quando informado, filtra as métricas
    /// LLM (cost/tokens/calls) para ignorar consumo dos agentes listados —
    /// frontend usa isso pra esconder consumo de agentes internos da plataforma
    /// (ex.: assistente-perfil, gerador-testcases). As métricas baseadas em
    /// <c>workflow_executions</c> (executions/completed/failed) não são afetadas
    /// porque a granularidade é por workflow, não por agent.
    /// </summary>
    Task<IReadOnlyList<ProjectTimeseriesBucket>> GetProjectTimeseriesAsync(
        string projectId,
        DateTime from,
        DateTime to,
        string groupBy,
        IReadOnlyCollection<string>? excludeAgentIds = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<ProjectAgentBreakdown>> GetProjectAgentBreakdownAsync(
        string projectId, DateTime from, DateTime to, int top, CancellationToken ct = default);

    Task<ProjectBudgetStatus> GetProjectBudgetStatusAsync(
        string projectId, int? maxTokensPerDay, decimal? maxCostUsdPerDay, CancellationToken ct = default);
}
