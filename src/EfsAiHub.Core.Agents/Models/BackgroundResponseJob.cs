namespace EfsAiHub.Core.Agents.Responses;

/// <summary>
/// Execução assíncrona de um agente, com polling (GET) ou callback HMAC.
/// Reusa a infraestrutura existente (bounded channel + LISTEN/NOTIFY) sem novos brokers.
/// </summary>
public enum BackgroundResponseStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled
}

public sealed class BackgroundResponseJob
{
    public string JobId { get; set; } = Guid.NewGuid().ToString("N");
    public string AgentId { get; set; } = "";
    public string? AgentVersionId { get; set; }
    public string? SessionId { get; set; }
    public string Input { get; set; } = "";
    public BackgroundResponseStatus Status { get; set; } = BackgroundResponseStatus.Queued;
    public string? Output { get; set; }
    public string? LastError { get; set; }
    public int Attempt { get; set; }
    public ResponseCallbackTarget? CallbackTarget { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? IdempotencyKey { get; set; }
}

/// <summary>
/// Webhook de notificação. O <see cref="HmacSecret"/> é usado para assinar o payload
/// (header <c>X-EfsAiHub-Signature: sha256=HEX</c>). Headers adicionais são mesclados.
/// </summary>
public sealed record ResponseCallbackTarget(
    string Url,
    string? HmacSecret = null,
    IReadOnlyDictionary<string, string>? Headers = null);

public interface IBackgroundResponseRepository
{
    Task<BackgroundResponseJob> InsertAsync(BackgroundResponseJob job, CancellationToken ct = default);
    Task<BackgroundResponseJob?> GetAsync(string jobId, CancellationToken ct = default);
    Task<BackgroundResponseJob?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default);
    Task UpdateAsync(BackgroundResponseJob job, CancellationToken ct = default);
    Task<IReadOnlyList<BackgroundResponseJob>> ListPendingAsync(int limit, CancellationToken ct = default);
}

public interface IBackgroundResponseService
{
    Task<BackgroundResponseJob> EnqueueAsync(BackgroundResponseJob job, CancellationToken ct = default);
    Task<BackgroundResponseJob?> GetAsync(string jobId, CancellationToken ct = default);
    Task<bool> CancelAsync(string jobId, CancellationToken ct = default);
}
