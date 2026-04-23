namespace EfsAiHub.Core.Abstractions.Observability;

/// <summary>
/// Relatórios agregados de uso/custo do Document Intelligence, lidos direto de
/// <c>aihub.document_extraction_jobs</c>. Usado pelo admin dashboard.
/// </summary>
public interface IDocumentIntelligenceUsageQueries
{
    Task<DocumentIntelligenceUsageSummary> GetSummaryAsync(
        DateTime from, DateTime to, CancellationToken ct = default);

    Task<IReadOnlyList<DocumentIntelligenceUsageByDay>> GetByDayAsync(
        DateTime from, DateTime to, CancellationToken ct = default);

    Task<IReadOnlyList<DocumentIntelligenceUsageByModel>> GetByModelAsync(
        DateTime from, DateTime to, CancellationToken ct = default);

    Task<IReadOnlyList<DocumentIntelligenceJobSummary>> GetRecentJobsAsync(
        DateTime from, DateTime to, int limit, CancellationToken ct = default);
}

public sealed record DocumentIntelligenceUsageSummary(
    long TotalJobs,
    long SucceededJobs,
    long CachedJobs,
    long FailedJobs,
    long TotalPages,
    decimal TotalCostUsd);

public sealed record DocumentIntelligenceUsageByDay(
    DateOnly Day,
    long JobCount,
    long Pages,
    decimal CostUsd);

public sealed record DocumentIntelligenceUsageByModel(
    string Model,
    long JobCount,
    long Pages,
    decimal CostUsd);

public sealed record DocumentIntelligenceJobSummary(
    Guid JobId,
    string ConversationId,
    string UserId,
    string Model,
    string Status,
    int? PageCount,
    decimal? CostUsd,
    int? DurationMs,
    DateTime CreatedAt);
