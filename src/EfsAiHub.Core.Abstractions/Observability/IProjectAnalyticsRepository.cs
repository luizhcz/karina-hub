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
    /// <summary>
    /// <paramref name="ownedOnly"/>: quando true, restringe as métricas LLM
    /// (cost/tokens/calls) e <c>topAgents</c> a agentes cujo <c>ProjectId</c>
    /// é o do request (ignora Visibility=global de outros projetos do tenant
    /// que rodaram aqui — ex.: assistente-perfil, gerador-testcases). Métricas
    /// de <c>workflow_executions</c> (executions/completed/failed) NÃO são
    /// afetadas — granularidade é por workflow, não por agent.
    /// </summary>
    Task<ProjectOverview> GetProjectOverviewAsync(
        string projectId, DateTime from, DateTime to, bool ownedOnly = false, CancellationToken ct = default);

    /// <summary>
    /// <paramref name="excludeAgentIds"/>: quando informado, filtra as métricas
    /// LLM (cost/tokens/calls) para ignorar consumo dos agentes listados.
    /// <paramref name="ownedOnly"/>: quando true, mantém só métricas LLM de
    /// agentes pertencentes ao projeto. Pode ser combinado com
    /// <paramref name="excludeAgentIds"/>. Métricas <c>workflow_executions</c>
    /// continuam intactas (granularidade workflow ≠ agent).
    /// </summary>
    Task<IReadOnlyList<ProjectTimeseriesBucket>> GetProjectTimeseriesAsync(
        string projectId,
        DateTime from,
        DateTime to,
        string groupBy,
        IReadOnlyCollection<string>? excludeAgentIds = null,
        bool ownedOnly = false,
        CancellationToken ct = default);

    /// <summary>
    /// <paramref name="ownedOnly"/>: quando true, retorna só agentes cujo
    /// <c>ProjectId</c> é o do request (descarta Visibility=global de outros
    /// projetos do mesmo tenant).
    /// </summary>
    Task<IReadOnlyList<ProjectAgentBreakdown>> GetProjectAgentBreakdownAsync(
        string projectId, DateTime from, DateTime to, int top, bool ownedOnly = false, CancellationToken ct = default);

    Task<ProjectBudgetStatus> GetProjectBudgetStatusAsync(
        string projectId, int? maxTokensPerDay, decimal? maxCostUsdPerDay, CancellationToken ct = default);
}
