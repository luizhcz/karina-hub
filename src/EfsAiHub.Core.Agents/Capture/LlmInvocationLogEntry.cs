using EfsAiHub.Core.Agents.Composition;

namespace EfsAiHub.Core.Agents.Capture;

/// <summary>
/// Row persistida em <c>aihub.llm_invocation_log</c>. Granularidade:
/// 1 row por <c>(TurnId, AttemptIndex)</c>. Retry/fallback geram rows
/// distintas com mesmo TurnId.
/// </summary>
public sealed record LlmInvocationLogEntry(
    long? Id,
    Guid TurnId,
    short AttemptIndex,
    string? ExecutionId,
    string? WorkflowId,
    string AgentId,
    string? AgentVersionId,
    int? StepIndex,
    string? ProjectId,
    string Provider,
    string ProviderResolved,
    string Model,
    string? Intent,
    /// <summary>JSON serializado das mensagens + chat options (cap 256KB).</summary>
    string RequestPayload,
    /// <summary>JSON serializado da resposta + token usage (cap 256KB).</summary>
    string ResponsePayload,
    /// <summary>Snapshot do <see cref="PromptComposition"/> — null quando captura
    /// rodou mas nenhum contribuidor anotou seções.</summary>
    IReadOnlyList<PromptSection>? Composition,
    /// <summary>Snapshot leve de ChatOptions (Temperature/MaxTokens/ResponseFormat/Tools).
    /// JSON serializado.</summary>
    string? ChatOptionsSnapshot,
    string Status,
    string? ErrorMessage,
    double DurationMs,
    int InputTokens,
    int OutputTokens,
    int CachedTokens,
    int RequestSizeBytes,
    int ResponseSizeBytes,
    bool Truncated,
    DateTime CreatedAt);

public interface ILlmInvocationLogRepository
{
    Task InsertBatchAsync(IReadOnlyList<LlmInvocationLogEntry> entries, CancellationToken ct = default);
    Task<LlmInvocationLogEntry?> GetByIdAsync(long id, DateTime createdAt, CancellationToken ct = default);

    /// <summary>Sobrecarga que não exige <c>CreatedAt</c> — útil pra
    /// endpoints REST onde o caller só tem o Id. Postgres escaneia todas as
    /// partições (low-frequency path; aceitável).</summary>
    Task<LlmInvocationLogEntry?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<LlmInvocationLogEntry>> ListAsync(LlmInvocationLogQuery query, CancellationToken ct = default);
    Task<int> CountAsync(LlmInvocationLogQuery query, CancellationToken ct = default);

    /// <summary>Retorna os últimos N turns do mesmo agente+execution antes
    /// de <paramref name="beforeCreatedAt"/>. Usado pelo endpoint de diff
    /// (popover "selecione turno anterior pra comparar").</summary>
    Task<IReadOnlyList<LlmInvocationLogEntry>> RecentForDiffAsync(
        string agentId,
        string? executionId,
        DateTime beforeCreatedAt,
        int limit,
        CancellationToken ct = default);

    /// <summary>Drop de partições mensais mais antigas que retentionDays.
    /// Retorna nomes das partições dropadas (pra log).</summary>
    Task<IReadOnlyList<string>> DropOldPartitionsAsync(int retentionDays, CancellationToken ct = default);

    /// <summary>Garante existência da partição do mês N à frente. Idempotente.</summary>
    Task EnsureFuturePartitionAsync(int monthsAhead, CancellationToken ct = default);
}

public sealed record LlmInvocationLogQuery(
    string? AgentId = null,
    string? ProjectId = null,
    string? Intent = null,
    string? Status = null,
    string? ExecutionId = null,
    DateTime? From = null,
    DateTime? To = null,
    double? MinDurationMs = null,
    int Page = 1,
    int Size = 50);
