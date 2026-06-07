namespace EfsAiHub.Core.Abstractions.Observability;

/// <summary>
/// Agregações de <c>aihub.message_feedbacks</c> por projeto. JOIN com
/// <c>chat_messages</c> pra trazer preview da fala avaliada. Sem cache no V1
/// — tabela é pequena (low-rate por convenção do produto: usuário clica
/// thumbs up/down ocasionalmente).
/// </summary>
public interface IFeedbackAnalyticsRepository
{
    Task<FeedbackOverview> GetOverviewAsync(
        string projectId, DateTime from, DateTime to, CancellationToken ct = default);

    Task<IReadOnlyList<FeedbackTimeseriesBucket>> GetTimeseriesAsync(
        string projectId, DateTime from, DateTime to, string groupBy, CancellationToken ct = default);

    /// <summary>
    /// Lista paginada ordenada por CreatedAt DESC.
    /// <paramref name="sentiment"/>: null = todos; +1 ou -1 filtra.
    /// </summary>
    Task<FeedbackRecentResponse> GetRecentAsync(
        string projectId,
        DateTime from,
        DateTime to,
        int? sentiment,
        int page,
        int pageSize,
        CancellationToken ct = default);
}
