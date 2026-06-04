using EfsAiHub.Core.Abstractions.Execution;
using EfsAiHub.Core.Agents.Responses;
using EfsAiHub.Core.Orchestration.Workflows;
using EfsAiHub.Platform.Runtime.Configuration;
using EfsAiHub.Platform.Runtime.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Host.Worker.Services.Handlers;

/// <summary>
/// Handler default — dispara workflow direto via <see cref="IWorkflowDispatcher"/>
/// e polla <c>workflow_executions</c> até terminal. Cobre o caminho "POST
/// /api/aihub/responses com workflowId" (sem ingestão de arquivo). É o último
/// na cadeia de <see cref="IStandaloneJobHandler"/> — aceita qualquer job que
/// nenhum outro handler quis (<see cref="CanHandle"/> retorna <c>true</c>
/// quando o job não tem IngestionContext nem Step).
/// </summary>
public sealed class WorkflowStandaloneJobHandler : IStandaloneJobHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly StandalonePoolsOptions _options;
    private readonly ILogger<WorkflowStandaloneJobHandler> _logger;

    public WorkflowStandaloneJobHandler(
        IServiceScopeFactory scopeFactory,
        IOptions<StandalonePoolsOptions> options,
        ILogger<WorkflowStandaloneJobHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Default handler — aceita qualquer job EXCETO jobs que carregam
    /// IngestionContext não-vazio sem Input válido. Esse cenário acontece
    /// quando IngestionContext está malformado/corrompido: o IngestionJobHandler
    /// rejeitou no CanHandle, e o caller não montou Input pro workflow direto.
    /// Cair aqui dispararia o workflow com payload vazio — preferimos falhar
    /// rapidamente com mensagem clara em vez de mascarar bug.
    /// </summary>
    public bool CanHandle(BackgroundResponseJob job)
    {
        if (!string.IsNullOrWhiteSpace(job.IngestionContext)
            && string.IsNullOrWhiteSpace(job.Input))
        {
            return false;
        }
        return true;
    }

    public async Task ProcessAsync(BackgroundResponseJob job, IStandaloneJobContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(job.WorkflowId))
        {
            await ctx.FailAsync("WorkflowId obrigatório no job standalone.", null, permanent: true, ct).ConfigureAwait(false);
            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IWorkflowDispatcher>();
        var executionRepo = scope.ServiceProvider.GetRequiredService<IWorkflowExecutionRepository>();

        var metadata = new Dictionary<string, string>
        {
            ["standaloneJobId"] = job.JobId
        };

        var executionId = await dispatcher.TriggerAsync(
            job.WorkflowId,
            job.Input,
            metadata,
            source: ExecutionSource.Api,
            mode: ExecutionMode.Production,
            workflowVersionId: job.AgentVersionId,
            ct: ct).ConfigureAwait(false);

        await ctx.SetExecutionIdAsync(executionId, ct).ConfigureAwait(false);
        await PollUntilTerminalAsync(job, executionId, executionRepo, ctx, ct).ConfigureAwait(false);
    }

    private async Task PollUntilTerminalAsync(
        BackgroundResponseJob job,
        string executionId,
        IWorkflowExecutionRepository executionRepo,
        IStandaloneJobContext ctx,
        CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddMinutes(_options.JobMaxLifetimeMinutes);
        var pollDelay = TimeSpan.FromSeconds(2);

        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            await Task.Delay(pollDelay, ct).ConfigureAwait(false);
            var execution = await executionRepo.GetByIdAsync(executionId, ct).ConfigureAwait(false);
            if (execution is null) continue;

            switch (execution.Status)
            {
                case WorkflowStatus.Completed:
                    await ctx.CompleteAsync(execution.Output, ct).ConfigureAwait(false);
                    return;

                case WorkflowStatus.Failed:
                case WorkflowStatus.Cancelled:
                    var cancelled = execution.Status == WorkflowStatus.Cancelled;
                    var permanent = cancelled || job.Attempt >= _options.MaxAttempts;
                    DateTime? next = permanent ? null : DateTime.UtcNow.AddSeconds(CalcBackoffSeconds(job.Attempt));
                    var errMsg = execution.ErrorMessage ?? (cancelled ? "Execution cancelled" : "Execution failed");
                    await ctx.FailAsync(errMsg, next, permanent, ct).ConfigureAwait(false);
                    return;

                // Paused (HITL), Pending, Running — segue pollando.
            }
        }

        if (!ct.IsCancellationRequested)
        {
            await ctx.FailAsync(
                $"Job excedeu JobMaxLifetimeMinutes={_options.JobMaxLifetimeMinutes}m",
                null,
                permanent: true,
                ct).ConfigureAwait(false);
        }
    }

    private int CalcBackoffSeconds(int attempt)
    {
        var baseSecs = Math.Max(1, _options.RetryBackoffBaseSeconds);
        var multiplier = Math.Pow(2, Math.Min(attempt, 10));
        return (int)(baseSecs * multiplier);
    }
}
