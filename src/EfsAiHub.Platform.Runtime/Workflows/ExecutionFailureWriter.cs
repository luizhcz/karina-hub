using System.Text.Json;
using EfsAiHub.Core.Abstractions.Conversations;
using EfsAiHub.Core.Abstractions.Execution;
using EfsAiHub.Core.Abstractions.Persistence;

namespace EfsAiHub.Platform.Runtime.BackgroundServices;

/// <summary>
/// Encapsula as transições de estado terminal de uma execução:
/// Failed, Cancelled e Completed.
/// Elimina os 5 blocos repetidos de Status→UpdateAsync→PublishEvent em WorkflowRunnerService.
/// Registrado como Scoped — um por execução.
/// </summary>
public sealed class ExecutionFailureWriter
{
    private readonly IWorkflowExecutionRepository _executionRepo;
    private readonly IWorkflowEventBus _eventBus;
    private readonly TokenBatcher _tokenBatcher;
    private readonly IEnumerable<IExecutionLifecycleObserver> _observers;
    private readonly IHumanInteractionService _hitlService;
    private readonly ILogger<ExecutionFailureWriter> _logger;

    public ExecutionFailureWriter(
        IWorkflowExecutionRepository executionRepo,
        IWorkflowEventBus eventBus,
        TokenBatcher tokenBatcher,
        IEnumerable<IExecutionLifecycleObserver> observers,
        IHumanInteractionService hitlService,
        ILogger<ExecutionFailureWriter> logger)
    {
        _executionRepo = executionRepo;
        _eventBus = eventBus;
        _tokenBatcher = tokenBatcher;
        _observers = observers;
        _hitlService = hitlService;
        _logger = logger;
    }

    public async Task MarkFailedAsync(
        WorkflowExecution execution,
        string reason,
        ErrorCategory category = ErrorCategory.Unknown)
    {
        execution.Status = WorkflowStatus.Failed;
        execution.ErrorCategory = category;
        execution.ErrorMessage = reason;
        execution.CompletedAt = DateTime.UtcNow;
        await _executionRepo.UpdateAsync(execution, CancellationToken.None);
        await _hitlService.ExpireForExecutionAsync(execution.ExecutionId);
        await PublishAsync(execution.ExecutionId, "error", new { message = reason });
        await NotifyFailedAsync(execution);
    }

    public async Task MarkCancelledAsync(WorkflowExecution execution, bool isTimeout)
    {
        execution.Status = WorkflowStatus.Cancelled;
        execution.ErrorCategory = isTimeout ? ErrorCategory.Timeout : ErrorCategory.Cancelled;
        execution.CompletedAt = DateTime.UtcNow;
        await _executionRepo.UpdateAsync(execution, CancellationToken.None);
        await _hitlService.ExpireForExecutionAsync(execution.ExecutionId);
        await PublishAsync(execution.ExecutionId, "error", new { message = "Execução cancelada.", code = isTimeout ? "TIMEOUT" : "CANCELLED" });
        await NotifyFailedAsync(execution);
    }

    public async Task MarkCompletedAsync(
        WorkflowExecution execution,
        string output,
        CancellationToken ct)
    {
        execution.Status = WorkflowStatus.Completed;
        execution.Output = output;
        execution.CompletedAt = DateTime.UtcNow;
        await _executionRepo.UpdateAsync(execution, CancellationToken.None);
        // Salvar mensagem no BD ANTES de publicar o evento para garantir que o frontend
        // encontre a mensagem ao chamar refetchMessages() após receber RUN_FINISHED.
        await NotifyCompletedAsync(execution, output, ct);
        await PublishAsync(execution.ExecutionId, "workflow_completed", new { output });
    }

    private async Task PublishAsync(string executionId, string eventType, object payload)
    {
        await _tokenBatcher.FlushAsync(executionId);
        var envelope = new WorkflowEventEnvelope
        {
            EventType = eventType,
            ExecutionId = executionId,
            Payload = JsonSerializer.Serialize(payload, JsonDefaults.Domain)
        };
        await _eventBus.PublishAsync(executionId, envelope);
    }

    private async Task NotifyFailedAsync(WorkflowExecution execution)
    {
        if (!execution.Metadata.TryGetValue("conversationId", out var conversationId))
            return;
        foreach (var observer in _observers)
        {
            try
            {
                await observer.OnExecutionFailedAsync(conversationId, execution.ExecutionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Observer '{Observer}' falhou em OnExecutionFailedAsync para conversa '{ConversationId}'.",
                    observer.GetType().Name, conversationId);
            }
        }
    }

    private async Task NotifyCompletedAsync(WorkflowExecution execution, string output, CancellationToken ct)
    {
        if (!execution.Metadata.TryGetValue("conversationId", out var conversationId))
            return;
        execution.Metadata.TryGetValue("lastActiveAgentId", out var lastActiveAgentId);
        foreach (var observer in _observers)
        {
            try
            {
                await observer.OnExecutionCompletedAsync(
                    conversationId, output, execution.ExecutionId, lastActiveAgentId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Observer '{Observer}' falhou em OnExecutionCompletedAsync para conversa '{ConversationId}'.",
                    observer.GetType().Name, conversationId);
            }
        }
    }
}
