using System.Text.Json;
using EfsAiHub.Core.Orchestration.Workflows;
using EfsAiHub.Infra.Persistence.Postgres;

namespace EfsAiHub.Host.Worker.Services;

/// <summary>
/// Executa uma única vez no startup:
/// 1. Marca execuções órfãs (Running/Pending interrompidas por restart) como Failed
/// 2. Recarrega interações HITL Pending do banco
///
/// Nota: DDL (criação de tabelas e materialized views) é responsabilidade do DBA
/// via db/schema.sql e db/views.sql — não é executado pela aplicação.
/// </summary>
public sealed class DatabaseBootstrapService : IHostedService
{
    private readonly IWorkflowExecutionRepository _executionRepo;
    private readonly IWorkflowEventRepository _eventRepo;
    private readonly IHumanInteractionService _hitlService;
    private readonly IHumanInteractionRepository _hitlRepo;
    private readonly ILogger<DatabaseBootstrapService> _logger;

    public DatabaseBootstrapService(
        IWorkflowExecutionRepository executionRepo,
        IWorkflowEventRepository eventRepo,
        IHumanInteractionService hitlService,
        IHumanInteractionRepository hitlRepo,
        ILogger<DatabaseBootstrapService> logger)
    {
        _executionRepo = executionRepo;
        _eventRepo = eventRepo;
        _hitlService = hitlService;
        _hitlRepo = hitlRepo;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        await CleanupOrphansAsync(ct);
        await ExpireOrphanedHitlsAsync(ct);
        await _hitlService.LoadPendingFromDbAsync();
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private async Task ExpireOrphanedHitlsAsync(CancellationToken ct)
    {
        try
        {
            await _hitlRepo.ExpireOrphanedAsync(ct);
            _logger.LogInformation("[Bootstrap] HITLs órfãos (execuções em estado terminal) expirados.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Bootstrap] Falha ao expirar HITLs órfãos. Continuando inicialização.");
        }
    }

    private async Task CleanupOrphansAsync(CancellationToken ct)
    {
        try
        {
            var actives = await _executionRepo.GetActiveExecutionsAsync();
            if (actives.Count == 0) return;

            foreach (var exec in actives)
            {
                // Paused: NÃO marcar como Failed. A execução parou em HITL e tem
                // checkpoint persistido — o HitlRecoveryService tenta retomá-la.
                if (exec.Status == WorkflowStatus.Paused)
                {
                    _logger.LogInformation(
                        "[Bootstrap] Execução '{ExecutionId}' em Paused — preservada para recovery HITL.",
                        exec.ExecutionId);
                    continue;
                }

                // Running / Pending: interrompidas por restart. Marcar como Failed.
                exec.Status = WorkflowStatus.Failed;
                exec.ErrorCategory = ErrorCategory.FrameworkError;
                exec.ErrorMessage = "Execução interrompida por restart do processo.";
                exec.CompletedAt = DateTime.UtcNow;
                await _executionRepo.UpdateAsync(exec, ct);

                await _eventRepo.AppendAsync(new WorkflowEventEnvelope
                {
                    EventType = "error",
                    ExecutionId = exec.ExecutionId,
                    Payload = JsonSerializer.Serialize(new
                    {
                        error = "Execução interrompida por restart do processo.",
                        category = nameof(ErrorCategory.FrameworkError)
                    })
                });

                _logger.LogWarning(
                    "[Bootstrap] Execução '{ExecutionId}' (workflow '{WorkflowId}') marcada como Failed.",
                    exec.ExecutionId, exec.WorkflowId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Bootstrap] Falha no cleanup de execuções órfãs. Continuando inicialização.");
        }
    }
}
