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

    public Task<bool> CompleteAsync(string? output, CancellationToken ct)
        => _jobs.CompleteAsync(_jobId, PodId, output, ct);

    public Task<bool> FailAsync(string lastError, DateTime? nextAttemptAt, bool permanent, CancellationToken ct)
        => _jobs.FailAsync(_jobId, PodId, lastError, nextAttemptAt, permanent, ct);

    public async Task UpdateStepAsync(string? step, CancellationToken ct)
        => await _jobs.UpdateStepAsync(_jobId, PodId, step, ct).ConfigureAwait(false);

    public async Task UpdateIngestionContextAsync(string? ingestionContextJson, CancellationToken ct)
        => await _jobs.UpdateIngestionContextAsync(_jobId, PodId, ingestionContextJson, ct).ConfigureAwait(false);

    public Task SetExecutionIdAsync(string executionId, CancellationToken ct)
        => _jobs.SetExecutionIdAsync(_jobId, executionId, ct);
}
