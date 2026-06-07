namespace EfsAiHub.Core.Abstractions.Observability;

/// <summary>
/// Agregações de decisões de Router (extraídas do payload de
/// <c>workflow_event_audit</c>) por projeto. Production-only via JOIN com
/// <c>v_production_executions</c>.
/// </summary>
public interface IRouterDecisionAnalyticsRepository
{
    Task<RouterDecisionOverview> GetOverviewAsync(
        string projectId, DateTime from, DateTime to, CancellationToken ct = default);

    Task<IReadOnlyList<RouterDecisionTimeseriesBucket>> GetTimeseriesAsync(
        string projectId, DateTime from, DateTime to, string groupBy, CancellationToken ct = default);
}
