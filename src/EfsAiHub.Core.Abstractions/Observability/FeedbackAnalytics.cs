namespace EfsAiHub.Core.Abstractions.Observability;

/// <summary>
/// Agregados de feedback do usuário (<c>aihub.message_feedbacks</c>) por
/// projeto. Sentiment é binário ±1 (constraint do schema): +1 = like,
/// -1 = dislike. Comment opcional.
/// </summary>
public sealed class FeedbackOverview
{
    public required string ProjectId { get; init; }
    public DateTime PeriodFrom { get; init; }
    public DateTime PeriodTo { get; init; }

    public long Total { get; init; }
    public long Positives { get; init; }
    public long Negatives { get; init; }
    /// <summary>Positives / (Positives + Negatives). 0 quando ainda não houve feedback.</summary>
    public double SatisfactionRate { get; init; }

    public int DistinctMessages { get; init; }
    public int DistinctUsers { get; init; }
    public long WithCommentCount { get; init; }

    /// <summary>Top 10 mensagens com mais feedback no período (preview da fala incluso).</summary>
    public required IReadOnlyList<FeedbackTopMessageRow> TopMessages { get; init; }
}

public sealed class FeedbackTopMessageRow
{
    public required string MessageId { get; init; }
    public long FeedbackCount { get; init; }
    public long Positives { get; init; }
    public long Negatives { get; init; }
    /// <summary>Primeiros ~200 chars do <c>chat_messages.Content</c>. Null se mensagem foi deletada.</summary>
    public string? MessagePreview { get; init; }
    public string? ConversationId { get; init; }
}

public sealed class FeedbackTimeseriesBucket
{
    public DateTime Bucket { get; init; }
    public long Count { get; init; }
    public long Positives { get; init; }
    public long Negatives { get; init; }
}

/// <summary>
/// Listagem paginada de feedbacks recentes. Inclui preview da mensagem
/// avaliada (JOIN com <c>chat_messages</c>) — é o que faz a tela útil pra
/// análise qualitativa ("o que levou thumbs down?").
/// </summary>
public sealed class FeedbackRecentResponse
{
    public required IReadOnlyList<FeedbackRecentRow> Items { get; init; }
    public long Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}

public sealed class FeedbackRecentRow
{
    public required string FeedbackId { get; init; }
    public required string MessageId { get; init; }
    public string? ConversationId { get; init; }
    public string? ExecutionId { get; init; }
    /// <summary>-1 (dislike) ou +1 (like). Schema garante via CHECK constraint.</summary>
    public int Sentiment { get; init; }
    public string? Comment { get; init; }
    public string? UserId { get; init; }
    /// <summary>Primeiros ~300 chars do <c>chat_messages.Content</c>. Null se mensagem foi deletada.</summary>
    public string? MessagePreview { get; init; }
    public DateTime CreatedAt { get; init; }
}
