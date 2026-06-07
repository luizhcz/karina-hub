namespace EfsAiHub.Core.Abstractions.Observability;

/// <summary>
/// Agregações da fila standalone por projeto. Fonte:
/// <c>aihub.background_response_jobs</c>. Sem cache no V1 — tabela é small
/// (tens of thousands de rows em prod típico) e índices em (Status, CreatedAt)
/// e (ProjectId, CreatedAt) cobrem os predicates. Cachear no Redis com mesma
/// estratégia do <see cref="IProjectAnalyticsRepository"/> quando volume
/// escalar.
/// </summary>
public interface IStandaloneJobAnalyticsRepository
{
    Task<StandaloneJobOverview> GetOverviewAsync(
        string projectId, DateTime from, DateTime to, CancellationToken ct = default);

    Task<IReadOnlyList<StandaloneJobTimeseriesBucket>> GetTimeseriesAsync(
        string projectId, DateTime from, DateTime to, string groupBy, CancellationToken ct = default);
}
