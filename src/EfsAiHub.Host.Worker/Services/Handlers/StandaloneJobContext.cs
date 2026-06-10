using EfsAiHub.Core.Agents.Responses;

namespace EfsAiHub.Host.Worker.Services.Handlers;

/// <summary>
/// Implementação default de <see cref="IStandaloneJobContext"/>. Singleton-friendly:
/// guarda referência ao repository + jobId + podId capturados no momento da
/// criação (uma instância por job, criada pelo dispatcher).
/// </summary>
internal sealed class StandaloneJobContext : IStandaloneJobContext
{
    private readonly IBackgroundResponseRepository _jobs;
    private readonly string _jobId;

    public StandaloneJobContext(IBackgroundResponseRepository jobs, string jobId, string podId)
    {
        _jobs = jobs;
        _jobId = jobId;
        PodId = podId;
    }

    public string PodId { get; }

    public BackgroundResponseStatus? LastTerminalStatus { get; private set; }
    public DateTime? LastTerminalAt { get; private set; }

    public async Task<bool> CompleteAsync(string? output, CancellationToken ct)
    {
        var ok = await _jobs.CompleteAsync(_jobId, PodId, output, ct).ConfigureAwait(false);
        if (ok)
        {
            LastTerminalStatus = BackgroundResponseStatus.Completed;
            LastTerminalAt = DateTime.UtcNow;
        }
        return ok;
    }

    public async Task<bool> FailAsync(string lastError, DateTime? nextAttemptAt, bool permanent, CancellationToken ct)
    {
        var ok = await _jobs.FailAsync(_jobId, PodId, lastError, nextAttemptAt, permanent, ct).ConfigureAwait(false);
        if (ok && (permanent || nextAttemptAt is null))
        {
            // Permanent = job vai pra Status='Failed' no SQL. Retry agendado
            // (Status='Queued') NÃO é terminal — não conta como completed na
            // métrica.
            LastTerminalStatus = BackgroundResponseStatus.Failed;
            LastTerminalAt = DateTime.UtcNow;
        }
        return ok;
    }

    public Task<bool> DeferAsync(string reason, DateTime nextAttemptAt, CancellationToken ct)
        // Backpressure: re-enfileira sem terminal. NÃO seta LastTerminalStatus —
        // o job volta pra Queued, não é um outcome final pras métricas.
        => _jobs.DeferAsync(_jobId, PodId, reason, nextAttemptAt, ct);

    public async Task UpdateStepAsync(string? step, CancellationToken ct)
        => await _jobs.UpdateStepAsync(_jobId, PodId, step, ct).ConfigureAwait(false);

    public async Task UpdateIngestionContextAsync(string? ingestionContextJson, CancellationToken ct)
        => await _jobs.UpdateIngestionContextAsync(_jobId, PodId, ingestionContextJson, ct).ConfigureAwait(false);

    public Task SetExecutionIdAsync(string executionId, CancellationToken ct)
        => _jobs.SetExecutionIdAsync(_jobId, executionId, ct);
}
